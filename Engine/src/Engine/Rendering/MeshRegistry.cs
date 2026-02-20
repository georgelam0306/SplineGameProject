using System.Numerics;
using System.Runtime.CompilerServices;
using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Memory;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace DerpLib.Rendering;

/// <summary>
/// Manages a global vertex and index buffer for all meshes.
/// Meshes are concatenated into shared buffers for efficient indirect rendering.
/// </summary>
public sealed class MeshRegistry : IDisposable
{
    /// <summary>Initial vertex buffer capacity (64K vertices = 2MB at 32 bytes each).</summary>
    private const int InitialVertexCapacity = 64 * 1024;

    /// <summary>Initial index buffer capacity (256K indices = 1MB at 4 bytes each).</summary>
    private const int InitialIndexCapacity = 256 * 1024;

    /// <summary>Maximum number of mesh slots.</summary>
    private const int MaxMeshSlots = 1024;

    private readonly ILogger _log;
    private readonly MemoryAllocator _allocator;

    // GPU buffers
    private BufferAllocation _vertexBuffer;
    private BufferAllocation _indexBuffer;

    // Capacities (in elements, not bytes)
    private int _vertexCapacity;
    private int _indexCapacity;

    // Current usage
    private int _vertexCount;
    private int _indexCount;

    // Mesh metadata
    private readonly MeshSlot[] _slots = new MeshSlot[MaxMeshSlots];
    private int _meshCount;

    private bool _disposed;

    public VkBuffer VertexBuffer => _vertexBuffer.Buffer;
    public VkBuffer IndexBuffer => _indexBuffer.Buffer;
    public int MeshCount => _meshCount;
    public int TotalVertices => _vertexCount;
    public int TotalIndices => _indexCount;

    /// <summary>Handle to the built-in unit quad mesh (mesh 0).</summary>
    public MeshHandle QuadMesh { get; private set; }

    public MeshRegistry(ILogger log, MemoryAllocator allocator)
    {
        _log = log;
        _allocator = allocator;
    }

    /// <summary>
    /// Initializes GPU buffers. Must be called after device is ready.
    /// </summary>
    public void Initialize()
    {
        _vertexCapacity = InitialVertexCapacity;
        _indexCapacity = InitialIndexCapacity;

        _vertexBuffer = _allocator.CreateVertexBuffer((ulong)(_vertexCapacity * Vertex3D.SizeInBytes));
        _indexBuffer = _allocator.CreateIndexBuffer((ulong)(_indexCapacity * sizeof(uint)));

        // Register built-in unit quad as mesh 0
        QuadMesh = CreateQuadMesh();

        _log.Debug("MeshRegistry initialized: {VertexCap} vertices, {IndexCap} indices, quad mesh registered",
            _vertexCapacity, _indexCapacity);
    }

    /// <summary>
    /// Creates a unit quad mesh centered at origin (-0.5 to 0.5).
    /// UV: (0,0) top-left, (1,1) bottom-right (Y-flipped for Vulkan).
    /// </summary>
    private MeshHandle CreateQuadMesh()
    {
        // Unit quad in XY plane, Z=0, normal pointing +Z
        Span<Vertex3D> vertices = stackalloc Vertex3D[4]
        {
            new(new(-0.5f, -0.5f, 0), new(0, 0, 1), new(0, 1)),  // bottom-left
            new(new( 0.5f, -0.5f, 0), new(0, 0, 1), new(1, 1)),  // bottom-right
            new(new( 0.5f,  0.5f, 0), new(0, 0, 1), new(1, 0)),  // top-right
            new(new(-0.5f,  0.5f, 0), new(0, 0, 1), new(0, 0)),  // top-left
        };

        Span<uint> indices = stackalloc uint[6]
        {
            0, 1, 2,  // first triangle
            2, 3, 0   // second triangle
        };

        return RegisterMesh(vertices, indices);
    }

