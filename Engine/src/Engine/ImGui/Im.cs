using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using DerpLib.ImGui.Viewport;
using DerpLib.ImGui.Windows;
using DerpLib.Sdf;
using DerpLib.ImGui.Rendering;
using DerpLib.Shaders;
using DerpLib.Text;
using DerpLib.Rendering;
using Silk.NET.Input;
using VkInstance = DerpLib.Core.VkInstance;
using VkDevice = DerpLib.Core.VkDevice;
using MemoryAllocator = DerpLib.Memory.MemoryAllocator;
using DescriptorCache = DerpLib.Core.DescriptorCache;

namespace DerpLib.ImGui;

/// <summary>
/// Static facade for the ImGUI system.
/// All coordinates are in logical/window space - DPI scaling is handled internally.
/// </summary>
public static class Im
{
    // Current frame state
    private static bool _inFrame;
    private static float _mainDockInsetTop;
    private static float _scale = 1f;

    // Double-click detection (screen space).
    private const float DoubleClickTimeSeconds = 0.3f;
    private const float DoubleClickDistancePx = 5f;
    private static float _inputTimeSeconds;
    private static float _lastLeftClickTimeSeconds = -999f;
    private static Vector2 _lastLeftClickScreenPos;

    // Debug toggles (keep allocation-free; off by default).
    public static bool DebugClipTestWindows;

    // Transform stack (local -> viewport affine transform; allocation-free)
    private const int MaxTransformStackDepth = 64;
    private static readonly Matrix3x2[] _transformStack = new Matrix3x2[MaxTransformStackDepth];
    private static readonly Matrix3x2[] _inverseTransformStack = new Matrix3x2[MaxTransformStackDepth];
    private static int _transformStackDepth;
    private static Matrix3x2 _currentTransform = Matrix3x2.Identity;
    private static Matrix3x2 _currentInverseTransform = Matrix3x2.Identity;

    // Window state
    private static ImWindow? _currentWindow;
    private static ImRect _currentWindowRect;
    private static ImRect _currentWindowContentRect;
    private static bool _windowBeganLayout;
    private static bool _windowSwitchedViewport;
    private static ImViewport? _windowPreviousViewport;
    private static Vector4 _currentClipRect;
    private static bool _windowPushedId;
    private static bool _windowPushedClip;
    private static bool _windowPushedTransform;
    private static bool _windowPushedScrollTransform;

    private static ViewportAssigner? _viewportAssigner;
    private static bool _hasLastPrimaryViewportScreenPos;
    private static Vector2 _lastPrimaryViewportScreenPos;

    // Text input state storage
    private static readonly Dictionary<int, TextInputState> _textInputStates = new();

    public readonly struct ImTextInputStateSnapshot
    {
        public int CaretPos { get; }
        public int SelectionStart { get; }
        public int SelectionEnd { get; }
        public float ScrollOffsetX { get; }

        public ImTextInputStateSnapshot(int caretPos, int selectionStart, int selectionEnd, float scrollOffsetX)
        {
            CaretPos = caretPos;
            SelectionStart = selectionStart;
            SelectionEnd = selectionEnd;
            ScrollOffsetX = scrollOffsetX;
        }
    }

    private struct TextInputState
    {
        public int CaretPos;
        public int SelectionStart;
        public int SelectionEnd;
        public float ScrollOffsetX;
        public float BackspaceRepeatHeldTime;
        public float BackspaceRepeatTimer;
        public float DeleteRepeatHeldTime;
        public float DeleteRepeatTimer;
        public bool WasCopyDown;
        public bool WasPasteDown;
        public bool WasCutDown;
    }

    // Dropdown/ComboBox state
    private static int _openDropdownId;
    private static int _openDropdownFrame;
    private static float _dropdownScrollY;
    private static int _activeDropdownItemId;
    private static int _activeDropdownItemIndex = -1;
    private static int _openComboId;
    private static int _openComboFrame;
    private static float _comboScrollY;
    private static int _highlightedIndex = -1;

    // Scratch buffers for popup content (avoid per-frame allocations).
    private static readonly string[] _dropdownOptionsScratch = new string[8192];
    private static readonly string[] _comboSuggestionsScratch = new string[8192];
    private static readonly int[] _comboFilteredIndicesScratch = new int[8192];
    private static readonly char[] _comboFilterTextScratch = new char[256];

    // Deferred popup rendering - struct-based (no closures)
    private static bool _hasDeferredDropdown;
    private static DeferredDropdownState _deferredDropdown;
    private static bool _hasDeferredComboBox;
    private static DeferredComboBoxState _deferredComboBox;

    // Struct for deferred dropdown popup rendering
    private struct DeferredDropdownState
    {
        public ImRect PopupRect;
        public string[] Options;
        public int OptionCount;
        public int SelectedIndex;
        public int HoveredIndex;
        public float ScrollY;
        public int SortKey;
        public ImViewport? Viewport;
    }

    // Struct for deferred combobox popup rendering
    private struct DeferredComboBoxState
    {
        public ImRect PopupRect;
        public string[] Suggestions;
        public int SuggestionCount;
        public int[] FilteredIndices;
        public int FilteredCount;
        public char[] FilterText;
        public int FilterTextLength;
        public int HoveredIndex;
        public int HighlightedIndex;
        public float ScrollY;
        public int SortKey;
        public ImViewport? Viewport;
    }

    //=== Context Access ===

    /// <summary>
    /// The singleton ImContext.
    /// </summary>
    public static ImContext Context => ImContext.Instance;

    /// <summary>
    /// Current style.
    /// </summary>
    public static ref ImStyle Style => ref Context.Style;

    /// <summary>
    /// Window manager for creating and managing windows.
    /// </summary>
    public static ImWindowManager WindowManager => Context.WindowManager;

    /// <summary>
    /// Current content scale (DPI factor).
    /// </summary>
    public static float Scale => _scale;

    /// <summary>
    /// Current viewport being rendered to.
    /// </summary>
    public static ImViewport? CurrentViewport => Context.CurrentViewport;

    /// <summary>
    /// Set the current draw layer for subsequent draw commands.
    /// </summary>
    public static void SetDrawLayer(Rendering.ImDrawLayer layer)
    {
        EnsureInFrame();
        Context.SetDrawLayer(layer);
        var viewport = CurrentViewport;
        if (viewport == null)
        {
            return;
        }

        viewport.SetDrawLayer(layer);

        // Keep clip state consistent across layer switches. Many widgets draw their background/chrome
        // on different layers than their content, but should still respect the active clip rect.
        if (_currentClipRect.W >= 0)
        {
            viewport.CurrentDrawList.SetClipRect(_currentClipRect);
        }
        else
        {
            viewport.CurrentDrawList.ClearClipRect();
        }
    }

    /// <summary>
    /// Current window rect in window-local coordinates.
    /// Only valid between BeginWindow and EndWindow.
    /// </summary>
    public static ImRect WindowRect => _currentWindowRect;

    /// <summary>
    /// Current window content rect in window-local coordinates.
    /// Only valid between BeginWindow and EndWindow.
    /// </summary>
    public static ImRect WindowContentRect => _currentWindowContentRect;

    /// <summary>
    /// Current local -> viewport transform matrix for the active transform stack.
    /// </summary>
    public static Matrix3x2 CurrentTransformMatrix => _currentTransform;

    /// <summary>
    /// Current accumulated translation (origin transformed into viewport space).
    /// </summary>
    public static Vector2 CurrentTranslation => TransformPointLocalToViewport(Vector2.Zero);

    /// <summary>
    /// Push an affine local transform onto the stack (composes with current transform).
    /// The provided transform maps child-local coordinates into current-local coordinates.
    /// </summary>
    public static void PushTransform(Matrix3x2 transform)
    {
        EnsureInFrame();

        if (_transformStackDepth >= MaxTransformStackDepth)
        {
            throw new InvalidOperationException("Transform stack overflow");
        }

        _transformStack[_transformStackDepth] = _currentTransform;
        _inverseTransformStack[_transformStackDepth] = _currentInverseTransform;
        _transformStackDepth++;

        _currentTransform = Matrix3x2.Multiply(transform, _currentTransform);
        if (!Matrix3x2.Invert(_currentTransform, out _currentInverseTransform))
        {
            throw new InvalidOperationException("Transform is non-invertible");
        }
    }

    /// <summary>
    /// Push a translation transform onto the stack.
    /// </summary>
    public static void PushTransform(Vector2 translation)
    {
        PushTransform(Matrix3x2.CreateTranslation(translation));
    }

    public static void PushTransform(float x, float y)
    {
        PushTransform(Matrix3x2.CreateTranslation(x, y));
    }

    /// <summary>
    /// Push a scale transform around a pivot (local coordinates).
    /// </summary>
    public static void PushScale(float scaleX, float scaleY, Vector2 pivot)
    {
        PushTransform(Matrix3x2.CreateScale(scaleX, scaleY, pivot));
    }

    /// <summary>
    /// Push a uniform scale transform around a pivot (local coordinates).
    /// </summary>
    public static void PushScale(float scale, Vector2 pivot)
    {
        PushTransform(Matrix3x2.CreateScale(scale, pivot));
    }

    /// <summary>
    /// Push a rotation transform around a pivot (local coordinates).
    /// </summary>
    public static void PushRotation(float radians, Vector2 pivot)
    {
        PushTransform(Matrix3x2.CreateRotation(radians, pivot));
    }

    /// <summary>
    /// Pushes the inverse of the current transform.
    /// Useful for viewport-space overlays rendered while inside transformed local spaces.
    /// </summary>
    public static void PushInverseTransform()
    {
        PushTransform(_currentInverseTransform);
    }

    /// <summary>
    /// Pop the most recent transform.
    /// </summary>
    public static void PopTransform()
    {
        EnsureInFrame();

        if (_transformStackDepth <= 0)
        {
            throw new InvalidOperationException("Transform stack underflow");
        }

        _transformStackDepth--;
        _currentTransform = _transformStack[_transformStackDepth];
        _currentInverseTransform = _inverseTransformStack[_transformStackDepth];
    }

    //=== Initialization ===

    /// <summary>
    /// Initialize the ImGUI system. Reads all resources from Derp automatically.
    /// Call once after Derp.InitSdf() and before any Im calls.
    /// </summary>
    /// <param name="enableMultiViewport">If true, enables multi-viewport support (windows can be dragged outside main window).</param>
    public static void Initialize(bool enableMultiViewport = true)
    {
        // Read everything from Derp
        var buffer = Derp.SdfBuffer;
        var size = new Vector2(Derp.GetScreenWidth(), Derp.GetScreenHeight());
        var contentScale = Derp.GetContentScale();

        var viewport = new ImViewport("Primary", isPrimary: true)
        {
            Buffer = buffer,
            Size = size,
            ContentScale = contentScale
        };
        Context.SetPrimaryViewport(viewport);
        _scale = contentScale;

        // Automatically enable multi-viewport if requested
        if (enableMultiViewport)
        {
            Context.EnableMultiViewport(
                Derp.VkInstance,
                Derp.Device,
                Derp.MemoryAllocator,
                Derp.DescriptorCache,
                Derp.PipelineCache,
                Derp.FramesInFlight,
                Derp.SdfShader);

            _viewportAssigner = new ViewportAssigner(Context.ViewportManager!, ImWindowManager.MaxWindowCount);
        }
    }

    /// <summary>
    /// True if multi-viewport is enabled.
    /// </summary>
    public static bool MultiViewportEnabled => Context.MultiViewportEnabled;

    /// <summary>
    /// Set the font for text rendering.
    /// </summary>
    public static void SetFont(Font font) => Context.SetFont(font);
    public static void SetFonts(Font primaryFont, Font secondaryFont) => Context.SetFonts(primaryFont, secondaryFont);

    /// <summary>
    /// Get the current font, or null if not set.
    /// </summary>
    public static Font? Font => Context.Font;
    public static Font? SecondaryFont => Context.SecondaryFont;

    //=== Frame Lifecycle ===

    /// <summary>
    /// Begin an ImGUI frame. Call once per frame before any widgets.
    /// Automatically reads input from Derp.
    /// </summary>
    /// <param name="deltaTime">Frame delta time in seconds.</param>
    public static void Begin(float deltaTime)
    {
        if (_inFrame)
            throw new InvalidOperationException("Already in Im frame. Did you forget to call Im.End()?");

        var ctx = Context;
        var viewport = ctx.PrimaryViewport;

        if (viewport == null)
            throw new InvalidOperationException("Im.Initialize() must be called before Im.Begin()");

        _inputTimeSeconds += deltaTime;

        Vector2 mousePosLocal = Derp.GetMousePosition();
        Vector2 windowPos = Derp.GetWindowPosition();
        Vector2 mousePosScreen = ctx.GlobalInput != null ? ctx.GlobalInput.GlobalMousePosition : mousePosLocal + windowPos;

        // Read input directly from Derp
        bool ctrlOrSuperDown =
            Derp.IsKeyDown(Key.ControlLeft) || Derp.IsKeyDown(Key.ControlRight) ||
            Derp.IsKeyDown(Key.SuperLeft) || Derp.IsKeyDown(Key.SuperRight);

        bool isDoubleClick = false;
        if (Derp.IsMouseButtonPressed(0))
        {
            float timeSinceLastClick = _inputTimeSeconds - _lastLeftClickTimeSeconds;
            float distFromLastClick = Vector2.Distance(mousePosScreen, _lastLeftClickScreenPos);

            isDoubleClick = timeSinceLastClick < DoubleClickTimeSeconds
                         && distFromLastClick < DoubleClickDistancePx;

            _lastLeftClickTimeSeconds = _inputTimeSeconds;
            _lastLeftClickScreenPos = mousePosScreen;
        }

        var input = new ImInput
        {
            // Mouse
            MousePos = mousePosLocal,
            MouseDown = Derp.IsMouseButtonDown(0),
            MousePressed = Derp.IsMouseButtonPressed(0),
            MouseReleased = Derp.IsMouseButtonReleased(0),
            MouseRightDown = Derp.IsMouseButtonDown(1),
            MouseRightPressed = Derp.IsMouseButtonPressed(1),
            MouseRightReleased = Derp.IsMouseButtonReleased(1),
            MouseMiddleDown = Derp.IsMouseButtonDown(2),
            MouseMiddlePressed = Derp.IsMouseButtonPressed(2),
            MouseMiddleReleased = Derp.IsMouseButtonReleased(2),
            ScrollDelta = Derp.GetMouseScrollDelta(),
            ScrollDeltaX = Derp.GetMouseScrollDeltaX(),
            IsDoubleClick = isDoubleClick,

            // Keyboard - navigation/editing keys (use edge detection for single action per press)
            KeyBackspace = Derp.IsKeyPressed(Key.Backspace),
            KeyDelete = Derp.IsKeyPressed(Key.Delete),
            KeyEnter = Derp.IsKeyPressed(Key.Enter),
            KeyEscape = Derp.IsKeyPressed(Key.Escape),
            KeyTab = Derp.IsKeyPressed(Key.Tab),
            KeyBackspaceDown = Derp.IsKeyDown(Key.Backspace),
            KeyDeleteDown = Derp.IsKeyDown(Key.Delete),

            // Arrow keys (edge detection)
            KeyLeft = Derp.IsKeyPressed(Key.Left),
            KeyRight = Derp.IsKeyPressed(Key.Right),
            KeyUp = Derp.IsKeyPressed(Key.Up),
            KeyDown = Derp.IsKeyPressed(Key.Down),

            // Text navigation (edge detection)
            KeyHome = Derp.IsKeyPressed(Key.Home),
            KeyEnd = Derp.IsKeyPressed(Key.End),
            KeyPageUp = Derp.IsKeyPressed(Key.PageUp),
            KeyPageDown = Derp.IsKeyPressed(Key.PageDown),

            // Modifiers
            KeyCtrl = ctrlOrSuperDown,
            KeyShift = Derp.IsKeyDown(Key.ShiftLeft) || Derp.IsKeyDown(Key.ShiftRight),
            KeyAlt = Derp.IsKeyDown(Key.AltLeft) || Derp.IsKeyDown(Key.AltRight),

            // Shortcuts (edge detection)
            KeyCtrlA = Derp.IsKeyPressed(Key.A) && ctrlOrSuperDown,
            KeyCtrlC = Derp.IsKeyPressed(Key.C) && ctrlOrSuperDown,
            KeyCtrlD = Derp.IsKeyPressed(Key.D) && ctrlOrSuperDown,
            KeyCtrlV = Derp.IsKeyPressed(Key.V) && ctrlOrSuperDown,
            KeyCtrlX = Derp.IsKeyPressed(Key.X) && ctrlOrSuperDown,
            KeyCtrlZ = Derp.IsKeyPressed(Key.Z) && ctrlOrSuperDown,
            KeyCtrlY = Derp.IsKeyPressed(Key.Y) && ctrlOrSuperDown,
            KeyCtrlB = Derp.IsKeyPressed(Key.B) && ctrlOrSuperDown,
            KeyCtrlI = Derp.IsKeyPressed(Key.I) && ctrlOrSuperDown,
            KeyCtrlU = Derp.IsKeyPressed(Key.U) && ctrlOrSuperDown,

            DeltaTime = deltaTime
        };

        // Add character input
        int charCount = Derp.GetInputCharCount();
        for (int i = 0; i < charCount && i < 16; i++)
        {
            input.AddInputChar(Derp.GetInputChar(i));
        }

        viewport.Input = input;

        // Update viewport size in case window was resized
        viewport.Size = new Vector2(Derp.GetScreenWidth(), Derp.GetScreenHeight());
        viewport.ScreenPosition = windowPos;

        // Update main dock layout to fill viewport (screen-space rect)
        var mainLayoutRect = new ImRect(
            viewport.ScreenPosition.X,
            viewport.ScreenPosition.Y,
            viewport.Size.X,
            viewport.Size.Y);
        if (_mainDockInsetTop > 0f)
        {
            float inset = _mainDockInsetTop;
            if (inset > mainLayoutRect.Height)
            {
                inset = mainLayoutRect.Height;
            }

            mainLayoutRect = new ImRect(
                mainLayoutRect.X,
                mainLayoutRect.Y + inset,
                mainLayoutRect.Width,
                mainLayoutRect.Height - inset);
        }
        ctx.DockController.UpdateMainLayout(mainLayoutRect);

        // Begin context frame
        ctx.BeginFrame(deltaTime);

        // Clear draw lists for all viewports
        foreach (var vp in ctx.Viewports)
            vp.ClearDrawLists();

        _inFrame = true;
        _transformStackDepth = 0;
        _currentTransform = Matrix3x2.Identity;
        _currentInverseTransform = Matrix3x2.Identity;
        _currentWindow = null;
        _currentClipRect = SdfCommand.NoClip;
        _scale = viewport.ContentScale;

        PrimeOverlayCaptureForOpenPopups(ctx);

        // Poll secondary OS windows and update their ImViewport input/state for this frame.
        // This must happen before UI code runs so BeginWindow can switch viewports and have valid input.
        var vm = ctx.ViewportManager;
        if (vm != null && _viewportAssigner != null)
        {
            vm.UpdateSecondaryViewports(_viewportAssigner);
        }

        // If the primary OS window moved, translate primary-assigned windows so they stay in place within it.
        if (_hasLastPrimaryViewportScreenPos)
        {
            var delta = viewport.ScreenPosition - _lastPrimaryViewportScreenPos;
            if (delta.X != 0 || delta.Y != 0)
            {
                foreach (var window in WindowManager.GetWindowsBackToFront())
                {
                    if (!window.IsOpen)
                    {
                        continue;
                    }

                    // Docked windows are positioned by docking layouts (which already follow the primary viewport).
                    // Shifting them again when the OS window moves can push them outside primary bounds and cause
                    // erroneous secondary viewport creation.
                    if (ctx.DockController.IsWindowDocked(window.Id))
                    {
                        continue;
                    }

                    // If multi-viewport is enabled, only shift windows that are currently assigned to primary.
                    // Otherwise shift all windows (they all live in the primary viewport).
                    if (_viewportAssigner != null && _viewportAssigner.GetViewportForWindow(window, ctx.DockController) != null)
                    {
                        continue;
                    }

                    window.Rect = new ImRect(
                        window.Rect.X + delta.X,
                        window.Rect.Y + delta.Y,
                        window.Rect.Width,
                        window.Rect.Height);
                }
            }
        }

        _lastPrimaryViewportScreenPos = viewport.ScreenPosition;
        _hasLastPrimaryViewportScreenPos = true;

        // Update docked flags before interactions so move/resize gating is consistent for this frame.
        ctx.DockController.UpdateDockedWindowFlags(WindowManager);

        // Update window interactions (move/resize) in screen space
        WindowManager.Update(ctx, viewport.ScreenPosition, ctx.Style.TitleBarHeight, 6f);

        // Sync docking layouts with windows (create/destroy as needed)
        ctx.DockController.SyncWithWindows(WindowManager);

        // Update layout rects to match window positions (screen-space)
        ctx.DockController.UpdateFloatingLayoutRects(WindowManager);

        // Process dock preview detection (while dragging)
        // Find which viewport the mouse is over and get screen-space mouse position
        var screenMousePos = GetScreenSpaceMousePosition(ctx, viewport);
        bool mouseDown = ctx.GlobalInput?.IsMouseButtonDown(DerpLib.ImGui.Input.MouseButton.Left) ?? ctx.Input.MouseDown;
        bool mousePressed = ctx.GlobalInput?.IsMouseButtonPressed(DerpLib.ImGui.Input.MouseButton.Left) ?? ctx.Input.MousePressed;
        bool mouseReleased = ctx.GlobalInput?.IsMouseButtonReleased(DerpLib.ImGui.Input.MouseButton.Left) ?? ctx.Input.MouseReleased;

        // Group resize for floating dock groups (resize entire layout).
        ctx.DockController.ProcessFloatingGroupResize(WindowManager, screenMousePos, mouseDown, mousePressed, mouseReleased, 6f);

        // Group chrome drag for floating dock groups (moves entire layout).
        ctx.DockController.ProcessFloatingGroupChromeDrag(WindowManager, screenMousePos, mouseDown, mousePressed, mouseReleased);
        ctx.DockController.ProcessDragInput(WindowManager, screenMousePos, mouseReleased);

        // Assign windows to viewports based on screen-space bounds
        if (_viewportAssigner != null)
        {
            _viewportAssigner.AssignViewports(WindowManager, viewport.ScreenBounds, ctx.DockController);
        }

        // Reset secondary buffers after assignment so all viewports start clean before UI draw.
        if (vm != null)
        {
            vm.ResetSecondaryBuffers();
        }

        // If there's a preview target, find its viewport (after viewport assignment).
        if (ctx.DockController.PreviewTargetLayout != null)
        {
            ctx.DockController.PreviewViewport = FindViewportContainingScreenPos(ctx.DockController.PreviewRect.Position);
        }

        // Draw docking chrome + tabs (background pass) before window content.
        ctx.DockController.DrawLayouts(WindowManager);
    }

