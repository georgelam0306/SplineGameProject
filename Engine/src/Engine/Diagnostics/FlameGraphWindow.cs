using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using FlameProfiler;

namespace DerpLib.Diagnostics;

/// <summary>
/// Icicle-style flame graph profiler window.
/// Displays hierarchical timing data with parent-child relationships.
/// Root at top, children grow downward.
/// </summary>
public static class FlameGraphWindow
{
    public static bool Visible { get; set; }

    public static void Toggle() => Visible = !Visible;

    private const float DefaultWindowWidth = 600f;
    private const float DefaultWindowHeight = 400f;

    private const float RowHeight = 20f;
    private const float MinNodeWidth = 2f; // Minimum width to render a node
    private const float HeaderHeight = 24f;
    private const float FooterHeight = 20f;

    // Colors (ABGR uint format)
    private const uint BackgroundColor = 0xF0141419;
    private const uint NodeBorderColor = 0xFF1A1A1A;
    private const uint TextColor = 0xFFE0E0E0;
    private const uint TextDimColor = 0xFF999999;
    private const uint HoverColor = 0x40FFFFFF;
    private const uint SelectedColor = 0x60FFD700;

    // Scope colors - warm gradient from blue (fast) to red (slow)
    private static readonly uint[] HeatColors =
    [
        0xFF40B040, // Fast (green)
        0xFF40C080,
        0xFF40D0C0,
        0xFF40C0E0,
        0xFF40A0E0,
        0xFF6080E0,
        0xFF8060E0,
        0xFFA040E0,
        0xFFC040C0,
        0xFFE04080, // Slow (red)
    ];

    // Scope colors by index (for consistent coloring)
    private static readonly uint[] ScopeColors =
    [
        0xFFE07040, 0xFF40C060, 0xFF40B0E0, 0xFFE040A0,
        0xFF40E0E0, 0xFFE08040, 0xFF8040E0, 0xFF40E080,
        0xFFA0A0A0, 0xFFC06040, 0xFF60C0A0, 0xFFA060E0,
        0xFF60A0E0, 0xFFE0C040, 0xFF80E060, 0xFFE06080,
    ];

    // State
    private static readonly FlameAggregate _aggregate = new();
    private static FlameAggregateNode? _hoveredNode;
    private static FlameAggregateNode? _selectedNode; // For zoom
    private static ulong _selectedNodePathHash;
    private static bool _hasSelectedNode;
    private static int _aggregateFrameCount = 60;
    private static bool _frozen;
    private static int _lastUpdateFrameNumber;

    public static void Draw()
    {
        if (!Visible)
        {
            return;
        }

        var service = FlameProfilerService.Instance;
        if (service == null)
        {
            return;
        }

        int latestFrameNumber = service.GetLatestFrame()?.FrameNumber ?? -1;

        // Update aggregate periodically (not every frame to save CPU)
        bool shouldRebuildAggregate = !_frozen
            && service.ValidFrameCount > 0
            && (_aggregate.AllNodes.Count == 0 || (latestFrameNumber - _lastUpdateFrameNumber) >= 2);

        if (shouldRebuildAggregate)
        {
            _aggregate.Build(service, _aggregateFrameCount);
            _lastUpdateFrameNumber = latestFrameNumber;

            // Re-resolve selected node after rebuild (nodes are pooled/reused).
            if (_hasSelectedNode)
            {
                _selectedNode = _aggregate.FindByPathHash(_selectedNodePathHash);
                if (_selectedNode == null)
                {
                    _hasSelectedNode = false;
                    _selectedNodePathHash = 0;
                }
            }
        }

        float windowX = 20;
        float windowY = 50;

        if (!Im.BeginWindow("Flame Graph", windowX, windowY, DefaultWindowWidth, DefaultWindowHeight))
        {
            Im.EndWindow();
            return;
        }

        DrawHeader(service);
        ImLayout.Space(4);

        // Calculate graph area
        float graphHeight = DefaultWindowHeight - HeaderHeight - FooterHeight - 40;
        var graphRect = ImLayout.AllocateRect(0, graphHeight);

        DrawFlameGraph(graphRect, service);

        ImLayout.Space(4);
        DrawFooter();

        Im.EndWindow();

        // Reset hover each frame
        _hoveredNode = null;
    }

