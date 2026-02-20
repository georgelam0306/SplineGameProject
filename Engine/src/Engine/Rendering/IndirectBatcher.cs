using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Memory;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace DerpLib.Rendering;

/// <summary>
/// Collects instances and issues batched indirect draw calls.
/// Uses counting sort to group instances by mesh type.
/// </summary>
public sealed class IndirectBatcher : IDisposable
{
    private readonly ILogger _log;
    private readonly MemoryAllocator _allocator;
    private readonly MeshRegistry _meshRegistry;
    private readonly int _framesInFlight;
    private readonly int _maxInstances;
    private readonly int _maxMeshTypes;

    // Input: unsorted pending instances
    private readonly PendingInstance[] _pending;
    private int _pendingCount;

    // Base offsets for multi-pass rendering (accumulate across Flush calls within a frame)
    private int _baseInstanceOffset;
    private int _baseCommandOffset;

    // Current batch's command location (set by Flush, used by Draw)
    private int _currentBatchCommandOffset;
    private int _currentBatchCommandCount;

    // Counting sort workspace
    private readonly int[] _counts;
    private readonly int[] _offsets;
    private readonly int[] _writeHeads;

    // Output: sorted instances
    private readonly InstanceData[] _sorted;

    // Indirect commands (one per mesh type used this frame)
    private readonly IndirectCommand[] _commands;
    private int _commandCount;

    // Track which meshes were used this frame
    private readonly int[] _usedMeshes;
    private int _usedMeshCount;

    // GPU buffers
    private readonly BufferAllocation[] _instanceBuffers;  // Per frame
    private readonly BufferAllocation[] _indirectBuffers; // Per frame

    private bool _disposed;

    public int InstanceCount => _pendingCount;
    public int CommandCount => _commandCount;

    public IndirectBatcher(
        ILogger log,
        MemoryAllocator allocator,
        MeshRegistry meshRegistry,
        int framesInFlight,
        int maxInstances,
        int maxMeshTypes)
    {
        _log = log;
        _allocator = allocator;
        _meshRegistry = meshRegistry;
        _framesInFlight = framesInFlight;
        _maxInstances = maxInstances;
        _maxMeshTypes = maxMeshTypes;

        _pending = new PendingInstance[maxInstances];
        _sorted = new InstanceData[maxInstances];
        _commands = new IndirectCommand[maxMeshTypes];
        _counts = new int[maxMeshTypes];
        _offsets = new int[maxMeshTypes];
        _writeHeads = new int[maxMeshTypes];
        _usedMeshes = new int[maxMeshTypes];

        _instanceBuffers = new BufferAllocation[framesInFlight];
        _indirectBuffers = new BufferAllocation[framesInFlight];
    }

