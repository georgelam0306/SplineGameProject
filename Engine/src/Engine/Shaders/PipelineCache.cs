using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Core;
using DerpLib.Rendering;
using VkPipelineCache = Silk.NET.Vulkan.PipelineCache;

namespace DerpLib.Shaders;

/// <summary>
/// Manages pipeline creation with both disk caching (VkPipelineCache)
/// and runtime caching (avoid redundant creates).
/// </summary>
public sealed class PipelineCache : IDisposable
{
    private readonly ILogger _log;
    private readonly VkDevice _vkDevice;
    private readonly string? _cacheFilePath;

    private VkPipelineCache _vkCache;
    private readonly Dictionary<PipelineKey, Pipeline> _pipelines = new();
    private readonly Dictionary<int, (ShaderModule vert, ShaderModule frag, PipelineLayout layout)> _shaderResources = new();

    // Compute pipeline caching
    private readonly Dictionary<int, Pipeline> _computePipelines = new();
    private readonly Dictionary<int, (ShaderModule module, PipelineLayout layout)> _computeShaderResources = new();

    // Descriptor set layout for pipeline creation (set externally)
    private DescriptorSetLayout _descriptorSetLayout;

    private Vk Vk => _vkDevice.Vk;
    private Device Device => _vkDevice.Device;

    public PipelineCache(ILogger log, VkDevice vkDevice, string? cacheFilePath = null)
    {
        _log = log;
        _vkDevice = vkDevice;
        _cacheFilePath = cacheFilePath;
    }

    /// <summary>
    /// Sets the descriptor set layout used for all pipeline layouts.
    /// Must be called before creating any pipelines.
    /// </summary>
    public void SetDescriptorSetLayout(DescriptorSetLayout layout)
    {
        _descriptorSetLayout = layout;
    }

    public unsafe void Initialize()
    {
        byte[]? cacheData = null;

        // Try to load existing cache from disk
        if (_cacheFilePath != null && File.Exists(_cacheFilePath))
        {
            try
            {
                cacheData = File.ReadAllBytes(_cacheFilePath);
                _log.Debug("Loaded pipeline cache from disk: {Size} bytes", cacheData.Length);
            }
            catch (Exception ex)
            {
                _log.Warning("Failed to load pipeline cache: {Error}", ex.Message);
            }
        }

        // Create Vulkan pipeline cache
        fixed (byte* pData = cacheData)
        {
            var createInfo = new PipelineCacheCreateInfo
            {
                SType = StructureType.PipelineCacheCreateInfo,
                InitialDataSize = cacheData != null ? (nuint)cacheData.Length : 0,
                PInitialData = pData
            };

            var result = Vk.CreatePipelineCache(Device, &createInfo, null, out _vkCache);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create pipeline cache: {result}");
            }
        }

