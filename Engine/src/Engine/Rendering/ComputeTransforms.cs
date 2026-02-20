using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Core;
using DerpLib.Memory;
using DerpLib.Shaders;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using PipelineCache = DerpLib.Shaders.PipelineCache;

namespace DerpLib.Rendering;

/// <summary>
/// Input parameters for GPU matrix generation.
/// 48 bytes per instance (std430 aligned), uploaded to GPU each frame.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InstanceParams
{
    public Vector3 Position;    // 0-11 (12 bytes)
    public float RotationY;     // 12-15 (4 bytes)
    public Vector3 Scale;       // 16-27 (12 bytes, aligned to 16 for vec3)
    public uint MeshId;         // 28-31 (4 bytes)
    public uint TextureId;      // 32-35 (4 bytes)
    public uint PackedColor;    // 36-39 (4 bytes)
    private uint _pad0;         // 40-43 (4 bytes, padding for 48-byte stride)
    private uint _pad1;         // 44-47 (4 bytes)
}                               // 48 bytes total

/// <summary>
/// Generates transform matrices on GPU using compute shaders.
/// Replaces CPU matrix computation for high instance counts.
/// </summary>
public sealed class ComputeTransforms : IDisposable
{
    private readonly ILogger _log;
    private readonly VkDevice _vkDevice;
    private readonly MemoryAllocator _allocator;
    private readonly PipelineCache _pipelineCache;
    private readonly int _framesInFlight;
    private readonly int _maxInstances;

    // CPU-side params storage (written by game code)
    private readonly InstanceParams[] _params;
    private int _count;

    // GPU buffers
    private readonly BufferAllocation[] _paramsBuffers;  // Per frame (input)
    private readonly BufferAllocation[] _transformBuffers;  // Per frame (output)

    // Compute pipeline
    private ComputeShader? _matrixGenShader;
    private DescriptorPool _descriptorPool;
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorSet[] _descriptorSets;

    private bool _disposed;

    private Vk Vk => _vkDevice.Vk;
    private Device Device => _vkDevice.Device;

    /// <summary>
    /// Number of instances added this frame.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Maximum instances supported.
    /// </summary>
    public int MaxInstances => _maxInstances;

    public ComputeTransforms(
        ILogger log,
        VkDevice vkDevice,
        MemoryAllocator allocator,
        PipelineCache pipelineCache,
        int framesInFlight,
        int maxInstances)
    {
        _log = log;
        _vkDevice = vkDevice;
        _allocator = allocator;
        _pipelineCache = pipelineCache;
        _framesInFlight = framesInFlight;
        _maxInstances = maxInstances;

        _params = new InstanceParams[maxInstances];
        _paramsBuffers = new BufferAllocation[framesInFlight];
        _transformBuffers = new BufferAllocation[framesInFlight];
        _descriptorSets = new DescriptorSet[framesInFlight];
    }