    /// <summary>
    /// Creates GPU buffers. Must be called after device is initialized.
    /// </summary>
    public unsafe void Initialize()
    {
        // Create instance buffers (one per frame in flight)
        var instanceBufferSize = (ulong)(_maxInstances * sizeof(InstanceData));
        for (int i = 0; i < _framesInFlight; i++)
        {
            _instanceBuffers[i] = _allocator.CreateVertexBuffer(instanceBufferSize);
        }

        // Create indirect command buffers (one per frame in flight).
        // A single shared indirect buffer can be overwritten while older frames are still executing.
        var indirectBufferSize = (ulong)(_maxMeshTypes * IndirectCommand.SizeInBytes);
        for (int i = 0; i < _framesInFlight; i++)
        {
            _indirectBuffers[i] = _allocator.CreateBuffer(
                indirectBufferSize,
                BufferUsageFlags.IndirectBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }

        _log.Debug("IndirectBatcher initialized: {MaxInstances} max instances, {MaxMeshTypes} max mesh types",
            _maxInstances, _maxMeshTypes);
    }

    /// <summary>
    /// Adds an instance to the batch.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(MeshHandle mesh, uint transformIndex, uint textureIndex, uint packedColor, uint flags)
    {
        if (_pendingCount >= _maxInstances)
            return;

        _pending[_pendingCount++] = new PendingInstance
        {
            MeshId = mesh.Id,
            Data = new InstanceData
            {
                TransformIndex = transformIndex,
                TextureIndex = textureIndex,
                PackedColor = packedColor,
                PackedUVOffset = flags
            }
        };
    }

    /// <summary>
    /// Adds an instance with unpacked color parameters.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(MeshHandle mesh, uint transformIndex, uint textureIndex, byte r, byte g, byte b, byte a, float uvOffsetX = 0, float uvOffsetY = 0)
    {
        Add(mesh, transformIndex, textureIndex,
            InstanceData.PackColor(r, g, b, a),
            InstanceData.PackHalf2(uvOffsetX, uvOffsetY));
    }

    /// <summary>
    /// Sorts instances by mesh, builds indirect commands, uploads to GPU.
    /// </summary>
    public unsafe void Flush(int frameIndex)
    {
        if (_pendingCount == 0)
        {
            _commandCount = 0;
            _currentBatchCommandOffset = 0;
            _currentBatchCommandCount = 0;
            return;
        }

        // === Pass 1: Count instances per mesh ===
        Array.Clear(_counts, 0, _maxMeshTypes);
        _usedMeshCount = 0;

        for (int i = 0; i < _pendingCount; i++)
        {
            int meshId = _pending[i].MeshId;

            // Track first use of this mesh
            if (_counts[meshId] == 0)
                _usedMeshes[_usedMeshCount++] = meshId;

            _counts[meshId]++;
        }

        // === Pass 2: Compute prefix sums (offsets) and build commands ===
        int offset = 0;
        _commandCount = 0;

        // Simple insertion sort for deterministic ordering (no allocation, fast for small N)
        for (int i = 1; i < _usedMeshCount; i++)
        {
            int key = _usedMeshes[i];
            int j = i - 1;
            while (j >= 0 && _usedMeshes[j] > key)
            {
                _usedMeshes[j + 1] = _usedMeshes[j];
                j--;
            }
            _usedMeshes[j + 1] = key;
        }

        for (int i = 0; i < _usedMeshCount; i++)
        {
            int meshId = _usedMeshes[i];
            var slot = _meshRegistry.GetSlot(meshId);

            _offsets[meshId] = offset;
            _writeHeads[meshId] = offset;

            // Build indirect command (FirstInstance includes base offset for multi-pass)
            _commands[_commandCount++] = new IndirectCommand
            {
                IndexCount = (uint)slot.IndexCount,
                InstanceCount = (uint)_counts[meshId],
                FirstIndex = slot.FirstIndex,
                VertexOffset = (int)slot.VertexOffset,
                FirstInstance = (uint)(_baseInstanceOffset + offset)
            };

            offset += _counts[meshId];
        }

        // === Pass 3: Scatter instances to sorted positions ===
        for (int i = 0; i < _pendingCount; i++)
        {
            int meshId = _pending[i].MeshId;
            int dest = _writeHeads[meshId]++;
            _sorted[dest] = _pending[i].Data;
        }

        // === Upload to GPU ===
        // Upload sorted instances at base offset (for multi-pass rendering)
        var instancePtr = (InstanceData*)_allocator.MapMemory(_instanceBuffers[frameIndex]);
        fixed (InstanceData* src = _sorted)
        {
            Unsafe.CopyBlock(instancePtr + _baseInstanceOffset, src, (uint)(_pendingCount * sizeof(InstanceData)));
        }
        _allocator.UnmapMemory(_instanceBuffers[frameIndex]);

        // Upload indirect commands at base offset (for multi-pass rendering)
        var commandPtr = (IndirectCommand*)_allocator.MapMemory(_indirectBuffers[frameIndex]);
        fixed (IndirectCommand* src = _commands)
        {
            Unsafe.CopyBlock(commandPtr + _baseCommandOffset, src, (uint)(_commandCount * IndirectCommand.SizeInBytes));
        }
        _allocator.UnmapMemory(_indirectBuffers[frameIndex]);

        // Store current batch location for Draw()
        _currentBatchCommandOffset = _baseCommandOffset;
        _currentBatchCommandCount = _commandCount;

        // Advance base offsets for next pass, clear pending for next batch
        _baseInstanceOffset += _pendingCount;
        _baseCommandOffset += _commandCount;
        _pendingCount = 0;
    }

    /// <summary>
    /// Issues indirect draw call. Call after Flush.
    /// </summary>
    public unsafe void Draw(Vk vk, CommandBuffer cmd, int frameIndex)
    {
        if (_currentBatchCommandCount == 0) return;

        // Bind vertex buffer (binding 0) - global mesh vertex buffer
        var vertexBuffer = _meshRegistry.VertexBuffer;
        ulong vertexOffset = 0;
        vk.CmdBindVertexBuffers(cmd, 0, 1, &vertexBuffer, &vertexOffset);

        // Bind instance buffer (binding 1)
        var instanceBuffer = _instanceBuffers[frameIndex].Buffer;
        ulong instanceOffset = 0;
        vk.CmdBindVertexBuffers(cmd, 1, 1, &instanceBuffer, &instanceOffset);

        // Bind global index buffer
        vk.CmdBindIndexBuffer(cmd, _meshRegistry.IndexBuffer, 0, IndexType.Uint32);

        // Issue indirect draw call for this batch's commands
        vk.CmdDrawIndexedIndirect(
            cmd,
            _indirectBuffers[frameIndex].Buffer,
            (ulong)(_currentBatchCommandOffset * IndirectCommand.SizeInBytes),
            (uint)_currentBatchCommandCount,
            (uint)IndirectCommand.SizeInBytes);
    }

    /// <summary>
    /// Resets the batch for the next frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        _pendingCount = 0;
        _commandCount = 0;
        _baseInstanceOffset = 0;
        _baseCommandOffset = 0;
        _currentBatchCommandOffset = 0;
        _currentBatchCommandCount = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = 0; i < _framesInFlight; i++)
        {
            _allocator.FreeBuffer(_instanceBuffers[i]);
            _allocator.FreeBuffer(_indirectBuffers[i]);
        }

        _log.Debug("IndirectBatcher disposed");
    }
}

/// <summary>
/// Pending instance before sorting.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PendingInstance
{
    public int MeshId;
    public InstanceData Data;
}
