using System.Numerics;
using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Memory;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace DerpLib.Rendering;

/// <summary>
/// Manages transform matrices in a GPU storage buffer (SSBO).
/// Double-buffered for safe CPU/GPU overlap.
/// </summary>
public sealed class TransformBuffer : IDisposable
{
    private readonly ILogger _log;
    private readonly MemoryAllocator _allocator;
    private readonly int _framesInFlight;
    private readonly int _maxTransforms;

    // CPU-side transform storage
    private readonly Matrix4x4[] _transforms;
    private int _count;

    // GPU buffers - one per frame in flight
    private readonly BufferAllocation[] _buffers;

    private bool _disposed;

    /// <summary>
    /// Number of allocated transforms.
    /// </summary>
    public int Count => _count;

    public TransformBuffer(ILogger log, MemoryAllocator allocator, int framesInFlight, int maxTransforms)
    {
        _log = log;
        _allocator = allocator;
        _framesInFlight = framesInFlight;
        _maxTransforms = maxTransforms;

        // Allocate CPU storage
        _transforms = new Matrix4x4[maxTransforms];
        _buffers = new BufferAllocation[framesInFlight];
    }

    /// <summary>
    /// Creates GPU buffers. Must be called after device is initialized.
    /// </summary>
    public void Initialize()
    {
        // Create GPU buffers (64 bytes per mat4)
        var bufferSize = (ulong)(_maxTransforms * 64);

        for (int i = 0; i < _framesInFlight; i++)
        {
            _buffers[i] = _allocator.CreateStorageBuffer(bufferSize);
        }

        _log.Debug("TransformBuffer initialized: {MaxTransforms} max transforms, {Frames} frames",
            _maxTransforms, _framesInFlight);
    }

    /// <summary>
    /// Allocates a transform slot and returns the index.
    /// Caller must call SetTransform() before rendering.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public int Allocate()
    {
        if (_count >= _maxTransforms)
            throw new InvalidOperationException($"TransformBuffer full: {_maxTransforms} transforms");

        return _count++;
    }

    /// <summary>
    /// Sets the transform at the given index.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void SetTransform(int index, in Matrix4x4 transform)
    {
        _transforms[index] = transform;
    }

    /// <summary>
    /// Gets the transform at the given index.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public ref Matrix4x4 GetTransform(int index) => ref _transforms[index];

    /// <summary>
    /// Uploads transforms to the GPU buffer for the specified frame.
    /// Call this once per frame before rendering.
    /// </summary>
    public unsafe void Sync(int frameIndex)
    {
        if (_count == 0) return;

        var ptr = _allocator.MapMemory(_buffers[frameIndex]);
        fixed (Matrix4x4* src = _transforms)
        {
            var byteCount = (long)_count * 64;
            System.Buffer.MemoryCopy(src, ptr, byteCount, byteCount);
        }
        _allocator.UnmapMemory(_buffers[frameIndex]);
    }

    /// <summary>
    /// Gets the GPU buffer handle for binding as SSBO.
    /// </summary>
    public VkBuffer GetBuffer(int frameIndex) => _buffers[frameIndex].Buffer;

    /// <summary>
    /// Resets the buffer, freeing all allocated transforms.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void Reset() => _count = 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = 0; i < _framesInFlight; i++)
        {
            _allocator.FreeBuffer(_buffers[i]);
        }

        _log.Debug("TransformBuffer disposed");
    }
}