    /// <summary>
    /// Reserve space at the top of the main dock layout (e.g. for a global menu bar).
    /// Value is in viewport logical coordinates.
    /// </summary>
    public static void SetMainDockTopInset(float insetTop)
    {
        _mainDockInsetTop = insetTop < 0f ? 0f : insetTop;
    }

    /// <summary>
    /// Get the screen-space mouse position, using GlobalInput if available.
    /// </summary>
    private static Vector2 GetScreenSpaceMousePosition(ImContext ctx, ImViewport primaryViewport)
    {
        // Use global input if available (handles multi-viewport correctly)
        if (ctx.GlobalInput != null)
        {
            return ctx.GlobalInput.GlobalMousePosition;
        }

        // Fallback to primary viewport
        return primaryViewport.Input.MousePos + primaryViewport.ScreenPosition;
    }

    /// <summary>
    /// End the ImGUI frame.
    /// Automatically processes window extractions if multi-viewport is enabled.
    /// </summary>
    public static void End()
    {
        EnsureInFrame();

        // Draw deferred popups (after all windows for correct z-order)
        if (_hasDeferredDropdown)
        {
            RenderDeferredDropdown();
            _hasDeferredDropdown = false;
        }
        if (_hasDeferredComboBox)
        {
            RenderDeferredComboBox();
            _hasDeferredComboBox = false;
        }

        // Draw dock preview overlay (on top of everything)
        // Uses PreviewViewport which was set during Begin() after ProcessDragInput
        var dockController = Context.DockController;
        if (dockController.PreviewViewport != null && dockController.PreviewZone != Docking.ImDockZone.None)
        {
            var previewViewport = dockController.PreviewViewport;
            var previousViewport = Context.CurrentViewport;
            var previousLayer = previousViewport?.CurrentLayer ?? Rendering.ImDrawLayer.WindowContent;
            SetCurrentViewportForInternalDraw(previewViewport);
            SetDrawLayer(Rendering.ImDrawLayer.Overlay);
            previewViewport.CurrentDrawList.SetSortKey(int.MaxValue - 1);
            dockController.DrawPreview(previewViewport.ScreenPosition);
            previewViewport.CurrentDrawList.SetSortKey(0);
            RestoreViewportForInternalDraw(previousViewport);
            SetDrawLayer(previousLayer);
        }

        // Draw ghost tab preview (on top of everything).
        if (dockController.GhostDraggingWindowId != 0)
        {
            var primaryViewport = Context.PrimaryViewport;
            if (primaryViewport != null)
            {
                var screenMousePos = GetScreenSpaceMousePosition(Context, primaryViewport);
                var ghostViewport = FindViewportContainingScreenPos(screenMousePos);
                if (ghostViewport != null)
                {
                    var previousViewport = Context.CurrentViewport;
                    var previousLayer = previousViewport?.CurrentLayer ?? Rendering.ImDrawLayer.WindowContent;
                    SetCurrentViewportForInternalDraw(ghostViewport);
                    SetDrawLayer(Rendering.ImDrawLayer.Overlay);
                    ghostViewport.CurrentDrawList.SetSortKey(int.MaxValue - 1);
                    dockController.DrawGhostPreview(ghostViewport.ScreenPosition, screenMousePos);
                    ghostViewport.CurrentDrawList.SetSortKey(0);
                    RestoreViewportForInternalDraw(previousViewport);
                    SetDrawLayer(previousLayer);
                }
            }
        }

        // Flush all viewports' draw lists to their respective SdfBuffers in layer order
        foreach (var vp in Context.Viewports)
            vp.FlushDrawLists();

        // Apply cursor from window manager
        Derp.SetCursor(WindowManager.DesiredCursor);

        // End context frame
        Context.EndFrame();

        _inFrame = false;
    }

    /// <summary>
    /// Render deferred dropdown popup from struct state.
    /// </summary>
    private static void RenderDeferredDropdown()
    {
        ref var d = ref _deferredDropdown;
        if (d.Viewport == null) return;

        // Switch to the viewport where the dropdown was created
        var previousViewport = Context.CurrentViewport;
        var previousLayer = previousViewport?.CurrentLayer ?? Rendering.ImDrawLayer.WindowContent;
        SetCurrentViewportForInternalDraw(d.Viewport);
        SetDrawLayer(Rendering.ImDrawLayer.Overlay);

        var style = Context.Style;
        var font = Context.Font;
        float itemHeight = style.MinButtonHeight;

        // Popups render above all windows/layouts.
        var drawList = CurrentViewport.GetDrawList(Rendering.ImDrawLayer.Overlay);
        int previousSortKey = drawList.GetSortKey();
        var previousClipRect = drawList.GetClipRect();
        drawList.SetSortKey(Math.Max(d.SortKey, 1_000_000_000));
        drawList.ClearClipRect();

        bool pushedCancelTransform = false;
        if (_currentTransform != Matrix3x2.Identity)
        {
            PushInverseTransform();
            pushedCancelTransform = true;
        }

        // Popups render in viewport space and must not inherit window clip rects.
        PushClipRectOverride(new ImRect(0f, 0f, CurrentViewport.Size.X, CurrentViewport.Size.Y));

        // Calculate scrollbar requirements
        float contentHeight = d.OptionCount * itemHeight;
        float scrollbarWidth = style.ScrollbarWidth;
        bool hasScrollbar = contentHeight > d.PopupRect.Height;
        float contentWidth = hasScrollbar ? d.PopupRect.Width - scrollbarWidth : d.PopupRect.Width;

        // Draw popup background with drop shadow
        {
            float pr = d.PopupRect.X * _scale;
            float py = d.PopupRect.Y * _scale;
            float pw = d.PopupRect.Width * _scale;
            float ph = d.PopupRect.Height * _scale;
            float cr = style.CornerRadius * _scale;
            var surfaceColor = ImStyle.ToVector4(style.Surface);
            var shadowColor = new Vector4(0f, 0f, 0f, 0.35f);
            drawList.AddRoundedRectWithShadow(pr, py, pw, ph, cr, surfaceColor,
                0f, 3f * _scale, 12f * _scale, shadowColor);
        }
        DrawRoundedRectStroke(d.PopupRect.X, d.PopupRect.Y, d.PopupRect.Width, d.PopupRect.Height,
            style.CornerRadius, style.Border, style.BorderWidth);

        // Sync scroll position for this popup (source-of-truth is the open dropdown scroll)
        d.ScrollY = _dropdownScrollY;

        var contentRect = new ImRect(d.PopupRect.X, d.PopupRect.Y, contentWidth, d.PopupRect.Height);
        float contentY = ImScrollView.Begin(contentRect, contentHeight, ref d.ScrollY, handleMouseWheel: false);

        // Draw options
        for (int i = 0; i < d.OptionCount; i++)
        {
            float itemY = contentY + i * itemHeight;
            float itemYDrawn = itemY - d.ScrollY;
            if (itemYDrawn + itemHeight < contentRect.Y || itemYDrawn > contentRect.Bottom)
            {
                continue;
            }

            var itemRect = new ImRect(contentRect.X + 1, itemY, contentRect.Width - 2, itemHeight);
            bool isHovered = i == d.HoveredIndex;
            bool isSelected = i == d.SelectedIndex;

            if (isHovered)
            {
                DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, style.Hover);
            }
            else if (isSelected)
            {
                DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, ImStyle.WithAlpha(style.Primary, 64));
            }

