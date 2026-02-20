using System;
using System.Collections.Generic;
using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using DerpLib.ImGui.Windows;
using FontAwesome.Sharp;
using Property.Runtime;

namespace Derp.UI;

internal static class ConstraintTargetPickerDialog
{
    private const float DialogWidth = 720f;
    private const float DialogHeight = 520f;
    private const float RowHeight = 24f;
    private const int SearchMaxChars = 128;

    private static bool _open;
    private static uint _constrainedEntityStableId;
    private static uint _owningPrefabStableId;
    private static int _constraintIndex;

    private static readonly char[] SearchBuffer = new char[SearchMaxChars];
    private static int _searchLength;
    private static int _selectedVisibleIndex = -1;
    private static float _scrollY;

    private static readonly List<Entry> Entries = new(capacity: 512);
    private static readonly List<int> VisibleIndices = new(capacity: 512);
    private static int _lastSearchLength = -1;
    private static EntityId[] _traversalStack = new EntityId[256];

    private static readonly string SearchButtonLabel = ((char)IconChar.MagnifyingGlass).ToString();
    private static readonly string OkButtonLabel = ((char)IconChar.Check).ToString() + " OK";
    private static readonly string CancelButtonLabel = ((char)IconChar.Xmark).ToString() + " Cancel";

    private readonly struct Entry
    {
        public readonly string Label;
        public readonly uint TargetSourceStableId;

        public Entry(string label, uint targetSourceStableId)
        {
            Label = label;
            TargetSourceStableId = targetSourceStableId;
        }
    }

    public static void Open(UiWorkspace workspace, EntityId constrainedEntity, EntityId owningPrefab, int constraintIndex)
    {
        if (constrainedEntity.IsNull || owningPrefab.IsNull)
        {
            return;
        }

        uint constrainedStableId = workspace.World.GetStableId(constrainedEntity);
        uint owningPrefabStableId = workspace.World.GetStableId(owningPrefab);
        if (constrainedStableId == 0 || owningPrefabStableId == 0)
        {
            return;
        }

        _open = true;
        _constrainedEntityStableId = constrainedStableId;
        _owningPrefabStableId = owningPrefabStableId;
        _constraintIndex = constraintIndex;
        _scrollY = 0f;
        _selectedVisibleIndex = -1;
        _lastSearchLength = -1;
        _searchLength = 0;
        SearchBuffer.AsSpan().Clear();

        RebuildEntries(workspace);
        EnsureVisibleListUpToDate();
    }

    public static bool IsOpen => _open;

    public static bool IsBlockingGlobalShortcuts()
    {
        if (!_open)
        {
            return false;
        }

        string title = GetWindowTitle();
        var focused = Im.WindowManager.FocusedWindow;
        if (focused != null && focused.IsOpen && focused.Title == title)
        {
            return true;
        }

        var window = Im.WindowManager.FindWindow(title);
        if (window == null || !window.IsOpen)
        {
            return false;
        }

        var viewport = Im.Context.CurrentViewport;
        Vector2 screenMouse = viewport == null ? Im.Context.Input.MousePos : viewport.ScreenPosition + Im.Context.Input.MousePos;
        return window.Rect.Contains(screenMouse);
    }

    public static void Draw(UiWorkspace workspace)
    {
        if (!_open)
        {
            return;
        }

        var ctx = Im.Context;
        if (ctx.Input.KeyEscape)
        {
            Close();
            return;
        }

        var viewport = Im.CurrentViewport;
        Vector2 vpSize = viewport == null ? new Vector2(1280f, 720f) : viewport.Size;

        float x = (vpSize.X - DialogWidth) * 0.5f;
        float y = (vpSize.Y - DialogHeight) * 0.5f;

        string title = GetWindowTitle();
        if (!Im.BeginWindow(title, x, y, DialogWidth, DialogHeight, ImWindowFlags.NoResize | ImWindowFlags.NoMove))
        {
            Im.EndWindow();
            return;
        }

        DrawToolbar(workspace);
        ImLayout.Space(6f);
        DrawList(workspace);
        ImLayout.Space(6f);
        DrawFooter(workspace);

        Im.EndWindow();
    }

    private static string GetWindowTitle()
    {
        return "Select Target";
    }

