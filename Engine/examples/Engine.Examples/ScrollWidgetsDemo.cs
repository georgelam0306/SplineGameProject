using Serilog;
using DerpLib.Diagnostics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Widgets;
using DerpLib.Text;

namespace DerpLib.Examples;

/// <summary>
/// Demo scene for scrollbar/scrollview behavior and visual parity.
/// Exercises window scrolling, popup scrolling (dropdown/combobox), and a custom scroll view region.
/// </summary>
public static class ScrollWidgetsDemo
{
    private static readonly string[] ManyOptions = CreateOptions(120, "Option");
    private static readonly string[] ManySuggestions = CreateOptions(120, "Suggestion");

    private static readonly char[] _comboBuffer = new char[64];
    private static int _comboLength;

    private static int _dropdownIndex;
    private static float _customScrollY;

    public static void Run()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== Scroll Widgets Demo ===");

        Derp.InitWindow(1400, 900, "Scroll Widgets Demo");
        Derp.InitSdf();

        Font font = Derp.LoadFont("arial");
        Derp.SetSdfFontAtlas(font.Atlas);

        Im.Initialize(enableMultiViewport: false);
        Im.SetFont(font);

        int frameNumber = 0;
        const int warmupFrames = 60;

        while (!Derp.WindowShouldClose())
        {
            frameNumber++;

            if (frameNumber == warmupFrames)
            {
                AllocationTracker.Enable(Log.Logger, logEveryFrame: false);
            }

            AllocationTracker.BeginFrame();

            Derp.PollEvents();
            const float dt = 0.016f;

            if (!Derp.BeginDrawing())
            {
                AllocationTracker.EndFrame();
                continue;
            }

            Derp.SdfBuffer.Reset();
            Im.Begin(dt);

            DrawWindowScrollingDemo();
            DrawPopupScrollingDemo();
            DrawCustomScrollViewDemo();

            Im.End();
            Derp.RenderSdf();
            Derp.EndDrawing();

            AllocationTracker.EndFrame();
        }

        AllocationTracker.LogSummary();
        AllocationTracker.Disable();
        Derp.CloseWindow();
        Log.Information("=== Scroll Widgets Demo Complete ===");
    }

    private static void DrawWindowScrollingDemo()
    {
        if (!Im.BeginWindow("Window Scrolling", 30, 30, 420, 820))
        {
            Im.EndWindow();
            return;
        }

        Im.LabelText("Use mouse wheel over window body.");
        Im.LabelText("Drag scrollbar thumb or click track to page.");
        ImLayout.Space(10);

        for (int i = 0; i < 200; i++)
        {
            Im.Label($"Row {i:000}  |  The quick brown fox jumps over the lazy dog.");
        }

        Im.EndWindow();
    }

    private static void DrawPopupScrollingDemo()
    {
        if (!Im.BeginWindow("Popup Scrolling (Dropdown + ComboBox)", 480, 30, 440, 300))
        {
            Im.EndWindow();
            return;
        }

        Im.LabelText("Open dropdown/combobox; scrollbars should match window behavior/visuals.");
        ImLayout.Space(10);

        Im.LabelText("Dropdown");
        Im.Dropdown("dropdown_scroll_demo", ManyOptions, ref _dropdownIndex, 320);

        ImLayout.Space(10);

        Im.LabelText("ComboBox");
        Im.ComboBox("combo_scroll_demo", _comboBuffer, ref _comboLength, 64, ManySuggestions, 320);

        Im.EndWindow();
    }

    private static void DrawCustomScrollViewDemo()
    {
        if (!Im.BeginWindow("Custom ScrollView Region", 480, 360, 440, 490))
        {
            Im.EndWindow();
            return;
        }

        Im.LabelText("This uses ImScrollView + ImScrollbar directly.");
        ImLayout.Space(10);

        var style = Im.Style;
        float viewWidth = 400f;
        float viewHeight = 360f;
        float scrollbarWidth = style.ScrollbarWidth;

        float contentHeight = 60f * style.MinButtonHeight;

        var viewRect = ImLayout.AllocateRect(viewWidth, viewHeight);

        // Visual container
        Im.Panel(viewRect.X, viewRect.Y, viewRect.Width, viewRect.Height);

        var contentRect = new ImRect(viewRect.X, viewRect.Y, viewRect.Width - scrollbarWidth, viewRect.Height);
        var scrollbarRect = new ImRect(contentRect.Right, viewRect.Y, scrollbarWidth, viewRect.Height);

        float contentY = ImScrollView.Begin(contentRect, contentHeight, ref _customScrollY, handleMouseWheel: true);

        for (int i = 0; i < 60; i++)
        {
            float rowY = contentY + i * style.MinButtonHeight;
            Im.Label($"Item {i:00}", contentRect.X + style.Padding, rowY + (style.MinButtonHeight - style.FontSize) * 0.5f);
        }

        ImScrollView.End(
            Im.Context.GetId(0x53564C57), // "SVLW"
            scrollbarRect,
            contentRect.Height,
            contentHeight,
            ref _customScrollY);

        ImLayout.Space(10f);
        Im.Label($"ScrollY: {_customScrollY:F1}");

        Im.EndWindow();
    }

    private static string[] CreateOptions(int count, string prefix)
    {
        var options = new string[count];
        for (int i = 0; i < count; i++)
        {
            options[i] = $"{prefix} {i:000}";
        }
        return options;
    }
}
