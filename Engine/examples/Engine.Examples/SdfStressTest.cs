using System.Diagnostics;
using Serilog;
using DerpLib;
using DerpLib.Sdf;
using Silk.NET.Input;

namespace DerpLib.Examples;

/// <summary>
/// SDF stress test - benchmarks tile-based rendering with many shapes.
/// Press P to add more shapes, M to reduce.
/// </summary>
public static class SdfStressTest
{
    public static void Run()
    {
        Derp.InitWindow(800, 600, "SDF Stress Test - Press P/M to change load");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Derp.InitSdf();

        float time = 0f;
        int stressMultiplier = 1;
        var stopwatch = Stopwatch.StartNew();
        double lastFpsUpdate = 0;
        int framesSinceLastUpdate = 0;
        double currentFps = 60.0;

        // Input tracking
        bool lastPState = false;
        bool lastMState = false;

        // Pre-allocate spline buffers
        Span<System.Numerics.Vector2> splinePoints = stackalloc System.Numerics.Vector2[65];

        while (!Derp.WindowShouldClose())
        {
            Derp.PollEvents();

            // Handle P/M keys for stress multiplier
            bool pDown = Derp.IsKeyDown(Key.P);
            bool mDown = Derp.IsKeyDown(Key.M);

            if (pDown && !lastPState)
            {
                stressMultiplier++;
                Log.Information("Stress multiplier: {Mult}x (~{Shapes} shapes)",
                    stressMultiplier, stressMultiplier * 170);
            }
            if (mDown && !lastMState && stressMultiplier > 1)
            {
                stressMultiplier--;
                Log.Information("Stress multiplier: {Mult}x (~{Shapes} shapes)",
                    stressMultiplier, stressMultiplier * 170);
            }
            lastPState = pDown;
            lastMState = mDown;

            if (!Derp.BeginDrawing())
                continue;

            time += 0.016f;
            float screenW = Derp.GetScreenWidth();
            float screenH = Derp.GetScreenHeight();

            DrawStressTest(time, stressMultiplier, screenW, screenH, splinePoints);

            Derp.RenderSdf();
            Derp.EndDrawing();

            // Update FPS
            framesSinceLastUpdate++;
            double elapsed = stopwatch.Elapsed.TotalSeconds;
            if (elapsed - lastFpsUpdate >= 0.25)
            {
                currentFps = framesSinceLastUpdate / (elapsed - lastFpsUpdate);
                Derp.SetWindowTitle($"SDF Stress Test - {currentFps:F1} FPS | {Derp.SdfCommandCount} shapes | {stressMultiplier}x (P/M to change)");
                lastFpsUpdate = elapsed;
                framesSinceLastUpdate = 0;
            }
        }

        Derp.CloseWindow();
    }

