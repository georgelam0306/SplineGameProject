using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Core;
using VkRenderPass = Silk.NET.Vulkan.RenderPass;

namespace DerpLib.Rendering;

/// <summary>
/// Manages render passes for 3D and 2D rendering.
/// - DepthPass: For 3D rendering with depth buffer, clears color and depth
/// - ColorPass: For 2D rendering, loads previous color, no depth
/// </summary>
public sealed class RenderPassManager : IDisposable
{
    private readonly ILogger _log;
    private readonly VkDevice _vkDevice;

    // Two render passes
    private VkRenderPass _depthPass;   // 3D: color + depth, clears both
    private VkRenderPass _colorPass;   // 2D: color only, loads previous

    // Framebuffers
    private Framebuffer[] _depthFramebuffers = [];  // For 3D pass (color + depth)
    private Framebuffer[] _colorFramebuffers = [];  // For 2D pass (color only)

    // Depth buffer resources (per swapchain image to avoid cross-frame contention)
    private Image[] _depthImages = [];
    private DeviceMemory[] _depthMemories = [];
    private ImageView[] _depthImageViews = [];
    private Format _depthFormat;

    private Format _colorFormat;
    private Extent2D _extent;
    private bool _disposed;

    private Vk Vk => _vkDevice.Vk;
    private Device Device => _vkDevice.Device;

    /// <summary>3D render pass with color + depth.</summary>
    public VkRenderPass DepthPass => _depthPass;

    /// <summary>2D render pass with color only.</summary>
    public VkRenderPass ColorPass => _colorPass;

    /// <summary>Current render extent.</summary>
    public Extent2D Extent => _extent;

    /// <summary>The depth format being used.</summary>
    public Format DepthFormat => _depthFormat;

    public RenderPassManager(ILogger log, VkDevice vkDevice)
    {
        _log = log;
        _vkDevice = vkDevice;
    }

    /// <summary>
    /// Initialize render passes and depth buffer.
    /// </summary>
    public unsafe void Initialize(Format colorFormat, ImageView[] swapchainImageViews, Extent2D extent)
    {
        _colorFormat = colorFormat;
        _extent = extent;

        // Find best depth format
        _depthFormat = FindDepthFormat();

        // Create both render passes
        CreateDepthPass(colorFormat);
        CreateColorPass(colorFormat);

        // Create depth buffers
        CreateDepthResources(swapchainImageViews.Length, extent);

        // Create framebuffers for both passes
        CreateDepthFramebuffers(swapchainImageViews, extent);
        CreateColorFramebuffers(swapchainImageViews, extent);

        _log.Information("RenderPassManager initialized: {Format}, depth={DepthFormat}, {Width}x{Height}",
            colorFormat, _depthFormat, extent.Width, extent.Height);
    }

    /// <summary>
    /// Recreate framebuffers and depth buffer on resize.
    /// </summary>
    public unsafe void Resize(ImageView[] swapchainImageViews, Extent2D extent)
    {
        _extent = extent;

        // Destroy old resources
        DestroyFramebuffers();
        DestroyDepthResources();

        // Recreate
        CreateDepthResources(swapchainImageViews.Length, extent);
        CreateDepthFramebuffers(swapchainImageViews, extent);
        CreateColorFramebuffers(swapchainImageViews, extent);

        _log.Debug("RenderPassManager resized: {Width}x{Height}", extent.Width, extent.Height);
    }

    private unsafe Format FindDepthFormat()
    {
        Format[] candidates = [Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint];

        foreach (var format in candidates)
        {
            FormatProperties props;
            Vk.GetPhysicalDeviceFormatProperties(_vkDevice.PhysicalDevice, format, &props);

            if ((props.OptimalTilingFeatures & FormatFeatureFlags.DepthStencilAttachmentBit) != 0)
            {
                return format;
            }
        }

        return Format.D32Sfloat; // Fallback
    }

    /// <summary>
    /// Create the 3D render pass (clears color + depth).
    /// Color final layout is ColorAttachmentOptimal so 2D pass can follow.
    /// </summary>
    private unsafe void CreateDepthPass(Format colorFormat)
    {
        // Color attachment - clears, stores for 2D pass to load
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ColorAttachmentOptimal  // 2D pass follows
        };

