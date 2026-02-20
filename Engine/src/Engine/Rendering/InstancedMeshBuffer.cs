using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Memory;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace DerpLib.Rendering;

/// <summary>
/// High-frequency instanced mesh buffer with per-mesh regions.
/// No sorting - instances append directly to their mesh's region.
/// </summary>
public sealed class InstancedMeshBuffer : IDisposable
{
    private readonly ILogger _log;
    private readonly MemoryAllocator _allocator;
    private readonly MeshRegistry _meshRegistry;
    private readonly int _framesInFlight;

    // Per-mesh region tracking
    private readonly int[] _regionBases;     // Start offset for each mesh
    private readonly int[] _regionCapacities; // Max instances per mesh
    private readonly int[] _regionCounts;    // Current count per mesh (reset each frame)
    private int _totalCapacity;
    private int _registeredMeshCount;
    private int _totalInstanceCount;         // Actual instances this frame (for efficient upload)
    private int _maxWrittenIndex;            // Max index written this pass/frame (for safe upload span)

    // CPU-side instance data (direct write to regions)
    private InstanceData[] _instances;

    // Commands (one per registered mesh with count > 0)
    private readonly IndirectCommand[] _commands;
    private int _commandCount;

    // GPU buffers
    private readonly BufferAllocation[] _instanceBuffers;  // Per frame
    private readonly BufferAllocation[] _indirectBuffers; // Per frame

    private bool _disposed;

    public int TotalCapacity => _totalCapacity;
    public int RegisteredMeshCount => _registeredMeshCount;

    public InstancedMeshBuffer(
        ILogger log,
        MemoryAllocator allocator,
        MeshRegistry meshRegistry,
        int framesInFlight,
        int maxMeshTypes = 64)
    {
        _log = log;
        _allocator = allocator;
        _meshRegistry = meshRegistry;
        _framesInFlight = framesInFlight;

        _regionBases = new int[maxMeshTypes];
        _regionCapacities = new int[maxMeshTypes];
        _regionCounts = new int[maxMeshTypes];
        _commands = new IndirectCommand[maxMeshTypes];

        // Placeholder, reallocated in Initialize()
        _instances = Array.Empty<InstanceData>();
        _instanceBuffers = new BufferAllocation[framesInFlight];
        _indirectBuffers = new BufferAllocation[framesInFlight];

        _log.Debug("InstancedMeshBuffer created");
    }

    /// <summary>
    /// Register a mesh for high-frequency instancing.
    /// Returns the region base offset for the mesh handle.
    /// </summary>
    public int RegisterMesh(int meshId, int capacity)
    {
        if (_registeredMeshCount > 0)
        {
            // For now, require all meshes registered before first use
            // Could support dynamic registration later
            _log.Warning("Registering mesh after buffer initialized - may need reallocation");
        }

        int baseOffset = _totalCapacity;
        _regionBases[meshId] = baseOffset;
        _regionCapacities[meshId] = capacity;
        _totalCapacity += capacity;
        _registeredMeshCount++;

        _log.Debug("Registered instanced mesh {MeshId}: base={Base}, capacity={Cap}, total={Total}",
            meshId, baseOffset, capacity, _totalCapacity);

        return baseOffset;
    }

