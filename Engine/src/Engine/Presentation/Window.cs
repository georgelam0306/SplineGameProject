using System.Numerics;
using Serilog;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace DerpLib.Presentation;

public sealed class Window : IDisposable
{
    private readonly ILogger _log;
    private readonly IWindow _window;
    private bool _resized;
    private static readonly Silk.NET.GLFW.Glfw GlfwApi = Silk.NET.GLFW.Glfw.GetApi();

    // Input
    private IInputContext? _input;
    private IKeyboard? _keyboard;
    private IMouse? _mouse;
    private Vector2 _mousePosition;
    private Vector2 _lastMousePosition;
    private Vector2 _mouseDelta;
    private float _scrollDelta;
    private float _scrollDeltaX;

    // Mouse button state tracking (for pressed/released detection)
    private bool _leftDown, _leftDownPrev;
    private bool _rightDown, _rightDownPrev;
    private bool _middleDown, _middleDownPrev;

    // Keyboard character input buffer (for text input)
    private readonly char[] _inputCharBuffer = new char[16];
    private int _inputCharCount;

    // Keyboard key state tracking (bool arrays for O(1) lookup on all keys)
    private const int MaxKeyCode = 512;
    private readonly bool[] _keyState = new bool[MaxKeyCode];      // Current frame
    private readonly bool[] _keyStatePrev = new bool[MaxKeyCode];  // Previous frame

    public IWindow NativeWindow => _window;
    public bool ShouldClose => _window.IsClosing;

    /// <summary>Logical window size (for input/UI coordinates).</summary>
    public int Width => _window.Size.X;
    /// <summary>Logical window size (for input/UI coordinates).</summary>
    public int Height => _window.Size.Y;

    /// <summary>Framebuffer size in actual pixels (for Vulkan swapchain/viewport).</summary>
    public int FramebufferWidth => _window.FramebufferSize.X;
    /// <summary>Framebuffer size in actual pixels (for Vulkan swapchain/viewport).</summary>
    public int FramebufferHeight => _window.FramebufferSize.Y;

    /// <summary>Content scale (DPI scale factor, e.g., 2.0 on Retina).</summary>
    public float ContentScaleX => _window.Size.X > 0 ? (float)_window.FramebufferSize.X / _window.Size.X : 1f;
    public float ContentScaleY => _window.Size.Y > 0 ? (float)_window.FramebufferSize.Y / _window.Size.Y : 1f;

    /// <summary>Window position in screen coordinates.</summary>
    public Vector2 ScreenPosition => new(_window.Position.X, _window.Position.Y);

    /// <summary>
    /// True if window was resized since last call to ConsumeResized().
    /// </summary>
    public bool WasResized => _resized;

    // Input accessors
    public Vector2 MousePosition => _mousePosition;
    public Vector2 MouseDelta => _mouseDelta;
    public float ScrollDelta => _scrollDelta;
    public float ScrollDeltaX => _scrollDeltaX;

    public unsafe string? GetClipboardText()
    {
        var handle = (Silk.NET.GLFW.WindowHandle*)_window.Handle;
        return GlfwApi.GetClipboardString(handle);
    }

    public unsafe void SetClipboardText(string text)
    {
        var handle = (Silk.NET.GLFW.WindowHandle*)_window.Handle;
        GlfwApi.SetClipboardString(handle, text);
    }

    public Window(ILogger log, int width = 1280, int height = 720, string title = "Engine Learning", bool decorated = true)
    {
        _log = log;

        var options = WindowOptions.DefaultVulkan;
        options.Size = new Vector2D<int>(width, height);
        options.Title = title;

        // Secondary viewports use undecorated windows (no OS title bar)
        if (!decorated)
        {
            options.WindowBorder = WindowBorder.Hidden;
        }

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

        // Initialize input
        _input = _window.CreateInput();
        if (_input.Keyboards.Count > 0)
        {
            _keyboard = _input.Keyboards[0];
            _keyboard.KeyDown += OnKeyDown;
            _keyboard.KeyUp += OnKeyUp;
            _keyboard.KeyChar += OnKeyChar;

            // Seed key state once so IsKeyDown works even before the first KeyDown/KeyUp events are received.
            for (int i = 0; i < MaxKeyCode; i++)
            {
                _keyState[i] = _keyboard.IsKeyPressed((Key)i);
            }
        }
        if (_input.Mice.Count > 0)
        {
            _mouse = _input.Mice[0];
            _mouse.Scroll += OnMouseScroll;
        }

        _log.Information("Window initialized: {Width}x{Height}", Width, Height);
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        _scrollDelta += scroll.Y;
        _scrollDeltaX += scroll.X;
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        int code = (int)key;
        if ((uint)code >= (uint)MaxKeyCode)
        {
            return;
        }
        _keyState[code] = true;
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scancode)
    {
        int code = (int)key;
        if ((uint)code >= (uint)MaxKeyCode)
        {
            return;
        }
        _keyState[code] = false;
    }

