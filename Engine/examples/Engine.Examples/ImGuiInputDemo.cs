using Serilog;
using DerpLib.Diagnostics;
using DerpLib.ImGui;
using DerpLib.ImGui.Layout;
using DerpLib.Text;

namespace DerpLib.Examples;

/// <summary>
/// Demo to test the ImGUI system with SDL global input and SDF rendering.
/// </summary>
public static class ImGuiInputDemo
{
    public static void Run()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== ImGUI Window System Test ===");

        // Initialize window and SDF
        Derp.InitWindow(1280, 720, "ImGUI Window Test");
        Derp.InitSdf();

        Log.Information("Window: {W}x{H}, Framebuffer: {FW}x{FH}, Scale: {Scale:F1}x",
            Derp.GetScreenWidth(), Derp.GetScreenHeight(),
            Derp.GetFramebufferWidth(), Derp.GetFramebufferHeight(),
            Derp.GetContentScale());

        // Load font for text rendering
        Font font = Derp.LoadFont("arial");
        Derp.SetSdfFontAtlas(font.Atlas);
        Log.Information("Font loaded: Atlas={W}x{H}, Glyphs={Count}",
            font.Atlas.Width, font.Atlas.Height, font.Glyphs.Length);

        // Initialize ImGUI (reads from Derp, enables multi-viewport by default)
        Im.Initialize();
        Im.SetFont(font);
        Log.Information("Multi-viewport enabled: drag windows outside to extract");

        // Track state per window
        int clickCount1 = 0;
        float sliderValue1 = 0.5f;
        bool checkboxValue1 = false;

        int clickCount2 = 0;
        float sliderValue2 = 0.75f;

        // Enable allocation tracking after JIT warmup (~80 frames needed)
        int frameNumber = 0;
        const int warmupFrames = 100;

        while (!Derp.WindowShouldClose())
        {
            frameNumber++;

            // Enable tracking after warmup frames
            if (frameNumber == warmupFrames)
            {
                AllocationTracker.Enable(Log.Logger, logEveryFrame: false);
            }

            AllocationTracker.BeginFrame();

            Derp.PollEvents();
            float dt = 0.016f;

            if (!Derp.BeginDrawing())
            {
                AllocationTracker.EndFrame();
                continue;
            }

            Derp.SdfBuffer.Reset();
            Im.Begin(dt);

            // Window 1: Controls
            if (Im.BeginWindow("Controls", 50, 50, 280, 250))
            {
                Im.LabelText("Click Counter");
                if (Im.Button("Click Me!"))
                {
                    clickCount1++;
                }

                ImLayout.Space(10);

                Im.LabelText("Slider Value");
                Im.Slider("slider1", ref sliderValue1, 0f, 1f);

                ImLayout.Space(10);

                Im.Checkbox("checkbox1", ref checkboxValue1);
                Im.LabelText("Enable Feature");
            }
            Im.EndWindow();

            // Window 2: Stats
            if (Im.BeginWindow("Stats", 400, 50, 250, 200))
            {
                Im.LabelText("Window 1 Clicks");
                if (Im.Button("Reset Counter"))
                {
                    clickCount1 = 0;
                    clickCount2++;
                }

                ImLayout.Space(10);

                Im.LabelText("Second Slider");
                Im.Slider("slider2", ref sliderValue2, 0f, 100f);
            }
            Im.EndWindow();

            // Cursor indicator
            uint cursorColor = Im.MouseDown ? 0xFF0078D4u : 0xFF3391FFu;
            Im.DrawCircle(Im.MousePos.X, Im.MousePos.Y, 8, cursorColor);

            Im.End();

            Derp.RenderSdf();
            Derp.EndDrawing();

            // Update and render secondary viewports (extracted windows)
            Im.UpdateSecondaryViewports(dt);

            AllocationTracker.EndFrame();
        }

        // Cleanup
        AllocationTracker.LogSummary();
        AllocationTracker.Disable();
        Derp.CloseWindow();
        Log.Information("=== ImGUI Window Test Complete ===");
    }
}
