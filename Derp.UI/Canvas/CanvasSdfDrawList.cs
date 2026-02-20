using System.Numerics;
using DerpLib.ImGui.Core;
using DerpLib.Sdf;

namespace Derp.UI;

public readonly struct CanvasSdfDrawList
{
    private readonly SdfBuffer _buffer;

    public CanvasSdfDrawList(SdfBuffer buffer)
    {
        _buffer = buffer;
    }

    public void Add(SdfCommand cmd)
    {
        _buffer.Add(cmd);
    }

    public int CommandCount => _buffer.Count;

    public int AddPolyline(ReadOnlySpan<Vector2> points)
    {
        return _buffer.AddPolyline(points);
    }

    public int AddGradientStops(ReadOnlySpan<SdfGradientStop> stops)
    {
        return _buffer.AddGradientStops(stops);
    }

    public void PushClipRect(float x, float y, float width, float height)
    {
        _buffer.PushClipRect(new Vector4(x, y, width, height));
    }

    public void PopClipRect()
    {
        _buffer.PopClipRect();
    }

    public void PushMask(SdfCommand maskCmd)
    {
        _buffer.PushMask(maskCmd);
    }

    public void PushMask(ReadOnlySpan<SdfCommand> maskCommands)
    {
        _buffer.PushMask(maskCommands);
    }

    public void PushModifierOffset(float offsetX, float offsetY)
    {
        _buffer.PushModifierOffset(offsetX, offsetY);
    }

    public void PushModifierFeather(float radiusPx, SdfFeatherDirection direction = SdfFeatherDirection.Both)
    {
        _buffer.PushModifierFeather(radiusPx, direction);
    }

    public void PushWarp(SdfWarp warp)
    {
        _buffer.PushWarp(warp);
    }

    public void PopModifier()
    {
        _buffer.PopModifier();
    }

    public void PopWarp()
    {
        _buffer.PopWarp();
    }

    public void PopMask()
    {
        _buffer.PopMask();
    }

    public void BeginBooleanGroup(SdfBooleanOp op, float smoothness)
    {
        _buffer.BeginGroup(op, smoothness);
    }

    public void EndBooleanGroup(uint fillColor)
    {
        var clipRect = _buffer.CurrentClipRect;
        _buffer.EndGroupClipped(ImStyle.ToVector4(fillColor), clipRect);
    }

    public void EndBooleanGroup(uint fillColor, uint blendMode)
    {
        var clipRect = _buffer.CurrentClipRect;
        _buffer.EndGroupClipped(ImStyle.ToVector4(fillColor), clipRect, blendMode);
    }

    public void EndBooleanGroup(uint fillColor, uint strokeColor, float strokeWidth, float glowRadius)
    {
        var clipRect = _buffer.CurrentClipRect;
        _buffer.EndGroupClipped(
            ImStyle.ToVector4(fillColor),
            clipRect,
            ImStyle.ToVector4(strokeColor),
            strokeWidth,
            glowRadius);
    }

    public void EndBooleanGroup(uint fillColor, uint strokeColor, float strokeWidth, float glowRadius, uint blendMode)
    {
        var clipRect = _buffer.CurrentClipRect;
        _buffer.EndGroupClipped(
            ImStyle.ToVector4(fillColor),
            clipRect,
            blendMode,
            ImStyle.ToVector4(strokeColor),
            strokeWidth,
            glowRadius);
    }

    public void EndBooleanGroup(uint fillColor, float glowRadius)
    {
        var clipRect = _buffer.CurrentClipRect;
        _buffer.EndGroupClipped(
            ImStyle.ToVector4(fillColor),
            clipRect,
            strokeColor: default,
            strokeWidth: 0f,
            glowRadius: glowRadius);
    }

    public void EndBooleanGroup(uint fillColor, float glowRadius, uint blendMode)
    {
        var clipRect = _buffer.CurrentClipRect;
        _buffer.EndGroupClipped(
            ImStyle.ToVector4(fillColor),
            clipRect,
            blendMode,
            strokeColor: default,
            strokeWidth: 0f,
            glowRadius: glowRadius);
    }

    public void DrawRect(float x, float y, float w, float h, uint color)
    {
        var center = new Vector2(x + w * 0.5f, y + h * 0.5f);
        var halfSize = new Vector2(w * 0.5f, h * 0.5f);
        _buffer.Add(SdfCommand.Rect(center, halfSize, ImStyle.ToVector4(color)));
    }

    public void DrawRect(float x, float y, float w, float h, uint color, float rotationRadians)
    {
        var center = new Vector2(x + w * 0.5f, y + h * 0.5f);
        var halfSize = new Vector2(w * 0.5f, h * 0.5f);
        var cmd = SdfCommand.Rect(center, halfSize, ImStyle.ToVector4(color)).WithRotation(rotationRadians);
        _buffer.Add(cmd);
    }

    public void DrawRoundedRectStroke(float x, float y, float w, float h, float radius, uint color, float strokeWidth)
    {
        var center = new Vector2(x + w * 0.5f, y + h * 0.5f);
        var halfSize = new Vector2(w * 0.5f, h * 0.5f);

        var cmd = radius <= 0.0001f
            ? SdfCommand.Rect(center, halfSize, Vector4.Zero)
            : SdfCommand.RoundedRect(center, halfSize, radius, Vector4.Zero);

        cmd = cmd.WithStroke(ImStyle.ToVector4(color), strokeWidth);
        _buffer.Add(cmd);
    }

    public void DrawRoundedRectPerCorner(float x, float y, float w, float h,
        float radiusTL, float radiusTR, float radiusBR, float radiusBL, uint color, float rotationRadians)
    {
        var center = new Vector2(x + w * 0.5f, y + h * 0.5f);
        var halfSize = new Vector2(w * 0.5f, h * 0.5f);
        var cmd = SdfCommand.RoundedRectPerCorner(center, halfSize, radiusTL, radiusTR, radiusBR, radiusBL, ImStyle.ToVector4(color))
            .WithRotation(rotationRadians);
        _buffer.Add(cmd);
    }

    public void DrawRoundedRectPerCornerStroke(float x, float y, float w, float h,
        float radiusTL, float radiusTR, float radiusBR, float radiusBL, uint color, float strokeWidth, float rotationRadians)
    {
        var center = new Vector2(x + w * 0.5f, y + h * 0.5f);
        var halfSize = new Vector2(w * 0.5f, h * 0.5f);
        var cmd = SdfCommand.RoundedRectPerCorner(center, halfSize, radiusTL, radiusTR, radiusBR, radiusBL, Vector4.Zero)
            .WithStroke(ImStyle.ToVector4(color), strokeWidth)
            .WithRotation(rotationRadians);
        _buffer.Add(cmd);
    }

    public void DrawRoundedRectPerCornerWithShadowAndGlow(float x, float y, float w, float h,
        float radiusTL, float radiusTR, float radiusBR, float radiusBL, uint color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, uint shadowColor, float glowRadius, float rotationRadians)
    {
        var center = new Vector2(x + w * 0.5f, y + h * 0.5f);
        var halfSize = new Vector2(w * 0.5f, h * 0.5f);
        var cmd = SdfCommand.RoundedRectPerCorner(center, halfSize, radiusTL, radiusTR, radiusBR, radiusBL, ImStyle.ToVector4(color))
            .WithGlow(glowRadius)
            .WithRotation(rotationRadians);
        _buffer.Add(cmd);
    }

    public void DrawRoundedRectStroke(float x, float y, float w, float h, float radius, uint color, float strokeWidth, float rotationRadians)
    {
        var center = new Vector2(x + w * 0.5f, y + h * 0.5f);
        var halfSize = new Vector2(w * 0.5f, h * 0.5f);

        var cmd = radius <= 0.0001f
            ? SdfCommand.Rect(center, halfSize, Vector4.Zero)
            : SdfCommand.RoundedRect(center, halfSize, radius, Vector4.Zero);

        cmd = cmd
            .WithStroke(ImStyle.ToVector4(color), strokeWidth)
            .WithRotation(rotationRadians);
        _buffer.Add(cmd);
    }

    public void DrawRectWithShadowAndGlow(float x, float y, float w, float h, uint color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, uint shadowColor, float glowRadius)
    {
        var center = new Vector2(x + w * 0.5f, y + h * 0.5f);
        var halfSize = new Vector2(w * 0.5f, h * 0.5f);
        var cmd = SdfCommand.Rect(center, halfSize, ImStyle.ToVector4(color))
            .WithGlow(glowRadius);
        _buffer.Add(cmd);
    }

    public void DrawRectWithShadowAndGlow(float x, float y, float w, float h, uint color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, uint shadowColor, float glowRadius, float rotationRadians)
    {
        var center = new Vector2(x + w * 0.5f, y + h * 0.5f);
        var halfSize = new Vector2(w * 0.5f, h * 0.5f);
        var cmd = SdfCommand.Rect(center, halfSize, ImStyle.ToVector4(color))
            .WithGlow(glowRadius)
            .WithRotation(rotationRadians);
        _buffer.Add(cmd);
    }

    public void DrawLine(float x0, float y0, float x1, float y1, float thickness, uint color)
    {
        _buffer.Add(SdfCommand.Line(
            new Vector2(x0, y0),
            new Vector2(x1, y1),
            thickness,
            ImStyle.ToVector4(color)));
    }

    public void DrawCircle(float cx, float cy, float radius, uint color)
    {
        _buffer.Add(SdfCommand.Circle(new Vector2(cx, cy), radius, ImStyle.ToVector4(color)));
    }

    public void DrawCircle(float cx, float cy, float radius, uint color, float rotationRadians)
    {
        var cmd = SdfCommand.Circle(new Vector2(cx, cy), radius, ImStyle.ToVector4(color))
            .WithRotation(rotationRadians);
        _buffer.Add(cmd);
    }

    public void DrawCircleWithGlow(float cx, float cy, float radius, uint color, float glowRadius)
    {
        var cmd = SdfCommand.Circle(new Vector2(cx, cy), radius, ImStyle.ToVector4(color))
            .WithGlow(glowRadius);
        _buffer.Add(cmd);
    }

    public void DrawCircleWithGlow(float cx, float cy, float radius, uint color, float glowRadius, float rotationRadians)
    {
        var cmd = SdfCommand.Circle(new Vector2(cx, cy), radius, ImStyle.ToVector4(color))
            .WithGlow(glowRadius)
            .WithRotation(rotationRadians);
        _buffer.Add(cmd);
    }

    public void DrawCircleWithShadowAndGlow(float cx, float cy, float radius, uint color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, uint shadowColor, float glowRadius)
    {
        var cmd = SdfCommand.Circle(new Vector2(cx, cy), radius, ImStyle.ToVector4(color))
            .WithGlow(glowRadius);
        _buffer.Add(cmd);
    }

    public void DrawCircleWithShadowAndGlow(float cx, float cy, float radius, uint color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, uint shadowColor, float glowRadius, float rotationRadians)
    {
        var cmd = SdfCommand.Circle(new Vector2(cx, cy), radius, ImStyle.ToVector4(color))
            .WithGlow(glowRadius)
            .WithRotation(rotationRadians);
        _buffer.Add(cmd);
    }

    public void DrawCircleStroke(float cx, float cy, float radius, uint color, float strokeWidth)
    {
        var cmd = SdfCommand.Circle(new Vector2(cx, cy), radius, Vector4.Zero)
            .WithStroke(ImStyle.ToVector4(color), strokeWidth);
        _buffer.Add(cmd);
    }

    public void DrawCircleStroke(float cx, float cy, float radius, uint color, float strokeWidth, float rotationRadians)
    {
        var cmd = SdfCommand.Circle(new Vector2(cx, cy), radius, Vector4.Zero)
            .WithStroke(ImStyle.ToVector4(color), strokeWidth)
            .WithRotation(rotationRadians);
        _buffer.Add(cmd);
    }

    public void DrawFilledPolygon(ReadOnlySpan<Vector2> points, uint color)
    {
        if (points.Length < 3)
        {
            return;
        }

        int headerIndex = _buffer.AddPolyline(points);
        _buffer.Add(SdfCommand.FilledPolygon(headerIndex, ImStyle.ToVector4(color)));
    }

    public void DrawFilledPolygonWithShadowAndGlow(ReadOnlySpan<Vector2> points, uint color,
        float shadowOffsetX, float shadowOffsetY, float shadowRadius, uint shadowColor, float glowRadius)
    {
        if (points.Length < 3)
        {
            return;
        }

        int headerIndex = _buffer.AddPolyline(points);
        var cmd = SdfCommand.FilledPolygon(headerIndex, ImStyle.ToVector4(color))
            .WithGlow(glowRadius);
        _buffer.Add(cmd);
    }

    public void DrawPolyline(ReadOnlySpan<Vector2> points, float thickness, uint color)
    {
        if (points.Length < 2)
        {
            return;
        }

        int headerIndex = _buffer.AddPolyline(points);
        var colorVec = ImStyle.ToVector4(color);
        _buffer.AddPolylineCommand(headerIndex, thickness, colorVec.X, colorVec.Y, colorVec.Z, colorVec.W);
    }
}
