using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;

namespace DerpLib.Diagnostics;

/// <summary>
/// Unity-style flat stacked profiler overlay window.
/// Shows per-frame scope bands and an explicit "Other" bucket for uninstrumented work.
/// </summary>
public static class ProfilerWindow
{
    public static bool Visible { get; set; }

    public static void Toggle() => Visible = !Visible;

    private const int HistorySize = 180; // ~3s at 60fps
    private const int MaxGraphedScopes = 12;
    private const int SelectionUpdateIntervalFrames = 15;

    private const float DefaultWindowWidth = 520f;
    private const float DefaultWindowHeight = 480f;

    private const float GraphHeight = 140f;
    private const float LegendHeight = 70f;
    private const float SectionSpacing = 6f;
    private const int MaxLegendItems = 60;

    // Colors (ABGR uint format)
    private const uint GridColor = 0x4D666666;
    private const uint GraphBgColor = 0xF0141419;
    private const uint TextDimColor = 0xFF999999;
    private const uint Target60Color = 0x9900D4FF;
    private const uint Target30Color = 0x994040FF;

    private const uint OtherLineColor = 0xFF808080;
    private const uint OtherFillColor = 0x4D808080;

    // Scope colors - stable mapping by scope index
    private static readonly uint[] ScopeColors =
    [
        0xFFE07040,
        0xFF40C060,
        0xFF40B0E0,
        0xFFE040A0,
        0xFF40E0E0,
        0xFFE08040,
        0xFF8040E0,
        0xFF40E080,
        0xFFA0A0A0,
        0xFFC06040,
        0xFF60C0A0,
        0xFFA060E0,
        0xFF60A0E0,
        0xFFE0C040,
        0xFF80E060,
        0xFFE06080,
    ];

    private static readonly uint[] ScopeFillColors =
    [
        0x4DE07040,
        0x4D40C060,
        0x4D40B0E0,
        0x4DE040A0,
        0x4D40E0E0,
        0x4DE08040,
        0x4D8040E0,
        0x4D40E080,
        0x4DA0A0A0,
        0x4DC06040,
        0x4D60C0A0,
        0x4DA060E0,
        0x4D60A0E0,
        0x4DE0C040,
        0x4D80E060,
        0x4DE06080,
    ];

    // CPU state
    private static ImGraph[] _cpuScopeGraphs = Array.Empty<ImGraph>();
    private static ImGraph? _cpuOtherGraph;
    private static string[] _cpuScopeNames = Array.Empty<string>();
    private static bool[] _cpuScopeEnabled = Array.Empty<bool>();
    private static bool _cpuOtherEnabled = true;

    // GPU state
    private static ImGraph[] _gpuScopeGraphs = Array.Empty<ImGraph>();
    private static ImGraph? _gpuOtherGraph;
    private static string[] _gpuScopeNames = Array.Empty<string>();
    private static bool[] _gpuScopeEnabled = Array.Empty<bool>();
    private static bool _gpuOtherEnabled = true;

    // Scratch buffers (resized only when scope count/history count changes)
    private static float[] _bandValues = Array.Empty<float>();        // [bandIndex * historyCount + sample]
    private static float[] _stackedTotals = Array.Empty<float>();     // [historyCount]
    private static float[] _baseline = Array.Empty<float>();          // [historyCount]
    private static float[] _topValues = Array.Empty<float>();         // [historyCount]
    private static Vector2[] _polygonPoints = Array.Empty<Vector2>(); // [historyCount * 2]
    private static int[] _selectedScopeIndices = Array.Empty<int>();  // [scopeCount]

    // Selection caches to avoid per-frame scope churn (prevents flicker).
    private static int _cpuSelectionCountdown;
    private static int[] _cpuSelectedScopeIndices = Array.Empty<int>(); // [MaxGraphedScopes]
    private static int _cpuSelectedScopeCount;
    private static int _gpuSelectionCountdown;
    private static int[] _gpuSelectedScopeIndices = Array.Empty<int>(); // [MaxGraphedScopes]
    private static int _gpuSelectedScopeCount;

