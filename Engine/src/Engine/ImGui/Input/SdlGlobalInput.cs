using System.Numerics;
using Silk.NET.SDL;

namespace DerpLib.ImGui.Input;

/// <summary>
/// SDL-based implementation of global input.
/// Uses SDL_GetGlobalMouseState for screen-space mouse position.
/// </summary>
public sealed class SdlGlobalInput : IGlobalInput, IDisposable
{
    private readonly Sdl _sdl;

    // Current frame state
    private Vector2 _currentPos;
    private uint _currentButtons;

    // Previous frame state (for delta/pressed/released detection)
    private Vector2 _lastPos;
    private uint _lastButtons;

    // Scroll accumulator (filled by event handler, consumed by Update)
    private float _scrollAccumulator;
    private float _scrollDelta;

    // SDL button masks
    private const uint SDL_BUTTON_LMASK = 1 << 0;
    private const uint SDL_BUTTON_MMASK = 1 << 1;
    private const uint SDL_BUTTON_RMASK = 1 << 2;

    public Vector2 GlobalMousePosition => _currentPos;
    public Vector2 MouseDelta => _currentPos - _lastPos;
    public float ScrollDelta => _scrollDelta;

    public SdlGlobalInput()
    {
        _sdl = Sdl.GetApi();

        // Initialize SDL if not already done
        // SDL_INIT_VIDEO is required for mouse functions
        if (_sdl.Init(Sdl.InitVideo) < 0)
        {
            throw new InvalidOperationException($"Failed to initialize SDL: {_sdl.GetErrorS()}");
        }
    }

    public void Update()
    {
        // Store previous state
        _lastPos = _currentPos;
        _lastButtons = _currentButtons;

        // Get global mouse state (screen coordinates)
        int x, y;
        unsafe
        {
            _currentButtons = _sdl.GetGlobalMouseState(&x, &y);
        }
        _currentPos = new Vector2(x, y);

        // Consume scroll accumulator
        _scrollDelta = _scrollAccumulator;
        _scrollAccumulator = 0;
    }

    public bool IsMouseButtonDown(MouseButton button)
    {
        uint mask = GetButtonMask(button);
        return (_currentButtons & mask) != 0;
    }

    public bool IsMouseButtonPressed(MouseButton button)
    {
        uint mask = GetButtonMask(button);
        bool isDown = (_currentButtons & mask) != 0;
        bool wasDown = (_lastButtons & mask) != 0;
        return isDown && !wasDown;
    }

    public bool IsMouseButtonReleased(MouseButton button)
    {
        uint mask = GetButtonMask(button);
        bool isDown = (_currentButtons & mask) != 0;
        bool wasDown = (_lastButtons & mask) != 0;
        return !isDown && wasDown;
    }

    public void AddScrollDelta(float delta)
    {
        _scrollAccumulator += delta;
    }

    public void ResetScrollDelta()
    {
        _scrollAccumulator = 0;
        _scrollDelta = 0;
    }

    private static uint GetButtonMask(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => SDL_BUTTON_LMASK,
            MouseButton.Middle => SDL_BUTTON_MMASK,
            MouseButton.Right => SDL_BUTTON_RMASK,
            _ => 0
        };
    }

    public void Dispose()
    {
        _sdl.Quit();
        _sdl.Dispose();
    }
}
