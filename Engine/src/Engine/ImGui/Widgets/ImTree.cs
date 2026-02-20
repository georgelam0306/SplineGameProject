using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;

namespace DerpLib.ImGui.Widgets;

/// <summary>
/// Tree widget for hierarchical data display with expand/collapse nodes.
/// Uses an inline declarative API similar to Dear ImGui's TreeNode pattern.
/// </summary>
/// <example>
/// if (ImTree.BeginNode("Root"))
/// {
///     if (ImTree.BeginNode("Child 1"))
///     {
///         ImTree.Leaf("Grandchild");
///         ImTree.EndNode();
///     }
///     ImTree.Leaf("Child 2");
///     ImTree.EndNode();
/// }
/// </example>
public static class ImTree
{
    // State tracking (fixed-size arrays to avoid allocations)
    private static readonly NodeState[] _nodeStates = new NodeState[256];
    private static readonly int[] _nodeStateIds = new int[256];
    private static int _nodeStateCount;
    private static int _selectedNodeId;
    private static int _hoveredNodeId;

    // Tree context stack (for nested trees)
    private static readonly TreeContext[] _contextStack = new TreeContext[8];
    private static int _contextDepth;
    private static TreeContext _currentContext;

    // Visual settings
    public static float IndentWidth = 18f;
    public static float RowHeight = 22f;
    public static float IconSize = 12f;
    public static float IconPadding = 4f;
    public static bool ShowIndentLines = true;
    public static bool ShowLeafBullet = true;

    /// <summary>True if the last LeafIconText call was right-clicked this frame.</summary>
    public static bool LastLeafRightClicked { get; private set; }
    /// <summary>The row rect of the last LeafIconText call that was right-clicked.</summary>
    public static ImRect LastLeafRect { get; private set; }

    /// <summary>True if the last BeginNode call was right-clicked this frame.</summary>
    public static bool LastNodeRightClicked { get; private set; }
    /// <summary>The row rect of the last BeginNode call.</summary>
    public static ImRect LastNodeRect { get; private set; }

    private struct NodeState
    {
        public bool Expanded;
    }

    private struct TreeContext
    {
        public int TreeId;
        public float X;
        public float Y;
        public float Width;
        public float CurrentY;
        public float WindowLocalStartY; // For reporting content size to layout
        public int Depth;
        public int LastNodeId;
        public bool SelectionChanged;
        public int NewSelectedId;
    }

    /// <summary>
    /// Begin a tree widget. Must be paired with End().
    /// </summary>
    public static bool Begin(string id, float x, float y, float width)
    {
        return Begin(Im.Context.GetId(id), x, y, width);
    }

    /// <summary>
    /// Begin a tree widget with integer ID.
    /// </summary>
    public static bool Begin(int id, float x, float y, float width)
    {
        // Push current context if nested
        if (_currentContext.TreeId != 0 && _contextDepth < 8)
        {
            _contextStack[_contextDepth++] = _currentContext;
        }

        var rect = new ImRect(x, y, width, 0);

        _currentContext = new TreeContext
        {
            TreeId = id,
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            CurrentY = rect.Y,
            WindowLocalStartY = y, // Store window-local Y for content reporting
            Depth = 0,
            LastNodeId = 0,
            SelectionChanged = false,
            NewSelectedId = 0
        };

        return true;
    }

    /// <summary>
    /// End the tree widget.
    /// </summary>
    /// <returns>True if selection changed during this frame.</returns>
    public static bool End()
    {
        bool changed = _currentContext.SelectionChanged;

        if (_currentContext.SelectionChanged)
            _selectedNodeId = _currentContext.NewSelectedId;

        // Report content used to layout system for scroll calculation
        float treeHeight = GetTreeHeight();
        if (treeHeight > 0)
        {
            ImLayout.ReportContentUsed(_currentContext.WindowLocalStartY, treeHeight);
        }

        // Pop context if nested
        if (_contextDepth > 0)
            _currentContext = _contextStack[--_contextDepth];
        else
            _currentContext = default;

        return changed;
    }

    /// <summary>
    /// Get the currently selected node ID.
    /// </summary>
    public static int SelectedNodeId => _selectedNodeId;

    /// <summary>
    /// Set the selected node ID programmatically.
    /// </summary>
    public static void SetSelected(int nodeId)
    {
        _selectedNodeId = nodeId;
    }

