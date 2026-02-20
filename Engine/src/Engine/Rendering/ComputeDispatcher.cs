using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using DerpLib.Core;
using DerpLib.Memory;
using DerpLib.Shaders;
using PipelineCache = DerpLib.Shaders.PipelineCache;

namespace DerpLib.Rendering;

/// <summary>
/// Manages compute shader dispatch state and descriptor bindings.
/// Provides a Begin/End pattern for compute work.
/// </summary>
public sealed class ComputeDispatcher : IDisposable
{
    private readonly VkDevice _device;
    private readonly PipelineCache _pipelineCache;

    // Descriptor resources for compute
    private DescriptorPool _descriptorPool;
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorSet[] _descriptorSets;
    private const int MaxBindings = 8;  // Max storage buffers per compute dispatch
    private const int FramesInFlight = 2;

    // Current dispatch state
    private bool _isActive;
    private int _currentFrame;
    private ComputeShader? _boundShader;
    private Pipeline _boundPipeline;
    private readonly BufferAllocation[] _boundBuffers = new BufferAllocation[MaxBindings];
    private readonly bool[] _bufferBound = new bool[MaxBindings];

    private Vk Vk => _device.Vk;
    private Device Device => _device.Device;

    public ComputeDispatcher(VkDevice device, PipelineCache pipelineCache)
    {
        _device = device;
        _pipelineCache = pipelineCache;
        _descriptorSets = new DescriptorSet[FramesInFlight];
    }

