using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Plugins;
using FontAwesome.Sharp;

namespace Derp.Doc.Panels;

/// <summary>
/// Horizontal tab bar above table content for switching between views.
/// Returns the Y offset where content should start below the bar.
/// </summary>
internal static class ViewSwitcherBar
{
    private const float BarHeight = 30f;
    private const float TabPaddingX = 10f;
    private const float TabGap = 2f;
    private const float AddButtonWidth = 26f;
    private const float AccentLineHeight = 3f;

    private static readonly string PlusIcon = ((char)IconChar.Plus).ToString();
    private static readonly string GearIcon = ((char)IconChar.Cog).ToString();
    private static readonly string GridLabel = "Grid view";
    private static readonly string BoardLabel = "Board view";
    private static readonly string CalendarLabel = "Calendar view";
    private static readonly string ChartLabel = "Chart view";
    private static readonly List<IDerpDocTableViewRenderer> _customRenderersScratch = new();

    private static readonly string AddMenuId = "view_add_menu";

    // Inline rename state
    private static bool _isRenaming;
    private static string _renameViewId = "";
    private static char[] _renameBuffer = new char[64];
    private static int _renameBufferLength;
    private static bool _renameNeedsFocus;

    // Track which context menu index is open
    private static int _contextMenuViewIndex = -1;