    private static void DrawToolbar(UiWorkspace workspace)
    {
        var row = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        float x = row.X;
        float y = row.Y;

        Im.Context.PushId("constraint_target_search");
        bool pressed = Im.Button(SearchButtonLabel, x, y, 40f, row.Height);
        Im.Context.PopId();
        if (pressed)
        {
            // focus occurs naturally by clicking the text input; keep button for discoverability
        }
        x += 40f + Im.Style.Spacing;

        float inputWidth = Math.Max(120f, row.Right - x);
        _ = Im.TextInput("constraint_target_search_input", SearchBuffer, ref _searchLength, SearchBuffer.Length, x, y, inputWidth);

        EnsureVisibleListUpToDate();
    }

    private static void DrawList(UiWorkspace workspace)
    {
        EnsureSelectionIsValid(workspace);

        var rect = ImLayout.AllocateRect(0f, 380f);

        int visibleCount = VisibleIndices.Count;
        float contentHeight = visibleCount * RowHeight;

        float scrollbarWidth = Im.Style.ScrollbarWidth;
        bool hasScrollbar = contentHeight > rect.Height;
        var contentRect = rect;
        var scrollbarRect = rect;
        if (hasScrollbar)
        {
            contentRect.Width = rect.Width - scrollbarWidth;
            scrollbarRect = new ImRect(rect.Right - scrollbarWidth, rect.Y, scrollbarWidth, rect.Height);
        }

        int scrollbarWidgetId = Im.Context.GetId("constraint_target_scroll");
        float contentY = ImScrollView.Begin(contentRect, contentHeight, ref _scrollY, handleMouseWheel: true);

        int first = (int)MathF.Floor(_scrollY / RowHeight);
        if (first < 0)
        {
            first = 0;
        }
        int rowsPerPage = (int)MathF.Ceiling(contentRect.Height / RowHeight) + 2;
        int last = first + rowsPerPage;
        if (last > visibleCount)
        {
            last = visibleCount;
        }

        var input = Im.Context.Input;
        for (int visibleIndex = first; visibleIndex < last; visibleIndex++)
        {
            if ((uint)visibleIndex >= (uint)VisibleIndices.Count)
            {
                break;
            }

            int entryIndex = VisibleIndices[visibleIndex];
            if ((uint)entryIndex >= (uint)Entries.Count)
            {
                continue;
            }

            Entry entry = Entries[entryIndex];

            float y = contentY + visibleIndex * RowHeight;
            var rowRect = new ImRect(contentRect.X, y, contentRect.Width, RowHeight);
            bool selected = visibleIndex == _selectedVisibleIndex;

            DrawEntryRow(workspace, input, visibleIndex, entry, rowRect, selected);
        }

        ImScrollView.End(scrollbarWidgetId, scrollbarRect, rect.Height, contentHeight, ref _scrollY);
    }

