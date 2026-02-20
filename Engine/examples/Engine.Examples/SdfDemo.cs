using System.Diagnostics;
using Serilog;
using DerpLib;
using DerpLib.Sdf;
using DerpLib.Text;

namespace DerpLib.Examples;

/// <summary>
/// Simple SDF rendering demo.
/// Shows circles, rectangles, and rounded rectangles rendered via compute shader.
/// </summary>
public static class SdfDemo
{
    public static void Run()
    {
        // Initialize window
        Derp.InitWindow(800, 600, "SDF Demo - Compute Shader Rendering");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Initialize SDF system
        Derp.InitSdf();

        // Load Arial font
        Font arial = Derp.LoadFont("arial");
        Log.Information("Font loaded: Atlas={W}x{H}, BaseSizePx={Size}, Glyphs={Count}, FirstCodepoint={First}",
            arial.Atlas.Width, arial.Atlas.Height, arial.BaseSizePixels, arial.Glyphs.Length, arial.FirstCodepoint);
        if (arial.TryGetGlyph('H', out var testGlyph))
        {
            Log.Information("Glyph 'H': W={W}, H={H}, AdvX={Adv}, UV=({U0},{V0})-({U1},{V1})",
                testGlyph.Width, testGlyph.Height, testGlyph.AdvanceX,
                testGlyph.U0, testGlyph.V0, testGlyph.U1, testGlyph.V1);
        }
        else
        {
            Log.Warning("Could not find glyph 'H' in font!");
        }

        float time = 0f;
        int frameCount = 0;
        var stopwatch = Stopwatch.StartNew();
        double lastFpsUpdate = 0;
        int framesSinceLastUpdate = 0;

        // Pre-allocate sample buffers outside the loop
        Span<float> waveformSamples = stackalloc float[256];
        Span<float> frameTimeSamples = stackalloc float[128];

        while (!Derp.WindowShouldClose())
        {
            Derp.PollEvents();

            if (!Derp.BeginDrawing())
                continue;

            time += 0.016f;
            float screenW = Derp.GetScreenWidth();
            float screenH = Derp.GetScreenHeight();

            // === Draw SDF shapes with WARPS ===

            // === WAVE WARP DEMO (left side) ===
            Derp.PushSdfWave(frequency: 3f, amplitude: 15f, phase: time * 4f);

            // Wavy circles
            for (int i = 0; i < 3; i++)
            {
                float x = 120f;
                float y = 150f + i * 100f;
                float hue = (i / 3f + time * 0.2f) % 1f;
                var (r, g, b) = HsvToRgb(hue, 0.9f, 1f);
                Derp.DrawSdfCircle(x, y, 35f, r, g, b, 0.9f);
            }

            // Wavy rectangle
            Derp.DrawSdfRoundedRect(120f, 480f, 60f, 30f, 10f, 0.2f, 0.8f, 0.4f, 0.8f);

            Derp.PopSdfWarp();

            // === STACKED WARP DEMO (center) ===
            // Demonstrates warp stacking: wave is applied, then twist warps the result.
            Derp.PushSdfWave(frequency: 2f, amplitude: 6f, phase: time * 2f);
            Derp.PushSdfTwist(strength: MathF.Sin(time * 2f) * 3f);

            // Twisted rectangle in center
            Derp.DrawSdfRoundedRect(screenW * 0.5f, screenH * 0.5f, 80f, 80f, 15f, 0.8f, 0.3f, 0.9f, 0.85f);

            // Inner twisted circle
            Derp.DrawSdfCircle(screenW * 0.5f, screenH * 0.5f, 30f, 1f, 0.9f, 0.3f, 0.9f);

            Derp.PopSdfWarp();
            Derp.PopSdfWarp();

            // === BULGE WARP DEMO (right side) ===
            // Stack twist + bulge to show multi-warp composition.
            Derp.PushSdfTwist(strength: MathF.Sin(time * 1.5f) * 1.5f);
            Derp.PushSdfBulge(strength: MathF.Sin(time * 3f) * 50f, radius: 100f);

            // Bulging shapes
            for (int i = 0; i < 3; i++)
            {
                float x = screenW - 120f;
                float y = 150f + i * 100f;
                float hue = (0.5f + i / 3f + time * 0.15f) % 1f;
                var (r, g, b) = HsvToRgb(hue, 0.8f, 1f);
                Derp.DrawSdfRect(x, y, 40f, 40f, r, g, b, 0.85f);
            }

            // Bulging rounded rect
            Derp.DrawSdfRoundedRect(screenW - 120f, 480f, 50f, 35f, 12f, 0.3f, 0.7f, 1f, 0.8f);

            Derp.PopSdfWarp();
            Derp.PopSdfWarp();

            // === LATTICE WARP DEMO (bottom row) ===

            // Create animated lattices each frame
            // Lattice 1: Wave pattern
            var waveLattice = SdfLattice.Wave(amplitude: 0.3f, frequency: 2f, phase: time * 3f);
            Derp.PushNewSdfLattice(waveLattice, scaleX: 150f, scaleY: 150f);
            Derp.DrawSdfRoundedRect(200f, screenH - 80f, 50f, 40f, 8f, 0.9f, 0.5f, 0.2f, 0.9f);
            Derp.PopSdfWarp();

            // Lattice 2: Twist pattern
            var twistLattice = SdfLattice.Twist(MathF.Sin(time * 2f) * 0.8f);
            Derp.PushNewSdfLattice(twistLattice, scaleX: 120f, scaleY: 120f);
            Derp.DrawSdfCircle(screenW * 0.5f, screenH - 80f, 40f, 0.2f, 0.9f, 0.6f, 0.9f);
            Derp.PopSdfWarp();

            // Lattice 3: Bulge pattern
            var bulgeLattice = SdfLattice.Bulge(MathF.Sin(time * 3f) * 0.5f);
            Derp.PushNewSdfLattice(bulgeLattice, scaleX: 140f, scaleY: 140f);
            Derp.DrawSdfRect(screenW - 200f, screenH - 80f, 45f, 45f, 0.5f, 0.3f, 0.9f, 0.9f);
            Derp.PopSdfWarp();

            // === POLYLINES DEMO (top area) ===

            // Stroked polygons (top row)
            float polyY = 50f;
            for (int i = 0; i < 4; i++)
            {
                int sides = i + 3; // 3, 4, 5, 6 sides
                float px = 100f + i * 100f;
                float rotation = time * (0.5f + i * 0.2f);
                float hue = (i / 4f + time * 0.1f) % 1f;
                var (pr, pg, pb) = HsvToRgb(hue, 0.9f, 1f);
                Derp.DrawSdfPolygon(px, polyY, 30f, sides, 3f, pr, pg, pb, 0.9f, rotation);
            }

            // Filled polygons (below stroked) - darker versions
            float filledY = 110f;
            for (int i = 0; i < 4; i++)
            {
                int sides = i + 3;
                float px = 100f + i * 100f;
                float rotation = -time * (0.3f + i * 0.15f); // Opposite rotation
                float hue = (i / 4f + time * 0.1f + 0.5f) % 1f;
                var (pr, pg, pb) = HsvToRgb(hue, 0.7f, 0.9f);
                Derp.DrawSdfFilledPolygon(px, filledY, 28f, sides, pr, pg, pb, 0.85f, rotation);
            }

            // Stroked star
            float starRotation = time * 0.5f;
            float starPulse = 1f + MathF.Sin(time * 3f) * 0.2f;
            Derp.DrawSdfStar(screenW - 140f, 50f, 35f * starPulse, 17f * starPulse, 5, 3f, 1f, 0.8f, 0.2f, 0.9f, starRotation);

            // Filled star (next to stroked)
            Derp.DrawSdfFilledStar(screenW - 60f, 50f, 35f * starPulse, 17f * starPulse, 5, 0.2f, 0.8f, 1f, 0.85f, -starRotation);

            // Filled star behind stroked for layering demo
            Derp.DrawSdfFilledStar(screenW - 100f, 110f, 40f, 20f, 6, 0.9f, 0.3f, 0.5f, 0.7f, time * 0.3f);
            Derp.DrawSdfStar(screenW - 100f, 110f, 40f, 20f, 6, 2f, 1f, 1f, 1f, 0.9f, time * 0.3f);

            // === LINES AND BEZIERS DEMO (second row) ===

            // Animated bezier curves
            float bezierY = 140f;
            for (int i = 0; i < 3; i++)
            {
                float baseX = 200f + i * 200f;
                float ctrlOffset = MathF.Sin(time * 2f + i) * 40f;
                float hue = (0.3f + i * 0.2f) % 1f;
                var (br, bg, bb) = HsvToRgb(hue, 0.9f, 1f);

                // Start point, control point, end point
                float x0 = baseX - 60f;
                float y0 = bezierY + 20f;
                float cx = baseX;
                float cy = bezierY - 30f + ctrlOffset;
                float x1 = baseX + 60f;
                float y1 = bezierY + 20f;

                Derp.DrawSdfBezier(x0, y0, cx, cy, x1, y1, 3f, br, bg, bb, 0.85f);

                // Draw control point as small circle
                Derp.DrawSdfCircle(cx, cy, 4f, 1f, 1f, 0.5f, 0.5f);
            }

            // === WAVEFORM/GRAPH DEMO (right side, middle) ===
            // Generate a noise waveform (256 samples)
            for (int i = 0; i < 256; i++)
            {
                float t = i / 256f;
                // Multi-frequency sine wave simulating audio/noise
                waveformSamples[i] = MathF.Sin(t * 20f + time * 5f) * 0.5f
                                   + MathF.Sin(t * 50f + time * 8f) * 0.3f
                                   + MathF.Sin(t * 120f + time * 12f) * 0.2f;
            }
            Derp.DrawSdfWaveform(waveformSamples, screenW - 280f, 250f, screenH * 0.5f, 40f, 2f, 0.2f, 1f, 0.5f, 0.9f);

            // Simulated frame time graph (128 samples)
            for (int i = 0; i < 128; i++)
            {
                // Simulate varying frame times with occasional spikes
                float noise = MathF.Sin(i * 0.3f + time * 2f) * 0.002f;
                float spike = (i % 30 == 0) ? 0.005f * MathF.Abs(MathF.Sin(time + i)) : 0f;
                frameTimeSamples[i] = 0.016f + noise + spike; // ~60fps baseline
            }
            Derp.DrawSdfGraph(frameTimeSamples, screenW - 280f, screenH * 0.5f + 30f, 250f, 60f, 0f, 0.033f, 1.5f, 1f, 0.8f, 0.2f, 0.85f);

            // === EFFECTS DEMO (shadow, glow, stroke, gradient) with WARPS ===
            // Access buffer directly for effect-enabled commands
            var buffer = Derp.SdfBuffer;

            // Row 1: Effects without warps (reference)
            float row1Y = screenH - 180f;

            // Shadow demo - rounded rect with drop shadow
            buffer.PushModifierOffset(4f, 4f);
            buffer.PushModifierFeather(8f);
            buffer.Add(SdfCommand.RoundedRect(
                new System.Numerics.Vector2(80f, row1Y),
                new System.Numerics.Vector2(50f, 35f),
                8f,
                new System.Numerics.Vector4(0f, 0f, 0f, 0.4f)));
            buffer.PopModifier();
            buffer.PopModifier();

            var shadowRect = SdfCommand.RoundedRect(
                new System.Numerics.Vector2(80f, row1Y),
                new System.Numerics.Vector2(50f, 35f),
                8f,
                new System.Numerics.Vector4(0.95f, 0.95f, 0.95f, 1f));
            buffer.Add(shadowRect);

            // Linear gradient circle
            var gradientCircle = SdfCommand.Circle(
                new System.Numerics.Vector2(200f, row1Y),
                30f,
                new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f))
                .WithLinearGradient(new System.Numerics.Vector4(0.3f, 0.3f, 1f, 1f), MathF.PI * 0.5f);
            buffer.Add(gradientCircle);

            // Radial gradient rect
            var radialRect = SdfCommand.RoundedRect(
                new System.Numerics.Vector2(320f, row1Y),
                new System.Numerics.Vector2(45f, 35f),
                8f,
                new System.Numerics.Vector4(1f, 1f, 0.3f, 1f))
                .WithRadialGradient(new System.Numerics.Vector4(0.8f, 0.2f, 0.8f, 1f));
            buffer.Add(radialRect);

            // Row 2: Same effects WITH WAVE WARP
            float row2Y = screenH - 100f;
            Derp.PushSdfWave(frequency: 4f, amplitude: 8f, phase: time * 5f);

            // Shadow + wave
            buffer.PushModifierOffset(4f, 4f);
            buffer.PushModifierFeather(8f);
            buffer.Add(SdfCommand.RoundedRect(
                new System.Numerics.Vector2(80f, row2Y),
                new System.Numerics.Vector2(50f, 35f),
                8f,
                new System.Numerics.Vector4(0f, 0f, 0f, 0.4f)));
            buffer.PopModifier();
            buffer.PopModifier();

            var shadowWave = SdfCommand.RoundedRect(
                new System.Numerics.Vector2(80f, row2Y),
                new System.Numerics.Vector2(50f, 35f),
                8f,
                new System.Numerics.Vector4(0.95f, 0.95f, 0.95f, 1f));
            buffer.Add(shadowWave);

            // Gradient + wave
            var gradientWave = SdfCommand.Circle(
                new System.Numerics.Vector2(200f, row2Y),
                30f,
                new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f))
                .WithLinearGradient(new System.Numerics.Vector4(0.3f, 0.3f, 1f, 1f), MathF.PI * 0.5f);
            buffer.Add(gradientWave);