    public static void Draw(in ProfilerStats stats)
    {
        if (!Visible)
        {
            return;
        }

        UpdateHistories(in stats);

        float windowX = Derp.GetScreenWidth() - DefaultWindowWidth - 20;
        float windowY = 50;

        if (!Im.BeginWindow("Profiler", windowX, windowY, DefaultWindowWidth, DefaultWindowHeight))
        {
            Im.EndWindow();
            return;
        }

        double cpuMs = stats.CpuFrameMs;
        double gpuMs = stats.GpuFrameMs;
        double frameMs = Math.Max(cpuMs, gpuMs);
        double fps = frameMs > 0 ? 1000.0 / frameMs : 0;

        Im.Label($"Frame: {frameMs:F2}ms ({fps:F0} FPS) | CPU: {cpuMs:F2}ms | GPU: {gpuMs:F2}ms");
        ImLayout.Space(SectionSpacing);

        // CPU section
        if (stats.HasCpuData)
        {
            Im.Label($"CPU {stats.CpuFrameMs:F2}ms (scoped {stats.CpuScopedMs:F2}ms, other {stats.CpuOtherMs:F2}ms)");

            var cpuGraphRect = ImLayout.AllocateRect(0, GraphHeight);
            DrawStackedGraph(cpuGraphRect, _cpuScopeGraphs, _cpuOtherGraph!, _cpuScopeEnabled, _cpuOtherEnabled, isCpu: true);

            ImLayout.Space(2);

            var cpuLegendRect = ImLayout.AllocateRect(0, LegendHeight);
            DrawLegend(cpuLegendRect, _cpuScopeNames, stats.CpuScopeMs, stats.CpuOtherMs, _cpuScopeEnabled, ref _cpuOtherEnabled);

            ImLayout.Space(SectionSpacing);
        }

        // GPU section
        if (stats.HasGpuData)
        {
            Im.Label($"GPU {stats.GpuFrameMs:F2}ms (scoped {stats.GpuScopedMs:F2}ms, other {stats.GpuOtherMs:F2}ms)");

            var gpuGraphRect = ImLayout.AllocateRect(0, GraphHeight);
            DrawStackedGraph(gpuGraphRect, _gpuScopeGraphs, _gpuOtherGraph!, _gpuScopeEnabled, _gpuOtherEnabled, isCpu: false);

            ImLayout.Space(2);

            var gpuLegendRect = ImLayout.AllocateRect(0, LegendHeight);
            DrawLegend(gpuLegendRect, _gpuScopeNames, stats.GpuScopeMs, stats.GpuOtherMs, _gpuScopeEnabled, ref _gpuOtherEnabled);

            ImLayout.Space(SectionSpacing);
        }

        DrawFooter(in stats);

        Im.EndWindow();
    }

    private static void UpdateHistories(in ProfilerStats stats)
    {
        if (stats.HasCpuData)
        {
            EnsureCpuScopes(stats.CpuScopeNames);

            double scopedSum = 0;
            int count = Math.Min(stats.CpuScopeMs.Length, _cpuScopeGraphs.Length);
            for (int i = 0; i < count; i++)
            {
                double ms = stats.CpuScopeMs[i];
                _cpuScopeGraphs[i].Push((float)ms);
                scopedSum += ms;
            }

            double frameMs = stats.CpuFrameMs;
            double otherMs = frameMs - scopedSum;
            if (otherMs < 0)
            {
                otherMs = 0;
            }

            _cpuOtherGraph!.Push((float)otherMs);
        }

        if (stats.HasGpuData)
        {
            EnsureGpuScopes(stats.GpuScopeNames);

            double scopedSum = 0;
            int count = Math.Min(stats.GpuScopeMs.Length, _gpuScopeGraphs.Length);
            for (int i = 0; i < count; i++)
            {
                double ms = stats.GpuScopeMs[i];
                _gpuScopeGraphs[i].Push((float)ms);
                scopedSum += ms;
            }

            double frameMs = stats.GpuFrameMs;
            double otherMs = frameMs - scopedSum;
            if (otherMs < 0)
            {
                otherMs = 0;
            }

            _gpuOtherGraph!.Push((float)otherMs);
        }
    }

