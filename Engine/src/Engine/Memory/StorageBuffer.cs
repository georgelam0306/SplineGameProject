using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using DerpLib.Core;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace DerpLib.Memory;

/// <summary>
/// A typed storage buffer (SSBO) for shader access.
/// Optionally supports CPU readback (blocking or async).
/// </summary>
public struct StorageBuffer<T> where T : unmanaged
{
    internal readonly BufferAllocation Allocation;
    private readonly MemoryAllocator _allocator;
    private readonly ReadbackState? _readback;

    /// <summary>Maximum number of elements this buffer can hold.</summary>
    public readonly int Capacity;

    /// <summary>Size of the buffer in bytes.</summary>
    public ulong Size => Allocation.Size;

    /// <summary>The underlying Vulkan buffer.</summary>
    internal VkBuffer Buffer => Allocation.Buffer;

    /// <summary>Whether this buffer supports CPU readback.</summary>
    public bool IsReadable => _readback != null;

    internal StorageBuffer(BufferAllocation allocation, MemoryAllocator allocator, int capacity, ReadbackState? readback = null)
    {
        Allocation = allocation;
        _allocator = allocator;
        Capacity = capacity;
        _readback = readback;
    }

    /// <summary>
    /// Upload data to the buffer from the beginning.
    /// </summary>
    public void Upload(ReadOnlySpan<T> data)
    {
        Upload(data, 0);
    }

    /// <summary>
    /// Upload data to the buffer at an element offset.
    /// </summary>
    public void Upload(ReadOnlySpan<T> data, int elementOffset)
    {
        if (elementOffset + data.Length > Capacity)
            throw new ArgumentException($"Data exceeds buffer capacity: {elementOffset + data.Length} > {Capacity}");

        var byteOffset = (ulong)(elementOffset * Marshal.SizeOf<T>());
        var bytes = MemoryMarshal.AsBytes(data);
        _allocator.UploadData(Allocation, bytes, byteOffset);
    }

    #region Blocking Readback

    /// <summary>
    /// Map the buffer for reading. Returns a span over the buffer data.
    /// For unified memory: direct access to GPU memory.
    /// For discrete GPU: copies to staging buffer and waits.
    /// Call Unmap() when done reading.
    /// </summary>
    public unsafe ReadOnlySpan<T> Map()
    {
        if (_readback == null)
            throw new InvalidOperationException("Buffer was not created with readable: true");

        // For unified memory, the primary buffer is HOST_VISIBLE - map directly
        if (Allocation.IsHostVisible)
        {
            var ptr = _allocator.MapMemory(Allocation);
            _readback.MappedPrimary = true;
            return new ReadOnlySpan<T>(ptr, Capacity);
        }

        // For discrete GPU, copy to staging and wait
        _readback.CopyToStagingAndWait(Allocation, 0);
        var stagingPtr = _allocator.MapMemory(_readback.Staging0);
        _readback.MappedStaging = 0;
        return new ReadOnlySpan<T>(stagingPtr, Capacity);
    }

    /// <summary>
    /// Unmap the buffer after reading.
    /// </summary>
    public void Unmap()
    {
        if (_readback == null)
            throw new InvalidOperationException("Buffer was not created with readable: true");

        if (_readback.MappedPrimary)
        {
            _allocator.UnmapMemory(Allocation);
            _readback.MappedPrimary = false;
        }
        else if (_readback.MappedStaging >= 0)
        {
            var staging = _readback.MappedStaging == 0 ? _readback.Staging0 : _readback.Staging1;
            _allocator.UnmapMemory(staging);
            _readback.MappedStaging = -1;
        }
    }

    #endregion

    #region Async Readback

    /// <summary>
    /// Request an async readback. The data will be available after 2 frames.
    /// Call this once per frame after compute/render that writes to the buffer.
    /// </summary>
    public void RequestReadback()
    {
        if (_readback == null)
            throw new InvalidOperationException("Buffer was not created with readable: true");

        _readback.RequestAsyncReadback(Allocation);
    }

    /// <summary>
    /// Whether async readback data is available (from 2 frames ago).
    /// </summary>
    public bool HasReadbackData
    {
        get
        {
            if (_readback == null) return false;
            return _readback.HasAsyncData;
        }
    }

    /// <summary>
    /// Get the async readback data (from 2 frames ago).
    /// Only valid when HasReadbackData is true.
    /// </summary>
    public unsafe ReadOnlySpan<T> ReadbackData
    {
        get
        {
            if (_readback == null)
                throw new InvalidOperationException("Buffer was not created with readable: true");

            if (!_readback.HasAsyncData)
                throw new InvalidOperationException("No readback data available. Call RequestReadback() and wait 2 frames.");

            var ptr = _readback.GetAsyncData(_allocator);
            return new ReadOnlySpan<T>(ptr, Capacity);
        }
    }

    #endregion
}

/// <summary>
/// Internal state for readable storage buffers.
/// Manages staging buffers and fences for readback.
/// </summary>
internal sealed class ReadbackState : IDisposable
{
    private readonly VkDevice _device;
    private readonly MemoryAllocator _allocator;
    private readonly ulong _size;

