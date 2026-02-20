using Serilog;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using DerpLib.Core;

namespace DerpLib.Presentation;

public sealed class Swapchain : IDisposable
{
    private readonly ILogger _log;
    private VkDevice _vkDevice = null!;
    private VkSurface _vkSurface = null!;
    private Vk _vk = null!;
    private Device _device;
    private KhrSwapchain _khrSwapchain = null!;

    private SwapchainKHR _swapchain;
    private Image[] _images = [];
    private ImageView[] _imageViews = [];
    private Format _imageFormat;
    private Extent2D _extent;

    public SwapchainKHR Handle => _swapchain;
    public Image[] Images => _images;
    public ImageView[] ImageViews => _imageViews;
    public Format ImageFormat => _imageFormat;
    public Extent2D Extent => _extent;
    public uint ImageCount => (uint)_images.Length;
    public KhrSwapchain KhrSwapchain => _khrSwapchain;

    public Swapchain(ILogger log)
    {
        _log = log;
    }

    public unsafe void Initialize(VkInstance vkInstance, VkDevice vkDevice, VkSurface vkSurface, uint width, uint height)
    {
        _vkDevice = vkDevice;
        _vkSurface = vkSurface;
        _vk = vkDevice.Vk;
        _device = vkDevice.Device;

        if (!_vk.TryGetDeviceExtension(vkInstance.Instance, _device, out _khrSwapchain))
        {
            throw new Exception("Failed to get KHR_swapchain extension");
        }

        CreateSwapchain(width, height, default);
    }

    public unsafe void Recreate(uint width, uint height)
    {
        // Wait for GPU to finish
        _vk.DeviceWaitIdle(_device);

        // Destroy old ImageViews (they reference old swapchain images)
        DestroyImageViews();

        var oldSwapchain = _swapchain;
        CreateSwapchain(width, height, oldSwapchain);

        // Destroy old swapchain after new one is created
        _khrSwapchain.DestroySwapchain(_device, oldSwapchain, null);

        _log.Information("Swapchain recreated: {Width}x{Height}", width, height);
    }

    private unsafe void CreateSwapchain(uint width, uint height, SwapchainKHR oldSwapchain)
    {
        var khrSurface = _vkSurface.KhrSurface;
        var surface = _vkSurface.Surface;
        var physicalDevice = _vkDevice.PhysicalDevice;

        khrSurface.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, surface, out var capabilities);

        var format = ChooseSurfaceFormat(khrSurface, physicalDevice, surface);
        _imageFormat = format.Format;

        var presentMode = ChoosePresentMode(khrSurface, physicalDevice, surface);
        _extent = ChooseExtent(capabilities, width, height);

        var imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
        {
            imageCount = capabilities.MaxImageCount;
        }

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface,
            MinImageCount = imageCount,
            ImageFormat = format.Format,
            ImageColorSpace = format.ColorSpace,
            ImageExtent = _extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,
            OldSwapchain = oldSwapchain
        };

        var result = _khrSwapchain.CreateSwapchain(_device, &createInfo, null, out _swapchain);
        if (result != Result.Success)
        {
            throw new Exception($"Failed to create swapchain: {result}");
        }

        uint swapchainImageCount = 0;
        _khrSwapchain.GetSwapchainImages(_device, _swapchain, &swapchainImageCount, null);
        _images = new Image[swapchainImageCount];
        fixed (Image* imagesPtr = _images)
        {
            _khrSwapchain.GetSwapchainImages(_device, _swapchain, &swapchainImageCount, imagesPtr);
        }

        CreateImageViews();

        if (oldSwapchain.Handle == 0)
        {
            _log.Information("Swapchain created: {Width}x{Height}, {Format}, {ImageCount} images, {PresentMode}",
                _extent.Width, _extent.Height, format.Format, _images.Length, presentMode);
        }
    }

    private unsafe void CreateImageViews()
    {
        _imageViews = new ImageView[_images.Length];

        for (int i = 0; i < _images.Length; i++)
        {
            var createInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _images[i],
                ViewType = ImageViewType.Type2D,
                Format = _imageFormat,
                Components = new ComponentMapping
                {
                    R = ComponentSwizzle.Identity,
                    G = ComponentSwizzle.Identity,
                    B = ComponentSwizzle.Identity,
                    A = ComponentSwizzle.Identity
                },
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            var result = _vk.CreateImageView(_device, &createInfo, null, out _imageViews[i]);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create image view {i}: {result}");
            }
        }
    }

    private unsafe void DestroyImageViews()
    {
        foreach (var imageView in _imageViews)
        {
            _vk.DestroyImageView(_device, imageView, null);
        }
        _imageViews = [];
    }

    private unsafe SurfaceFormatKHR ChooseSurfaceFormat(KhrSurface khrSurface, PhysicalDevice physicalDevice, SurfaceKHR surface)
    {
        uint formatCount = 0;
        khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, &formatCount, null);

        var formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* formatsPtr = formats)
        {
            khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, surface, &formatCount, formatsPtr);
        }

        foreach (var format in formats)
        {
            if (format.Format == Format.B8G8R8A8Srgb &&
                format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return format;
            }
        }

        return formats[0];
    }

    private unsafe PresentModeKHR ChoosePresentMode(KhrSurface khrSurface, PhysicalDevice physicalDevice, SurfaceKHR surface)
    {
        uint modeCount = 0;
        khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, surface, &modeCount, null);

        var modes = new PresentModeKHR[modeCount];
        fixed (PresentModeKHR* modesPtr = modes)
        {
            khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, surface, &modeCount, modesPtr);
        }

        foreach (var mode in modes)
        {
            if (mode == PresentModeKHR.MailboxKhr)
            {
                return mode;
            }
        }

        return PresentModeKHR.FifoKhr;
    }

    private Extent2D ChooseExtent(SurfaceCapabilitiesKHR capabilities, uint width, uint height)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }

        return new Extent2D
        {
            Width = Math.Clamp(width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
            Height = Math.Clamp(height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height)
        };
    }

    public unsafe Result AcquireNextImage(Silk.NET.Vulkan.Semaphore imageAvailableSemaphore, out uint imageIndex)
    {
        uint index = 0;
        var result = _khrSwapchain.AcquireNextImage(
            _device,
            _swapchain,
            ulong.MaxValue,  // timeout
            imageAvailableSemaphore,
            default,         // no fence
            &index);

        imageIndex = index;
        return result;
    }

    public unsafe Result Present(Queue queue, Silk.NET.Vulkan.Semaphore waitSemaphore, uint imageIndex)
    {
        var swapchain = _swapchain;
        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex
        };

        return _khrSwapchain.QueuePresent(queue, &presentInfo);
    }

    public unsafe void Dispose()
    {
        DestroyImageViews();
        _khrSwapchain.DestroySwapchain(_device, _swapchain, null);
    }
}
