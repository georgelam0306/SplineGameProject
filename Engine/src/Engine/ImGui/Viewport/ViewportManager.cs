using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Windows;
using DerpLib.Presentation;
using DerpLib.Shaders;
using Docking = DerpLib.ImGui.Docking;
using Serilog;
using VkDevice = DerpLib.Core.VkDevice;
using VkInstance = DerpLib.Core.VkInstance;
using MemoryAllocator = DerpLib.Memory.MemoryAllocator;
using DescriptorCache = DerpLib.Core.DescriptorCache;

namespace DerpLib.ImGui.Viewport;

/// <summary>
/// Owns secondary OS windows ("viewports") and their per-window Vulkan resources.
/// Viewport assignment is computed elsewhere (ViewportAssigner).
/// </summary>
public sealed class ViewportManager : IDisposable
{
    private readonly ILogger _log;
    private readonly VkInstance _vkInstance;
    private readonly VkDevice _vkDevice;
    private readonly MemoryAllocator _memoryAllocator;
    private readonly DescriptorCache _descriptorCache;
    private readonly PipelineCache _pipelineCache;
    private readonly int _framesInFlight;
    private readonly ComputeShader _sdfShader;

    private readonly List<SecondaryViewportEntry> _secondaryViewports = new();

    private struct SecondaryViewportEntry
    {
        public ViewportResources Resources;
        public ImViewport ImViewport;
    }

    public int SecondaryViewportCount => _secondaryViewports.Count;

    public bool TryGetImViewport(ViewportResources viewportResources, out ImViewport imViewport)
    {
        for (int viewportIndex = 0; viewportIndex < _secondaryViewports.Count; viewportIndex++)
        {
            if (ReferenceEquals(_secondaryViewports[viewportIndex].Resources, viewportResources))
            {
                imViewport = _secondaryViewports[viewportIndex].ImViewport;
                return true;
            }
        }

        imViewport = null!;
        return false;
    }

    internal bool TryGetViewportResourcesById(int resourcesId, out ViewportResources viewportResources)
    {
        for (int viewportIndex = 0; viewportIndex < _secondaryViewports.Count; viewportIndex++)
        {
            if (_secondaryViewports[viewportIndex].Resources.Id == resourcesId)
            {
                viewportResources = _secondaryViewports[viewportIndex].Resources;
                return true;
            }
        }

        viewportResources = null!;
        return false;
    }

    public ViewportManager(
        ILogger log,
        VkInstance vkInstance,
        VkDevice vkDevice,
        MemoryAllocator memoryAllocator,
        DescriptorCache descriptorCache,
        PipelineCache pipelineCache,
        int framesInFlight,
        ComputeShader sdfShader)
    {
        _log = log;
        _vkInstance = vkInstance;
        _vkDevice = vkDevice;
        _memoryAllocator = memoryAllocator;
        _descriptorCache = descriptorCache;
        _pipelineCache = pipelineCache;
        _framesInFlight = framesInFlight;
        _sdfShader = sdfShader;
    }

    public ViewportResources CreateViewportForWindow(ImWindow window)
    {
        _log.Information(
            "Creating secondary viewport for window '{Title}' at ({X}, {Y}) {W}x{H}",
            window.Title, window.Rect.X, window.Rect.Y, window.Rect.Width, window.Rect.Height);

        var viewport = new ViewportResources(
            _log,
            _vkInstance,
            _vkDevice,
            _memoryAllocator,
            _descriptorCache,
            _pipelineCache,
            _framesInFlight,
            (int)window.Rect.Width,
            (int)window.Rect.Height,
            window.Title);

        viewport.Initialize((int)window.Rect.X, (int)window.Rect.Y);
        viewport.InitializeSdf(_sdfShader);

        var imViewport = new ImViewport($"Viewport_{viewport.Id}")
        {
            Buffer = viewport.SdfBuffer!,
            Size = new Vector2(viewport.Window.Width, viewport.Window.Height),
            ContentScale = viewport.Window.ContentScaleX,
            ScreenPosition = viewport.Window.ScreenPosition,
            ResourcesId = viewport.Id
        };

        ImContext.Instance.AddViewport(imViewport);
        _secondaryViewports.Add(new SecondaryViewportEntry { Resources = viewport, ImViewport = imViewport });
        return viewport;
    }

