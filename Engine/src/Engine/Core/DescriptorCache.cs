using System.Runtime.InteropServices;
using Serilog;
using Silk.NET.Vulkan;

namespace DerpLib.Core;

/// <summary>
/// Describes a single descriptor binding slot.
/// Blittable struct for fast byte-level comparison.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct DescriptorBinding
{
    public readonly uint Binding;
    public readonly DescriptorType Type;
    public readonly uint Count;
    public readonly ShaderStageFlags Stages;
    public readonly DescriptorBindingFlags Flags;

    public DescriptorBinding(
        uint binding,
        DescriptorType type,
        uint count,
        ShaderStageFlags stages,
        DescriptorBindingFlags flags = DescriptorBindingFlags.None)
    {
        Binding = binding;
        Type = type;
        Count = count;
        Stages = stages;
        Flags = flags;
    }
}

/// <summary>
/// Entry in the layout cache.
/// </summary>
internal struct LayoutEntry
{
    public bool IsOccupied;
    public int Hash;
    public DescriptorBinding[] Bindings;
    public DescriptorSetLayout Layout;
}

/// <summary>
/// Central cache for descriptor set layouts, pools, and sets.
/// Uses open-addressed array for zero-allocation layout lookups.
/// </summary>
public sealed class DescriptorCache : IDisposable
{
    private const int MaxLayouts = 64;
    private const uint PersistentPoolStorageBufferCount = 4096;
    private const uint PersistentPoolCombinedImageSamplerCount = 16384;
    private const uint PersistentPoolUniformBufferCount = 256;
    private const uint PersistentPoolStorageImageCount = 512;
    private const uint TransientPoolStorageBufferCount = 256;
    private const uint TransientPoolCombinedImageSamplerCount = 1024;
    private const uint TransientPoolUniformBufferCount = 64;
    private const uint TransientPoolStorageImageCount = 64;
    private const int PersistentPoolMaxSets = 512;

    private readonly ILogger _log;
    private readonly VkDevice _vkDevice;
    private readonly int _framesInFlight;

    // Open-addressed layout cache
    private readonly LayoutEntry[] _layouts = new LayoutEntry[MaxLayouts];
    private int _layoutCount;

    // Per-frame transient pools (reset each frame)
    private readonly DescriptorPool[] _framePools;

    // Persistent pool for long-lived sets
    private DescriptorPool _persistentPool;

    private Vk Vk => _vkDevice.Vk;
    private Device Device => _vkDevice.Device;

    private bool _disposed;

    public DescriptorCache(ILogger log, VkDevice vkDevice, int framesInFlight)
    {
        _log = log;
        _vkDevice = vkDevice;
        _framesInFlight = framesInFlight;

        _framePools = new DescriptorPool[framesInFlight];
    }

    public unsafe void Initialize()
    {
        // Create per-frame transient pools
        for (int i = 0; i < _framesInFlight; i++)
        {
            _framePools[i] = CreatePool(maxSets: 100, isTransient: true);
        }

        // Create persistent pool for bindless resources
        // Sized for multi-viewport + editor/runtime CanvasSurface renderers.
        // Each SdfRenderer consumes 2 persistent sets and a large combined-image array (binding 13).
        _persistentPool = CreatePool(maxSets: PersistentPoolMaxSets, isTransient: false);

        _log.Debug("DescriptorCache initialized: {Frames} frame pools + 1 persistent pool",
            _framesInFlight);
    }

    /// <summary>
    /// Gets or creates a descriptor set layout for the given bindings.
    /// Zero-allocation on cache hit.
    /// </summary>
    public unsafe DescriptorSetLayout GetOrCreateLayout(ReadOnlySpan<DescriptorBinding> bindings)
    {
        // Hash from raw bytes - zero allocation
        var hash = new HashCode();
        hash.AddBytes(MemoryMarshal.AsBytes(bindings));
        int key = hash.ToHashCode();

        // Open-addressed lookup
        int index = (key & 0x7FFFFFFF) % MaxLayouts;
        int probeCount = 0;

        while (_layouts[index].IsOccupied)
        {
            ref var entry = ref _layouts[index];

            // Check hash first (fast path)
            if (entry.Hash == key)
            {
                // Verify bindings match (collision check)
                if (BindingsMatch(bindings, entry.Bindings))
                {
                    return entry.Layout;
                }
            }

            // Linear probe
            index = (index + 1) % MaxLayouts;
            probeCount++;

            if (probeCount >= MaxLayouts)
            {
                throw new InvalidOperationException(
                    $"DescriptorCache full: {MaxLayouts} layouts exceeded. This indicates a design issue.");
            }
        }

        // Cache miss - create and store (allocates only here)
        if (_layoutCount >= MaxLayouts)
        {
            throw new InvalidOperationException(
                $"DescriptorCache full: {MaxLayouts} layouts exceeded.");
        }

        var layout = CreateLayout(bindings);

        _layouts[index] = new LayoutEntry
        {
            IsOccupied = true,
            Hash = key,
            Bindings = bindings.ToArray(),
            Layout = layout
        };
        _layoutCount++;

        _log.Debug("Created descriptor set layout with {Count} bindings (total: {Total})",
            bindings.Length, _layoutCount);

        return layout;
    }

