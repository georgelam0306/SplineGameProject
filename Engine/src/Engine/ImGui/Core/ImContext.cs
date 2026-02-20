using System.Numerics;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Rendering;
using DerpLib.ImGui.Viewport;
using DerpLib.ImGui.Docking;
using DerpLib.ImGui.Windows;
using DerpLib.Sdf;
using DerpLib.Shaders;
using DerpLib.Text;
using Serilog;
using VkInstance = DerpLib.Core.VkInstance;
using VkDevice = DerpLib.Core.VkDevice;
using MemoryAllocator = DerpLib.Memory.MemoryAllocator;
using DescriptorCache = DerpLib.Core.DescriptorCache;

namespace DerpLib.ImGui.Core;

/// <summary>
/// Central UI state shared across all viewports.
/// Singleton that owns WindowManager, Style, and all UI state.
/// </summary>
public sealed class ImContext
{
    //=== Singleton ===

    private static ImContext? _instance;

    /// <summary>
    /// The singleton ImContext instance.
    /// </summary>
    public static ImContext Instance => _instance ??= new ImContext();

    /// <summary>
    /// Reset the singleton (for testing or reinitialization).
    /// </summary>
    public static void Reset()
    {
        _instance = null;
    }

    //=== Owned Systems ===

    /// <summary>
    /// Manages all windows across all viewports.
    /// </summary>
    public ImWindowManager WindowManager { get; } = new();

    /// <summary>
    /// Manages docking layouts - 1-1 mapping with floating windows.
    /// </summary>
    public DockController DockController { get; } = new();

    /// <summary>
    /// Primary viewport (main application window).
    /// </summary>
    public ImViewport? PrimaryViewport { get; private set; }

    /// <summary>
    /// Currently active viewport for rendering.
    /// </summary>
    public ImViewport? CurrentViewport { get; private set; }

    /// <summary>
    /// All viewports (primary + extracted windows).
    /// </summary>
    private readonly List<ImViewport> _viewports = new();

    /// <summary>
    /// All registered viewports.
    /// </summary>
    public IReadOnlyList<ImViewport> Viewports => _viewports;

    /// <summary>
    /// Global input system for screen-space mouse tracking (for multi-viewport).
    /// May be null if not available on this platform.
    /// </summary>
    public SdlGlobalInput? GlobalInput { get; private set; }

    /// <summary>
    /// Global mouse position in screen coordinates.
    /// Returns Vector2.Zero if global input not available.
    /// </summary>
    public Vector2 GlobalMousePosition => GlobalInput?.GlobalMousePosition ?? Vector2.Zero;

    /// <summary>
    /// Manages multiple viewports for window extraction/re-docking.
    /// Null if multi-viewport is not enabled.
    /// </summary>
    public ViewportManager? ViewportManager { get; private set; }

    /// <summary>
    /// True if multi-viewport is enabled.
    /// </summary>
    public bool MultiViewportEnabled => ViewportManager != null;

    //=== Font ===

    /// <summary>
    /// The current font for text rendering. Null if no font is set.
    /// </summary>
    public Font? Font { get; private set; }

    public Font? SecondaryFont { get; private set; }

    /// <summary>
    /// Set the font for text rendering.
    /// </summary>
    public void SetFont(Font font)
    {
        Font = font;
        SecondaryFont = null;
    }

    public void SetFonts(Font primaryFont, Font secondaryFont)
    {
        Font = primaryFont;
        SecondaryFont = secondaryFont;
    }

    //=== Draw Lists (one per layer) ===

    private const int LayerCount = 4; // Must match ImDrawLayer enum count

    /// <summary>
    /// Draw lists for each layer. Index corresponds to ImDrawLayer enum value.
    /// </summary>
    private readonly ImDrawList[] _drawLists = new ImDrawList[LayerCount];

    /// <summary>
    /// Current draw layer for new draw commands.
    /// </summary>
    public ImDrawLayer CurrentLayer { get; private set; } = ImDrawLayer.WindowContent;

    /// <summary>
    /// Get the draw list for a specific layer.
    /// </summary>
    public ImDrawList GetDrawList(ImDrawLayer layer) => _drawLists[(int)layer];