    /// <summary>
    /// Clear selection.
    /// </summary>
    public static void ClearSelection()
    {
        _selectedNodeId = 0;
    }

    /// <summary>
    /// Begin a tree node that can have children.
    /// Returns true if the node is expanded and children should be rendered.
    /// </summary>
    public static bool BeginNode(string label, bool defaultOpen = false)
    {
        int nodeId = Im.Context.GetId(label);
        return BeginNodeInternal(nodeId, label, defaultOpen, hasChildren: true);
    }

    /// <summary>
    /// Begin a tree node with explicit ID.
    /// </summary>
    public static bool BeginNode(int id, string label, bool defaultOpen = false)
    {
        return BeginNodeInternal(id, label, defaultOpen, hasChildren: true);
    }

    /// <summary>
    /// Begin a tree node with an icon and label rendered separately (avoids allocating a combined label string).
    /// Returns true if the node is expanded and children should be rendered.
    /// </summary>
    public static bool BeginNodeIconText(int id, ReadOnlySpan<char> icon, ReadOnlySpan<char> text, bool defaultOpen = false, float iconTextGap = 6f)
    {
        return BeginNodeIconTextInternal(id, icon, text, defaultOpen, iconTextGap);
    }

    /// <summary>
    /// Draw a leaf node (no children, no expand arrow).
    /// </summary>
    public static bool Leaf(string label)
    {
        int nodeId = Im.Context.GetId(label);
        return LeafInternal(nodeId, label);
    }

    /// <summary>
    /// Draw a leaf node with explicit ID.
    /// </summary>
    public static bool Leaf(int id, string label)
    {
        return LeafInternal(id, label);
    }

    /// <summary>
    /// Draw a leaf node with an icon and label rendered separately (avoids allocating a combined label string).
    /// </summary>
    public static bool LeafIconText(int id, ReadOnlySpan<char> icon, ReadOnlySpan<char> text, float iconTextGap = 6f)
    {
        return LeafIconTextInternal(id, icon, text, iconTextGap);
    }

    /// <summary>
    /// End a tree node. Only call if BeginNode() returned true.
    /// </summary>
    public static void EndNode()
    {
        _currentContext.Depth--;
    }

    /// <summary>
    /// Check if a node is expanded.
    /// </summary>
    public static bool IsExpanded(string label)
    {
        int nodeId = Im.Context.GetId(label);
        int idx = FindState(nodeId);
        return idx >= 0 && _nodeStates[idx].Expanded;
    }

    /// <summary>
    /// Set node expanded state programmatically.
    /// </summary>
    public static void SetExpanded(string label, bool expanded)
    {
        int nodeId = Im.Context.GetId(label);
        SetExpanded(nodeId, expanded);
    }

    /// <summary>
    /// Set node expanded state by ID.
    /// </summary>
    public static void SetExpanded(int nodeId, bool expanded)
    {
        int idx = FindOrCreateState(nodeId);
        _nodeStates[idx].Expanded = expanded;
    }

    /// <summary>
    /// Get the total height used by the tree so far.
    /// </summary>
    public static float GetTreeHeight()
    {
        return _currentContext.CurrentY - _currentContext.Y;
    }

    /// <summary>
    /// Reset all tree state (expanded nodes, selection).
    /// </summary>
    public static void ResetState()
    {
        _nodeStateCount = 0;
        _selectedNodeId = 0;
        _hoveredNodeId = 0;
    }

    private static int FindState(int id)
    {
        for (int i = 0; i < _nodeStateCount; i++)
        {
            if (_nodeStateIds[i] == id) return i;
        }
        return -1;
    }

    private static int FindOrCreateState(int id)
    {
        int idx = FindState(id);
        if (idx >= 0) return idx;

        if (_nodeStateCount >= 256) return 0; // Fallback

        idx = _nodeStateCount++;
        _nodeStateIds[idx] = id;
        _nodeStates[idx] = default;
        return idx;
    }