    /// <summary>
    /// Allocates a descriptor set from the per-frame transient pool.
    /// These are automatically "freed" when ResetFramePool is called.
    /// </summary>
    public unsafe DescriptorSet AllocateTransient(int frameIndex, DescriptorSetLayout layout)
    {
        return AllocateFromPool(_framePools[frameIndex], layout);
    }

    /// <summary>
    /// Allocates a persistent descriptor set for long-lived resources.
    /// These are never automatically freed.
    /// </summary>
    public unsafe DescriptorSet AllocatePersistent(DescriptorSetLayout layout)
    {
        return AllocateFromPool(_persistentPool, layout);
    }

    /// <summary>
    /// Resets the per-frame pool, instantly freeing all transient sets.
    /// Call at the start of each frame.
    /// </summary>
    public void ResetFramePool(int frameIndex)
    {
        Vk.ResetDescriptorPool(Device, _framePools[frameIndex], 0);
    }

    /// <summary>
    /// Free persistent descriptor sets (e.g., when disposing a secondary viewport's SdfRenderer).
    /// </summary>
    public unsafe void FreePersistent(ReadOnlySpan<DescriptorSet> sets)
    {
        if (sets.Length == 0) return;

        fixed (DescriptorSet* pSets = sets)
        {
            Vk.FreeDescriptorSets(Device, _persistentPool, (uint)sets.Length, pSets);
        }
    }

    /// <summary>
    /// Writes a buffer binding to a descriptor set.
    /// </summary>
    public unsafe void WriteBuffer(
        DescriptorSet set,
        uint binding,
        DescriptorType type,
        Silk.NET.Vulkan.Buffer buffer,
        ulong offset = 0,
        ulong range = Vk.WholeSize)
    {
        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = buffer,
            Offset = offset,
            Range = range
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = type,
            PBufferInfo = &bufferInfo
        };