        // Depth attachment
        var depthAttachment = new AttachmentDescription
        {
            Format = _depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var colorRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        var depthRef = new AttachmentReference
        {
            Attachment = 1,
            Layout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PDepthStencilAttachment = &depthRef
        };

        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
        };

        // Ensure depth pass writes are visible to subsequent passes that load color.
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit
        };

        var attachments = stackalloc AttachmentDescription[2] { colorAttachment, depthAttachment };

        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies
        };

        var result = Vk.CreateRenderPass(Device, &createInfo, null, out _depthPass);
        if (result != Result.Success)
            throw new Exception($"Failed to create depth pass: {result}");
    }

    /// <summary>
    /// Create the 2D render pass (loads previous color, no depth).
    /// Color final layout is PresentSrcKhr for presentation.
    /// </summary>
    private unsafe void CreateColorPass(Format colorFormat)
    {
        // Color attachment - loads previous content (3D), stores for present
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,  // Preserve 3D content
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,  // From 3D pass
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        var colorRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef
            // No depth attachment
        };

        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit
        };

        // Ensure color pass writes are visible to presentation.
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.BottomOfPipeBit,
            DstAccessMask = AccessFlags.MemoryReadBit
        };

        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies
        };

        var result = Vk.CreateRenderPass(Device, &createInfo, null, out _colorPass);
        if (result != Result.Success)
            throw new Exception($"Failed to create color pass: {result}");
    }

    private unsafe void CreateDepthResources(int swapchainImageCount, Extent2D extent)
    {
        if (swapchainImageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(swapchainImageCount));
        }

        _depthImages = new Image[swapchainImageCount];
        _depthMemories = new DeviceMemory[swapchainImageCount];
        _depthImageViews = new ImageView[swapchainImageCount];

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = _depthFormat,
            Extent = new Extent3D(extent.Width, extent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            ViewType = ImageViewType.Type2D,
            Format = _depthFormat,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.DepthBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        for (int imageIndex = 0; imageIndex < swapchainImageCount; imageIndex++)
        {
            var result = Vk.CreateImage(Device, &imageInfo, null, out _depthImages[imageIndex]);
            if (result != Result.Success)
                throw new Exception($"Failed to create depth image {imageIndex}: {result}");

            MemoryRequirements memReqs;
            Vk.GetImageMemoryRequirements(Device, _depthImages[imageIndex], &memReqs);

            var memoryTypeIndex = _vkDevice.Capabilities.FindMemoryType(Vk, _vkDevice.PhysicalDevice, memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);

            var allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memReqs.Size,
                MemoryTypeIndex = memoryTypeIndex
            };

            result = Vk.AllocateMemory(Device, &allocInfo, null, out _depthMemories[imageIndex]);
            if (result != Result.Success)
                throw new Exception($"Failed to allocate depth image memory {imageIndex}: {result}");

            Vk.BindImageMemory(Device, _depthImages[imageIndex], _depthMemories[imageIndex], 0);

            viewInfo.Image = _depthImages[imageIndex];
            result = Vk.CreateImageView(Device, &viewInfo, null, out _depthImageViews[imageIndex]);
            if (result != Result.Success)
                throw new Exception($"Failed to create depth image view {imageIndex}: {result}");
        }

        _log.Debug("Created {Count} depth buffers: {Width}x{Height}, {Format}",
            _depthImages.Length, extent.Width, extent.Height, _depthFormat);
    }

    private unsafe void CreateDepthFramebuffers(ImageView[] imageViews, Extent2D extent)
    {
        _depthFramebuffers = new Framebuffer[imageViews.Length];
        var attachments = stackalloc ImageView[2];

        for (int i = 0; i < imageViews.Length; i++)
        {
            attachments[0] = imageViews[i];
            attachments[1] = _depthImageViews[i];

            var createInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _depthPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = extent.Width,
                Height = extent.Height,
                Layers = 1
            };

            var result = Vk.CreateFramebuffer(Device, &createInfo, null, out _depthFramebuffers[i]);
            if (result != Result.Success)
                throw new Exception($"Failed to create depth framebuffer {i}: {result}");
        }

        _log.Debug("Created {Count} depth framebuffers: {Width}x{Height}",
            _depthFramebuffers.Length, extent.Width, extent.Height);
    }

    private unsafe void CreateColorFramebuffers(ImageView[] imageViews, Extent2D extent)
    {
        _colorFramebuffers = new Framebuffer[imageViews.Length];

        for (int i = 0; i < imageViews.Length; i++)
        {
            var attachment = imageViews[i];

            var createInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _colorPass,
                AttachmentCount = 1,
                PAttachments = &attachment,
                Width = extent.Width,
                Height = extent.Height,
                Layers = 1
            };

            var result = Vk.CreateFramebuffer(Device, &createInfo, null, out _colorFramebuffers[i]);
            if (result != Result.Success)
                throw new Exception($"Failed to create color framebuffer {i}: {result}");
        }

        _log.Debug("Created {Count} color framebuffers: {Width}x{Height}",
            _colorFramebuffers.Length, extent.Width, extent.Height);
    }

    /// <summary>
    /// Begin the 3D render pass (clears color and depth).
    /// Returns the render pass for pipeline creation.
    /// </summary>
    public unsafe VkRenderPass BeginDepthPass(CommandBuffer cmd, int framebufferIndex, float r, float g, float b)
    {
        var clearValues = stackalloc ClearValue[2];
        clearValues[0].Color = new ClearColorValue { Float32_0 = r, Float32_1 = g, Float32_2 = b, Float32_3 = 1f };
        clearValues[1].DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 };

        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _depthPass,
            Framebuffer = _depthFramebuffers[framebufferIndex],
            RenderArea = new Rect2D { Offset = default, Extent = _extent },
            ClearValueCount = 2,
            PClearValues = clearValues
        };

        Vk.CmdBeginRenderPass(cmd, &beginInfo, SubpassContents.Inline);
        return _depthPass;
    }

    /// <summary>
    /// Begin the 2D render pass (loads previous color, no depth).
    /// Returns the render pass for pipeline creation.
    /// </summary>
    public unsafe VkRenderPass BeginColorPass(CommandBuffer cmd, int framebufferIndex)
    {
        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _colorPass,
            Framebuffer = _colorFramebuffers[framebufferIndex],
            RenderArea = new Rect2D { Offset = default, Extent = _extent },
            ClearValueCount = 0,
            PClearValues = null
        };

        Vk.CmdBeginRenderPass(cmd, &beginInfo, SubpassContents.Inline);
        return _colorPass;
    }

    /// <summary>
    /// End the current render pass.
    /// </summary>
    public void EndPass(CommandBuffer cmd)
    {
        Vk.CmdEndRenderPass(cmd);
    }

    private unsafe void DestroyFramebuffers()
    {
        foreach (var fb in _depthFramebuffers)
            Vk.DestroyFramebuffer(Device, fb, null);
        _depthFramebuffers = [];

        foreach (var fb in _colorFramebuffers)
            Vk.DestroyFramebuffer(Device, fb, null);
        _colorFramebuffers = [];
    }

    private unsafe void DestroyDepthResources()
    {
        for (int imageIndex = 0; imageIndex < _depthImageViews.Length; imageIndex++)
        {
            if (_depthImageViews[imageIndex].Handle != 0)
            {
                Vk.DestroyImageView(Device, _depthImageViews[imageIndex], null);
            }

            if (_depthImages[imageIndex].Handle != 0)
            {
                Vk.DestroyImage(Device, _depthImages[imageIndex], null);
            }

            if (_depthMemories[imageIndex].Handle != 0)
            {
                Vk.FreeMemory(Device, _depthMemories[imageIndex], null);
            }
        }

        _depthImageViews = [];
        _depthImages = [];
        _depthMemories = [];
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DestroyFramebuffers();
        DestroyDepthResources();

        Vk.DestroyRenderPass(Device, _depthPass, null);
        Vk.DestroyRenderPass(Device, _colorPass, null);

        _log.Debug("RenderPassManager disposed");
    }
}