    /// <summary>
    /// Get the current draw list (for the current layer).
    /// </summary>
    public ImDrawList CurrentDrawList => _drawLists[(int)CurrentLayer];

    /// <summary>
    /// Set the current draw layer for subsequent draw commands.
    /// </summary>
    public void SetDrawLayer(ImDrawLayer layer)
    {
        CurrentLayer = layer;
    }

    //=== Widget Interaction State ===

    /// <summary>
    /// ID of widget currently under the mouse cursor (hovered).
    /// 0 = no widget is hot.
    /// </summary>
    public int HotId;

    /// <summary>
    /// ID of widget currently being interacted with (mouse held down).
    /// 0 = no widget is active.
    /// </summary>
    public int ActiveId;

    /// <summary>
    /// ID of widget that has keyboard focus (for text input).
    /// 0 = no widget has focus.
    /// </summary>
    public int FocusId;

    /// <summary>
    /// ID of widget requesting focus next frame.
    /// </summary>
    public int FocusIdRequest;

    //=== Focus Traversal ===

    private const int MaxFocusTraversalIds = 2048;
    private const int MaxFocusScopeDepth = 8;

    private readonly int[] _focusTraversalIds = new int[MaxFocusTraversalIds];
    private int _focusTraversalCount;

    private readonly int[] _focusScopeStarts = new int[MaxFocusScopeDepth];
    private int _focusScopeDepth;

    /// <summary>
    /// Previous frame's HotId (for detecting hover changes).
    /// </summary>
    public int HotIdPrev;

    /// <summary>
    /// Previous frame's ActiveId (for detecting activation changes).
    /// </summary>
    public int ActiveIdPrev;

    //=== Mouse Ownership (ImGui-style) ===

    /// <summary>
    /// When the left mouse button is held, this is the widget id that owns the click/drag.
    /// Prevents newly spawned popovers/menus from reacting to the same click that opened them.
    /// </summary>
    public int MouseDownOwnerLeft;

    /// <summary>
    /// When the right mouse button is held, this is the widget id that owns the click/drag.
    /// </summary>
    public int MouseDownOwnerRight;

    /// <summary>
    /// When the middle mouse button is held, this is the widget id that owns the click/drag.
    /// </summary>
    public int MouseDownOwnerMiddle;

    /// <summary>
    /// True when an overlay (menu/popup) wants to capture the mouse for the rest of the frame.
    /// Used to prevent hover/click-through into widgets rendered after the overlay logic ran.
    /// </summary>
    public bool OverlayCaptureMouse;

    /// <summary>
    /// Viewport-space rect where overlay is capturing the mouse.
    /// </summary>
    public ImRect OverlayCaptureRect;

    /// <summary>
    /// True while drawing overlay widgets (menus/popovers). Overlay widgets are allowed to be hot/active even when overlay capture is enabled.
    /// </summary>
    public bool InOverlayScope;

    //=== ID Stack ===

    private const int MaxIdStackDepth = 64;
    private readonly int[] _idStack = new int[MaxIdStackDepth];
    private int _idStackDepth;

    //=== Style Stack ===

    private const int MaxStyleStackDepth = 32;
    private readonly ImStyle[] _styleStack = new ImStyle[MaxStyleStackDepth];
    private int _styleStackDepth;
    private ImStyle _currentStyle;

    //=== Clip Stack ===

    private const int MaxClipStackDepth = 64;
    private readonly ImRect[] _clipStack = new ImRect[MaxClipStackDepth];
    private int _clipStackDepth;

    //=== Input ===

    /// <summary>
    /// Input state for this frame (from current viewport).
    /// </summary>
    public ImInput Input
    {
        get
        {
            ImInput input = CurrentViewport?.Input ?? default;
            if (IsPointerCapturedByOverlay(input))
            {
                // Non-overlay widgets should not see pointer input while a popup captures this area.
                // Mouse position is moved outside viewport bounds so hover hit-tests also fail.
                input.MousePos = new Vector2(-1_000_000f, -1_000_000f);
                input.MouseDelta = Vector2.Zero;
                input.MousePressed = false;
                input.MouseReleased = false;
                input.MouseRightPressed = false;
                input.MouseRightReleased = false;
                input.MouseMiddlePressed = false;
                input.MouseMiddleReleased = false;
                input.IsDoubleClick = false;
                input.ScrollDelta = 0f;
                input.ScrollDeltaX = 0f;
            }

            return input;
        }
    }

