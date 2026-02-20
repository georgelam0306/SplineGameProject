using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Core;

namespace DerpLib.Rendering;

public sealed class RenderPass : IDisposable
{
    private readonly ILogger _log;
    private readonly VkDevice _vkDevice;

    private Silk.NET.Vulkan.RenderPass _renderPass;
    private Framebuffer[] _framebuffers = [];
    private Extent2D _extent;

    private Vk Vk => _vkDevice.Vk;
    private Device Device => _vkDevice.Device;

    public Silk.NET.Vulkan.RenderPass Handle => _renderPass;
    public Framebuffer[] Framebuffers => _framebuffers;
    public Extent2D Extent => _extent;

    public RenderPass(ILogger log, VkDevice vkDevice)
    {
        _log = log;
        _vkDevice = vkDevice;
    }

    public unsafe void Initialize(Format colorFormat)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,     // Clear on begin (we'll switch to Load + manual clear later)
            StoreOp = AttachmentStoreOp.Store,   // Keep results
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr  // Ready for presentation
        };

        var colorAttachmentRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef
        };

        // Dependency: external commands must finish before we write color
        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };

        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        var result = Vk.CreateRenderPass(Device, &createInfo, null, out _renderPass);
        if (result != Result.Success)
        {
            throw new Exception($"Failed to create render pass: {result}");
        }

        _log.Information("RenderPass created: {Format}", colorFormat);
    }

    public unsafe void CreateFramebuffers(ImageView[] imageViews, Extent2D extent)
    {
        _extent = extent;

        // Destroy old framebuffers if any
        DestroyFramebuffers();

        _framebuffers = new Framebuffer[imageViews.Length];

        for (int i = 0; i < imageViews.Length; i++)
        {
            var attachment = imageViews[i];

            var createInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 1,
                PAttachments = &attachment,
                Width = extent.Width,
                Height = extent.Height,
                Layers = 1
            };

            var result = Vk.CreateFramebuffer(Device, &createInfo, null, out _framebuffers[i]);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create framebuffer {i}: {result}");
            }
        }

        _log.Information("Created {Count} framebuffers: {Width}x{Height}",
            _framebuffers.Length, extent.Width, extent.Height);
    }

    public unsafe void Begin(CommandBuffer commandBuffer, int framebufferIndex, ClearValue? clearValue = null)
    {
        var clearVal = clearValue ?? new ClearValue
        {
            Color = new ClearColorValue { Float32_0 = 0, Float32_1 = 0, Float32_2 = 0, Float32_3 = 1 }
        };

        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffers[framebufferIndex],
            RenderArea = new Rect2D
            {
                Offset = new Offset2D { X = 0, Y = 0 },
                Extent = _extent
            },
            ClearValueCount = 1,
            PClearValues = &clearVal
        };

        Vk.CmdBeginRenderPass(commandBuffer, &beginInfo, SubpassContents.Inline);
    }

    public void End(CommandBuffer commandBuffer)
    {
        Vk.CmdEndRenderPass(commandBuffer);
    }

    private unsafe void DestroyFramebuffers()
    {
        foreach (var fb in _framebuffers)
        {
            Vk.DestroyFramebuffer(Device, fb, null);
        }
        _framebuffers = [];
    }

    public unsafe void Dispose()
    {
        DestroyFramebuffers();
        Vk.DestroyRenderPass(Device, _renderPass, null);
    }
}