    private static void DrawHeader(FlameProfilerService service)
    {
        double frameMs = _aggregate.AverageFrameMs;
        double fps = frameMs > 0 ? 1000.0 / frameMs : 0;

        Im.Label($"Frame: {frameMs:F2}ms ({fps:F0} FPS) | Depth: {_aggregate.MaxDepth} | Nodes: {_aggregate.AllNodes.Count}");
    }

    private static void DrawFlameGraph(ImRect rect, FlameProfilerService service)
    {
        float x = rect.X;
        float y = rect.Y;
        float width = rect.Width;
        float height = rect.Height;

        // Background
        DrawRectWindowLocal(x, y, width, height, BackgroundColor);

        if (_aggregate.AllNodes.Count == 0)
        {
            Im.Text("No flame data. Ensure FlameProfilerService.BeginFrame/EndFrame runs and code uses ProfileScope.Begin(scopeId).",
                x + 10, y + height * 0.5f - 6, 11f, TextDimColor);
            return;
        }

        var mouse = Im.WindowMousePos;
        bool mouseInGraph = mouse.X >= x && mouse.X < x + width && mouse.Y >= y && mouse.Y < y + height;

        // Determine zoom root
        FlameAggregateNode? zoomRoot = _selectedNode;
        double zoomLeft = 0;
        double zoomWidth = 1;

        if (zoomRoot != null)
        {
            zoomLeft = zoomRoot.NormalizedLeft;
            zoomWidth = zoomRoot.NormalizedWidth;
        }

        // Calculate visible depth range
        int startDepth = zoomRoot?.Depth ?? 0;
        int maxVisibleRows = (int)(height / RowHeight);

        // Draw nodes by depth
        for (int depth = startDepth; depth <= _aggregate.MaxDepth && (depth - startDepth) < maxVisibleRows; depth++)
        {
            float rowY = y + (depth - startDepth) * RowHeight;

            foreach (var node in _aggregate.GetNodesAtDepth(depth))
            {
                // Skip nodes outside zoom area
                if (zoomRoot != null && !IsDescendantOf(node, zoomRoot) && node != zoomRoot)
                {
                    continue;
                }

                // Calculate screen position
                double nodeLeft = (node.NormalizedLeft - zoomLeft) / zoomWidth;
                double nodeWidth = node.NormalizedWidth / zoomWidth;

                float nodeX = x + (float)(nodeLeft * width);
                float nodeW = (float)(nodeWidth * width);

                // Skip if too small
                if (nodeW < MinNodeWidth)
                {
                    continue;
                }

                // Skip if off-screen
                if (nodeX + nodeW < x || nodeX > x + width)
                {
                    continue;
                }

                // Clamp to visible area
                float clampedX = Math.Max(nodeX, x);
                float clampedW = Math.Min(nodeX + nodeW, x + width) - clampedX;

                DrawNode(clampedX, rowY, clampedW, RowHeight - 2, node, service);

                // Check hover
                if (mouseInGraph &&
                    mouse.X >= clampedX && mouse.X < clampedX + clampedW &&
                    mouse.Y >= rowY && mouse.Y < rowY + RowHeight)
                {
                    _hoveredNode = node;
                }
            }
        }

        // Handle click to zoom
        if (mouseInGraph && Im.MousePressed)
        {
            if (_hoveredNode != null)
            {
                if (_hoveredNode == _selectedNode)
                {
                    // Click on already selected node - zoom out to parent
                    _selectedNode = _selectedNode.Parent;
                    if (_selectedNode != null)
                    {
                        _hasSelectedNode = true;
                        _selectedNodePathHash = _selectedNode.PathHash;
                    }
                    else
                    {
                        _hasSelectedNode = false;
                        _selectedNodePathHash = 0;
                    }
                }
                else
                {
                    // Click on new node - zoom in
                    _selectedNode = _hoveredNode;
                    _hasSelectedNode = true;
                    _selectedNodePathHash = _selectedNode.PathHash;
                }
            }
        }

        // Right-click to reset zoom
        if (mouseInGraph && Im.Context.Input.MouseRightPressed)
        {
            _selectedNode = null;
            _hasSelectedNode = false;
            _selectedNodePathHash = 0;
        }

        // Draw tooltip for hovered node
        if (_hoveredNode != null)
        {
            DrawTooltip(mouse.X, mouse.Y, _hoveredNode);
        }
    }

