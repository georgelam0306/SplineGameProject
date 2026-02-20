using System;
using System.Numerics;
using Serilog;
using DerpLib.Sdf;

namespace DerpLib.Examples;

public static class TrimDashDemo
{
    public static void Run()
    {
        Derp.InitWindow(1100, 650, "SDF Trim + Dash Demo");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Derp.InitSdf();

        float time = 0f;
        var polylinePoints = new Vector2[6];

        while (!Derp.WindowShouldClose())
        {
            Derp.PollEvents();

            if (!Derp.BeginDrawing())
            {
                continue;
            }

            time += 0.016f;

            float screenW = Derp.GetScreenWidth();
            float screenH = Derp.GetScreenHeight();
            var buffer = Derp.SdfBuffer;

            // === 1) Trimmed circle outline: cap styles ===
            float topY = screenH * 0.22f;
            float startX = screenW * 0.18f;
            float stepX = screenW * 0.21f;
            float radius = 46f;
            float strokeWidth = 10f;
            float totalLen = 2f * MathF.PI * radius;
            float trimLen = totalLen * 0.55f;
            float trimOffset = time * 120f;

            DrawTrimmedCircle(buffer, new Vector2(startX + stepX * 0f, topY), radius, strokeWidth, SdfStrokeCap.Butt, trimLen, trimOffset);
            DrawTrimmedCircle(buffer, new Vector2(startX + stepX * 1f, topY), radius, strokeWidth, SdfStrokeCap.Round, trimLen, trimOffset);
            DrawTrimmedCircle(buffer, new Vector2(startX + stepX * 2f, topY), radius, strokeWidth, SdfStrokeCap.Square, trimLen, trimOffset);
            DrawTrimmedCircle(buffer, new Vector2(startX + stepX * 3f, topY), radius, strokeWidth, SdfStrokeCap.Soft, trimLen, trimOffset);

            // === 2) Dashed polyline: constant-speed dashes across segments ===
            float midY = screenH * 0.52f;
            float polylineThickness = 14f;

            polylinePoints[0] = new Vector2(screenW * 0.12f, midY);
            polylinePoints[1] = new Vector2(screenW * 0.26f, midY - 70f);
            polylinePoints[2] = new Vector2(screenW * 0.42f, midY + 55f);
            polylinePoints[3] = new Vector2(screenW * 0.58f, midY - 35f);
            polylinePoints[4] = new Vector2(screenW * 0.72f, midY + 75f);
            polylinePoints[5] = new Vector2(screenW * 0.86f, midY);

            int headerIndex = buffer.AddPolyline(polylinePoints);

            var dash = new SdfStrokeDash(
                dashLengthPx: 46f,
                gapLengthPx: 24f,
                offsetPx: -time * 180f,
                cap: SdfStrokeCap.Round,
                capSoftnessPx: 14f);

            var polyline = SdfCommand.Polyline(headerIndex, polylineThickness, new Vector4(0.15f, 0.85f, 0.75f, 0.95f))
                .WithStrokeDash(in dash);
            buffer.Add(polyline);

            // === 3) Boolean subtract: dashed + trimmed stroke inherits contour s from winner ===
            float boolX = screenW * 0.5f;
            float boolY = screenH * 0.82f;

            buffer.BeginGroup(SdfBooleanOp.Subtract);

            var baseShape = SdfCommand.RoundedRect(new Vector2(boolX, boolY), new Vector2(120f, 56f), 18f, new Vector4(0.1f, 0.2f, 0.4f, 0.12f))
                .WithStroke(new Vector4(0.95f, 0.95f, 1f, 0.9f), 8f);
            buffer.Add(baseShape);

            float holeOffsetX = MathF.Sin(time * 1.2f) * 55f;
            var hole = SdfCommand.Circle(new Vector2(boolX + holeOffsetX, boolY), 34f, new Vector4(0f, 0f, 0f, 0f))
                .WithStroke(new Vector4(0.95f, 0.95f, 1f, 0.9f), 8f);
            buffer.Add(hole);

            var groupTrim = new SdfStrokeTrim(startPx: 0f, lengthPx: 280f, offsetPx: time * 140f, cap: SdfStrokeCap.Round);
            var groupDash = new SdfStrokeDash(dashLengthPx: 42f, gapLengthPx: 18f, offsetPx: -time * 160f, cap: SdfStrokeCap.Butt);
            buffer.EndGroup(
                new Vector4(0.1f, 0.2f, 0.4f, 0.12f),
                new Vector4(0.95f, 0.95f, 1f, 0.9f),
                8f,
                0f,
                SdfCommand.DefaultSoftEdge,
                groupTrim,
                groupDash);

            Derp.RenderSdf();
            Derp.EndDrawing();
        }

        Derp.CloseWindow();
    }

    private static void DrawTrimmedCircle(
        SdfBuffer buffer,
        Vector2 center,
        float radius,
        float strokeWidth,
        SdfStrokeCap cap,
        float trimLengthPx,
        float trimOffsetPx)
    {
        var strokeTrim = new SdfStrokeTrim(
            startPx: 0f,
            lengthPx: trimLengthPx,
            offsetPx: trimOffsetPx,
            cap: cap,
            capSoftnessPx: 18f);

        var cmd = SdfCommand.Circle(center, radius, new Vector4(0f, 0f, 0f, 0f))
            .WithStroke(new Vector4(1f, 1f, 1f, 0.9f), strokeWidth)
            .WithStrokeTrim(in strokeTrim);

        buffer.Add(cmd);
    }
}
