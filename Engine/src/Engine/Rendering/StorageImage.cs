using Silk.NET.Vulkan;
using DerpLib.Core;

namespace DerpLib.Rendering;

/// <summary>
/// A GPU storage image for compute shader read/write operations.
/// Used for compute-based rendering like SDF evaluation.
/// </summary>
public sealed class StorageImage : IDisposable
{
    private readonly VkDevice _device;

    private Image _image;
    private DeviceMemory _memory;
    private ImageView _imageView;

    private readonly uint _width;
    private readonly uint _height;
    private readonly Format _format;
    private bool _disposed;

    public uint Width => _width;
    public uint Height => _height;
    public Format Format => _format;
    public Image Image => _image;
    public ImageView ImageView => _imageView;
    public Extent2D Extent => new(_width, _height);

    private Vk Vk => _device.Vk;
    private Device Device => _device.Device;

    public StorageImage(VkDevice device, uint width, uint height, Format format = Format.R8G8B8A8Unorm)
    {
        _device = device;
        _width = width;
        _height = height;
        _format = format;

        CreateImage();
        CreateImageView();
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
            // Storage for compute write, TransferSrc for blit to swapchain
            Usage = ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        var result = Vk.CreateImage(Device, &imageInfo, null, out _image);
        if (result != Result.Success)
            throw new Exception($"Failed to create storage image: {result}");

        // Allocate device-local memory
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
            throw new Exception($"Failed to allocate storage image memory: {result}");

        Vk.BindImageMemory(Device, _image, _memory, 0);
    }

    private unsafe void CreateImageView()
    {
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2D,
            Format = _format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        var result = Vk.CreateImageView(Device, &viewInfo, null, out _imageView);
        if (result != Result.Success)
            throw new Exception($"Failed to create storage image view: {result}");
    }

    /// <summary>
    /// Transition image layout. Call before compute dispatch (to General) and before blit (to TransferSrcOptimal).
    /// </summary>
    public unsafe void TransitionLayout(CommandBuffer cmd, ImageLayout oldLayout, ImageLayout newLayout)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = _image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        PipelineStageFlags srcStage;
        PipelineStageFlags dstStage;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.General)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.ShaderWriteBit;
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.ComputeShaderBit;
        }
        else if (oldLayout == ImageLayout.General && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            srcStage = PipelineStageFlags.ComputeShaderBit;
            dstStage = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.General)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderReadBit;
            barrier.DstAccessMask = AccessFlags.ShaderWriteBit;
            srcStage = PipelineStageFlags.FragmentShaderBit;
            dstStage = PipelineStageFlags.ComputeShaderBit;
        }
        else if (oldLayout == ImageLayout.General && newLayout == ImageLayout.TransferSrcOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
            barrier.DstAccessMask = AccessFlags.TransferReadBit;
            srcStage = PipelineStageFlags.ComputeShaderBit;
            dstStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.TransferSrcOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderReadBit;
            barrier.DstAccessMask = AccessFlags.TransferReadBit;
            srcStage = PipelineStageFlags.FragmentShaderBit;
            dstStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferSrcOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferReadBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.TransferSrcOptimal && newLayout == ImageLayout.General)
        {
            barrier.SrcAccessMask = AccessFlags.TransferReadBit;
            barrier.DstAccessMask = AccessFlags.ShaderWriteBit;
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.ComputeShaderBit;
        }
        else
        {
            throw new ArgumentException($"Unsupported layout transition: {oldLayout} -> {newLayout}");
        }

        Vk.CmdPipelineBarrier(cmd, srcStage, dstStage, 0,
            0, null, 0, null, 1, &barrier);
    }

    /// <summary>
    /// Resize the storage image. Recreates all resources.
    /// </summary>
    public void Resize(uint newWidth, uint newHeight)
    {
        if (newWidth == _width && newHeight == _height)
            return;

        // Destroy old resources
        DestroyResources();

        // Update dimensions (use reflection to update readonly fields)
        unsafe
        {
            fixed (uint* pWidth = &_width)
            fixed (uint* pHeight = &_height)
            {
                *pWidth = newWidth;
                *pHeight = newHeight;
            }
        }

        // Recreate
        CreateImage();
        CreateImageView();
    }

    private unsafe void DestroyResources()
    {
        Vk.DestroyImageView(Device, _imageView, null);
        Vk.DestroyImage(Device, _image, null);
        Vk.FreeMemory(Device, _memory, null);
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyResources();
    }
}
