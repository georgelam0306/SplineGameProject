using System.Runtime.InteropServices;
using Serilog;
using Silk.NET.Core;
using Silk.NET.Vulkan;

namespace DerpLib.Core;

/// <summary>
/// Manages the Vulkan instance. Can be created with or without surface extensions.
/// </summary>
public sealed class VkInstance : IDisposable
{
    private readonly ILogger _log;
    private readonly Vk _vk;
    private Instance _instance;

    public Vk Vk => _vk;
    public Instance Instance => _instance;

    public VkInstance(ILogger log)
    {
        _log = log;
        _vk = Vk.GetApi();
    }

    /// <summary>
    /// Initialize for headless (compute-only) use.
    /// </summary>
    public unsafe void Initialize()
    {
        Initialize([]);
    }

    /// <summary>
    /// Initialize with additional extensions (e.g., surface extensions for presentation).
    /// </summary>
    public unsafe void Initialize(IReadOnlyList<string> additionalExtensions)
    {
        // Enable Metal Argument Buffers Tier 2 for higher descriptor limits on macOS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Environment.SetEnvironmentVariable("MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS", "1");
        }

        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)Marshal.StringToHGlobalAnsi("Engine Learning"),
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)Marshal.StringToHGlobalAnsi("LearningEngine"),
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version12
        };

        var extensions = new List<string>(additionalExtensions);

        // MoltenVK portability requirements (macOS)
        var instanceCreateFlags = InstanceCreateFlags.None;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            extensions.Add("VK_KHR_portability_enumeration");
            extensions.Add("VK_KHR_get_physical_device_properties2");
            instanceCreateFlags |= InstanceCreateFlags.EnumeratePortabilityBitKhr;
        }

        var extensionPtrs = new nint[extensions.Count];
        for (var i = 0; i < extensions.Count; i++)
        {
            extensionPtrs[i] = Marshal.StringToHGlobalAnsi(extensions[i]);
        }

        fixed (nint* extensionsPtrPtr = extensionPtrs)
        {
            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = (uint)extensions.Count,
                PpEnabledExtensionNames = (byte**)extensionsPtrPtr,
                EnabledLayerCount = 0,
                Flags = instanceCreateFlags
            };

            var result = _vk.CreateInstance(&createInfo, null, out _instance);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to create Vulkan instance: {result}");
            }
        }

        Marshal.FreeHGlobal((nint)appInfo.PApplicationName);
        Marshal.FreeHGlobal((nint)appInfo.PEngineName);
        foreach (var ptr in extensionPtrs)
        {
            Marshal.FreeHGlobal(ptr);
        }

        _log.Debug("Vulkan instance created");
    }

    public unsafe void Dispose()
    {
        _vk.DestroyInstance(_instance, null);
        _vk.Dispose();
    }
}