    /// <summary>
    /// Creates GPU buffers and descriptors. Call after device is initialized.
    /// </summary>
    public unsafe void Initialize(ComputeShader matrixGenShader)
    {
        _matrixGenShader = matrixGenShader;

        // Create GPU buffers
        var paramsSize = (ulong)(_maxInstances * sizeof(InstanceParams));
        var transformsSize = (ulong)(_maxInstances * 64); // mat4 = 64 bytes

        for (int i = 0; i < _framesInFlight; i++)
        {
            // Params buffer: CPU writable, GPU readable
            _paramsBuffers[i] = _allocator.CreateStorageBuffer(paramsSize);

            // Transform buffer: GPU writable, shader readable
            _transformBuffers[i] = _allocator.CreateStorageBuffer(transformsSize);
        }

        // Create descriptor pool
        var poolSizes = stackalloc DescriptorPoolSize[1];
        poolSizes[0] = new DescriptorPoolSize
        {
            Type = DescriptorType.StorageBuffer,
            DescriptorCount = 2 * (uint)_framesInFlight  // params + transforms per frame
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = (uint)_framesInFlight,
            PoolSizeCount = 1,
            PPoolSizes = poolSizes
        };

        Vk.CreateDescriptorPool(Device, &poolInfo, null, out _descriptorPool);

        // Create descriptor set layout (binding 0: params, binding 1: transforms)
        var bindings = stackalloc DescriptorSetLayoutBinding[2];
        bindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.ComputeBit
        };
        bindings[1] = new DescriptorSetLayoutBinding
        {
            Binding = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.ComputeBit
        };

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 2,
            PBindings = bindings
        };

        Vk.CreateDescriptorSetLayout(Device, &layoutInfo, null, out _descriptorSetLayout);

        // Allocate descriptor sets
        var layouts = stackalloc DescriptorSetLayout[_framesInFlight];
        for (int i = 0; i < _framesInFlight; i++)
            layouts[i] = _descriptorSetLayout;

        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = (uint)_framesInFlight,
            PSetLayouts = layouts
        };

        fixed (DescriptorSet* pSets = _descriptorSets)
        {
            Vk.AllocateDescriptorSets(Device, &allocInfo, pSets);
        }

        // Write descriptor sets
        var writes = stackalloc WriteDescriptorSet[2];
        for (int i = 0; i < _framesInFlight; i++)
        {
            var paramsInfo = new DescriptorBufferInfo
            {
                Buffer = _paramsBuffers[i].Buffer,
                Offset = 0,
                Range = Vk.WholeSize
            };

            var transformsInfo = new DescriptorBufferInfo
            {
                Buffer = _transformBuffers[i].Buffer,
                Offset = 0,
                Range = Vk.WholeSize
            };

            writes[0] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _descriptorSets[i],
                DstBinding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PBufferInfo = &paramsInfo
            };
            writes[1] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _descriptorSets[i],
                DstBinding = 1,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PBufferInfo = &transformsInfo
            };

            Vk.UpdateDescriptorSets(Device, 2, writes, 0, null);
        }

        _log.Debug("ComputeTransforms initialized: {MaxInstances} max instances, {Frames} frames",
            _maxInstances, _framesInFlight);
    }

    /// <summary>
    /// Adds an instance and returns the transform index, or -1 if full.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Add(Vector3 position, float rotationY, Vector3 scale, uint meshId, uint textureId, uint packedColor)
    {
        if (_count >= _maxInstances)
            return -1;

        int idx = _count++;
        _params[idx] = new InstanceParams
        {
            Position = position,
            RotationY = rotationY,
            Scale = scale,
            MeshId = meshId,
            TextureId = textureId,
            PackedColor = packedColor
        };
        return idx;
    }

    /// <summary>
    /// Adds an instance with uniform scale.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Add(Vector3 position, float rotationY, float uniformScale, uint meshId, uint textureId, uint packedColor)
    {
        return Add(position, rotationY, new Vector3(uniformScale), meshId, textureId, packedColor);
    }

    /// <summary>
    /// Adds an instance from a Matrix4x4 transform.
    /// Decomposes the matrix into position, Y-rotation, and scale.
    /// Works correctly for transforms that are Translation * RotationY * Scale.
    /// Preserves negative scales (for UV flipping).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AddMatrix(in Matrix4x4 transform, uint meshId, uint textureId, uint packedColor)
    {
        // Extract translation from column 3
        var position = new Vector3(transform.M41, transform.M42, transform.M43);

        // Extract scale from column magnitudes
        var scaleX = MathF.Sqrt(transform.M11 * transform.M11 + transform.M21 * transform.M21 + transform.M31 * transform.M31);
        var scaleY = MathF.Sqrt(transform.M12 * transform.M12 + transform.M22 * transform.M22 + transform.M32 * transform.M32);
        var scaleZ = MathF.Sqrt(transform.M13 * transform.M13 + transform.M23 * transform.M23 + transform.M33 * transform.M33);

        // Detect negative scales by checking the sign of the dominant diagonal element
        // For 2D/UI transforms without rotation: M11 = scaleX, M22 = scaleY, M33 = scaleZ
        // For Y-rotation: M11 = cos*scaleX, M22 = scaleY, so M22 directly gives scaleY sign
        if (transform.M22 < 0) scaleY = -scaleY;
        if (transform.M11 < 0 && MathF.Abs(transform.M31) < MathF.Abs(transform.M11)) scaleX = -scaleX;
        if (transform.M33 < 0) scaleZ = -scaleZ;

        // Extract Y rotation from the rotation part (assuming no X/Z rotation)
        // The rotation matrix for Y is: [cos, 0, sin; 0, 1, 0; -sin, 0, cos]
        // After scaling: M11 = cos*scaleX, M31 = -sin*scaleX
        float rotationY = 0f;
        if (MathF.Abs(scaleX) > 0.0001f)
        {
            float cosY = transform.M11 / scaleX;
            float sinY = -transform.M31 / scaleX;
            rotationY = MathF.Atan2(sinY, cosY);
        }

        return Add(position, rotationY, new Vector3(scaleX, scaleY, scaleZ), meshId, textureId, packedColor);
    }

    /// <summary>
    /// Uploads params to GPU and dispatches compute shader to generate matrices.
    /// Call once per frame before rendering.
    /// </summary>
    public unsafe void Dispatch(CommandBuffer cmd, int frameIndex)
    {
        if (_count == 0 || _matrixGenShader == null) return;

        // Upload params to GPU
        var ptr = (InstanceParams*)_allocator.MapMemory(_paramsBuffers[frameIndex]);
        fixed (InstanceParams* src = _params)
        {
            Unsafe.CopyBlock(ptr, src, (uint)(_count * sizeof(InstanceParams)));
        }
        _allocator.UnmapMemory(_paramsBuffers[frameIndex]);

        // Host writes are outside pipeline ordering; make them visible to compute reads.
        var hostToComputeBarrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.HostWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit
        };

        Vk.CmdPipelineBarrier(
            cmd,
            PipelineStageFlags.HostBit,
            PipelineStageFlags.ComputeShaderBit,
            0,
            1, &hostToComputeBarrier,
            0, null,
            0, null);

        // Bind compute pipeline
        var pipeline = _pipelineCache.GetOrCreateCompute(_matrixGenShader, _descriptorSetLayout, sizeof(uint));
        Vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, pipeline);

        // Bind descriptor set
        var layout = _pipelineCache.GetComputePipelineLayout(_matrixGenShader);
        var set = _descriptorSets[frameIndex];
        Vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, layout, 0, 1, &set, 0, null);

        // Push constants (instance count)
        uint instanceCount = (uint)_count;
        Vk.CmdPushConstants(cmd, layout, ShaderStageFlags.ComputeBit, 0, sizeof(uint), &instanceCount);

        // Dispatch
        uint groups = ((uint)_count + 255) / 256;
        Vk.CmdDispatch(cmd, groups, 1, 1);

        // Memory barrier: compute writes → vertex shader reads
        var barrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit
        };

        Vk.CmdPipelineBarrier(
            cmd,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.VertexShaderBit,
            0,
            1, &barrier,
            0, null,
            0, null);
    }

    /// <summary>
    /// Gets the GPU transform buffer for binding to render pipeline.
    /// </summary>
    public VkBuffer GetTransformBuffer(int frameIndex) => _transformBuffers[frameIndex].Buffer;

    /// <summary>
    /// Resets the instance count for next frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => _count = 0;

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Vk.DestroyDescriptorSetLayout(Device, _descriptorSetLayout, null);
        Vk.DestroyDescriptorPool(Device, _descriptorPool, null);

        for (int i = 0; i < _framesInFlight; i++)
        {
            _allocator.FreeBuffer(_paramsBuffers[i]);
            _allocator.FreeBuffer(_transformBuffers[i]);
        }

        _log.Debug("ComputeTransforms disposed");
    }
}
