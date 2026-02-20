using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Core;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using DeviceCapabilities = DerpLib.Core.DeviceCapabilities;

namespace DerpLib.Memory;

/// <summary>
/// Simple memory allocator for Vulkan buffers.
/// Handles buffer creation, memory allocation, and data upload.
/// </summary>
public sealed class MemoryAllocator : IDisposable
{
    private readonly ILogger _log;
    private readonly VkDevice _vkDevice;
    private readonly DeviceCapabilities _capabilities;
    private readonly List<DeviceMemory> _allocations = new();
    private readonly List<VkBuffer> _buffers = new();

    private int _allocationCount;
    public int AllocationCount => _allocationCount;

    private Vk Vk => _vkDevice.Vk;
    private Device Device => _vkDevice.Device;

    public MemoryAllocator(ILogger log, VkDevice vkDevice)
    {
        _log = log;
        _vkDevice = vkDevice;
        _capabilities = vkDevice.Capabilities;
    }

    /// <summary>
    /// Creates a buffer with the specified usage and memory properties.
    /// </summary>
    public unsafe BufferAllocation CreateBuffer(
        ulong size,
        BufferUsageFlags usage,
        MemoryPropertyFlags requiredProperties)
    {
        // Create buffer
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        VkBuffer buffer;
        var result = Vk.CreateBuffer(Device, &bufferInfo, null, &buffer);
        if (result != Result.Success)
        {
            throw new Exception($"Failed to create buffer: {result}");
        }

        // Get memory requirements
        MemoryRequirements memRequirements;
        Vk.GetBufferMemoryRequirements(Device, buffer, &memRequirements);

        // Find suitable memory type
        var memoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, requiredProperties);
        if (memoryTypeIndex == -1)
        {
            Vk.DestroyBuffer(Device, buffer, null);
            throw new Exception($"Failed to find suitable memory type for properties: {requiredProperties}");
        }

