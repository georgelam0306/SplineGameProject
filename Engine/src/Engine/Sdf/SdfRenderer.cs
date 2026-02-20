using System.Runtime.InteropServices;
using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Core;
using DerpLib.Memory;
using DerpLib.Rendering;
using DerpLib.Shaders;
using PipelineCache = DerpLib.Shaders.PipelineCache;

namespace DerpLib.Sdf;

/// <summary>
/// Push constants for SDF compute shader.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SdfPushConstants
{
    public uint ScreenWidth;
    public uint ScreenHeight;
    public uint CommandCount;
    public uint TilesX;
    public uint TilesY;
    public uint _padding1;
    public uint _padding2;
    public uint _padding3;

    public static uint Size => (uint)Marshal.SizeOf<SdfPushConstants>();
}

/// <summary>
/// Compute-based SDF renderer using existing Engine APIs.
/// Evaluates SDF commands on GPU with storage image output.
/// </summary>
public sealed class SdfRenderer : IDisposable
{
    private const int TileSize = 8; // Workgroup size for compute shader
    private const int FramesInFlight = 2;
    private const uint FontAtlasArrayDescriptorCount = 256;

    private readonly ILogger _log;
    private readonly VkDevice _device;
    private readonly MemoryAllocator _allocator;
    private readonly DescriptorCache _descriptorCache;
    private readonly PipelineCache _pipelineCache;
    private readonly SdfBuffer _sdfBuffer;

    // Storage image for compute output
    private StorageImage _storageImage;
    private uint _currentWidth;
    private uint _currentHeight;

    // Compute shader
    private ComputeShader? _computeShader;

    // Descriptors - managed via DescriptorCache
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorSet[] _descriptorSets;

    // Track image layout
    private ImageLayout _currentLayout = ImageLayout.Undefined;

    // Optional font atlases for glyph rendering
    private ImageView _primaryFontAtlasImageView;
    private Sampler _primaryFontAtlasSampler;
    private bool _hasPrimaryFontAtlas;

    private ImageView _secondaryFontAtlasImageView;
    private Sampler _secondaryFontAtlasSampler;
    private bool _hasSecondaryFontAtlas;

    // Optional bindless font atlas array for mixed-font text rendering (e.g., rich text variants).
    private TextureArray? _fontTextureArray;
    private int _fontTextureArrayBoundCount = -1;

    private bool _disposed;

    private Vk Vk => _device.Vk;
    private Device Device => _device.Device;

    public SdfBuffer Buffer => _sdfBuffer;
    public StorageImage StorageImage => _storageImage;

    public SdfRenderer(
        ILogger log,
        VkDevice device,
        MemoryAllocator allocator,
        DescriptorCache descriptorCache,
        PipelineCache pipelineCache,
        uint width,
        uint height,
        int maxCommands = 8192)
    {
        _log = log;
        _device = device;
        _allocator = allocator;
        _descriptorCache = descriptorCache;
        _pipelineCache = pipelineCache;
        _currentWidth = width;
        _currentHeight = height;

        _sdfBuffer = new SdfBuffer(log, allocator, maxCommands);
        _storageImage = new StorageImage(device, width, height, Format.R8G8B8A8Unorm);
        _descriptorSets = Array.Empty<DescriptorSet>();

        try
        {
            CreateDescriptorResources();
            _log.Debug("SdfRenderer created: {Width}x{Height}", width, height);
        }
        catch
        {
            _storageImage.Dispose();
            _sdfBuffer.Dispose();
            throw;
        }
    }