    public ViewportResources CreateViewportForDockLayout(ImRect rect, string title)
    {
        _log.Information(
            "Creating secondary viewport for dock layout '{Title}' at ({X}, {Y}) {W}x{H}",
            title, rect.X, rect.Y, rect.Width, rect.Height);

        var viewport = new ViewportResources(
            _log,
            _vkInstance,
            _vkDevice,
            _memoryAllocator,
            _descriptorCache,
            _pipelineCache,
            _framesInFlight,
            (int)rect.Width,
            (int)rect.Height,
            title);

        viewport.Initialize((int)rect.X, (int)rect.Y);
        viewport.InitializeSdf(_sdfShader);

        var imViewport = new ImViewport($"Viewport_{viewport.Id}")
        {
            Buffer = viewport.SdfBuffer!,
            Size = new Vector2(viewport.Window.Width, viewport.Window.Height),
            ContentScale = viewport.Window.ContentScaleX,
            ScreenPosition = viewport.Window.ScreenPosition,
            ResourcesId = viewport.Id
        };

        ImContext.Instance.AddViewport(imViewport);
        _secondaryViewports.Add(new SecondaryViewportEntry { Resources = viewport, ImViewport = imViewport });
        return viewport;
    }

    public void DestroyViewport(ViewportResources viewport)
    {
        int entryIndex = FindEntryIndex(viewport);
        if (entryIndex < 0)
        {
            return;
        }

        var entry = _secondaryViewports[entryIndex];
        ImContext.Instance.RemoveViewport(entry.ImViewport);

        _secondaryViewports.RemoveAt(entryIndex);
        viewport.Dispose();

        _log.Information("Disposed secondary viewport {Id}", viewport.Id);
    }