    //=== Frame State ===

    /// <summary>
    /// Current frame number.
    /// </summary>
    public int FrameCount;

    /// <summary>
    /// Delta time for current frame.
    /// </summary>
    public float DeltaTime;

    /// <summary>
    /// True if we're between BeginFrame() and EndFrame().
    /// </summary>
    public bool InFrame;

    /// <summary>
    /// True if any widget is hovered this frame.
    /// </summary>
    public bool AnyHovered;

    /// <summary>
    /// True if any widget is active this frame.
    /// </summary>
    public bool AnyActive;

    //=== Input Capture Flags ===

    /// <summary>
    /// Set to true when UI wants to capture mouse input.
    /// Game code should check this before processing mouse.
    /// </summary>
    public bool WantCaptureMouse;

    /// <summary>
    /// Set to true when UI wants to capture keyboard input.
    /// Game code should check this before processing keyboard.
    /// </summary>
    public bool WantCaptureKeyboard;

    //=== Text Input State ===

    /// <summary>
    /// Caret blink timer for text inputs.
    /// </summary>
    public float CaretBlinkTimer;

    /// <summary>
    /// Whether caret is currently visible (toggles with timer).
    /// </summary>
    public bool CaretVisible;

    private const float CaretBlinkRate = 0.53f;

    //=== Properties ===

    /// <summary>
    /// Current style (top of style stack or default).
    /// </summary>
    public ref ImStyle Style => ref _currentStyle;

    /// <summary>
    /// Current clip rect (top of clip stack or full viewport).
    /// </summary>
    public ImRect ClipRect => _clipStackDepth > 0 ? _clipStack[_clipStackDepth - 1] : _viewportBounds;

    private ImRect _viewportBounds;

    //=== Initialization ===

    private ImContext()
    {
        _currentStyle = ImStyle.Default;
        InitializeDrawLists();
        InitializeGlobalInput();
    }

    private void InitializeDrawLists()
    {
        for (int i = 0; i < LayerCount; i++)
        {
            _drawLists[i] = new ImDrawList();
        }
    }

    private void InitializeGlobalInput()
    {
        try
        {
            GlobalInput = new SdlGlobalInput();
            Log.Debug("SDL global input initialized");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SDL global input not available");
            GlobalInput = null;
        }
    }

    //=== Viewport Management ===

    /// <summary>
    /// Set the primary viewport (main application window).
    /// </summary>
    public void SetPrimaryViewport(ImViewport viewport)
    {
        if (PrimaryViewport != null)
            throw new InvalidOperationException("Primary viewport already set");

        PrimaryViewport = viewport;
        _viewports.Add(viewport);
    }

    /// <summary>
    /// Enable multi-viewport support. Call after SetPrimaryViewport.
    /// </summary>
    public void EnableMultiViewport(
        VkInstance vkInstance,
        VkDevice vkDevice,
        MemoryAllocator memoryAllocator,
        DescriptorCache descriptorCache,
        PipelineCache pipelineCache,
        int framesInFlight,
        ComputeShader sdfShader)
    {
        if (PrimaryViewport == null)
            throw new InvalidOperationException("Primary viewport must be set before enabling multi-viewport");

        ViewportManager = new ViewportManager(
            Log.Logger,
            vkInstance,
            vkDevice,
            memoryAllocator,
            descriptorCache,
            pipelineCache,
            framesInFlight,
            sdfShader);

        Log.Debug("Multi-viewport enabled");
    }

    /// <summary>
    /// Add a secondary viewport (for extracted windows).
    /// </summary>
    public void AddViewport(ImViewport viewport)
    {
        _viewports.Add(viewport);
    }

    /// <summary>
    /// Remove a viewport.
    /// </summary>
    public void RemoveViewport(ImViewport viewport)
    {
        if (viewport == PrimaryViewport)
            throw new InvalidOperationException("Cannot remove primary viewport");

        _viewports.Remove(viewport);
    }