            // Radial + wave
            var radialWave = SdfCommand.RoundedRect(
                new System.Numerics.Vector2(320f, row2Y),
                new System.Numerics.Vector2(45f, 35f),
                8f,
                new System.Numerics.Vector4(1f, 1f, 0.3f, 1f))
                .WithRadialGradient(new System.Numerics.Vector4(0.8f, 0.2f, 0.8f, 1f));
            buffer.Add(radialWave);

            Derp.PopSdfWarp();

            // Row 2 continued: TWIST WARP
            Derp.PushSdfTwist(strength: MathF.Sin(time * 2f) * 4f);

            // Shadow + twist
            buffer.PushModifierOffset(4f, 4f);
            buffer.PushModifierFeather(8f);
            buffer.Add(SdfCommand.RoundedRect(
                new System.Numerics.Vector2(440f, row2Y),
                new System.Numerics.Vector2(50f, 35f),
                8f,
                new System.Numerics.Vector4(0f, 0f, 0f, 0.4f)));
            buffer.PopModifier();
            buffer.PopModifier();

            var shadowTwist = SdfCommand.RoundedRect(
                new System.Numerics.Vector2(440f, row2Y),
                new System.Numerics.Vector2(50f, 35f),
                8f,
                new System.Numerics.Vector4(0.9f, 0.95f, 1f, 1f))
                .WithLinearGradient(new System.Numerics.Vector4(0.3f, 0.8f, 0.9f, 1f), MathF.PI * 0.25f);
            buffer.Add(shadowTwist);