    /// <summary>
    /// Poll events and handle resizes for all secondary viewports.
    /// Also sync OS window position/size back into the owning ImWindow.Rect.
    /// </summary>
    public void UpdateSecondaryViewports(ViewportAssigner viewportAssigner)
    {
        for (int viewportIndex = _secondaryViewports.Count - 1; viewportIndex >= 0; viewportIndex--)
        {
            var entry = _secondaryViewports[viewportIndex];
            var viewport = entry.Resources;

            // Primary window's PollEvents already pumped the global event queue this frame.
            // Secondary windows should not pump again, otherwise they can wipe typed characters/scroll deltas.
            viewport.Window.SyncInputState();

            if (viewport.Window.ShouldClose)
            {
                if (viewportAssigner.TryGetDockLayoutForViewport(viewport, out var layout))
                {
                    CloseDockLayoutWindows(layout, ImContext.Instance.WindowManager);
                }
                else if (viewportAssigner.TryGetWindowForViewport(viewport, out var window))
                {
                    window.IsOpen = false;
                }

                viewportAssigner.RemoveViewportForViewport(viewport);
                continue;
            }

            if (viewport.Window.ConsumeResized())
            {
                viewport.HandleResize();
            }

            if (viewportAssigner.TryGetDockLayoutForViewport(viewport, out var ownerLayout))
            {
                var pos = viewport.Window.ScreenPosition;
                ownerLayout.Rect = new ImRect(pos.X, pos.Y, viewport.Window.Width, viewport.Window.Height);
            }
            else if (viewportAssigner.TryGetWindowForViewport(viewport, out var ownerWindow))
            {
                var pos = viewport.Window.ScreenPosition;
                ownerWindow.Rect = new ImRect(pos.X, pos.Y, viewport.Window.Width, viewport.Window.Height);
            }

            // Keep ImViewport state and input up to date for the upcoming UI build.
            entry.ImViewport.Size = new Vector2(viewport.Window.Width, viewport.Window.Height);
            entry.ImViewport.ScreenPosition = viewport.Window.ScreenPosition;
            entry.ImViewport.ContentScale = viewport.Window.ContentScaleX;
            var input = new Input.ImInput
            {
                MousePos = viewport.Window.MousePosition,
                MouseDelta = viewport.Window.MouseDelta,
                MouseDown = viewport.Window.IsMouseButtonDown(Silk.NET.Input.MouseButton.Left),
                MousePressed = viewport.Window.IsMouseButtonPressed(Silk.NET.Input.MouseButton.Left),
                MouseReleased = viewport.Window.IsMouseButtonReleased(Silk.NET.Input.MouseButton.Left),
                MouseRightDown = viewport.Window.IsMouseButtonDown(Silk.NET.Input.MouseButton.Right),
                MouseRightPressed = viewport.Window.IsMouseButtonPressed(Silk.NET.Input.MouseButton.Right),
                MouseRightReleased = viewport.Window.IsMouseButtonReleased(Silk.NET.Input.MouseButton.Right),
                MouseMiddleDown = viewport.Window.IsMouseButtonDown(Silk.NET.Input.MouseButton.Middle),
                MouseMiddlePressed = viewport.Window.IsMouseButtonPressed(Silk.NET.Input.MouseButton.Middle),
                MouseMiddleReleased = viewport.Window.IsMouseButtonReleased(Silk.NET.Input.MouseButton.Middle),
                ScrollDelta = viewport.Window.ScrollDelta,
                ScrollDeltaX = viewport.Window.ScrollDeltaX,

                KeyBackspace = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Backspace),
                KeyDelete = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Delete),
                KeyEnter = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Enter),
                KeyEscape = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Escape),
                KeyTab = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Tab),
                KeyBackspaceDown = viewport.Window.IsKeyDown(Silk.NET.Input.Key.Backspace),
                KeyDeleteDown = viewport.Window.IsKeyDown(Silk.NET.Input.Key.Delete),

                KeyLeft = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Left),
                KeyRight = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Right),
                KeyUp = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Up),
                KeyDown = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Down),

                KeyHome = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Home),
                KeyEnd = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.End),
                KeyPageUp = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.PageUp),
                KeyPageDown = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.PageDown),

                KeyCtrl =
                    viewport.Window.IsKeyDown(Silk.NET.Input.Key.ControlLeft) || viewport.Window.IsKeyDown(Silk.NET.Input.Key.ControlRight) ||
                    viewport.Window.IsKeyDown(Silk.NET.Input.Key.SuperLeft) || viewport.Window.IsKeyDown(Silk.NET.Input.Key.SuperRight),
                KeyShift = viewport.Window.IsKeyDown(Silk.NET.Input.Key.ShiftLeft) || viewport.Window.IsKeyDown(Silk.NET.Input.Key.ShiftRight),
                KeyAlt = viewport.Window.IsKeyDown(Silk.NET.Input.Key.AltLeft) || viewport.Window.IsKeyDown(Silk.NET.Input.Key.AltRight),
            };

            if (input.KeyCtrl)
            {
                input.KeyCtrlA = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.A);
                input.KeyCtrlC = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.C);
                input.KeyCtrlD = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.D);
                input.KeyCtrlV = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.V);
                input.KeyCtrlX = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.X);
                input.KeyCtrlZ = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Z);
                input.KeyCtrlY = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.Y);
                input.KeyCtrlB = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.B);
                input.KeyCtrlI = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.I);
                input.KeyCtrlU = viewport.Window.IsKeyPressed(Silk.NET.Input.Key.U);
            }

            int inputCharCount = viewport.Window.InputCharCount;
            for (int i = 0; i < inputCharCount && i < 16; i++)
            {
                input.AddInputChar(viewport.Window.GetInputChar(i));
            }

            entry.ImViewport.Input = input;
            viewport.Window.ConsumePerFrameTextAndScroll();

            _secondaryViewports[viewportIndex] = entry;
        }
    }

    private static void CloseDockLayoutWindows(Docking.DockingLayout layout, ImWindowManager windowManager)
    {
        if (layout.Root == null)
        {
            return;
        }

        CloseDockNodeWindows(layout.Root, windowManager);
    }

    private static void CloseDockNodeWindows(Docking.ImDockNode node, ImWindowManager windowManager)
    {
        if (node is Docking.ImDockLeaf leaf)
        {
            for (int i = 0; i < leaf.WindowIds.Count; i++)
            {
                var window = windowManager.FindWindowById(leaf.WindowIds[i]);
                if (window != null)
                {
                    window.IsOpen = false;
                }
            }

            return;
        }

        if (node is Docking.ImDockSplit split)
        {
            if (split.First != null)
            {
                CloseDockNodeWindows(split.First, windowManager);
            }

            if (split.Second != null)
            {
                CloseDockNodeWindows(split.Second, windowManager);
            }
        }
    }

    public void ResetSecondaryBuffers()
    {
        for (int viewportIndex = 0; viewportIndex < _secondaryViewports.Count; viewportIndex++)
        {
            var buffer = _secondaryViewports[viewportIndex].Resources.SdfBuffer;
            buffer?.Reset();
        }
    }

    public int GetSecondaryViewportCount()
    {
        return _secondaryViewports.Count;
    }

    public void GetSecondaryViewportAt(int index, out ViewportResources viewportResources, out ImViewport imViewport)
    {
        var entry = _secondaryViewports[index];
        viewportResources = entry.Resources;
        imViewport = entry.ImViewport;
    }

    public void SetViewportContextForRendering(ImViewport imViewport, ViewportResources viewportResources)
    {
        if (viewportResources.SdfBuffer == null)
        {
            return;
        }

        // Keep ImViewport state in sync
        imViewport.Size = new Vector2(viewportResources.Window.Width, viewportResources.Window.Height);
        imViewport.ScreenPosition = viewportResources.Window.ScreenPosition;
        imViewport.ContentScale = viewportResources.Window.ContentScaleX;

        // Read input from this window (viewport-local)
        var input = new Input.ImInput
        {
            MousePos = viewportResources.Window.MousePosition,
            MouseDelta = viewportResources.Window.MouseDelta,
            MouseDown = viewportResources.Window.IsMouseButtonDown(Silk.NET.Input.MouseButton.Left),
            MousePressed = viewportResources.Window.IsMouseButtonPressed(Silk.NET.Input.MouseButton.Left),
            MouseReleased = viewportResources.Window.IsMouseButtonReleased(Silk.NET.Input.MouseButton.Left),
            MouseRightDown = viewportResources.Window.IsMouseButtonDown(Silk.NET.Input.MouseButton.Right),
            MouseRightPressed = viewportResources.Window.IsMouseButtonPressed(Silk.NET.Input.MouseButton.Right),
            MouseRightReleased = viewportResources.Window.IsMouseButtonReleased(Silk.NET.Input.MouseButton.Right),
            MouseMiddleDown = viewportResources.Window.IsMouseButtonDown(Silk.NET.Input.MouseButton.Middle),
            MouseMiddlePressed = viewportResources.Window.IsMouseButtonPressed(Silk.NET.Input.MouseButton.Middle),
            MouseMiddleReleased = viewportResources.Window.IsMouseButtonReleased(Silk.NET.Input.MouseButton.Middle),
            ScrollDelta = viewportResources.Window.ScrollDelta,
            ScrollDeltaX = viewportResources.Window.ScrollDeltaX,

            KeyBackspace = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.Backspace),
            KeyDelete = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.Delete),
            KeyEnter = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.Enter),
            KeyEscape = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.Escape),
            KeyTab = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.Tab),
            KeyBackspaceDown = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.Backspace),
            KeyDeleteDown = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.Delete),

            KeyLeft = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.Left),
            KeyRight = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.Right),
            KeyUp = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.Up),
            KeyDown = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.Down),

            KeyHome = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.Home),
            KeyEnd = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.End),
            KeyPageUp = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.PageUp),
            KeyPageDown = viewportResources.Window.IsKeyPressed(Silk.NET.Input.Key.PageDown),

            KeyCtrl =
                viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.ControlLeft) || viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.ControlRight) ||
                viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.SuperLeft) || viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.SuperRight),
            KeyShift = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.ShiftLeft) || viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.ShiftRight),
            KeyAlt = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.AltLeft) || viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.AltRight),
        };

                if (input.KeyCtrl)
                {
                    input.KeyCtrlA = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.A);
                    input.KeyCtrlC = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.C);
                    input.KeyCtrlD = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.D);
                    input.KeyCtrlV = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.V);
                    input.KeyCtrlX = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.X);
                    input.KeyCtrlZ = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.Z);
                    input.KeyCtrlY = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.Y);
                    input.KeyCtrlB = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.B);
                    input.KeyCtrlI = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.I);
                    input.KeyCtrlU = viewportResources.Window.IsKeyDown(Silk.NET.Input.Key.U);
                }

        int inputCharCount = viewportResources.Window.InputCharCount;
        for (int i = 0; i < inputCharCount && i < 16; i++)
        {
            input.AddInputChar(viewportResources.Window.GetInputChar(i));
        }

        imViewport.Input = input;

        // Set current viewport and clear draw buffer
        var ctx = ImContext.Instance;
        ctx.SetCurrentViewport(imViewport);
        viewportResources.SdfBuffer.Reset();
    }

    private int FindEntryIndex(ViewportResources viewport)
    {
        for (int entryIndex = 0; entryIndex < _secondaryViewports.Count; entryIndex++)
        {
            if (ReferenceEquals(_secondaryViewports[entryIndex].Resources, viewport))
            {
                return entryIndex;
            }
        }

        return -1;
    }

    public void Dispose()
    {
        for (int viewportIndex = _secondaryViewports.Count - 1; viewportIndex >= 0; viewportIndex--)
        {
            var entry = _secondaryViewports[viewportIndex];
            ImContext.Instance.RemoveViewport(entry.ImViewport);
            entry.Resources.Dispose();
            _secondaryViewports.RemoveAt(viewportIndex);
        }
    }
}
