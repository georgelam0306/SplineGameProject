using System.Numerics;
using DerpLib.ImGui.Core;
using Silk.NET.Input;

namespace DerpLib.ImGui.Windows;

/// <summary>
/// Manages all UI windows: creation, z-order, focus, and move/resize interactions.
/// </summary>
public sealed class ImWindowManager
{
    public const int MaxWindowCount = 64;
    private const float ScrollWheelSpeed = 48f;

    private readonly ImWindow?[] _windows = new ImWindow?[MaxWindowCount];
    private readonly int[] _zOrderedIndices = new int[MaxWindowCount];
    private int _windowCount;
    private int _nextZOrder;

    // Single active interaction (Dear ImGui style).
    private ImWindow? _movingWindow;
    private ImWindow? _sizingWindow;

    /// <summary>Currently focused window (topmost, receives input first).</summary>
    public ImWindow? FocusedWindow { get; private set; }

    /// <summary>Number of registered windows.</summary>
    public int WindowCount => _windowCount;

    /// <summary>Cursor that should be displayed based on current hover/interaction state.</summary>
    public StandardCursor DesiredCursor { get; set; } = StandardCursor.Default;

    /// <summary>
    /// Register a new window. Coordinates are screen-space (desktop) logical coordinates.
    /// </summary>
    public ImWindow RegisterWindow(
        string title,
        float screenX,
        float screenY,
        float width,
        float height,
        ImWindowFlags flags = ImWindowFlags.None,
        int? explicitId = null)
    {
        if (_windowCount >= MaxWindowCount)
        {
            throw new InvalidOperationException("Maximum window count exceeded");
        }

        var window = new ImWindow(title, screenX, screenY, width, height, flags, explicitId)
        {
            ZOrder = _nextZOrder++
        };

        _windows[_windowCount] = window;
        _zOrderedIndices[_windowCount] = _windowCount;
        _windowCount++;

        SortByZOrder();
        return window;
    }

    /// <summary>
    /// Find a window by title.
    /// </summary>
    public ImWindow? FindWindow(string title)
    {
        int id = HashTitle(title);
        for (int windowIndex = 0; windowIndex < _windowCount; windowIndex++)
        {
            if (_windows[windowIndex]?.Id == id)
            {
                return _windows[windowIndex];
            }
        }

        return null;
    }

    /// <summary>
    /// Find a window by ID.
    /// </summary>
    public ImWindow? FindWindowById(int id)
    {
        for (int windowIndex = 0; windowIndex < _windowCount; windowIndex++)
        {
            if (_windows[windowIndex]?.Id == id)
            {
                return _windows[windowIndex];
            }
        }

        return null;
    }

    /// <summary>
    /// Find or create a window by title.
    /// </summary>
    public ImWindow GetOrCreateWindow(
        string title,
        float screenX,
        float screenY,
        float width,
        float height,
        ImWindowFlags flags = ImWindowFlags.None,
        int? explicitId = null)
    {
        int id = explicitId ?? HashTitle(title);
        var existing = FindWindowById(id);
        if (existing != null)
        {
            return existing;
        }

        return RegisterWindow(title, screenX, screenY, width, height, flags, id);
    }

    /// <summary>
    /// Close and unregister a window.
    /// </summary>
    public void CloseWindow(ImWindow window)
    {
        for (int windowIndex = 0; windowIndex < _windowCount; windowIndex++)
        {
            if (_windows[windowIndex] == window)
            {
                // Shift remaining windows
                for (int shiftIndex = windowIndex; shiftIndex < _windowCount - 1; shiftIndex++)
                {
                    _windows[shiftIndex] = _windows[shiftIndex + 1];
                }

                _windows[_windowCount - 1] = null;
                _windowCount--;

                if (FocusedWindow == window)
                {
                    FocusedWindow = null;
                }

                if (_movingWindow == window)
                {
                    _movingWindow = null;
                }

                if (_sizingWindow == window)
                {
                    _sizingWindow = null;
                }

                SortByZOrder();
                return;
            }
        }
    }

    /// <summary>
    /// Bring a window to the front (highest z-order).
    /// </summary>
    public void BringToFront(ImWindow window)
    {
        window.ZOrder = _nextZOrder++;
        FocusedWindow = window;
        SortByZOrder();
    }

