using Serilog;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace DerpLib.Presentation;

public sealed class Window : IDisposable
{
    private readonly ILogger _log;
    private readonly IWindow _window;
    private bool _resized;

    public IWindow NativeWindow => _window;
    public bool ShouldClose => _window.IsClosing;
    public int Width => _window.Size.X;
    public int Height => _window.Size.Y;

    /// <summary>
    /// True if window was resized since last call to ConsumeResized().
    /// </summary>
    public bool WasResized => _resized;

    public Window(ILogger log, int width = 1280, int height = 720, string title = "Engine Learning")
    {
        _log = log;

        var options = WindowOptions.DefaultVulkan;
        options.Size = new Vector2D<int>(width, height);
        options.Title = title;

        _window = Silk.NET.Windowing.Window.Create(options);
    }

    public void Initialize()
    {
        _window.Initialize();

        if (_window.VkSurface == null)
        {
            throw new Exception("Failed to create Vulkan surface. Is Vulkan supported?");
        }

        _window.Resize += OnResize;

        _log.Information("Window initialized: {Width}x{Height}", Width, Height);
    }

    private void OnResize(Vector2D<int> size)
    {
        _resized = true;
        _log.Debug("Window resized: {Width}x{Height}", size.X, size.Y);
    }

    /// <summary>
    /// Returns true if resized, and clears the flag.
    /// </summary>
    public bool ConsumeResized()
    {
        if (!_resized) return false;
        _resized = false;
        return true;
    }

    public void PollEvents()
    {
        _window.DoEvents();
    }

    public void Dispose()
    {
        _window.Resize -= OnResize;
        _window.Close();
        _window.Dispose();
    }
}