    /// <summary>
    /// Initialize GPU buffers. Call after all meshes are registered.
    /// </summary>
    public void Initialize()
    {
        if (_totalCapacity == 0)
        {
            _log.Warning("InstancedMeshBuffer.Initialize called with no registered meshes");
            return;
        }

        // Allocate CPU instance array
        _instances = new InstanceData[_totalCapacity];

        // Allocate GPU buffers
        var instanceSize = (ulong)(_totalCapacity * Unsafe.SizeOf<InstanceData>());
        var commandSize = (ulong)(_registeredMeshCount * IndirectCommand.SizeInBytes);

        for (int i = 0; i < _framesInFlight; i++)
        {
            _instanceBuffers[i] = _allocator.CreateVertexBuffer(instanceSize);
            _indirectBuffers[i] = _allocator.CreateBuffer(
                commandSize,
                BufferUsageFlags.IndirectBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }

        _log.Information("InstancedMeshBuffer initialized: {Capacity} instances, {Meshes} meshes",
            _totalCapacity, _registeredMeshCount);
    }

    /// <summary>
    /// Add an instance to its mesh's region. Returns transform index or -1 if full.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Add(MeshHandle mesh, uint transformIndex, uint textureIndex, uint packedColor)
    {
        int meshId = mesh.Id;
        int count = _regionCounts[meshId];
        int capacity = _regionCapacities[meshId];

        if (count >= capacity)
            return -1;  // Region full

        int baseOffset = _regionBases[meshId];
        int index = baseOffset + count;
        _regionCounts[meshId] = count + 1;
        _totalInstanceCount++;
        int writtenEnd = index + 1;
        if (writtenEnd > _maxWrittenIndex)
        {
            _maxWrittenIndex = writtenEnd;
        }

        _instances[index] = new InstanceData
        {
            TransformIndex = transformIndex,
            TextureIndex = textureIndex,
            PackedColor = packedColor,
            PackedUVOffset = 0
        };

        return index;
    }

    /// <summary>
    /// Build commands and upload to GPU. Call before Draw.
    /// </summary>
    public unsafe void Flush(int frameIndex)
    {
        _commandCount = 0;

        // Build one command per mesh with instances
        // Only iterate up to our array size - meshes beyond that aren't registered as instanced
        int maxMeshId = Math.Min(_meshRegistry.MeshCount, _regionCapacities.Length);
        for (int meshId = 0; meshId < maxMeshId; meshId++)
        {
            int count = _regionCounts[meshId];
            if (count == 0) continue;

            // Skip meshes that aren't registered as instanced
            if (_regionCapacities[meshId] == 0) continue;

            var slot = _meshRegistry.GetSlot(meshId);
            _commands[_commandCount++] = new IndirectCommand
            {
                IndexCount = (uint)slot.IndexCount,
                InstanceCount = (uint)count,
                FirstIndex = slot.FirstIndex,
                VertexOffset = (int)slot.VertexOffset,
                FirstInstance = (uint)_regionBases[meshId]
            };
        }

        if (_commandCount == 0) return;

        // Regions can be sparse by mesh id; upload through the highest written index.
        int uploadCount = _maxWrittenIndex;
        if (uploadCount <= 0)
        {
            return;
        }

        var instancePtr = (InstanceData*)_allocator.MapMemory(_instanceBuffers[frameIndex]);
        fixed (InstanceData* src = _instances)
        {
            Unsafe.CopyBlock(instancePtr, src, (uint)(uploadCount * Unsafe.SizeOf<InstanceData>()));
        }
        _allocator.UnmapMemory(_instanceBuffers[frameIndex]);

        // Upload commands
        var commandPtr = (IndirectCommand*)_allocator.MapMemory(_indirectBuffers[frameIndex]);
        fixed (IndirectCommand* src = _commands)
        {
            Unsafe.CopyBlock(commandPtr, src, (uint)(_commandCount * IndirectCommand.SizeInBytes));
        }
        _allocator.UnmapMemory(_indirectBuffers[frameIndex]);
    }

    /// <summary>
    /// Issue indirect draw calls. Call after Flush.
    /// </summary>
    public unsafe void Draw(Vk vk, CommandBuffer cmd, int frameIndex)
    {
        if (_commandCount == 0) return;

        // Bind vertex buffer (binding 0 = mesh vertices handled by caller)
        // Bind instance buffer (binding 1)
        var instanceBuffer = _instanceBuffers[frameIndex].Buffer;
        ulong instanceOffset = 0;
        vk.CmdBindVertexBuffers(cmd, 1, 1, &instanceBuffer, &instanceOffset);

        // Issue indirect draw
        vk.CmdDrawIndexedIndirect(
            cmd,
            _indirectBuffers[frameIndex].Buffer,
            0,
            (uint)_commandCount,
            (uint)IndirectCommand.SizeInBytes);
    }

    /// <summary>
    /// Get instance buffer for external binding.
    /// </summary>
    public VkBuffer GetInstanceBuffer(int frameIndex) => _instanceBuffers[frameIndex].Buffer;

    /// <summary>
    /// Get indirect command buffer.
    /// </summary>
    public VkBuffer GetCommandBuffer() => _indirectBuffers[0].Buffer;

    public VkBuffer GetCommandBuffer(int frameIndex) => _indirectBuffers[frameIndex].Buffer;

    public int CommandCount => _commandCount;

    /// <summary>
    /// Reset counts for next frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        Array.Clear(_regionCounts, 0, _regionCounts.Length);
        _commandCount = 0;
        _totalInstanceCount = 0;
        _maxWrittenIndex = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = 0; i < _framesInFlight; i++)
        {
            if (_instanceBuffers[i].Buffer.Handle != 0)
                _allocator.FreeBuffer(_instanceBuffers[i]);
            if (_indirectBuffers[i].Buffer.Handle != 0)
                _allocator.FreeBuffer(_indirectBuffers[i]);
        }

        _log.Debug("InstancedMeshBuffer disposed");
    }
}