    /// <summary>
    /// Get windows in z-order (back to front). Zero-allocation.
    /// </summary>
    public WindowEnumerator GetWindowsBackToFront() => new(this, frontToBack: false);

    /// <summary>
    /// Get windows in reverse z-order (front to back) for hit testing. Zero-allocation.
    /// </summary>
    public WindowEnumerator GetWindowsFrontToBack() => new(this, frontToBack: true);

    /// <summary>
    /// Update window interactions (move/resize) using screen-space mouse state.
    /// Call once per frame.
    /// </summary>
    public void Update(ImContext ctx, Vector2 primaryViewportScreenPos, float titleBarHeight, float resizeGrabSize)
    {
        var input = ctx.Input;
        var globalInput = ctx.GlobalInput;

        Vector2 mousePos;
        bool mouseDown;
        bool mousePressed;
        bool mouseReleased;

        if (globalInput != null)
        {
            mousePos = globalInput.GlobalMousePosition;
            mouseDown = globalInput.IsMouseButtonDown(Input.MouseButton.Left);
            mousePressed = globalInput.IsMouseButtonPressed(Input.MouseButton.Left);
            mouseReleased = globalInput.IsMouseButtonReleased(Input.MouseButton.Left);
        }
        else
        {
            mousePos = primaryViewportScreenPos + input.MousePos;
            mouseDown = input.MouseDown;
            mousePressed = input.MousePressed;
            mouseReleased = input.MouseReleased;
        }

        DesiredCursor = StandardCursor.Default;

        // Active move
        if (_movingWindow != null)
        {
            if (mouseDown)
            {
                _movingWindow.Rect = new ImRect(
                    mousePos.X - _movingWindow.DragOffset.X,
                    mousePos.Y - _movingWindow.DragOffset.Y,
                    _movingWindow.Rect.Width,
                    _movingWindow.Rect.Height);
            }
            else
            {
                _movingWindow.IsDragging = false;
                _movingWindow = null;
            }

            return;
        }

        // Active resize
        if (_sizingWindow != null)
        {
            DesiredCursor = GetResizeCursor(_sizingWindow.ResizeEdge);

            if (mouseDown)
            {
                var delta = mousePos - _sizingWindow.ResizeStartMouse;
                _sizingWindow.Rect = _sizingWindow.ResizeStartRect;
                _sizingWindow.ApplyResize(_sizingWindow.ResizeEdge, delta);
            }
            else
            {
                _sizingWindow.IsResizing = false;
                _sizingWindow.ResizeEdge = 0;
                _sizingWindow = null;
                DesiredCursor = StandardCursor.Default;
            }

            return;
        }

        // Hover cursor for resize edges
        foreach (var window in GetWindowsFrontToBack())
        {
            if (!window.IsOpen)
            {
                continue;
            }

            // Docked windows should not participate in window-manager hit testing.
            // Dock interaction (tabs/chrome) is handled by the docking system.
            if (window.IsDocked)
            {
                continue;
            }

            int hoverEdge = window.HitTestResize(mousePos, resizeGrabSize);
            if (hoverEdge > 0)
            {
                DesiredCursor = GetResizeCursor(hoverEdge);
                break;
            }

            if (window.Rect.Contains(mousePos))
            {
                break;
            }
        }

        float scrollDelta = input.ScrollDelta;
        if (scrollDelta != 0f)
        {
            foreach (var window in GetWindowsFrontToBack())
            {
                if (!window.IsOpen || window.IsCollapsed || !window.HasVerticalScroll)
                {
                    continue;
                }

                if (!window.Rect.Contains(mousePos))
                {
                    continue;
                }

                // Only scroll when mouse is over the window body (not title bar).
                var titleRect = window.GetTitleBarRect(titleBarHeight);
                if (titleRect.Contains(mousePos))
                {
                    break;
                }

                float newScrollY = window.ScrollOffset.Y - scrollDelta * ScrollWheelSpeed;
                if (newScrollY < 0f)
                {
                    newScrollY = 0f;
                }
                else if (newScrollY > window.MaxScrollOffset.Y)
                {
                    newScrollY = window.MaxScrollOffset.Y;
                }

                window.ScrollOffset = new Vector2(window.ScrollOffset.X, newScrollY);
                break;
            }
        }

        // Start new interactions
        if (mousePressed)
        {
            foreach (var window in GetWindowsFrontToBack())
            {
                if (!window.IsOpen)
                {
                    continue;
                }

                // Docked windows do not participate in window-manager interactions.
                if (window.IsDocked)
                {
                    continue;
                }

                // Resize (not allowed for docked windows)
                int resizeEdge = window.HitTestResize(mousePos, resizeGrabSize);
                if (resizeEdge > 0 && !window.IsDocked)
                {
                    window.IsResizing = true;
                    window.ResizeEdge = resizeEdge;
                    window.ResizeStartRect = window.Rect;
                    window.ResizeStartMouse = mousePos;

                    _sizingWindow = window;
                    BringToFront(window);
                    return;
                }

                // Title bar drag (not allowed for docked windows)
                var titleRect = window.GetTitleBarRect(titleBarHeight);
                if (titleRect.Contains(mousePos) && !window.Flags.HasFlag(ImWindowFlags.NoMove) && !window.IsDocked)
                {
                    window.IsDragging = true;
                    window.DragOffset = mousePos - window.Rect.Position;

                    _movingWindow = window;
                    BringToFront(window);
                    return;
                }

                // Body click (focus only)
                if (window.Rect.Contains(mousePos))
                {
                    BringToFront(window);
                    return;
                }
            }
        }

        if (mouseReleased)
        {
            // Defensive: ensure no stale per-window flags linger.
            foreach (var window in GetWindowsBackToFront())
            {
                window.IsDragging = false;
                window.IsResizing = false;
                window.ResizeEdge = 0;
            }
        }
    }