    private void OnKeyChar(IKeyboard keyboard, char c)
    {
        if (_inputCharCount < _inputCharBuffer.Length)
        {
            _inputCharBuffer[_inputCharCount++] = c;
        }
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
        // Clear per-frame deltas before processing new events
        _scrollDelta = 0;
        _scrollDeltaX = 0;
        _mouseDelta = Vector2.Zero;
        _inputCharCount = 0;  // Clear character input buffer

        // Store previous button states
        _leftDownPrev = _leftDown;
        _rightDownPrev = _rightDown;
        _middleDownPrev = _middleDown;

        // Copy current key state to previous
        Array.Copy(_keyState, _keyStatePrev, MaxKeyCode);

        _window.DoEvents();

        // Update mouse position and delta
        if (_mouse != null)
        {
            _mousePosition = _mouse.Position;
            _mouseDelta = _mousePosition - _lastMousePosition;
            _lastMousePosition = _mousePosition;

            // Update button states
            _leftDown = _mouse.IsButtonPressed(MouseButton.Left);
            _rightDown = _mouse.IsButtonPressed(MouseButton.Right);
            _middleDown = _mouse.IsButtonPressed(MouseButton.Middle);
        }

        // Key state is updated via KeyDown/KeyUp events during DoEvents().
    }

    /// <summary>
    /// Update input state for this window without pumping the global event queue.
    /// Use this when another window already called DoEvents() for the frame.
    /// Preserves per-frame event accumulators (typed chars + scroll) until you call ConsumePerFrameTextAndScroll().
    /// </summary>
    public void SyncInputState()
    {
        _mouseDelta = Vector2.Zero;

        // Store previous button states
        _leftDownPrev = _leftDown;
        _rightDownPrev = _rightDown;
        _middleDownPrev = _middleDown;

        // Copy current key state to previous
        Array.Copy(_keyState, _keyStatePrev, MaxKeyCode);

        // Update mouse position and delta
        if (_mouse != null)
        {
            _mousePosition = _mouse.Position;
            _mouseDelta = _mousePosition - _lastMousePosition;
            _lastMousePosition = _mousePosition;

            // Update button states
            _leftDown = _mouse.IsButtonPressed(MouseButton.Left);
            _rightDown = _mouse.IsButtonPressed(MouseButton.Right);
            _middleDown = _mouse.IsButtonPressed(MouseButton.Middle);
        }

        // Key state is updated via KeyDown/KeyUp events when DoEvents() is called on the owning window.
    }

    /// <summary>
    /// Consume and clear per-frame scroll + typed character buffers.
    /// Call once after you have copied ScrollDelta/InputChars to your UI input struct.
    /// </summary>
    public void ConsumePerFrameTextAndScroll()
    {
        _scrollDelta = 0;
        _scrollDeltaX = 0;
        _inputCharCount = 0;
    }

    /// <summary>Check if a key is currently held down.</summary>
    public bool IsKeyDown(Key key)
    {
        int code = (int)key;
        if ((uint)code >= (uint)MaxKeyCode)
        {
            return false;
        }
        return _keyState[code];
    }

    /// <summary>Check if a key was pressed this frame (edge detection - only true on first frame).</summary>
    public bool IsKeyPressed(Key key)
    {
        int code = (int)key;
        if (code < 0 || code >= MaxKeyCode) return false;
        return _keyState[code] && !_keyStatePrev[code];
    }

    /// <summary>Check if a key was released this frame (edge detection - only true on release frame).</summary>
    public bool IsKeyReleased(Key key)
    {
        int code = (int)key;
        if (code < 0 || code >= MaxKeyCode) return false;
        return !_keyState[code] && _keyStatePrev[code];
    }