        // Allocate memory
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = (uint)memoryTypeIndex
        };

        DeviceMemory memory;
        result = Vk.AllocateMemory(Device, &allocInfo, null, &memory);
        if (result != Result.Success)
        {
            Vk.DestroyBuffer(Device, buffer, null);
            throw new Exception($"Failed to allocate memory: {result}");
        }

        // Bind memory to buffer
        result = Vk.BindBufferMemory(Device, buffer, memory, 0);
        if (result != Result.Success)
        {
            Vk.FreeMemory(Device, memory, null);
            Vk.DestroyBuffer(Device, buffer, null);
            throw new Exception($"Failed to bind buffer memory: {result}");
        }

        _allocations.Add(memory);
        _buffers.Add(buffer);

        _allocationCount++;
        _log.Debug("Allocated buffer: {Size:N0} bytes, usage={Usage}, props={Props}",
            size, usage, requiredProperties);

        return new BufferAllocation(buffer, memory, size, requiredProperties);
    }

    /// <summary>
    /// Creates a host-visible buffer suitable for vertex data.
    /// </summary>
    public BufferAllocation CreateVertexBuffer(ulong size)
    {
        return CreateBuffer(
            size,
            BufferUsageFlags.VertexBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
    }

    /// <summary>
    /// Creates a host-visible buffer suitable for index data.
    /// </summary>
    public BufferAllocation CreateIndexBuffer(ulong size)
    {
        return CreateBuffer(
            size,
            BufferUsageFlags.IndexBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
    }

    /// <summary>
    /// Creates a storage buffer (SSBO) for shader access.
    /// Uses HOST_VISIBLE memory on unified architectures, DEVICE_LOCAL on discrete.
    /// </summary>
    public BufferAllocation CreateStorageBuffer(ulong size)
    {
        var usage = BufferUsageFlags.StorageBufferBit
                  | BufferUsageFlags.TransferDstBit
                  | BufferUsageFlags.TransferSrcBit;

        if (_capabilities.IsUnifiedMemory)
        {
            // Unified memory: use host-visible, host-coherent memory (no staging needed)
            return CreateBuffer(size, usage,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }
        else
        {
            // Discrete GPU: use device-local memory (staging required)
            return CreateBuffer(size, usage, MemoryPropertyFlags.DeviceLocalBit);
        }
    }

    /// <summary>
    /// Creates a staging buffer for transfers to device-local memory.
    /// </summary>
    public BufferAllocation CreateStagingBuffer(ulong size)
    {
        return CreateBuffer(size,
            BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
    }

    /// <summary>
    /// Creates a typed storage buffer for shader access.
    /// </summary>
    public StorageBuffer<T> CreateStorageBuffer<T>(int capacity, bool readable = false) where T : unmanaged
    {
        var size = (ulong)(capacity * System.Runtime.InteropServices.Marshal.SizeOf<T>());
        var allocation = CreateStorageBuffer(size);

        ReadbackState? readback = null;
        if (readable)
        {
            readback = new ReadbackState(_vkDevice, this, size);
        }

        return new StorageBuffer<T>(allocation, this, capacity, readback);
    }

    /// <summary>
    /// Frees a typed storage buffer.
    /// </summary>
    public void FreeStorageBuffer<T>(StorageBuffer<T> buffer) where T : unmanaged
    {
        FreeBuffer(buffer.Allocation);
    }

    /// <summary>
    /// Maps buffer memory for CPU access.
    /// </summary>
    public unsafe void* MapMemory(BufferAllocation allocation)
    {
        void* data;
        var result = Vk.MapMemory(Device, allocation.Memory, 0, allocation.Size, 0, &data);
        if (result != Result.Success)
        {
            throw new Exception($"Failed to map memory: {result}");
        }
        return data;
    }

    /// <summary>
    /// Unmaps previously mapped memory.
    /// </summary>
    public void UnmapMemory(BufferAllocation allocation)
    {
        Vk.UnmapMemory(Device, allocation.Memory);
    }

    /// <summary>
    /// Uploads data to a buffer.
    /// </summary>
    public unsafe void UploadData<T>(BufferAllocation allocation, ReadOnlySpan<T> data) where T : unmanaged
    {
        UploadData(allocation, data, 0);
    }

    /// <summary>
    /// Uploads data to a buffer at a byte offset.
    /// </summary>
    public unsafe void UploadData<T>(BufferAllocation allocation, ReadOnlySpan<T> data, ulong byteOffset) where T : unmanaged
    {
        var byteSize = (ulong)(data.Length * sizeof(T));
        if (byteOffset + byteSize > allocation.Size)
        {
            throw new ArgumentException($"Data size ({byteSize}) at offset ({byteOffset}) exceeds buffer size ({allocation.Size})");
        }

        var mapped = MapMemory(allocation);
        var dest = (byte*)mapped + byteOffset;
        fixed (T* src = data)
        {
            System.Buffer.MemoryCopy(src, dest, (long)(allocation.Size - byteOffset), (long)byteSize);
        }
        UnmapMemory(allocation);
    }

    /// <summary>
    /// Frees a buffer and its associated memory.
    /// </summary>
    public unsafe void FreeBuffer(BufferAllocation allocation)
    {
        Vk.DestroyBuffer(Device, allocation.Buffer, null);
        Vk.FreeMemory(Device, allocation.Memory, null);
        _buffers.Remove(allocation.Buffer);
        _allocations.Remove(allocation.Memory);
    }

    private int FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        var memProperties = _vkDevice.MemoryProperties;
        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << i)) != 0 &&
                (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }
        return -1;
    }

    public unsafe void Dispose()
    {
        foreach (var buffer in _buffers)
        {
            Vk.DestroyBuffer(Device, buffer, null);
        }
        foreach (var memory in _allocations)
        {
            Vk.FreeMemory(Device, memory, null);
        }
        _buffers.Clear();
        _allocations.Clear();
    }
}

/// <summary>
/// Represents an allocated buffer and its associated memory.
/// </summary>
public readonly struct BufferAllocation
{
    public VkBuffer Buffer { get; }
    public DeviceMemory Memory { get; }
    public ulong Size { get; }
    public MemoryPropertyFlags Properties { get; }
    public bool IsHostVisible => (Properties & MemoryPropertyFlags.HostVisibleBit) != 0;

    public BufferAllocation(VkBuffer buffer, DeviceMemory memory, ulong size, MemoryPropertyFlags properties)
    {
        Buffer = buffer;
        Memory = memory;
        Size = size;
        Properties = properties;
    }
}