    // Staging buffers (pre-allocated, HOST_VISIBLE)
    public BufferAllocation Staging0 { get; }
    public BufferAllocation Staging1 { get; }

    // Fences for async readback
    private readonly Fence _fence0;
    private readonly Fence _fence1;

    // Tracking state
    internal bool MappedPrimary;
    internal int MappedStaging = -1;
    private int _asyncWriteIndex;  // Which staging we're writing to
    private int _asyncReadIndex = -1;  // Which staging has valid data (-1 = none)
    private int _frameCount;  // Frames since first RequestReadback

    // Command pool for copy operations
    private readonly CommandPool _commandPool;

    private Vk Vk => _device.Vk;
    private Device Device => _device.Device;

    public unsafe ReadbackState(VkDevice device, MemoryAllocator allocator, ulong size)
    {
        _device = device;
        _allocator = allocator;
        _size = size;

        // Create staging buffers
        Staging0 = allocator.CreateStagingBuffer(size);
        Staging1 = allocator.CreateStagingBuffer(size);

        // Create fences (unsignaled)
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = 0  // Start unsignaled
        };

        Vk.CreateFence(Device, &fenceInfo, null, out _fence0);
        Vk.CreateFence(Device, &fenceInfo, null, out _fence1);

        // Create command pool for copy operations
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = device.GraphicsQueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        Vk.CreateCommandPool(Device, &poolInfo, null, out _commandPool);
    }

    /// <summary>
    /// Copy buffer to staging and wait for completion (blocking).
    /// </summary>
    public unsafe void CopyToStagingAndWait(BufferAllocation source, int stagingIndex)
    {
        var staging = stagingIndex == 0 ? Staging0 : Staging1;

        // Allocate command buffer
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        CommandBuffer cmd;
        Vk.AllocateCommandBuffers(Device, &allocInfo, &cmd);

        // Begin recording
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        Vk.BeginCommandBuffer(cmd, &beginInfo);

        // Copy command
        var copyRegion = new BufferCopy
        {
            SrcOffset = 0,
            DstOffset = 0,
            Size = _size
        };

        Vk.CmdCopyBuffer(cmd, source.Buffer, staging.Buffer, 1, &copyRegion);

        Vk.EndCommandBuffer(cmd);

        // Submit and wait
        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd
        };

        Vk.QueueSubmit(_device.GraphicsQueue, 1, &submitInfo, default);
        Vk.QueueWaitIdle(_device.GraphicsQueue);

        // Free command buffer
        Vk.FreeCommandBuffers(Device, _commandPool, 1, &cmd);
    }

    /// <summary>
    /// Request async readback (non-blocking copy to staging).
    /// </summary>
    public unsafe void RequestAsyncReadback(BufferAllocation source)
    {
        var staging = _asyncWriteIndex == 0 ? Staging0 : Staging1;
        var fence = _asyncWriteIndex == 0 ? _fence0 : _fence1;

        // Wait for previous use of this staging buffer (if any)
        if (_frameCount >= 2)
        {
            Vk.WaitForFences(Device, 1, &fence, true, ulong.MaxValue);
        }
        Vk.ResetFences(Device, 1, &fence);

        // Allocate command buffer
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        CommandBuffer cmd;
        Vk.AllocateCommandBuffers(Device, &allocInfo, &cmd);

        // Begin recording
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        Vk.BeginCommandBuffer(cmd, &beginInfo);

        // Copy command
        var copyRegion = new BufferCopy
        {
            SrcOffset = 0,
            DstOffset = 0,
            Size = _size
        };

        Vk.CmdCopyBuffer(cmd, source.Buffer, staging.Buffer, 1, &copyRegion);

        Vk.EndCommandBuffer(cmd);

        // Submit with fence
        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd
        };

        Vk.QueueSubmit(_device.GraphicsQueue, 1, &submitInfo, fence);

        // Free command buffer (will complete after fence)
        Vk.FreeCommandBuffers(Device, _commandPool, 1, &cmd);

        // Update state
        _asyncReadIndex = _asyncWriteIndex;
        _asyncWriteIndex = 1 - _asyncWriteIndex;  // Ping-pong
        _frameCount++;
    }

    /// <summary>
    /// Whether async data is available.
    /// </summary>
    public unsafe bool HasAsyncData
    {
        get
        {
            if (_asyncReadIndex < 0 || _frameCount < 2) return false;

            // Check if fence is signaled
            var fence = _asyncReadIndex == 0 ? _fence0 : _fence1;
            var status = Vk.GetFenceStatus(Device, fence);
            return status == Result.Success;
        }
    }

    /// <summary>
    /// Get pointer to async data (must check HasAsyncData first).
    /// </summary>
    public unsafe void* GetAsyncData(MemoryAllocator allocator)
    {
        var staging = _asyncReadIndex == 0 ? Staging0 : Staging1;
        return allocator.MapMemory(staging);
    }

    public unsafe void Dispose()
    {
        Vk.DestroyFence(Device, _fence0, null);
        Vk.DestroyFence(Device, _fence1, null);
        Vk.DestroyCommandPool(Device, _commandPool, null);
        _allocator.FreeBuffer(Staging0);
        _allocator.FreeBuffer(Staging1);
    }
}