    public unsafe void Initialize()
    {
        // Create descriptor pool for compute
        var poolSizes = stackalloc DescriptorPoolSize[1];
        poolSizes[0] = new DescriptorPoolSize
        {
            Type = DescriptorType.StorageBuffer,
            DescriptorCount = MaxBindings * FramesInFlight
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = FramesInFlight,
            PoolSizeCount = 1,
            PPoolSizes = poolSizes
        };

        var result = Vk.CreateDescriptorPool(Device, &poolInfo, null, out _descriptorPool);
        if (result != Result.Success)
            throw new Exception($"Failed to create compute descriptor pool: {result}");

        // Create descriptor set layout with MaxBindings storage buffers
        var bindings = stackalloc DescriptorSetLayoutBinding[MaxBindings];
        for (uint i = 0; i < MaxBindings; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            };
        }

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = MaxBindings,
            PBindings = bindings
        };

        result = Vk.CreateDescriptorSetLayout(Device, &layoutInfo, null, out _descriptorSetLayout);
        if (result != Result.Success)
            throw new Exception($"Failed to create compute descriptor set layout: {result}");

        // Allocate descriptor sets
        var layouts = stackalloc DescriptorSetLayout[FramesInFlight];
        for (int i = 0; i < FramesInFlight; i++)
            layouts[i] = _descriptorSetLayout;

        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = FramesInFlight,
            PSetLayouts = layouts
        };

        fixed (DescriptorSet* pSets = _descriptorSets)
        {
            result = Vk.AllocateDescriptorSets(Device, &allocInfo, pSets);
            if (result != Result.Success)
                throw new Exception($"Failed to allocate compute descriptor sets: {result}");
        }
    }

    /// <summary>
    /// Gets the descriptor set layout for compute pipelines.
    /// </summary>
    public DescriptorSetLayout DescriptorSetLayout => _descriptorSetLayout;

    /// <summary>
    /// Begin compute dispatch mode. Must be called before binding buffers or dispatching.
    /// </summary>
    public void Begin(int frameIndex)
    {
        if (_isActive)
            throw new InvalidOperationException("ComputeDispatcher.Begin called while already active");

        _isActive = true;
        _currentFrame = frameIndex;
        _boundShader = null;

        // Clear bound buffer state
        for (int i = 0; i < MaxBindings; i++)
            _bufferBound[i] = false;
    }

    /// <summary>
    /// Bind a storage buffer to a binding slot.
    /// </summary>
    public unsafe void BindStorageBuffer<T>(uint binding, StorageBuffer<T> buffer) where T : unmanaged
    {
        if (!_isActive)
            throw new InvalidOperationException("ComputeDispatcher.BindStorageBuffer called outside Begin/End");
        if (binding >= MaxBindings)
            throw new ArgumentOutOfRangeException(nameof(binding), $"Binding must be < {MaxBindings}");

        _boundBuffers[binding] = buffer.Allocation;
        _bufferBound[binding] = true;

        // Update descriptor set
        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = buffer.Buffer,
            Offset = 0,
            Range = buffer.Size
        };

        var writeSet = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSets[_currentFrame],
            DstBinding = binding,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            PBufferInfo = &bufferInfo
        };

        Vk.UpdateDescriptorSets(Device, 1, &writeSet, 0, null);
    }

    /// <summary>
    /// Dispatch a compute shader.
    /// </summary>
    public unsafe void Dispatch(CommandBuffer cmd, ComputeShader shader, uint groupsX, uint groupsY, uint groupsZ, uint pushConstantSize = 0)
    {
        if (!_isActive)
            throw new InvalidOperationException("ComputeDispatcher.Dispatch called outside Begin/End");

        // Get or create pipeline
        if (_boundShader != shader)
        {
            var pipeline = _pipelineCache.GetOrCreateCompute(shader, _descriptorSetLayout, pushConstantSize);
            Vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, pipeline);
            _boundPipeline = pipeline;
            _boundShader = shader;
        }

        // Bind descriptor set
        var layout = _pipelineCache.GetComputePipelineLayout(shader);
        var set = _descriptorSets[_currentFrame];
        Vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, layout, 0, 1, &set, 0, null);

        // Dispatch
        Vk.CmdDispatch(cmd, groupsX, groupsY, groupsZ);
    }

    /// <summary>
    /// Set push constants for a compute shader.
    /// Binds the pipeline if not already bound.
    /// </summary>
    public unsafe void SetPushConstants<T>(CommandBuffer cmd, ComputeShader shader, T data, uint pushConstantSize) where T : unmanaged
    {
        if (!_isActive)
            throw new InvalidOperationException("ComputeDispatcher.SetPushConstants called outside Begin/End");

        // Bind pipeline if not already bound (push constants require bound pipeline)
        if (_boundShader != shader)
        {
            var pipeline = _pipelineCache.GetOrCreateCompute(shader, _descriptorSetLayout, pushConstantSize);
            Vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, pipeline);
            _boundPipeline = pipeline;
            _boundShader = shader;
        }

        var layout = _pipelineCache.GetComputePipelineLayout(shader);
        var size = (uint)Marshal.SizeOf<T>();
        Vk.CmdPushConstants(cmd, layout, ShaderStageFlags.ComputeBit, 0, size, &data);
    }

    /// <summary>
    /// End compute dispatch mode and insert a memory barrier.
    /// The barrier ensures compute writes are visible to subsequent render/compute passes.
    /// </summary>
    public unsafe void End(CommandBuffer cmd)
    {
        if (!_isActive)
            throw new InvalidOperationException("ComputeDispatcher.End called without matching Begin");

        // Insert memory barrier: compute shader writes → vertex/fragment reads
        var memoryBarrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.VertexAttributeReadBit | AccessFlags.IndirectCommandReadBit
        };

        Vk.CmdPipelineBarrier(
            cmd,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.VertexInputBit | PipelineStageFlags.VertexShaderBit | PipelineStageFlags.DrawIndirectBit,
            0,
            1, &memoryBarrier,
            0, null,
            0, null);

        _isActive = false;
        _boundShader = null;
    }

    public unsafe void Dispose()
    {
        Vk.DestroyDescriptorSetLayout(Device, _descriptorSetLayout, null);
        Vk.DestroyDescriptorPool(Device, _descriptorPool, null);
    }
}