    private static void EnsureCpuScopes(ReadOnlySpan<string> scopeNames)
    {
        if (ScopeNamesMatch(_cpuScopeNames, scopeNames))
        {
            return;
        }

        _cpuScopeNames = CopyScopeNames(scopeNames);
        _cpuScopeGraphs = new ImGraph[_cpuScopeNames.Length];
        _cpuScopeEnabled = new bool[_cpuScopeNames.Length];
        for (int i = 0; i < _cpuScopeGraphs.Length; i++)
        {
            _cpuScopeGraphs[i] = new ImGraph(HistorySize, 0f, 0f);
            _cpuScopeEnabled[i] = true;
        }

        _cpuOtherGraph = new ImGraph(HistorySize, 0f, 0f);
        _cpuOtherEnabled = true;
        ResetScratch();
    }

    private static void EnsureGpuScopes(ReadOnlySpan<string> scopeNames)
    {
        if (ScopeNamesMatch(_gpuScopeNames, scopeNames))
        {
            return;
        }

        _gpuScopeNames = CopyScopeNames(scopeNames);
        _gpuScopeGraphs = new ImGraph[_gpuScopeNames.Length];
        _gpuScopeEnabled = new bool[_gpuScopeNames.Length];
        for (int i = 0; i < _gpuScopeGraphs.Length; i++)
        {
            _gpuScopeGraphs[i] = new ImGraph(HistorySize, 0f, 0f);
            _gpuScopeEnabled[i] = true;
        }

        _gpuOtherGraph = new ImGraph(HistorySize, 0f, 0f);
        _gpuOtherEnabled = true;
        ResetScratch();
    }