            Derp.PopSdfWarp();

            // BULGE WARP with effects
            Derp.PushSdfBulge(strength: MathF.Sin(time * 3f) * 40f, radius: 80f);

            // Shadow + gradient + bulge
            buffer.PushModifierOffset(5f, 5f);
            buffer.PushModifierFeather(10f);
            buffer.Add(SdfCommand.Circle(
                new System.Numerics.Vector2(560f, row2Y),
                35f,
                new System.Numerics.Vector4(0f, 0f, 0f, 0.35f)));
            buffer.PopModifier();
            buffer.PopModifier();

            var shadowBulge = SdfCommand.Circle(
                new System.Numerics.Vector2(560f, row2Y),
                35f,
                new System.Numerics.Vector4(0.3f, 0.9f, 0.5f, 1f))
                .WithRadialGradient(new System.Numerics.Vector4(0.9f, 0.9f, 0.3f, 1f))
                .WithGlow(8f);
            buffer.Add(shadowBulge);

            Derp.PopSdfWarp();

            // LATTICE WARP with gradient
            var pulseLattice = SdfLattice.Wave(amplitude: 0.25f, frequency: 2f, phase: time * 4f);
            Derp.PushNewSdfLattice(pulseLattice, scaleX: 120f, scaleY: 120f);

