using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Rendering;
using DerpLib.ImGui.Viewport;
using DerpLib.ImGui.Widgets;
using FontAwesome.Sharp;

namespace Derp.UI;

internal static class Toolbar
{
    internal const float BarHeight = 44f;

    private const float SeparatorWidth = 10f;
    private const float DropdownWidth = 22f;
    private const float SplitIconMainExtraWidth = 8f;

    private static bool _overlayActive;
    private static ImDrawLayer _previousLayer;
    private static int _previousSortKey;
    private static Vector4 _previousClipRect;
    private static Vector2 _savedTranslation;
    private static bool _pushedCancelTransform;
    private static bool _pushedClipOverride;

    private static readonly string SelectToolButtonLabel = ((char)IconChar.ArrowPointer).ToString();
    private static readonly string PrefabToolButtonLabel = ((char)IconChar.LayerGroup).ToString();
    private static readonly string InstanceToolButtonLabel = ((char)IconChar.Clone).ToString();
    private static readonly string RectToolButtonLabel = ((char)IconChar.Square).ToString();
    private static readonly string CircleToolButtonLabel = ((char)IconChar.Circle).ToString();
    private static readonly string PenToolButtonLabel = ((char)IconChar.Pen).ToString();
    private static readonly string TextToolButtonLabel = ((char)IconChar.Font).ToString();

    private static readonly string RuntimePlayIcon = ((char)IconChar.Play).ToString();
    private static readonly string RuntimeStopIcon = ((char)IconChar.Stop).ToString();
    private static readonly string RuntimePlayButtonLabel = RuntimePlayIcon;
    private static readonly string RuntimeStopButtonLabel = RuntimeStopIcon;

    private static readonly string CreateGroupMenuLabel = ((char)IconChar.LayerGroup).ToString() + " Create Group";
    private static readonly string CreateMaskMenuLabel = ((char)IconChar.Mask).ToString() + " Create Mask";
    private static readonly string CreateBooleanMenuLabel = ((char)IconChar.CircleNodes).ToString() + " Create Boolean";

    private static readonly string RectMenuLabel = ((char)IconChar.Square).ToString() + " Rect";
    private static readonly string CircleMenuLabel = ((char)IconChar.Circle).ToString() + " Circle";
    private static readonly string PenMenuLabel = ((char)IconChar.Pen).ToString() + " Pen";
    private static readonly string TextMenuLabel = ((char)IconChar.Font).ToString() + " Text";

    private static readonly string[] BooleanOpMenuLabels =
    [
        ((char)IconChar.CirclePlus).ToString() + " Union",
        ((char)IconChar.CircleMinus).ToString() + " Subtract",
        ((char)IconChar.CircleDot).ToString() + " Intersect",
        ((char)IconChar.CircleXmark).ToString() + " Exclude",
    ];

    public static void DrawToolbarWindow(UiWorkspace workspace)
    {
        HandleUndoRedoShortcuts(workspace);
        HandleClipboardShortcuts(workspace);

        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            return;
        }

        BeginOverlay(viewport);

        var style = Im.Style;
        float barY = ImMainMenuBar.MenuBarHeight;
        var barRect = new ImRect(0f, barY, viewport.Size.X, BarHeight);

        Im.DrawRect(barRect.X, barRect.Y, barRect.Width, barRect.Height, style.Background);
        Im.DrawLine(barRect.X, barRect.Bottom, barRect.Right, barRect.Bottom, 1f, style.Border);
        Im.Context.AddOverlayCaptureRect(barRect);

        float buttonY = barRect.Y + (barRect.Height - style.MinButtonHeight) * 0.5f;
        float cursorX = style.Padding;

        Im.Context.PushId(0x54424C52); // "TBLR"

        DrawRuntimePlayButton(workspace, barRect, buttonY);

        DrawToolButtonIcon(workspace, SelectToolButtonLabel, UiWorkspace.CanvasTool.Select, ref cursorX, buttonY, isActive: workspace.ActiveTool == UiWorkspace.CanvasTool.Select);
        DrawToolButtonPrefab(workspace, ref cursorX, buttonY);
        DrawToolButtonInstance(workspace, ref cursorX, buttonY);
        DrawToolButtonShape(workspace, ref cursorX, buttonY);