            // Draw option text
            if (font != null)
            {
                float textY = itemRect.Y + (itemRect.Height - style.FontSize) / 2f;
                var color = ImStyle.ToVector4(style.TextPrimary);
                DrawTextLocalToDrawList(CurrentViewport.CurrentDrawList, font, d.Options[i].AsSpan(),
                    itemRect.X + style.Padding, textY, style.FontSize, color);
            }
        }

        var scrollbarRect = new ImRect(d.PopupRect.Right - scrollbarWidth, d.PopupRect.Y, scrollbarWidth, d.PopupRect.Height);
        int scrollbarWidgetId = _openDropdownId ^ 0x44524F50; // "DROP"
        ImScrollView.End(scrollbarWidgetId, scrollbarRect, contentRect.Height, contentHeight, ref d.ScrollY);

        _dropdownScrollY = d.ScrollY;

        PopClipRect();

        if (pushedCancelTransform)
        {
            PopTransform();
        }

        drawList.SetClipRect(previousClipRect);
        drawList.SetSortKey(previousSortKey);
        RestoreViewportForInternalDraw(previousViewport);
        SetDrawLayer(previousLayer);
    }

    private static void PrimeOverlayCaptureForOpenPopups(ImContext ctx)
    {
        var currentViewport = ctx.CurrentViewport;
        if (currentViewport == null)
        {
            return;
        }

        ImModal.PrimeOverlayCapture(ctx);
        ImContextMenu.PrimeOverlayCapture(ctx);

        if (_openDropdownId != 0 && _deferredDropdown.Viewport == currentViewport &&
            _deferredDropdown.PopupRect.Width > 0f && _deferredDropdown.PopupRect.Height > 0f)
        {
            ImPopover.AddCaptureRect(_deferredDropdown.PopupRect);
        }

        if (_openComboId != 0 && _deferredComboBox.Viewport == currentViewport &&
            _deferredComboBox.PopupRect.Width > 0f && _deferredComboBox.PopupRect.Height > 0f)
        {
            ImPopover.AddCaptureRect(_deferredComboBox.PopupRect);
        }
    }

    /// <summary>
    /// Render deferred combobox popup from struct state.
    /// </summary>
    private static void RenderDeferredComboBox()
    {
        ref var c = ref _deferredComboBox;
        if (c.Viewport == null) return;

        // Switch to the viewport where the combobox was created
        var previousViewport = Context.CurrentViewport;
        var previousLayer = previousViewport?.CurrentLayer ?? Rendering.ImDrawLayer.WindowContent;
        SetCurrentViewportForInternalDraw(c.Viewport);
        SetDrawLayer(Rendering.ImDrawLayer.Overlay);

        var style = Context.Style;
        var font = Context.Font;
        float itemHeight = style.MinButtonHeight;

        // Popups render above all windows/layouts.
        var drawList = CurrentViewport.GetDrawList(Rendering.ImDrawLayer.Overlay);
        int previousSortKey = drawList.GetSortKey();
        var previousClipRect = drawList.GetClipRect();
        drawList.SetSortKey(Math.Max(c.SortKey, 1_000_000_000));
        drawList.ClearClipRect();

        bool pushedCancelTransform = false;
        if (_currentTransform != Matrix3x2.Identity)
        {
            PushInverseTransform();
            pushedCancelTransform = true;
        }

        // Popups render in viewport space and must not inherit window clip rects.
        PushClipRectOverride(new ImRect(0f, 0f, CurrentViewport.Size.X, CurrentViewport.Size.Y));

        // Calculate scrollbar requirements
        float contentHeight = c.FilteredCount * itemHeight;
        float scrollbarWidth = style.ScrollbarWidth;
        bool hasScrollbar = contentHeight > c.PopupRect.Height;
        float contentWidth = hasScrollbar ? c.PopupRect.Width - scrollbarWidth : c.PopupRect.Width;

        // Draw popup background (use Surface so the list is opaque even if Style.Background is transparent)
        DrawRoundedRect(c.PopupRect.X, c.PopupRect.Y, c.PopupRect.Width, c.PopupRect.Height,
            style.CornerRadius, style.Surface);
        DrawRoundedRectStroke(c.PopupRect.X, c.PopupRect.Y, c.PopupRect.Width, c.PopupRect.Height,
            style.CornerRadius, style.Border, style.BorderWidth);

        // Sync scroll position for this popup (source-of-truth is the open combobox scroll)
        c.ScrollY = _comboScrollY;

        var contentRect = new ImRect(c.PopupRect.X, c.PopupRect.Y, contentWidth, c.PopupRect.Height);
        float contentY = ImScrollView.Begin(contentRect, contentHeight, ref c.ScrollY, handleMouseWheel: false);

        // Draw suggestions
        ReadOnlySpan<char> highlight = c.FilterText.AsSpan(0, c.FilterTextLength);
        for (int i = 0; i < c.FilteredCount; i++)
        {
            int suggestionIndex = c.FilteredIndices[i];
            string suggestion = c.Suggestions[suggestionIndex];

            float itemY = contentY + i * itemHeight;
            float itemYDrawn = itemY - c.ScrollY;
            if (itemYDrawn + itemHeight < contentRect.Y || itemYDrawn > contentRect.Bottom)
            {
                continue;
            }

            var itemRect = new ImRect(contentRect.X + 1, itemY, contentRect.Width - 2, itemHeight);
            bool isHovered = i == c.HoveredIndex;
            bool isHighlighted = i == c.HighlightedIndex;

            if (isHovered || isHighlighted)
            {
                DrawRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, style.Hover);
            }

            // Draw highlighted text
            if (font != null)
            {
                DrawHighlightedText(suggestion, highlight, itemRect, style);
            }
        }

        var scrollbarRect = new ImRect(c.PopupRect.Right - scrollbarWidth, c.PopupRect.Y, scrollbarWidth, c.PopupRect.Height);
        int scrollbarWidgetId = _openComboId ^ 0x434F4D42; // "COMB"
        ImScrollView.End(scrollbarWidgetId, scrollbarRect, contentRect.Height, contentHeight, ref c.ScrollY);

        _comboScrollY = c.ScrollY;

        PopClipRect();

        if (pushedCancelTransform)
        {
            PopTransform();
        }

        drawList.SetClipRect(previousClipRect);
        drawList.SetSortKey(previousSortKey);
        RestoreViewportForInternalDraw(previousViewport);
        SetDrawLayer(previousLayer);
    }

    /// <summary>
    /// Update and render all secondary viewports.
    /// Call this after RenderSdf() for the primary viewport.
    /// Returns the number of secondary viewports rendered.
    /// </summary>
    public static int UpdateSecondaryViewports(float deltaTime)
    {
        var vm = Context.ViewportManager;
        var viewportAssigner = _viewportAssigner;
        if (vm == null || viewportAssigner == null)
            return 0;

        int viewportCount = vm.GetSecondaryViewportCount();
        if (viewportCount == 0)
            return 0;

        // Get fonts for secondary viewport atlas binding
        var primaryFont = Context.Font;
        var secondaryFont = Context.SecondaryFont;

        int renderedCount = 0;
        for (int viewportIndex = 0; viewportIndex < viewportCount; viewportIndex++)
        {
            vm.GetSecondaryViewportAt(viewportIndex, out var viewportResources, out var imViewport);

            if (viewportAssigner.TryGetDockLayoutForViewport(viewportResources, out var layout))
            {
                if (!layout.HasAnyOpenWindow(WindowManager))
                {
                    continue;
                }
            }
            else
            {
                if (!viewportAssigner.TryGetWindowForViewport(viewportResources, out var window))
                {
                    continue;
                }

                if (!window.IsOpen)
                {
                    continue;
                }
            }

            if (!viewportResources.BeginFrame(out uint imageIndex))
            {
                continue;
            }

            // Bind font atlases to secondary viewport's SdfRenderer before rendering
            if (viewportResources.SdfRenderer != null && primaryFont != null)
            {
                Derp.SetSdfFontTextureArray(viewportResources.SdfRenderer);
                Derp.SetSdfFontAtlas(viewportResources.SdfRenderer, primaryFont.Atlas);
                if (secondaryFont != null)
                {
                    Derp.SetSdfSecondaryFontAtlas(viewportResources.SdfRenderer, secondaryFont.Atlas);
                }
            }

            viewportResources.RenderSdf(imageIndex);
            viewportResources.EndFrame(imageIndex);
            renderedCount++;
        }

        return renderedCount;
    }

    //=== Windows ===

    /// <summary>
    /// Begin a window. Returns true if the window is open and content should be drawn.
    /// Call EndWindow() when done, even if this returns false.
    /// </summary>
    /// <param name="title">Window title (also used as ID).</param>
    /// <param name="x">Initial X position (only used if window is new).</param>
    /// <param name="y">Initial Y position (only used if window is new).</param>
    /// <param name="width">Initial width (only used if window is new).</param>
    /// <param name="height">Initial height (only used if window is new).</param>
    /// <param name="flags">Window behavior flags.</param>
    /// <returns>True if window content should be drawn.</returns>
    public static bool BeginWindow(string title, float x, float y, float width, float height,
        ImWindowFlags flags = ImWindowFlags.None)
    {
        return BeginWindowCore(title, x, y, width, height, flags, explicitId: null);
    }

    /// <summary>
    /// Begin a window with an explicit ID (allows multiple windows with the same title).
    /// </summary>
    public static bool BeginWindow(string title, int id, float x, float y, float width, float height,
        ImWindowFlags flags = ImWindowFlags.None)
    {
        return BeginWindowCore(title, x, y, width, height, flags, explicitId: id);
    }

    private static bool BeginWindowCore(string title, float x, float y, float width, float height,
        ImWindowFlags flags, int? explicitId)
    {
        EnsureInFrame();
        var ctx = Context;
        var style = ctx.Style;
        var viewport = ctx.CurrentViewport ?? throw new InvalidOperationException("No current viewport set");

        // Convert initial local coordinates to screen-space (only used for first creation).
        float screenX = viewport.ScreenPosition.X + x;
        float screenY = viewport.ScreenPosition.Y + y;

        var window = WindowManager.GetOrCreateWindow(title, screenX, screenY, width, height, flags, explicitId);
        _currentWindow = window;
        _windowPushedId = false;
        _windowPushedClip = false;
        _windowBeganLayout = false;
        _windowSwitchedViewport = false;
        _windowPreviousViewport = null;
        _windowPushedTransform = false;
        _windowPushedScrollTransform = false;

        // If window is docked, its rect is owned by the docking layout.
        // For single-window floating layouts, IsWindowDocked is false (the window owns its own rect).
        var dockController = ctx.DockController;
        var windowLayer = Rendering.ImDrawLayer.FloatingWindows;
        if (dockController.IsWindowDocked(window.Id))
        {
            var leaf = dockController.FindLeafForWindow(window.Id);
            if (leaf != null && leaf.Rect.Width > 0 && leaf.Rect.Height > 0)
            {
                window.Rect = leaf.ContentRect;
            }

            window.IsDocked = true;

            // If this tab is being ghost-dragged, don't render its content this frame.
            if (dockController.GhostDraggingWindowId == window.Id)
            {
                return false;
            }

            // If this leaf has tabs, only the active tab renders.
            if (leaf != null && leaf.WindowIds.Count > 1)
            {
                int activeIndex = leaf.ActiveTabIndex;
                if (activeIndex < 0 || activeIndex >= leaf.WindowIds.Count)
                {
                    activeIndex = 0;
                }

                if (leaf.WindowIds[activeIndex] != window.Id)
                {
                    return false;
                }
            }

            // Docked windows in the main layout render in WindowContent; docked windows in floating groups
            // render in FloatingWindows so the whole group z-orders like a window.
            var layout = dockController.FindLayoutForWindow(window.Id);
            if (layout != null && layout.IsMainLayout)
            {
                windowLayer = Rendering.ImDrawLayer.WindowContent;
            }
            else if (layout != null && layout.IsFloatingGroup())
            {
                windowLayer = Rendering.ImDrawLayer.FloatingWindows;
            }
        }
        else
        {
            window.IsDocked = false;
            windowLayer = Rendering.ImDrawLayer.FloatingWindows;
        }

        // Switch viewport if window belongs to a different viewport than the current one.
        var windowViewport = _viewportAssigner?.GetViewportForWindow(window, dockController);
        int targetResourcesId = windowViewport?.Id ?? -1;
        if (viewport.ResourcesId != targetResourcesId)
        {
            var desiredViewport = FindViewportByResourcesId(ctx, targetResourcesId);
            if (desiredViewport == null)
            {
                return false;
            }

            _windowSwitchedViewport = true;
            _windowPreviousViewport = viewport;
            ctx.SetCurrentViewport(desiredViewport);
            viewport = desiredViewport;
            _scale = viewport.ContentScale;
        }

        PrimeOverlayCaptureForOpenPopups(ctx);

        if (!window.IsOpen)
        {
            return false;
        }

        // For drawing, always use window.Rect - it's the source of truth for position
        // Layouts are only used for detection (preview zones), not for drawing
        var transformPos = new Vector2(
            window.Rect.X - viewport.ScreenPosition.X,
            window.Rect.Y - viewport.ScreenPosition.Y);

        PushTransform(transformPos);
        _windowPushedTransform = true;

        _currentWindowRect = new ImRect(0, 0, window.Rect.Width, window.Rect.Height);
        float titleBarHeight = window.IsDocked ? 0 : style.TitleBarHeight;
        _currentWindowContentRect = window.GetContentRect(_currentWindowRect, titleBarHeight, style.ScrollbarWidth);

        // Push ID for window scope
        if (explicitId.HasValue)
        {
            ctx.PushId(explicitId.Value);
        }
        else
        {
            ctx.PushId(title);
        }
        _windowPushedId = true;

        // Set sort key for window z-ordering.
        int sortKey = window.ZOrder;
        if (window.IsDocked)
        {
            var layout = dockController.FindLayoutForWindow(window.Id);
            if (layout != null)
            {
                if (layout.IsMainLayout)
                {
                    sortKey = 0;
                }
                else if (layout.IsFloatingGroup())
                {
                    sortKey = dockController.GetLayoutSortKeyBase(layout, WindowManager);
                }
            }
        }
        CurrentViewport.GetDrawList(windowLayer).SetSortKey(sortKey);

        // Draw frame and widgets in the same layer. Insertion order keeps window background behind widgets.
        SetDrawLayer(windowLayer);
        DrawWindowFrame(window, _currentWindowRect, style);
        SetDrawLayer(windowLayer);

        // If collapsed, don't draw content
        if (window.IsCollapsed)
        {
            return false;
        }

        // Set up content clip rect
        var contentRect = _currentWindowContentRect;

        UpdateWindowScrollState(window, contentRect.Width, contentRect.Height);
        if (window.HasVerticalScroll)
        {
            var scrollbarRect = new ImRect(_currentWindowContentRect.Right, _currentWindowContentRect.Y, style.ScrollbarWidth, _currentWindowContentRect.Height);
            float scrollY = window.ScrollOffset.Y;
            int scrollbarWidgetId = ctx.GetId(0x5343524C); // "SCRL"
            ImScrollbar.DrawVertical(scrollbarWidgetId, scrollbarRect, ref scrollY, _currentWindowContentRect.Height, window.ContentSize.Y);
            window.ScrollOffset = new Vector2(window.ScrollOffset.X, scrollY);
        }

        Im.PushClipRect(_currentWindowContentRect);
        _windowPushedClip = true;

        // Debug: draws two small markers to verify that clip rect is actually applied to window content.
        // - Green marker should be visible (inside clip)
        // - Magenta marker should be invisible (outside clip)
        if (DebugClipTestWindows)
        {
            DrawRect(_currentWindowContentRect.X + 2f, _currentWindowContentRect.Y + 2f, 6f, 6f, 0xFF00FF00);
            DrawRect(_currentWindowContentRect.X - 12f, _currentWindowContentRect.Y - 12f, 6f, 6f, 0xFFFF00FF);
        }

        PushTransform(-window.ScrollOffset);
        _windowPushedScrollTransform = true;

        ImLayout.InternalBeginWindow(_currentWindowContentRect, style.Padding, style.Spacing);
        _windowBeganLayout = true;

        return true;
    }

    /// <summary>
    /// End the current window.
    /// </summary>
    public static void EndWindow()
    {
        EnsureInFrame();
        var ctx = Context;

        if (_currentWindow == null)
            throw new InvalidOperationException("EndWindow called without matching BeginWindow");

        if (_windowBeganLayout)
        {
            var contentSize = ImLayout.InternalEndWindow();
            _currentWindow.ContentSize = contentSize;
            var windowContentRect = _currentWindow.GetContentRect(_currentWindowRect, ctx.Style.TitleBarHeight, ctx.Style.ScrollbarWidth);
            UpdateWindowScrollState(_currentWindow, windowContentRect.Width, windowContentRect.Height);
        }

        // Pop scroll, clip, and ID based on what we actually pushed (not current window state)
        if (_windowPushedScrollTransform)
        {
            PopTransform();
        }

        if (_windowPushedClip)
        {
            Im.PopClipRect();
        }

        // Reset sort keys to 0 for any non-window drawing after this.
        CurrentViewport.GetDrawList(Rendering.ImDrawLayer.WindowContent).SetSortKey(0);
        CurrentViewport.GetDrawList(Rendering.ImDrawLayer.FloatingWindows).SetSortKey(0);

        if (_windowPushedId)
        {
            ctx.PopId();
        }

        if (_windowPushedTransform)
        {
            PopTransform();
        }

        _currentWindow = null;
        _currentWindowRect = ImRect.Zero;
        _currentWindowContentRect = ImRect.Zero;
        _windowBeganLayout = false;
        _currentClipRect = SdfCommand.NoClip;
        _windowPushedId = false;
        _windowPushedClip = false;
        _windowPushedTransform = false;
        _windowPushedScrollTransform = false;

        if (_windowSwitchedViewport && _windowPreviousViewport != null)
        {
            ctx.SetCurrentViewport(_windowPreviousViewport);
            _scale = _windowPreviousViewport.ContentScale;
        }

        _windowSwitchedViewport = false;
        _windowPreviousViewport = null;

        // Default back to WindowContent layer after every window.
        SetDrawLayer(Rendering.ImDrawLayer.WindowContent);
    }

    /// <summary>
    /// Draw all windows. Call this after Im.Begin() and before drawing non-window widgets.
    /// </summary>
    public static void DrawWindows(Action<ImWindow> drawWindowContent)
    {
        EnsureInFrame();

        foreach (var window in WindowManager.GetWindowsBackToFront())
        {
            var viewport = Context.CurrentViewport ?? throw new InvalidOperationException("No current viewport set");
            float localX = window.Rect.X - viewport.ScreenPosition.X;
            float localY = window.Rect.Y - viewport.ScreenPosition.Y;

            if (BeginWindow(window.Title, window.Id, localX, localY,
                window.Rect.Width, window.Rect.Height, window.Flags))
            {
                drawWindowContent(window);
            }
            EndWindow();
        }
    }

    private static void DrawWindowFrame(ImWindow window, ImRect localRect, ImStyle style)
    {
        // Docked windows don't draw their own frame
        if (window.IsDocked)
            return;

        float x = localRect.X;
        float y = localRect.Y;
        float w = localRect.Width;
        float h = window.IsCollapsed ? style.TitleBarHeight : localRect.Height;

        // Window background with shadow (goes through draw list for correct z-ordering)
        var shadowColor = ImStyle.ToVector4(style.ShadowColor);
        var bgColor = ImStyle.ToVector4(style.Background);

        AddRoundedRectWithShadowLocal(
            x, y, w, h,
            style.CornerRadius,
            bgColor,
            style.ShadowOffsetX,
            style.ShadowOffsetY,
            style.ShadowRadius,
            shadowColor);

        // Title bar
        bool isFocused = WindowManager.FocusedWindow == window;
        uint titleBarColor = isFocused ? style.TitleBar : style.TitleBarInactive;

        DrawRoundedRect(x, y, w, style.TitleBarHeight, style.CornerRadius, titleBarColor);

        // Title bar bottom edge (to connect with body when not collapsed)
        if (!window.IsCollapsed)
        {
            DrawRect(x, y + style.TitleBarHeight - style.CornerRadius,
                w, style.CornerRadius, titleBarColor);
        }

        // Border
        DrawRoundedRectStroke(x, y, w, h, style.CornerRadius, style.Border, style.BorderWidth);

        // Title text placeholder
        float textX = x + style.Padding;
        float textY = y + (style.TitleBarHeight - 14) / 2;
        LabelTextViewport(window.Title, textX, textY);

        // Close button (if not NoClose)
        if (!window.Flags.HasFlag(ImWindowFlags.NoClose))
        {
            float btnSize = style.TitleBarHeight - 8;
            float btnX = x + w - btnSize - 4;
            float btnY = y + 4;

            if (ButtonInternal(window.Id ^ 0x636C6F73, btnX, btnY, btnSize, btnSize)) // "clos"
            {
                window.IsOpen = false;
            }

            // Draw X
            float pad = btnSize * 0.3f;
            DrawLine(btnX + pad, btnY + pad, btnX + btnSize - pad, btnY + btnSize - pad, 2f, style.TextPrimary);
            DrawLine(btnX + btnSize - pad, btnY + pad, btnX + pad, btnY + btnSize - pad, 2f, style.TextPrimary);
        }

        // Collapse button (if not NoCollapse)
        if (!window.Flags.HasFlag(ImWindowFlags.NoCollapse))
        {
            float btnSize = style.TitleBarHeight - 8;
            float offset = window.Flags.HasFlag(ImWindowFlags.NoClose) ? 4 : btnSize + 8;
            float btnX = x + w - btnSize - offset;
            float btnY = y + 4;

            if (ButtonInternal(window.Id ^ 0x636F6C6C, btnX, btnY, btnSize, btnSize)) // "coll"
            {
                window.IsCollapsed = !window.IsCollapsed;
            }

            // Draw collapse indicator (triangle or minus)
            float pad = btnSize * 0.3f;
            if (window.IsCollapsed)
            {
                // Right-pointing triangle (collapsed)
                DrawLine(btnX + pad, btnY + pad, btnX + btnSize - pad, btnY + btnSize / 2, 2f, style.TextPrimary);
                DrawLine(btnX + btnSize - pad, btnY + btnSize / 2, btnX + pad, btnY + btnSize - pad, 2f, style.TextPrimary);
            }
            else
            {
                // Down-pointing indicator (expanded)
                DrawLine(btnX + pad, btnY + btnSize / 2, btnX + btnSize - pad, btnY + btnSize / 2, 2f, style.TextPrimary);
            }
        }
    }

    private static void UpdateWindowScrollState(ImWindow window, float visibleContentWidth, float visibleContentHeight)
    {
        if (window.Flags.HasFlag(ImWindowFlags.NoScrollbar))
        {
            window.MaxScrollOffset = Vector2.Zero;
            window.HasVerticalScroll = false;
            window.HasHorizontalScroll = false;
            window.ScrollOffset = Vector2.Zero;
            return;
        }

        float maxScrollX = Math.Max(0f, window.ContentSize.X - visibleContentWidth);
        float maxScrollY = Math.Max(0f, window.ContentSize.Y - visibleContentHeight);
        window.MaxScrollOffset = new Vector2(maxScrollX, maxScrollY);

        window.HasHorizontalScroll = maxScrollX > 0f;
        window.HasVerticalScroll = maxScrollY > 0f;

        float clampedX = window.ScrollOffset.X;
        if (clampedX < 0f)
        {
            clampedX = 0f;
        }
        else if (clampedX > maxScrollX)
        {
            clampedX = maxScrollX;
        }

        float clampedY = window.ScrollOffset.Y;
        if (clampedY < 0f)
        {
            clampedY = 0f;
        }
        else if (clampedY > maxScrollY)
        {
            clampedY = maxScrollY;
        }

        window.ScrollOffset = new Vector2(clampedX, clampedY);
    }

    /// <summary>
    /// Internal button without label drawing (for window chrome).
    /// Uses numeric ID to avoid string allocation.
    /// </summary>
    internal static bool ButtonInternal(int widgetId, float x, float y, float w, float h)
    {
        var ctx = Context;
        var style = ctx.Style;
        var rect = new ImRect(x, y, w, h);

        bool hovered = rect.Contains(MousePos);
        if (hovered)
            ctx.SetHot(widgetId);

        bool pressed = false;
        if (ctx.IsHot(widgetId) && ctx.Input.MousePressed)
        {
            ctx.SetActive(widgetId);
        }

        if (ctx.IsActive(widgetId) && ctx.Input.MouseReleased)
        {
            if (ctx.IsHot(widgetId))
                pressed = true;
            ctx.ClearActive();
        }

        // Draw hover/active state
        if (ctx.IsActive(widgetId))
            DrawRoundedRect(x, y, w, h, 2f, style.Active);
        else if (ctx.IsHot(widgetId))
            DrawRoundedRect(x, y, w, h, 2f, style.Hover);

        return pressed;
    }

    //=== Widgets ===

    /// <summary>
    /// Draw a clickable button. Returns true if clicked this frame.
    /// </summary>
    public static bool Button(string label)
    {
        EnsureInFrame();
        var style = Context.Style;
        var rect = ImLayout.AllocateRect(style.MinButtonWidth, style.MinButtonHeight);
        return Button(label, rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static bool Button(string label, float width)
    {
        EnsureInFrame();
        var style = Context.Style;
        var rect = ImLayout.AllocateRect(width, style.MinButtonHeight);
        return Button(label, rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static bool Button(string label, float x, float y, float w, float h)
    {
        EnsureInFrame();
        var ctx = Context;
        var style = ctx.Style;

        int id = ctx.GetId(label);
        var rect = new ImRect(x, y, w, h);

        // Hit test
        bool hovered = rect.Contains(MousePos);
        if (hovered)
            ctx.SetHot(id);

        // Handle interaction
        bool pressed = false;
        if (ctx.IsHot(id))
        {
            if (ctx.Input.MousePressed)
            {
                ctx.SetActive(id);
            }
        }

        if (ctx.IsActive(id))
        {
            if (ctx.Input.MouseReleased)
            {
                if (ctx.IsHot(id))
                    pressed = true;
                ctx.ClearActive();
            }
        }

        // Visuals: no background/border by default. Only show hover/active backer.
        if (ctx.IsActive(id))
        {
            DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, style.Active);
        }
        else if (ctx.IsHot(id))
        {
            DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, style.Hover);
        }

        // Draw text label centered in button
        var font = ctx.Font;
        if (font != null)
        {
            ReadOnlySpan<char> visible = GetVisibleLabelText(label);
            if (!visible.IsEmpty)
            {
                float textWidth = MeasureTextWidth(visible, style.FontSize);
                float textX = rect.X + (rect.Width - textWidth) / 2f;
                float textY = rect.Y + (rect.Height - style.FontSize) / 2f;
                var color = ImStyle.ToVector4(style.TextPrimary);
                DrawTextLocalToDrawList(CurrentViewport.CurrentDrawList, font, visible, textX, textY, style.FontSize, color);
            }
        }

        return pressed;
    }

    /// <summary>
    /// Draw a text label using zero-allocation string interpolation.
    /// Usage: Im.Label($"Value: {x:F2}", px, py);
    /// For existing string variables, use LabelText() instead.
    /// </summary>
    public static void Label(ImFormatHandler handler, float x, float y)
    {
        LabelTextCore(handler.Text, x, y);
    }

    public static void Label(ImFormatHandler handler)
    {
        EnsureInFrame();
        var style = Context.Style;
        var text = handler.Text;

        float width = MeasureTextWidth(text, style.FontSize);
        float height = style.FontSize;
        var rect = ImLayout.AllocateRect(width, height);
        LabelTextCore(text, rect.X, rect.Y);
    }


    /// <summary>
    /// Draw a label at the current layout position with custom color.
    /// </summary>
    public static void Label(ImFormatHandler handler, uint color)
    {
        EnsureInFrame();
        var style = Context.Style;
        var text = handler.Text;

        float width = MeasureTextWidth(text, style.FontSize);
        float height = style.FontSize;
        var rect = ImLayout.AllocateRect(width, height);
        LabelTextCore(text, rect.X, rect.Y, color);
    }

    /// <summary>
    /// Draw a text label from an existing string (no allocation for the string itself).
    /// </summary>
    public static void LabelText(string text, float x, float y)
    {
        LabelTextCore(text.AsSpan(), x, y);
    }

    public static void LabelText(string text)
    {
        EnsureInFrame();
        var style = Context.Style;

        float width = MeasureTextWidth(text.AsSpan(), style.FontSize);
        float height = style.FontSize;
        var rect = ImLayout.AllocateRect(width, height);
        LabelTextCore(text.AsSpan(), rect.X, rect.Y);
    }

    private static void LabelTextCore(ReadOnlySpan<char> text, float x, float y)
    {
        EnsureInFrame();
        var style = Context.Style;
        var font = Context.Font;

        if (font != null)
        {
            // Render actual text using SDF to current viewport's draw list
            var color = ImStyle.ToVector4(style.TextPrimary);
            DrawTextLocalToDrawList(CurrentViewport.CurrentDrawList, font, text, x, y, style.FontSize, color);
        }
        else
        {
            // Fallback: draw a bar representing text length
            float charWidth = 7f;
            float charHeight = style.FontSize;
            int len = Math.Min(text.Length, 60);
            float w = len * charWidth;
            DrawRoundedRect(x, y, w, charHeight, 2f, ImStyle.WithAlphaF(style.TextPrimary, 0.3f));
        }
    }


    private static void LabelTextCore(ReadOnlySpan<char> text, float x, float y, uint color)
    {
        EnsureInFrame();
        var style = Context.Style;
        var font = Context.Font;

        if (font != null)
        {
            var colorVec = ImStyle.ToVector4(color);
            DrawTextLocalToDrawList(CurrentViewport.CurrentDrawList, font, text, x, y, style.FontSize, colorVec);
        }
        else
        {
            float charWidth = 7f;
            float charHeight = style.FontSize;
            int len = Math.Min(text.Length, 60);
            float w = len * charWidth;
            DrawRoundedRect(x, y, w, charHeight, 2f, ImStyle.WithAlphaF(color, 0.3f));
        }
    }

    /// <summary>
    /// Measure the width of text at a given font size.
    /// </summary>
    public static float MeasureTextWidth(ReadOnlySpan<char> text, float fontSize)
    {
        return ImTextMetrics.MeasureWidth(Context.Font, Context.SecondaryFont, text, fontSize);
    }

    private static ReadOnlySpan<char> GetVisibleLabelText(string label)
    {
        if (label == null)
        {
            return ReadOnlySpan<char>.Empty;
        }

        ReadOnlySpan<char> span = label.AsSpan();
        for (int i = 0; i + 1 < span.Length; i++)
        {
            if (span[i] == '#' && span[i + 1] == '#')
            {
                return span[..i];
            }
        }

        return span;
    }

    /// <summary>
    /// Draw a horizontal slider. Returns true if value changed.
    /// </summary>
    public static bool Slider(string label, ref float value, float min, float max)
    {
        EnsureInFrame();
        var style = Context.Style;
        float height = style.SliderHeight + 10f;
        var rect = ImLayout.AllocateRect(0, height);
        return Slider(label, ref value, min, max, rect.X, rect.Y, rect.Width);
    }

    public static bool Slider(string label, ref float value, float min, float max, float x, float y, float w)
    {
        EnsureInFrame();
        var ctx = Context;
        var style = ctx.Style;

        int id = ctx.GetId(label);
        float h = style.SliderHeight;
        float thumbW = style.SliderThumbWidth;
        var rect = new ImRect(x, y, w, h + 10); // Extra height for easier interaction

        // Hit test
        bool hovered = rect.Contains(MousePos);
        if (hovered)
            ctx.SetHot(id);

        // Handle interaction
        bool changed = false;
        if (ctx.IsHot(id) && ctx.Input.MousePressed)
        {
            ctx.SetActive(id);
        }

        if (ctx.IsActive(id))
        {
            // Calculate new value from mouse position
            float mouseX = MousePos.X;
            float t = Math.Clamp((mouseX - rect.X - thumbW / 2) / (rect.Width - thumbW), 0f, 1f);
            float newValue = min + t * (max - min);

            if (newValue != value)
            {
                value = newValue;
                changed = true;
            }

            if (ctx.Input.MouseReleased)
                ctx.ClearActive();
        }

        // Draw track
        float trackY = rect.Y + 5; // Center vertically
        DrawRoundedRect(rect.X, trackY, rect.Width, h, h / 2, style.Surface);

        // Draw fill
        float t2 = Math.Clamp((value - min) / (max - min), 0f, 1f);
        float fillW = t2 * (rect.Width - thumbW) + thumbW / 2;
        if (fillW > 2)
            DrawRoundedRect(rect.X, trackY, fillW, h, h / 2, style.SliderFill);

        // Draw thumb
        float thumbX = rect.X + t2 * (rect.Width - thumbW);
        uint thumbColor = ctx.IsActive(id) ? style.Active : (ctx.IsHot(id) ? style.Hover : style.TextPrimary);
        DrawCircle(thumbX + thumbW / 2, trackY + h / 2, thumbW / 2, thumbColor);

        return changed;
    }

    /// <summary>
    /// Draw a checkbox. Returns true if state changed.
    /// </summary>
    public static bool Checkbox(string label, ref bool value)
    {
        EnsureInFrame();
        var style = Context.Style;
        var rect = ImLayout.AllocateRect(style.CheckboxSize, style.CheckboxSize);
        return Checkbox(label, ref value, rect.X, rect.Y);
    }

    public static bool Checkbox(string label, ref bool value, float x, float y)
    {
        EnsureInFrame();
        var ctx = Context;
        var style = ctx.Style;

        int id = ctx.GetId(label);
        float size = style.CheckboxSize;
        var rect = new ImRect(x, y, size, size);

        // Hit test
        bool hovered = rect.Contains(MousePos);
        if (hovered)
            ctx.SetHot(id);

        // Handle interaction
        bool changed = false;
        if (ctx.IsHot(id) && ctx.Input.MousePressed)
        {
            ctx.SetActive(id);
        }

        if (ctx.IsActive(id) && ctx.Input.MouseReleased)
        {
            if (ctx.IsHot(id))
            {
                value = !value;
                changed = true;
            }
            ctx.ClearActive();
        }

        // Draw box
        uint bgColor = ctx.IsActive(id) ? style.Active : (ctx.IsHot(id) ? style.Hover : style.Surface);
        DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, bgColor);
        DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, style.Border, style.BorderWidth);

        // Draw check mark if checked
        if (value)
        {
            float inset = size * 0.25f;
            DrawRoundedRect(rect.X + inset, rect.Y + inset, rect.Width - inset * 2, rect.Height - inset * 2, 2f, style.CheckMark);
        }

        return changed;
    }

    /// <summary>
    /// Draw a tri-state checkbox. When <paramref name="mixed"/> is true, the box shows a dash and clicking sets <paramref name="value"/> to true.
    /// Returns true if state changed.
    /// </summary>
    public static bool CheckboxTriState(string label, ref bool value, bool mixed, float x, float y)
    {
        EnsureInFrame();
        var ctx = Context;
        var style = ctx.Style;

        int id = ctx.GetId(label);
        float size = style.CheckboxSize;
        var rect = new ImRect(x, y, size, size);

        bool hovered = rect.Contains(MousePos);
        if (hovered)
        {
            ctx.SetHot(id);
        }

        bool changed = false;
        if (ctx.IsHot(id) && ctx.Input.MousePressed)
        {
            ctx.SetActive(id);
        }

        if (ctx.IsActive(id) && ctx.Input.MouseReleased)
        {
            if (ctx.IsHot(id))
            {
                if (mixed)
                {
                    if (!value)
                    {
                        value = true;
                    }
                    changed = true;
                }
                else
                {
                    value = !value;
                    changed = true;
                }
            }
            ctx.ClearActive();
        }

        uint bgColor = ctx.IsActive(id) ? style.Active : (ctx.IsHot(id) ? style.Hover : style.Surface);
        DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, bgColor);
        DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, style.Border, style.BorderWidth);

        if (mixed)
        {
            float inset = size * 0.22f;
            float dashHeight = MathF.Max(2f, size * 0.18f);
            float dashY = rect.Y + (size - dashHeight) * 0.5f;
            DrawRoundedRect(rect.X + inset, dashY, rect.Width - inset * 2f, dashHeight, 2f, style.CheckMark);
        }
        else if (value)
        {
            float inset = size * 0.25f;
            DrawRoundedRect(rect.X + inset, rect.Y + inset, rect.Width - inset * 2, rect.Height - inset * 2, 2f, style.CheckMark);
        }

        return changed;
    }

    //=== Radio Button ===

    /// <summary>
    /// Draw a radio button at the current layout position.
    /// Returns true if selection changed.
    /// </summary>
    public static bool RadioButton(string label, ref int selected, int thisValue)
    {
        EnsureInFrame();
        var style = Context.Style;
        ReadOnlySpan<char> visible = GetVisibleLabelText(label);
        var rect = ImLayout.AllocateRect(style.CheckboxSize + style.Spacing + MeasureTextWidth(visible, style.FontSize), style.CheckboxSize);
        return RadioButton(label, ref selected, thisValue, rect.X, rect.Y);
    }

    /// <summary>
    /// Draw a radio button at explicit position (no layout).
    /// Returns true if selection changed.
    /// </summary>
    public static bool RadioButton(string label, ref int selected, int thisValue, float x, float y)
    {
        EnsureInFrame();
        var ctx = Context;
        var style = ctx.Style;

        int id = ctx.GetId(label);
        float size = style.CheckboxSize;
        var rect = new ImRect(x, y, size, size);

        // Hit test (include label area)
        ReadOnlySpan<char> visible = GetVisibleLabelText(label);
        float labelWidth = MeasureTextWidth(visible, style.FontSize);
        var hitRect = new ImRect(x, y, size + style.Spacing + labelWidth, size);
        bool hovered = hitRect.Contains(MousePos);
        if (hovered)
            ctx.SetHot(id);

        // Handle interaction
        bool changed = false;
        if (ctx.IsHot(id) && ctx.Input.MousePressed)
        {
            ctx.SetActive(id);
        }

        if (ctx.IsActive(id) && ctx.Input.MouseReleased)
        {
            if (ctx.IsHot(id) && selected != thisValue)
            {
                selected = thisValue;
                changed = true;
            }
            ctx.ClearActive();
        }

        // Determine colors based on state
        bool isSelected = selected == thisValue;
        uint bgColor = ctx.IsActive(id) ? style.Active : (ctx.IsHot(id) ? style.Hover : style.Surface);

        // Draw outer circle
        float radius = size / 2f;
        DrawCircle(rect.X + radius, rect.Y + radius, radius, bgColor);

        // Draw border (through draw list for correct z-ordering)
        DrawCircleStroke(rect.X + radius, rect.Y + radius, radius, style.Border, style.BorderWidth);

        // Draw inner circle if selected
        if (isSelected)
        {
            float innerRadius = radius * 0.5f;
            DrawCircle(rect.X + radius, rect.Y + radius, innerRadius, style.CheckMark);
        }

        // Draw label
        float labelX = rect.X + size + style.Spacing;
        float labelY = rect.Y + (size - style.FontSize) * 0.5f;
        Text(visible, labelX, labelY, style.FontSize, style.TextPrimary);

        return changed;
    }

    /// <summary>
    /// Draw a horizontal radio button group.
    /// Returns the index that was clicked (-1 if none).
    /// </summary>
    public static int RadioGroup(string id, ReadOnlySpan<string> options, ref int selected)
    {
        EnsureInFrame();
        var ctx = Context;
        var style = ctx.Style;

        ctx.PushId(id);

        // Calculate total width
        float totalWidth = 0;
        for (int i = 0; i < options.Length; i++)
        {
            float optionWidth = style.CheckboxSize + style.Spacing + MeasureTextWidth(options[i], style.FontSize);
            totalWidth += optionWidth;
            if (i < options.Length - 1)
                totalWidth += style.Spacing * 2;
        }

        var rect = ImLayout.AllocateRect(totalWidth, style.CheckboxSize);
        float x = rect.X;

        int clicked = -1;
        for (int i = 0; i < options.Length; i++)
        {
            if (RadioButton(options[i], ref selected, i, x, rect.Y))
            {
                clicked = i;
            }

            float optionWidth = style.CheckboxSize + style.Spacing + MeasureTextWidth(options[i], style.FontSize);
            x += optionWidth + style.Spacing * 2;
        }

        ctx.PopId();
        return clicked;
    }


    //=== Text Input ===

    [Flags]
    public enum ImTextInputFlags
    {
        None = 0,
        NoBackground = 1 << 0,
        NoBorder = 1 << 1,
        NoRounding = 1 << 2,
        BorderLeft = 1 << 3,
        BorderRight = 1 << 4,
        BorderTop = 1 << 5,
        BorderBottom = 1 << 6,
        KeepFocusOnEnter = 1 << 7,
    }

    /// <summary>
    /// Draw a text input at the current layout position.
    /// Returns true if the text changed.
    /// </summary>
    public static bool TextInput(string id, Span<char> buffer, ref int length, int maxLength, float width = 150)
    {
        EnsureInFrame();
        var style = Context.Style;
        float height = style.MinButtonHeight;
        var rect = ImLayout.AllocateRect(width, height);
        return TextInput(id, buffer, ref length, maxLength, rect.X, rect.Y, rect.Width, ImTextInputFlags.None);
    }

    /// <summary>
    /// Draw a text input at explicit position (no layout).
    /// Returns true if the text changed.
    /// </summary>
    public static bool TextInput(string id, Span<char> buffer, ref int length, int maxLength, float x, float y, float width)
    {
        return TextInput(id, buffer, ref length, maxLength, x, y, width, ImTextInputFlags.None);
    }

    /// <summary>
    /// Draw a text input at explicit position (no layout).
    /// Returns true if the text changed.
    /// </summary>
    public static bool TextInput(string id, Span<char> buffer, ref int length, int maxLength, float x, float y, float width, ImTextInputFlags flags)
    {
        EnsureInFrame();
        var ctx = Context;
        var style = ctx.Style;
        var mousePos = MousePos;

        int widgetId = ctx.GetId(id);
        ctx.RegisterFocusable(widgetId);
        float height = style.MinButtonHeight;
        var rect = new ImRect(x, y, width, height);

        // Get or create state
        if (!_textInputStates.TryGetValue(widgetId, out var state))
        {
            state = new TextInputState { CaretPos = length, SelectionStart = -1, SelectionEnd = -1 };
        }

        bool isFocused = ctx.IsFocused(widgetId);
        bool hovered = rect.Contains(mousePos);
        bool changed = false;
        bool shouldScrollToCaret = false;

        // Hit test
        if (hovered)
        {
            ctx.SetHot(widgetId);
            if (ctx.Input.MousePressed)
            {
                ctx.RequestFocus(widgetId);
                ctx.SetActive(widgetId);

                // Calculate caret position from click (accounts for horizontal scroll)
                state.CaretPos = GetCaretPosFromX(buffer, length, rect, mousePos.X, style, state.ScrollOffsetX);
                state.SelectionStart = state.CaretPos;
                state.SelectionEnd = state.CaretPos;
                ctx.ResetCaretBlink();
            }
        }

        // Drag selection (mouse down while active)
        if (ctx.IsActive(widgetId))
        {
            if (ctx.Input.MouseDown)
            {
                float stylePadding = style.Padding;
                float visibleMinX = rect.X + stylePadding + 4f;
                float visibleMaxX = rect.Right - stylePadding - 4f;

                float mouseX = mousePos.X;
                if (mouseX < visibleMinX)
                {
                    float overshoot = visibleMinX - mouseX;
                    state.ScrollOffsetX -= (200f + overshoot * 12f) * ctx.DeltaTime;
                }
                else if (mouseX > visibleMaxX)
                {
                    float overshoot = mouseX - visibleMaxX;
                    state.ScrollOffsetX += (200f + overshoot * 12f) * ctx.DeltaTime;
                }

                state.ScrollOffsetX = ClampHorizontalScroll(style, buffer, length, state.ScrollOffsetX, width);

                int caretPos = GetCaretPosFromX(buffer, length, rect, mouseX, style, state.ScrollOffsetX);
                state.CaretPos = caretPos;
                state.SelectionEnd = caretPos;
                ctx.ResetCaretBlink();
            }
            else if (ctx.Input.MouseReleased)
            {
                ctx.ClearActive();
            }
        }

        // Handle text editing when focused
        if (isFocused)
        {
            ctx.WantCaptureKeyboard = true;

            // Handle typed characters (copy input to local to access fixed buffer)
            var input = ctx.Input;
            unsafe
            {
                for (int i = 0; i < input.InputCharCount; i++)
                {
                    char c = input.InputChars[i];
                    if (c >= 32 && length < maxLength) // Printable characters
                    {
                        // Delete selection if any
                        if (state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
                        {
                            int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
                            int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
                            DeleteRange(buffer, ref length, selStart, selEnd);
                            state.CaretPos = selStart;
                            state.SelectionStart = -1;
                            state.SelectionEnd = -1;
                        }

                        // Insert character
                        InsertChar(buffer, ref length, maxLength, state.CaretPos, c);
                        state.CaretPos++;
                        changed = true;
                        shouldScrollToCaret = true;
                        ctx.ResetCaretBlink();
                    }
                }
            }

            // Backspace
            if (ctx.Input.KeyBackspace)
            {
                state.BackspaceRepeatHeldTime = 0f;
                state.BackspaceRepeatTimer = 0.45f;
                if (ApplyBackspace(ref state, buffer, ref length))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    ctx.ResetCaretBlink();
                }
            }
            else if (ctx.Input.KeyBackspaceDown)
            {
                state.BackspaceRepeatHeldTime += ctx.DeltaTime;
                state.BackspaceRepeatTimer -= ctx.DeltaTime;

                for (int i = 0; i < 8 && state.BackspaceRepeatTimer <= 0f; i++)
                {
                    float rate = GetKeyRepeatRate(state.BackspaceRepeatHeldTime);
                    state.BackspaceRepeatTimer += 1f / rate;

                    if (!ApplyBackspace(ref state, buffer, ref length))
                    {
                        state.BackspaceRepeatTimer = 0f;
                        break;
                    }

                    changed = true;
                    shouldScrollToCaret = true;
                    ctx.ResetCaretBlink();
                }
            }
            else
            {
                state.BackspaceRepeatHeldTime = 0f;
                state.BackspaceRepeatTimer = 0f;
            }

            // Delete
            if (ctx.Input.KeyDelete)
            {
                state.DeleteRepeatHeldTime = 0f;
                state.DeleteRepeatTimer = 0.45f;
                if (ApplyDelete(ref state, buffer, ref length))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    ctx.ResetCaretBlink();
                }
            }
            else if (ctx.Input.KeyDeleteDown)
            {
                state.DeleteRepeatHeldTime += ctx.DeltaTime;
                state.DeleteRepeatTimer -= ctx.DeltaTime;

                for (int i = 0; i < 8 && state.DeleteRepeatTimer <= 0f; i++)
                {
                    float rate = GetKeyRepeatRate(state.DeleteRepeatHeldTime);
                    state.DeleteRepeatTimer += 1f / rate;

                    if (!ApplyDelete(ref state, buffer, ref length))
                    {
                        state.DeleteRepeatTimer = 0f;
                        break;
                    }

                    changed = true;
                    shouldScrollToCaret = true;
                    ctx.ResetCaretBlink();
                }
            }
            else
            {
                state.DeleteRepeatHeldTime = 0f;
                state.DeleteRepeatTimer = 0f;
            }

            // Arrow keys
            if (ctx.Input.KeyLeft && state.CaretPos > 0)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos--;
                if (!ctx.Input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }
            if (ctx.Input.KeyRight && state.CaretPos < length)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos++;
                if (!ctx.Input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }

            // Home/End
            if (ctx.Input.KeyHome)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos = 0;
                if (!ctx.Input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }
            if (ctx.Input.KeyEnd)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos = length;
                if (!ctx.Input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }

            // Ctrl+A - Select all
            if (ctx.Input.KeyCtrlA)
            {
                state.SelectionStart = 0;
                state.SelectionEnd = length;
                state.CaretPos = length;
                shouldScrollToCaret = true;
            }

            if (ctx.Input.KeyCtrlC && !state.WasCopyDown)
            {
                CopyTextInputSelectionToClipboard(ref state, buffer.Slice(0, length), length);
            }

            if (ctx.Input.KeyCtrlX && !state.WasCutDown)
            {
                if (CutTextInputSelectionToClipboard(ref state, buffer, ref length))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    ctx.ResetCaretBlink();
                }
            }

            if (ctx.Input.KeyCtrlV && !state.WasPasteDown)
            {
                if (PasteTextInputFromClipboard(ref state, buffer, ref length, maxLength))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    ctx.ResetCaretBlink();
                }
            }

            state.WasCopyDown = ctx.Input.KeyCtrlC;
            state.WasPasteDown = ctx.Input.KeyCtrlV;
            state.WasCutDown = ctx.Input.KeyCtrlX;

            // Escape or Enter - lose focus (unless caller keeps focus on Enter).
            if (ctx.Input.KeyEscape || (ctx.Input.KeyEnter && (flags & ImTextInputFlags.KeepFocusOnEnter) == 0))
            {
                ctx.ClearFocus();
            }

            // Click outside - lose focus
            if (ctx.Input.MousePressed && !hovered)
            {
                ctx.ClearFocus();
            }
        }
        else
        {
            state.WasCopyDown = false;
            state.WasPasteDown = false;
            state.WasCutDown = false;
        }

        // Clamp caret
        state.CaretPos = Math.Clamp(state.CaretPos, 0, length);

        // Keep horizontal scroll clamped and (optionally) follow the caret like ImGui.
        state.ScrollOffsetX = ClampHorizontalScroll(style, buffer, length, state.ScrollOffsetX, width);
        if (shouldScrollToCaret)
        {
            state.ScrollOffsetX = ScrollToCaretX(style, buffer, length, state.CaretPos, state.ScrollOffsetX, width);
        }

        // Save state
        _textInputStates[widgetId] = state;

        uint bgColor = isFocused ? style.Surface : (hovered ? style.Hover : style.Surface);
        uint borderColor = isFocused ? style.Primary : style.Border;
        float cornerRadius = (flags & ImTextInputFlags.NoRounding) != 0 ? 0f : style.CornerRadius;

        // Draw background
        if ((flags & ImTextInputFlags.NoBackground) == 0)
        {
            DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, cornerRadius, bgColor);
        }

        // Draw border
        if ((flags & ImTextInputFlags.NoBorder) == 0)
        {
            const ImTextInputFlags sideMask = ImTextInputFlags.BorderLeft | ImTextInputFlags.BorderRight | ImTextInputFlags.BorderTop | ImTextInputFlags.BorderBottom;
            if ((flags & sideMask) != 0)
            {
                float stroke = Math.Max(1f, style.BorderWidth);
                const float endInset = 1f;
                float y0 = rect.Y + endInset;
                float y1 = rect.Bottom - endInset;
                if ((flags & ImTextInputFlags.BorderLeft) != 0)
                {
                    DrawLine(rect.X + 0.5f, y0, rect.X + 0.5f, y1, stroke, borderColor);
                }
                if ((flags & ImTextInputFlags.BorderRight) != 0)
                {
                    DrawLine(rect.Right - 0.5f, y0, rect.Right - 0.5f, y1, stroke, borderColor);
                }
                if ((flags & ImTextInputFlags.BorderTop) != 0)
                {
                    DrawLine(rect.X, rect.Y + 0.5f, rect.Right, rect.Y + 0.5f, stroke, borderColor);
                }
                if ((flags & ImTextInputFlags.BorderBottom) != 0)
                {
                    DrawLine(rect.X, rect.Bottom - 0.5f, rect.Right, rect.Bottom - 0.5f, stroke, borderColor);
                }
            }
            else
            {
                DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, cornerRadius, borderColor, style.BorderWidth);
            }
        }

        // Calculate text position
        float padding = style.Padding;
        float textX = rect.X + padding;
        float textY = rect.Y + (rect.Height - style.FontSize) / 2f;

        // Clip the text/selection/caret region to keep draw calls bounded.
        Im.PushClipRect(new ImRect(x + 2f, y + 2f, width - 4f, height - 4f));

        float textDrawX = textX - state.ScrollOffsetX;

        // Draw selection highlight
        if (isFocused && state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
        {
            int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
            int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);

            float selStartX = textDrawX + MeasureTextWidth(buffer.Slice(0, selStart), style.FontSize);
            float selEndX = textDrawX + MeasureTextWidth(buffer.Slice(0, selEnd), style.FontSize);

            DrawRect(selStartX, rect.Y + 2, selEndX - selStartX, rect.Height - 4, ImStyle.WithAlpha(style.Primary, 100));
        }

        // Draw text
        var font = ctx.Font;
        if (font != null && length > 0)
        {
            var color = ImStyle.ToVector4(style.TextPrimary);
            DrawTextLocalToDrawList(CurrentViewport.CurrentDrawList, font, buffer.Slice(0, length), textDrawX, textY, style.FontSize, color);
        }

        // Draw caret
        if (isFocused && ctx.CaretVisible)
        {
            float caretX = textDrawX + MeasureTextWidth(buffer.Slice(0, state.CaretPos), style.FontSize);
            DrawRect(caretX, rect.Y + 4, 1, rect.Height - 8, style.TextPrimary);
        }

        Im.PopClipRect();

        return changed;
    }

    private static int GetCaretPosFromX(Span<char> buffer, int length, ImRect rect, float mouseX, ImStyle style, float scrollOffsetX)
    {
        float padding = style.Padding;
        float textX = rect.X + padding;
        float relativeX = (mouseX - textX) + scrollOffsetX;
        return ImTextEdit.GetCaretPosFromRelativeX(Context.Font, buffer.Slice(0, length), style.FontSize, letterSpacingPx: 0f, relativeX);
    }

    private static bool ApplyBackspace(ref TextInputState state, Span<char> buffer, ref int length)
    {
        if (state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
        {
            int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
            int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
            DeleteRange(buffer, ref length, selStart, selEnd);
            state.CaretPos = selStart;
            state.SelectionStart = -1;
            state.SelectionEnd = -1;
            return true;
        }

        if (state.CaretPos <= 0 || length <= 0)
        {
            return false;
        }

        DeleteRange(buffer, ref length, state.CaretPos - 1, state.CaretPos);
        state.CaretPos--;
        return true;
    }

    private static bool ApplyDelete(ref TextInputState state, Span<char> buffer, ref int length)
    {
        if (state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
        {
            int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
            int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
            DeleteRange(buffer, ref length, selStart, selEnd);
            state.CaretPos = selStart;
            state.SelectionStart = -1;
            state.SelectionEnd = -1;
            return true;
        }

        if (state.CaretPos >= length)
        {
            return false;
        }

        DeleteRange(buffer, ref length, state.CaretPos, state.CaretPos + 1);
        return true;
    }

    private static void CopyTextInputSelectionToClipboard(
        ref TextInputState state,
        ReadOnlySpan<char> buffer,
        int length)
    {
        if (state.SelectionStart < 0 || state.SelectionStart == state.SelectionEnd || length <= 0)
        {
            return;
        }

        int selectionStart = Math.Clamp(Math.Min(state.SelectionStart, state.SelectionEnd), 0, length);
        int selectionEnd = Math.Clamp(Math.Max(state.SelectionStart, state.SelectionEnd), 0, length);
        if (selectionEnd <= selectionStart)
        {
            return;
        }

        string selectedText = new string(buffer.Slice(selectionStart, selectionEnd - selectionStart));
        DerpLib.Derp.SetClipboardText(selectedText);
    }

    private static bool CutTextInputSelectionToClipboard(
        ref TextInputState state,
        Span<char> buffer,
        ref int length)
    {
        if (state.SelectionStart < 0 || state.SelectionStart == state.SelectionEnd || length <= 0)
        {
            return false;
        }

        int selectionStart = Math.Clamp(Math.Min(state.SelectionStart, state.SelectionEnd), 0, length);
        int selectionEnd = Math.Clamp(Math.Max(state.SelectionStart, state.SelectionEnd), 0, length);
        if (selectionEnd <= selectionStart)
        {
            return false;
        }

        string selectedText = new string(buffer.Slice(selectionStart, selectionEnd - selectionStart));
        DerpLib.Derp.SetClipboardText(selectedText);

        DeleteRange(buffer, ref length, selectionStart, selectionEnd);
        state.CaretPos = selectionStart;
        state.SelectionStart = -1;
        state.SelectionEnd = -1;
        return true;
    }

    private static bool PasteTextInputFromClipboard(
        ref TextInputState state,
        Span<char> buffer,
        ref int length,
        int maxLength)
    {
        string? clipboard = DerpLib.Derp.GetClipboardText();
        if (string.IsNullOrEmpty(clipboard))
        {
            return false;
        }

        bool hadSelection = state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd;
        if (hadSelection)
        {
            int selectionStart = Math.Clamp(Math.Min(state.SelectionStart, state.SelectionEnd), 0, length);
            int selectionEnd = Math.Clamp(Math.Max(state.SelectionStart, state.SelectionEnd), 0, length);
            if (selectionEnd > selectionStart)
            {
                DeleteRange(buffer, ref length, selectionStart, selectionEnd);
                state.CaretPos = selectionStart;
            }

            state.SelectionStart = -1;
            state.SelectionEnd = -1;
        }

        bool insertedAny = false;
        for (int characterIndex = 0; characterIndex < clipboard.Length; characterIndex++)
        {
            char character = clipboard[characterIndex];
            if (character < 32)
            {
                continue;
            }

            if (length >= maxLength)
            {
                break;
            }

            InsertChar(buffer, ref length, maxLength, state.CaretPos, character);
            state.CaretPos++;
            insertedAny = true;
        }

        return insertedAny;
    }

    private static float GetKeyRepeatRate(float heldTime)
    {
        const float initialRate = 7f;
        const float maxRate = 38f;
        const float accelTime = 1.25f;

        float t = heldTime / accelTime;
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;

        return initialRate + (maxRate - initialRate) * t;
    }

    private static float ClampHorizontalScroll(ImStyle style, ReadOnlySpan<char> text, int length, float scrollOffsetX, float viewWidth)
    {
        float padding = style.Padding;
        float contentWidth = viewWidth - padding * 2f;
        if (contentWidth <= 0f)
        {
            return 0f;
        }

        float textWidth = MeasureTextWidth(text.Slice(0, length), style.FontSize);
        float maxScroll = Math.Max(0f, textWidth - contentWidth);
        return Math.Clamp(scrollOffsetX, 0f, maxScroll);
    }

    private static float ScrollToCaretX(ImStyle style, ReadOnlySpan<char> text, int length, int caretPos, float scrollOffsetX, float viewWidth)
    {
        float padding = style.Padding;
        float contentWidth = viewWidth - padding * 2f;
        if (contentWidth <= 0f)
        {
            return 0f;
        }

        float caretX = MeasureTextWidth(text.Slice(0, caretPos), style.FontSize);
        float left = scrollOffsetX;
        float right = scrollOffsetX + contentWidth;

        float margin = Math.Min(20f, contentWidth * 0.25f);
        float targetLeft = left;

        if (caretX < left + margin)
        {
            targetLeft = caretX - margin;
        }
        else if (caretX > right - margin)
        {
            targetLeft = caretX - contentWidth + margin;
        }

        float textWidth = MeasureTextWidth(text.Slice(0, length), style.FontSize);
        float maxScroll = Math.Max(0f, textWidth - contentWidth);
        return Math.Clamp(targetLeft, 0f, maxScroll);
    }

    private static void InsertChar(Span<char> buffer, ref int length, int maxLength, int pos, char c)
    {
        ImTextEdit.InsertChar(buffer, ref length, maxLength, pos, c);
    }

    private static void DeleteRange(Span<char> buffer, ref int length, int start, int end)
    {
        ImTextEdit.DeleteRange(buffer, ref length, start, end);
    }

    /// <summary>
    /// Clear stored state for a text input widget.
    /// </summary>
    public static void ClearTextInputState(string id)
    {
        var ctx = Context;
        int widgetId = ctx.GetId(id);
        _textInputStates.Remove(widgetId);
    }

    /// <summary>
    /// Get the current state snapshot for a text input widget.
    /// Returns false if the input has no stored state yet.
    /// </summary>
    public static bool TryGetTextInputState(string id, out ImTextInputStateSnapshot stateSnapshot)
    {
        var ctx = Context;
        int widgetId = ctx.GetId(id);
        if (_textInputStates.TryGetValue(widgetId, out var state))
        {
            stateSnapshot = new ImTextInputStateSnapshot(
                state.CaretPos,
                state.SelectionStart,
                state.SelectionEnd,
                state.ScrollOffsetX);
            return true;
        }

        stateSnapshot = default;
        return false;
    }

    /// <summary>
    /// Set caret and selection for a text input widget.
    /// Returns false if the input has no stored state yet.
    /// </summary>
    public static bool SetTextInputSelection(string id, int caretPos, int selectionStart = -1, int selectionEnd = -1)
    {
        var ctx = Context;
        int widgetId = ctx.GetId(id);
        if (!_textInputStates.TryGetValue(widgetId, out var state))
        {
            return false;
        }

        state.CaretPos = Math.Max(0, caretPos);
        if (selectionStart < 0 || selectionEnd < 0)
        {
            state.SelectionStart = -1;
            state.SelectionEnd = -1;
        }
        else
        {
            state.SelectionStart = Math.Max(0, selectionStart);
            state.SelectionEnd = Math.Max(0, selectionEnd);
        }

        _textInputStates[widgetId] = state;
        ctx.ResetCaretBlink();
        return true;
    }


    //=== Clip Rect ===

    /// <summary>
    /// Push a clip rectangle to restrict rendering.
    /// </summary>
    public static void PushClipRect(ImRect rect)
    {
        EnsureInFrame();
        var viewportRect = TransformRectLocalToViewportAabb(rect);

        // Push to context (intersects with current clip).
        Context.PushClipRect(viewportRect);

        // Use the intersected clip rect for rendering so nested clips cannot escape their parent.
        var clip = Context.ClipRect;
        var clipVec = new Vector4(
            clip.X * _scale,
            clip.Y * _scale,
            clip.Width * _scale,
            clip.Height * _scale);
        CurrentViewport.CurrentDrawList.SetClipRect(clipVec);
        // Windows can render on either WindowContent (main layout) or FloatingWindows (floating groups),
        // and some code sets sort keys on those lists directly. Keep clip state aligned on both.
        CurrentViewport.GetDrawList(Rendering.ImDrawLayer.WindowContent).SetClipRect(clipVec);
        CurrentViewport.GetDrawList(Rendering.ImDrawLayer.FloatingWindows).SetClipRect(clipVec);
        _currentClipRect = clipVec;
    }

    /// <summary>
    /// Push a clip rectangle that overrides (does not intersect) the current clip stack.
    /// Useful for viewport-space overlays/popovers that are rendered outside a window's content rect.
    /// </summary>
    public static void PushClipRectOverride(ImRect rect)
    {
        EnsureInFrame();

        var viewportRect = TransformRectLocalToViewportAabb(rect);

        Context.PushClipRectOverride(viewportRect);

        var clip = Context.ClipRect;
        var clipVec = new Vector4(
            clip.X * _scale,
            clip.Y * _scale,
            clip.Width * _scale,
            clip.Height * _scale);

        CurrentViewport.CurrentDrawList.SetClipRect(clipVec);
        CurrentViewport.GetDrawList(Rendering.ImDrawLayer.WindowContent).SetClipRect(clipVec);
        CurrentViewport.GetDrawList(Rendering.ImDrawLayer.FloatingWindows).SetClipRect(clipVec);
        _currentClipRect = clipVec;
    }

    /// <summary>
    /// Pop the current clip rectangle.
    /// </summary>
    public static void PopClipRect()
    {
        EnsureInFrame();
        Context.PopClipRect();
        var clip = Context.ClipRect;
        _currentClipRect = new Vector4(
            clip.X * _scale,
            clip.Y * _scale,
            clip.Width * _scale,
            clip.Height * _scale);
        // Restore clip rect on draw lists (or clear if no clip)
        if (clip.Width > 0 && clip.Height > 0)
        {
            CurrentViewport.CurrentDrawList.SetClipRect(_currentClipRect);
            CurrentViewport.GetDrawList(Rendering.ImDrawLayer.WindowContent).SetClipRect(_currentClipRect);
            CurrentViewport.GetDrawList(Rendering.ImDrawLayer.FloatingWindows).SetClipRect(_currentClipRect);
        }
        else
        {
            CurrentViewport.CurrentDrawList.ClearClipRect();
            CurrentViewport.GetDrawList(Rendering.ImDrawLayer.WindowContent).ClearClipRect();
            CurrentViewport.GetDrawList(Rendering.ImDrawLayer.FloatingWindows).ClearClipRect();
        }
    }


    //=== Dropdown ===

    /// <summary>
    /// Draw a dropdown at the current layout position.
    /// Returns true if selection changed.
    /// </summary>
    public static bool Dropdown(string id, ReadOnlySpan<string> options, ref int selectedIndex, float width = 150)
    {
        EnsureInFrame();
        var style = Context.Style;
        float height = style.MinButtonHeight;
        var rect = ImLayout.AllocateRect(width, height);
        return DropdownSized(id, options, ref selectedIndex, rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>
    /// Draw a dropdown at explicit position (no layout).
    /// Returns true if selection changed.
    /// </summary>
    public static bool Dropdown(string id, ReadOnlySpan<string> options, ref int selectedIndex, float x, float y, float width)
    {
        EnsureInFrame();
        var style = Context.Style;
        return DropdownSized(id, options, ref selectedIndex, x, y, width, style.MinButtonHeight);
    }

    public static bool Dropdown(string id, ReadOnlySpan<string> options, ref int selectedIndex, float x, float y, float width, ImDropdownFlags flags)
    {
        EnsureInFrame();
        var style = Context.Style;
        return DropdownSized(id, options, ref selectedIndex, x, y, width, style.MinButtonHeight, flags);
    }

    /// <summary>
    /// Draw a dropdown at explicit position (no layout) with an explicit height.
    /// Returns true if selection changed.
    /// </summary>
    public static bool DropdownSized(string id, ReadOnlySpan<string> options, ref int selectedIndex, float x, float y, float width, float height)
    {
        EnsureInFrame();
        return DropdownSizedCore(id, options, ref selectedIndex, x, y, width, height, ImDropdownFlags.None);
    }

    public static bool DropdownSized(string id, ReadOnlySpan<string> options, ref int selectedIndex, float x, float y, float width, float height, ImDropdownFlags flags)
    {
        EnsureInFrame();
        return DropdownSizedCore(id, options, ref selectedIndex, x, y, width, height, flags);
    }

    private static bool DropdownSizedCore(string id, ReadOnlySpan<string> options, ref int selectedIndex, float x, float y, float width, float height, ImDropdownFlags flags)
    {
        var ctx = Context;
        var style = ctx.Style;
        var mousePos = MousePos;
        var viewport = CurrentViewport;

        int widgetId = ctx.GetId(id);
        var rect = new ImRect(x, y, width, height);
        var visualRect = GetDropdownVisualRect(rect);
        bool isOpen = _openDropdownId == widgetId;
        bool hovered = rect.Contains(mousePos);
        bool changed = false;

        // Toggle dropdown on click (press claims ownership; release clears active).
        if (hovered)
        {
            ctx.SetHot(widgetId);
            if (ctx.Input.MousePressed)
            {
                ctx.SetActive(widgetId);
                if (isOpen)
                {
                    _openDropdownId = 0;
                    _activeDropdownItemId = 0;
                    _activeDropdownItemIndex = -1;
                }
                else
                {
                    _openDropdownId = widgetId;
                    _openDropdownFrame = ctx.FrameCount;
                    _dropdownScrollY = 0;
                    _activeDropdownItemId = 0;
                    _activeDropdownItemIndex = -1;
                }
            }
        }

        if (ctx.IsActive(widgetId) && ctx.Input.MouseReleased)
        {
            ctx.ClearActive();
        }

        // Close if clicked outside
        if (isOpen && ctx.Input.MousePressed && !hovered)
        {
            // Check if click is in popup area
            float itemHeight = style.MinButtonHeight;
            float contentHeight = options.Length * itemHeight;
            var popupRect = ComputePopupRectLocal(viewport, visualRect, itemHeight, contentHeight);
            var popupRectViewport = TransformRectLocalToViewportAabb(popupRect);
            if (ImPopover.ShouldClose(
                    openedFrame: _openDropdownFrame,
                    closeOnEscape: false,
                    closeOnOutsideButtons: ImPopoverCloseButtons.Left,
                    consumeCloseClick: false,
                    requireNoMouseOwner: false,
                    useViewportMouseCoordinates: true,
                    insideRect: popupRectViewport))
            {
                _openDropdownId = 0;
                isOpen = false;
                _activeDropdownItemId = 0;
                _activeDropdownItemIndex = -1;
            }
        }

        // Draw button (optionally flat, for tool/inspector UIs).
        if ((flags & ImDropdownFlags.NoBackground) == 0)
        {
            uint bgColor = isOpen ? style.Active : (hovered ? style.Hover : style.Surface);
            DrawRoundedRect(visualRect.X, visualRect.Y, visualRect.Width, visualRect.Height, style.CornerRadius, bgColor);
        }
        if ((flags & ImDropdownFlags.NoBorder) == 0)
        {
            DrawRoundedRectStroke(visualRect.X, visualRect.Y, visualRect.Width, visualRect.Height, style.CornerRadius, style.Border, style.BorderWidth);
        }
        else if (hovered || isOpen)
        {
            DrawLine(visualRect.X, visualRect.Bottom - 1f, visualRect.Right, visualRect.Bottom - 1f, 1f, ImStyle.WithAlpha(style.Border, 160));
        }

        // Draw selected text
        var font = ctx.Font;
        if (font != null)
        {
            float textY = visualRect.Y + (visualRect.Height - style.FontSize) / 2f;
            bool validSelection = selectedIndex >= 0 && selectedIndex < options.Length;
            var color = ImStyle.ToVector4(validSelection ? style.TextPrimary : style.TextSecondary);
            ReadOnlySpan<char> text = validSelection ? options[selectedIndex].AsSpan() : "—".AsSpan();
            DrawTextLocalToDrawList(CurrentViewport.CurrentDrawList, font, text, visualRect.X + style.Padding, textY, style.FontSize, color);
        }

        // Draw dropdown arrow
        float arrowSize = 6;
        float arrowX = visualRect.Right - style.Padding - arrowSize;
        float arrowY = visualRect.Center.Y;
        DrawDropdownArrow(arrowX, arrowY, arrowSize, isOpen, style.TextSecondary);

        // Handle popup interaction and defer rendering if open
        if (isOpen)
        {
            float itemHeight = style.MinButtonHeight;
            float contentHeight = options.Length * itemHeight;
            var popupRect = ComputePopupRectLocal(viewport, visualRect, itemHeight, contentHeight);
            using var popupOverlayScope = ImPopover.PushOverlayScopeLocal(popupRect);
            var popupRectViewport = TransformRectLocalToViewportAabb(popupRect);
            bool popupHovered = popupRectViewport.Contains(MousePosViewport);
            bool pressedInsidePopup = popupHovered && ctx.Input.MousePressed;
            bool releasedInsidePopup = popupHovered && ctx.Input.MouseReleased;
            if (popupHovered)
            {
                ctx.SetHot(widgetId);
            }

            // Handle scrolling (interaction - must happen now)
            if (popupHovered)
            {
                float maxScroll = Math.Max(0, contentHeight - popupRect.Height);
                float scrollDelta = ctx.Input.ScrollDelta;
                _dropdownScrollY -= scrollDelta * 30f;
                _dropdownScrollY = Math.Clamp(_dropdownScrollY, 0, maxScroll);
                if (scrollDelta != 0f)
                {
                    ctx.ConsumeScroll();
                }
            }

            // Handle item clicks (interaction - must happen now)
            int hoveredItem = -1;
            float scrollbarWidth = style.ScrollbarWidth;
            bool hasScrollbar = contentHeight > popupRect.Height;
            float contentWidth = hasScrollbar ? popupRect.Width - scrollbarWidth : popupRect.Width;
            for (int i = 0; i < options.Length; i++)
            {
                float itemY = popupRect.Y + i * itemHeight - _dropdownScrollY;
                if (itemY + itemHeight < popupRect.Y || itemY > popupRect.Bottom)
                {
                    continue;
                }

                var itemRect = new ImRect(popupRect.X + 1, itemY, contentWidth - 2, itemHeight);
                var itemRectViewport = TransformRectLocalToViewportAabb(itemRect);
                if (itemRectViewport.Contains(MousePosViewport) && popupHovered)
                {
                    hoveredItem = i;
                    if (ctx.Input.MousePressed)
                    {
                        ctx.PushId(widgetId);
                        int itemId = ctx.GetId(i);
                        ctx.PopId();
                        ctx.SetActive(itemId);
                        if (ctx.IsActive(itemId))
                        {
                            _activeDropdownItemId = itemId;
                            _activeDropdownItemIndex = i;
                            ctx.ConsumeMouseLeftPress();
                        }
                    }
                }
            }

            if (_activeDropdownItemId != 0 && ctx.IsActive(_activeDropdownItemId) && ctx.Input.MouseReleased)
            {
                if (hoveredItem == _activeDropdownItemIndex)
                {
                    selectedIndex = _activeDropdownItemIndex;
                    _openDropdownId = 0;
                    changed = true;
                }
                ctx.ConsumeMouseLeftRelease();
                ctx.ClearActive();
                _activeDropdownItemId = 0;
                _activeDropdownItemIndex = -1;
            }

            // Swallow pointer events over the popup so controls drawn later in the frame
            // cannot click-through behind the dropdown (especially in Inspector).
            if (pressedInsidePopup)
            {
                ctx.ConsumeMouseLeftPress();
            }
            if (releasedInsidePopup)
            {
                ctx.ConsumeMouseLeftRelease();
            }

            // Store state for deferred rendering (struct-based, no closures)
            int optionCount = Math.Min(options.Length, _dropdownOptionsScratch.Length);
            for (int i = 0; i < optionCount; i++)
            {
                _dropdownOptionsScratch[i] = options[i];
            }

            _hasDeferredDropdown = true;
            _deferredDropdown = new DeferredDropdownState
            {
                PopupRect = popupRectViewport,
                Options = _dropdownOptionsScratch,
                OptionCount = optionCount,
                SelectedIndex = selectedIndex,
                HoveredIndex = hoveredItem,
                ScrollY = _dropdownScrollY,
                SortKey = CurrentViewport.CurrentDrawList.GetSortKey() + 1,
                Viewport = CurrentViewport
            };
        }

        return changed;
    }

    /// <summary>
    /// Draw a dropdown at explicit position (no layout) with a non-interactive overlay region on the right.
    /// Returns true if selection changed.
    /// </summary>
    public static bool Dropdown(string id, ReadOnlySpan<string> options, ref int selectedIndex, float x, float y, float width, float rightOverlayWidth)
    {
        EnsureInFrame();
        var style = Context.Style;
        return Dropdown(id, options, ref selectedIndex, x, y, width, style.MinButtonHeight, rightOverlayWidth);
    }

    /// <summary>
    /// Draw a dropdown at explicit position (no layout) with explicit height and a non-interactive overlay region on the right.
    /// Returns true if selection changed.
    /// </summary>
    public static bool Dropdown(string id, ReadOnlySpan<string> options, ref int selectedIndex, float x, float y, float width, float height, float rightOverlayWidth)
    {
        EnsureInFrame();
        var ctx = Context;
        var style = ctx.Style;
        var mousePos = MousePos;
        var viewport = CurrentViewport;

        int widgetId = ctx.GetId(id);
        var rect = new ImRect(x, y, width, height);
        var visualRect = GetDropdownVisualRect(rect);
        float overlay = MathF.Max(0f, rightOverlayWidth);
        var interactiveRect = new ImRect(x, y, MathF.Max(0f, width - overlay), height);

        bool isOpen = _openDropdownId == widgetId;
        bool hoveredAny = rect.Contains(mousePos);
        bool hoveredInteractive = interactiveRect.Contains(mousePos);
        bool changed = false;

        // Toggle dropdown on click (press claims ownership; release clears active).
        if (hoveredAny)
        {
            ctx.SetHot(widgetId);
            if (hoveredInteractive && ctx.Input.MousePressed)
            {
                ctx.SetActive(widgetId);
                if (isOpen)
                {
                    _openDropdownId = 0;
                    _activeDropdownItemId = 0;
                    _activeDropdownItemIndex = -1;
                }
                else
                {
                    _openDropdownId = widgetId;
                    _openDropdownFrame = ctx.FrameCount;
                    _dropdownScrollY = 0;
                    _activeDropdownItemId = 0;
                    _activeDropdownItemIndex = -1;
                }
            }
        }

        if (ctx.IsActive(widgetId) && ctx.Input.MouseReleased)
        {
            ctx.ClearActive();
        }

        // Close if clicked outside
        if (isOpen && ctx.Input.MousePressed && !hoveredAny)
        {
            float itemHeight = style.MinButtonHeight;
            float contentHeight = options.Length * itemHeight;
            var popupRect = ComputePopupRectLocal(viewport, visualRect, itemHeight, contentHeight);
            var popupRectViewport = TransformRectLocalToViewportAabb(popupRect);
            if (ImPopover.ShouldClose(
                    openedFrame: _openDropdownFrame,
                    closeOnEscape: false,
                    closeOnOutsideButtons: ImPopoverCloseButtons.Left,
                    consumeCloseClick: false,
                    requireNoMouseOwner: false,
                    useViewportMouseCoordinates: true,
                    insideRect: popupRectViewport))
            {
                _openDropdownId = 0;
                isOpen = false;
                _activeDropdownItemId = 0;
                _activeDropdownItemIndex = -1;
            }
        }

        // Draw button
        uint bgColor = isOpen ? style.Active : (hoveredAny ? style.Hover : style.Surface);
        DrawRoundedRect(visualRect.X, visualRect.Y, visualRect.Width, visualRect.Height, style.CornerRadius, bgColor);
        DrawRoundedRectStroke(visualRect.X, visualRect.Y, visualRect.Width, visualRect.Height, style.CornerRadius, style.Border, style.BorderWidth);

        // Draw selected text
        var font = ctx.Font;
        if (font != null)
        {
            float textY = visualRect.Y + (visualRect.Height - style.FontSize) / 2f;
            bool validSelection = selectedIndex >= 0 && selectedIndex < options.Length;
            var color = ImStyle.ToVector4(validSelection ? style.TextPrimary : style.TextSecondary);
            ReadOnlySpan<char> text = validSelection ? options[selectedIndex].AsSpan() : "—".AsSpan();
            DrawTextLocalToDrawList(CurrentViewport.CurrentDrawList, font, text, visualRect.X + style.Padding, textY, style.FontSize, color);
        }

        // Draw dropdown arrow (shifted left by overlay region)
        float arrowSize = 6;
        float arrowX = visualRect.Right - style.Padding - arrowSize - overlay;
        float arrowY = visualRect.Center.Y;
        DrawDropdownArrow(arrowX, arrowY, arrowSize, isOpen, style.TextSecondary);

        // Handle popup interaction and defer rendering if open
        if (isOpen)
        {
            float itemHeight = style.MinButtonHeight;
            float contentHeight = options.Length * itemHeight;
            var popupRect = ComputePopupRectLocal(viewport, visualRect, itemHeight, contentHeight);
            using var popupOverlayScope = ImPopover.PushOverlayScopeLocal(popupRect);
            var popupRectViewport = TransformRectLocalToViewportAabb(popupRect);
            bool popupHovered = popupRectViewport.Contains(MousePosViewport);
            bool pressedInsidePopup = popupHovered && ctx.Input.MousePressed;
            bool releasedInsidePopup = popupHovered && ctx.Input.MouseReleased;
            if (popupHovered)
            {
                ctx.SetHot(widgetId);
            }

            // Handle scrolling (interaction - must happen now)
            if (popupHovered)
            {
                float maxScroll = Math.Max(0, contentHeight - popupRect.Height);
                float scrollDelta = ctx.Input.ScrollDelta;
                _dropdownScrollY -= scrollDelta * 30f;
                _dropdownScrollY = Math.Clamp(_dropdownScrollY, 0, maxScroll);
                if (scrollDelta != 0f)
                {
                    ctx.ConsumeScroll();
                }
            }

            // Handle item clicks (interaction - must happen now)
            int hoveredItem = -1;
            float scrollbarWidth = style.ScrollbarWidth;
            bool hasScrollbar = contentHeight > popupRect.Height;
            float contentWidth = hasScrollbar ? popupRect.Width - scrollbarWidth : popupRect.Width;
            for (int i = 0; i < options.Length; i++)
            {
                float itemY = popupRect.Y + i * itemHeight - _dropdownScrollY;
                if (itemY + itemHeight < popupRect.Y || itemY > popupRect.Bottom)
                {
                    continue;
                }

                var itemRect = new ImRect(popupRect.X + 1, itemY, contentWidth - 2, itemHeight);
                var itemRectViewport = TransformRectLocalToViewportAabb(itemRect);
                if (itemRectViewport.Contains(MousePosViewport) && popupHovered)
                {
                    hoveredItem = i;
                    if (ctx.Input.MousePressed)
                    {
                        ctx.PushId(widgetId);
                        int itemId = ctx.GetId(i);
                        ctx.PopId();
                        ctx.SetActive(itemId);
                        if (ctx.IsActive(itemId))
                        {
                            _activeDropdownItemId = itemId;
                            _activeDropdownItemIndex = i;
                            ctx.ConsumeMouseLeftPress();
                        }
                    }
                }
            }

            if (_activeDropdownItemId != 0 && ctx.IsActive(_activeDropdownItemId) && ctx.Input.MouseReleased)
            {
                if (hoveredItem == _activeDropdownItemIndex)
                {
                    selectedIndex = _activeDropdownItemIndex;
                    _openDropdownId = 0;
                    changed = true;
                }
                ctx.ConsumeMouseLeftRelease();
                ctx.ClearActive();
                _activeDropdownItemId = 0;
                _activeDropdownItemIndex = -1;
            }

            // Swallow pointer events over the popup so controls drawn later in the frame
            // cannot click-through behind the dropdown (especially in Inspector).
            if (pressedInsidePopup)
            {
                ctx.ConsumeMouseLeftPress();
            }
            if (releasedInsidePopup)
            {
                ctx.ConsumeMouseLeftRelease();
            }

            // Store state for deferred rendering (struct-based, no closures)
            int optionCount = Math.Min(options.Length, _dropdownOptionsScratch.Length);
            for (int i = 0; i < optionCount; i++)
            {
                _dropdownOptionsScratch[i] = options[i];
            }

            _hasDeferredDropdown = true;
            _deferredDropdown = new DeferredDropdownState
            {
                PopupRect = popupRectViewport,
                Options = _dropdownOptionsScratch,
                OptionCount = optionCount,
                SelectedIndex = selectedIndex,
                HoveredIndex = hoveredItem,
                ScrollY = _dropdownScrollY,
                SortKey = CurrentViewport.CurrentDrawList.GetSortKey() + 1,
                Viewport = CurrentViewport
            };
        }

        return changed;
    }

    private static ImRect GetDropdownVisualRect(ImRect rect)
    {
        const float verticalInset = 2f;
        float inset = rect.Height > verticalInset * 2f ? verticalInset : 0f;
        return new ImRect(rect.X, rect.Y + inset, rect.Width, rect.Height - inset * 2f);
    }

    private static ImRect ComputePopupRectLocal(ImViewport? viewport, ImRect anchorRectLocal, float itemHeight, float contentHeight)
    {
        const float viewportMargin = 6f;
        const float popupGap = 2f;

        if (viewport == null)
        {
            float fallbackHeight = Math.Min(contentHeight, itemHeight * 10f);
            return new ImRect(anchorRectLocal.X, anchorRectLocal.Bottom + popupGap, anchorRectLocal.Width, fallbackHeight);
        }

        // Work in viewport space for clamping, then convert back to local.
        var anchorRectViewport = TransformRectLocalToViewportAabb(anchorRectLocal);
        float anchorXViewport = anchorRectViewport.X;
        float anchorYViewport = anchorRectViewport.Y;
        float anchorBottomViewport = anchorRectViewport.Bottom;

        float availableBelow = viewport.Size.Y - viewportMargin - (anchorBottomViewport + popupGap);
        float availableAbove = anchorYViewport - viewportMargin - popupGap;

        bool placeBelow = true;
        float requestedHeight = Math.Max(itemHeight, contentHeight);
        if (availableBelow < itemHeight && availableAbove > availableBelow)
        {
            placeBelow = false;
        }
        else if (availableBelow < requestedHeight && availableAbove > availableBelow)
        {
            placeBelow = false;
        }

        float maxHeight = placeBelow ? Math.Max(itemHeight, availableBelow) : Math.Max(itemHeight, availableAbove);
        float maxHeightOnScreen = Math.Max(itemHeight, viewport.Size.Y - viewportMargin * 2f);
        maxHeight = Math.Min(maxHeight, maxHeightOnScreen);
        float popupHeight = Math.Min(contentHeight, maxHeight);

        float xViewport = anchorXViewport;
        float yViewport = placeBelow
            ? anchorBottomViewport + popupGap
            : anchorYViewport - popupGap - popupHeight;

        // Clamp to viewport bounds.
        if (xViewport + anchorRectLocal.Width > viewport.Size.X - viewportMargin)
        {
            xViewport = viewport.Size.X - viewportMargin - anchorRectLocal.Width;
        }
        if (xViewport < viewportMargin)
        {
            xViewport = viewportMargin;
        }

        if (yViewport + popupHeight > viewport.Size.Y - viewportMargin)
        {
            yViewport = viewport.Size.Y - viewportMargin - popupHeight;
        }
        if (yViewport < viewportMargin)
        {
            yViewport = viewportMargin;
        }

        Vector2 localTopLeft = TransformPointViewportToLocal(new Vector2(xViewport, yViewport));
        float localScaleY = GetTransformScaleY();
        float popupHeightLocal = localScaleY > 0.00001f ? popupHeight / localScaleY : popupHeight;
        return new ImRect(localTopLeft.X, localTopLeft.Y, anchorRectLocal.Width, popupHeightLocal);
    }

    private static void DrawDropdownArrow(float x, float y, float size, bool isOpen, uint color)
    {
        float topY = y - size * 0.5f;
        ImIcons.DrawChevron(x, topY, size, isOpen ? ImIcons.ChevronDirection.Up : ImIcons.ChevronDirection.Down, color);
    }

    /// <summary>
    /// Close any open dropdown.
    /// </summary>
    public static void CloseAllDropdowns()
    {
        _openDropdownId = 0;
        _openDropdownFrame = 0;
    }

    /// <summary>
    /// True if any dropdown popup is currently open (persists from previous frame until closed).
    /// Useful for suppressing input in custom widgets that share the same screen space.
    /// </summary>
    public static bool IsAnyDropdownOpen => _openDropdownId != 0;


    //=== ComboBox ===

    /// <summary>
    /// Draw a combo box (text input with suggestions) at the current layout position.
    /// Returns true if the text changed or a suggestion was selected.
    /// </summary>
    public static bool ComboBox(string id, Span<char> buffer, ref int length, int maxLength,
        ReadOnlySpan<string> suggestions, float width = 150)
    {
        EnsureInFrame();
        var style = Context.Style;
        float height = style.FontSize + style.Padding * 2;
        var rect = ImLayout.AllocateRect(width, height);
        return ComboBox(id, buffer, ref length, maxLength, suggestions, rect.X, rect.Y, rect.Width);
    }

    /// <summary>
    /// Draw a combo box at explicit position (no layout).
    /// Returns true if the text changed or a suggestion was selected.
    /// </summary>
    public static bool ComboBox(string id, Span<char> buffer, ref int length, int maxLength,
        ReadOnlySpan<string> suggestions, float x, float y, float width)
    {
        EnsureInFrame();
        var ctx = Context;
        var style = ctx.Style;
        var mousePos = MousePos;

        int widgetId = ctx.GetId(id);
        float height = style.FontSize + style.Padding * 2;
        var rect = new ImRect(x, y, width, height);
        bool isFocused = ctx.IsFocused(widgetId);
        bool hovered = rect.Contains(mousePos);
        bool changed = false;
        bool shouldScrollToCaret = false;

        float padding = style.Padding;
        float arrowSize = 6f;
        float arrowAreaWidth = arrowSize + padding * 3f;
        float textAreaWidth = rect.Width - arrowAreaWidth;
        if (textAreaWidth < 40f)
        {
            textAreaWidth = rect.Width;
        }

        var textRect = new ImRect(rect.X, rect.Y, textAreaWidth, rect.Height);

        if (!_textInputStates.TryGetValue(widgetId, out var state))
        {
            state = new TextInputState { CaretPos = length, SelectionStart = -1, SelectionEnd = -1 };
        }

        int suggestionCount = 0;
        int filteredCount = 0;
        int filterTextLength = 0;

        bool isOpen = _openComboId == widgetId;

        if (hovered)
        {
            ctx.SetHot(widgetId);
            if (ctx.Input.MousePressed)
            {
                ctx.RequestFocus(widgetId);
                ctx.SetActive(widgetId);
                _openComboId = widgetId;
                _openComboFrame = ctx.FrameCount;
                _comboScrollY = 0;
                _highlightedIndex = -1;

                float mouseX = Math.Clamp(mousePos.X, textRect.X + padding, textRect.Right - padding);
                state.CaretPos = GetCaretPosFromX(buffer, length, textRect, mouseX, style, state.ScrollOffsetX);
                state.SelectionStart = state.CaretPos;
                state.SelectionEnd = state.CaretPos;
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }
        }

        isOpen = _openComboId == widgetId;

        // Drag selection (mouse down while active).
        if (ctx.IsActive(widgetId))
        {
            if (ctx.Input.MouseDown)
            {
                float visibleMinX = textRect.X + padding + 4f;
                float visibleMaxX = textRect.Right - padding - 4f;

                float mouseX = mousePos.X;
                if (mouseX < visibleMinX)
                {
                    float overshoot = visibleMinX - mouseX;
                    state.ScrollOffsetX -= (200f + overshoot * 12f) * ctx.DeltaTime;
                }
                else if (mouseX > visibleMaxX)
                {
                    float overshoot = mouseX - visibleMaxX;
                    state.ScrollOffsetX += (200f + overshoot * 12f) * ctx.DeltaTime;
                }

                state.ScrollOffsetX = ClampHorizontalScroll(style, buffer, length, state.ScrollOffsetX, textRect.Width);

                float clampedMouseX = Math.Clamp(mouseX, textRect.X + padding, textRect.Right - padding);
                int caretPos = GetCaretPosFromX(buffer, length, textRect, clampedMouseX, style, state.ScrollOffsetX);
                state.CaretPos = caretPos;
                state.SelectionEnd = caretPos;
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }
            else if (ctx.Input.MouseReleased)
            {
                ctx.ClearActive();
            }
        }

        // Filter suggestions based on current text (allocation-free).
        if (isOpen)
        {
            suggestionCount = Math.Min(suggestions.Length, _comboSuggestionsScratch.Length);
            for (int i = 0; i < suggestionCount; i++)
            {
                _comboSuggestionsScratch[i] = suggestions[i];
            }

            filterTextLength = Math.Min(length, _comboFilterTextScratch.Length);
            for (int i = 0; i < filterTextLength; i++)
            {
                _comboFilterTextScratch[i] = buffer[i];
            }

            ReadOnlySpan<char> filterText = _comboFilterTextScratch.AsSpan(0, filterTextLength);
            if (filterTextLength == 0)
            {
                filteredCount = suggestionCount;
                for (int i = 0; i < filteredCount; i++)
                {
                    _comboFilteredIndicesScratch[i] = i;
                }
            }
            else
            {
                for (int i = 0; i < suggestionCount; i++)
                {
                    string suggestion = _comboSuggestionsScratch[i];
                    if (ImTextSearch.ContainsOrdinalIgnoreCase(suggestion, filterText))
                    {
                        _comboFilteredIndicesScratch[filteredCount++] = i;
                        if (filteredCount >= _comboFilteredIndicesScratch.Length)
                        {
                            break;
                        }
                    }
                }
            }
        }

        // Handle keyboard navigation when focused
        if (isFocused && isOpen && filteredCount > 0)
        {
            if (ctx.Input.KeyDown)
            {
                _highlightedIndex = Math.Min(_highlightedIndex + 1, filteredCount - 1);
            }
            if (ctx.Input.KeyUp)
            {
                _highlightedIndex = Math.Max(_highlightedIndex - 1, 0);
            }
            if (ctx.Input.KeyEnter && _highlightedIndex >= 0)
            {
                // Select highlighted suggestion
                int selectedSuggestionIndex = _comboFilteredIndicesScratch[_highlightedIndex];
                SetText(buffer, ref length, maxLength, _comboSuggestionsScratch[selectedSuggestionIndex]);
                state.CaretPos = length;
                state.SelectionStart = -1;
                state.SelectionEnd = -1;
                shouldScrollToCaret = true;
                _openComboId = 0;
                ctx.ClearFocus();
                changed = true;
            }
            if (ctx.Input.KeyEscape)
            {
                _openComboId = 0;
                ctx.ClearFocus();
            }
        }

        // Draw text input background
        uint bgColor = isFocused ? style.Surface : (hovered ? style.Hover : style.Surface);
        uint borderColor = isFocused ? style.Primary : style.Border;
        DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, bgColor);
        DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, style.CornerRadius, borderColor, style.BorderWidth);

        // Handle text input
        if (isFocused)
        {
            ctx.WantCaptureKeyboard = true;

            var input = ctx.Input;

            // Typed characters.
            unsafe
            {
                for (int i = 0; i < input.InputCharCount; i++)
                {
                    char c = input.InputChars[i];
                    if (c < 32)
                    {
                        continue;
                    }

                    if (state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
                    {
                        int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
                        int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);
                        DeleteRange(buffer, ref length, selStart, selEnd);
                        state.CaretPos = selStart;
                        state.SelectionStart = -1;
                        state.SelectionEnd = -1;
                    }

                    if (length >= maxLength)
                    {
                        continue;
                    }

                    InsertChar(buffer, ref length, maxLength, state.CaretPos, c);
                    state.CaretPos++;
                    changed = true;
                    shouldScrollToCaret = true;
                    _highlightedIndex = -1;
                    ctx.ResetCaretBlink();
                }
            }

            // Backspace (with repeat/accel).
            if (input.KeyBackspace)
            {
                state.BackspaceRepeatHeldTime = 0f;
                state.BackspaceRepeatTimer = 0.45f;
                if (ApplyBackspace(ref state, buffer, ref length))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    _highlightedIndex = -1;
                    ctx.ResetCaretBlink();
                }
            }
            else if (input.KeyBackspaceDown)
            {
                state.BackspaceRepeatHeldTime += ctx.DeltaTime;
                state.BackspaceRepeatTimer -= ctx.DeltaTime;

                for (int i = 0; i < 8 && state.BackspaceRepeatTimer <= 0f; i++)
                {
                    float rate = GetKeyRepeatRate(state.BackspaceRepeatHeldTime);
                    state.BackspaceRepeatTimer += 1f / rate;

                    if (!ApplyBackspace(ref state, buffer, ref length))
                    {
                        state.BackspaceRepeatTimer = 0f;
                        break;
                    }

                    changed = true;
                    shouldScrollToCaret = true;
                    _highlightedIndex = -1;
                    ctx.ResetCaretBlink();
                }
            }
            else
            {
                state.BackspaceRepeatHeldTime = 0f;
                state.BackspaceRepeatTimer = 0f;
            }

            // Delete (with repeat/accel).
            if (input.KeyDelete)
            {
                state.DeleteRepeatHeldTime = 0f;
                state.DeleteRepeatTimer = 0.45f;
                if (ApplyDelete(ref state, buffer, ref length))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    _highlightedIndex = -1;
                    ctx.ResetCaretBlink();
                }
            }
            else if (input.KeyDeleteDown)
            {
                state.DeleteRepeatHeldTime += ctx.DeltaTime;
                state.DeleteRepeatTimer -= ctx.DeltaTime;

                for (int i = 0; i < 8 && state.DeleteRepeatTimer <= 0f; i++)
                {
                    float rate = GetKeyRepeatRate(state.DeleteRepeatHeldTime);
                    state.DeleteRepeatTimer += 1f / rate;

                    if (!ApplyDelete(ref state, buffer, ref length))
                    {
                        state.DeleteRepeatTimer = 0f;
                        break;
                    }

                    changed = true;
                    shouldScrollToCaret = true;
                    _highlightedIndex = -1;
                    ctx.ResetCaretBlink();
                }
            }
            else
            {
                state.DeleteRepeatHeldTime = 0f;
                state.DeleteRepeatTimer = 0f;
            }

            // Left/Right.
            if (input.KeyLeft && state.CaretPos > 0)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos--;
                if (!input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }
            if (input.KeyRight && state.CaretPos < length)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos++;
                if (!input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }

            // Home/End.
            if (input.KeyHome)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos = 0;
                if (!input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }
            if (input.KeyEnd)
            {
                int caretBeforeMove = state.CaretPos;
                state.CaretPos = length;
                if (!input.KeyShift)
                {
                    state.SelectionStart = -1;
                    state.SelectionEnd = -1;
                }
                else
                {
                    if (state.SelectionStart < 0)
                    {
                        state.SelectionStart = caretBeforeMove;
                    }
                    state.SelectionEnd = state.CaretPos;
                }
                shouldScrollToCaret = true;
                ctx.ResetCaretBlink();
            }

            // Ctrl+A.
            if (input.KeyCtrlA)
            {
                state.SelectionStart = 0;
                state.SelectionEnd = length;
                state.CaretPos = length;
                shouldScrollToCaret = true;
            }

            if (input.KeyCtrlC && !state.WasCopyDown)
            {
                CopyTextInputSelectionToClipboard(ref state, buffer.Slice(0, length), length);
            }

            if (input.KeyCtrlX && !state.WasCutDown)
            {
                if (CutTextInputSelectionToClipboard(ref state, buffer, ref length))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    _highlightedIndex = -1;
                    ctx.ResetCaretBlink();
                }
            }

            if (input.KeyCtrlV && !state.WasPasteDown)
            {
                if (PasteTextInputFromClipboard(ref state, buffer, ref length, maxLength))
                {
                    changed = true;
                    shouldScrollToCaret = true;
                    _highlightedIndex = -1;
                    ctx.ResetCaretBlink();
                }
            }

            state.WasCopyDown = input.KeyCtrlC;
            state.WasPasteDown = input.KeyCtrlV;
            state.WasCutDown = input.KeyCtrlX;
        }
        else
        {
            state.WasCopyDown = false;
            state.WasPasteDown = false;
            state.WasCutDown = false;
        }

        // Clamp caret and keep horizontal scroll clamped and (optionally) following the caret.
        state.CaretPos = Math.Clamp(state.CaretPos, 0, length);
        state.ScrollOffsetX = ClampHorizontalScroll(style, buffer, length, state.ScrollOffsetX, textRect.Width);
        if (shouldScrollToCaret)
        {
            state.ScrollOffsetX = ScrollToCaretX(style, buffer, length, state.CaretPos, state.ScrollOffsetX, textRect.Width);
        }

        _textInputStates[widgetId] = state;

        // Draw text
        float textX = textRect.X + padding;
        float textY = textRect.Y + (textRect.Height - style.FontSize) / 2f;
        float textDrawX = textX - state.ScrollOffsetX;

        Im.PushClipRect(new ImRect(x + 2f, y + 2f, textRect.Width - 4f, height - 4f));

        // Draw selection highlight
        if (isFocused && state.SelectionStart >= 0 && state.SelectionStart != state.SelectionEnd)
        {
            int selStart = Math.Min(state.SelectionStart, state.SelectionEnd);
            int selEnd = Math.Max(state.SelectionStart, state.SelectionEnd);

            float selStartX = textDrawX + MeasureTextWidth(buffer.Slice(0, selStart), style.FontSize);
            float selEndX = textDrawX + MeasureTextWidth(buffer.Slice(0, selEnd), style.FontSize);
            DrawRect(selStartX, textRect.Y + 2, selEndX - selStartX, textRect.Height - 4, ImStyle.WithAlpha(style.Primary, 100));
        }

        var font = ctx.Font;
        if (font != null && length > 0)
        {
            var color = ImStyle.ToVector4(style.TextPrimary);
            DrawTextLocalToDrawList(CurrentViewport.CurrentDrawList, font, buffer.Slice(0, length), textDrawX, textY, style.FontSize, color);
        }

        // Draw caret
        if (isFocused && ctx.CaretVisible)
        {
            float caretX = textDrawX + MeasureTextWidth(buffer.Slice(0, state.CaretPos), style.FontSize);
            DrawRect(caretX, textRect.Y + 4, 1, textRect.Height - 8, style.TextPrimary);
        }

        Im.PopClipRect();

        // Draw dropdown arrow
        float arrowX = rect.Right - padding - arrowSize;
        float arrowY = rect.Center.Y;
        DrawDropdownArrow(arrowX, arrowY, arrowSize, isOpen, style.TextSecondary);

        // Refilter after editing so the popup reflects same-frame input.
        if (isOpen)
        {
            suggestionCount = Math.Min(suggestions.Length, _comboSuggestionsScratch.Length);
            for (int i = 0; i < suggestionCount; i++)
            {
                _comboSuggestionsScratch[i] = suggestions[i];
            }

            filterTextLength = Math.Min(length, _comboFilterTextScratch.Length);
            for (int i = 0; i < filterTextLength; i++)
            {
                _comboFilterTextScratch[i] = buffer[i];
            }

            filteredCount = 0;
            ReadOnlySpan<char> filterText = _comboFilterTextScratch.AsSpan(0, filterTextLength);
            if (filterTextLength == 0)
            {
                filteredCount = suggestionCount;
                for (int i = 0; i < filteredCount; i++)
                {
                    _comboFilteredIndicesScratch[i] = i;
                }
            }
            else
            {
                for (int i = 0; i < suggestionCount; i++)
                {
                    string suggestion = _comboSuggestionsScratch[i];
                    if (ImTextSearch.ContainsOrdinalIgnoreCase(suggestion, filterText))
                    {
                        _comboFilteredIndicesScratch[filteredCount++] = i;
                        if (filteredCount >= _comboFilteredIndicesScratch.Length)
                        {
                            break;
                        }
                    }
                }
            }

            if (filteredCount == 0)
            {
                _highlightedIndex = -1;
            }
            else if (_highlightedIndex >= filteredCount)
            {
                _highlightedIndex = filteredCount - 1;
            }
        }

        // Close popup if clicked outside (must happen BEFORE popup interaction)
        if (isOpen && ctx.Input.MousePressed && !rect.Contains(mousePos))
        {
            // Calculate popup bounds to check if click is inside popup
            float checkItemHeight = style.MinButtonHeight;
            float checkPopupHeight = filteredCount > 0 ? Math.Min(filteredCount * checkItemHeight, 200) : 0;
            var checkPopupRect = new ImRect(rect.X, rect.Bottom + 2, rect.Width, checkPopupHeight);

            bool clickedOutsidePopup = filteredCount == 0 ||
                ImPopover.ShouldClose(
                    openedFrame: _openComboFrame,
                    closeOnEscape: false,
                    closeOnOutsideButtons: ImPopoverCloseButtons.Left,
                    consumeCloseClick: false,
                    requireNoMouseOwner: false,
                    useViewportMouseCoordinates: false,
                    insideRect: checkPopupRect);

            if (clickedOutsidePopup)
            {
                _openComboId = 0;
                ctx.ClearFocus();
                isOpen = false;
            }
        }

        // Handle suggestions popup interaction and defer rendering
        if (isOpen && filteredCount > 0)
        {
            float itemHeight = style.MinButtonHeight;
            float contentHeight = filteredCount * itemHeight;
            float popupHeight = Math.Min(filteredCount * itemHeight, 200);
            var popupRect = new ImRect(rect.X, rect.Bottom + 2, rect.Width, popupHeight);

            if (isOpen)
            {
                using var popupOverlayScope = ImPopover.PushOverlayScopeLocal(popupRect);
                bool popupHovered = popupRect.Contains(mousePos);
                bool pressedInsidePopup = popupHovered && ctx.Input.MousePressed;
                bool releasedInsidePopup = popupHovered && ctx.Input.MouseReleased;

                // Handle scrolling (interaction - must happen now)
                if (popupHovered)
                {
                    float maxScroll = Math.Max(0, contentHeight - popupHeight);
                    float scrollDelta = ctx.Input.ScrollDelta;
                    _comboScrollY -= scrollDelta * 30f;
                    _comboScrollY = Math.Clamp(_comboScrollY, 0, maxScroll);
                    if (scrollDelta != 0f)
                    {
                        ctx.ConsumeScroll();
                    }
                }

                // Handle item clicks and track hovered state (interaction - must happen now)
                int hoveredItem = -1;
                float scrollbarWidth = style.ScrollbarWidth;
                bool hasScrollbar = contentHeight > popupHeight;
                float contentWidth = hasScrollbar ? popupRect.Width - scrollbarWidth : popupRect.Width;
                for (int i = 0; i < filteredCount; i++)
                {
                    float itemY = popupRect.Y + i * itemHeight - _comboScrollY;
                    if (itemY + itemHeight < popupRect.Y || itemY > popupRect.Bottom)
                        continue;

                    var itemRect = new ImRect(popupRect.X + 1, itemY, contentWidth - 2, itemHeight);
                    if (itemRect.Contains(mousePos) && popupHovered)
                    {
                        hoveredItem = i;
                        _highlightedIndex = i;
                        if (ctx.Input.MousePressed)
                        {
                            int selectedSuggestionIndex = _comboFilteredIndicesScratch[i];
                            SetText(buffer, ref length, maxLength, _comboSuggestionsScratch[selectedSuggestionIndex]);
                            state.CaretPos = length;
                            state.SelectionStart = -1;
                            state.SelectionEnd = -1;
                            _textInputStates[widgetId] = state;
                            _openComboId = 0;
                            ctx.ClearFocus();
                            changed = true;
                        }
                    }
                }

                if (pressedInsidePopup)
                {
                    ctx.ConsumeMouseLeftPress();
                }

                if (releasedInsidePopup)
                {
                    ctx.ConsumeMouseLeftRelease();
                }

                // Store state for deferred rendering (struct-based, no closures)
                _hasDeferredComboBox = true;
                var popupRectViewport = TransformRectLocalToViewportAabb(popupRect);
                _deferredComboBox = new DeferredComboBoxState
                {
                    PopupRect = popupRectViewport,
                    Suggestions = _comboSuggestionsScratch,
                    SuggestionCount = suggestionCount,
                    FilteredIndices = _comboFilteredIndicesScratch,
                    FilteredCount = filteredCount,
                    FilterText = _comboFilterTextScratch,
                    FilterTextLength = filterTextLength,
                    HoveredIndex = hoveredItem,
                    HighlightedIndex = _highlightedIndex,
                    ScrollY = _comboScrollY,
                    SortKey = CurrentViewport.CurrentDrawList.GetSortKey() + 1,
                    Viewport = CurrentViewport
                };
            }
        }

        return changed;
    }

    private static void DrawHighlightedText(string text, ReadOnlySpan<char> highlight, ImRect rect, ImStyle style)
    {
        var ctx = Context;
        var font = ctx.Font;
        if (font == null) return;

        float x = rect.X + style.Padding;
        float y = rect.Y + (rect.Height - style.FontSize) / 2f;
        var drawList = CurrentViewport.CurrentDrawList;

        if (highlight.Length == 0)
        {
            var color = ImStyle.ToVector4(style.TextPrimary);
            DrawTextLocalToDrawList(drawList, font, text.AsSpan(), x, y, style.FontSize, color);
            return;
        }

        int index = ImTextSearch.IndexOfOrdinalIgnoreCase(text, highlight);
        if (index < 0)
        {
            var color = ImStyle.ToVector4(style.TextPrimary);
            DrawTextLocalToDrawList(drawList, font, text.AsSpan(), x, y, style.FontSize, color);
            return;
        }

        // Draw text with highlighted portion
        ReadOnlySpan<char> before = text.AsSpan(0, index);
        ReadOnlySpan<char> match = text.AsSpan(index, highlight.Length);
        ReadOnlySpan<char> after = text.AsSpan(index + highlight.Length);

        float beforeWidth = MeasureTextWidth(before, style.FontSize);
        float matchWidth = MeasureTextWidth(match, style.FontSize);

        var primaryColor = ImStyle.ToVector4(style.TextPrimary);
        var highlightColor = ImStyle.ToVector4(style.Primary);

        if (before.Length > 0)
        {
            DrawTextLocalToDrawList(drawList, font, before, x, y, style.FontSize, primaryColor);
        }

        DrawTextLocalToDrawList(drawList, font, match, x + beforeWidth, y, style.FontSize, highlightColor);

        if (after.Length > 0)
        {
            DrawTextLocalToDrawList(drawList, font, after, x + beforeWidth + matchWidth, y, style.FontSize, primaryColor);
        }
    }

    private static void SetText(Span<char> buffer, ref int length, int maxLength, string text)
    {
        length = Math.Min(text.Length, maxLength);
        for (int i = 0; i < length; i++)
            buffer[i] = text[i];
    }

    /// <summary>
    /// Close any open combo box.
    /// </summary>
    public static void CloseAllCombos()
    {
        _openComboId = 0;
        _openComboFrame = 0;
    }

    /// <summary>
    /// Draw a panel/window background.
    /// </summary>
    public static void Panel(float x, float y, float w, float h)
    {
        EnsureInFrame();
        var style = Context.Style;

        // Background with shadow (through draw list for correct z-ordering)
        var bgColor = ImStyle.ToVector4(style.Background);
        var shadowColor = ImStyle.ToVector4(style.ShadowColor);

        AddRoundedRectWithShadowLocal(
            x, y, w, h,
            style.CornerRadius,
            bgColor,
            style.ShadowOffsetX,
            style.ShadowOffsetY,
            style.ShadowRadius,
            shadowColor);

        // Border
        DrawRoundedRectStroke(x, y, w, h, style.CornerRadius, style.Border, style.BorderWidth);
    }

    internal static void AddRoundedRectWithShadowLocal(
        float x,
        float y,
        float width,
        float height,
        float radius,
        Vector4 color,
        float shadowOffsetX,
        float shadowOffsetY,
        float shadowRadius,
        Vector4 shadowColor,
        float rotation = 0f)
    {
        TransformRectForDraw(
            x,
            y,
            width,
            height,
            rotation,
            out float viewportX,
            out float viewportY,
            out float viewportWidth,
            out float viewportHeight,
            out float viewportRotation,
            out float uniformScale);

        CurrentViewport.CurrentDrawList.AddRoundedRectWithShadow(
            viewportX * _scale,
            viewportY * _scale,
            viewportWidth * _scale,
            viewportHeight * _scale,
            radius * uniformScale * _scale,
            color,
            shadowOffsetX * uniformScale * _scale,
            shadowOffsetY * uniformScale * _scale,
            shadowRadius * uniformScale * _scale,
            shadowColor,
            viewportRotation);
    }

    //=== Primitive Drawing (scaled internally) ===

    /// <summary>
    /// Draw a filled circle.
    /// </summary>
    public static void DrawCircle(float cx, float cy, float radius, uint color)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        var pos = S(TransformPointLocalToViewport(new Vector2(cx, cy)));
        float uniformScale = GetUniformTransformScale();
        CurrentViewport.CurrentDrawList.AddCircle(pos.X, pos.Y, radius * uniformScale * _scale, colorVec);
    }

    public static void DrawCircle(float cx, float cy, float radius, uint color, float rotation)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        var pos = S(TransformPointLocalToViewport(new Vector2(cx, cy)));
        float uniformScale = GetUniformTransformScale();
        float viewportRotation = rotation + GetTransformRotationRadians();
        CurrentViewport.CurrentDrawList.AddCircle(pos.X, pos.Y, radius * uniformScale * _scale, colorVec, viewportRotation);
    }

    public static void DrawCircleWithGlow(float cx, float cy, float radius, uint color, float glowRadius)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        var pos = S(TransformPointLocalToViewport(new Vector2(cx, cy)));
        float uniformScale = GetUniformTransformScale();
        CurrentViewport.CurrentDrawList.AddCircleWithGlow(
            pos.X,
            pos.Y,
            radius * uniformScale * _scale,
            colorVec,
            glowRadius * uniformScale * _scale);
    }

    public static void DrawCircleWithGlow(float cx, float cy, float radius, uint color, float glowRadius, float rotation)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        var pos = S(TransformPointLocalToViewport(new Vector2(cx, cy)));
        float uniformScale = GetUniformTransformScale();
        float viewportRotation = rotation + GetTransformRotationRadians();
        CurrentViewport.CurrentDrawList.AddCircleWithGlow(
            pos.X,
            pos.Y,
            radius * uniformScale * _scale,
            colorVec,
            glowRadius * uniformScale * _scale,
            viewportRotation);
    }

    /// <summary>
    /// Draw a circle outline (stroke).
    /// </summary>
    public static void DrawCircleStroke(float cx, float cy, float radius, uint color, float strokeWidth)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        var pos = S(TransformPointLocalToViewport(new Vector2(cx, cy)));
        float uniformScale = GetUniformTransformScale();
        CurrentViewport.CurrentDrawList.AddCircleStroke(
            pos.X,
            pos.Y,
            radius * uniformScale * _scale,
            colorVec,
            strokeWidth * uniformScale * _scale);
    }

    /// <summary>
    /// Draw a circle outline (stroke) with rotation.
    /// </summary>
    public static void DrawCircleStroke(float cx, float cy, float radius, uint color, float strokeWidth, float rotation)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        var pos = S(TransformPointLocalToViewport(new Vector2(cx, cy)));
        float uniformScale = GetUniformTransformScale();
        float viewportRotation = rotation + GetTransformRotationRadians();
        CurrentViewport.CurrentDrawList.AddCircleStroke(
            pos.X,
            pos.Y,
            radius * uniformScale * _scale,
            colorVec,
            strokeWidth * uniformScale * _scale,
            viewportRotation);
    }

    /// <summary>
    /// Draw a filled rectangle.
    /// </summary>
    public static void DrawRect(float x, float y, float w, float h, uint color)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation: 0f,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out _);
        CurrentViewport.CurrentDrawList.AddRect(viewportX * _scale, viewportY * _scale, viewportW * _scale, viewportH * _scale, colorVec, viewportRotation);
    }

    public static void DrawRect(float x, float y, float w, float h, uint color, float rotation)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out _);
        CurrentViewport.CurrentDrawList.AddRect(viewportX * _scale, viewportY * _scale, viewportW * _scale, viewportH * _scale, colorVec, viewportRotation);
    }

    public static void DrawRectWithGlow(float x, float y, float w, float h, uint color, float glowRadius)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation: 0f,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);
        CurrentViewport.CurrentDrawList.AddRectWithGlow(
            viewportX * _scale,
            viewportY * _scale,
            viewportW * _scale,
            viewportH * _scale,
            colorVec,
            glowRadius * uniformScale * _scale,
            viewportRotation);
    }

    public static void DrawRectWithGlow(float x, float y, float w, float h, uint color, float glowRadius, float rotation)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);
        CurrentViewport.CurrentDrawList.AddRectWithGlow(
            viewportX * _scale,
            viewportY * _scale,
            viewportW * _scale,
            viewportH * _scale,
            colorVec,
            glowRadius * uniformScale * _scale,
            viewportRotation);
    }

    public static void DrawRectWithShadow(float x, float y, float w, float h, uint color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, uint shadowColor)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        var shadowColorVec = ImStyle.ToVector4(shadowColor);
        AddRoundedRectWithShadowLocal(x, y, w, h, radius: 0f, colorVec,
            shadowOffsetX, shadowOffsetY, shadowRadius, shadowColorVec);
    }

    public static void DrawRectWithShadow(float x, float y, float w, float h, uint color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, uint shadowColor, float rotation)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        var shadowColorVec = ImStyle.ToVector4(shadowColor);
        AddRoundedRectWithShadowLocal(x, y, w, h, radius: 0f, colorVec,
            shadowOffsetX, shadowOffsetY, shadowRadius, shadowColorVec, rotation);
    }

    public static void DrawRectWithShadowAndGlow(float x, float y, float w, float h, uint color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, uint shadowColor, float glowRadius)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        var shadowColorVec = ImStyle.ToVector4(shadowColor);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation: 0f,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);

        CurrentViewport.CurrentDrawList.AddRoundedRectWithShadowAndGlow(
            viewportX * _scale,
            viewportY * _scale,
            viewportW * _scale,
            viewportH * _scale,
            radius: 0f,
            colorVec,
            shadowOffsetX * uniformScale * _scale,
            shadowOffsetY * uniformScale * _scale,
            shadowRadius * uniformScale * _scale,
            shadowColorVec,
            glowRadius * uniformScale * _scale,
            viewportRotation);
    }

    public static void DrawRectWithShadowAndGlow(float x, float y, float w, float h, uint color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, uint shadowColor, float glowRadius, float rotation)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        var shadowColorVec = ImStyle.ToVector4(shadowColor);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);

        CurrentViewport.CurrentDrawList.AddRoundedRectWithShadowAndGlow(
            viewportX * _scale,
            viewportY * _scale,
            viewportW * _scale,
            viewportH * _scale,
            radius: 0f,
            colorVec,
            shadowOffsetX * uniformScale * _scale,
            shadowOffsetY * uniformScale * _scale,
            shadowRadius * uniformScale * _scale,
            shadowColorVec,
            glowRadius * uniformScale * _scale,
            viewportRotation);
    }

    /// <summary>
    /// Draw a filled rounded rectangle.
    /// </summary>
    public static void DrawRoundedRect(float x, float y, float w, float h, float radius, uint color)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation: 0f,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);
        CurrentViewport.CurrentDrawList.AddRoundedRect(
            viewportX * _scale,
            viewportY * _scale,
            viewportW * _scale,
            viewportH * _scale,
            radius * uniformScale * _scale,
            colorVec,
            viewportRotation);
    }

    public static void DrawRoundedRect(float x, float y, float w, float h, float radius, uint color, float rotation)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);
        CurrentViewport.CurrentDrawList.AddRoundedRect(
            viewportX * _scale,
            viewportY * _scale,
            viewportW * _scale,
            viewportH * _scale,
            radius * uniformScale * _scale,
            colorVec,
            viewportRotation);
    }

    public static void DrawRoundedRectGradientStops(float x, float y, float w, float h, float radius, ReadOnlySpan<SdfGradientStop> stops, float rotation = 0f)
    {
        EnsureInFrame();
        if (stops.Length <= 0)
        {
            return;
        }

        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);
        CurrentViewport.CurrentDrawList.AddGradientStopsRoundedRect(
            viewportX * _scale,
            viewportY * _scale,
            viewportW * _scale,
            viewportH * _scale,
            radius * uniformScale * _scale,
            stops,
            viewportRotation);
    }

    /// <summary>
    /// Draw a rounded rectangle stroke (outline only).
    /// </summary>
    public static void DrawRoundedRectStroke(float x, float y, float w, float h, float radius, uint color, float strokeWidth)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation: 0f,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);
        CurrentViewport.CurrentDrawList.AddRoundedRectStroke(
            viewportX * _scale,
            viewportY * _scale,
            viewportW * _scale,
            viewportH * _scale,
            radius * uniformScale * _scale,
            colorVec,
            strokeWidth * uniformScale * _scale,
            viewportRotation);
    }

    public static void DrawRoundedRectStroke(float x, float y, float w, float h, float radius, uint color, float strokeWidth, float rotation)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);
        CurrentViewport.CurrentDrawList.AddRoundedRectStroke(
            viewportX * _scale,
            viewportY * _scale,
            viewportW * _scale,
            viewportH * _scale,
            radius * uniformScale * _scale,
            colorVec,
            strokeWidth * uniformScale * _scale,
            viewportRotation);
    }

    /// <summary>
    /// Draw a filled rounded rectangle with per-corner radii.
    /// </summary>
    /// <param name="radiusTL">Top-left corner radius.</param>
    /// <param name="radiusTR">Top-right corner radius.</param>
    /// <param name="radiusBR">Bottom-right corner radius.</param>
    /// <param name="radiusBL">Bottom-left corner radius.</param>
    public static void DrawRoundedRectPerCorner(float x, float y, float w, float h,
        float radiusTL, float radiusTR, float radiusBR, float radiusBL, uint color)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation: 0f,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);
        CurrentViewport.CurrentDrawList.AddRoundedRectPerCorner(
            viewportX * _scale,
            viewportY * _scale,
            viewportW * _scale,
            viewportH * _scale,
            radiusTL * uniformScale * _scale,
            radiusTR * uniformScale * _scale,
            radiusBR * uniformScale * _scale,
            radiusBL * uniformScale * _scale,
            colorVec,
            viewportRotation);
    }

    public static void DrawRoundedRectPerCorner(float x, float y, float w, float h,
        float radiusTL, float radiusTR, float radiusBR, float radiusBL, uint color, float rotation)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);
        CurrentViewport.CurrentDrawList.AddRoundedRectPerCorner(
            viewportX * _scale, viewportY * _scale, viewportW * _scale, viewportH * _scale,
            radiusTL * uniformScale * _scale, radiusTR * uniformScale * _scale, radiusBR * uniformScale * _scale, radiusBL * uniformScale * _scale,
            colorVec,
            viewportRotation);
    }

    /// <summary>
    /// Draw a rounded rectangle stroke with per-corner radii.
    /// </summary>
    public static void DrawRoundedRectPerCornerStroke(float x, float y, float w, float h,
        float radiusTL, float radiusTR, float radiusBR, float radiusBL, uint color, float strokeWidth)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation: 0f,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);
        CurrentViewport.CurrentDrawList.AddRoundedRectPerCornerStroke(
            viewportX * _scale, viewportY * _scale, viewportW * _scale, viewportH * _scale,
            radiusTL * uniformScale * _scale, radiusTR * uniformScale * _scale, radiusBR * uniformScale * _scale, radiusBL * uniformScale * _scale,
            colorVec, strokeWidth * uniformScale * _scale, viewportRotation);
    }

    public static void DrawRoundedRectPerCornerStroke(float x, float y, float w, float h,
        float radiusTL, float radiusTR, float radiusBR, float radiusBL, uint color, float strokeWidth, float rotation)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out float uniformScale);
        CurrentViewport.CurrentDrawList.AddRoundedRectPerCornerStroke(
            viewportX * _scale, viewportY * _scale, viewportW * _scale, viewportH * _scale,
            radiusTL * uniformScale * _scale, radiusTR * uniformScale * _scale, radiusBR * uniformScale * _scale, radiusBL * uniformScale * _scale,
            colorVec, strokeWidth * uniformScale * _scale, viewportRotation);
    }

    /// <summary>
    /// Draw a saturation-value gradient rectangle for color pickers.
    /// X-axis is saturation (0 to 1), Y-axis is value (1 to 0, top to bottom).
    /// </summary>
    /// <param name="hueColor">The hue color at full saturation and value.</param>
    public static void DrawSVRect(float x, float y, float w, float h, uint hueColor)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(hueColor);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation: 0f,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out _,
            out _);
        CurrentViewport.CurrentDrawList.AddSVRect(viewportX * _scale, viewportY * _scale, viewportW * _scale, viewportH * _scale, colorVec);
    }

    /// <summary>
    /// Draw a line between two points.
    /// </summary>
    public static void DrawLine(float x1, float y1, float x2, float y2, float thickness, uint color)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(color);
        var p1 = S(TransformPointLocalToViewport(new Vector2(x1, y1)));
        var p2 = S(TransformPointLocalToViewport(new Vector2(x2, y2)));
        CurrentViewport.CurrentDrawList.AddLine(p1.X, p1.Y, p2.X, p2.Y, thickness * GetUniformTransformScale() * _scale, colorVec);
    }

    /// <summary>
    /// Draw a textured image in window-local coordinates.
    /// </summary>
    public static void DrawImage(Texture texture, float x, float y, float width, float height)
    {
        DrawImage(texture, x, y, width, height, new Rectangle(0f, 0f, texture.Width, texture.Height), 0xFFFFFFFF, rotation: 0f);
    }

    /// <summary>
    /// Draw a textured image in window-local coordinates with explicit rotation (radians).
    /// Rotation is around the image center.
    /// </summary>
    public static void DrawImage(Texture texture, float x, float y, float width, float height, float rotation)
    {
        DrawImage(texture, x, y, width, height, new Rectangle(0f, 0f, texture.Width, texture.Height), 0xFFFFFFFF, rotation);
    }

    /// <summary>
    /// Draw a textured image with source rectangle and tint in window-local coordinates.
    /// </summary>
    public static void DrawImage(Texture texture, float x, float y, float width, float height, Rectangle source, uint tint)
    {
        DrawImage(texture, x, y, width, height, source, tint, rotation: 0f);
    }

    /// <summary>
    /// Draw a textured image with source rectangle, tint and explicit rotation (radians) in window-local coordinates.
    /// Rotation is around the image center.
    /// </summary>
    public static void DrawImage(Texture texture, float x, float y, float width, float height, Rectangle source, uint tint, float rotation)
    {
        EnsureInFrame();
        if (texture.Width <= 0 || texture.Height <= 0)
        {
            return;
        }

        float invTextureWidth = 1f / texture.Width;
        float invTextureHeight = 1f / texture.Height;
        float u0 = source.X * invTextureWidth;
        float v0 = source.Y * invTextureHeight;
        float u1 = (source.X + source.Width) * invTextureWidth;
        float v1 = (source.Y + source.Height) * invTextureHeight;

        TransformRectForDraw(
            x,
            y,
            width,
            height,
            rotation,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out float viewportRotation,
            out _);
        Vector4 tintVector = ImStyle.ToVector4(tint);
        CurrentViewport.CurrentDrawList.AddImage(
            viewportX * _scale,
            viewportY * _scale,
            viewportW * _scale,
            viewportH * _scale,
            u0,
            v0,
            u1,
            v1,
            tintVector,
            texture.Index,
            viewportRotation);
    }

    /// <summary>
    /// Draw a connected polyline through all points.
    /// </summary>
    public static void DrawPolyline(ReadOnlySpan<Vector2> points, float thickness, uint color)
    {
        EnsureInFrame();
        if (points.Length < 2)
        {
            return;
        }

        var colorVec = ImStyle.ToVector4(color);
        Span<Vector2> scaledPoints = stackalloc Vector2[points.Length];
        for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
        {
            Vector2 transformedPoint = TransformPointLocalToViewport(points[pointIndex]);
            scaledPoints[pointIndex] = new Vector2(transformedPoint.X * _scale, transformedPoint.Y * _scale);
        }

        CurrentViewport.CurrentDrawList.AddPolyline(scaledPoints, thickness * GetUniformTransformScale() * _scale, colorVec);
    }

    //=== Boolean Groups (SDF) ===

    /// <summary>
    /// Begin an SDF boolean group. Shapes drawn until EndBooleanGroup() are combined and rendered at the end marker.
    /// </summary>
    public static void BeginBooleanGroup(SdfBooleanOp op, float smoothness = 10f)
    {
        EnsureInFrame();
        CurrentViewport.CurrentDrawList.AddGroupBegin(op, smoothness);
    }

    /// <summary>
    /// End the current SDF boolean group and render the combined result using the given fill color.
    /// </summary>
    public static void EndBooleanGroup(uint fillColor)
    {
        EnsureInFrame();
        var colorVec = ImStyle.ToVector4(fillColor);
        CurrentViewport.CurrentDrawList.AddGroupEnd(colorVec);
    }

    /// <summary>
    /// Draw a profiler-style graph from values array.
    /// </summary>
    /// <param name="values">Graph values (oldest to newest).</param>
    /// <param name="x">Left edge X position (window-local).</param>
    /// <param name="y">Top edge Y position (window-local).</param>
    /// <param name="w">Graph width.</param>
    /// <param name="h">Graph height.</param>
    /// <param name="minValue">Minimum value for Y-axis scaling.</param>
    /// <param name="maxValue">Maximum value for Y-axis scaling.</param>
    /// <param name="thickness">Line thickness.</param>
    /// <param name="color">Line color (ABGR uint).</param>
    public static void DrawGraph(ReadOnlySpan<float> values, float x, float y, float w, float h,
        float minValue, float maxValue, float thickness, uint color)
    {
        EnsureInFrame();
        if (values.Length < 2) return;

        var colorVec = ImStyle.ToVector4(color);
        TransformRectForDraw(
            x,
            y,
            w,
            h,
            rotation: 0f,
            out float viewportX,
            out float viewportY,
            out float viewportW,
            out float viewportH,
            out _,
            out _);

        CurrentViewport.CurrentDrawList.AddGraph(values,
            viewportX * _scale, viewportY * _scale,
            viewportW * _scale, viewportH * _scale,
            minValue, maxValue, colorVec, thickness * GetUniformTransformScale() * _scale);
    }

    /// <summary>
    /// Draw a filled polygon from points (window-local coordinates).
    /// Useful for graph fills under curves.
    /// </summary>
    public static void DrawFilledPolygon(ReadOnlySpan<Vector2> points, uint color)
    {
        EnsureInFrame();
        if (points.Length < 3) return;

        var colorVec = ImStyle.ToVector4(color);

        // Convert to viewport coordinates and scale
        Span<Vector2> scaledPoints = stackalloc Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 transformedPoint = TransformPointLocalToViewport(points[i]);
            scaledPoints[i] = new Vector2(transformedPoint.X * _scale, transformedPoint.Y * _scale);
        }

        CurrentViewport.CurrentDrawList.AddFilledPolygon(scaledPoints, colorVec);
    }

    /// <summary>
    /// Draw text at a specific position with custom font size.
    /// For general labels, prefer Label() which uses layout.
    /// </summary>
    public static void Text(ReadOnlySpan<char> text, float x, float y, float fontSize, uint color)
    {
        EnsureInFrame();
        var font = Context.Font;
        if (font == null) return;

        var colorVec = ImStyle.ToVector4(color);
        DrawTextLocalToDrawList(CurrentViewport.CurrentDrawList, font, text, x, y, fontSize, colorVec);
    }

    /// <summary>
    /// Draw text using viewport-local coordinates (no window-local conversion).
    /// Use this for overlays/widgets that already compute screen/viewport coordinates.
    /// </summary>
    public static void TextViewport(ReadOnlySpan<char> text, float x, float y, float fontSize, uint color)
    {
        EnsureInFrame();
        var font = Context.Font;
        if (font == null) return;

        var colorVec = ImStyle.ToVector4(color);
        DrawTextToDrawList(CurrentViewport.CurrentDrawList, font, text,
            x * _scale, y * _scale, fontSize * _scale, colorVec);
    }

    /// <summary>
    /// Draw text using viewport-local coordinates (default font size).
    /// </summary>
    public static void TextViewport(ReadOnlySpan<char> text, float x, float y, uint color)
    {
        TextViewport(text, x, y, Context.Style.FontSize, color);
    }

    //=== Dear ImGui-style Menu API (Main Menu Bar) ===

    public static bool BeginMainMenuBar()
    {
        return ImMainMenuBar.Begin();
    }

    public static void EndMainMenuBar()
    {
        ImMainMenuBar.End();
    }

    public static bool BeginMenu(string label)
    {
        return ImMainMenuBar.BeginMenu(label);
    }

    public static void EndMenu()
    {
        ImMainMenuBar.EndMenu();
    }

    public static bool MenuItem(string label, string shortcut = "", bool enabled = true)
    {
        return ImMainMenuBar.MenuItem(label, shortcut, enabled);
    }

    public static bool MenuItem(string label, string shortcut, ref bool selected, bool enabled = true)
    {
        return ImMainMenuBar.MenuItem(label, shortcut, ref selected, enabled);
    }

    public static void Separator()
    {
        ImMainMenuBar.Separator();
    }

    /// <summary>
    /// Render text glyphs to a draw list (no text effects support).
    /// </summary>
    private static void DrawTextLocalToDrawList(
        Rendering.ImDrawList drawList,
        Font font,
        ReadOnlySpan<char> text,
        float x,
        float y,
        float fontSize,
        Vector4 color)
    {
        float scaleX = GetTransformScaleX();
        float scaleY = GetTransformScaleY();
        float transformRotation = GetTransformRotationRadians();
        bool hasRotation = MathF.Abs(transformRotation) > 0.0001f;
        bool isUniformScale = MathF.Abs(scaleX - scaleY) <= 0.0001f;
        if (!hasRotation && isUniformScale)
        {
            Vector2 viewportPosition = TransformPointLocalToViewport(new Vector2(x, y));
            float scaledFontSize = fontSize * scaleX;
            DrawTextToDrawList(
                drawList,
                font,
                text,
                viewportPosition.X * _scale,
                viewportPosition.Y * _scale,
                scaledFontSize * _scale,
                color);
            return;
        }

        DrawTextToDrawListTransformed(drawList, font, text, x, y, fontSize, color);
    }

    private static void DrawTextToDrawList(
        Rendering.ImDrawList drawList,
        Font font,
        ReadOnlySpan<char> text,
        float x,
        float y,
        float fontSize,
        Vector4 color)
    {
        if (text.Length == 0) return;

        var secondaryFont = Context.SecondaryFont;
        float primaryScale = fontSize / font.BaseSizePixels;
        float secondaryScale = secondaryFont == null ? 0f : fontSize / secondaryFont.BaseSizePixels;
        float cursorX = x;
        float baselineY = y + font.AscentPixels * primaryScale;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            float scale;
            int atlasIndex;
            bool useSecondaryAtlas;
            FontGlyph glyph;
            bool preferSecondary = PreferSecondaryFontForCodepoint(c);

            if (preferSecondary &&
                secondaryFont != null &&
                secondaryFont.TryGetGlyph(c, out var secondaryGlyph))
            {
                glyph = secondaryGlyph;
                scale = secondaryScale;
                atlasIndex = 0;
                useSecondaryAtlas = true;
            }
            else if (font.TryGetGlyph(c, out var primaryGlyph))
            {
                glyph = primaryGlyph;
                scale = primaryScale;
                atlasIndex = font.Atlas.Index;
                useSecondaryAtlas = false;
            }
            else if (secondaryFont != null && secondaryFont.TryGetGlyph(c, out var fallbackSecondaryGlyph))
            {
                glyph = fallbackSecondaryGlyph;
                scale = secondaryScale;
                atlasIndex = 0;
                useSecondaryAtlas = true;
            }
            else
            {
                if (font.TryGetGlyph(' ', out var spaceGlyph))
                {
                    cursorX += spaceGlyph.AdvanceX * primaryScale;
                }
                else if (secondaryFont != null && secondaryFont.TryGetGlyph(' ', out spaceGlyph))
                {
                    cursorX += spaceGlyph.AdvanceX * secondaryScale;
                }
                continue;
            }

            float glyphX = cursorX + glyph.OffsetX * scale;
            float glyphY = baselineY + glyph.OffsetY * scale;
            float glyphW = glyph.Width * scale;
            float glyphH = glyph.Height * scale;

            if (glyph.Width > 0 && glyph.Height > 0)
            {
                if (useSecondaryAtlas)
                {
                    drawList.AddGlyph(glyphX, glyphY, glyphW, glyphH, glyph.U0, glyph.V0, glyph.U1, glyph.V1, color, secondaryFontAtlas: true);
                }
                else
                {
                    drawList.AddGlyph(glyphX, glyphY, glyphW, glyphH, glyph.U0, glyph.V0, glyph.U1, glyph.V1, color, atlasIndex);
                }
            }

            cursorX += glyph.AdvanceX * scale;
        }
    }

    private static void DrawTextToDrawListTransformed(
        Rendering.ImDrawList drawList,
        Font font,
        ReadOnlySpan<char> text,
        float x,
        float y,
        float fontSize,
        Vector4 color)
    {
        if (text.Length == 0)
        {
            return;
        }

        var secondaryFont = Context.SecondaryFont;
        float primaryScale = fontSize / font.BaseSizePixels;
        float secondaryScale = secondaryFont == null ? 0f : fontSize / secondaryFont.BaseSizePixels;
        float cursorX = x;
        float baselineY = y + font.AscentPixels * primaryScale;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            float scale;
            int atlasIndex;
            bool useSecondaryAtlas;
            FontGlyph glyph;
            bool preferSecondary = PreferSecondaryFontForCodepoint(c);

            if (preferSecondary &&
                secondaryFont != null &&
                secondaryFont.TryGetGlyph(c, out var secondaryGlyph))
            {
                glyph = secondaryGlyph;
                scale = secondaryScale;
                atlasIndex = 0;
                useSecondaryAtlas = true;
            }
            else if (font.TryGetGlyph(c, out var primaryGlyph))
            {
                glyph = primaryGlyph;
                scale = primaryScale;
                atlasIndex = font.Atlas.Index;
                useSecondaryAtlas = false;
            }
            else if (secondaryFont != null && secondaryFont.TryGetGlyph(c, out var fallbackSecondaryGlyph))
            {
                glyph = fallbackSecondaryGlyph;
                scale = secondaryScale;
                atlasIndex = 0;
                useSecondaryAtlas = true;
            }
            else
            {
                if (font.TryGetGlyph(' ', out var spaceGlyph))
                {
                    cursorX += spaceGlyph.AdvanceX * primaryScale;
                }
                else if (secondaryFont != null && secondaryFont.TryGetGlyph(' ', out spaceGlyph))
                {
                    cursorX += spaceGlyph.AdvanceX * secondaryScale;
                }

                continue;
            }

            float glyphX = cursorX + glyph.OffsetX * scale;
            float glyphY = baselineY + glyph.OffsetY * scale;
            float glyphW = glyph.Width * scale;
            float glyphH = glyph.Height * scale;

            if (glyph.Width > 0 && glyph.Height > 0)
            {
                TransformRectForDraw(
                    glyphX,
                    glyphY,
                    glyphW,
                    glyphH,
                    rotation: 0f,
                    out float viewportX,
                    out float viewportY,
                    out float viewportW,
                    out float viewportH,
                    out float viewportRotation,
                    out _);

                drawList.AddGlyph(
                    viewportX * _scale,
                    viewportY * _scale,
                    viewportW * _scale,
                    viewportH * _scale,
                    glyph.U0,
                    glyph.V0,
                    glyph.U1,
                    glyph.V1,
                    color,
                    useSecondaryAtlas ? Rendering.ImDrawList.SecondaryFontAtlasSentinel : atlasIndex,
                    viewportRotation);
            }

            cursorX += glyph.AdvanceX * scale;
        }
    }

    private static bool PreferSecondaryFontForCodepoint(char codepoint)
    {
        return codepoint >= '\ue000' && codepoint <= '\uf8ff';
    }

    /// <summary>
    /// Draw formatted text at a specific position with custom font size (allocation-free).
    /// </summary>
    public static void Text(ImFormatHandler handler, float x, float y, float fontSize, uint color)
    {
        Text(handler.Text, x, y, fontSize, color);
    }

    /// <summary>
    /// Draw text at a specific position using default font size.
    /// </summary>
    public static void Text(ReadOnlySpan<char> text, float x, float y, uint color)
    {
        Text(text, x, y, Context.Style.FontSize, color);
    }

    /// <summary>
    /// Draw formatted text at a specific position using default font size (allocation-free).
    /// </summary>
    public static void Text(ImFormatHandler handler, float x, float y, uint color)
    {
        Text(handler.Text, x, y, Context.Style.FontSize, color);
    }

    internal static void LabelTextViewport(string text, float x, float y)
    {
        var style = Context.Style;
        var font = Context.Font;

        if (font != null)
        {
            var color = ImStyle.ToVector4(style.TextPrimary);
            DrawTextLocalToDrawList(CurrentViewport.CurrentDrawList, font, text.AsSpan(), x, y, style.FontSize, color);
        }
        else
        {
            // Fallback: draw a bar representing text length
            float charWidth = 7f;
            float charHeight = style.FontSize;
            int len = Math.Min(text.Length, 60);
            float w = len * charWidth;
            DrawRoundedRect(x, y, w, charHeight, 2f, ImStyle.WithAlphaF(style.TextPrimary, 0.3f));
        }
    }

    //=== Input Helpers ===

    /// <summary>
    /// Get mouse position in viewport-local logical coordinates.
    /// </summary>
    public static Vector2 MousePosViewport
    {
        get
        {
            EnsureInFrame();
            return Context.Input.MousePos;
        }
    }

    /// <summary>
    /// Get mouse position in the current local coordinate space (inverse of current transform).
    /// </summary>
    public static Vector2 MousePos
    {
        get
        {
            EnsureInFrame();
            return TransformPointViewportToLocal(Context.Input.MousePos);
        }
    }

    public static Vector2 WindowMousePos
    {
        get
        {
            EnsureInFrame();
            return TransformPointViewportToLocal(Context.Input.MousePos);
        }
    }

    /// <summary>
    /// Returns true if mouse button is currently held.
    /// </summary>
    public static bool MouseDown
    {
        get
        {
            EnsureInFrame();
            return Context.Input.MouseDown;
        }
    }

    /// <summary>
    /// Returns true if mouse button was pressed this frame.
    /// </summary>
    public static bool MousePressed
    {
        get
        {
            EnsureInFrame();
            return Context.Input.MousePressed;
        }
    }

    /// <summary>
    /// Get global mouse position in screen coordinates (for multi-viewport).
    /// Returns Vector2.Zero if global input not available.
    /// </summary>
    public static Vector2 GlobalMousePosition => Context.GlobalMousePosition;

    /// <summary>
    /// Set the desired cursor for this frame. Useful for widgets that need specific cursors
    /// (e.g., resize, text select, drag).
    /// </summary>
    public static void SetCursor(StandardCursor cursor)
    {
        WindowManager.DesiredCursor = cursor;
    }

    public static Vector2 TransformPointLocalToViewport(Vector2 localPoint)
    {
        return Vector2.Transform(localPoint, _currentTransform);
    }

    public static Vector2 TransformPointViewportToLocal(Vector2 viewportPoint)
    {
        return Vector2.Transform(viewportPoint, _currentInverseTransform);
    }

    public static ImRect TransformRectLocalToViewportAabb(ImRect localRect)
    {
        Vector2 topLeft = TransformPointLocalToViewport(new Vector2(localRect.X, localRect.Y));
        Vector2 topRight = TransformPointLocalToViewport(new Vector2(localRect.Right, localRect.Y));
        Vector2 bottomLeft = TransformPointLocalToViewport(new Vector2(localRect.X, localRect.Bottom));
        Vector2 bottomRight = TransformPointLocalToViewport(new Vector2(localRect.Right, localRect.Bottom));

        float minX = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomLeft.X, bottomRight.X));
        float minY = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomLeft.Y, bottomRight.Y));
        float maxX = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomLeft.X, bottomRight.X));
        float maxY = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomLeft.Y, bottomRight.Y));

        return new ImRect(minX, minY, maxX - minX, maxY - minY);
    }

    internal static void TransformRectForDraw(
        float x,
        float y,
        float width,
        float height,
        float rotation,
        out float viewportX,
        out float viewportY,
        out float viewportWidth,
        out float viewportHeight,
        out float viewportRotation,
        out float uniformScale)
    {
        Vector2 originViewport = TransformPointLocalToViewport(new Vector2(x, y));
        viewportX = originViewport.X;
        viewportY = originViewport.Y;

        float scaleX = GetTransformScaleX();
        float scaleY = GetTransformScaleY();
        uniformScale = (scaleX + scaleY) * 0.5f;
        if (!float.IsFinite(uniformScale) || uniformScale <= 0f)
        {
            uniformScale = 1f;
        }

        viewportWidth = width * scaleX;
        viewportHeight = height * scaleY;
        viewportRotation = rotation + GetTransformRotationRadians();
    }

    internal static float GetTransformScaleX()
    {
        return Vector2.TransformNormal(Vector2.UnitX, _currentTransform).Length();
    }

    internal static float GetTransformScaleY()
    {
        return Vector2.TransformNormal(Vector2.UnitY, _currentTransform).Length();
    }

    internal static float GetUniformTransformScale()
    {
        float scaleX = GetTransformScaleX();
        float scaleY = GetTransformScaleY();
        float scale = (scaleX + scaleY) * 0.5f;
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            return 1f;
        }

        return scale;
    }

    internal static float GetTransformRotationRadians()
    {
        Vector2 axisX = Vector2.TransformNormal(Vector2.UnitX, _currentTransform);
        if (axisX.LengthSquared() <= 1e-8f)
        {
            return 0f;
        }

        return MathF.Atan2(axisX.Y, axisX.X);
    }

    //=== Internal Helpers ===

    private static void EnsureInFrame()
    {
        if (!_inFrame)
            throw new InvalidOperationException("Not in Im frame. Call Im.Begin() first.");
    }

    private static ImViewport? FindViewportByResourcesId(ImContext ctx, int resourcesId)
    {
        for (int viewportIndex = 0; viewportIndex < ctx.Viewports.Count; viewportIndex++)
        {
            var viewport = ctx.Viewports[viewportIndex];
            if (viewport.ResourcesId == resourcesId)
            {
                return viewport;
            }
        }

        return null;
    }

    internal static void SetCurrentViewportForInternalDraw(ImViewport viewport)
    {
        EnsureInFrame();
        Context.SetCurrentViewport(viewport);
        _scale = viewport.ContentScale;
    }

    internal static void RestoreViewportForInternalDraw(ImViewport? viewport)
    {
        EnsureInFrame();
        if (viewport != null)
        {
            Context.SetCurrentViewport(viewport);
            _scale = viewport.ContentScale;
        }
    }

    private static ImViewport? FindViewportContainingScreenPos(Vector2 screenPos)
    {
        // Check secondary viewports first (they render on top)
        for (int i = Context.Viewports.Count - 1; i >= 0; i--)
        {
            var viewport = Context.Viewports[i];
            if (viewport.ScreenBounds.Contains(screenPos))
            {
                return viewport;
            }
        }

        // Fall back to primary viewport
        return Context.PrimaryViewport;
    }

    /// <summary>
    /// Scale a point from logical to framebuffer coordinates.
    /// </summary>
    private static Vector2 S(Vector2 point) => point * _scale;
}