    private void CreateDescriptorResources()
    {
        // Create descriptor set layout using DescriptorCache
        // Binding 0: Storage image (compute shader output)
        // Binding 1: Storage buffer (SDF commands)
        // Binding 2: Storage buffer (lattices for FFD)
        // Binding 3: Storage buffer (polyline headers - bounds + indices)
        // Binding 4: Storage buffer (polyline points - flat vec2 array)
        // Binding 10: Storage buffer (polyline prefix lengths - flat float array)
        // Binding 5: Storage buffer (tile offsets)
        // Binding 6: Storage buffer (tile indices)
        // Binding 7: Primary font atlas (optional, for glyph rendering)
        // Binding 12: Secondary font atlas (optional, for glyph rendering)
        // Binding 13: Bindless font atlas array (optional, for per-glyph atlas selection)
        // Binding 8: Storage buffer (gradient stops)
        // Binding 9: Storage buffer (warp node chain)
        // Binding 11: Storage buffer (modifier node chain)
        ReadOnlySpan<DescriptorBinding> bindings = stackalloc DescriptorBinding[]
        {
            new(0, DescriptorType.StorageImage, 1, ShaderStageFlags.ComputeBit),
            new(1, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(2, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(3, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(4, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(10, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(5, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(6, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(7, DescriptorType.CombinedImageSampler, 1, ShaderStageFlags.ComputeBit),
            new(12, DescriptorType.CombinedImageSampler, 1, ShaderStageFlags.ComputeBit),
            new(13, DescriptorType.CombinedImageSampler, FontAtlasArrayDescriptorCount, ShaderStageFlags.ComputeBit,
                DescriptorBindingFlags.PartiallyBoundBit),
            new(8, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(9, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit),
            new(11, DescriptorType.StorageBuffer, 1, ShaderStageFlags.ComputeBit)
        };

        _descriptorSetLayout = _descriptorCache.GetOrCreateLayout(bindings);

        var descriptorSets = new DescriptorSet[FramesInFlight];
        int allocatedSetCount = 0;
        try
        {
            // Allocate persistent descriptor sets (one per frame-in-flight)
            for (int frameIndex = 0; frameIndex < FramesInFlight; frameIndex++)
            {
                descriptorSets[frameIndex] = _descriptorCache.AllocatePersistent(_descriptorSetLayout);
                allocatedSetCount++;
            }

            _descriptorSets = descriptorSets;
            UpdateDescriptors();
        }
        catch
        {
            if (allocatedSetCount > 0)
            {
                _descriptorCache.FreePersistent(descriptorSets.AsSpan(0, allocatedSetCount));
            }

            _descriptorSets = Array.Empty<DescriptorSet>();
            throw;
        }
    }

    private void UpdateDescriptors()
    {
        for (int i = 0; i < FramesInFlight; i++)
        {
            // Write storage image to binding 0
            _descriptorCache.WriteStorageImage(
                _descriptorSets[i],
                binding: 0,
                _storageImage.ImageView,
                ImageLayout.General);

            // Write command buffer to binding 1 (per-frame buffer)
            ref var gpuBuffer = ref _sdfBuffer.GetGpuBuffer(i);
            _descriptorCache.WriteBuffer(
                _descriptorSets[i],
                binding: 1,
                DescriptorType.StorageBuffer,
                gpuBuffer.Buffer,
                offset: 0,
                range: gpuBuffer.Size);

            // Write warp node buffer to binding 9 (per-frame buffer)
            ref var warpNodeBuffer = ref _sdfBuffer.GetWarpNodeBuffer(i);
            _descriptorCache.WriteBuffer(
                _descriptorSets[i],
                binding: 9,
                DescriptorType.StorageBuffer,
                warpNodeBuffer.Buffer,
                offset: 0,
                range: warpNodeBuffer.Size);

            // Write modifier node buffer to binding 11 (per-frame buffer)
            ref var modifierNodeBuffer = ref _sdfBuffer.GetModifierNodeBuffer(i);
            _descriptorCache.WriteBuffer(
                _descriptorSets[i],
                binding: 11,
                DescriptorType.StorageBuffer,
                modifierNodeBuffer.Buffer,
                offset: 0,
                range: modifierNodeBuffer.Size);

            // Write lattice buffer to binding 2 (per-frame buffer)
            ref var latticeBuffer = ref _sdfBuffer.GetLatticeBuffer(i);
            _descriptorCache.WriteBuffer(
                _descriptorSets[i],
                binding: 2,
                DescriptorType.StorageBuffer,
                latticeBuffer.Buffer,
                offset: 0,
                range: latticeBuffer.Size);

            // Write polyline header buffer to binding 3 (per-frame buffer)
            ref var headerBuffer = ref _sdfBuffer.GetHeaderBuffer(i);
            _descriptorCache.WriteBuffer(
                _descriptorSets[i],
                binding: 3,
                DescriptorType.StorageBuffer,
                headerBuffer.Buffer,
                offset: 0,
                range: headerBuffer.Size);

            // Write polyline point buffer to binding 4 (per-frame buffer)
            ref var pointBuffer = ref _sdfBuffer.GetPointBuffer(i);
            _descriptorCache.WriteBuffer(
                _descriptorSets[i],
                binding: 4,
                DescriptorType.StorageBuffer,
                pointBuffer.Buffer,
                offset: 0,
                range: pointBuffer.Size);

            // Write polyline length buffer to binding 10 (per-frame buffer)
            ref var lengthBuffer = ref _sdfBuffer.GetLengthBuffer(i);
            _descriptorCache.WriteBuffer(
                _descriptorSets[i],
                binding: 10,
                DescriptorType.StorageBuffer,
                lengthBuffer.Buffer,
                offset: 0,
                range: lengthBuffer.Size);

            // Write tile offsets buffer to binding 5 (per-frame buffer)
            ref var tileOffsetsBuffer = ref _sdfBuffer.GetTileOffsetsBuffer(i);
            _descriptorCache.WriteBuffer(
                _descriptorSets[i],
                binding: 5,
                DescriptorType.StorageBuffer,
                tileOffsetsBuffer.Buffer,
                offset: 0,
                range: tileOffsetsBuffer.Size);

            // Write tile indices buffer to binding 6 (per-frame buffer)
            ref var tileIndicesBuffer = ref _sdfBuffer.GetTileIndicesBuffer(i);
            _descriptorCache.WriteBuffer(
                _descriptorSets[i],
                binding: 6,
                DescriptorType.StorageBuffer,
                tileIndicesBuffer.Buffer,
                offset: 0,
                range: tileIndicesBuffer.Size);

            // Write gradient stop buffer to binding 8 (per-frame buffer)
            ref var gradientStopBuffer = ref _sdfBuffer.GetGradientStopBuffer(i);
            _descriptorCache.WriteBuffer(
                _descriptorSets[i],
                binding: 8,
                DescriptorType.StorageBuffer,
                gradientStopBuffer.Buffer,
                offset: 0,
                range: gradientStopBuffer.Size);

            if (_hasPrimaryFontAtlas)
            {
                _descriptorCache.WriteImage(
                    _descriptorSets[i],
                    binding: 7,
                    arrayElement: 0,
                    _primaryFontAtlasImageView,
                    _primaryFontAtlasSampler,
                    ImageLayout.ShaderReadOnlyOptimal);
            }

            if (_hasSecondaryFontAtlas)
            {
                _descriptorCache.WriteImage(
                    _descriptorSets[i],
                    binding: 12,
                    arrayElement: 0,
                    _secondaryFontAtlasImageView,
                    _secondaryFontAtlasSampler,
                    ImageLayout.ShaderReadOnlyOptimal);
            }

            if (_fontTextureArray != null)
            {
                int textureCount = _fontTextureArray.Count;
                if (textureCount > (int)FontAtlasArrayDescriptorCount)
                {
                    textureCount = (int)FontAtlasArrayDescriptorCount;
                }

                for (int textureIndex = 0; textureIndex < textureCount; textureIndex++)
                {
                    _descriptorCache.WriteImage(
                        _descriptorSets[i],
                        binding: 13,
                        arrayElement: (uint)textureIndex,
                        _fontTextureArray.GetImageView(textureIndex),
                        _fontTextureArray.Sampler,
                        ImageLayout.ShaderReadOnlyOptimal);
                }
            }
        }
    }

    /// <summary>
    /// Bind a font atlas for glyph rendering in the SDF compute shader.
    /// The atlas must be in ShaderReadOnlyOptimal layout when Dispatch() is called.
    /// </summary>
    public void SetFontAtlas(ImageView imageView, Sampler sampler)
    {
        _primaryFontAtlasImageView = imageView;
        _primaryFontAtlasSampler = sampler;
        _hasPrimaryFontAtlas = true;
        UpdateDescriptors();
    }

    /// <summary>
    /// Bind a font atlas from a Texture loaded via TextureArray.
    /// </summary>
    public void SetFontAtlas(Texture texture, TextureArray textureArray)
    {
        SetFontAtlas(textureArray.GetImageView(texture.Index), textureArray.Sampler);
    }

    public void SetSecondaryFontAtlas(ImageView imageView, Sampler sampler)
    {
        _secondaryFontAtlasImageView = imageView;
        _secondaryFontAtlasSampler = sampler;
        _hasSecondaryFontAtlas = true;
        UpdateDescriptors();
    }

    public void SetSecondaryFontAtlas(Texture texture, TextureArray textureArray)
    {
        SetSecondaryFontAtlas(textureArray.GetImageView(texture.Index), textureArray.Sampler);
    }

    /// <summary>
    /// Bind the global texture array for per-glyph font atlas indexing.
    /// Rewrites descriptors only when the texture count changes.
    /// </summary>
    public void SetFontTextureArray(TextureArray textureArray)
    {
        int textureCount = textureArray.Count;
        if (ReferenceEquals(_fontTextureArray, textureArray) && _fontTextureArrayBoundCount == textureCount)
        {
            return;
        }

        _fontTextureArray = textureArray;
        _fontTextureArrayBoundCount = textureCount;
        UpdateDescriptors();
    }

    /// <summary>
    /// Set the compute shader for SDF evaluation.
    /// </summary>
    public void SetShader(ComputeShader shader)
    {
        if (_computeShader == shader) return;

        _computeShader = shader;

        // Ensure pipeline is created via PipelineCache
        _pipelineCache.GetOrCreateCompute(shader, _descriptorSetLayout, SdfPushConstants.Size);

        _log.Debug("SDF compute shader set");
    }

    /// <summary>
    /// Resize the storage image if dimensions changed.
    /// </summary>
    public void Resize(uint width, uint height)
    {
        if (width == _currentWidth && height == _currentHeight)
            return;

        _currentWidth = width;
        _currentHeight = height;
        _currentLayout = ImageLayout.Undefined;

        _storageImage.Resize(width, height);
        UpdateDescriptors();

        _log.Debug("SdfRenderer resized: {Width}x{Height}", width, height);
    }

    /// <summary>
    /// Dispatch the SDF compute shader.
    /// Call after adding commands to the buffer and calling Flush().
    /// </summary>
    public unsafe void Dispatch(CommandBuffer cmd, int frameIndex)
    {
        if (_computeShader == null)
            throw new InvalidOperationException("No compute shader set. Call SetShader first.");

        if (_sdfBuffer.Count == 0)
            return;

        if (_sdfBuffer.HasGlyphCommands && !_hasPrimaryFontAtlas)
            throw new InvalidOperationException("SDF glyph commands were submitted, but no primary font atlas is bound. Call Derp.SetSdfFontAtlas() first.");

        if (_sdfBuffer.HasSecondaryGlyphCommands && !_hasSecondaryFontAtlas)
            throw new InvalidOperationException("SDF glyph commands were submitted for the secondary font atlas, but no secondary font atlas is bound. Call Derp.SetSdfSecondaryFontAtlas() first.");

        // Transition storage image to General layout for compute write
        if (_currentLayout != ImageLayout.General)
        {
            _storageImage.TransitionLayout(cmd, _currentLayout, ImageLayout.General);
            _currentLayout = ImageLayout.General;
        }

        // Get pipeline via PipelineCache
        var pipeline = _pipelineCache.GetOrCreateCompute(_computeShader, _descriptorSetLayout, SdfPushConstants.Size);
        var pipelineLayout = _pipelineCache.GetComputePipelineLayout(_computeShader);

        // Bind pipeline and descriptor set
        Vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, pipeline);

        var descriptorSet = _descriptorSets[frameIndex];
        Vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, pipelineLayout,
            0, 1, &descriptorSet, 0, null);

        // Set push constants
        var pushConstants = new SdfPushConstants
        {
            ScreenWidth = _currentWidth,
            ScreenHeight = _currentHeight,
            CommandCount = (uint)_sdfBuffer.Count,
            TilesX = (uint)_sdfBuffer.TilesX,
            TilesY = (uint)_sdfBuffer.TilesY
        };

        Vk.CmdPushConstants(cmd, pipelineLayout, ShaderStageFlags.ComputeBit,
            0, SdfPushConstants.Size, &pushConstants);

        // Dispatch: ceil(width/TileSize) x ceil(height/TileSize)
        uint groupsX = (_currentWidth + TileSize - 1) / TileSize;
        uint groupsY = (_currentHeight + TileSize - 1) / TileSize;

        Vk.CmdDispatch(cmd, groupsX, groupsY, 1);

        // Memory barrier: compute write -> transfer read
        var memBarrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit | AccessFlags.ShaderReadBit
        };

        Vk.CmdPipelineBarrier(cmd,
            PipelineStageFlags.ComputeShaderBit,
            PipelineStageFlags.TransferBit | PipelineStageFlags.FragmentShaderBit,
            0, 1, &memBarrier, 0, null, 0, null);
    }

    /// <summary>
    /// Blit the SDF storage image to the swapchain image.
    /// Call after Dispatch() and before presenting.
    /// </summary>
    public unsafe void BlitToSwapchain(CommandBuffer cmd, Image swapchainImage, uint swapchainWidth, uint swapchainHeight)
    {
        if (_sdfBuffer.Count == 0)
            return;

        // Transition storage image to TransferSrcOptimal
        _storageImage.TransitionLayout(cmd, _currentLayout, ImageLayout.TransferSrcOptimal);
        _currentLayout = ImageLayout.TransferSrcOptimal;

        // Transition swapchain image to TransferDstOptimal
        var swapchainBarrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.Undefined, // Or track actual layout
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = swapchainImage,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.TransferWriteBit
        };

        Vk.CmdPipelineBarrier(cmd,
            PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.TransferBit,
            0, 0, null, 0, null, 1, &swapchainBarrier);

        // Blit storage image to swapchain
        var blitRegion = new ImageBlit
        {
            SrcSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            DstSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        blitRegion.SrcOffsets[0] = new Offset3D(0, 0, 0);
        blitRegion.SrcOffsets[1] = new Offset3D((int)_currentWidth, (int)_currentHeight, 1);
        blitRegion.DstOffsets[0] = new Offset3D(0, 0, 0);
        blitRegion.DstOffsets[1] = new Offset3D((int)swapchainWidth, (int)swapchainHeight, 1);

        Vk.CmdBlitImage(cmd,
            _storageImage.Image, ImageLayout.TransferSrcOptimal,
            swapchainImage, ImageLayout.TransferDstOptimal,
            1, &blitRegion,
            Filter.Linear);

        // Transition swapchain image to PresentSrcKhr
        swapchainBarrier.OldLayout = ImageLayout.TransferDstOptimal;
        swapchainBarrier.NewLayout = ImageLayout.PresentSrcKhr;
        swapchainBarrier.SrcAccessMask = AccessFlags.TransferWriteBit;
        swapchainBarrier.DstAccessMask = 0;

        Vk.CmdPipelineBarrier(cmd,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.BottomOfPipeBit,
            0, 0, null, 0, null, 1, &swapchainBarrier);

        // Transition storage image back to General for next frame
        _storageImage.TransitionLayout(cmd, ImageLayout.TransferSrcOptimal, ImageLayout.General);
        _currentLayout = ImageLayout.General;
    }

    /// <summary>
    /// Transition the SDF output image to ShaderReadOnlyOptimal for sampling in a render pass.
    /// Use this if you want to composite SDF output via a draw call instead of blitting.
    /// </summary>
    public void TransitionOutputForSampling(CommandBuffer cmd)
    {
        if (_sdfBuffer.Count == 0)
            return;

        if (_currentLayout == ImageLayout.ShaderReadOnlyOptimal)
            return;

        _storageImage.TransitionLayout(cmd, _currentLayout, ImageLayout.ShaderReadOnlyOptimal);
        _currentLayout = ImageLayout.ShaderReadOnlyOptimal;
    }

    /// <summary>
    /// Reset for next frame. Call at the start of each frame.
    /// </summary>
    public void Reset()
    {
        _sdfBuffer.Reset();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Free persistent descriptor sets to reclaim pool space
        if (_descriptorSets != null && _descriptorSets.Length > 0)
        {
            _descriptorCache.FreePersistent(_descriptorSets);
        }

        // Pipelines are managed by PipelineCache (shared)

        _storageImage.Dispose();
        _sdfBuffer.Dispose();

        _log.Debug("SdfRenderer disposed");
    }
}
