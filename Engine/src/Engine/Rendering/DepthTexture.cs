using Silk.NET.Vulkan;
using DerpLib.Core;
using VkRenderPass = Silk.NET.Vulkan.RenderPass;

namespace DerpLib.Rendering;

/// <summary>
/// Depth-only render target for shadow maps.
/// Uses comparison sampler for shadow testing.
/// </summary>
public sealed class DepthTexture : IDisposable
{
    private readonly VkDevice _device;

    private Image _image;
    private DeviceMemory _memory;
    private ImageView _imageView;
    private Framebuffer _framebuffer;
    private VkRenderPass _renderPass;
    private Sampler _sampler;

    private readonly uint _width;
    private readonly uint _height;
    private readonly Format _format;
    private bool _disposed;

    public uint Width => _width;
    public uint Height => _height;
    public ImageView ImageView => _imageView;
    public Framebuffer Framebuffer => _framebuffer;
    public VkRenderPass RenderPass => _renderPass;
    public Sampler Sampler => _sampler;
    public Extent2D Extent => new(_width, _height);

    private Vk Vk => _device.Vk;
    private Device Device => _device.Device;

    public DepthTexture(VkDevice device, uint width, uint height, Format format)
    {
        _device = device;
        _width = width;
        _height = height;
        _format = format;

        CreateImage();
        CreateRenderPass();
        CreateFramebuffer();
        CreateSampler();
    }

    private unsafe void CreateImage()
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = _format,
            Extent = new Extent3D(_width, _height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        var result = Vk.CreateImage(Device, &imageInfo, null, out _image);
        if (result != Result.Success)
            throw new Exception($"Failed to create depth texture image: {result}");

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
            throw new Exception($"Failed to allocate depth texture memory: {result}");

        Vk.BindImageMemory(Device, _image, _memory, 0);

        // Create image view
        var viewInfo = new ImageViewCreateInfo
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
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        result = Vk.CreateImageView(Device, &viewInfo, null, out _imageView);
        if (result != Result.Success)
            throw new Exception($"Failed to create depth texture image view: {result}");
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
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal  // Ready for shadow sampling
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
            throw new Exception($"Failed to create depth texture render pass: {result}");
    }

    private unsafe void CreateFramebuffer()
    {
        var attachment = _imageView;

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

        var result = Vk.CreateFramebuffer(Device, &createInfo, null, out _framebuffer);
        if (result != Result.Success)
            throw new Exception($"Failed to create depth texture framebuffer: {result}");
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
            CompareOp = CompareOp.LessOrEqual,  // Shadow test: is fragment closer than shadow map?
            MinLod = 0,
            MaxLod = 0,
            BorderColor = BorderColor.FloatOpaqueWhite,  // Outside shadow map = lit
            UnnormalizedCoordinates = false
        };

        var result = Vk.CreateSampler(Device, &samplerInfo, null, out _sampler);
        if (result != Result.Success)
            throw new Exception($"Failed to create depth texture sampler: {result}");
    }

    /// <summary>
    /// Begin rendering to this depth texture.
    /// </summary>
    public unsafe void BeginRenderPass(CommandBuffer cmd)
    {
        var clearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } };

        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffer,
            RenderArea = new Rect2D { Offset = default, Extent = new Extent2D(_width, _height) },
            ClearValueCount = 1,
            PClearValues = &clearValue
        };

        Vk.CmdBeginRenderPass(cmd, &beginInfo, SubpassContents.Inline);
    }

    /// <summary>
    /// End rendering to this depth texture.
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
        Vk.DestroyFramebuffer(Device, _framebuffer, null);
        Vk.DestroyRenderPass(Device, _renderPass, null);
        Vk.DestroyImageView(Device, _imageView, null);
        Vk.DestroyImage(Device, _image, null);
        Vk.FreeMemory(Device, _memory, null);
    }
}