    /// <summary>
    /// Set the current viewport for rendering.
    /// </summary>
    public void SetCurrentViewport(ImViewport viewport)
    {
        CurrentViewport = viewport;
        _viewportBounds = new ImRect(0, 0, viewport.Size.X, viewport.Size.Y);
    }

    //=== Frame Lifecycle ===

    /// <summary>
    /// Begin a new frame. Call once per frame before any UI code.
    /// </summary>
    public void BeginFrame(float deltaTime)
    {
        if (InFrame)
            throw new InvalidOperationException("Already in frame. Did you forget to call EndFrame?");

        // Update global input (for multi-viewport mouse tracking)
        GlobalInput?.Update();

        InFrame = true;
        FrameCount++;
        DeltaTime = deltaTime;

        // Store previous frame state
        HotIdPrev = HotId;
        ActiveIdPrev = ActiveId;

        // Reset per-frame state
        HotId = 0;
        AnyHovered = false;
        AnyActive = ActiveId != 0;
        WantCaptureMouse = false;
        WantCaptureKeyboard = FocusId != 0;

        OverlayCaptureMouse = false;
        OverlayCaptureRect = ImRect.Zero;
        InOverlayScope = false;

        // Process focus request
        if (FocusIdRequest != 0)
        {
            FocusId = FocusIdRequest;
            FocusIdRequest = 0;
        }

        // Update caret blink
        CaretBlinkTimer += deltaTime;
        if (CaretBlinkTimer >= CaretBlinkRate)
        {
            CaretBlinkTimer -= CaretBlinkRate;
            CaretVisible = !CaretVisible;
        }

        // Default to primary viewport
        if (PrimaryViewport != null)
        {
            SetCurrentViewport(PrimaryViewport);
        }

        // Reset mouse ownership when buttons are not held.
        // Ownership persists across frames while the button is down to prevent click-through.
        if (!Input.MouseDown)
        {
            MouseDownOwnerLeft = 0;
        }
        if (!Input.MouseRightDown)
        {
            MouseDownOwnerRight = 0;
        }
        if (!Input.MouseMiddleDown)
        {
            MouseDownOwnerMiddle = 0;
        }

        // Clear stacks
        _idStackDepth = 0;
        _styleStackDepth = 0;
        _clipStackDepth = 0;

        _focusTraversalCount = 0;
        _focusScopeDepth = 0;

        // Clear draw lists and reset to default layer
        for (int i = 0; i < LayerCount; i++)
        {
            _drawLists[i].Clear();
        }
        CurrentLayer = ImDrawLayer.WindowContent;
    }

    /// <summary>
    /// End the current frame. Call after all UI code.
    /// </summary>
    public void EndFrame()
    {
        if (!InFrame)
            throw new InvalidOperationException("Not in frame. Did you forget to call BeginFrame?");

        // Validate stacks are balanced
        if (_idStackDepth != 0)
            throw new InvalidOperationException($"ID stack not balanced: depth = {_idStackDepth}");
        if (_styleStackDepth != 0)
            throw new InvalidOperationException($"Style stack not balanced: depth = {_styleStackDepth}");
        if (_clipStackDepth != 0)
            throw new InvalidOperationException($"Clip stack not balanced: depth = {_clipStackDepth}");

        InFrame = false;

        // Update capture flags based on final state
        if (HotId != 0 || ActiveId != 0)
            WantCaptureMouse = true;
        if (FocusId != 0)
            WantCaptureKeyboard = true;
    }

    /// <summary>
    /// Flush all draw lists to the SdfBuffer in layer order.
    /// Layers are flushed from background (0) to overlay (3) for correct z-ordering.
    /// </summary>
    public void FlushDrawLists(SdfBuffer buffer)
    {
        for (int i = 0; i < LayerCount; i++)
        {
            _drawLists[i].Flush(buffer);
        }
    }

    //=== ID Management ===

    /// <summary>
    /// Get a unique ID for a widget from its label.
    /// </summary>
    public int GetId(string label)
    {
        int seed = _idStackDepth > 0 ? _idStack[_idStackDepth - 1] : 0;
        return HashCombine(seed, StringHash(label));
    }