    private static void DrawStressTest(float time, int multiplier, float screenW, float screenH,
        Span<System.Numerics.Vector2> splinePoints)
    {
        var buffer = Derp.SdfBuffer;

        int shapesPerRow = 10 * multiplier;

        // Row 1: Circles across top with glow
        float rowSpacing = screenH / 9f;
        float row1Y = 50f;
        float spacing = (screenW - 40f) / MathF.Max(shapesPerRow * 2, 1);

        for (int i = 0; i < shapesPerRow * 2; i++)
        {
            float x = 20 + i * spacing;
            float y = row1Y + MathF.Sin(time * 3 + i * 0.5f) * 15f;
            var color = HsvToColor(i * 18 + time * 50, 0.9f, 1f);
            var circle = SdfCommand.Circle(
                new System.Numerics.Vector2(x, y),
                10f, color)
                .WithGlow(6f);
            buffer.Add(circle);
        }

        // Row 2: Rectangles with strokes
        float row2Y = row1Y + rowSpacing;
        spacing = (screenW - 40f) / MathF.Max(shapesPerRow * 1.5f, 1);
        for (int i = 0; i < (int)(shapesPerRow * 1.5f); i++)
        {
            float x = 20 + i * spacing;
            float rotation = time + i * 0.3f;
            var color = HsvToColor(i * 24 + 90, 0.8f, 1f);
            float ox = MathF.Cos(rotation) * 5;
            float oy = MathF.Sin(rotation) * 5;
            var rect = SdfCommand.RoundedRect(
                new System.Numerics.Vector2(x + ox, row2Y + oy),
                new System.Numerics.Vector2(15f, 10f),
                5f, color)
                .WithStroke(new System.Numerics.Vector4(0, 0, 0, 1), 1f);
            buffer.Add(rect);
        }

        // Row 3: Filled polygons
        float row3Y = row2Y + rowSpacing;
        spacing = (screenW - 60f) / MathF.Max(shapesPerRow * 1.2f, 1);
        for (int i = 0; i < (int)(shapesPerRow * 1.2f); i++)
        {
            float x = 30 + i * spacing;
            int sides = 3 + i % 6;
            var color = HsvToColor(i * 30 + 180, 0.85f, 1f);
            float rot = time * (1 + i * 0.1f);
            Derp.DrawSdfFilledPolygon(x, row3Y, 18f, sides, color.X, color.Y, color.Z, color.W, rot);
        }

        // Row 4: Lines
        float row4Y = row3Y + rowSpacing;
        spacing = (screenW - 40f) / MathF.Max(shapesPerRow * 1.6f, 1);
        for (int i = 0; i < (int)(shapesPerRow * 1.6f); i++)
        {
            float x1 = 20 + i * spacing;
            float x2 = x1 + 25;
            float y1 = row4Y + MathF.Sin(time * 2 + i * 0.4f) * 10f;
            float y2 = row4Y + 20 + MathF.Cos(time * 2 + i * 0.4f) * 10f;
            var color = HsvToColor(i * 22 + 45, 0.9f, 1f);
            Derp.DrawSdfLine(x1, y1, x2, y2, 3f, color.X, color.Y, color.Z, color.W);
        }

        // Row 5: Bezier curves (converted to polylines for consistent tile assignment)
        float row5Y = row4Y + rowSpacing;
        spacing = (screenW - 40f) / MathF.Max(shapesPerRow * 0.8f, 1);
        for (int i = 0; i < (int)(shapesPerRow * 0.8f); i++)
        {
            float x0 = 20 + i * spacing;
            float cx = x0 + spacing * 0.5f;
            float x1 = x0 + spacing * 0.9f;
            float cy = row5Y + MathF.Sin(time * 3 + i) * 25f;
            var color = HsvToColor(i * 45 + 225, 0.9f, 1f);
            buffer.AddBezier(x0, row5Y + 20f, cx, cy, x1, row5Y + 20f,
                3f, color.X, color.Y, color.Z, color.W, glowRadius: 5f);
        }

        // Row 6: Boolean groups (smooth union metaballs)
        float row6Y = row5Y + rowSpacing;
        int boolCount = MathF.Max(shapesPerRow / 2, 3).ToInt32();
        spacing = (screenW - 100f) / MathF.Max(boolCount, 1);
        for (int i = 0; i < boolCount; i++)
        {
            float x = 50 + i * spacing;

            if (i % 3 == 0)
            {
                // Smooth union
                buffer.BeginGroup(SdfBooleanOp.SmoothUnion, 10f);
                float ox = MathF.Cos(time * 2 + i) * 15f;
                buffer.AddCircle(x - 15 + ox, row6Y, 18f, 0, 0, 0, 1);
                buffer.AddCircle(x + 15 - ox, row6Y, 18f, 0, 0, 0, 1);
                var color = HsvToColor(i * 60, 0.8f, 1f);
                buffer.EndGroup(color.X, color.Y, color.Z, color.W);
            }
            else if (i % 3 == 1)
            {
                // Subtract (donut)
                buffer.BeginGroup(SdfBooleanOp.Subtract);
                buffer.AddCircle(x, row6Y, 25f, 0, 0, 0, 1);
                buffer.AddCircle(x, row6Y, 12f, 0, 0, 0, 1);
                var color = HsvToColor(i * 60 + 30, 0.8f, 1f);
                buffer.EndGroup(color.X, color.Y, color.Z, color.W);
            }
            else
            {
                // Intersect
                buffer.BeginGroup(SdfBooleanOp.Intersect);
                buffer.AddCircle(x - 10, row6Y, 22f, 0, 0, 0, 1);
                buffer.AddCircle(x + 10, row6Y, 22f, 0, 0, 0, 1);
                var color = HsvToColor(i * 60 + 60, 0.8f, 1f);
                buffer.EndGroup(color.X, color.Y, color.Z, color.W);
            }
        }

        // Row 7: Gradient shapes
        float row7Y = row6Y + rowSpacing;
        spacing = (screenW - 80f) / MathF.Max(shapesPerRow, 1);
        for (int i = 0; i < shapesPerRow; i++)
        {
            float x = 40 + i * spacing;
            var startColor = HsvToColor(i * 36 + time * 20, 0.9f, 1f);
            var endColor = HsvToColor(i * 36 + 180 + time * 20, 0.9f, 1f);

            if (i % 2 == 0)
            {
                var circle = SdfCommand.Circle(
                    new System.Numerics.Vector2(x, row7Y),
                    15f, startColor)
                    .WithLinearGradient(endColor, time + i * 0.5f);
                buffer.Add(circle);
            }
            else
            {
                var rect = SdfCommand.RoundedRect(
                    new System.Numerics.Vector2(x, row7Y),
                    new System.Numerics.Vector2(14f, 14f),
                    4f, startColor)
                    .WithRadialGradient(endColor);
                buffer.Add(rect);
            }
        }

        // Row 8: Warped shapes
        float row8Y = row7Y + rowSpacing - 20f;
        int warpCount = shapesPerRow / 2;
        spacing = (screenW - 100f) / MathF.Max(warpCount, 1);

        for (int i = 0; i < warpCount; i++)
        {
            float x = 50 + i * spacing;
            var color = HsvToColor(i * 24 + time * 30, 0.85f, 1f);

            if (i % 3 == 0)
            {
                // Stacked warp: wave -> twist
                Derp.PushSdfWave(4f, 6f, time * 3f + i);
                Derp.PushSdfTwist(MathF.Sin(time * 2f + i) * 1.5f);
                Derp.DrawSdfCircle(x, row8Y, 16f, color.X, color.Y, color.Z, color.W);
                Derp.PopSdfWarp();
                Derp.PopSdfWarp();
            }
            else if (i % 3 == 1)
            {
                // Stacked warp: twist -> bulge
                Derp.PushSdfTwist(MathF.Sin(time * 2f + i) * 3f);
                Derp.PushSdfBulge(MathF.Sin(time * 3f + i) * 20f, 50f);
                Derp.DrawSdfRoundedRect(x, row8Y, 18f, 18f, 4f, color.X, color.Y, color.Z, color.W);
                Derp.PopSdfWarp();
                Derp.PopSdfWarp();
            }
            else
            {
                // Stacked warp: bulge -> wave
                Derp.PushSdfBulge(MathF.Sin(time * 3f + i) * 30f, 50f);
                Derp.PushSdfWave(3f, 4f, time * 2f + i * 0.25f);
                Derp.DrawSdfRect(x, row8Y, 16f, 16f, color.X, color.Y, color.Z, color.W);
                Derp.PopSdfWarp();
                Derp.PopSdfWarp();
            }
        }

        // Row 9: Morphing shapes (circle ↔ rect blend)
        float row9Y = row8Y + rowSpacing;
        int morphCount = MathF.Max(shapesPerRow / 2, 4).ToInt32();
        spacing = (screenW - 80f) / MathF.Max(morphCount, 1);

        for (int i = 0; i < morphCount; i++)
        {
            float x = 40 + i * spacing;
            // Animate morphFactor 0→1→0 with phase offset per shape
            float morphFactor = (MathF.Sin(time * 2f + i * 0.8f) + 1f) * 0.5f;
            var color = HsvToColor(i * 45 + time * 40, 0.9f, 1f);

            buffer.BeginMorph(morphFactor);
            buffer.AddCircle(x, row9Y, 18f, 0, 0, 0, 1);  // Shape A: circle
            buffer.AddRoundedRect(x, row9Y, 30f, 20f, 4f, 0, 0, 0, 1);  // Shape B: rounded rect
            buffer.EndMorph(color.X, color.Y, color.Z, color.W);
        }
    }

    private static System.Numerics.Vector4 HsvToColor(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f;  // Normalize hue to 0-360
        h /= 360f;  // Convert to 0-1

        float c = v * s;
        float x = c * (1f - MathF.Abs((h * 6f) % 2f - 1f));
        float m = v - c;

        float r, g, b;
        int hi = (int)(h * 6f) % 6;
        switch (hi)
        {
            case 0: (r, g, b) = (c, x, 0); break;
            case 1: (r, g, b) = (x, c, 0); break;
            case 2: (r, g, b) = (0, c, x); break;
            case 3: (r, g, b) = (0, x, c); break;
            case 4: (r, g, b) = (x, 0, c); break;
            default: (r, g, b) = (c, 0, x); break;
        }

        return new System.Numerics.Vector4(r + m, g + m, b + m, 1f);
    }
}

// Extension to convert float to int safely
internal static class FloatExtensions
{
    public static int ToInt32(this float value) => (int)value;
}
