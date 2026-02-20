using System.Numerics;
using DerpEngine = DerpLib.Derp;
using Derp.Doc.Assets;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Docking;
using DerpLib.ImGui.Windows;
using DerpLib.ImGui.Widgets;
using DerpLib.Rendering;
using DerpLib.Text;
using FontAwesome.Sharp;

namespace Derp.Doc.Editor;

internal sealed class DocEditorApp
{
    private readonly DocWorkspace _workspace;
    private bool _lastShowInspector;
    private bool _lastShowPreferences;
    private ImDockLeaf? _contentDockLeaf;

    private static readonly string GearIcon = ((char)IconChar.Cog).ToString();

    // Palette.
    // Derp.Doc colors are authored as sRGB (#RRGGBB). The current SDF + presentation path effectively applies a
    // gamma curve (~sqrt) when going to the display, so we "preconvert" sRGB bytes into the engine color space
    // using a gamma=2 transform: linearByte ~= round(srgbByte^2 / 255).
    private static readonly uint CanvasBackground = ColorSrgb(0x1E, 0x1E, 0x1E); // #1E1E1E
    private static readonly uint PanelBackground = ColorSrgb(0x2D, 0x2C, 0x2D);  // #2D2C2D
    private static readonly uint PanelSurface = ColorSrgb(0x3A, 0x39, 0x3A);     // Control surface
    private static readonly uint HoverSurface = ColorSrgb(0x44, 0x43, 0x44);
    private static readonly uint ActiveSurface = ColorSrgb(0x4E, 0x4D, 0x4E);
    private static readonly uint BorderLine = ColorSrgb(0x24, 0x23, 0x24);
    private static readonly uint TabActiveBackground = PanelSurface;
    private static readonly uint TabInactiveBackground = PanelBackground;
    private static readonly uint ScrollbarThumb = ColorSrgb(0x5C, 0x5B, 0x5C);
    private static readonly uint AccentBlue = ColorSrgb(0x0D, 0x99, 0xFF); // Figma-ish blue
    private static readonly uint TextPrimary = ColorSrgb(0xEA, 0xEA, 0xEA);
    private static readonly uint TextSecondary = ColorSrgb(0xB5, 0xB5, 0xB5);
    private static readonly uint TextDisabled = ColorSrgb(0x7A, 0x7A, 0x7A);
    private static readonly uint White = ColorSrgb(0xFF, 0xFF, 0xFF);

    private static uint ColorSrgb(byte r, byte g, byte b, byte a = 255)
    {
        return ImStyle.Rgba(Gamma2Byte(r), Gamma2Byte(g), Gamma2Byte(b), a);
    }

    private static byte Gamma2Byte(byte srgbByte)
    {
        int v = srgbByte;
        int linear = (v * v + 127) / 255;
        if (linear < 0)
        {
            return 0;
        }
        if (linear > 255)
        {
            return 255;
        }
        return (byte)linear;
    }

    public DocEditorApp(DocWorkspace workspace)
    {
        _workspace = workspace;
    }

    public void Run()
    {
        DerpEngine.InitWindow(1200, 800, "Derp.Doc");
        DerpEngine.InitSdf();

        Font font = DerpEngine.LoadFont("arial");
        Font iconFont = DerpEngine.LoadFont("fa-solid-900");
        DerpEngine.SetSdfFontAtlas(font.Atlas);
        DerpEngine.SetSdfSecondaryFontAtlas(iconFont.Atlas);

        Im.Initialize();
        Im.SetFonts(font, iconFont);
        ApplyUiFontPreference();
        Im.SetMainDockTopInset(ImMainMenuBar.MenuBarHeight);
        RebuildDockLayout();

        const float deltaTime = 1f / 60f;

        while (!DerpEngine.WindowShouldClose())
        {
            DerpEngine.PollEvents();

            if (!DerpEngine.BeginDrawing())
                continue;

            DocAssetServices.ThumbnailCache.ProcessLoadQueue(2);
            DerpEngine.SdfBuffer.Reset();
            ApplyUiFontPreference();
            var styleOverride = BuildDerpDocStyle();
            Im.Style = styleOverride;
            Im.Begin(deltaTime);

            HandleShortcuts();
            DrawWorkspace(styleOverride);
            Im.End();
            DerpEngine.RenderSdf();
            DerpEngine.EndDrawing();
        }

        _workspace.Shutdown();
        DocAssetServices.AudioPreviewPlayer.Clear();
        DerpEngine.CloseWindow();
    }