    /// <summary>
    /// Get a unique ID for a widget from an index.
    /// </summary>
    public int GetId(int index)
    {
        int seed = _idStackDepth > 0 ? _idStack[_idStackDepth - 1] : 0;
        return HashCombine(seed, index);
    }

    /// <summary>
    /// Push an ID onto the stack for hierarchical IDs.
    /// </summary>
    public void PushId(string id)
    {
        if (_idStackDepth >= MaxIdStackDepth)
            throw new InvalidOperationException("ID stack overflow");

        int newId = GetId(id);
        _idStack[_idStackDepth++] = newId;
    }

    /// <summary>
    /// Push an ID onto the stack for hierarchical IDs.
    /// </summary>
    public void PushId(int id)
    {
        if (_idStackDepth >= MaxIdStackDepth)
            throw new InvalidOperationException("ID stack overflow");

        int newId = GetId(id);
        _idStack[_idStackDepth++] = newId;
    }

    /// <summary>
    /// Pop an ID from the stack.
    /// </summary>
    public void PopId()
    {
        if (_idStackDepth <= 0)
            throw new InvalidOperationException("ID stack underflow");

        _idStackDepth--;
    }

    //=== Style Stack ===

    /// <summary>
    /// Push a style onto the stack.
    /// </summary>
    public void PushStyle(ImStyle style)
    {
        if (_styleStackDepth >= MaxStyleStackDepth)
            throw new InvalidOperationException("Style stack overflow");

        _styleStack[_styleStackDepth++] = _currentStyle;
        _currentStyle = style;
    }

    /// <summary>
    /// Pop a style from the stack.
    /// </summary>
    public void PopStyle()
    {
        if (_styleStackDepth <= 0)
            throw new InvalidOperationException("Style stack underflow");

        _currentStyle = _styleStack[--_styleStackDepth];
    }

    //=== Clip Stack ===

    /// <summary>
    /// Push a clip rect onto the stack. Intersects with current clip.
    /// </summary>
    public void PushClipRect(ImRect rect)
    {
        if (_clipStackDepth >= MaxClipStackDepth)
            throw new InvalidOperationException("Clip stack overflow");

        // Intersect with current clip
        var clipped = _clipStackDepth > 0 ? _clipStack[_clipStackDepth - 1].Intersect(rect) : rect;
        _clipStack[_clipStackDepth++] = clipped;
    }

    /// <summary>
    /// Push a clip rect onto the stack without intersecting with the current clip.
    /// Use sparingly (typically for viewport-space overlays/popovers rendered outside a window).
    /// </summary>
    public void PushClipRectOverride(ImRect rect)
    {
        if (_clipStackDepth >= MaxClipStackDepth)
            throw new InvalidOperationException("Clip stack overflow");

        _clipStack[_clipStackDepth++] = rect;
    }

    /// <summary>
    /// Pop a clip rect from the stack.
    /// </summary>
    public void PopClipRect()
    {
        if (_clipStackDepth <= 0)
            throw new InvalidOperationException("Clip stack underflow");

        _clipStackDepth--;
    }

    //=== Widget State Helpers ===

    /// <summary>
    /// Returns true if the widget is hot (hovered).
    /// </summary>
    public bool IsHot(int id) => HotId == id;

    /// <summary>
    /// Returns true if the widget is active (being interacted with).
    /// </summary>
    public bool IsActive(int id) => ActiveId == id;

    /// <summary>
    /// Returns true if the widget has keyboard focus.
    /// </summary>
    public bool IsFocused(int id) => FocusId == id;

    /// <summary>
    /// Set this widget as hot (hovered). Only takes effect if no other widget is active.
    /// </summary>
    public void SetHot(int id)
    {
        if (IsPointerCapturedByOverlay())
        {
            return;
        }

        // Allow "stealing" the hot state on the mouse-press frame so other widgets
        // can become active even if something else (like a text input) currently owns ActiveId.
        if (ActiveId == 0 || ActiveId == id || Input.MousePressed || Input.MouseRightPressed || Input.MouseMiddlePressed)
        {
            HotId = id;
            AnyHovered = true;
        }
    }

