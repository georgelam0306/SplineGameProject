using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Core;
using DerpLib.Memory;

namespace DerpLib.Rendering;

/// <summary>
/// Manages a bindless array of textures for GPU access.
/// Textures are accessed by index in shaders via sampler2D[maxTextures].
/// </summary>
public sealed class TextureArray : IDisposable
{
    private readonly ILogger _log;
    private readonly VkDevice _vkDevice;
    private readonly MemoryAllocator _allocator;
    private readonly int _maxTextures;

    private readonly Image[] _images;
    private readonly DeviceMemory[] _memories;
    private readonly ImageView[] _imageViews;
    private readonly bool[] _slotActive;
    private readonly bool[] _ownsSlot;
    private readonly uint[] _ownedWidths;
    private readonly uint[] _ownedHeights;
    private readonly int[] _freeSlots;
    private Sampler _sampler;
    private BufferAllocation _updateStagingBuffer;
    private bool _hasUpdateStagingBuffer;
    private int _freeSlotCount;

    private int _count;
    private int _activeCount;
    private bool _disposed;

    private Vk Vk => _vkDevice.Vk;
    private Device Device => _vkDevice.Device;

    // Count is the high-water slot count (contiguous [0..Count-1] range).
    // Descriptor array writes rely on this being the maximum index range, not active slot count.
    public int Count => _count;
    public int ActiveCount => _activeCount;
    public int MaxTextures => _maxTextures;

    public TextureArray(ILogger log, VkDevice vkDevice, MemoryAllocator allocator, int maxTextures)
    {
        _log = log;
        _vkDevice = vkDevice;
        _allocator = allocator;
        _maxTextures = maxTextures;
        _images = new Image[maxTextures];
        _memories = new DeviceMemory[maxTextures];
        _imageViews = new ImageView[maxTextures];
        _slotActive = new bool[maxTextures];
        _ownsSlot = new bool[maxTextures];
        _ownedWidths = new uint[maxTextures];
        _ownedHeights = new uint[maxTextures];
        _freeSlots = new int[maxTextures];
    }

    /// <summary>
    /// Creates the shared sampler. Call after device is initialized.
    /// </summary>
    public unsafe void Initialize()
    {
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            AnisotropyEnable = false,
            MaxAnisotropy = 1,
            BorderColor = BorderColor.IntOpaqueBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MipmapMode = SamplerMipmapMode.Linear,
            MipLodBias = 0,
            MinLod = 0,
            MaxLod = 0
        };

        var result = Vk.CreateSampler(Device, &samplerInfo, null, out _sampler);
        if (result != Result.Success)
            throw new Exception($"Failed to create sampler: {result}");