    /// <summary>Get the number of characters typed this frame.</summary>
    public int InputCharCount => _inputCharCount;

    /// <summary>Get a character from the input buffer.</summary>
    public char GetInputChar(int index) => index < _inputCharCount ? _inputCharBuffer[index] : '\0';

    /// <summary>Check if a mouse button is currently held down.</summary>
    public bool IsMouseButtonDown(MouseButton button) => button switch
    {
        MouseButton.Left => _leftDown,
        MouseButton.Right => _rightDown,
        MouseButton.Middle => _middleDown,
        _ => false
    };

    /// <summary>Check if a mouse button was pressed this frame (down now, not down last frame).</summary>
    public bool IsMouseButtonPressed(MouseButton button) => button switch
    {
        MouseButton.Left => _leftDown && !_leftDownPrev,
        MouseButton.Right => _rightDown && !_rightDownPrev,
        MouseButton.Middle => _middleDown && !_middleDownPrev,
        _ => false
    };

    /// <summary>Check if a mouse button was released this frame (not down now, was down last frame).</summary>
    public bool IsMouseButtonReleased(MouseButton button) => button switch
    {
        MouseButton.Left => !_leftDown && _leftDownPrev,
        MouseButton.Right => !_rightDown && _rightDownPrev,
        MouseButton.Middle => !_middleDown && _middleDownPrev,
        _ => false
    };

    /// <summary>Set the window title.</summary>
    public void SetTitle(string title) => _window.Title = title;

    /// <summary>Set the mouse cursor shape.</summary>
    public void SetCursor(StandardCursor cursor)
    {
        if (_mouse != null)
        {
            _mouse.Cursor.StandardCursor = cursor;
        }
    }

    public unsafe Silk.NET.GLFW.CursorModeValue GetCursorMode()
    {
        var handle = (Silk.NET.GLFW.WindowHandle*)_window.Handle;
        return (Silk.NET.GLFW.CursorModeValue)GlfwApi.GetInputMode(handle, Silk.NET.GLFW.CursorStateAttribute.Cursor);
    }

    public unsafe void SetCursorMode(Silk.NET.GLFW.CursorModeValue mode)
    {
        var handle = (Silk.NET.GLFW.WindowHandle*)_window.Handle;
        GlfwApi.SetInputMode(handle, Silk.NET.GLFW.CursorStateAttribute.Cursor, mode);
    }

    public unsafe void SetCursorPosition(double x, double y)
    {
        var handle = (Silk.NET.GLFW.WindowHandle*)_window.Handle;
        GlfwApi.SetCursorPos(handle, x, y);
    }

    public unsafe Vector2 GetCursorPosition()
    {
        var handle = (Silk.NET.GLFW.WindowHandle*)_window.Handle;
        double x = 0;
        double y = 0;
        GlfwApi.GetCursorPos(handle, out x, out y);
        return new Vector2((float)x, (float)y);
    }

    /// <summary>Set the window position in screen coordinates.</summary>
    public void SetPosition(int x, int y)
    {
        _window.Position = new Vector2D<int>(x, y);
    }

    /// <summary>Set the window size in logical coordinates.</summary>
    public void SetSize(int width, int height)
    {
        _window.Size = new Vector2D<int>(width, height);
    }

    /// <summary>Show the window (make visible).</summary>
    public void Show()
    {
        _window.IsVisible = true;
    }

    /// <summary>Hide the window.</summary>
    public void Hide()
    {
        _window.IsVisible = false;
    }

    /// <summary>Focus/raise the window.</summary>
    public void Focus()
    {
        // Silk.NET doesn't have a direct Focus method, but we can try to raise it
        _window.IsVisible = true;
    }

    public void Dispose()
    {
        _window.Resize -= OnResize;

        // Dispose input context (must be done before window disposal)
        if (_keyboard != null)
        {
            _keyboard.KeyDown -= OnKeyDown;
            _keyboard.KeyUp -= OnKeyUp;
            _keyboard.KeyChar -= OnKeyChar;
        }
        if (_mouse != null)
        {
            _mouse.Scroll -= OnMouseScroll;
        }
        _input?.Dispose();

        _window.Close();
        _window.Dispose();
    }
}
