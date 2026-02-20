using System.Numerics;

namespace DerpLib.ImGui.Input;

/// <summary>
/// Per-viewport input state filled each frame by ViewportInputRouter.
/// All mouse coordinates are in viewport-local space (0,0 = top-left of viewport).
/// </summary>
public unsafe struct ImInput
{
    //=== Mouse (viewport-local coordinates) ===

    /// <summary>
    /// Mouse position relative to viewport top-left.
    /// </summary>
    public Vector2 MousePos;

    /// <summary>
    /// Mouse movement since last frame.
    /// </summary>
    public Vector2 MouseDelta;

    /// <summary>
    /// True only on the frame left mouse button was pressed.
    /// </summary>
    public bool MousePressed;

    /// <summary>
    /// True while left mouse button is held down.
    /// </summary>
    public bool MouseDown;

    /// <summary>
    /// True only on the frame left mouse button was released.
    /// </summary>
    public bool MouseReleased;

    /// <summary>
    /// Right mouse button - pressed this frame.
    /// </summary>
    public bool MouseRightPressed;

    /// <summary>
    /// Right mouse button - currently held.
    /// </summary>
    public bool MouseRightDown;

    /// <summary>
    /// Right mouse button - released this frame.
    /// </summary>
    public bool MouseRightReleased;

    /// <summary>
    /// Middle mouse button - pressed this frame.
    /// </summary>
    public bool MouseMiddlePressed;

    /// <summary>
    /// Middle mouse button - currently held.
    /// </summary>
    public bool MouseMiddleDown;

    /// <summary>
    /// Middle mouse button - released this frame.
    /// </summary>
    public bool MouseMiddleReleased;

    /// <summary>
    /// Scroll wheel delta this frame. Positive = scroll up.
    /// </summary>
    public float ScrollDelta;
    
    /// <summary>
    /// Horizontal scroll delta this frame. Positive = scroll right.
    /// </summary>
    public float ScrollDeltaX;

    /// <summary>
    /// True if this is a double-click (two clicks within time/distance threshold).
    /// </summary>
    public bool IsDoubleClick;

    //=== Keyboard (only valid for focused viewport) ===

    /// <summary>
    /// Navigation and editing keys - pressed this frame.
    /// </summary>
    public bool KeyBackspace;
    public bool KeyDelete;
    public bool KeyEnter;
    public bool KeyEscape;
    public bool KeyTab;

    /// <summary>
    /// Editing keys - currently held down.
    /// Used for key-repeat behaviors (delete/backspace acceleration).
    /// </summary>
    public bool KeyBackspaceDown;
    public bool KeyDeleteDown;

    /// <summary>
    /// Arrow keys - pressed this frame.
    /// </summary>
    public bool KeyLeft;
    public bool KeyRight;
    public bool KeyUp;
    public bool KeyDown;

    /// <summary>
    /// Text navigation keys - pressed this frame.
    /// </summary>
    public bool KeyHome;
    public bool KeyEnd;
    public bool KeyPageUp;
    public bool KeyPageDown;

    /// <summary>
    /// Modifier keys - currently held (not pressed detection).
    /// </summary>
    public bool KeyCtrl;
    public bool KeyShift;
    public bool KeyAlt;

    /// <summary>
    /// Common shortcuts - pressed this frame with Ctrl held.
    /// </summary>
    public bool KeyCtrlA;
    public bool KeyCtrlC;
    public bool KeyCtrlD;
    public bool KeyCtrlV;
    public bool KeyCtrlX;
    public bool KeyCtrlZ;
    public bool KeyCtrlY;
    public bool KeyCtrlB;
    public bool KeyCtrlI;
    public bool KeyCtrlU;

    //=== Text Input ===

    /// <summary>
    /// Text input characters this frame. Fixed-size to avoid allocation.
    /// Use InputCharCount to know how many are valid.
    /// </summary>
    public fixed char InputChars[16];

    /// <summary>
    /// Number of valid characters in InputChars this frame.
    /// </summary>
    public int InputCharCount;

    //=== Timing ===

    /// <summary>
    /// Time since last frame in seconds.
    /// </summary>
    public float DeltaTime;

    /// <summary>
    /// Total elapsed time in seconds since start.
    /// </summary>
    public float Time;

    //=== Helper Methods ===

    /// <summary>
    /// Add a character to the input buffer. Returns false if buffer is full.
    /// </summary>
    public bool AddInputChar(char c)
    {
        if (InputCharCount >= 16)
            return false;

        InputChars[InputCharCount++] = c;
        return true;
    }

    /// <summary>
    /// Clear all input state for a new frame.
    /// </summary>
    public void Clear()
    {
        // Mouse
        MousePos = Vector2.Zero;
        MouseDelta = Vector2.Zero;
        MousePressed = false;
        MouseDown = false;
        MouseReleased = false;
        MouseRightPressed = false;
        MouseRightDown = false;
        MouseRightReleased = false;
        MouseMiddlePressed = false;
        MouseMiddleDown = false;
        MouseMiddleReleased = false;
        ScrollDelta = 0;
        ScrollDeltaX = 0;
        IsDoubleClick = false;

        // Keyboard
        KeyBackspace = false;
        KeyDelete = false;
        KeyEnter = false;
        KeyEscape = false;
        KeyTab = false;
        KeyBackspaceDown = false;
        KeyDeleteDown = false;
        KeyLeft = false;
        KeyRight = false;
        KeyUp = false;
        KeyDown = false;
        KeyHome = false;
        KeyEnd = false;
        KeyPageUp = false;
        KeyPageDown = false;
        KeyCtrl = false;
        KeyShift = false;
        KeyAlt = false;
        KeyCtrlA = false;
        KeyCtrlC = false;
        KeyCtrlD = false;
        KeyCtrlV = false;
        KeyCtrlX = false;
        KeyCtrlZ = false;
        KeyCtrlY = false;
        KeyCtrlB = false;
        KeyCtrlI = false;
        KeyCtrlU = false;

        // Text input
        InputCharCount = 0;

        // Timing preserved (set by router)
    }
}