        _log.Debug("TextureArray initialized with shared sampler");
    }

    /// <summary>
    /// Loads a texture from RGBA pixel data. Returns a handle to the texture.
    /// </summary>
    public unsafe TextureHandle Load(ReadOnlySpan<byte> pixels, uint width, uint height)
    {
        int index = AcquireSlot();
        var imageSize = width * height * 4; // RGBA

        // Create staging buffer
        var staging = _allocator.CreateStagingBuffer(imageSize);
        _allocator.UploadData(staging, pixels);

        // Create image
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Srgb,
            Extent = new Extent3D { Width = width, Height = height, Depth = 1 },
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        Image image;
        var result = Vk.CreateImage(Device, &imageInfo, null, &image);
        if (result != Result.Success)
            throw new Exception($"Failed to create image: {result}");

        // Allocate memory
        MemoryRequirements memReq;
        Vk.GetImageMemoryRequirements(Device, image, &memReq);

        var memTypeIndex = FindMemoryType(memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReq.Size,
            MemoryTypeIndex = (uint)memTypeIndex
        };

        DeviceMemory memory;
        result = Vk.AllocateMemory(Device, &allocInfo, null, &memory);
        if (result != Result.Success)
            throw new Exception($"Failed to allocate image memory: {result}");

        Vk.BindImageMemory(Device, image, memory, 0);

        // Transition to transfer dest, copy, transition to shader read
        TransitionImageLayout(image, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
        CopyBufferToImage(staging.Buffer, image, width, height);
        TransitionImageLayout(image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        // Free staging buffer
        _allocator.FreeBuffer(staging);

        // Create image view
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Srgb,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        ImageView imageView;
        result = Vk.CreateImageView(Device, &viewInfo, null, &imageView);
        if (result != Result.Success)
            throw new Exception($"Failed to create image view: {result}");

        _images[index] = image;
        _memories[index] = memory;
        _imageViews[index] = imageView;
        _slotActive[index] = true;
        _ownsSlot[index] = true;
        _ownedWidths[index] = width;
        _ownedHeights[index] = height;

        _log.Debug("Loaded texture {Index}: {Width}x{Height}", index, width, height);
        return new TextureHandle(index);
    }

    /// <summary>
    /// Registers an externally-owned image view as a bindless texture slot.
    /// The TextureArray will reference the view in descriptors, but will not destroy it.
    /// </summary>
    public TextureHandle RegisterExternal(ImageView imageView)
    {
        int index = AcquireSlot();
        _imageViews[index] = imageView;
        _slotActive[index] = true;
        _ownsSlot[index] = false;
        _ownedWidths[index] = 0;
        _ownedHeights[index] = 0;
        return new TextureHandle(index);
    }

    /// <summary>
    /// Releases an owned texture slot and returns it to the free-slot pool.
    /// The slot is rebound to the default white texture to keep descriptors valid.
    /// </summary>
    public unsafe bool UnloadOwned(int index)
    {
        if ((uint)index >= (uint)_count)
        {
            return false;
        }

        if (!_slotActive[index] || !_ownsSlot[index])
        {
            return false;
        }

        if (index == Texture.White.GetIndex())
        {
            return false;
        }

        Vk.DestroyImageView(Device, _imageViews[index], null);
        Vk.DestroyImage(Device, _images[index], null);
        Vk.FreeMemory(Device, _memories[index], null);

        _slotActive[index] = false;
        _ownsSlot[index] = false;
        _images[index] = default;
        _memories[index] = default;
        _ownedWidths[index] = 0;
        _ownedHeights[index] = 0;

        _freeSlots[_freeSlotCount] = index;
        _freeSlotCount++;
        _activeCount = Math.Max(0, _activeCount - 1);

        int whiteIndex = Texture.White.GetIndex();
        if ((uint)whiteIndex < (uint)_count)
        {
            _imageViews[index] = _imageViews[whiteIndex];
        }
        else
        {
            _imageViews[index] = default;
        }

        return true;
    }

    /// <summary>
    /// Releases an external texture slot so it can be reused by future external registrations.
    /// The slot is rebound to the default white texture to keep descriptors valid.
    /// </summary>
    public bool UnregisterExternal(int index)
    {
        if ((uint)index >= (uint)_count)
        {
            return false;
        }

        if (!_slotActive[index] || _ownsSlot[index])
        {
            return false;
        }

        if (index == Texture.White.GetIndex())
        {
            return false;
        }

        _slotActive[index] = false;
        _ownsSlot[index] = false;
        _images[index] = default;
        _memories[index] = default;
        _ownedWidths[index] = 0;
        _ownedHeights[index] = 0;
        _freeSlots[_freeSlotCount] = index;
        _freeSlotCount++;
        _activeCount = Math.Max(0, _activeCount - 1);

        int whiteIndex = Texture.White.GetIndex();
        if ((uint)whiteIndex < (uint)_count)
        {
            _imageViews[index] = _imageViews[whiteIndex];
        }
        else
        {
            _imageViews[index] = default;
        }

        return true;
    }

    /// <summary>
    /// Updates an external texture slot's image view after the underlying image is recreated (e.g., resize).
    /// </summary>
    public void UpdateExternal(int index, ImageView imageView)
    {
        if ((uint)index >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (!_slotActive[index] || _ownsSlot[index])
        {
            throw new InvalidOperationException("UpdateExternal can only be used with external texture slots.");
        }

        _imageViews[index] = imageView;
    }

    /// <summary>
    /// Updates pixel data for an existing owned texture slot.
    /// Width and height must match the original texture dimensions.
    /// </summary>
    public void UpdateOwnedTexturePixels(int index, ReadOnlySpan<byte> pixels, uint width, uint height)
    {
        if ((uint)index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (!_ownsSlot[index])
            throw new InvalidOperationException("Cannot update pixel data for an external texture slot.");

        if (_ownedWidths[index] != width || _ownedHeights[index] != height)
        {
            throw new InvalidOperationException("Updated texture dimensions must match the existing texture dimensions.");
        }

        ulong requiredSize = (ulong)width * height * 4ul;
        if ((ulong)pixels.Length < requiredSize)
        {
            throw new ArgumentException("Pixel buffer is smaller than required RGBA texture size.", nameof(pixels));
        }

        if (requiredSize > int.MaxValue)
        {
            throw new InvalidOperationException("Texture update buffer is too large.");
        }

        EnsureUpdateStagingBuffer(requiredSize);
        _allocator.UploadData(_updateStagingBuffer, pixels[..(int)requiredSize]);
        TransitionImageLayout(_images[index], ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferDstOptimal);
        CopyBufferToImage(_updateStagingBuffer.Buffer, _images[index], width, height);
        TransitionImageLayout(_images[index], ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
    }

    /// <summary>
    /// Creates a 1x1 solid color texture. Useful for untextured rendering.
    /// </summary>
    public TextureHandle LoadSolidColor(byte r, byte g, byte b, byte a = 255)
    {
        Span<byte> pixel = stackalloc byte[4] { r, g, b, a };
        return Load(pixel, 1, 1);
    }

    /// <summary>
    /// Loads a texture from RGBA pixel data and returns a Texture struct with dimensions.
    /// </summary>
    public Texture LoadTexture(ReadOnlySpan<byte> pixels, int width, int height)
    {
        var handle = Load(pixels, (uint)width, (uint)height);
        return new Texture(handle.Index, width, height);
    }

    /// <summary>
    /// Writes all loaded textures to a descriptor set at the specified binding.
    /// </summary>
    public unsafe void WriteToDescriptorSet(DescriptorCache cache, DescriptorSet set, uint binding)
    {
        for (int i = 0; i < _count; i++)
        {
            cache.WriteImage(set, binding, (uint)i, _imageViews[i], _sampler);
        }
    }

    /// <summary>
    /// Gets the image view for a texture index (for manual descriptor writes).
    /// </summary>
    public ImageView GetImageView(int index) => _imageViews[index];

    /// <summary>
    /// Gets the shared sampler.
    /// </summary>
    public Sampler Sampler => _sampler;

    private unsafe void TransitionImageLayout(Image image, ImageLayout oldLayout, ImageLayout newLayout)
    {
        var commandBuffer = BeginSingleTimeCommands();

        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        PipelineStageFlags srcStage, dstStage;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderReadBit;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            srcStage = PipelineStageFlags.FragmentShaderBit;
            dstStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.FragmentShaderBit;
        }
        else
        {
            throw new ArgumentException($"Unsupported layout transition: {oldLayout} -> {newLayout}");
        }

        Vk.CmdPipelineBarrier(commandBuffer, srcStage, dstStage, 0,
            0, null, 0, null, 1, &barrier);

        EndSingleTimeCommands(commandBuffer);
    }

    private unsafe void CopyBufferToImage(Silk.NET.Vulkan.Buffer buffer, Image image, uint width, uint height)
    {
        var commandBuffer = BeginSingleTimeCommands();

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            ImageOffset = new Offset3D { X = 0, Y = 0, Z = 0 },
            ImageExtent = new Extent3D { Width = width, Height = height, Depth = 1 }
        };

        Vk.CmdCopyBufferToImage(commandBuffer, buffer, image, ImageLayout.TransferDstOptimal, 1, &region);

        EndSingleTimeCommands(commandBuffer);
    }

    private unsafe CommandBuffer BeginSingleTimeCommands()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = _vkDevice.CommandPool,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        Vk.AllocateCommandBuffers(Device, &allocInfo, &commandBuffer);

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        Vk.BeginCommandBuffer(commandBuffer, &beginInfo);
        return commandBuffer;
    }

    private unsafe void EndSingleTimeCommands(CommandBuffer commandBuffer)
    {
        Vk.EndCommandBuffer(commandBuffer);

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        Vk.QueueSubmit(_vkDevice.GraphicsQueue, 1, &submitInfo, default);
        Vk.QueueWaitIdle(_vkDevice.GraphicsQueue);
        Vk.FreeCommandBuffers(Device, _vkDevice.CommandPool, 1, &commandBuffer);
    }

    private int FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        var memProperties = _vkDevice.MemoryProperties;
        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << i)) != 0 &&
                (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }
        throw new Exception($"Failed to find suitable memory type for {properties}");
    }

    private void EnsureUpdateStagingBuffer(ulong requiredSize)
    {
        if (_hasUpdateStagingBuffer && _updateStagingBuffer.Size >= requiredSize)
        {
            return;
        }

        if (_hasUpdateStagingBuffer)
        {
            _allocator.FreeBuffer(_updateStagingBuffer);
            _hasUpdateStagingBuffer = false;
        }

        _updateStagingBuffer = _allocator.CreateStagingBuffer(requiredSize);
        _hasUpdateStagingBuffer = true;
    }

    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hasUpdateStagingBuffer)
        {
            _allocator.FreeBuffer(_updateStagingBuffer);
            _hasUpdateStagingBuffer = false;
        }

        for (int i = 0; i < _count; i++)
        {
            if (_ownsSlot[i])
            {
                Vk.DestroyImageView(Device, _imageViews[i], null);
                Vk.DestroyImage(Device, _images[i], null);
                Vk.FreeMemory(Device, _memories[i], null);
            }
        }

        Vk.DestroySampler(Device, _sampler, null);
        _log.Debug("TextureArray disposed ({Count} textures)", _count);
    }

    private int AcquireSlot()
    {
        int index;
        if (_freeSlotCount > 0)
        {
            _freeSlotCount--;
            index = _freeSlots[_freeSlotCount];
        }
        else
        {
            if (_count >= _maxTextures)
            {
                throw new InvalidOperationException($"TextureArray full: {_maxTextures} textures");
            }

            index = _count;
            _count++;
        }

        _slotActive[index] = true;
        _activeCount++;
        return index;
    }
}