    private static bool BeginNodeInternal(int nodeId, string label, bool defaultOpen, bool hasChildren)
    {
        LastNodeRightClicked = false;

        // Get or create state
        int previousStateCount = _nodeStateCount;
        int stateIdx = FindOrCreateState(nodeId);
        ref var state = ref _nodeStates[stateIdx];
        bool stateWasCreated = stateIdx == previousStateCount;

        // Initialize with default only the first time this node is seen.
        if (stateWasCreated)
        {
            state.Expanded = defaultOpen;
        }

        float x = _currentContext.X;
        float y = _currentContext.CurrentY;
        float width = _currentContext.Width;
        float indent = _currentContext.Depth * IndentWidth;

        // Row rect
        var rowRect = new ImRect(x, y, width, RowHeight);
        LastNodeRect = rowRect;

        // Content rect (indented)
        float contentX = x + indent;
        float iconX = contentX + IconPadding;
        float textX = contentX + IconSize + IconPadding * 2;

        // Hit testing
        bool hovered = rowRect.Contains(Im.MousePos);
        bool clicked = hovered && Im.MousePressed;
        bool rightClicked = hovered && Im.Context.Input.MouseRightPressed;

        // Check if clicking on expand icon specifically
        var iconRect = new ImRect(iconX - 2, y + (RowHeight - IconSize) / 2 - 2, IconSize + 4, IconSize + 4);
        bool iconClicked = iconRect.Contains(Im.MousePos) && Im.MousePressed;

        // Update hover state
        if (hovered)
            _hoveredNodeId = nodeId;
        else if (_hoveredNodeId == nodeId)
            _hoveredNodeId = 0;

        // Draw selection/hover background
        bool isSelected = _selectedNodeId == nodeId;
        if (isSelected)
        {
            Im.DrawRect(x, y, width, RowHeight, Im.Style.Active);
        }
        else if (hovered)
        {
            Im.DrawRect(x, y, width, RowHeight, Im.Style.Hover);
        }

        // Draw indent lines
        if (ShowIndentLines && _currentContext.Depth > 0)
        {
            for (int i = 0; i < _currentContext.Depth; i++)
            {
                float lineX = x + i * IndentWidth + IndentWidth / 2;
                Im.DrawLine(lineX, y, lineX, y + RowHeight, 1f, 0x40FFFFFF);
            }
        }

        // Draw expand/collapse icon for nodes with children
        if (hasChildren)
        {
            DrawNodeChevronGlyph(iconX, y, state.Expanded);
        }

        // Draw label
        float textY = y + (RowHeight - Im.Style.FontSize) / 2;
        uint textColor = isSelected ? 0xFFFFFFFF : Im.Style.TextPrimary;
        Im.Text(label.AsSpan(), textX, textY, Im.Style.FontSize, textColor);

        // Handle input
        if (hasChildren && iconClicked)
        {
            // Toggle expansion
            state.Expanded = !state.Expanded;
        }
        else if (clicked && !iconClicked)
        {
            // Select node
            _currentContext.SelectionChanged = true;
            _currentContext.NewSelectedId = nodeId;
        }

        if (rightClicked)
        {
            LastNodeRightClicked = true;
            _currentContext.SelectionChanged = true;
            _currentContext.NewSelectedId = nodeId;
        }

        // Advance Y
        _currentContext.CurrentY += RowHeight;
        _currentContext.LastNodeId = nodeId;

        // If expanded, increment depth for children
        if (state.Expanded)
        {
            _currentContext.Depth++;
            return true;
        }

        return false;
    }