            // Combined: lattice + shadow + gradient + stroke
            buffer.PushModifierOffset(3f, 3f);
            buffer.PushModifierFeather(6f);
            buffer.Add(SdfCommand.RoundedRect(
                new System.Numerics.Vector2(screenW - 100f, row2Y),
                new System.Numerics.Vector2(55f, 40f),
                12f,
                new System.Numerics.Vector4(0f, 0f, 0f, 0.3f)));
            buffer.PopModifier();
            buffer.PopModifier();

            var combined = SdfCommand.RoundedRect(
                new System.Numerics.Vector2(screenW - 100f, row2Y),
                new System.Numerics.Vector2(55f, 40f),
                12f,
                new System.Numerics.Vector4(0.2f, 0.7f, 0.9f, 1f))
                .WithLinearGradient(new System.Numerics.Vector4(0.9f, 0.3f, 0.5f, 1f), time * 0.5f)
                .WithStroke(new System.Numerics.Vector4(1f, 1f, 1f, 0.8f), 2f);
            buffer.Add(combined);

            Derp.PopSdfWarp();

            // === BOOLEAN OPERATIONS DEMO ===
            float boolY = 80f;

            // 1. UNION - combine shapes (like metaballs)
            buffer.BeginGroup(SdfBooleanOp.Union);
            buffer.AddCircle(80f, boolY, 20f, 1f, 0.3f, 0.3f, 1f);
            buffer.AddCircle(110f, boolY, 20f, 1f, 0.3f, 0.3f, 1f);
            buffer.EndGroup(0.3f, 0.3f, 1f, 1f);