    private void SortByZOrder()
    {
        // Simple insertion sort (windows array is small)
        for (int windowIndex = 0; windowIndex < _windowCount; windowIndex++)
        {
            _zOrderedIndices[windowIndex] = windowIndex;
        }

        for (int windowIndex = 1; windowIndex < _windowCount; windowIndex++)
        {
            int keyIndex = _zOrderedIndices[windowIndex];
            int keyZ = _windows[keyIndex]?.ZOrder ?? 0;
            int insertIndex = windowIndex - 1;

            while (insertIndex >= 0 && (_windows[_zOrderedIndices[insertIndex]]?.ZOrder ?? 0) > keyZ)
            {
                _zOrderedIndices[insertIndex + 1] = _zOrderedIndices[insertIndex];
                insertIndex--;
            }

            _zOrderedIndices[insertIndex + 1] = keyIndex;
        }
    }

    private static int HashTitle(string title)
    {
        unchecked
        {
            const int fnvPrime = 16777619;
            int hash = (int)2166136261;
            foreach (char c in title)
            {
                hash ^= c;
                hash *= fnvPrime;
            }

            return hash;
        }
    }

    private static StandardCursor GetResizeCursor(int edge)
    {
        // Silk.NET StandardCursor: Default=0, Arrow=1, IBeam=2, Crosshair=3, Hand=4, HResize=5, VResize=6
        // Diagonal resize cursors not available in GLFW/Silk.NET, use Crosshair as fallback
        return edge switch
        {
            1 or 2 => StandardCursor.HResize,   // W, E (horizontal)
            3 or 4 => StandardCursor.VResize,   // N, S (vertical)
            5 or 6 or 7 or 8 => StandardCursor.Crosshair, // Corners (fallback)
            _ => StandardCursor.Default
        };
    }

    /// <summary>
    /// Zero-allocation enumerator for iterating windows.
    /// </summary>
    public struct WindowEnumerator
    {
        private readonly ImWindowManager _manager;
        private readonly bool _frontToBack;
        private int _index;

        public WindowEnumerator(ImWindowManager manager, bool frontToBack)
        {
            _manager = manager;
            _frontToBack = frontToBack;
            _index = frontToBack ? manager._windowCount : -1;
        }

        public ImWindow Current { get; private set; } = null!;

        public bool MoveNext()
        {
            while (true)
            {
                if (_frontToBack)
                {
                    _index--;
                    if (_index < 0)
                    {
                        return false;
                    }
                }
                else
                {
                    _index++;
                    if (_index >= _manager._windowCount)
                    {
                        return false;
                    }
                }

                int windowIndex = _manager._zOrderedIndices[_index];
                var window = _manager._windows[windowIndex];
                if (window != null)
                {
                    Current = window;
                    return true;
                }
            }
        }

        public WindowEnumerator GetEnumerator() => this;
    }
}