    /// <summary>
    /// Set this widget as active (being interacted with).
    /// </summary>
    public void SetActive(int id)
    {
        if (IsPointerCapturedByOverlay())
        {
            return;
        }

        // If a mouse button is currently held, do not allow other widgets to steal ownership.
        // This prevents click-through into freshly opened popovers/menus in the same frame.
        if (Input.MouseDown && MouseDownOwnerLeft != 0 && MouseDownOwnerLeft != id)
        {
            return;
        }
        if (Input.MouseRightDown && MouseDownOwnerRight != 0 && MouseDownOwnerRight != id)
        {
            return;
        }
        if (Input.MouseMiddleDown && MouseDownOwnerMiddle != 0 && MouseDownOwnerMiddle != id)
        {
            return;
        }

        // Allow overriding an existing active widget only on the initial press frame
        // (e.g., clicking a button while a text input is focused).
        if (ActiveId != 0 && ActiveId != id)
        {
            bool canOverrideOnThisPress =
                (Input.MousePressed && MouseDownOwnerLeft == 0) ||
                (Input.MouseRightPressed && MouseDownOwnerRight == 0) ||
                (Input.MouseMiddlePressed && MouseDownOwnerMiddle == 0);

            if ((Input.MouseDown || Input.MouseRightDown || Input.MouseMiddleDown) && !canOverrideOnThisPress)
            {
                return;
            }
        }

        if (FocusId != 0 && FocusId != id)
        {
            FocusId = 0;
            FocusIdRequest = 0;
        }

        ActiveId = id;
        AnyActive = true;

        // Claim mouse ownership on the press frame.
        if (Input.MousePressed && MouseDownOwnerLeft == 0)
        {
            MouseDownOwnerLeft = id;
        }
        if (Input.MouseRightPressed && MouseDownOwnerRight == 0)
        {
            MouseDownOwnerRight = id;
        }
        if (Input.MouseMiddlePressed && MouseDownOwnerMiddle == 0)
        {
            MouseDownOwnerMiddle = id;
        }
    }

    private bool IsPointerCapturedByOverlay()
    {
        var viewport = CurrentViewport;
        if (viewport == null)
        {
            return false;
        }

        return IsPointerCapturedByOverlay(viewport.Input);
    }

    private bool IsPointerCapturedByOverlay(in ImInput input)
    {
        if (InOverlayScope || !OverlayCaptureMouse)
        {
            return false;
        }

        if (OverlayCaptureRect.Width <= 0f || OverlayCaptureRect.Height <= 0f)
        {
            return false;
        }

        return OverlayCaptureRect.Contains(input.MousePos);
    }

    /// <summary>
    /// Clear the active widget.
    /// </summary>
    public void ClearActive()
    {
        ActiveId = 0;
    }

    public void PushOverlayScope()
    {
        InOverlayScope = true;
    }

    public void PopOverlayScope()
    {
        InOverlayScope = false;
    }

    public void AddOverlayCaptureRect(ImRect rectViewport)
    {
        if (rectViewport.Width <= 0f || rectViewport.Height <= 0f)
        {
            return;
        }

        OverlayCaptureMouse = true;
        if (OverlayCaptureRect.Width <= 0f || OverlayCaptureRect.Height <= 0f)
        {
            OverlayCaptureRect = rectViewport;
            return;
        }

        float x0 = MathF.Min(OverlayCaptureRect.X, rectViewport.X);
        float y0 = MathF.Min(OverlayCaptureRect.Y, rectViewport.Y);
        float x1 = MathF.Max(OverlayCaptureRect.Right, rectViewport.Right);
        float y1 = MathF.Max(OverlayCaptureRect.Bottom, rectViewport.Bottom);
        OverlayCaptureRect = new ImRect(x0, y0, x1 - x0, y1 - y0);
    }

    public void ConsumeMouseLeftPress()
    {
        if (CurrentViewport != null)
        {
            CurrentViewport.Input.MousePressed = false;
        }
    }