    private static ImStyle BuildDerpDocStyle()
    {
        var style = Im.Style;
        style.Primary = AccentBlue;
        style.Secondary = AccentBlue;
        style.Background = CanvasBackground;
        style.Surface = PanelBackground;
        style.TextPrimary = TextPrimary;
        style.TextSecondary = TextSecondary;
        style.TextDisabled = TextDisabled;
        style.Hover = HoverSurface;
        style.Active = ActiveSurface;
        style.Border = BorderLine;
        style.TitleBar = PanelBackground;
        style.TitleBarInactive = PanelBackground;
        style.ScrollbarWidth = 6f;
        style.ScrollbarTrack = CanvasBackground;
        style.ScrollbarThumb = ScrollbarThumb;
        style.TabInactive = TabInactiveBackground;
        style.TabActive = TabActiveBackground;
        style.DockPreview = ImStyle.WithAlpha(AccentBlue, 0x40);
        style.ShadowColor = 0x80000000;
        style.SliderFill = AccentBlue;
        style.CheckMark = White;
        return style;
    }

    private static void DrawWindowBackground(int windowId, uint color)
    {
        var window = Im.WindowManager.FindWindowById(windowId);
        Vector2 scrollOffset = window?.ScrollOffset ?? Vector2.Zero;

        // BeginWindow() applies a -ScrollOffset transform to content; cancel it so the background doesn't "scroll"
        // and continues to cover the scrollbar gutter.
        Im.PushTransform(scrollOffset);

        var viewport = Im.CurrentViewport;
        var previousLayer = viewport?.CurrentLayer ?? DerpLib.ImGui.Rendering.ImDrawLayer.WindowContent;
        Im.SetDrawLayer(DerpLib.ImGui.Rendering.ImDrawLayer.Background);

        var rect = Im.WindowRect;
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            Im.SetDrawLayer(previousLayer);
            Im.PopTransform();
            return;
        }

        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, color);

        Im.SetDrawLayer(previousLayer);
        Im.PopTransform();
    }

    private void RebuildDockLayout()
    {
        var dc = Im.Context.DockController;
        bool showInspector = _workspace.ShowInspector;
        bool showPreferences = _workspace.ShowPreferences;

        Im.WindowManager.GetOrCreateWindow("Sidebar", 0, 0, 240, 700);

        for (int tabIndex = 0; tabIndex < _workspace.ContentTabs.TabCount; tabIndex++)
        {
            var tab = _workspace.ContentTabs.GetTabAt(tabIndex);
            Im.WindowManager.GetOrCreateWindow(tab.DisplayTitle, 0, 0, 900, 700, ImWindowFlags.NoBackground, explicitId: tab.WindowId);
        }

        if (showPreferences)
        {
            Im.WindowManager.GetOrCreateWindow("Preferences", 0, 0, 900, 700);
        }

        var sidebarLeaf = new ImDockLeaf();
        sidebarLeaf.WindowIds.Add(ImWindow.GetIdForTitle("Sidebar"));

        var contentLeaf = new ImDockLeaf();
        for (int tabIndex = 0; tabIndex < _workspace.ContentTabs.TabCount; tabIndex++)
        {
            contentLeaf.WindowIds.Add(_workspace.ContentTabs.GetTabAt(tabIndex).WindowId);
        }
        if (showPreferences)
        {
            contentLeaf.WindowIds.Add(ImWindow.GetIdForTitle("Preferences"));
        }

        _contentDockLeaf = contentLeaf;
        var activeTab = _workspace.ContentTabs.ActiveTab;
        if (activeTab != null)
        {
            int activeWindowId = activeTab.WindowId;
            for (int i = 0; i < contentLeaf.WindowIds.Count; i++)
            {
                if (contentLeaf.WindowIds[i] == activeWindowId)
                {
                    contentLeaf.ActiveTabIndex = i;
                    break;
                }
            }
        }

        if (showInspector)
        {
            Im.WindowManager.GetOrCreateWindow("Inspector", 0, 0, 300, 700);
            Im.WindowManager.GetOrCreateWindow("Chat", 0, 0, 300, 700);

            var rightLeaf = new ImDockLeaf();
            rightLeaf.WindowIds.Add(ImWindow.GetIdForTitle("Inspector"));
            rightLeaf.WindowIds.Add(ImWindow.GetIdForTitle("Chat"));

            var inner = new ImDockSplit(contentLeaf, rightLeaf, ImSplitDirection.Horizontal, 0.75f);
            dc.MainLayout.Root = new ImDockSplit(sidebarLeaf, inner, ImSplitDirection.Horizontal, 0.17f);
        }
        else
        {
            // Close Inspector/Chat windows if they exist so they don't become orphan floats.
            int inspectorId = ImWindow.GetIdForTitle("Inspector");
            var inspectorWindow = Im.WindowManager.FindWindowById(inspectorId);
            if (inspectorWindow != null)
            {
                Im.WindowManager.CloseWindow(inspectorWindow);
            }

            int chatId = ImWindow.GetIdForTitle("Chat");
            var chatWindow = Im.WindowManager.FindWindowById(chatId);
            if (chatWindow != null)
            {
                Im.WindowManager.CloseWindow(chatWindow);
            }

            dc.MainLayout.Root = new ImDockSplit(sidebarLeaf, contentLeaf, ImSplitDirection.Horizontal, 0.20f);
        }

        if (!showPreferences)
        {
            int preferencesId = ImWindow.GetIdForTitle("Preferences");
            var preferencesWindow = Im.WindowManager.FindWindowById(preferencesId);
            if (preferencesWindow != null)
            {
                Im.WindowManager.CloseWindow(preferencesWindow);
            }
        }

        _lastShowInspector = showInspector;
        _lastShowPreferences = showPreferences;
    }

    private void DrawWorkspace(ImStyle styleOverride)
    {
            _workspace.PollExternalChanges();
            Panels.MainMenuPanel.Draw(_workspace);

            if (_workspace.ShowInspector != _lastShowInspector ||
                _workspace.ShowPreferences != _lastShowPreferences)
            {
                RebuildDockLayout();
            }

            float top = ImMainMenuBar.MenuBarHeight;

            Panels.ProjectIoDialog.PrimeOverlayCapture();

            if (_workspace.ContentTabs.HasDirtyDockLayout)
            {
                RebuildDockLayout();
                _workspace.ContentTabs.ClearDirtyDockLayout();
            }

            SyncWorkspaceActiveContentTabFromDock();

            if (Im.BeginWindow("Sidebar", 0, top, 240, 700 - top, ImWindowFlags.NoBackground))
            {
                var sidebarStyle = styleOverride;
                sidebarStyle.Background = PanelBackground;
                sidebarStyle.Surface = PanelSurface;
                sidebarStyle.ScrollbarTrack = PanelBackground;
                Im.Context.PushStyle(sidebarStyle);
                try
                {
                    DrawWindowBackground(ImWindow.GetIdForTitle("Sidebar"), PanelBackground);

                    Panels.SidebarPanel.Draw(_workspace);
                }
                finally
                {
                    Im.Context.PopStyle();
                }
            }
            Im.EndWindow();

            if (_workspace.ContentTabs.HasDirtyDockLayout)
            {
                RebuildDockLayout();
                _workspace.ContentTabs.ClearDirtyDockLayout();
            }

            ApplyPendingContentTabFocus();

            // Draw Inspector/Chat BEFORE Content so overlay rects block content clicks.
            if (_workspace.ShowInspector)
            {
                if (Im.BeginWindow("Inspector", 0, top, 300, 700 - top, ImWindowFlags.NoBackground))
                {
                    var inspectorStyle = styleOverride;
                    inspectorStyle.Background = PanelBackground;
                    inspectorStyle.Surface = PanelSurface;
                    inspectorStyle.ScrollbarTrack = PanelBackground;
                    Im.Context.PushStyle(inspectorStyle);
                    try
                    {
                        DrawWindowBackground(ImWindow.GetIdForTitle("Inspector"), PanelBackground);

                        Panels.InspectorPanel.Draw(_workspace);
                    }
                    finally
                    {
                        Im.Context.PopStyle();
                    }
                }
                Im.EndWindow();

                if (Im.BeginWindow("Chat", 0, top, 300, 700 - top, ImWindowFlags.NoBackground))
                {
                    var chatStyle = styleOverride;
                    chatStyle.Background = PanelBackground;
                    chatStyle.Surface = PanelSurface;
                    chatStyle.ScrollbarTrack = PanelBackground;
                    Im.Context.PushStyle(chatStyle);
                    try
                    {
                        DrawWindowBackground(ImWindow.GetIdForTitle("Chat"), PanelBackground);

                        Panels.ChatPanel.Draw(_workspace, Im.WindowContentRect);
                    }
                    finally
                    {
                        Im.Context.PopStyle();
                    }
                }
                Im.EndWindow();
            }

            int contentTabCount = _workspace.ContentTabs.TabCount;
            for (int tabIndex = 0; tabIndex < contentTabCount; tabIndex++)
            {
                var tab = _workspace.ContentTabs.GetTabAt(tabIndex);
                if (Im.BeginWindow(tab.DisplayTitle, tab.WindowId, 260, top, 900, 700 - top, ImWindowFlags.NoBackground))
                {
                    _workspace.ContentTabs.TryActivateByWindowId(tab.WindowId);

                    DrawWindowBackground(tab.WindowId, CanvasBackground);

                    if (tab.Kind == DocContentTabs.TabKind.Document)
                    {
                        Panels.DocumentPanel.Draw(_workspace);

                        var wr = Im.WindowRect;
                        if (Im.Button(GearIcon, wr.Right - 30, wr.Y + 4f, 24, 24))
                        {
                            _workspace.ShowInspector = !_workspace.ShowInspector;
                        }
                    }
                    else
                    {
                        Panels.TablePanel.Draw(_workspace);
                    }
                }
                Im.EndWindow();
            }

            if (_workspace.ShowPreferences)
            {
                if (Im.BeginWindow("Preferences", 260, top, 900, 700 - top, ImWindowFlags.NoBackground))
                {
                    DrawWindowBackground(ImWindow.GetIdForTitle("Preferences"), CanvasBackground);

                    Panels.PreferencesPanel.Draw(_workspace, Im.WindowContentRect);
                }
                Im.EndWindow();
            }

            _workspace.ContentTabs.CaptureWorkspaceStateIntoTabIfActive();
            _workspace.ContentTabs.RemoveClosedTabs(Im.WindowManager);
            _workspace.ContentTabs.ClosePendingTabWindows(Im.WindowManager);
            if (_workspace.ContentTabs.HasDirtyActiveProjectState)
            {
                _workspace.PersistActiveProjectState();
                _workspace.ContentTabs.ClearDirtyActiveProjectState();
            }

            Panels.AssetBrowserModal.Draw(_workspace);
            Panels.ProjectIoDialog.Draw(_workspace);
    }

    private void SyncWorkspaceActiveContentTabFromDock()
    {
        if (_contentDockLeaf == null)
        {
            return;
        }

        // When a tab was just opened programmatically, defer dock-to-workspace sync until
        // the pending focus request has a chance to activate the new dock tab.
        if (_workspace.ContentTabs.PendingFocusWindowId != 0)
        {
            return;
        }

        if (_contentDockLeaf.WindowIds.Count <= 0)
        {
            return;
        }

        int activeIndex = _contentDockLeaf.ActiveTabIndex;
        if (activeIndex < 0 || activeIndex >= _contentDockLeaf.WindowIds.Count)
        {
            activeIndex = 0;
        }

        int activeWindowId = _contentDockLeaf.WindowIds[activeIndex];
        _workspace.ContentTabs.TryActivateByWindowId(activeWindowId);
    }

    private void ApplyPendingContentTabFocus()
    {
        if (_contentDockLeaf == null)
        {
            return;
        }

        int pendingWindowId = _workspace.ContentTabs.PendingFocusWindowId;
        if (pendingWindowId == 0)
        {
            return;
        }

        int targetIndex = -1;
        for (int i = 0; i < _contentDockLeaf.WindowIds.Count; i++)
        {
            if (_contentDockLeaf.WindowIds[i] == pendingWindowId)
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex >= 0)
        {
            _contentDockLeaf.ActiveTabIndex = targetIndex;
            var window = Im.WindowManager.FindWindowById(pendingWindowId);
            if (window != null)
            {
                Im.WindowManager.BringToFront(window);
            }

            _workspace.ContentTabs.ClearPendingFocus();
            return;
        }
    }

    private void HandleShortcuts()
    {
        if (Panels.ProjectIoDialog.IsBlockingGlobalShortcuts())
        {
            return;
        }

        var input = Im.Context.Input;

        // Don't capture undo/redo when a text input is active (table edit mode)
        // For document mode, we handle undo/redo even with keyboard captured,
        // because the text area always has focus
        if (_workspace.ActiveView == ActiveViewKind.Table && Im.Context.WantCaptureKeyboard)
            return;

        // Ctrl+Z = Undo, Ctrl+Shift+Z = Redo, Ctrl+Y = Redo
        if (input.KeyCtrlZ && !input.KeyShift)
        {
            if (_workspace.ActiveView == ActiveViewKind.Table)
                _workspace.CancelTableCellEditIfActive();
            _workspace.Undo();
        }
        else if ((input.KeyCtrlZ && input.KeyShift) || input.KeyCtrlY)
        {
            if (_workspace.ActiveView == ActiveViewKind.Table)
                _workspace.CancelTableCellEditIfActive();
            _workspace.Redo();
        }
    }

    private void ApplyUiFontPreference()
    {
        float preferredFontSize = _workspace.UserPreferences.UiFontSize;
        if (MathF.Abs(Im.Style.FontSize - preferredFontSize) < 0.001f)
        {
            return;
        }

        Im.Style.FontSize = preferredFontSize;
    }
}