            // 2. SUBTRACT - cut hole in shape
            buffer.BeginGroup(SdfBooleanOp.Subtract);
            buffer.AddRoundedRect(200f, boolY, 30f, 25f, 6f, 1f, 0.6f, 0.2f, 1f);
            buffer.AddCircle(200f + MathF.Sin(time * 2f) * 15f, boolY, 15f, 1f, 1f, 1f, 1f);
            buffer.EndGroup(1f, 0.6f, 0.2f, 1f);

            // 3. INTERSECT - show only overlap
            buffer.BeginGroup(SdfBooleanOp.Intersect);
            buffer.AddCircle(310f - 12f + MathF.Sin(time * 1.5f) * 10f, boolY, 22f, 0.2f, 0.8f, 0.4f, 1f);
            buffer.AddCircle(310f + 12f - MathF.Sin(time * 1.5f) * 10f, boolY, 22f, 0.2f, 0.8f, 0.4f, 1f);
            buffer.EndGroup(0.2f, 0.8f, 0.4f, 1f);

            // 4. SMOOTH UNION - blobby metaballs
            buffer.BeginGroup(SdfBooleanOp.SmoothUnion, smoothness: 15f);
            for (int m = 0; m < 4; m++)
            {
                float angle = time * 1.2f + m * MathF.PI * 0.5f;
                float mx = 440f + MathF.Cos(angle) * 25f;
                float my = boolY + MathF.Sin(angle) * 25f;
                buffer.AddCircle(mx, my, 14f, 0.9f, 0.4f, 0.8f, 1f);
            }
            buffer.EndGroup(0.9f, 0.4f, 0.8f, 1f);

