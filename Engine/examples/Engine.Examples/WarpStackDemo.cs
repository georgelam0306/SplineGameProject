using System;
using Serilog;
using DerpLib.Sdf;

namespace DerpLib.Examples;

/// <summary>
/// Demonstrates stacked SDF warps (multiple Push/Pop applied to the same shape).
/// </summary>
public static class WarpStackDemo
{
    public static void Run()
    {
        Derp.InitWindow(1100, 650, "SDF Warp Stack Demo");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Derp.InitSdf();

        float time = 0f;

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

            float centerY = screenH * 0.42f;
            float startX = 80f;
            float stepX = (screenW - startX * 2f) / 6f;

            // Same base shape, increasing warp stack depth.
            for (int depth = 0; depth <= 6; depth++)
            {
                float x = startX + depth * stepX;
                float hue = (depth * 0.12f + time * 0.08f) % 1f;
                var (r, g, b) = HsvToRgb(hue, 0.85f, 1f);

                for (int warpIndex = 0; warpIndex < depth; warpIndex++)
                {
                    int warpKind = warpIndex % 3;
                    if (warpKind == 0)
                    {
                        Derp.PushSdfWave(frequency: 2.5f + warpIndex * 0.2f, amplitude: 6f + warpIndex * 1.5f, phase: time * 2f);
                    }
                    else if (warpKind == 1)
                    {
                        Derp.PushSdfTwist(strength: MathF.Sin(time * 1.2f + warpIndex) * 2.2f);
                    }
                    else
                    {
                        Derp.PushSdfBulge(strength: MathF.Sin(time * 1.6f + warpIndex) * 25f, radius: 90f);
                    }
                }

                Derp.DrawSdfRoundedRect(x, centerY, 55f, 55f, 14f, r, g, b, 0.9f);
                Derp.DrawSdfCircle(x, centerY, 18f, 1f, 1f, 1f, 0.75f);

                for (int warpIndex = 0; warpIndex < depth; warpIndex++)
                {
                    Derp.PopSdfWarp();
                }
            }

            // Order matters: A(B(p)) vs B(A(p))
            float compareY = screenH * 0.78f;
            float compareX1 = screenW * 0.33f;
            float compareX2 = screenW * 0.67f;

            // Push(A) then Push(B) => A(B(p))
            Derp.PushSdfWave(frequency: 3f, amplitude: 10f, phase: time * 2f); // A
            Derp.PushSdfTwist(strength: 2.8f);                                 // B
            Derp.DrawSdfRoundedRect(compareX1, compareY, 90f, 40f, 16f, 0.3f, 0.8f, 1f, 0.9f);
            Derp.PopSdfWarp();
            Derp.PopSdfWarp();

            // Push(B) then Push(A) => B(A(p))
            Derp.PushSdfTwist(strength: 2.8f);                                 // B
            Derp.PushSdfWave(frequency: 3f, amplitude: 10f, phase: time * 2f); // A
            Derp.DrawSdfRoundedRect(compareX2, compareY, 90f, 40f, 16f, 1f, 0.6f, 0.2f, 0.9f);
            Derp.PopSdfWarp();
            Derp.PopSdfWarp();

            Derp.RenderSdf();
            Derp.EndDrawing();
        }

        Derp.CloseWindow();
    }

    private static (float r, float g, float b) HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs((h * 6f) % 2f - 1f));
        float m = v - c;

        float r1;
        float g1;
        float b1;

        if (h < 1f / 6f)
        {
            r1 = c;
            g1 = x;
            b1 = 0f;
        }
        else if (h < 2f / 6f)
        {
            r1 = x;
            g1 = c;
            b1 = 0f;
        }
        else if (h < 3f / 6f)
        {
            r1 = 0f;
            g1 = c;
            b1 = x;
        }
        else if (h < 4f / 6f)
        {
            r1 = 0f;
            g1 = x;
            b1 = c;
        }
        else if (h < 5f / 6f)
        {
            r1 = x;
            g1 = 0f;
            b1 = c;
        }
        else
        {
            r1 = c;
            g1 = 0f;
            b1 = x;
        }

        return (r1 + m, g1 + m, b1 + m);
    }
}