    /// <summary>
    /// Draws the view switcher bar and returns the Y coordinate where content should start.
    /// </summary>
    public static float Draw(DocWorkspace workspace, ImRect contentRect)
    {
        var table = workspace.ActiveTable;
        if (table == null) return contentRect.Y;

        var style = Im.Style;
        var input = Im.Context.Input;
        var mousePos = Im.MousePos;
        float barY = contentRect.Y;
        float barBottom = barY + BarHeight;
        float x = contentRect.X + 4f;

        // Background
        Im.DrawRect(contentRect.X, barY, contentRect.Width, BarHeight, style.TabActive);

        // If no views, show implicit "All" tab
        if (table.Views.Count == 0)
        {
            float tabW = Im.MeasureTextWidth("All".AsSpan(), style.FontSize) + TabPaddingX * 2f;
            Im.DrawRect(x, barY, tabW, BarHeight - 1f, style.TabActive);
            Im.DrawLine(x, barBottom - AccentLineHeight, x + tabW, barBottom - AccentLineHeight, AccentLineHeight, style.Primary);
            float textY = barY + (BarHeight - style.FontSize) * 0.5f;
            Im.Text("All".AsSpan(), x + TabPaddingX, textY, style.FontSize, style.TextPrimary);
            x += tabW + TabGap;
        }
        else
        {
            // Draw tabs for each view
            for (int i = 0; i < table.Views.Count; i++)
            {
                var view = table.Views[i];
                bool isActive = view == workspace.ActiveTableView;
                float tabRadius = style.CornerRadius;
                float maxRadius = (BarHeight - 1f) * 0.5f;
                if (tabRadius > maxRadius)
                {
                    tabRadius = maxRadius;
                }

                // Inline rename?
                if (_isRenaming && string.Equals(_renameViewId, view.Id, StringComparison.Ordinal))
                {
                    float renameW = 120f;
                    float inputY = barY + (BarHeight - style.MinButtonHeight) * 0.5f;
                    Im.TextInput("view_rename_input", _renameBuffer, ref _renameBufferLength, _renameBuffer.Length, x, inputY, renameW);

                    if (_renameNeedsFocus)
                    {
                        int widgetId = Im.Context.GetId("view_rename_input");
                        Im.Context.RequestFocus(widgetId);
                        if (Im.TryGetTextInputState("view_rename_input", out _))
                        {
                            Im.SetTextInputSelection("view_rename_input", _renameBufferLength, 0, _renameBufferLength);
                        }
                        _renameNeedsFocus = false;
                    }

                    // Commit on Enter/Tab
                    if (input.KeyEnter || input.KeyTab)
                    {
                        CommitViewRename(workspace, table, view);
                    }
                    // Commit on click outside
                    else if (input.MousePressed)
                    {
                        var renameRect = new ImRect(x, inputY, renameW, style.MinButtonHeight);
                        if (!renameRect.Contains(mousePos))
                        {
                            CommitViewRename(workspace, table, view);
                        }
                    }
                    // Cancel on Escape
                    else if (input.KeyEscape)
                    {
                        _isRenaming = false;
                        Im.ClearTextInputState("view_rename_input");
                    }

                    x += renameW + TabGap;
                    continue;
                }

                string label = view.Name;
                float tabW = Im.MeasureTextWidth(label.AsSpan(), style.FontSize) + TabPaddingX * 2f;
                var tabRect = new ImRect(x, barY, tabW, BarHeight - 1f);

                // Hover
                bool hovered = tabRect.Contains(mousePos);

                if (isActive)
                {
                    Im.DrawRoundedRectPerCorner(tabRect.X, tabRect.Y, tabRect.Width, tabRect.Height, tabRadius, tabRadius, 0f, 0f, style.TabActive);
                    Im.DrawLine(tabRect.X, barBottom - AccentLineHeight, tabRect.Right, barBottom - AccentLineHeight, AccentLineHeight, style.Primary);
                }
                else if (hovered)
                {
                    uint hoverTabColor = ImStyle.Lerp(style.TabActive, style.Hover, 0.62f);
                    Im.DrawRoundedRectPerCorner(tabRect.X, tabRect.Y, tabRect.Width, tabRect.Height, tabRadius, tabRadius, 0f, 0f, hoverTabColor);
                }

                // Label
                float textY = barY + (BarHeight - style.FontSize) * 0.5f;
                Im.Text(label.AsSpan(), x + TabPaddingX, textY, style.FontSize, isActive ? style.TextPrimary : style.TextSecondary);

                // Click to switch
                if (hovered && input.MousePressed)
                {
                    workspace.ActiveTableView = view;

                    // Double-click to rename
                    if (input.IsDoubleClick)
                    {
                        BeginRename(view);
                    }
                }

                // Right-click context menu
                if (hovered && input.MouseRightPressed)
                {
                    _contextMenuViewIndex = i;
                    ImContextMenu.Open("view_tab_ctx");
                }

                x += tabW + TabGap;
            }
        }

        // View tab context menu (rendered once, outside the loop)
        if (ImContextMenu.Begin("view_tab_ctx"))
        {
            int ctxIdx = _contextMenuViewIndex;
            if (ctxIdx >= 0 && ctxIdx < table.Views.Count)
            {
                var ctxView = table.Views[ctxIdx];

                if (ImContextMenu.Item("Rename"))
                {
                    BeginRename(ctxView);
                }
                if (ImContextMenu.Item("Duplicate"))
                {
                    var clone = ctxView.Clone();
                    clone.Id = Guid.NewGuid().ToString();
                    clone.Name = ctxView.Name + " copy";
                    workspace.ExecuteCommand(new DocCommand
                    {
                        Kind = DocCommandKind.AddView,
                        TableId = table.Id,
                        ViewIndex = ctxIdx + 1,
                        ViewSnapshot = clone,
                    });
                }
                if (table.Views.Count > 1)
                {
                    ImContextMenu.Separator();
                    if (ImContextMenu.Item("Delete"))
                    {
                        workspace.ExecuteCommand(new DocCommand
                        {
                            Kind = DocCommandKind.RemoveView,
                            TableId = table.Id,
                            ViewIndex = ctxIdx,
                            ViewId = ctxView.Id,
                            ViewSnapshot = ctxView.Clone(),
                        });
                        if (workspace.ActiveTableView == ctxView)
                        {
                            workspace.ActiveTableView = table.Views.Count > 0 ? table.Views[0] : null;
                        }
                    }
                }
            }
            ImContextMenu.End();
        }

        // Inspector + "+" controls (right-aligned).
        float addBtnX = contentRect.Right - AddButtonWidth - 6f;
        float addBtnY = barY + (BarHeight - AddButtonWidth) * 0.5f;
        float inspectorBtnX = addBtnX - AddButtonWidth - 4f;

        if (Im.Button(GearIcon, inspectorBtnX, addBtnY, AddButtonWidth, AddButtonWidth))
        {
            workspace.ShowInspector = !workspace.ShowInspector;
        }

        if (Im.Button(PlusIcon, addBtnX, addBtnY, AddButtonWidth, AddButtonWidth))
        {
            ImContextMenu.Open(AddMenuId);
        }

        if (ImContextMenu.Begin(AddMenuId))
        {
            if (ImContextMenu.Item("Grid view"))
            {
                AddNewView(workspace, table, DocViewType.Grid, GridLabel);
            }
            if (ImContextMenu.Item("Board view"))
            {
                AddNewView(workspace, table, DocViewType.Board, BoardLabel);
            }
            if (ImContextMenu.Item("Calendar view"))
            {
                AddNewView(workspace, table, DocViewType.Calendar, CalendarLabel);
            }
            if (ImContextMenu.Item("Chart view"))
            {
                AddNewView(workspace, table, DocViewType.Chart, ChartLabel);
            }

            TableViewRendererRegistry.CopyRenderers(_customRenderersScratch);
            if (_customRenderersScratch.Count > 0)
            {
                ImContextMenu.Separator();
                ImContextMenu.ItemDisabled("Custom Renderers");
                for (int rendererIndex = 0; rendererIndex < _customRenderersScratch.Count; rendererIndex++)
                {
                    var renderer = _customRenderersScratch[rendererIndex];
                    if (ImContextMenu.Item(renderer.DisplayName))
                    {
                        AddNewCustomView(workspace, table, renderer);
                    }
                }
            }

            ImContextMenu.End();
        }

        return barBottom;
    }

