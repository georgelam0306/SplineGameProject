using System.Runtime.InteropServices;
using Serilog;
using Silk.NET.Vulkan;

namespace DerpLib.Core;

/// <summary>
/// Detects device capabilities including memory architecture.
/// Determines whether to use unified memory (Apple Silicon) or discrete memory (PC GPUs).
/// </summary>
public sealed class DeviceCapabilities
{
    private readonly ILogger _log;

    /// <summary>
    /// True if the device has unified memory (CPU and GPU share memory).
    /// On unified devices, we can map buffers directly without staging.
    /// </summary>
    public bool IsUnifiedMemory { get; private set; }

    /// <summary>
    /// True if running on Apple Silicon via MoltenVK.
    /// </summary>
    public bool IsMoltenVK { get; private set; }

    /// <summary>
    /// True if Buffer Device Address is supported.
    /// </summary>
    public bool SupportsBufferDeviceAddress { get; private set; }

    /// <summary>
    /// True if descriptor indexing is supported.
    /// </summary>
    public bool SupportsDescriptorIndexing { get; private set; }

    /// <summary>
    /// Maximum number of storage buffers that can be bound.
    /// </summary>
    public uint MaxStorageBuffers { get; private set; }

    /// <summary>
    /// Maximum number of sampled images that can be bound.
    /// </summary>
    public uint MaxSampledImages { get; private set; }

    /// <summary>
    /// The memory type index for device-local memory (VRAM).
    /// </summary>
    public uint DeviceLocalMemoryType { get; private set; }

    /// <summary>
    /// The memory type index for host-visible memory (for staging or unified access).
    /// </summary>
    public uint HostVisibleMemoryType { get; private set; }

    /// <summary>
    /// The memory type index for host-visible, device-local memory (unified memory).
    /// Only valid if IsUnifiedMemory is true.
    /// </summary>
    public uint UnifiedMemoryType { get; private set; }

    public DeviceCapabilities(ILogger log)
    {
        _log = log;
    }

    public unsafe void Detect(Vk vk, PhysicalDevice physicalDevice)
    {
        // Check if running on macOS (MoltenVK)
        IsMoltenVK = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        // Get device properties
        PhysicalDeviceProperties props;
        vk.GetPhysicalDeviceProperties(physicalDevice, &props);

        // Get memory properties
        PhysicalDeviceMemoryProperties memProps;
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, &memProps);

        // Detect unified memory architecture
        // Unified memory has memory types that are both DEVICE_LOCAL and HOST_VISIBLE
        IsUnifiedMemory = false;
        DeviceLocalMemoryType = uint.MaxValue;
        HostVisibleMemoryType = uint.MaxValue;
        UnifiedMemoryType = uint.MaxValue;

        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            var memType = memProps.MemoryTypes[(int)i];
            var flags = memType.PropertyFlags;

            var isDeviceLocal = (flags & MemoryPropertyFlags.DeviceLocalBit) != 0;
            var isHostVisible = (flags & MemoryPropertyFlags.HostVisibleBit) != 0;
            var isHostCoherent = (flags & MemoryPropertyFlags.HostCoherentBit) != 0;

            // Unified memory: both device local and host visible
            if (isDeviceLocal && isHostVisible && isHostCoherent)
            {
                IsUnifiedMemory = true;
                UnifiedMemoryType = i;
            }

            // Best device local memory (prefer without host visible for discrete GPUs)
            if (isDeviceLocal && DeviceLocalMemoryType == uint.MaxValue)
            {
                DeviceLocalMemoryType = i;
            }

            // Best host visible memory (for staging buffers)
            if (isHostVisible && isHostCoherent && HostVisibleMemoryType == uint.MaxValue)
            {
                HostVisibleMemoryType = i;
            }
        }

        // For unified memory, use the unified type for device-local operations
        if (IsUnifiedMemory && UnifiedMemoryType != uint.MaxValue)
        {
            DeviceLocalMemoryType = UnifiedMemoryType;
        }

        // Check for descriptor indexing and BDA support
        DetectVulkan12Features(vk, physicalDevice);

        // Get descriptor limits
        MaxStorageBuffers = props.Limits.MaxPerStageDescriptorStorageBuffers;
        MaxSampledImages = props.Limits.MaxPerStageDescriptorSampledImages;

        // Log capabilities
        _log.Information("Memory Architecture: {Architecture}", IsUnifiedMemory ? "Unified" : "Discrete");
        _log.Debug("MoltenVK: {IsMoltenVK}", IsMoltenVK);
        _log.Debug("Buffer Device Address: {Supported}", SupportsBufferDeviceAddress);
        _log.Debug("Descriptor Indexing: {Supported}", SupportsDescriptorIndexing);
        _log.Debug("Max Storage Buffers: {Count}", MaxStorageBuffers);
        _log.Debug("Max Sampled Images: {Count}", MaxSampledImages);
    }

    private unsafe void DetectVulkan12Features(Vk vk, PhysicalDevice physicalDevice)
    {
        // Query Vulkan 1.2 features
        var vulkan12Features = new PhysicalDeviceVulkan12Features
        {
            SType = StructureType.PhysicalDeviceVulkan12Features
        };

        var features2 = new PhysicalDeviceFeatures2
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &vulkan12Features
        };

        vk.GetPhysicalDeviceFeatures2(physicalDevice, &features2);

        SupportsBufferDeviceAddress = vulkan12Features.BufferDeviceAddress;
        SupportsDescriptorIndexing = vulkan12Features.DescriptorIndexing;

        // On MoltenVK, BDA support may be limited - be conservative
        if (IsMoltenVK)
        {
            SupportsBufferDeviceAddress = false;
        }
    }

    /// <summary>
    /// Finds a suitable memory type index for the given requirements.
    /// </summary>
    public unsafe uint FindMemoryType(
        Vk vk,
        PhysicalDevice physicalDevice,
        uint typeFilter,
        MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProps;
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, &memProps);

        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new Exception($"Failed to find suitable memory type for properties: {properties}");
    }
}