    /// <summary>
    /// Registers a mesh from vertex and index arrays.
    /// Returns a handle for drawing.
    /// </summary>
    public MeshHandle RegisterMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<uint> indices)
    {
        if (_meshCount >= MaxMeshSlots)
        {
            _log.Error("MeshRegistry full: max {Max} meshes", MaxMeshSlots);
            return MeshHandle.Invalid;
        }

        // Ensure capacity
        EnsureVertexCapacity(_vertexCount + vertices.Length);
        EnsureIndexCapacity(_indexCount + indices.Length);

        // Record offsets before appending
        var vertexOffset = (uint)_vertexCount;
        var firstIndex = (uint)_indexCount;

        // Upload vertices
        _allocator.UploadData(_vertexBuffer, vertices, (ulong)(_vertexCount * Vertex3D.SizeInBytes));
        _vertexCount += vertices.Length;

        // Upload indices
        _allocator.UploadData(_indexBuffer, indices, (ulong)(_indexCount * sizeof(uint)));
        _indexCount += indices.Length;

        // Create slot
        var slotId = _meshCount++;
        _slots[slotId] = new MeshSlot
        {
            VertexOffset = vertexOffset,
            FirstIndex = firstIndex,
            VertexCount = vertices.Length,
            IndexCount = indices.Length
        };

        _log.Debug("Registered mesh {Id}: {Verts} vertices, {Indices} indices (vOffset={VOffset}, iOffset={IOffset})",
            slotId, vertices.Length, indices.Length, vertexOffset, firstIndex);

        return new MeshHandle(slotId, vertices.Length, indices.Length, vertexOffset, firstIndex);
    }

    /// <summary>
    /// Registers a mesh with instanced rendering support.
    /// The mesh gets a reserved region in the instanced buffer.
    /// </summary>
    public MeshHandle RegisterMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<uint> indices,
        int instanceRegionBase, int instanceCapacity)
    {
        // Register geometry first
        var handle = RegisterMesh(vertices, indices);
        if (!handle.IsValid) return handle;

        // Return handle with instanced info
        return new MeshHandle(
            handle.Id, handle.VertexCount, handle.IndexCount,
            handle.VertexOffset, handle.FirstIndex,
            instanceRegionBase, instanceCapacity);
    }

    /// <summary>
    /// Uploads all vertices and indices from a CompiledMesh once,
    /// then returns multiple MeshHandles for each submesh.
    /// Much more efficient than registering each submesh separately.
    /// </summary>
    public unsafe MeshHandle[] RegisterMeshWithSubmeshes(float[] vertexData, int vertexCount, uint[] indices,
        Assets.Submesh[] submeshes)
    {
        if (submeshes == null || submeshes.Length == 0)
        {
            // No submeshes - register as single mesh
            return new[] { RegisterMesh(vertexData, vertexCount, indices) };
        }

        if (_meshCount + submeshes.Length > MaxMeshSlots)
        {
            _log.Error("MeshRegistry full: need {Need}, max {Max}", submeshes.Length, MaxMeshSlots);
            return Array.Empty<MeshHandle>();
        }

        // Upload all vertices once
        EnsureVertexCapacity(_vertexCount + vertexCount);
        var baseVertexOffset = (uint)_vertexCount;

        fixed (float* ptr = vertexData)
        {
            var vertices = new ReadOnlySpan<Vertex3D>(ptr, vertexCount);
            _allocator.UploadData(_vertexBuffer, vertices, (ulong)(_vertexCount * Vertex3D.SizeInBytes));
        }
        _vertexCount += vertexCount;

        // Upload all indices once
        EnsureIndexCapacity(_indexCount + indices.Length);
        var baseIndexOffset = (uint)_indexCount;
        _allocator.UploadData<uint>(_indexBuffer, indices.AsSpan(), (ulong)(_indexCount * sizeof(uint)));
        _indexCount += indices.Length;

        // Create a MeshHandle for each submesh
        var handles = new MeshHandle[submeshes.Length];
        for (int i = 0; i < submeshes.Length; i++)
        {
            var sub = submeshes[i];
            var slotId = _meshCount++;

            // Each submesh points to a portion of the shared index buffer
            // vertexOffset is shared (baseVertexOffset), indices are offset from baseIndexOffset
            _slots[slotId] = new MeshSlot
            {
                VertexOffset = baseVertexOffset,
                FirstIndex = baseIndexOffset + (uint)sub.IndexOffset,
                VertexCount = vertexCount,  // All submeshes share the same vertices
                IndexCount = sub.IndexCount
            };

            handles[i] = new MeshHandle(slotId, vertexCount, sub.IndexCount,
                baseVertexOffset, baseIndexOffset + (uint)sub.IndexOffset);
        }

        _log.Debug("Registered mesh with {Submeshes} submeshes: {Verts} vertices, {Indices} indices",
            submeshes.Length, vertexCount, indices.Length);

        return handles;
    }

    /// <summary>
    /// Registers a mesh from CompiledMesh data (interleaved floats).
    /// </summary>
    public unsafe MeshHandle RegisterMesh(float[] vertexData, int vertexCount, uint[] indices)
    {
        // Reinterpret float[] as Vertex3D[] (8 floats = 1 vertex)
        if (vertexData.Length != vertexCount * 8)
        {
            _log.Error("Invalid vertex data: expected {Expected} floats, got {Actual}",
                vertexCount * 8, vertexData.Length);
            return MeshHandle.Invalid;
        }

        fixed (float* ptr = vertexData)
        {
            var vertices = new ReadOnlySpan<Vertex3D>(ptr, vertexCount);
            return RegisterMesh(vertices, indices);
        }
    }

    /// <summary>
    /// Gets mesh info by slot ID.
    /// </summary>
    public MeshSlot GetSlot(int id)
    {
        if (id < 0 || id >= _meshCount)
            return default;
        return _slots[id];
    }

    private void EnsureVertexCapacity(int required)
    {
        if (required <= _vertexCapacity)
            return;

        var newCapacity = _vertexCapacity;
        while (newCapacity < required)
            newCapacity *= 2;

        GrowVertexBuffer(newCapacity);
    }

    private void EnsureIndexCapacity(int required)
    {
        if (required <= _indexCapacity)
            return;

        var newCapacity = _indexCapacity;
        while (newCapacity < required)
            newCapacity *= 2;

        GrowIndexBuffer(newCapacity);
    }

    private unsafe void GrowVertexBuffer(int newCapacity)
    {
        var newSize = (ulong)(newCapacity * Vertex3D.SizeInBytes);
        var newBuffer = _allocator.CreateVertexBuffer(newSize);

        // Copy existing data
        if (_vertexCount > 0)
        {
            var oldPtr = (byte*)_allocator.MapMemory(_vertexBuffer);
            var newPtr = (byte*)_allocator.MapMemory(newBuffer);
            Unsafe.CopyBlock(newPtr, oldPtr, (uint)(_vertexCount * Vertex3D.SizeInBytes));
            _allocator.UnmapMemory(newBuffer);
            _allocator.UnmapMemory(_vertexBuffer);
        }

        _allocator.FreeBuffer(_vertexBuffer);
        _vertexBuffer = newBuffer;
        _vertexCapacity = newCapacity;

        _log.Debug("Grew vertex buffer to {Capacity} vertices ({Size:N0} bytes)",
            newCapacity, newSize);
    }

    private unsafe void GrowIndexBuffer(int newCapacity)
    {
        var newSize = (ulong)(newCapacity * sizeof(uint));
        var newBuffer = _allocator.CreateIndexBuffer(newSize);

        // Copy existing data
        if (_indexCount > 0)
        {
            var oldPtr = (byte*)_allocator.MapMemory(_indexBuffer);
            var newPtr = (byte*)_allocator.MapMemory(newBuffer);
            Unsafe.CopyBlock(newPtr, oldPtr, (uint)(_indexCount * sizeof(uint)));
            _allocator.UnmapMemory(newBuffer);
            _allocator.UnmapMemory(_indexBuffer);
        }

        _allocator.FreeBuffer(_indexBuffer);
        _indexBuffer = newBuffer;
        _indexCapacity = newCapacity;

        _log.Debug("Grew index buffer to {Capacity} indices ({Size:N0} bytes)",
            newCapacity, newSize);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _allocator.FreeBuffer(_vertexBuffer);
        _allocator.FreeBuffer(_indexBuffer);

        _log.Debug("MeshRegistry disposed: {Meshes} meshes, {Vertices} vertices, {Indices} indices",
            _meshCount, _vertexCount, _indexCount);
    }
}

/// <summary>
/// Metadata for a registered mesh.
/// </summary>
public struct MeshSlot
{
    /// <summary>Vertex offset in the global buffer (for vertexOffset in draw call).</summary>
    public uint VertexOffset;

    /// <summary>First index in the global buffer (for firstIndex in draw call).</summary>
    public uint FirstIndex;

    /// <summary>Number of vertices.</summary>
    public int VertexCount;

    /// <summary>Number of indices (for indexCount in draw call).</summary>
    public int IndexCount;
}
