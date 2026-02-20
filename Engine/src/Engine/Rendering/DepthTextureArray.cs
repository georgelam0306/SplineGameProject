using Silk.NET.Vulkan;
using DerpLib.Core;
using VkRenderPass = Silk.NET.Vulkan.RenderPass;

namespace DerpLib.Rendering;

/// <summary>
/// Array of depth textures with comparison sampler.
/// For shadow cascades, point light shadows, virtual shadow maps.
/// Each layer can be rendered to separately.
/// </summary>
public sealed class DepthTextureArray : IDisposable
{
    private readonly VkDevice _device;

    private Image _image;
    private DeviceMemory _memory;
    private ImageView _arrayView;          // View of entire array for sampling
    private ImageView[] _layerViews;       // Per-layer views for rendering
    private Framebuffer[] _framebuffers;   // Per-layer framebuffers
    private VkRenderPass _renderPass;
    private Sampler _sampler;

    private readonly uint _width;
    private readonly uint _height;
    private readonly uint _layers;
    private readonly Format _format;
    private bool _disposed;

    public uint Width => _width;
    public uint Height => _height;
    public uint Layers => _layers;
    public ImageView ArrayView => _arrayView;
    public VkRenderPass RenderPass => _renderPass;
    public Sampler Sampler => _sampler;
    public Extent2D Extent => new(_width, _height);

    /// <summary>
    /// Get the framebuffer for a specific layer.
    /// </summary>
    public Framebuffer GetFramebuffer(int layer) => _framebuffers[layer];

    /// <summary>
    /// Get the image view for a specific layer.
    /// </summary>
    public ImageView GetLayerView(int layer) => _layerViews[layer];

    private Vk Vk => _device.Vk;
    private Device Device => _device.Device;

    public DepthTextureArray(VkDevice device, uint width, uint height, uint layers, Format format)
    {
        _device = device;
        _width = width;
        _height = height;
        _layers = layers;
        _format = format;

        _layerViews = new ImageView[layers];
        _framebuffers = new Framebuffer[layers];

        CreateImage();
        CreateRenderPass();
        CreateFramebuffers();
        CreateSampler();
    }

    private unsafe void CreateImage()
    {
        // Create 2D array image
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = _format,
            Extent = new Extent3D(_width, _height, 1),
            MipLevels = 1,
            ArrayLayers = _layers,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        var result = Vk.CreateImage(Device, &imageInfo, null, out _image);
        if (result != Result.Success)
            throw new Exception($"Failed to create depth texture array image: {result}");

        // Allocate memory
        MemoryRequirements memReqs;
        Vk.GetImageMemoryRequirements(Device, _image, &memReqs);

        var memoryTypeIndex = _device.Capabilities.FindMemoryType(
            Vk, _device.PhysicalDevice, memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = memoryTypeIndex
        };

        result = Vk.AllocateMemory(Device, &allocInfo, null, out _memory);
        if (result != Result.Success)
            throw new Exception($"Failed to allocate depth texture array memory: {result}");

        Vk.BindImageMemory(Device, _image, _memory, 0);

        // Create array view (for sampling entire array)
        var arrayViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2DArray,
            Format = _format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.DepthBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = _layers
            }
        };

        result = Vk.CreateImageView(Device, &arrayViewInfo, null, out _arrayView);
        if (result != Result.Success)
            throw new Exception($"Failed to create depth texture array view: {result}");

        // Create per-layer views (for rendering)
        for (uint i = 0; i < _layers; i++)
        {
            var layerViewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _image,
                ViewType = ImageViewType.Type2D,
                Format = _format,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.DepthBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = i,
                    LayerCount = 1
                }
            };

            result = Vk.CreateImageView(Device, &layerViewInfo, null, out _layerViews[i]);
            if (result != Result.Success)
                throw new Exception($"Failed to create depth texture layer view {i}: {result}");
        }
    }

    private unsafe void CreateRenderPass()
    {
        var depthAttachment = new AttachmentDescription
        {
            Format = _format,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        var depthRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 0,
            PDepthStencilAttachment = &depthRef
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
            DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit
        };

        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &depthAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        var result = Vk.CreateRenderPass(Device, &createInfo, null, out _renderPass);
        if (result != Result.Success)
            throw new Exception($"Failed to create depth texture array render pass: {result}");
    }

    private unsafe void CreateFramebuffers()
    {
        for (uint i = 0; i < _layers; i++)
        {
            var attachment = _layerViews[i];

            var createInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 1,
                PAttachments = &attachment,
                Width = _width,
                Height = _height,
                Layers = 1
            };

            var result = Vk.CreateFramebuffer(Device, &createInfo, null, out _framebuffers[i]);
            if (result != Result.Success)
                throw new Exception($"Failed to create depth texture array framebuffer {i}: {result}");
        }
    }

    private unsafe void CreateSampler()
    {
        // Comparison sampler for shadow testing
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToBorder,
            AddressModeV = SamplerAddressMode.ClampToBorder,
            AddressModeW = SamplerAddressMode.ClampToBorder,
            MipLodBias = 0,
            AnisotropyEnable = false,
            MaxAnisotropy = 1,
            CompareEnable = true,
            CompareOp = CompareOp.LessOrEqual,
            MinLod = 0,
            MaxLod = 0,
            BorderColor = BorderColor.FloatOpaqueWhite,
            UnnormalizedCoordinates = false
        };

        var result = Vk.CreateSampler(Device, &samplerInfo, null, out _sampler);
        if (result != Result.Success)
            throw new Exception($"Failed to create depth texture array sampler: {result}");
    }

    /// <summary>
    /// Begin rendering to a specific layer.
    /// </summary>
    public unsafe void BeginRenderPass(CommandBuffer cmd, int layer)
    {
        if (layer < 0 || layer >= _layers)
            throw new ArgumentOutOfRangeException(nameof(layer));

        var clearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } };

        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffers[layer],
            RenderArea = new Rect2D { Offset = default, Extent = new Extent2D(_width, _height) },
            ClearValueCount = 1,
            PClearValues = &clearValue
        };

        Vk.CmdBeginRenderPass(cmd, &beginInfo, SubpassContents.Inline);
    }

    /// <summary>
    /// End rendering to the current layer.
    /// </summary>
    public void EndRenderPass(CommandBuffer cmd)
    {
        Vk.CmdEndRenderPass(cmd);
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Vk.DestroySampler(Device, _sampler, null);

        foreach (var fb in _framebuffers)
            Vk.DestroyFramebuffer(Device, fb, null);

        foreach (var view in _layerViews)
            Vk.DestroyImageView(Device, view, null);

        Vk.DestroyRenderPass(Device, _renderPass, null);
        Vk.DestroyImageView(Device, _arrayView, null);
        Vk.DestroyImage(Device, _image, null);
        Vk.FreeMemory(Device, _memory, null);
    }
}
