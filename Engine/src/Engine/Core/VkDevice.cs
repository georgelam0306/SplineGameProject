using System.Runtime.InteropServices;
using Serilog;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace DerpLib.Core;

/// <summary>
/// Manages physical device selection and logical device creation.
/// Can be initialized with or without a surface (for headless compute).
/// </summary>
public sealed class VkDevice : IDisposable
{
    private readonly ILogger _log;
    private readonly VkInstance _vkInstance;
    private readonly DeviceCapabilities _capabilities;

    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _graphicsQueue;
    private Queue _computeQueue;
    private uint _graphicsQueueFamily;
    private uint _computeQueueFamily;
    private PhysicalDeviceMemoryProperties _memoryProperties;
    private CommandPool _transferPool;

    public Vk Vk => _vkInstance.Vk;
    public PhysicalDevice PhysicalDevice => _physicalDevice;
    public Device Device => _device;
    public Queue GraphicsQueue => _graphicsQueue;
    public Queue ComputeQueue => _computeQueue;
    public uint GraphicsQueueFamily => _graphicsQueueFamily;
    public uint ComputeQueueFamily => _computeQueueFamily;
    public PhysicalDeviceMemoryProperties MemoryProperties => _memoryProperties;
    public DeviceCapabilities Capabilities => _capabilities;
    public CommandPool CommandPool => _transferPool;

    public VkDevice(ILogger log, VkInstance vkInstance, DeviceCapabilities capabilities)
    {
        _log = log;
        _vkInstance = vkInstance;
        _capabilities = capabilities;
    }

    /// <summary>
    /// Initialize for headless (compute-only) use.
    /// </summary>
    public void Initialize()
    {
        Initialize(null, null, requireSwapchain: false);
    }

    /// <summary>
    /// Initialize with surface for presentation.
    /// </summary>
    public void Initialize(KhrSurface khrSurface, SurfaceKHR surface)
    {
        Initialize(khrSurface, surface, requireSwapchain: true);
    }

    private unsafe void Initialize(KhrSurface? khrSurface, SurfaceKHR? surface, bool requireSwapchain)
    {
        PickPhysicalDevice(khrSurface, surface, requireSwapchain);
        CreateLogicalDevice(khrSurface, surface, requireSwapchain);
    }

    private unsafe void PickPhysicalDevice(KhrSurface? khrSurface, SurfaceKHR? surface, bool requireSwapchain)
    {
        var vk = _vkInstance.Vk;
        var instance = _vkInstance.Instance;

        uint deviceCount = 0;
        vk.EnumeratePhysicalDevices(instance, &deviceCount, null);

        if (deviceCount == 0)
        {
            throw new Exception("No Vulkan-capable GPU found");
        }

        var devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* devicesPtr = devices)
        {
            vk.EnumeratePhysicalDevices(instance, &deviceCount, devicesPtr);
        }

        PhysicalDevice? selectedDevice = null;
        var selectedScore = -1;

        foreach (var device in devices)
        {
            var score = RateDevice(device);
            if (score > selectedScore && IsDeviceSuitable(device, khrSurface, surface, requireSwapchain))
            {
                selectedDevice = device;
                selectedScore = score;
            }
        }

        if (selectedDevice == null)
        {
            throw new Exception("No suitable GPU found");
        }

        _physicalDevice = selectedDevice.Value;