            // 5. SMOOTH SUBTRACT - soft carved hole
            buffer.BeginGroup(SdfBooleanOp.SmoothSubtract, smoothness: 12f);
            buffer.AddRoundedRect(560f, boolY, 35f, 28f, 8f, 0.8f, 0.5f, 0.2f, 1f);
            buffer.AddCircle(560f, boolY + MathF.Sin(time * 2.5f) * 8f, 18f, 1f, 1f, 1f, 1f);
            buffer.EndGroup(0.8f, 0.5f, 0.2f, 1f);

            // 6. Nested boolean: (A ∪ B) - C
            buffer.BeginGroup(SdfBooleanOp.Subtract);
            // Inner union
            buffer.BeginGroup(SdfBooleanOp.SmoothUnion, smoothness: 10f);
            buffer.AddCircle(680f - 15f, boolY, 20f, 0.5f, 0.7f, 0.9f, 1f);
            buffer.AddCircle(680f + 15f, boolY, 20f, 0.5f, 0.7f, 0.9f, 1f);
            buffer.EndGroup(0.5f, 0.7f, 0.9f, 1f);
            // Subtract a rectangle
            buffer.AddRect(680f + MathF.Cos(time * 2f) * 10f, boolY, 12f, 30f, 1f, 1f, 1f, 1f);
            buffer.EndGroup(0.5f, 0.7f, 0.9f, 1f);

            // === WARPED GROUPS (row 2) ===
            float boolY2 = 160f;

            // 7. Wavy metaballs - entire group waves together
            Derp.PushSdfWave(frequency: 3f, amplitude: 6f, phase: time * 4f);
            buffer.BeginGroup(SdfBooleanOp.SmoothUnion, smoothness: 12f);
            buffer.AddCircle(80f - 20f, boolY2, 18f, 0.2f, 0.9f, 0.6f, 1f);
            buffer.AddCircle(80f + 20f, boolY2, 18f, 0.2f, 0.9f, 0.6f, 1f);
            buffer.AddCircle(80f, boolY2 - 15f, 14f, 0.2f, 0.9f, 0.6f, 1f);
            buffer.EndGroup(0.2f, 0.9f, 0.6f, 1f);
            Derp.PopSdfWarp();

            // 8. Twisted subtract
            Derp.PushSdfTwist(strength: MathF.Sin(time * 2f) * 3f);
            buffer.BeginGroup(SdfBooleanOp.Subtract);
            buffer.AddRoundedRect(200f, boolY2, 35f, 30f, 8f, 0.9f, 0.5f, 0.3f, 1f);
            buffer.AddCircle(200f, boolY2, 18f, 1f, 1f, 1f, 1f);
            buffer.EndGroup(0.9f, 0.5f, 0.3f, 1f);
            Derp.PopSdfWarp();

            // 9. Bulging intersection
            Derp.PushSdfBulge(strength: MathF.Sin(time * 3f) * 30f, radius: 60f);
            buffer.BeginGroup(SdfBooleanOp.Intersect);
            buffer.AddCircle(320f - 15f, boolY2, 28f, 0.8f, 0.4f, 0.9f, 1f);
            buffer.AddCircle(320f + 15f, boolY2, 28f, 0.8f, 0.4f, 0.9f, 1f);
            buffer.EndGroup(0.8f, 0.4f, 0.9f, 1f);
            Derp.PopSdfWarp();

