using System.Diagnostics;
using System.Numerics;
using DerpLib;
using DerpLib.Sdf;
using DerpLib.Text;
using Serilog;

namespace DerpLib.Examples;

/// <summary>
/// Font rendering demo showcasing SDF text with outlines, scaling, and warps.
/// </summary>
public static class FontDemo
{
    public static void Run()
    {
        Derp.InitWindow(1200, 800, "Font Demo - SDF Text Rendering");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Derp.InitSdf();

        // Load font
        Font arial = Derp.LoadFont("arial");
        Log.Information("Font loaded: Atlas={W}x{H}, BaseSizePx={Size}, Glyphs={Count}",
            arial.Atlas.Width, arial.Atlas.Height, arial.BaseSizePixels, arial.Glyphs.Length);

        float time = 0f;
        int frameCount = 0;
        var stopwatch = Stopwatch.StartNew();
        double lastFpsUpdate = 0;
        int framesSinceLastUpdate = 0;

        while (!Derp.WindowShouldClose())
        {
            Derp.PollEvents();

            if (!Derp.BeginDrawing())
                continue;

            time += 0.016f;
            float screenW = Derp.GetScreenWidth();
            float screenH = Derp.GetScreenHeight();
            var buffer = Derp.SdfBuffer;

            // === SECTION 1: Scaled Text ===
            float section1Y = 30f;
            Derp.DrawText(arial, "SCALED TEXT", 20f, section1Y, 24f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            // Multiple sizes
            float[] sizes = { 12f, 18f, 24f, 36f, 48f, 72f };
            float yOffset = section1Y + 35f;
            for (int i = 0; i < sizes.Length; i++)
            {
                float size = sizes[i];
                float hue = i / (float)sizes.Length;
                var (r, g, b) = HsvToRgb(hue, 0.8f, 1f);
                Derp.DrawText(arial, $"{size}px - The quick brown fox", 20f, yOffset, size, 0f, r, g, b, 1f);
                yOffset += size + 8f;
            }

            // === SECTION 2: Wave Warped Text ===
            float section2Y = 280f;
            Derp.DrawText(arial, "WAVE WARP", 20f, section2Y, 24f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            Derp.PushSdfWave(frequency: 2f, amplitude: 12f, phase: time * 3f);
            Derp.DrawText(arial, "Wavy text flows like water!", 20f, section2Y + 35f, 36f, 0f, 0.3f, 0.8f, 1f, 1f);
            Derp.PopSdfWarp();

            Derp.PushSdfWave(frequency: 4f, amplitude: 6f, phase: time * 5f);
            Derp.DrawText(arial, "Higher frequency waves", 20f, section2Y + 80f, 28f, 0f, 0.5f, 1f, 0.6f, 1f);
            Derp.PopSdfWarp();

            // === SECTION 3: Twist Warped Text ===
            float section3Y = 370f;
            Derp.DrawText(arial, "TWIST WARP", 20f, section3Y, 24f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            Derp.PushSdfTwist(strength: MathF.Sin(time * 2f) * 4f);
            Derp.DrawText(arial, "Twisted typography!", 20f, section3Y + 35f, 40f, 0f, 1f, 0.5f, 0.8f, 1f);
            Derp.PopSdfWarp();

            // === SECTION 4: Bulge Warped Text ===
            float section4Y = 430f;
            Derp.DrawText(arial, "BULGE WARP", 20f, section4Y, 24f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            Derp.PushSdfBulge(strength: MathF.Sin(time * 2.5f) * 40f, radius: 150f);
            Derp.DrawText(arial, "Bulging letters pop out!", 20f, section4Y + 35f, 36f, 0f, 1f, 0.8f, 0.3f, 1f);
            Derp.PopSdfWarp();

            // === SECTION 5: Lattice Warped Text ===
            float section5Y = 490f;
            Derp.DrawText(arial, "LATTICE WARP", 20f, section5Y, 24f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            var lattice = SdfLattice.Wave(amplitude: 0.35f, frequency: 1.5f, phase: time * 2f);
            Derp.PushNewSdfLattice(lattice, scaleX: 200f, scaleY: 80f);
            Derp.DrawText(arial, "Lattice deformation!", 20f, section5Y + 35f, 32f, 0f, 0.9f, 0.5f, 0.2f, 1f);
            Derp.PopSdfWarp();

            // === SECTION 6: Text with Effects (right side) ===
            float rightX = screenW * 0.5f + 20f;
            float section6Y = 30f;
            Derp.DrawText(arial, "TEXT WITH EFFECTS", rightX, section6Y, 24f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            // Text with outline effect - uses text group so outline applies to entire string
            Derp.DrawTextWithEffects(arial, "Outlined Text", rightX, section6Y + 40f, 48f,
                new Vector4(1f, 1f, 1f, 1f),
                strokeWidth: 3f, strokeColor: new Vector4(0.2f, 0.2f, 0.8f, 1f));

            // Text with shadow effect
            Derp.DrawTextWithEffects(arial, "Shadow Text", rightX, section6Y + 100f, 42f,
                new Vector4(1f, 0.9f, 0.7f, 1f),
                shadowOffsetX: 4f, shadowOffsetY: 4f, shadowBlur: 8f, shadowColor: new Vector4(0f, 0f, 0f, 0.5f));

            // Text with glow effect
            Derp.DrawTextWithEffects(arial, "Glowing Text", rightX, section6Y + 160f, 40f,
                new Vector4(0.3f, 1f, 0.4f, 1f),
                glowRadius: 15f);

            // === SECTION 7: Combined Effects (right side) ===
            float section7Y = 250f;
            Derp.DrawText(arial, "COMBINED EFFECTS", rightX, section7Y, 24f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            // Wavy + outline - warps via push/pop, effects via text group
            Derp.PushSdfWave(frequency: 3f, amplitude: 8f, phase: time * 4f);
            Derp.DrawTextWithEffects(arial, "Wavy Outline", rightX, section7Y + 35f, 36f,
                new Vector4(1f, 0.8f, 0.2f, 1f),
                strokeWidth: 2.5f, strokeColor: new Vector4(0.8f, 0.2f, 0.1f, 1f));
            Derp.PopSdfWarp();

            // Twisted + glow
            Derp.PushSdfTwist(strength: MathF.Sin(time * 1.5f) * 3f);
            Derp.DrawTextWithEffects(arial, "Twisted Glow", rightX, section7Y + 85f, 34f,
                new Vector4(0.5f, 0.8f, 1f, 1f),
                glowRadius: 12f);
            Derp.PopSdfWarp();

            // Bulge + shadow + outline - combined effects!
            Derp.PushSdfBulge(strength: MathF.Sin(time * 2f) * 25f, radius: 120f);
            Derp.DrawTextWithEffects(arial, "Full FX!", rightX, section7Y + 135f, 48f,
                new Vector4(1f, 1f, 1f, 1f),
                strokeWidth: 2f, strokeColor: new Vector4(0.8f, 0.3f, 0.8f, 1f),
                shadowOffsetX: 3f, shadowOffsetY: 3f, shadowBlur: 6f, shadowColor: new Vector4(0f, 0f, 0f, 0.4f));
            Derp.PopSdfWarp();

            // === SECTION 8: Large Display Text ===
            float section8Y = 420f;
            Derp.DrawText(arial, "LARGE DISPLAY", rightX, section8Y, 24f, 0f, 0.7f, 0.7f, 0.7f, 1f);

            // Big wavy rainbow text (per-character colors, so uses individual glyphs)
            Derp.PushSdfWave(frequency: 1.5f, amplitude: 15f, phase: time * 2f);
            DrawRainbowText(arial, "RAINBOW", rightX, section8Y + 50f, 72f, time, buffer);
            Derp.PopSdfWarp();

            // Massive pulsing text with glow
            float pulse = 1f + MathF.Sin(time * 3f) * 0.1f;
            float bigSize = 64f * pulse;
            Derp.PushSdfBulge(strength: MathF.Sin(time * 2f) * 30f, radius: 200f);
            Derp.DrawTextWithEffects(arial, "HUGE", rightX + 100f, section8Y + 140f, bigSize,
                new Vector4(1f, 0.3f, 0.3f, 1f),
                glowRadius: 20f);
            Derp.PopSdfWarp();

            // === Bottom info bar ===
            Derp.DrawText(arial, $"Frame: {frameCount}  Time: {time:F2}s  Shapes: {Derp.SdfCommandCount}",
                20f, screenH - 30f, 16f, 0f, 0.6f, 0.6f, 0.6f, 1f);

            Derp.RenderSdf();
            Derp.EndDrawing();

            frameCount++;
            framesSinceLastUpdate++;

            double elapsed = stopwatch.Elapsed.TotalSeconds;
            if (elapsed - lastFpsUpdate >= 0.5)
            {
                double fps = framesSinceLastUpdate / (elapsed - lastFpsUpdate);
                Derp.SetWindowTitle($"Font Demo - {fps:F1} FPS | {Derp.SdfCommandCount} shapes");
                lastFpsUpdate = elapsed;
                framesSinceLastUpdate = 0;
            }
        }

        Log.Information("Font Demo: {Frames} frames in {Seconds:F1}s",
            frameCount, stopwatch.Elapsed.TotalSeconds);

        Derp.CloseWindow();
    }

    /// <summary>
    /// Draws text with rainbow colors cycling through hue.
    /// </summary>
    private static void DrawRainbowText(Font font, ReadOnlySpan<char> text, float x, float y, float fontSize,
        float time, SdfBuffer buffer)
    {
        float scale = fontSize / font.BaseSizePixels;
        float penX = x;
        float baselineY = y + font.AscentPixels * scale;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\n') { penX = x; baselineY += font.LineHeightPixels * scale; continue; }
            if (!font.TryGetGlyph(ch, out var glyph)) continue;

            float glyphWidth = glyph.Width * scale;
            float glyphHeight = glyph.Height * scale;

            if (glyphWidth > 0f && glyphHeight > 0f)
            {
                float glyphX = penX + glyph.OffsetX * scale;
                float glyphY = baselineY + glyph.OffsetY * scale;
                float halfW = glyphWidth * 0.5f;
                float halfH = glyphHeight * 0.5f;
                float centerX = glyphX + halfW;
                float centerY = glyphY + halfH;

                // Rainbow color per character
                float hue = (i / (float)text.Length + time * 0.3f) % 1f;
                var (r, g, b) = HsvToRgb(hue, 0.9f, 1f);

                var cmd = SdfCommand.Glyph(
                    new Vector2(centerX, centerY),
                    new Vector2(halfW, halfH),
                    new Vector4(glyph.U0, glyph.V0, glyph.U1, glyph.V1),
                    new Vector4(r, g, b, 1f))
                    .WithGlow(8f);
                buffer.Add(cmd);
            }

            penX += glyph.AdvanceX * scale;
        }
    }

    private static (float r, float g, float b) HsvToRgb(float h, float s, float v)
    {
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