    public void ConsumeMouseLeftRelease()
    {
        if (CurrentViewport != null)
        {
            CurrentViewport.Input.MouseReleased = false;
        }
    }

    public void ConsumeMouseRightPress()
    {
        if (CurrentViewport != null)
        {
            CurrentViewport.Input.MouseRightPressed = false;
        }
    }

    public void ConsumeMouseRightRelease()
    {
        if (CurrentViewport != null)
        {
            CurrentViewport.Input.MouseRightReleased = false;
        }
    }

    public void ConsumeScroll()
    {
        if (CurrentViewport != null)
        {
            CurrentViewport.Input.ScrollDelta = 0f;
            CurrentViewport.Input.ScrollDeltaX = 0f;
        }
    }

    /// <summary>
    /// Request focus for a widget (will be granted next frame).
    /// </summary>
    public void RequestFocus(int id)
    {
        FocusIdRequest = id;
        CaretBlinkTimer = 0;
        CaretVisible = true;
    }

    /// <summary>
    /// Clear keyboard focus.
    /// </summary>
    public void ClearFocus()
    {
        FocusId = 0;
        FocusIdRequest = 0;
    }

    /// <summary>
    /// Reset caret blink (call when text changes to keep caret visible).
    /// </summary>
    public void ResetCaretBlink()
    {
        CaretBlinkTimer = 0;
        CaretVisible = true;
    }

    //=== Focus Traversal ===

    /// <summary>
    /// Register a widget id as focusable (for tab navigation).
    /// </summary>
    public void RegisterFocusable(int id)
    {
        if (id == 0)
        {
            return;
        }
        if (_focusTraversalCount >= MaxFocusTraversalIds)
        {
            return;
        }
        if (_focusTraversalCount > 0 && _focusTraversalIds[_focusTraversalCount - 1] == id)
        {
            return;
        }

        _focusTraversalIds[_focusTraversalCount++] = id;
    }

    /// <summary>
    /// Begin a focus scope. Calling <see cref="EndFocusScope"/> will process tab navigation
    /// for focusable widgets registered within the scope.
    /// </summary>
    public void BeginFocusScope()
    {
        if (_focusScopeDepth >= MaxFocusScopeDepth)
        {
            return;
        }

        _focusScopeStarts[_focusScopeDepth++] = _focusTraversalCount;
    }

    /// <summary>
    /// End a focus scope and, if Tab is pressed, advance focus to the next focusable widget in this scope.
    /// </summary>
    public void EndFocusScope()
    {
        if (_focusScopeDepth <= 0)
        {
            return;
        }

        int start = _focusScopeStarts[--_focusScopeDepth];
        int end = _focusTraversalCount;
        if (!Input.KeyTab)
        {
            return;
        }
        if (end <= start)
        {
            return;
        }

        bool backward = Input.KeyShift;
        int currentFocusId = FocusId;
        if (currentFocusId == 0)
        {
            int targetId = backward ? _focusTraversalIds[end - 1] : _focusTraversalIds[start];
            RequestFocus(targetId);
            return;
        }

        int focusedIndex = -1;
        for (int i = start; i < end; i++)
        {
            if (_focusTraversalIds[i] == currentFocusId)
            {
                focusedIndex = i;
                break;
            }
        }
        if (focusedIndex < 0)
        {
            return;
        }

        int nextIndex;
        if (!backward)
        {
            nextIndex = focusedIndex + 1;
            if (nextIndex >= end)
            {
                nextIndex = start;
            }
        }
        else
        {
            nextIndex = focusedIndex - 1;
            if (nextIndex < start)
            {
                nextIndex = end - 1;
            }
        }

        RequestFocus(_focusTraversalIds[nextIndex]);
    }

    //=== Hashing (FNV-1a) ===

    private static int StringHash(string str)
    {
        unchecked
        {
            const int fnvPrime = 16777619;
            int hash = (int)2166136261;

            foreach (char c in str)
            {
                hash ^= c;
                hash *= fnvPrime;
            }

            return hash;
        }
    }

    private static int HashCombine(int seed, int value)
    {
        unchecked
        {
            return seed ^ (value + (int)0x9e3779b9 + (seed << 6) + (seed >> 2));
        }
    }
}