        Vk.UpdateDescriptorSets(Device, 1, &write, 0, null);
    }

    /// <summary>
    /// Writes an image/sampler binding to a descriptor set.
    /// </summary>
    public unsafe void WriteImage(
        DescriptorSet set,
        uint binding,
        uint arrayElement,
        ImageView imageView,
        Sampler sampler,
        ImageLayout layout = ImageLayout.ShaderReadOnlyOptimal)
    {
        var imageInfo = new DescriptorImageInfo
        {
            ImageView = imageView,
            Sampler = sampler,
            ImageLayout = layout
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DstArrayElement = arrayElement,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo
        };

        Vk.UpdateDescriptorSets(Device, 1, &write, 0, null);
    }

    /// <summary>
    /// Writes a storage image binding to a descriptor set.
    /// Used for compute shader read/write images.
    /// </summary>
    public unsafe void WriteStorageImage(
        DescriptorSet set,
        uint binding,
        ImageView imageView,
        ImageLayout layout = ImageLayout.General)
    {
        var imageInfo = new DescriptorImageInfo
        {
            ImageView = imageView,
            ImageLayout = layout
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageImage,
            PImageInfo = &imageInfo
        };

        Vk.UpdateDescriptorSets(Device, 1, &write, 0, null);
    }

    private static bool BindingsMatch(ReadOnlySpan<DescriptorBinding> a, DescriptorBinding[] b)
    {
        if (a.Length != b.Length) return false;
        return MemoryMarshal.AsBytes(a).SequenceEqual(MemoryMarshal.AsBytes(b.AsSpan()));
    }

    private unsafe DescriptorPool CreatePool(int maxSets, bool isTransient)
    {
        uint storageBufferCount = isTransient
            ? TransientPoolStorageBufferCount
            : PersistentPoolStorageBufferCount;
        uint combinedImageSamplerCount = isTransient
            ? TransientPoolCombinedImageSamplerCount
            : PersistentPoolCombinedImageSamplerCount;
        uint uniformBufferCount = isTransient
            ? TransientPoolUniformBufferCount
            : PersistentPoolUniformBufferCount;
        uint storageImageCount = isTransient
            ? TransientPoolStorageImageCount
            : PersistentPoolStorageImageCount;

        var poolSizes = stackalloc DescriptorPoolSize[4];
        poolSizes[0] = new DescriptorPoolSize
        {
            Type = DescriptorType.StorageBuffer,
            DescriptorCount = storageBufferCount
        };
        poolSizes[1] = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = combinedImageSamplerCount
        };
        poolSizes[2] = new DescriptorPoolSize
        {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = uniformBufferCount
        };
        poolSizes[3] = new DescriptorPoolSize
        {
            Type = DescriptorType.StorageImage,
            DescriptorCount = storageImageCount
        };

        var flags = isTransient
            ? DescriptorPoolCreateFlags.None
            : DescriptorPoolCreateFlags.UpdateAfterBindBit | DescriptorPoolCreateFlags.FreeDescriptorSetBit;

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = (uint)maxSets,
            PoolSizeCount = 4,
            PPoolSizes = poolSizes,
            Flags = flags
        };

        DescriptorPool pool;
        var result = Vk.CreateDescriptorPool(Device, &poolInfo, null, &pool);
        if (result != Result.Success)
        {
            throw new Exception($"Failed to create descriptor pool: {result}");
        }

        return pool;
    }

    private unsafe DescriptorSetLayout CreateLayout(ReadOnlySpan<DescriptorBinding> bindings)
    {
        // Build Vulkan binding descriptions
        Span<DescriptorSetLayoutBinding> vkBindings = stackalloc DescriptorSetLayoutBinding[bindings.Length];
        Span<DescriptorBindingFlags> bindingFlags = stackalloc DescriptorBindingFlags[bindings.Length];
        bool hasBindingFlags = false;

        for (int i = 0; i < bindings.Length; i++)
        {
            var b = bindings[i];
            vkBindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = b.Binding,
                DescriptorType = b.Type,
                DescriptorCount = b.Count,
                StageFlags = b.Stages,
                PImmutableSamplers = null
            };
            bindingFlags[i] = b.Flags;

            if (b.Flags != DescriptorBindingFlags.None)
            {
                hasBindingFlags = true;
            }
        }

        fixed (DescriptorSetLayoutBinding* pBindings = vkBindings)
        fixed (DescriptorBindingFlags* pFlags = bindingFlags)
        {
            var layoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)bindings.Length,
                PBindings = pBindings
            };

            // Chain binding flags if needed
            var flagsInfo = new DescriptorSetLayoutBindingFlagsCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
                BindingCount = (uint)bindings.Length,
                PBindingFlags = pFlags
            };

            if (hasBindingFlags)
            {
                layoutInfo.PNext = &flagsInfo;
                layoutInfo.Flags = DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit;
            }

            DescriptorSetLayout layout;
            var result = Vk.CreateDescriptorSetLayout(Device, &layoutInfo, null, &layout);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create descriptor set layout: {result}");
            }

            return layout;
        }
    }

    private unsafe DescriptorSet AllocateFromPool(DescriptorPool pool, DescriptorSetLayout layout)
    {
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = pool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };

        DescriptorSet set;
        var result = Vk.AllocateDescriptorSets(Device, &allocInfo, &set);
        if (result != Result.Success)
        {
            throw new Exception($"Failed to allocate descriptor set: {result}");
        }

        return set;
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = 0; i < MaxLayouts; i++)
        {
            if (_layouts[i].IsOccupied)
            {
                Vk.DestroyDescriptorSetLayout(Device, _layouts[i].Layout, null);
            }
        }

        foreach (var pool in _framePools)
        {
            Vk.DestroyDescriptorPool(Device, pool, null);
        }

        Vk.DestroyDescriptorPool(Device, _persistentPool, null);

        _log.Debug("DescriptorCache disposed");
    }
}