    private static void AddNewView(DocWorkspace workspace, DocTable table, DocViewType type, string name)
    {
        var view = new DocView
        {
            Type = type,
            Name = name,
        };

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddView,
            TableId = table.Id,
            ViewIndex = table.Views.Count,
            ViewSnapshot = view,
        });

        // Switch to the newly created view
        workspace.ActiveTableView = table.Views[^1];
    }

    private static void AddNewCustomView(
        DocWorkspace workspace,
        DocTable table,
        IDerpDocTableViewRenderer renderer)
    {
        var view = new DocView
        {
            Type = DocViewType.Custom,
            CustomRendererId = renderer.RendererId,
            Name = renderer.DisplayName + " view",
        };

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.AddView,
            TableId = table.Id,
            ViewIndex = table.Views.Count,
            ViewSnapshot = view,
        });

        workspace.ActiveTableView = table.Views[^1];
    }

    private static void CommitViewRename(DocWorkspace workspace, DocTable table, DocView view)
    {
        string newName = new string(_renameBuffer, 0, _renameBufferLength).Trim();
        if (!string.IsNullOrWhiteSpace(newName) && !string.Equals(newName, view.Name, StringComparison.Ordinal))
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.RenameView,
                TableId = table.Id,
                ViewId = view.Id,
                OldName = view.Name,
                NewName = newName,
            });
        }
        _isRenaming = false;
        Im.ClearTextInputState("view_rename_input");
    }

    private static void BeginRename(DocView view)
    {
        _isRenaming = true;
        _renameViewId = view.Id;
        _renameNeedsFocus = true;
        var span = view.Name.AsSpan();
        int len = Math.Min(span.Length, _renameBuffer.Length);
        span[..len].CopyTo(_renameBuffer);
        _renameBufferLength = len;
    }

}