    private static void DrawNode(float x, float y, float width, float height, FlameAggregateNode node, FlameProfilerService service)
    {
        // Color based on scope index for consistency
        uint color = ScopeColors[node.ScopeId % ScopeColors.Length];

        // Darken color slightly based on depth for visual separation
        float darken = 1.0f - node.Depth * 0.03f;
        darken = Math.Max(darken, 0.6f);
        color = DarkenColor(color, darken);

        // Draw background
        DrawRectWindowLocal(x, y, width, height, color);

        // Draw border
        DrawRectOutlineWindowLocal(x, y, width, height, NodeBorderColor);

        // Draw hover highlight
        if (node == _hoveredNode)
        {
            DrawRectWindowLocal(x, y, width, height, HoverColor);
        }

        // Draw selection highlight
        if (node == _selectedNode)
        {
            DrawRectOutlineWindowLocal(x + 1, y + 1, width - 2, height - 2, SelectedColor);
        }

        // Draw text if there's room
        if (width > 40)
        {
            float textX = x + 4;
            float textY = y + 3;

            ReadOnlySpan<char> label = node.Name.AsSpan();
            int maxChars = (int)((width - 8) / 6f);
            if (maxChars > 0)
            {
                if (label.Length <= maxChars)
                {
                    Im.Text(label, textX, textY, 10f, TextColor);
                }
                else if (maxChars >= 2)
                {
                    ReadOnlySpan<char> prefix = label.Slice(0, maxChars - 2);
                    Im.Text(prefix, textX, textY, 10f, TextColor);

                    float prefixWidth = ImTextMetrics.MeasureWidth(Im.Context.Font, prefix, 10f);
                    Im.Text("..".AsSpan(), textX + prefixWidth, textY, 10f, TextColor);
                }
            }
        }
    }

    private static void DrawTooltip(float mouseX, float mouseY, FlameAggregateNode node)
    {
        float tooltipWidth = 280f;
        float tooltipHeight = 70f;
        float tooltipX = mouseX + 15;
        float tooltipY = mouseY + 15;

        // Keep tooltip on screen
        float screenW = Derp.GetScreenWidth();
        float screenH = Derp.GetScreenHeight();
        if (tooltipX + tooltipWidth > screenW)
        {
            tooltipX = mouseX - tooltipWidth - 5;
        }
        if (tooltipY + tooltipHeight > screenH)
        {
            tooltipY = mouseY - tooltipHeight - 5;
        }

        // Background
        DrawRectWindowLocal(tooltipX, tooltipY, tooltipWidth, tooltipHeight, 0xF0202020);
        DrawRectOutlineWindowLocal(tooltipX, tooltipY, tooltipWidth, tooltipHeight, 0xFF404040);

        float textY = tooltipY + 4;
        float lineHeight = 13f;

        // Path
        DrawPathLine(tooltipX + 4, textY, node);
        textY += lineHeight;

        // Self time vs total time
        Im.Text($"Self: {node.SelfMs:F2}ms | Total: {node.TotalMs:F2}ms",
            tooltipX + 4, textY, 10f, TextDimColor);
        textY += lineHeight;

        // Percentage
        Im.Text($"{node.PercentOfFrame:F1}% of frame | {node.PercentOfParent:F1}% of parent",
            tooltipX + 4, textY, 10f, TextDimColor);
        textY += lineHeight;

        // Min/Max
        Im.Text($"Min: {node.MinTotalMs:F2}ms | Max: {node.MaxTotalMs:F2}ms",
            tooltipX + 4, textY, 10f, TextDimColor);
    }

