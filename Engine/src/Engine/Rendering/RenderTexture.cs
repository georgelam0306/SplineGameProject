using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Core;
using DerpLib.Memory;
using VkRenderPass = Silk.NET.Vulkan.RenderPass;

namespace DerpLib.Rendering;

/// <summary>
/// Offscreen render target with color and depth attachments.
/// Use for portals, mirrors, offscreen 3D rendering, and post-processing input.
/// </summary>
public sealed class RenderTexture : IDisposable
{
    private readonly VkDevice _device;
    private readonly MemoryAllocator _allocator;

    // Color attachment
    private Image _colorImage;
    private DeviceMemory _colorMemory;
    private ImageView _colorImageView;

    // Depth attachment
    private Image _depthImage;
    private DeviceMemory _depthMemory;
    private ImageView _depthImageView;

    // Framebuffer and render pass
    private Framebuffer _framebuffer;
    private VkRenderPass _renderPass;

    // Sampler for reading the color texture
    private Sampler _sampler;

    private readonly uint _width;
    private readonly uint _height;
    private readonly Format _colorFormat;
    private readonly Format _depthFormat;
    private bool _disposed;

    public uint Width => _width;
    public uint Height => _height;
    public ImageView ColorImageView => _colorImageView;
    public ImageView DepthImageView => _depthImageView;
    public Framebuffer Framebuffer => _framebuffer;
    public VkRenderPass RenderPass => _renderPass;
    public Sampler Sampler => _sampler;
    public Extent2D Extent => new(_width, _height);

    private Vk Vk => _device.Vk;
    private Device Device => _device.Device;

    public RenderTexture(VkDevice device, MemoryAllocator allocator, uint width, uint height, Format colorFormat, Format depthFormat)
    {
        _device = device;
        _allocator = allocator;
        _width = width;
        _height = height;
        _colorFormat = colorFormat;
        _depthFormat = depthFormat;

        CreateColorAttachment();
        CreateDepthAttachment();
        CreateRenderPass();
        CreateFramebuffer();
        CreateSampler();
    }

    private unsafe void CreateColorAttachment()
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = _colorFormat,
            Extent = new Extent3D(_width, _height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        var result = Vk.CreateImage(Device, &imageInfo, null, out _colorImage);
        if (result != Result.Success)
            throw new Exception($"Failed to create render texture color image: {result}");

        // Allocate memory
        MemoryRequirements memReqs;
        Vk.GetImageMemoryRequirements(Device, _colorImage, &memReqs);

        var memoryTypeIndex = _device.Capabilities.FindMemoryType(
            Vk, _device.PhysicalDevice, memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = memoryTypeIndex
        };

        result = Vk.AllocateMemory(Device, &allocInfo, null, out _colorMemory);
        if (result != Result.Success)
            throw new Exception($"Failed to allocate render texture color memory: {result}");

        Vk.BindImageMemory(Device, _colorImage, _colorMemory, 0);

        // Create image view
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _colorImage,
            ViewType = ImageViewType.Type2D,
            Format = _colorFormat,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        result = Vk.CreateImageView(Device, &viewInfo, null, out _colorImageView);
        if (result != Result.Success)
            throw new Exception($"Failed to create render texture color image view: {result}");
    }

    private unsafe void CreateDepthAttachment()
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = _depthFormat,
            Extent = new Extent3D(_width, _height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        var result = Vk.CreateImage(Device, &imageInfo, null, out _depthImage);
        if (result != Result.Success)
            throw new Exception($"Failed to create render texture depth image: {result}");

        // Allocate memory
        MemoryRequirements memReqs;
        Vk.GetImageMemoryRequirements(Device, _depthImage, &memReqs);

        var memoryTypeIndex = _device.Capabilities.FindMemoryType(
            Vk, _device.PhysicalDevice, memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = memoryTypeIndex
        };

        result = Vk.AllocateMemory(Device, &allocInfo, null, out _depthMemory);
        if (result != Result.Success)
            throw new Exception($"Failed to allocate render texture depth memory: {result}");

        Vk.BindImageMemory(Device, _depthImage, _depthMemory, 0);

        // Create image view
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _depthImage,
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

        result = Vk.CreateImageView(Device, &viewInfo, null, out _depthImageView);
        if (result != Result.Success)
            throw new Exception($"Failed to create render texture depth image view: {result}");
    }

    private unsafe void CreateRenderPass()
    {
        // Color attachment - clears and stores
        var colorAttachment = new AttachmentDescription
        {
            Format = _colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ShaderReadOnlyOptimal  // Ready for sampling
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

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
        };

        var attachments = stackalloc AttachmentDescription[2] { colorAttachment, depthAttachment };

        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        var result = Vk.CreateRenderPass(Device, &createInfo, null, out _renderPass);
        if (result != Result.Success)
            throw new Exception($"Failed to create render texture render pass: {result}");
    }

    private unsafe void CreateFramebuffer()
    {
        var attachments = stackalloc ImageView[2] { _colorImageView, _depthImageView };

        var createInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _renderPass,
            AttachmentCount = 2,
            PAttachments = attachments,
            Width = _width,
            Height = _height,
            Layers = 1
        };

        var result = Vk.CreateFramebuffer(Device, &createInfo, null, out _framebuffer);
        if (result != Result.Success)
            throw new Exception($"Failed to create render texture framebuffer: {result}");
    }

    private unsafe void CreateSampler()
    {
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MipLodBias = 0,
            AnisotropyEnable = false,
            MaxAnisotropy = 1,
            CompareEnable = false,
            MinLod = 0,
            MaxLod = 0,
            BorderColor = BorderColor.FloatOpaqueBlack,
            UnnormalizedCoordinates = false
        };

        var result = Vk.CreateSampler(Device, &samplerInfo, null, out _sampler);
        if (result != Result.Success)
            throw new Exception($"Failed to create render texture sampler: {result}");
    }

    /// <summary>
    /// Begin rendering to this texture.
    /// </summary>
    public unsafe void BeginRenderPass(CommandBuffer cmd, float clearR = 0, float clearG = 0, float clearB = 0)
    {
        var clearValues = stackalloc ClearValue[2];
        clearValues[0].Color = new ClearColorValue { Float32_0 = clearR, Float32_1 = clearG, Float32_2 = clearB, Float32_3 = 1f };
        clearValues[1].DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 };

        var beginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffer,
            RenderArea = new Rect2D { Offset = default, Extent = new Extent2D(_width, _height) },
            ClearValueCount = 2,
            PClearValues = clearValues
        };

        Vk.CmdBeginRenderPass(cmd, &beginInfo, SubpassContents.Inline);
    }

    /// <summary>
    /// End rendering to this texture.
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

        Vk.DestroyImageView(Device, _depthImageView, null);
        Vk.DestroyImage(Device, _depthImage, null);
        Vk.FreeMemory(Device, _depthMemory, null);

        Vk.DestroyImageView(Device, _colorImageView, null);
        Vk.DestroyImage(Device, _colorImage, null);
        Vk.FreeMemory(Device, _colorMemory, null);
    }
}