            // 10. Lattice-warped smooth union
            var groupLattice = SdfLattice.Wave(amplitude: 0.3f, frequency: 2f, phase: time * 3f);
            Derp.PushNewSdfLattice(groupLattice, scaleX: 100f, scaleY: 80f);
            buffer.BeginGroup(SdfBooleanOp.SmoothUnion, smoothness: 10f);
            buffer.AddCircle(440f - 25f, boolY2, 20f, 0.9f, 0.7f, 0.3f, 1f);
            buffer.AddCircle(440f + 25f, boolY2, 20f, 0.9f, 0.7f, 0.3f, 1f);
            buffer.AddCircle(440f, boolY2 + 20f, 16f, 0.9f, 0.7f, 0.3f, 1f);
            buffer.EndGroup(0.9f, 0.7f, 0.3f, 1f);
            Derp.PopSdfWarp();

            // === NO WARP (reference shapes) ===
            // Corner indicators (no warp applied - static reference)
            float boxSize = 25f;
            Derp.DrawSdfRect(boxSize + 5, boxSize + 5, boxSize, boxSize, 0.5f, 0.5f, 0.5f, 0.3f);
            Derp.DrawSdfRect(screenW - boxSize - 5, boxSize + 5, boxSize, boxSize, 0.5f, 0.5f, 0.5f, 0.3f);

            // Small orbiting circles with subtle glow
            for (int i = 0; i < 8; i++)
            {
                float angle = time * 0.5f + i * MathF.PI * 2f / 8f;
                float x = screenW * 0.5f + MathF.Cos(angle) * 180f;
                float y = screenH * 0.4f + MathF.Sin(angle) * 120f;

                // Add with glow effect
                var orbitCircle = SdfCommand.Circle(
                    new System.Numerics.Vector2(x, y),
                    5f,
                    new System.Numerics.Vector4(1f, 1f, 1f, 0.5f))
                    .WithGlow(8f);
                buffer.Add(orbitCircle);
            }

            // === DEBUG TEXT (disabled - conflicts with Arial atlas) ===
            // Derp.DrawDebugText("SDF GLYPH TEXT", 12f, 12f, pixelSize: 24f, r: 1f, g: 1f, b: 1f, a: 1f);
            // Derp.DrawDebugText("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 12f, 44f, pixelSize: 16f, r: 0.9f, g: 0.9f, b: 1f, a: 1f);
            // Derp.DrawDebugText("0123456789", 12f, 64f, pixelSize: 16f, r: 0.9f, g: 1f, b: 0.9f, a: 1f);

            // === ARIAL FONT TEXT ===
            Derp.DrawText(arial, "Hello from Arial!", 12f, 12f, 32f, 0f, 1f, 1f, 0.3f, 1f);
            Derp.DrawText(arial, "The quick brown fox jumps over the lazy dog.", 12f, 50f, 24f, 0f, 1f, 0.8f, 0.6f, 1f);
            Derp.DrawText(arial, $"Frame: {frameCount}  Time: {time:F2}s", 12f, 80f, 18f, 0f, 0.8f, 0.8f, 0.8f, 1f);

            // Render all SDF commands
            Derp.RenderSdf();

            Derp.EndDrawing();

            frameCount++;
            framesSinceLastUpdate++;

            // Update FPS in title
            double elapsed = stopwatch.Elapsed.TotalSeconds;
            if (elapsed - lastFpsUpdate >= 0.5)
            {
                double fps = framesSinceLastUpdate / (elapsed - lastFpsUpdate);
                Derp.SetWindowTitle($"SDF Demo - {fps:F1} FPS | {Derp.SdfCommandCount} shapes");
                lastFpsUpdate = elapsed;
                framesSinceLastUpdate = 0;
            }
        }

        Log.Information("SDF Demo: {Frames} frames in {Seconds:F1}s",
            frameCount, stopwatch.Elapsed.TotalSeconds);

        Derp.CloseWindow();
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
