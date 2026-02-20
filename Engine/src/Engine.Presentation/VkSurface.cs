using System.Runtime.InteropServices;
using Serilog;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using DerpLib.Core;

namespace DerpLib.Presentation;

/// <summary>
/// Manages the Vulkan surface for window presentation.
/// </summary>
public sealed class VkSurface : IDisposable
{
    private readonly ILogger _log;
    private readonly VkInstance _vkInstance;

    private KhrSurface _khrSurface = null!;
    private SurfaceKHR _surface;

    public KhrSurface KhrSurface => _khrSurface;
    public SurfaceKHR Surface => _surface;

    public VkSurface(ILogger log, VkInstance vkInstance)
    {
        _log = log;
        _vkInstance = vkInstance;
    }

    public unsafe void Initialize(IWindow window)
    {
        var vk = _vkInstance.Vk;
        var instance = _vkInstance.Instance;

        // Get KHR_surface extension
        if (!vk.TryGetInstanceExtension(instance, out _khrSurface))
        {
            throw new Exception("Failed to get KHR_surface extension");
        }

        // Create surface from window
        _surface = window.VkSurface!.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();

        _log.Debug("Vulkan surface created");
    }

    /// <summary>
    /// Gets the required instance extensions for surface creation.
    /// Call this before VkInstance.Initialize().
    /// </summary>
    public static unsafe IReadOnlyList<string> GetRequiredExtensions(IWindow window)
    {
        var windowExtensions = window.VkSurface!.GetRequiredExtensions(out var windowExtCount);
        var extensions = new List<string>();

        for (var i = 0; i < windowExtCount; i++)
        {
            extensions.Add(Marshal.PtrToStringAnsi((nint)windowExtensions[i])!);
        }

        return extensions;
    }

    public unsafe void Dispose()
    {
        _khrSurface.DestroySurface(_vkInstance.Instance, _surface, null);
    }
}
