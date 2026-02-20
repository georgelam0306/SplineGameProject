using System.Numerics;
using Serilog;
using DerpLib.Sdf;
using DerpLib.Text;

namespace DerpLib.Examples;

/// <summary>
/// Demo showcasing the SDF alpha masking system.
/// Features: animated reveals, nested masks, soft edges, complex shape masking.
/// </summary>
public static class MaskingDemo
{
    public static void Run()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== SDF Masking Demo ===");

        Derp.InitWindow(1280, 720, "SDF Masking Demo");
        Derp.InitSdf();

        // Load font for labels
        Font font = Derp.LoadFont("arial");
        Derp.SetSdfFontAtlas(font.Atlas);

        float time = 0f;
        var buffer = Derp.SdfBuffer;

        while (!Derp.WindowShouldClose())
        {
            Derp.PollEvents();

            if (!Derp.BeginDrawing())
                continue;

            time += 0.016f;
            buffer.Reset();

            float screenW = Derp.GetScreenWidth();
            float screenH = Derp.GetScreenHeight();

            // === SECTION 1: Animated Circle Reveal ===
            float section1X = 100f;
            float section1Y = 80f;
            Derp.DrawText(font, "ANIMATED CIRCLE REVEAL", section1X, section1Y - 50f, 20f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            // Pulsing reveal radius
            float revealRadius = 80f + MathF.Sin(time * 2f) * 60f;
            var maskCenter = new Vector2(section1X + 100f, section1Y + 100f);

            // Push circle mask
            buffer.PushMask(SdfMaskShape.Circle(maskCenter, revealRadius, softEdge: 8f));

            // Draw complex content underneath (union of shapes)
            buffer.BeginGroup(SdfBooleanOp.Union);
            buffer.Add(SdfCommand.Circle(maskCenter + new Vector2(-40, -30), 50f, new Vector4(1f, 0.3f, 0.3f, 1f)));
            buffer.Add(SdfCommand.Circle(maskCenter + new Vector2(40, -30), 50f, new Vector4(0.3f, 1f, 0.3f, 1f)));
            buffer.Add(SdfCommand.Circle(maskCenter + new Vector2(0, 40), 50f, new Vector4(0.3f, 0.3f, 1f, 1f)));
            buffer.EndGroup(new Vector4(1f, 1f, 1f, 1f));

            buffer.PopMask();

            // Draw mask outline for reference (not masked)
            buffer.Add(SdfCommand.Circle(maskCenter, revealRadius, new Vector4(1f, 1f, 1f, 0.3f))
                .WithStroke(new Vector4(1f, 1f, 1f, 0.5f), 2f));

            // === SECTION 2: Nested Masks (Donut) ===
            float section2X = 450f;
            float section2Y = 80f;
            Derp.DrawText(font, "NESTED MASKS (DONUT)", section2X, section2Y - 50f, 20f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            var donutCenter = new Vector2(section2X + 100f, section2Y + 100f);
            float outerRadius = 100f;
            float innerRadius = 40f + MathF.Sin(time * 3f) * 20f;

            // Outer mask (inside visible)
            buffer.PushMask(SdfMaskShape.Circle(donutCenter, outerRadius, softEdge: 4f));
            // Inner mask inverted (outside visible) - creates ring
            buffer.PushMask(SdfMaskShape.Circle(donutCenter, innerRadius, softEdge: 4f, invert: true));

            // Rainbow gradient rect inside the ring
            for (int i = 0; i < 6; i++)
            {
                float hue = i / 6f;
                var (r, g, b) = HsvToRgb(hue + time * 0.2f, 0.8f, 1f);
                float stripY = section2Y + i * 35f;
                buffer.Add(SdfCommand.Rect(
                    new Vector2(section2X + 100f, stripY + 17f),
                    new Vector2(120f, 17f),
                    new Vector4(r, g, b, 1f)));
            }

            buffer.PopMask();
            buffer.PopMask();

            // === SECTION 3: Soft vs Hard Edges ===
            float section3X = 800f;
            float section3Y = 80f;
            Derp.DrawText(font, "SOFT vs HARD EDGES", section3X, section3Y - 50f, 20f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            // Hard edge (softEdge = 1)
            Derp.DrawText(font, "Hard (1px)", section3X, section3Y, 14f, 0f, 0.6f, 0.6f, 0.6f, 1f);
            buffer.PushMask(SdfMaskShape.Circle(new Vector2(section3X + 60f, section3Y + 70f), 50f, softEdge: 1f));
            buffer.Add(SdfCommand.Rect(new Vector2(section3X + 60f, section3Y + 70f), new Vector2(80f, 80f), new Vector4(1f, 0.5f, 0.2f, 1f)));
            buffer.PopMask();

            // Medium edge (softEdge = 8)
            Derp.DrawText(font, "Medium (8px)", section3X + 130f, section3Y, 14f, 0f, 0.6f, 0.6f, 0.6f, 1f);
            buffer.PushMask(SdfMaskShape.Circle(new Vector2(section3X + 190f, section3Y + 70f), 50f, softEdge: 8f));
            buffer.Add(SdfCommand.Rect(new Vector2(section3X + 190f, section3Y + 70f), new Vector2(80f, 80f), new Vector4(0.2f, 1f, 0.5f, 1f)));
            buffer.PopMask();

            // Soft edge (softEdge = 20)
            Derp.DrawText(font, "Soft (20px)", section3X + 260f, section3Y, 14f, 0f, 0.6f, 0.6f, 0.6f, 1f);
            buffer.PushMask(SdfMaskShape.Circle(new Vector2(section3X + 320f, section3Y + 70f), 50f, softEdge: 20f));
            buffer.Add(SdfCommand.Rect(new Vector2(section3X + 320f, section3Y + 70f), new Vector2(80f, 80f), new Vector4(0.5f, 0.2f, 1f, 1f)));
            buffer.PopMask();

            // === SECTION 4: Complex Union Masking Union ===
            float section4X = 100f;
            float section4Y = 380f;
            Derp.DrawText(font, "UNION SHAPES MASKING UNION SHAPES", section4X, section4Y - 50f, 20f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            // Create a complex mask from multiple circles (moving)
            float maskAngle = time * 1.5f;
            var maskCenters = new Vector2[]
            {
                new Vector2(section4X + 200f + MathF.Cos(maskAngle) * 80f, section4Y + 120f + MathF.Sin(maskAngle) * 40f),
                new Vector2(section4X + 200f + MathF.Cos(maskAngle + 2.1f) * 80f, section4Y + 120f + MathF.Sin(maskAngle + 2.1f) * 40f),
                new Vector2(section4X + 200f + MathF.Cos(maskAngle + 4.2f) * 80f, section4Y + 120f + MathF.Sin(maskAngle + 4.2f) * 40f),
            };

            // Push union mask (3 circles)
            buffer.PushMask(SdfMaskShape.Union(
                SdfMaskShape.Circle(maskCenters[0], 60f, softEdge: 6f),
                SdfMaskShape.Circle(maskCenters[1], 60f, softEdge: 6f),
                SdfMaskShape.Circle(maskCenters[2], 60f, softEdge: 6f)
            ));

            // Draw union of shapes inside (grid of rects + circles)
            buffer.BeginGroup(SdfBooleanOp.Union);
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    float px = section4X + 40f + x * 45f;
                    float py = section4Y + 40f + y * 45f;
                    float hue = (x + y) / 12f + time * 0.1f;
                    var (r, g, b) = HsvToRgb(hue, 0.7f, 1f);

                    if ((x + y) % 2 == 0)
                        buffer.Add(SdfCommand.Circle(new Vector2(px, py), 15f, new Vector4(r, g, b, 1f)));
                    else
                        buffer.Add(SdfCommand.RoundedRect(new Vector2(px, py), new Vector2(12f, 12f), 4f, new Vector4(r, g, b, 1f)));
                }
            }
            buffer.EndGroup(new Vector4(1f, 1f, 1f, 1f));

            buffer.PopMask();

            // Draw mask outlines for reference
            foreach (var mc in maskCenters)
            {
                buffer.Add(SdfCommand.Circle(mc, 60f, new Vector4(1f, 1f, 1f, 0f))
                    .WithStroke(new Vector4(1f, 1f, 1f, 0.4f), 1.5f));
            }

            // === SECTION 5: Text with Warp Masked ===
            float section5X = 550f;
            float section5Y = 380f;
            Derp.DrawText(font, "MASKED WARPED TEXT", section5X, section5Y - 50f, 20f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            // Moving rect mask
            float maskX = section5X + 180f + MathF.Sin(time * 2f) * 100f;
            buffer.PushMask(SdfMaskShape.RoundedRect(
                new Vector2(maskX, section5Y + 80f),
                new Vector2(100f, 60f),
                cornerRadius: 15f,
                softEdge: 10f));

            // Wavy text inside mask
            Derp.PushSdfWave(frequency: 2f, amplitude: 8f, phase: time * 4f);
            Derp.DrawTextWithEffects(font, "MASKING!", section5X + 50f, section5Y + 60f, 48f,
                new Vector4(1f, 0.8f, 0.2f, 1f),
                glowRadius: 12f);
            Derp.PopSdfWarp();

            buffer.PopMask();

            // Draw mask outline
            buffer.Add(SdfCommand.RoundedRect(
                new Vector2(maskX, section5Y + 80f),
                new Vector2(100f, 60f), 15f, new Vector4(1f, 1f, 1f, 0f))
                .WithStroke(new Vector4(1f, 1f, 1f, 0.4f), 1.5f));

            // === Footer ===
            Derp.DrawText(font, $"Time: {time:F2}s", 20f, screenH - 30f, 14f, 0f, 0.5f, 0.5f, 0.5f, 1f);

            Derp.RenderSdf();
            Derp.EndDrawing();
        }

        Derp.CloseWindow();
        Log.Information("=== Masking Demo Complete ===");
    }

    private static (float r, float g, float b) HsvToRgb(float h, float s, float v)
    {
        h = ((h % 1f) + 1f) % 1f; // Wrap to 0-1
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

        return (r + m, g + m, b + m);
    }
}