        Im.Context.PopId();

        CleanupOverlay();
    }

    private static void HandleUndoRedoShortcuts(UiWorkspace workspace)
    {
        if (workspace.IsRuntimeMode)
        {
            return;
        }

        var input = Im.Context.Input;

        if (input.KeyCtrlZ)
        {
            if (input.KeyShift)
            {
                workspace.Commands.Redo();
            }
            else
            {
                workspace.Commands.Undo();
            }
        }
        else if (input.KeyCtrlY)
        {
            workspace.Commands.Redo();
        }
    }

    private static void HandleClipboardShortcuts(UiWorkspace workspace)
    {
        if (workspace.IsRuntimeMode)
        {
            return;
        }

        if (Im.Context.WantCaptureKeyboard || IsBlockingGlobalShortcuts())
        {
            return;
        }

        var input = Im.Context.Input;

        if (input.KeyCtrlC)
        {
            if (workspace.Commands.CopySelectionToClipboard())
            {
                workspace.ShowToast("Copied");
            }
        }
        else if (input.KeyCtrlV)
        {
            if (!workspace.Commands.PasteClipboardAtCursor())
            {
                workspace.ShowToast("Nothing to paste");
            }
        }
        else if (input.KeyCtrlD)
        {
            if (!workspace.Commands.DuplicateSelectionAsSiblings())
            {
                workspace.ShowToast("Nothing to duplicate");
            }
        }
    }

    private static bool IsBlockingGlobalShortcuts()
    {
        if (DocumentIoDialog.IsBlockingGlobalShortcuts())
        {
            return true;
        }

        if (ConstraintTargetPickerDialog.IsBlockingGlobalShortcuts())
        {
            return true;
        }

        var focused = Im.WindowManager.FocusedWindow;
        if (focused != null && focused.Title == "Animations")
        {
            return true;
        }

        var animationsWindow = Im.WindowManager.FindWindow("Animations");
        if (animationsWindow == null || !animationsWindow.IsOpen)
        {
            return false;
        }

        var viewport = Im.Context.CurrentViewport;
        Vector2 screenMouse = viewport == null ? Im.Context.Input.MousePos : viewport.ScreenPosition + Im.Context.Input.MousePos;
        return animationsWindow.Rect.Contains(screenMouse);
    }

    private static void DrawToolButtonIcon(UiWorkspace workspace, string label, UiWorkspace.CanvasTool tool, ref float cursorX, float y, bool isActive)
    {
        var style = Im.Style;
        float h = style.MinButtonHeight;
        float x = cursorX;
        float w = h;

        if (Im.Button(label, x, y, w, h))
        {
            TrySetActiveTool(workspace, tool);
        }

        if (isActive)
        {
            Im.DrawRoundedRectStroke(x, y, w, h, style.CornerRadius, style.Primary, 2f);
        }

        cursorX += w + style.Spacing;
    }

    private static void DrawRuntimePlayButton(UiWorkspace workspace, ImRect barRect, float y)
    {
        var style = Im.Style;
        float h = style.MinButtonHeight;
        float w = h;

        float x = barRect.X + (barRect.Width - w) * 0.5f;
        string label = workspace.IsRuntimeMode ? RuntimeStopButtonLabel : RuntimePlayButtonLabel;

        Im.Context.PushId("runtime_play");
        if (Im.Button(label, x, y, w, h))
        {
            workspace.ToggleRuntimeMode();
        }
        Im.Context.PopId();

        if (workspace.IsRuntimeMode)
        {
            Im.DrawRoundedRectStroke(x, y, w, h, style.CornerRadius, style.Primary, 2f);
        }
    }

    private static void DrawToolButtonPrefab(UiWorkspace workspace, ref float cursorX, float y)
    {
        var style = Im.Style;
        float h = style.MinButtonHeight;
        float x = cursorX;
        float w = (h + SplitIconMainExtraWidth) + DropdownWidth;

        Im.Context.PushId(1);
        var result = ImSplitDropdownButton.Draw(
            PrefabToolButtonLabel,
            x,
            y,
            w,
            h,
            dropdownWidth: DropdownWidth,
            isActiveOutline: workspace.ActiveTool == UiWorkspace.CanvasTool.Prefab);

        if (result == ImSplitDropdownButtonResult.Primary)
        {
            TrySetActiveTool(workspace, UiWorkspace.CanvasTool.Prefab);
        }
        else if (result == ImSplitDropdownButtonResult.Dropdown)
        {
            ImContextMenu.OpenAt("##menu", x, y + h);
        }

        if (ImContextMenu.Begin("##menu"))
        {
            if (workspace.HasActivePrefab)
            {
                if (ImContextMenu.Item(CreateGroupMenuLabel))
                {
                    workspace.Commands.CreateEmptyGroup();
                }

                if (ImContextMenu.Item(CreateMaskMenuLabel))
                {
                    workspace.Commands.CreateEmptyMaskGroup();
                }

                if (ImContextMenu.BeginMenu(CreateBooleanMenuLabel))
                {
                    for (int i = 0; i < UiWorkspace.BooleanOpOptions.Length; i++)
                    {
                        if (ImContextMenu.Item(BooleanOpMenuLabels[i]))
                        {
                            workspace.Commands.CreateEmptyBooleanGroup(i);
                        }
                    }
                    ImContextMenu.EndMenu();
                }
            }
            else
            {
                ImContextMenu.ItemDisabled("No active prefab");
            }

            ImContextMenu.End();
        }

        Im.Context.PopId();

        cursorX += w + style.Spacing;
    }

    private static void DrawToolButtonInstance(UiWorkspace workspace, ref float cursorX, float y)
    {
        var style = Im.Style;
        float h = style.MinButtonHeight;
        float x = cursorX;
        float w = (h + SplitIconMainExtraWidth) + DropdownWidth;

        Im.Context.PushId(2);
        var result = ImSplitDropdownButton.Draw(
            InstanceToolButtonLabel,
            x,
            y,
            w,
            h,
            dropdownWidth: DropdownWidth,
            isActiveOutline: workspace.ActiveTool == UiWorkspace.CanvasTool.Instance);

        if (result == ImSplitDropdownButtonResult.Primary)
        {
            TrySetActiveTool(workspace, UiWorkspace.CanvasTool.Instance);
        }
        else if (result == ImSplitDropdownButtonResult.Dropdown)
        {
            ImContextMenu.OpenAt("##menu", x, y + h);
        }

        if (ImContextMenu.Begin("##menu"))
        {
            if (workspace.TryGetPrefabInstanceSourceDropdown(out string[] options, out int selectedIndex))
            {
                for (int i = 0; i < options.Length; i++)
                {
                    bool checkedValue = i == selectedIndex;
                    if (ImContextMenu.ItemCheckbox(options[i], ref checkedValue))
                    {
                        workspace.TrySetPrefabInstanceSourceByIndex(i);
                        if (workspace.HasActivePrefab)
                        {
                            workspace.ActiveTool = UiWorkspace.CanvasTool.Instance;
                        }
                    }
                }
            }
            else
            {
                ImContextMenu.ItemDisabled("No prefabs");
            }

            ImContextMenu.End();
        }

        Im.Context.PopId();

        cursorX += w + style.Spacing;
    }

    private static void DrawToolButtonShape(UiWorkspace workspace, ref float cursorX, float y)
    {
        var style = Im.Style;
        float h = style.MinButtonHeight;
        float x = cursorX;
        float w = (h + SplitIconMainExtraWidth) + DropdownWidth;
        UiWorkspace.CanvasTool primaryTool = GetPrimaryShapeTool(workspace.ActiveTool);
        string label = GetShapeToolButtonLabel(primaryTool);

        Im.Context.PushId(3);
        var result = ImSplitDropdownButton.Draw(
            label,
            x,
            y,
            w,
            h,
            dropdownWidth: DropdownWidth,
            isActiveOutline: IsShapeTool(workspace.ActiveTool));

        if (result == ImSplitDropdownButtonResult.Primary)
        {
            TrySetActiveTool(workspace, primaryTool);
        }
        else if (result == ImSplitDropdownButtonResult.Dropdown)
        {
            ImContextMenu.OpenAt("##menu", x, y + h);
        }

        if (ImContextMenu.Begin("##menu"))
        {
            DrawShapeMenuItem(workspace, RectMenuLabel, UiWorkspace.CanvasTool.Rect);
            DrawShapeMenuItem(workspace, CircleMenuLabel, UiWorkspace.CanvasTool.Circle);
            DrawShapeMenuItem(workspace, PenMenuLabel, UiWorkspace.CanvasTool.Pen);
            DrawShapeMenuItem(workspace, TextMenuLabel, UiWorkspace.CanvasTool.Text);
            ImContextMenu.End();
        }

        Im.Context.PopId();

        cursorX += w + style.Spacing;
    }

    private static void DrawShapeMenuItem(UiWorkspace workspace, string label, UiWorkspace.CanvasTool tool)
    {
        if (!workspace.HasActivePrefab)
        {
            ImContextMenu.ItemDisabled(label);
            return;
        }

        if (ImContextMenu.Item(label))
        {
            workspace.ActiveTool = tool;
        }
    }

    private static UiWorkspace.CanvasTool GetPrimaryShapeTool(UiWorkspace.CanvasTool activeTool)
    {
        if (IsShapeTool(activeTool))
        {
            return activeTool;
        }

        return UiWorkspace.CanvasTool.Rect;
    }

    private static bool IsShapeTool(UiWorkspace.CanvasTool tool)
    {
        return tool == UiWorkspace.CanvasTool.Rect ||
               tool == UiWorkspace.CanvasTool.Circle ||
               tool == UiWorkspace.CanvasTool.Pen ||
               tool == UiWorkspace.CanvasTool.Text;
    }

    private static string GetShapeToolButtonLabel(UiWorkspace.CanvasTool tool)
    {
        switch (tool)
        {
            case UiWorkspace.CanvasTool.Circle:
                return CircleToolButtonLabel;
            case UiWorkspace.CanvasTool.Pen:
                return PenToolButtonLabel;
            case UiWorkspace.CanvasTool.Text:
                return TextToolButtonLabel;
            default:
                return RectToolButtonLabel;
        }
    }

    private static void TrySetActiveTool(UiWorkspace workspace, UiWorkspace.CanvasTool tool)
    {
        if (workspace.IsRuntimeMode)
        {
            workspace.ShowToast("Stop runtime to edit");
            return;
        }

        if (!workspace.HasActivePrefab &&
            tool != UiWorkspace.CanvasTool.Select &&
            tool != UiWorkspace.CanvasTool.Prefab)
        {
            workspace.ShowToast("No active prefab");
            return;
        }

        workspace.ActiveTool = tool;
    }

    private static void BeginOverlay(ImViewport viewport)
    {
        _overlayActive = false;
        _pushedCancelTransform = false;

        _previousLayer = viewport.CurrentLayer;
        var drawList = viewport.GetDrawList(ImDrawLayer.Overlay);
        _previousSortKey = drawList.GetSortKey();
        _previousClipRect = drawList.GetClipRect();

        Im.SetDrawLayer(ImDrawLayer.Overlay);
        drawList.SetSortKey(int.MaxValue - 640);
        drawList.ClearClipRect();

        _savedTranslation = Im.CurrentTranslation;
        if (_savedTranslation.X != 0f || _savedTranslation.Y != 0f)
        {
            Im.PushTransform(-_savedTranslation);
            _pushedCancelTransform = true;
        }

        Im.Context.PushOverlayScope();

        Im.PushClipRectOverride(new ImRect(0f, 0f, viewport.Size.X, viewport.Size.Y));
        _pushedClipOverride = true;

        _overlayActive = true;
    }

    private static void CleanupOverlay()
    {
        if (!_overlayActive)
        {
            return;
        }

        if (_pushedClipOverride)
        {
            Im.PopClipRect();
            _pushedClipOverride = false;
        }

        if (_pushedCancelTransform)
        {
            Im.PopTransform();
            _pushedCancelTransform = false;
        }

        Im.Context.PopOverlayScope();

        var viewport = Im.CurrentViewport;
        if (viewport != null)
        {
            var drawList = viewport.GetDrawList(ImDrawLayer.Overlay);
            drawList.SetClipRect(_previousClipRect);
            drawList.SetSortKey(_previousSortKey);
            Im.SetDrawLayer(_previousLayer);
        }

        _overlayActive = false;
    }
}
