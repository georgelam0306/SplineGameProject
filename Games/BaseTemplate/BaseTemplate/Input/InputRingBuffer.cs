using System.Numerics;
using Core;

namespace BaseTemplate.Presentation.Input;

[Flags]
public enum MouseButtonFlags : byte
{
    None = 0,
    LeftPressed = 1 << 0,
    LeftReleased = 1 << 1,
    LeftDown = 1 << 2,
    RightPressed = 1 << 3,
    RightReleased = 1 << 4,
    RightDown = 1 << 5,
}

[Flags]
public enum KeyFlags : ushort
{
    None = 0,
    A_Pressed = 1 << 0,
    P_Pressed = 1 << 1,
    Escape_Pressed = 1 << 2,
    S_Pressed = 1 << 3,
    H_Pressed = 1 << 4,
}

public struct FrameInputState
{
    public int FrameNumber;
    public Vector2 MouseScreenPos;
    public Fixed64Vec2 MouseWorldPos;
    public MouseButtonFlags MouseFlags;
    public KeyFlags KeyFlags;
    public bool MouseInGameWorld;
}

/// <summary>
/// Ring buffer storing input state for the last N render frames.
/// Allows querying input events across frame boundaries.
/// </summary>
public class InputRingBuffer
{
    private const int Capacity = 8;
    private readonly FrameInputState[] _buffer = new FrameInputState[Capacity];
    private int _head;  // Next write position
    private int _count;
    private int _currentFrame;

    // Cached press/release positions for quick access
    private Vector2 _lastLeftPressScreen;
    private Fixed64Vec2 _lastLeftPressWorld;
    private int _lastLeftPressFrame;
    private Vector2 _lastLeftReleaseScreen;
    private Fixed64Vec2 _lastLeftReleaseWorld;
    private int _lastLeftReleaseFrame;
    private Vector2 _lastRightPressScreen;
    private Fixed64Vec2 _lastRightPressWorld;
    private int _lastRightPressFrame;

    public int CurrentFrame => _currentFrame;
    public Vector2 LatestMouseScreen => _count > 0 ? _buffer[(_head - 1 + Capacity) % Capacity].MouseScreenPos : Vector2.Zero;