    private static bool ScopeNamesMatch(string[] cached, ReadOnlySpan<string> incoming)
    {
        if (cached.Length != incoming.Length)
        {
            return false;
        }

        for (int i = 0; i < cached.Length; i++)
        {
            if (!string.Equals(cached[i], incoming[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] CopyScopeNames(ReadOnlySpan<string> names)
    {
        if (names.Length == 0)
        {
            return Array.Empty<string>();
        }

        var copy = new string[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            copy[i] = names[i];
        }

        return copy;
    }

    private static void DrawStackedGraph(
        ImRect rect,
        ImGraph[] scopeGraphs,
        ImGraph otherGraph,
        bool[] scopeEnabled,
        bool otherEnabled,
        bool isCpu)
    {
        float x = rect.X;
        float y = rect.Y;
        float width = rect.Width;
        float height = rect.Height;

        DrawRectWindowLocal(x, y, width, height, GraphBgColor);

        int scopeCount = scopeGraphs.Length;
        EnsureScopeScratch(scopeCount);

        var selection = EnsureSelectionCache(isCpu);
        int enabledCount = selection.EnabledCount;
        int selectedCount = selection.SelectedCount;
        int hiddenCount = selection.HiddenCount;
        ReadOnlySpan<int> selected = selection.SelectedIndices;

        int bandCount = selectedCount + 1; // +Other
        int bandStride = HistorySize;

        int historyCount = otherGraph.Count;
        if (historyCount < 2)
        {
            return;
        }

        EnsureScratchCapacity(bandCount);

        // Copy history for each scope (and Other) once, and compute stacked totals.
        var totals = _stackedTotals.AsSpan(0, historyCount);
        totals.Clear();

        for (int bandIndex = 0; bandIndex < bandCount; bandIndex++)
        {
            var dst = _bandValues.AsSpan(bandIndex * bandStride, historyCount);
            dst.Clear();

            if (bandIndex < selectedCount)
            {
                int scopeIndex = selected[bandIndex];
                scopeGraphs[scopeIndex].GetValues(dst);
            }
            else
            {
                if (otherEnabled)
                {
                    otherGraph.GetValues(dst);
                }
            }

            for (int sampleIndex = 0; sampleIndex < historyCount; sampleIndex++)
            {
                totals[sampleIndex] += dst[sampleIndex];
            }
        }

        float maxVal = 0f;
        for (int i = 0; i < historyCount; i++)
        {
            float v = totals[i];
            if (v > maxVal)
            {
                maxVal = v;
            }
        }

        maxVal = Math.Max(maxVal, 20f);
        maxVal = MathF.Ceiling(maxVal / 10f) * 10f;

        DrawGridLines(x, y, width, height, maxVal);
        DrawTargetLine(x, y, width, height, maxVal, 16.67f, Target60Color, "16.7ms");
        DrawTargetLine(x, y, width, height, maxVal, 33.33f, Target30Color, "33.3ms");

        float step = historyCount > 1 ? width / (historyCount - 1) : 0f;
        float range = maxVal;

        var baseline = _baseline.AsSpan(0, historyCount);
        baseline.Clear();

        for (int bandIndex = 0; bandIndex < bandCount; bandIndex++)
        {
            if (bandIndex >= selectedCount)
            {
                if (!otherEnabled)
                {
                    continue;
                }
            }

            var values = _bandValues.AsSpan(bandIndex * bandStride, historyCount);

            uint fillColor;
            uint lineColor;
            if (bandIndex < selectedCount)
            {
                int scopeIndex = selected[bandIndex];
                int colorIndex = scopeIndex % ScopeColors.Length;
                fillColor = ScopeFillColors[colorIndex];
                lineColor = ScopeColors[colorIndex];
            }
            else
            {
                fillColor = OtherFillColor;
                lineColor = OtherLineColor;
            }

            DrawStackedArea(x, y, width, height, step, range, baseline, values, historyCount, fillColor);
            DrawStackedLine(x, y, width, height, step, range, baseline, values, historyCount, lineColor);

            for (int i = 0; i < historyCount; i++)
            {
                baseline[i] += values[i];
            }
        }

        DrawYAxisLabels(x, y, width, height, maxVal);

        if (enabledCount > MaxGraphedScopes)
        {
            Im.Text($"Graph: top {selectedCount}/{enabledCount} scopes (hidden {hiddenCount})", x + 6, y + 4, 10f, TextDimColor);
        }
    }

    private static void EnsureScopeScratch(int scopeCount)
    {
        if (_selectedScopeIndices.Length < scopeCount)
        {
            _selectedScopeIndices = new int[scopeCount];
        }
    }

    private readonly ref struct SelectionResult
    {
        public readonly ReadOnlySpan<int> SelectedIndices;
        public readonly int SelectedCount;
        public readonly int EnabledCount;
        public readonly int HiddenCount;

        public SelectionResult(ReadOnlySpan<int> selectedIndices, int selectedCount, int enabledCount, int hiddenCount)
        {
            SelectedIndices = selectedIndices;
            SelectedCount = selectedCount;
            EnabledCount = enabledCount;
            HiddenCount = hiddenCount;
        }
    }

    private static SelectionResult EnsureSelectionCache(bool isCpu)
    {
        ImGraph[] scopeGraphs = isCpu ? _cpuScopeGraphs : _gpuScopeGraphs;
        bool[] scopeEnabled = isCpu ? _cpuScopeEnabled : _gpuScopeEnabled;

        int scopeCount = scopeGraphs.Length;
        int enabledCount = 0;
        for (int i = 0; i < scopeCount; i++)
        {
            if (i < scopeEnabled.Length && scopeEnabled[i])
            {
                enabledCount++;
            }
        }

        int countdown;
        int selectedCount;
        int[] selectedIndices;

        if (isCpu)
        {
            countdown = _cpuSelectionCountdown;
            selectedCount = _cpuSelectedScopeCount;
            selectedIndices = _cpuSelectedScopeIndices;
        }
        else
        {
            countdown = _gpuSelectionCountdown;
            selectedCount = _gpuSelectedScopeCount;
            selectedIndices = _gpuSelectedScopeIndices;
        }

        int maxToGraph = Math.Min(MaxGraphedScopes, scopeCount);
        if (selectedIndices.Length != maxToGraph)
        {
            selectedIndices = maxToGraph > 0 ? new int[maxToGraph] : Array.Empty<int>();
            selectedCount = 0;
            countdown = 0;
        }

        if (countdown > 0)
        {
            countdown--;
        }

        if (countdown == 0)
        {
            selectedCount = 0;
            var selectedScratch = _selectedScopeIndices.AsSpan(0, scopeCount);
            selectedScratch.Clear();

            for (int pick = 0; pick < maxToGraph; pick++)
            {
                int bestIndex = -1;
                float bestValue = 0f;

                for (int scopeIndex = 0; scopeIndex < scopeCount; scopeIndex++)
                {
                    if (scopeIndex >= scopeEnabled.Length || !scopeEnabled[scopeIndex])
                    {
                        continue;
                    }

                    if (selectedScratch[scopeIndex] != 0)
                    {
                        continue;
                    }

                    float last = scopeGraphs[scopeIndex].Current;
                    if (last > bestValue)
                    {
                        bestValue = last;
                        bestIndex = scopeIndex;
                    }
                }

                if (bestIndex < 0)
                {
                    break;
                }

                selectedScratch[bestIndex] = 1;
                selectedIndices[selectedCount++] = bestIndex;
            }

            countdown = SelectionUpdateIntervalFrames;
        }

        if (isCpu)
        {
            _cpuSelectionCountdown = countdown;
            _cpuSelectedScopeCount = selectedCount;
            _cpuSelectedScopeIndices = selectedIndices;
        }
        else
        {
            _gpuSelectionCountdown = countdown;
            _gpuSelectedScopeCount = selectedCount;
            _gpuSelectedScopeIndices = selectedIndices;
        }

        int hiddenCount = Math.Max(0, enabledCount - selectedCount);
        return new SelectionResult(selectedIndices, selectedCount, enabledCount, hiddenCount);
    }

    private static void DrawStackedArea(
        float x,
        float y,
        float w,
        float h,
        float step,
        float range,
        ReadOnlySpan<float> baseline,
        ReadOnlySpan<float> values,
        int count,
        uint color)
    {
        int pointCount = count * 2;
        var points = _polygonPoints.AsSpan(0, pointCount);

        // Top edge (left to right)
        for (int i = 0; i < count; i++)
        {
            float topVal = baseline[i] + values[i];
            float normalized = topVal / range;
            normalized = Math.Clamp(normalized, 0f, 1f);
            float px = x + i * step;
            float py = y + h - normalized * h;
            points[i] = new Vector2(px, py);
        }

        // Bottom edge (right to left)
        for (int i = 0; i < count; i++)
        {
            int ri = count - 1 - i;
            float baseVal = baseline[ri];
            float normalized = baseVal / range;
            normalized = Math.Clamp(normalized, 0f, 1f);
            float px = x + ri * step;
            float py = y + h - normalized * h;
            points[count + i] = new Vector2(px, py);
        }

        Im.DrawFilledPolygon(points, color);
    }

    private static void DrawStackedLine(
        float x,
        float y,
        float w,
        float h,
        float step,
        float range,
        ReadOnlySpan<float> baseline,
        ReadOnlySpan<float> values,
        int count,
        uint color)
    {
        var topValues = _topValues.AsSpan(0, count);
        for (int i = 0; i < count; i++)
        {
            topValues[i] = baseline[i] + values[i];
        }

        Im.DrawGraph(topValues, x, y, w, h, 0f, range, 1.5f, color);
    }

    private static void DrawLegend(
        ImRect rect,
        string[] names,
        ReadOnlySpan<double> valuesMs,
        double otherMs,
        bool[] scopeEnabled,
        ref bool otherEnabled)
    {
        DrawRectWindowLocal(rect.X, rect.Y, rect.Width, rect.Height, 0xCC1A1A1Au);

        float minItemWidth = 160f;
        int columns = Math.Max(1, (int)(rect.Width / minItemWidth));
        float itemWidth = rect.Width / columns;
        float rowHeight = 16f;

        int maxRows = Math.Max(1, (int)(rect.Height / rowHeight));
        int totalItems = names.Length + 1; // +Other
        int maxItems = Math.Min(totalItems, columns * maxRows);
        maxItems = Math.Min(maxItems, MaxLegendItems);

        float boxSize = 10f;
        float boxOffsetY = 2f;
        float labelOffsetX = boxSize + 6f;
        float valueRightPad = 6f;

        var mouse = Im.WindowMousePos;
        bool clicked = Im.MousePressed;

        for (int itemIndex = 0; itemIndex < maxItems; itemIndex++)
        {
            int row = itemIndex / columns;
            int col = itemIndex - row * columns;

            float itemX = rect.X + col * itemWidth;
            float itemY = rect.Y + row * rowHeight;
            bool hovered = mouse.X >= itemX && mouse.X < itemX + itemWidth && mouse.Y >= itemY && mouse.Y < itemY + rowHeight;

            uint color;
            ReadOnlySpan<char> nameSpan;
            double valueMs;
            bool enabled;

            if (itemIndex < names.Length)
            {
                int colorIndex = itemIndex % ScopeColors.Length;
                color = ScopeColors[colorIndex];
                nameSpan = names[itemIndex].AsSpan();
                valueMs = itemIndex < valuesMs.Length ? valuesMs[itemIndex] : 0.0;
                enabled = itemIndex < scopeEnabled.Length && scopeEnabled[itemIndex];
            }
            else
            {
                color = OtherLineColor;
                nameSpan = "Other";
                valueMs = otherMs;
                enabled = otherEnabled;
            }

            if (hovered)
            {
                DrawRectWindowLocal(itemX + 1, itemY + 1, itemWidth - 2, rowHeight - 2, 0x33222222u);
                if (clicked)
                {
                    if (itemIndex < names.Length)
                    {
                        if (itemIndex < scopeEnabled.Length)
                        {
                            scopeEnabled[itemIndex] = !scopeEnabled[itemIndex];
                            enabled = scopeEnabled[itemIndex];
                        }
                    }
                    else
                    {
                        otherEnabled = !otherEnabled;
                        enabled = otherEnabled;
                    }
                }
            }

            uint boxColor = enabled ? color : SetAlpha(color, 0x55);
            uint textColor = enabled ? TextDimColor : SetAlpha(TextDimColor, 0x55);

            DrawRectWindowLocal(itemX + 4, itemY + boxOffsetY, boxSize, boxSize, boxColor);
            Im.Text(nameSpan, itemX + 4 + labelOffsetX, itemY + 1, 10f, textColor);

            float valueX = itemX + itemWidth - 54f - valueRightPad;
            Im.Text($"{valueMs:F1}ms", valueX, itemY + 1, 10f, textColor);
        }

        int hidden = totalItems - maxItems;
        if (hidden > 0)
        {
            Im.Text($"+{hidden} more", rect.X + 6, rect.Y + rect.Height - rowHeight + 1, 10f, TextDimColor);
        }
    }

    private static void DrawFooter(in ProfilerStats stats)
    {
        Im.Label($"Draw: {stats.MeshInstances} | SDF: {stats.SdfCommands} | Textures: {stats.TextureCount}");

        long bytes = stats.AllocatedThisFrame;
        if (bytes >= 1024 * 1024)
        {
            Im.Label($"Mem alloc: {bytes / (1024 * 1024f):F1}MB | GC: {stats.GcGen0}/{stats.GcGen1}/{stats.GcGen2}");
        }
        else if (bytes >= 1024)
        {
            Im.Label($"Mem alloc: {bytes / 1024f:F1}KB | GC: {stats.GcGen0}/{stats.GcGen1}/{stats.GcGen2}");
        }
        else
        {
            Im.Label($"Mem alloc: {bytes}B | GC: {stats.GcGen0}/{stats.GcGen1}/{stats.GcGen2}");
        }
    }

    private static void EnsureScratchCapacity(int bandCount)
    {
        int valuesNeeded = bandCount * HistorySize;
        if (_bandValues.Length < valuesNeeded)
        {
            _bandValues = new float[valuesNeeded];
        }

        if (_stackedTotals.Length < HistorySize)
        {
            _stackedTotals = new float[HistorySize];
        }

        if (_baseline.Length < HistorySize)
        {
            _baseline = new float[HistorySize];
        }

        if (_topValues.Length < HistorySize)
        {
            _topValues = new float[HistorySize];
        }

        int pointCount = HistorySize * 2;
        if (_polygonPoints.Length < pointCount)
        {
            _polygonPoints = new Vector2[pointCount];
        }
    }

    private static void ResetScratch()
    {
        _bandValues = Array.Empty<float>();
        _stackedTotals = Array.Empty<float>();
        _baseline = Array.Empty<float>();
        _topValues = Array.Empty<float>();
        _polygonPoints = Array.Empty<Vector2>();
    }

    private static void DrawGridLines(float x, float y, float width, float height, float maxVal)
    {
        float interval = maxVal <= 20f ? 5f : 10f;
        for (float ms = interval; ms < maxVal; ms += interval)
        {
            float lineY = y + height - (ms / maxVal) * height;
            DrawLineWindowLocal(x, lineY, x + width, lineY, 1f, GridColor);
        }
    }

    private static void DrawTargetLine(
        float x,
        float y,
        float width,
        float height,
        float maxVal,
        float targetMs,
        uint color,
        string label)
    {
        if (targetMs > maxVal)
        {
            return;
        }

        float lineY = y + height - (targetMs / maxVal) * height;
        if (lineY < y || lineY > y + height)
        {
            return;
        }

        float dashLength = 8f;
        float gapLength = 4f;
        float currentX = x;
        while (currentX < x + width)
        {
            float endX = Math.Min(currentX + dashLength, x + width);
            DrawLineWindowLocal(currentX, lineY, endX, lineY, 1.5f, color);
            currentX += dashLength + gapLength;
        }

        Im.Text(label.AsSpan(), x + width - 44, lineY - 12, 10f, color);
    }

    private static void DrawYAxisLabels(float x, float y, float width, float height, float maxVal)
    {
        Im.Text("0".AsSpan(), x + 2, y + height - 12, 10f, TextDimColor);

        float midVal = maxVal / 2f;
        float midY = y + height / 2f;
        Im.Text($"{midVal:F0}", x + 2, midY - 5, 10f, TextDimColor);

        Im.Text($"{maxVal:F0}ms", x + 2, y + 2, 10f, TextDimColor);
    }

    private static void DrawRectWindowLocal(float x, float y, float w, float h, uint color)
    {
        Im.DrawRect(x, y, w, h, color);
    }

    private static void DrawLineWindowLocal(float x1, float y1, float x2, float y2, float thickness, uint color)
    {
        Im.DrawLine(x1, y1, x2, y2, thickness, color);
    }

    private static uint SetAlpha(uint colorAbgr, byte alpha)
    {
        return (colorAbgr & 0x00FFFFFFu) | ((uint)alpha << 24);
    }
}