    private static bool BeginNodeIconTextInternal(int nodeId, ReadOnlySpan<char> icon, ReadOnlySpan<char> text, bool defaultOpen, float iconTextGap)
    {
        LastNodeRightClicked = false;

        // Get or create state
        int previousStateCount = _nodeStateCount;
        int stateIdx = FindOrCreateState(nodeId);
        ref var state = ref _nodeStates[stateIdx];
        bool stateWasCreated = stateIdx == previousStateCount;

        // Initialize with default only the first time this node is seen.
        if (stateWasCreated)
        {
            state.Expanded = defaultOpen;
        }

        float x = _currentContext.X;
        float y = _currentContext.CurrentY;
        float width = _currentContext.Width;
        float indent = _currentContext.Depth * IndentWidth;

        // Row rect
        var rowRect = new ImRect(x, y, width, RowHeight);
        LastNodeRect = rowRect;

        // Content rect (indented)
        float contentX = x + indent;
        float chevronX = contentX + IconPadding;
        float labelStartX = contentX + IconSize + IconPadding * 2f;

        // Hit testing
        bool hovered = rowRect.Contains(Im.MousePos);
        bool clicked = hovered && Im.MousePressed;
        bool rightClicked = hovered && Im.Context.Input.MouseRightPressed;

        // Check if clicking on expand icon specifically
        var iconRect = new ImRect(chevronX - 2, y + (RowHeight - IconSize) / 2 - 2, IconSize + 4, IconSize + 4);
        bool iconClicked = iconRect.Contains(Im.MousePos) && Im.MousePressed;

        // Update hover state
        if (hovered)
        {
            _hoveredNodeId = nodeId;
        }
        else if (_hoveredNodeId == nodeId)
        {
            _hoveredNodeId = 0;
        }

        // Draw selection/hover background
        bool isSelected = _selectedNodeId == nodeId;
        if (isSelected)
        {
            Im.DrawRect(x, y, width, RowHeight, Im.Style.Active);
        }
        else if (hovered)
        {
            Im.DrawRect(x, y, width, RowHeight, Im.Style.Hover);
        }

        // Draw indent lines
        if (ShowIndentLines && _currentContext.Depth > 0)
        {
            for (int i = 0; i < _currentContext.Depth; i++)
            {
                float lineX = x + i * IndentWidth + IndentWidth / 2;
                Im.DrawLine(lineX, y, lineX, y + RowHeight, 1f, 0x40FFFFFF);
            }
        }

        // Draw expand/collapse chevron
        DrawNodeChevronGlyph(chevronX, y, state.Expanded);

        // Draw icon + label
        float textY = y + (RowHeight - Im.Style.FontSize) / 2;
        uint textColor = isSelected ? 0xFFFFFFFF : Im.Style.TextPrimary;
        float iconFontSize = Im.Style.FontSize - 1f;
        if (icon.Length > 0)
        {
            Im.Text(icon, labelStartX, textY, iconFontSize, Im.Style.TextSecondary);
        }

        float iconWidth = icon.Length > 0 ? Im.MeasureTextWidth(icon, iconFontSize) : 0f;
        float labelX = labelStartX + iconWidth + (icon.Length > 0 ? iconTextGap : 0f);
        Im.Text(text, labelX, textY, Im.Style.FontSize, textColor);

        // Handle input
        if (iconClicked)
        {
            state.Expanded = !state.Expanded;
        }
        else if (clicked)
        {
            _currentContext.SelectionChanged = true;
            _currentContext.NewSelectedId = nodeId;
        }

        if (rightClicked)
        {
            LastNodeRightClicked = true;
            _currentContext.SelectionChanged = true;
            _currentContext.NewSelectedId = nodeId;
        }

        // Advance Y
        _currentContext.CurrentY += RowHeight;
        _currentContext.LastNodeId = nodeId;

        // If expanded, increment depth for children
        if (state.Expanded)
        {
            _currentContext.Depth++;
            return true;
        }

        return false;
    }

    private static void DrawNodeChevronGlyph(float iconX, float rowY, bool expanded)
    {
        // Keep chevron size consistent with other UI chevrons (e.g., sidebar section headers).
        const float chevronSize = 10f;
        float iconBoxY = rowY + (RowHeight - IconSize) * 0.5f;
        float chevronX = iconX + (IconSize - chevronSize) * 0.5f;
        float chevronY = iconBoxY + (IconSize - chevronSize) * 0.5f;
        ImIcons.DrawChevron(
            chevronX,
            chevronY,
            chevronSize,
            expanded ? ImIcons.ChevronDirection.Down : ImIcons.ChevronDirection.Right,
            Im.Style.TextSecondary);
    }