    /// <summary>
    /// Record input state for current render frame. Call once per render frame.
    /// </summary>
    public void Record(
        int frameNumber,
        Vector2 mouseScreenPos,
        Fixed64Vec2 mouseWorldPos,
        MouseButtonFlags mouseFlags,
        KeyFlags keyFlags,
        bool mouseInGameWorld)
    {
        _currentFrame = frameNumber;

        ref var slot = ref _buffer[_head];
        slot.FrameNumber = frameNumber;
        slot.MouseScreenPos = mouseScreenPos;
        slot.MouseWorldPos = mouseWorldPos;
        slot.MouseFlags = mouseFlags;
        slot.KeyFlags = keyFlags;
        slot.MouseInGameWorld = mouseInGameWorld;

        // Cache press/release positions
        if ((mouseFlags & MouseButtonFlags.LeftPressed) != 0)
        {
            _lastLeftPressScreen = mouseScreenPos;
            _lastLeftPressWorld = mouseWorldPos;
            _lastLeftPressFrame = frameNumber;
        }
        if ((mouseFlags & MouseButtonFlags.LeftReleased) != 0)
        {
            _lastLeftReleaseScreen = mouseScreenPos;
            _lastLeftReleaseWorld = mouseWorldPos;
            _lastLeftReleaseFrame = frameNumber;
        }
        if ((mouseFlags & MouseButtonFlags.RightPressed) != 0)
        {
            _lastRightPressScreen = mouseScreenPos;
            _lastRightPressWorld = mouseWorldPos;
            _lastRightPressFrame = frameNumber;
        }

        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    /// <summary>
    /// Check if left mouse was pressed within the last N frames.
    /// </summary>
    public bool WasLeftPressed(int withinFrames = Capacity)
    {
        return WasMouseFlag(MouseButtonFlags.LeftPressed, withinFrames);
    }

    /// <summary>
    /// Check if left mouse was released within the last N frames.
    /// </summary>
    public bool WasLeftReleased(int withinFrames = Capacity)
    {
        return WasMouseFlag(MouseButtonFlags.LeftReleased, withinFrames);
    }

    /// <summary>
    /// Check if right mouse was pressed within the last N frames.
    /// </summary>
    public bool WasRightPressed(int withinFrames = Capacity)
    {
        return WasMouseFlag(MouseButtonFlags.RightPressed, withinFrames);
    }

    /// <summary>
    /// Check if left mouse is currently down (latest frame).
    /// </summary>
    public bool IsLeftDown()
    {
        if (_count == 0) return false;
        return (_buffer[(_head - 1 + Capacity) % Capacity].MouseFlags & MouseButtonFlags.LeftDown) != 0;
    }

    /// <summary>
    /// Check if a key was pressed within the last N frames.
    /// </summary>
    public bool WasKeyPressed(KeyFlags key, int withinFrames = Capacity)
    {
        int framesToCheck = Math.Min(withinFrames, _count);
        for (int i = 0; i < framesToCheck; i++)
        {
            int idx = (_head - 1 - i + Capacity) % Capacity;
            if ((_buffer[idx].KeyFlags & key) != 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Get the screen position where left mouse was last pressed.
    /// </summary>
    public Vector2 GetLeftPressScreen() => _lastLeftPressScreen;

    /// <summary>
    /// Get the world position where left mouse was last pressed.
    /// </summary>
    public Fixed64Vec2 GetLeftPressWorld() => _lastLeftPressWorld;

    /// <summary>
    /// Get the frame number when left mouse was last pressed.
    /// </summary>
    public int GetLeftPressFrame() => _lastLeftPressFrame;

    /// <summary>
    /// Get the screen position where left mouse was last released.
    /// </summary>
    public Vector2 GetLeftReleaseScreen() => _lastLeftReleaseScreen;

    /// <summary>
    /// Get the world position where left mouse was last released.
    /// </summary>
    public Fixed64Vec2 GetLeftReleaseWorld() => _lastLeftReleaseWorld;

    /// <summary>
    /// Get the world position where right mouse was last pressed.
    /// </summary>
    public Fixed64Vec2 GetRightPressWorld() => _lastRightPressWorld;

    /// <summary>
    /// Check if mouse was in game world when left was pressed.
    /// </summary>
    public bool WasLeftPressInGameWorld()
    {
        int framesToCheck = Math.Min(Capacity, _count);
        for (int i = 0; i < framesToCheck; i++)
        {
            int idx = (_head - 1 - i + Capacity) % Capacity;
            ref var slot = ref _buffer[idx];
            if ((slot.MouseFlags & MouseButtonFlags.LeftPressed) != 0)
                return slot.MouseInGameWorld;
        }
        return false;
    }

    /// <summary>
    /// Check if mouse was in game world when right was pressed.
    /// </summary>
    public bool WasRightPressInGameWorld()
    {
        int framesToCheck = Math.Min(Capacity, _count);
        for (int i = 0; i < framesToCheck; i++)
        {
            int idx = (_head - 1 - i + Capacity) % Capacity;
            ref var slot = ref _buffer[idx];
            if ((slot.MouseFlags & MouseButtonFlags.RightPressed) != 0)
                return slot.MouseInGameWorld;
        }
        return false;
    }

    /// <summary>
    /// Clear events older than the given frame (mark as consumed).
    /// </summary>
    public void ConsumeUntil(int frame)
    {
        // Just update the last consumed frame markers if needed
        // The ring buffer naturally overwrites old data
    }

    private bool WasMouseFlag(MouseButtonFlags flag, int withinFrames)
    {
        int framesToCheck = Math.Min(withinFrames, _count);
        for (int i = 0; i < framesToCheck; i++)
        {
            int idx = (_head - 1 - i + Capacity) % Capacity;
            if ((_buffer[idx].MouseFlags & flag) != 0)
                return true;
        }
        return false;
    }
}