    private static void DrawFooter()
    {
        if (_selectedNode != null)
        {
            Im.Label($"Zoomed: {_selectedNode.Name} (click to zoom, right-click to reset){(_frozen ? " [FROZEN]" : "")}");
        }
        else
        {
            Im.Label($"Click to zoom, right-click to reset{(_frozen ? " [FROZEN]" : "")}");
        }
    }

    private static void DrawPathLine(float x, float y, FlameAggregateNode node)
    {
        int count = 0;
        var current = node;
        while (current != null && count < FlameProfiler.FlameFrame.MaxDepth)
        {
            PathScratch[count++] = current;
            current = current.Parent;
        }

        float cursorX = x;
        for (int i = count - 1; i >= 0; i--)
        {
            var part = PathScratch[i];
            if (part == null)
            {
                continue;
            }

            ReadOnlySpan<char> name = part.Name.AsSpan();
            Im.Text(name, cursorX, y, 10f, TextColor);
            cursorX += ImTextMetrics.MeasureWidth(Im.Context.Font, name, 10f);

            if (i > 0)
            {
                ReadOnlySpan<char> sep = " > ".AsSpan();
                Im.Text(sep, cursorX, y, 10f, TextDimColor);
                cursorX += ImTextMetrics.MeasureWidth(Im.Context.Font, sep, 10f);
            }
        }

        // Clear used scratch entries (avoid keeping refs alive longer than needed)
        for (int i = 0; i < count; i++)
        {
            PathScratch[i] = null;
        }
    }

    private static bool IsDescendantOf(FlameAggregateNode node, FlameAggregateNode ancestor)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current == ancestor)
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

    private static void DrawRectWindowLocal(float x, float y, float w, float h, uint color)
    {
        Im.DrawRect(x, y, w, h, color);
    }

    private static void DrawRectOutlineWindowLocal(float x, float y, float w, float h, uint color)
    {
        DrawRectOutline(x, y, w, h, color);
    }

    private static void DrawRectOutline(float x, float y, float w, float h, uint color)
    {
        // Draw 4 lines to form a rectangle outline
        Im.DrawLine(x, y, x + w, y, 1f, color);         // Top
        Im.DrawLine(x + w, y, x + w, y + h, 1f, color); // Right
        Im.DrawLine(x + w, y + h, x, y + h, 1f, color); // Bottom
        Im.DrawLine(x, y + h, x, y, 1f, color);         // Left
    }

    private static uint DarkenColor(uint colorAbgr, float factor)
    {
        byte a = (byte)(colorAbgr >> 24);
        byte b = (byte)((colorAbgr >> 16) & 0xFF);
        byte g = (byte)((colorAbgr >> 8) & 0xFF);
        byte r = (byte)(colorAbgr & 0xFF);

        r = (byte)(r * factor);
        g = (byte)(g * factor);
        b = (byte)(b * factor);

        return (uint)((a << 24) | (b << 16) | (g << 8) | r);
    }

    /// <summary>
    /// Freeze/unfreeze the flame graph (stop updating).
    /// </summary>
    public static void ToggleFreeze() => _frozen = !_frozen;

    /// <summary>
    /// Reset zoom to show full frame.
    /// </summary>
    public static void ResetZoom()
    {
        _selectedNode = null;
        _hasSelectedNode = false;
        _selectedNodePathHash = 0;
    }

    private static readonly FlameAggregateNode?[] PathScratch = new FlameAggregateNode?[FlameProfiler.FlameFrame.MaxDepth];
}