    private static bool LeafInternal(int nodeId, string label)
    {
        float x = _currentContext.X;
        float y = _currentContext.CurrentY;
        float width = _currentContext.Width;
        float indent = _currentContext.Depth * IndentWidth;

        // Row rect
        var rowRect = new ImRect(x, y, width, RowHeight);

        // Content rect (indented, no icon space needed for leaf)
        float textX = x + indent + IconSize + IconPadding * 2;

        // Hit testing
        bool hovered = rowRect.Contains(Im.MousePos);
        bool clicked = hovered && Im.MousePressed;

        // Update hover state
        if (hovered)
            _hoveredNodeId = nodeId;
        else if (_hoveredNodeId == nodeId)
            _hoveredNodeId = 0;

        // Draw selection/hover background
        bool isSelected = _selectedNodeId == nodeId;
        if (isSelected)
        {
            Im.DrawRect(x, y, width, RowHeight, Im.Style.Active);
        }
        else if (hovered)
        {
            Im.DrawRect(x, y, width, RowHeight, Im.Style.Hover);
        }

        // Draw indent lines
        if (ShowIndentLines && _currentContext.Depth > 0)
        {
            for (int i = 0; i < _currentContext.Depth; i++)
            {
                float lineX = x + i * IndentWidth + IndentWidth / 2;
                Im.DrawLine(lineX, y, lineX, y + RowHeight, 1f, 0x40FFFFFF);
            }
        }

        if (ShowLeafBullet)
        {
            // Draw bullet/dot for leaf
            float bulletX = x + indent + IconPadding + IconSize / 2;
            float bulletY = y + RowHeight / 2;
            float bulletRadius = 2f;
            Im.DrawCircle(bulletX, bulletY, bulletRadius, Im.Style.TextSecondary);
        }

        // Draw label
        float textY = y + (RowHeight - Im.Style.FontSize) / 2;
        uint textColor = isSelected ? 0xFFFFFFFF : Im.Style.TextPrimary;
        Im.Text(label.AsSpan(), textX, textY, Im.Style.FontSize, textColor);

        // Handle input
        if (clicked)
        {
            _currentContext.SelectionChanged = true;
            _currentContext.NewSelectedId = nodeId;
        }

        // Advance Y
        _currentContext.CurrentY += RowHeight;
        _currentContext.LastNodeId = nodeId;

        return clicked;
    }

    private static bool LeafIconTextInternal(int nodeId, ReadOnlySpan<char> icon, ReadOnlySpan<char> text, float iconTextGap)
    {
        LastLeafRightClicked = false;

        float x = _currentContext.X;
        float y = _currentContext.CurrentY;
        float width = _currentContext.Width;
        float indent = _currentContext.Depth * IndentWidth;

        // Row rect
        var rowRect = new ImRect(x, y, width, RowHeight);
        LastLeafRect = rowRect;

        // Hit testing
        bool hovered = rowRect.Contains(Im.MousePos);
        bool clicked = hovered && Im.MousePressed;
        bool rightClicked = hovered && Im.Context.Input.MouseRightPressed;

        // Update hover state
        if (hovered)
            _hoveredNodeId = nodeId;
        else if (_hoveredNodeId == nodeId)
            _hoveredNodeId = 0;

        // Draw selection/hover background
        bool isSelected = _selectedNodeId == nodeId;
        if (isSelected)
        {
            Im.DrawRect(x, y, width, RowHeight, Im.Style.Active);
        }
        else if (hovered)
        {
            Im.DrawRect(x, y, width, RowHeight, Im.Style.Hover);
        }

        // Draw indent lines
        if (ShowIndentLines && _currentContext.Depth > 0)
        {
            for (int i = 0; i < _currentContext.Depth; i++)
            {
                float lineX = x + i * IndentWidth + IndentWidth / 2;
                Im.DrawLine(lineX, y, lineX, y + RowHeight, 1f, 0x40FFFFFF);
            }
        }

        float textY = y + (RowHeight - Im.Style.FontSize) / 2;
        uint textColor = isSelected ? 0xFFFFFFFF : Im.Style.TextPrimary;

        float iconX = x + indent + IconPadding;
        float iconFontSize = Im.Style.FontSize - 1f;
        if (icon.Length > 0)
        {
            Im.Text(icon, iconX, textY, iconFontSize, Im.Style.TextSecondary);
        }

        float iconWidth = icon.Length > 0 ? Im.MeasureTextWidth(icon, iconFontSize) : 0f;
        float labelX = iconX + iconWidth + iconTextGap;
        Im.Text(text, labelX, textY, Im.Style.FontSize, textColor);

        // Handle input
        if (clicked)
        {
            _currentContext.SelectionChanged = true;
            _currentContext.NewSelectedId = nodeId;
        }

        if (rightClicked)
        {
            LastLeafRightClicked = true;
            LastLeafRect = rowRect;
            // Also select the right-clicked item
            _currentContext.SelectionChanged = true;
            _currentContext.NewSelectedId = nodeId;
        }

        // Advance Y
        _currentContext.CurrentY += RowHeight;
        _currentContext.LastNodeId = nodeId;

        return clicked;
    }

}