    private static void DrawEntryRow(UiWorkspace workspace, ImInput input, int visibleIndex, in Entry entry, ImRect rowRect, bool selected)
    {
        bool hovered = rowRect.Contains(Im.MousePos);
        if (selected)
        {
            Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, Im.Style.Active);
        }
        else if (hovered)
        {
            Im.DrawRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, Im.Style.Hover);
        }

        float textY = rowRect.Y + (rowRect.Height - Im.Style.FontSize) * 0.5f;
        Im.Text(entry.Label.AsSpan(), rowRect.X + 8f, textY, Im.Style.FontSize, Im.Style.TextPrimary);

        if (hovered && input.MousePressed)
        {
            _selectedVisibleIndex = visibleIndex;
        }

        if (hovered && input.MousePressed && input.IsDoubleClick)
        {
            ApplySelection(workspace, entry.TargetSourceStableId);
        }
    }

    private static void DrawFooter(UiWorkspace workspace)
    {
        var row = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        float y = row.Y;

        float okX = row.Right - 120f - (120f + Im.Style.Spacing);
        float cancelX = row.Right - 120f;

        if (DrawFooterButton(new ImRect(cancelX, y, 120f, row.Height), CancelButtonLabel, enabled: true))
        {
            Close();
            return;
        }

        bool canOk = _selectedVisibleIndex >= 0 && _selectedVisibleIndex < VisibleIndices.Count;
        if (DrawFooterButton(new ImRect(okX, y, 120f, row.Height), OkButtonLabel, enabled: canOk))
        {
            int entryIndex = VisibleIndices[_selectedVisibleIndex];
            if ((uint)entryIndex >= (uint)Entries.Count)
            {
                return;
            }

            ApplySelection(workspace, Entries[entryIndex].TargetSourceStableId);
        }
    }

    private static bool DrawFooterButton(ImRect rect, string label, bool enabled)
    {
        bool hovered = enabled && rect.Contains(Im.MousePos);
        uint bg = enabled ? (hovered ? Im.Style.Hover : Im.Style.Surface) : Im.Style.Surface;
        uint border = ImStyle.WithAlpha(Im.Style.Border, (byte)180);
        uint textColor = enabled ? Im.Style.TextPrimary : Im.Style.TextDisabled;

        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, bg);
        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, border, 1f);

        ReadOnlySpan<char> visible = GetVisibleLabelText(label);
        float fontSize = Im.Style.FontSize;
        float textWidth = Im.MeasureTextWidth(visible, fontSize);
        float textX = rect.X + (rect.Width - textWidth) * 0.5f;
        float textY = rect.Y + (rect.Height - fontSize) * 0.5f;
        Im.Text(visible, textX, textY, fontSize, textColor);

        return hovered && Im.MousePressed;
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

    private static void EnsureSelectionIsValid(UiWorkspace workspace)
    {
        if (_selectedVisibleIndex >= 0 && _selectedVisibleIndex < VisibleIndices.Count)
        {
            return;
        }

        uint current = GetCurrentTargetSourceStableId(workspace);
        if (current == 0)
        {
            _selectedVisibleIndex = 0;
            return;
        }

        for (int visibleIndex = 0; visibleIndex < VisibleIndices.Count; visibleIndex++)
        {
            int entryIndex = VisibleIndices[visibleIndex];
            if ((uint)entryIndex >= (uint)Entries.Count)
            {
                continue;
            }

            if (Entries[entryIndex].TargetSourceStableId == current)
            {
                _selectedVisibleIndex = visibleIndex;
                return;
            }
        }

        _selectedVisibleIndex = 0;
    }

    private static uint GetCurrentTargetSourceStableId(UiWorkspace workspace)
    {
        if (_constrainedEntityStableId == 0)
        {
            return 0;
        }

        EntityId constrainedEntity = workspace.World.GetEntityByStableId(_constrainedEntityStableId);
        if (constrainedEntity.IsNull)
        {
            return 0;
        }

        if (!workspace.World.TryGetComponent(constrainedEntity, ConstraintListComponent.Api.PoolIdConst, out AnyComponentHandle constraintsAny) || !constraintsAny.IsValid)
        {
            return 0;
        }

        var constraintsHandle = new ConstraintListComponentHandle(constraintsAny.Index, constraintsAny.Generation);
        var constraints = ConstraintListComponent.Api.FromHandle(workspace.PropertyWorld, constraintsHandle);
        if (!constraints.IsAlive || constraints.Count == 0)
        {
            return 0;
        }

        ushort count = constraints.Count;
        if (count > ConstraintListComponent.MaxConstraints)
        {
            count = ConstraintListComponent.MaxConstraints;
        }

        if ((uint)_constraintIndex >= (uint)count)
        {
            return 0;
        }

        ReadOnlySpan<uint> targets = constraints.TargetSourceStableIdReadOnlySpan();
        return targets[_constraintIndex];
    }

    private static void ApplySelection(UiWorkspace workspace, uint targetSourceStableId)
    {
        if (_constrainedEntityStableId == 0 || _owningPrefabStableId == 0)
        {
            Close();
            return;
        }

        EntityId constrainedEntity = workspace.World.GetEntityByStableId(_constrainedEntityStableId);
        EntityId owningPrefab = workspace.World.GetEntityByStableId(_owningPrefabStableId);
        if (constrainedEntity.IsNull || owningPrefab.IsNull)
        {
            Close();
            return;
        }

        if (!workspace.World.TryGetComponent(constrainedEntity, ConstraintListComponent.Api.PoolIdConst, out AnyComponentHandle constraintsAny) || !constraintsAny.IsValid)
        {
            Close();
            return;
        }

        var constraintsHandle = new ConstraintListComponentHandle(constraintsAny.Index, constraintsAny.Generation);
        var constraints = ConstraintListComponent.Api.FromHandle(workspace.PropertyWorld, constraintsHandle);
        if (!constraints.IsAlive)
        {
            Close();
            return;
        }

        ushort count = constraints.Count;
        if (count > ConstraintListComponent.MaxConstraints)
        {
            count = ConstraintListComponent.MaxConstraints;
        }

        if ((uint)_constraintIndex >= (uint)count)
        {
            Close();
            return;
        }

        Span<uint> targets = constraints.TargetSourceStableIdSpan();
        targets[_constraintIndex] = targetSourceStableId;
        workspace.BumpPrefabRevision(owningPrefab);
        Close();
    }

    private static void EnsureVisibleListUpToDate()
    {
        if (_lastSearchLength == _searchLength)
        {
            return;
        }

        _lastSearchLength = _searchLength;
        VisibleIndices.Clear();

        string filter = _searchLength <= 0 ? string.Empty : new string(SearchBuffer, 0, _searchLength);
        for (int i = 0; i < Entries.Count; i++)
        {
            string label = Entries[i].Label;
            if (string.IsNullOrEmpty(filter) || label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                VisibleIndices.Add(i);
            }
        }

        if (_selectedVisibleIndex >= VisibleIndices.Count)
        {
            _selectedVisibleIndex = VisibleIndices.Count - 1;
        }
        if (_selectedVisibleIndex < 0 && VisibleIndices.Count > 0)
        {
            _selectedVisibleIndex = 0;
        }
    }

    private static void RebuildEntries(UiWorkspace workspace)
    {
        Entries.Clear();
        Entries.Add(new Entry("(None)", 0));

        if (_owningPrefabStableId == 0)
        {
            return;
        }

        EntityId owningPrefab = workspace.World.GetEntityByStableId(_owningPrefabStableId);
        if (owningPrefab.IsNull || workspace.World.GetNodeType(owningPrefab) != UiNodeType.Prefab)
        {
            return;
        }

        int stackCount = 0;
        EnsureTraversalCapacity(1);
        _traversalStack[stackCount++] = owningPrefab;

        while (stackCount > 0)
        {
            EntityId current = _traversalStack[--stackCount];

            ReadOnlySpan<EntityId> children = workspace.World.GetChildren(current);
            for (int i = 0; i < children.Length; i++)
            {
                EntityId child = children[i];
                UiNodeType type = workspace.World.GetNodeType(child);
                if (type == UiNodeType.Shape || type == UiNodeType.BooleanGroup || type == UiNodeType.Text || type == UiNodeType.PrefabInstance)
                {
                    uint stableId = workspace.World.GetStableId(child);
                    if (stableId != 0)
                    {
                        workspace.EnsureDefaultLayerName(child);
                        string label = workspace.TryGetLayerName(stableId, out string name) && !string.IsNullOrEmpty(name)
                            ? name
                            : type + " " + stableId;
                        uint targetSourceStableId = stableId;
                        if (workspace.World.TryGetComponent(child, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) && expandedAny.IsValid)
                        {
                            var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
                            var expanded = PrefabExpandedComponent.Api.FromHandle(workspace.PropertyWorld, expandedHandle);
                            if (expanded.IsAlive && expanded.SourceNodeStableId != 0)
                            {
                                targetSourceStableId = expanded.SourceNodeStableId;
                            }
                        }

                        Entries.Add(new Entry(label, targetSourceStableId));
                    }
                }

                if (type != UiNodeType.None)
                {
                    EnsureTraversalCapacity(stackCount + 1);
                    _traversalStack[stackCount++] = child;
                }
            }
        }
    }

    private static void EnsureTraversalCapacity(int required)
    {
        if (_traversalStack.Length >= required)
        {
            return;
        }

        int next = Math.Max(required, _traversalStack.Length * 2);
        Array.Resize(ref _traversalStack, next);
    }

    private static void Close()
    {
        _open = false;
        _constrainedEntityStableId = 0;
        _owningPrefabStableId = 0;
        _constraintIndex = 0;
        _selectedVisibleIndex = -1;
        _scrollY = 0f;
        _searchLength = 0;
        _lastSearchLength = -1;
        SearchBuffer.AsSpan().Clear();
        Entries.Clear();
        VisibleIndices.Clear();
    }
}