        PhysicalDeviceProperties props;
        vk.GetPhysicalDeviceProperties(_physicalDevice, &props);
        vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out _memoryProperties);
        var deviceName = Marshal.PtrToStringAnsi((nint)props.DeviceName);
        _log.Information("Selected GPU: {DeviceName} ({DeviceType})", deviceName, props.DeviceType);

        // Detect device capabilities (unified memory, feature support, etc.)
        _capabilities.Detect(vk, _physicalDevice);
    }

    private unsafe int RateDevice(PhysicalDevice device)
    {
        var vk = _vkInstance.Vk;

        PhysicalDeviceProperties props;
        vk.GetPhysicalDeviceProperties(device, &props);

        return props.DeviceType switch
        {
            PhysicalDeviceType.DiscreteGpu => 1000,
            PhysicalDeviceType.IntegratedGpu => 100,
            PhysicalDeviceType.VirtualGpu => 50,
            PhysicalDeviceType.Cpu => 10,
            _ => 0
        };
    }

    private unsafe bool IsDeviceSuitable(PhysicalDevice device, KhrSurface? khrSurface, SurfaceKHR? surface, bool requireSwapchain)
    {
        var vk = _vkInstance.Vk;

        // Check for queue families
        var (graphicsFamily, computeFamily) = FindQueueFamilies(device, khrSurface, surface);
        if (graphicsFamily == null && computeFamily == null) return false;

        // Check for required extensions
        uint extensionCount = 0;
        vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &extensionCount, null);
        var extensions = new ExtensionProperties[extensionCount];
        fixed (ExtensionProperties* extensionsPtr = extensions)
        {
            vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &extensionCount, extensionsPtr);
        }

        if (requireSwapchain)
        {
            var hasSwapchain = false;
            foreach (var ext in extensions)
            {
                var name = Marshal.PtrToStringAnsi((nint)ext.ExtensionName);
                if (name == "VK_KHR_swapchain")
                {
                    hasSwapchain = true;
                    break;
                }
            }
            if (!hasSwapchain) return false;
        }

        return true;
    }

    private unsafe (uint? graphics, uint? compute) FindQueueFamilies(PhysicalDevice device, KhrSurface? khrSurface, SurfaceKHR? surface)
    {
        var vk = _vkInstance.Vk;

        uint queueFamilyCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, queueFamiliesPtr);
        }

        uint? graphicsFamily = null;
        uint? computeFamily = null;

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            var family = queueFamilies[i];

            // Check for graphics support
            if ((family.QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                // If we have a surface, also check for present support
                if (khrSurface != null && surface != null)
                {
                    khrSurface.GetPhysicalDeviceSurfaceSupport(device, i, surface.Value, out var presentSupport);
                    if (presentSupport)
                    {
                        graphicsFamily = i;
                    }
                }
                else
                {
                    graphicsFamily = i;
                }
            }

            // Check for compute support (prefer dedicated compute queue)
            if ((family.QueueFlags & QueueFlags.ComputeBit) != 0)
            {
                if (computeFamily == null || (family.QueueFlags & QueueFlags.GraphicsBit) == 0)
                {
                    computeFamily = i;
                }
            }
        }

        return (graphicsFamily, computeFamily ?? graphicsFamily);
    }

    private unsafe void CreateLogicalDevice(KhrSurface? khrSurface, SurfaceKHR? surface, bool requireSwapchain)
    {
        var vk = _vkInstance.Vk;

        var (graphicsFamily, computeFamily) = FindQueueFamilies(_physicalDevice, khrSurface, surface);
        _graphicsQueueFamily = graphicsFamily ?? computeFamily!.Value;
        _computeQueueFamily = computeFamily ?? graphicsFamily!.Value;

        // Create queue create infos
        var uniqueFamilies = new HashSet<uint> { _graphicsQueueFamily, _computeQueueFamily };
        var queueCreateInfos = new DeviceQueueCreateInfo[uniqueFamilies.Count];
        var queuePriority = 1.0f;

        var i = 0;
        foreach (var family in uniqueFamilies)
        {
            queueCreateInfos[i++] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = family,
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
        }

        var deviceFeatures = new PhysicalDeviceFeatures();

        // Required extensions
        var extensions = new List<string>();

        if (requireSwapchain)
        {
            extensions.Add("VK_KHR_swapchain");
        }

        // MoltenVK portability subset (macOS)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            extensions.Add("VK_KHR_portability_subset");
        }

        var extensionPtrs = new nint[extensions.Count];
        for (var j = 0; j < extensions.Count; j++)
        {
            extensionPtrs[j] = Marshal.StringToHGlobalAnsi(extensions[j]);
        }

        fixed (DeviceQueueCreateInfo* queueCreateInfosPtr = queueCreateInfos)
        fixed (nint* extensionsPtrPtr = extensionPtrs)
        {
            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)queueCreateInfos.Length,
                PQueueCreateInfos = queueCreateInfosPtr,
                PEnabledFeatures = &deviceFeatures,
                EnabledExtensionCount = (uint)extensions.Count,
                PpEnabledExtensionNames = (byte**)extensionsPtrPtr,
                EnabledLayerCount = 0
            };

            var result = vk.CreateDevice(_physicalDevice, &createInfo, null, out _device);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create logical device: {result}");
            }
        }

        foreach (var ptr in extensionPtrs)
        {
            Marshal.FreeHGlobal(ptr);
        }

        // Get queues
        vk.GetDeviceQueue(_device, _graphicsQueueFamily, 0, out _graphicsQueue);
        vk.GetDeviceQueue(_device, _computeQueueFamily, 0, out _computeQueue);

        // Create transfer command pool
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _graphicsQueueFamily,
            Flags = CommandPoolCreateFlags.TransientBit
        };

        var result2 = vk.CreateCommandPool(_device, &poolInfo, null, out _transferPool);
        if (result2 != Result.Success)
            throw new Exception($"Failed to create transfer command pool: {result2}");

        _log.Debug("Logical device and queues created");
    }

    public unsafe void Dispose()
    {
        _vkInstance.Vk.DeviceWaitIdle(_device);
        _vkInstance.Vk.DestroyCommandPool(_device, _transferPool, null);
        _vkInstance.Vk.DestroyDevice(_device, null);
    }
}
