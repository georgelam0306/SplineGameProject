using System;
using System.Numerics;
using DerpLib;
using DerpLib.Sdf;
using GyrussClone.Simulation.Ecs;
using FixedMath;

namespace GyrussClone;

public static class GyrussRenderer
{
    public static Vector2 PolarToScreen(float angle, float radius, float centerX, float centerY)
    {
        float x = centerX + MathF.Cos(angle) * radius;
        float y = centerY + MathF.Sin(angle) * radius;
        return new Vector2(x, y);
    }

    /// <summary>
    /// Perspective scale: objects near center appear small (far away), objects near edge appear large (close).
    /// Returns a multiplier in [minScale, 1.0] based on radius relative to maxRadius.
    /// </summary>
    private static float PerspectiveScale(float radius, float maxRadius, float minScale = 0.3f)
    {
        float t = MathF.Min(radius / maxRadius, 1f);
        return minScale + (1f - minScale) * t;
    }

    public static void DrawPlayfield(SdfBuffer buffer, float cx, float cy, float playerRadius, float scale)
    {
        // Outer orbit ring (where player lives)
        buffer.Add(
            SdfCommand.Circle(new Vector2(cx, cy), playerRadius, new Vector4(0f, 0f, 0f, 0f))
                .WithStroke(new Vector4(0.3f, 0.5f, 1.0f, 0.6f), 2f * scale)
                .WithGlow(8f * scale));

        // Guide rings (inner rings for visual depth — closer rings are dimmer/thinner)
        for (int i = 1; i <= 5; i++)
        {
            float r = playerRadius * i / 6f;
            float pScale = PerspectiveScale(r, playerRadius);
            float alpha = 0.08f + 0.06f * pScale;
            float strokeW = (0.5f + 0.5f * pScale) * scale;
            buffer.Add(
                SdfCommand.Circle(new Vector2(cx, cy), r, new Vector4(0f, 0f, 0f, 0f))
                    .WithStroke(new Vector4(0.2f, 0.3f, 0.5f, alpha), strokeW));
        }

        // Center dot (vanishing point)
        buffer.Add(
            SdfCommand.Circle(new Vector2(cx, cy), 3f * scale, new Vector4(0.4f, 0.5f, 0.9f, 0.6f))
                .WithGlow(4f * scale));
    }

    public static void DrawPlayer(SdfBuffer buffer, float cx, float cy, float playerAngle, float playerRadius, float scale)
    {
        var pos = PolarToScreen(playerAngle, playerRadius, cx, cy);
        float inwardAngle = playerAngle + MathF.PI;

        float shipSize = 12f * scale;

        // Ship body: bright green circle with stroke
        buffer.Add(
            SdfCommand.Circle(pos, shipSize * 0.6f, new Vector4(0.15f, 0.8f, 0.25f, 0.9f))
                .WithStroke(new Vector4(0.3f, 1.0f, 0.4f, 1.0f), 1.5f * scale)
                .WithGlow(6f * scale));

        // Direction indicator: two lines forming a "V" nose pointing inward
        float noseLen = shipSize * 1.2f;
        float wingSpread = 0.4f; // radians

        var nose = new Vector2(
            pos.X + MathF.Cos(inwardAngle) * noseLen,
            pos.Y + MathF.Sin(inwardAngle) * noseLen);
        var wingL = new Vector2(
            pos.X + MathF.Cos(inwardAngle + wingSpread) * shipSize * 0.5f,
            pos.Y + MathF.Sin(inwardAngle + wingSpread) * shipSize * 0.5f);
        var wingR = new Vector2(
            pos.X + MathF.Cos(inwardAngle - wingSpread) * shipSize * 0.5f,
            pos.Y + MathF.Sin(inwardAngle - wingSpread) * shipSize * 0.5f);

        var lineColor = new Vector4(0.4f, 1.0f, 0.5f, 1.0f);
        float lineThick = 2.5f * scale;
        buffer.Add(SdfCommand.Line(wingL, nose, lineThick, lineColor).WithGlow(3f * scale));
        buffer.Add(SdfCommand.Line(wingR, nose, lineThick, lineColor).WithGlow(3f * scale));

        // Tail line extending outward
        var tail = new Vector2(
            pos.X - MathF.Cos(inwardAngle) * shipSize * 0.5f,
            pos.Y - MathF.Sin(inwardAngle) * shipSize * 0.5f);
        buffer.Add(SdfCommand.Line(pos, tail, 1.5f * scale, new Vector4(0.2f, 0.6f, 0.3f, 0.7f)));
    }

    public static void DrawEnemies(SdfBuffer buffer, SimEcsWorld world, float cx, float cy, float scale)
    {
        float maxRadius = world.PlayerRadius.ToFloat() * scale;

        for (int row = 0; row < world.Enemy.Count; row++)
        {
            ref var transform = ref world.Enemy.PolarTransform(row);
            ref var enemy = ref world.Enemy.Enemy(row);

            if (enemy.Health <= 0)
                continue;

            float angle = transform.Angle.ToFloat();
            float radius = transform.Radius.ToFloat() * scale;
            var pos = PolarToScreen(angle, radius, cx, cy);

            // Perspective: enemies near center are small, near edge are large
            float pScale = PerspectiveScale(radius, maxRadius);
            float baseSize = 8f * scale * pScale;

            // Color + style based on enemy type
            Vector4 fillColor, strokeColor;
            float glowR;
            switch (enemy.EnemyType)
            {
                case 0: // Red enemy
                    fillColor = new Vector4(0.9f, 0.15f, 0.1f, 0.85f);
                    strokeColor = new Vector4(1.0f, 0.4f, 0.3f, 1.0f);
                    glowR = 4f * scale * pScale;
                    break;
                case 1: // Orange enemy
                    fillColor = new Vector4(0.9f, 0.45f, 0.05f, 0.85f);
                    strokeColor = new Vector4(1.0f, 0.7f, 0.2f, 1.0f);
                    glowR = 5f * scale * pScale;
                    break;
                default: // Purple enemy
                    fillColor = new Vector4(0.75f, 0.15f, 0.7f, 0.85f);
                    strokeColor = new Vector4(1.0f, 0.4f, 0.9f, 1.0f);
                    glowR = 6f * scale * pScale;
                    break;
            }

            // Enemy as circle with colored stroke + glow
            buffer.Add(
                SdfCommand.Circle(pos, baseSize, fillColor)
                    .WithStroke(strokeColor, 1.5f * scale * pScale)
                    .WithGlow(glowR));
        }
    }

    public static void DrawBullets(SdfBuffer buffer, SimEcsWorld world, float cx, float cy, float scale)
    {
        float maxRadius = world.PlayerRadius.ToFloat() * scale;

        for (int row = 0; row < world.PlayerBullet.Count; row++)
        {
            ref var transform = ref world.PlayerBullet.PolarTransform(row);

            float angle = transform.Angle.ToFloat();
            float radius = transform.Radius.ToFloat() * scale;
            var pos = PolarToScreen(angle, radius, cx, cy);

            // Perspective: bullets shrink as they fly inward
            float pScale = PerspectiveScale(radius, maxRadius);
            float bulletSize = 3.5f * scale * pScale;

            // Bright yellow-white with glow
            buffer.Add(
                SdfCommand.Circle(pos, bulletSize, new Vector4(1.0f, 0.95f, 0.5f, 1.0f))
                    .WithGlow(3f * scale * pScale));
        }
    }
}