        _log.Information("PipelineCache initialized");
    }

    /// <summary>
    /// Gets the pipeline layout for a shader (needed for CmdPushConstants).
    /// </summary>
    public PipelineLayout GetPipelineLayout(Shader shader)
    {
        if (_shaderResources.TryGetValue(shader.Id, out var resources))
        {
            return resources.layout;
        }
        throw new InvalidOperationException($"Shader {shader.Id} not yet loaded. Call GetOrCreate first.");
    }

    /// <summary>
    /// Gets or creates a pipeline for the given shader + render state combination.
    /// </summary>
    public Pipeline GetOrCreate(Shader shader, RenderState state, Silk.NET.Vulkan.RenderPass renderPass, Extent2D extent)
    {
        var key = state.ToKey(shader);
        key.RenderPassHandle = renderPass.Handle;

        if (_pipelines.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var pipeline = CreatePipeline(shader, state, renderPass, extent);
        _pipelines[key] = pipeline;

        _log.Debug("Created pipeline for shader {ShaderId}, blend={Blend}. Cache size: {Count}",
            shader.Id, state.BlendMode, _pipelines.Count);
        return pipeline;
    }

    private unsafe Pipeline CreatePipeline(Shader shader, RenderState state, Silk.NET.Vulkan.RenderPass renderPass, Extent2D extent)
    {
        // Get or create shader modules and layout for this shader
        var (vertModule, fragModule, pipelineLayout) = GetOrCreateShaderResources(shader);

        var entryPoint = "main"u8;
        fixed (byte* pEntryPoint = entryPoint)
        {
            // Shader stages
            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertModule,
                PName = pEntryPoint
            };
            shaderStages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragModule,
                PName = pEntryPoint
            };

            // Vertex input - Vertex3D (binding 0) + InstanceData (binding 1)
            var bindingDescs = stackalloc VertexInputBindingDescription[2];
            bindingDescs[0] = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = Vertex3D.SizeInBytes,
                InputRate = VertexInputRate.Vertex
            };
            bindingDescs[1] = new VertexInputBindingDescription
            {
                Binding = 1,
                Stride = 16,  // sizeof(InstanceData)
                InputRate = VertexInputRate.Instance
            };

            var attributeDescs = stackalloc VertexInputAttributeDescription[7];
            // Vertex3D attributes (binding 0)
            attributeDescs[0] = new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 0,
                Format = Format.R32G32B32Sfloat,  // Position (vec3)
                Offset = 0
            };
            attributeDescs[1] = new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 1,
                Format = Format.R32G32B32Sfloat,  // Normal (vec3)
                Offset = 12
            };
            attributeDescs[2] = new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 2,
                Format = Format.R32G32Sfloat,  // TexCoord (vec2)
                Offset = 24
            };
            // InstanceData attributes (binding 1)
            attributeDescs[3] = new VertexInputAttributeDescription
            {
                Binding = 1,
                Location = 3,
                Format = Format.R32Uint,  // TransformIndex
                Offset = 0
            };
            attributeDescs[4] = new VertexInputAttributeDescription
            {
                Binding = 1,
                Location = 4,
                Format = Format.R32Uint,  // TextureIndex
                Offset = 4
            };
            attributeDescs[5] = new VertexInputAttributeDescription
            {
                Binding = 1,
                Location = 5,
                Format = Format.R32Uint,  // PackedColor
                Offset = 8
            };
            attributeDescs[6] = new VertexInputAttributeDescription
            {
                Binding = 1,
                Location = 6,
                Format = Format.R32Uint,  // PackedUVOffset
                Offset = 12
            };

            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 2,
                PVertexBindingDescriptions = bindingDescs,
                VertexAttributeDescriptionCount = 7,
                PVertexAttributeDescriptions = attributeDescs
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = state.Topology,
                PrimitiveRestartEnable = false
            };

            // Dynamic viewport and scissor
            var dynamicStates = stackalloc DynamicState[2];
            dynamicStates[0] = DynamicState.Viewport;
            dynamicStates[1] = DynamicState.Scissor;

            var dynamicState = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates
            };

            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1.0f,
                CullMode = state.CullMode,
                FrontFace = FrontFace.CounterClockwise,
                DepthBiasEnable = false
            };

            var multisampling = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                SampleShadingEnable = false,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };

            // Depth stencil state
            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = state.DepthTestEnabled,
                DepthWriteEnable = state.DepthWriteEnabled,
                DepthCompareOp = CompareOp.Less,  // Closer objects pass
                DepthBoundsTestEnable = false,
                StencilTestEnable = false
            };

            // Blend mode
            var (blendEnable, srcColor, dstColor, srcAlpha, dstAlpha) = GetBlendFactors(state.BlendMode);

            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = blendEnable,
                SrcColorBlendFactor = srcColor,
                DstColorBlendFactor = dstColor,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = srcAlpha,
                DstAlphaBlendFactor = dstAlpha,
                AlphaBlendOp = BlendOp.Add
            };

            var colorBlending = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment
            };

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = shaderStages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisampling,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &colorBlending,
                PDynamicState = &dynamicState,
                Layout = pipelineLayout,
                RenderPass = renderPass,
                Subpass = 0
            };

            Pipeline pipeline;
            var result = Vk.CreateGraphicsPipelines(Device, _vkCache, 1, &pipelineInfo, null, &pipeline);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create graphics pipeline: {result}");
            }

            return pipeline;
        }
    }

    private static (bool enable, BlendFactor srcColor, BlendFactor dstColor, BlendFactor srcAlpha, BlendFactor dstAlpha) GetBlendFactors(BlendMode mode)
    {
        return mode switch
        {
            BlendMode.None => (false, BlendFactor.One, BlendFactor.Zero, BlendFactor.One, BlendFactor.Zero),
            BlendMode.Alpha => (true, BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendFactor.One, BlendFactor.OneMinusSrcAlpha),
            BlendMode.Additive => (true, BlendFactor.SrcAlpha, BlendFactor.One, BlendFactor.One, BlendFactor.One),
            BlendMode.Multiply => (true, BlendFactor.DstColor, BlendFactor.OneMinusSrcAlpha, BlendFactor.DstAlpha, BlendFactor.OneMinusSrcAlpha),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private unsafe (ShaderModule vert, ShaderModule frag, PipelineLayout layout) GetOrCreateShaderResources(Shader shader)
    {
        if (_shaderResources.TryGetValue(shader.Id, out var existing))
        {
            return existing;
        }

        var vertModule = CreateShaderModule(shader.VertexBytes);
        var fragModule = CreateShaderModule(shader.FragmentBytes);

        // Push constant range: mat4 projection (64 bytes) for vertex shader
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = 64  // sizeof(Matrix4x4)
        };

        // Include descriptor set layout (set by Engine)
        var setLayout = _descriptorSetLayout;

        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
        };

        PipelineLayout pipelineLayout;
        var result = Vk.CreatePipelineLayout(Device, &layoutInfo, null, &pipelineLayout);
        if (result != Result.Success)
        {
            throw new Exception($"Failed to create pipeline layout: {result}");
        }

        _shaderResources[shader.Id] = (vertModule, fragModule, pipelineLayout);
        return (vertModule, fragModule, pipelineLayout);
    }

    private unsafe ShaderModule CreateShaderModule(byte[] code)
    {
        fixed (byte* pCode = code)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)pCode
            };

            ShaderModule module;
            var result = Vk.CreateShaderModule(Device, &createInfo, null, &module);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create shader module: {result}");
            }

            return module;
        }
    }

    #region Compute Pipelines

    /// <summary>
    /// Gets the pipeline layout for a compute shader.
    /// </summary>
    public PipelineLayout GetComputePipelineLayout(ComputeShader shader)
    {
        if (_computeShaderResources.TryGetValue(shader.Id, out var resources))
        {
            return resources.layout;
        }
        throw new InvalidOperationException($"Compute shader {shader.Id} not yet loaded. Call GetOrCreateCompute first.");
    }

    /// <summary>
    /// Gets or creates a compute pipeline for the given shader.
    /// </summary>
    public Pipeline GetOrCreateCompute(ComputeShader shader, DescriptorSetLayout descriptorLayout, uint pushConstantSize = 0)
    {
        if (_computePipelines.TryGetValue(shader.Id, out var existing))
        {
            return existing;
        }

        var pipeline = CreateComputePipeline(shader, descriptorLayout, pushConstantSize);
        _computePipelines[shader.Id] = pipeline;

        _log.Debug("Created compute pipeline for shader {ShaderId}. Cache size: {Count}",
            shader.Id, _computePipelines.Count);
        return pipeline;
    }

    private unsafe Pipeline CreateComputePipeline(ComputeShader shader, DescriptorSetLayout descriptorLayout, uint pushConstantSize)
    {
        var (module, layout) = GetOrCreateComputeShaderResources(shader, descriptorLayout, pushConstantSize);

        var entryPoint = "main"u8;
        fixed (byte* pEntryPoint = entryPoint)
        {
            var stageInfo = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = module,
                PName = pEntryPoint
            };

            var pipelineInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = stageInfo,
                Layout = layout
            };

            Pipeline pipeline;
            var result = Vk.CreateComputePipelines(Device, _vkCache, 1, &pipelineInfo, null, &pipeline);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create compute pipeline: {result}");
            }

            return pipeline;
        }
    }

    private unsafe (ShaderModule module, PipelineLayout layout) GetOrCreateComputeShaderResources(
        ComputeShader shader, DescriptorSetLayout descriptorLayout, uint pushConstantSize)
    {
        if (_computeShaderResources.TryGetValue(shader.Id, out var existing))
        {
            return existing;
        }

        var module = CreateShaderModule(shader.Bytes);

        // Create pipeline layout
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &descriptorLayout
        };

        // Optional push constants
        PushConstantRange pushConstantRange = default;
        if (pushConstantSize > 0)
        {
            pushConstantRange = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.ComputeBit,
                Offset = 0,
                Size = pushConstantSize
            };
            layoutInfo.PushConstantRangeCount = 1;
            layoutInfo.PPushConstantRanges = &pushConstantRange;
        }

        PipelineLayout pipelineLayout;
        var result = Vk.CreatePipelineLayout(Device, &layoutInfo, null, &pipelineLayout);
        if (result != Result.Success)
        {
            throw new Exception($"Failed to create compute pipeline layout: {result}");
        }

        _computeShaderResources[shader.Id] = (module, pipelineLayout);
        return (module, pipelineLayout);
    }

    #endregion

    /// <summary>
    /// Saves the pipeline cache to disk for faster loading next time.
    /// </summary>
    public unsafe void SaveToDisk()
    {
        if (_cacheFilePath == null) return;

        try
        {
            nuint dataSize = 0;
            Vk.GetPipelineCacheData(Device, _vkCache, &dataSize, null);

            var data = new byte[dataSize];
            fixed (byte* pData = data)
            {
                Vk.GetPipelineCacheData(Device, _vkCache, &dataSize, pData);
            }

            var dir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(_cacheFilePath, data);
            _log.Debug("Saved pipeline cache to disk: {Size} bytes", data.Length);
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to save pipeline cache: {Error}", ex.Message);
        }
    }

    public unsafe void Dispose()
    {
        SaveToDisk();

        foreach (var (_, pipeline) in _pipelines)
        {
            Vk.DestroyPipeline(Device, pipeline, null);
        }

        foreach (var (_, (vert, frag, layout)) in _shaderResources)
        {
            Vk.DestroyShaderModule(Device, vert, null);
            Vk.DestroyShaderModule(Device, frag, null);
            Vk.DestroyPipelineLayout(Device, layout, null);
        }

        // Cleanup compute resources
        foreach (var (_, pipeline) in _computePipelines)
        {
            Vk.DestroyPipeline(Device, pipeline, null);
        }

        foreach (var (_, (module, layout)) in _computeShaderResources)
        {
            Vk.DestroyShaderModule(Device, module, null);
            Vk.DestroyPipelineLayout(Device, layout, null);
        }

        Vk.DestroyPipelineCache(Device, _vkCache, null);
    }
}
