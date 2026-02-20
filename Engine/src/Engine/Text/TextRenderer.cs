using System.Numerics;
using DerpLib.Sdf;

namespace DerpLib.Text;

/// <summary>
/// Renders text using SDF glyph commands.
/// </summary>
public static class TextRenderer
{
    /// <summary>
    /// Draws text at the specified position using SDF glyph rendering.
    /// </summary>
    /// <param name="buffer">SDF buffer to add glyph commands to.</param>
    /// <param name="font">The font to use.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="x">X position (left edge).</param>
    /// <param name="y">Y position (top edge).</param>
    /// <param name="fontSize">Font size in pixels.</param>
    /// <param name="r">Red component (0-1).</param>
    /// <param name="g">Green component (0-1).</param>
    /// <param name="b">Blue component (0-1).</param>
    /// <param name="a">Alpha component (0-1).</param>
    /// <param name="spacing">Additional spacing between characters.</param>
    /// <param name="clipRect">Optional clip rect (x, y, width, height). Default = no clip.</param>
    public static void DrawText(
        SdfBuffer buffer,
        Font font,
        ReadOnlySpan<char> text,
        float x,
        float y,
        float fontSize,
        float r = 1f,
        float g = 1f,
        float b = 1f,
        float a = 1f,
        float spacing = 0f,
        float strokeWidth = 0f,
        Vector4 strokeColor = default,
        float glowRadius = 0f,
        float shadowOffsetX = 0f,
        float shadowOffsetY = 0f,
        float shadowBlur = 0f,
        Vector4 shadowColor = default,
        Vector4 clipRect = default)
    {
        if (text.Length == 0)
        {
            return;
        }

        bool hasOffset = shadowOffsetX != 0f || shadowOffsetY != 0f;
        bool hasFeather = shadowBlur > 0.0001f;

        if (hasOffset)
        {
            buffer.PushModifierOffset(shadowOffsetX, shadowOffsetY);
        }

        if (hasFeather)
        {
            buffer.PushModifierFeather(shadowBlur);
        }

        // Begin text group - glyphs will be grouped and treated as a single SDF shape
        buffer.BeginTextGroup(r, g, b, a, clipRect);

        float scale = fontSize / font.BaseSizePixels;
        float cursorX = x;

        // Y is top of text box, adjust for ascent to get baseline
        float baselineY = y + font.AscentPixels * scale;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (!font.TryGetGlyph(c, out var glyph))
            {
                // Use space advance for unknown characters
                if (font.TryGetGlyph(' ', out var spaceGlyph))
                    cursorX += spaceGlyph.AdvanceX * scale + spacing;
                continue;
            }

            // Calculate screen position for this glyph
            // OffsetY is typically negative for glyphs above baseline
            float glyphX = cursorX + glyph.OffsetX * scale;
            float glyphY = baselineY + glyph.OffsetY * scale;
            float glyphW = glyph.Width * scale;
            float glyphH = glyph.Height * scale;

            // Only draw if glyph has size (skip spaces)
            if (glyph.Width > 0 && glyph.Height > 0)
            {
                // AddGlyph takes center position and half-size
                float centerX = glyphX + glyphW * 0.5f;
                float centerY = glyphY + glyphH * 0.5f;
                float halfW = glyphW * 0.5f;
                float halfH = glyphH * 0.5f;

                buffer.AddGlyph(
                    centerX, centerY,
                    halfW, halfH,
                    glyph.U0, glyph.V0, glyph.U1, glyph.V1,
                    r, g, b, a);
            }

            // Advance cursor
            cursorX += glyph.AdvanceX * scale + spacing;
        }

        // End text group - emits TextGroup command with warp and effects
        buffer.EndTextGroup(strokeColor, strokeWidth, glowRadius);

        if (hasFeather)
        {
            buffer.PopModifier();
        }

        if (hasOffset)
        {
            buffer.PopModifier();
        }
    }

    /// <summary>
    /// Draws text at the specified position using a Vector2.
    /// </summary>
    public static void DrawText(
        SdfBuffer buffer,
        Font font,
        ReadOnlySpan<char> text,
        Vector2 position,
        float fontSize,
        float r = 1f,
        float g = 1f,
        float b = 1f,
        float a = 1f,
        float spacing = 0f,
        float strokeWidth = 0f,
        Vector4 strokeColor = default,
        float glowRadius = 0f,
        float shadowOffsetX = 0f,
        float shadowOffsetY = 0f,
        float shadowBlur = 0f,
        Vector4 shadowColor = default)
    {
        DrawText(buffer, font, text, position.X, position.Y, fontSize, r, g, b, a, spacing,
            strokeWidth, strokeColor, glowRadius,
            shadowOffsetX, shadowOffsetY, shadowBlur, shadowColor);
    }

    /// <summary>
    /// Draws text with a Vector4 color.
    /// </summary>
    public static void DrawText(
        SdfBuffer buffer,
        Font font,
        ReadOnlySpan<char> text,
        float x,
        float y,
        float fontSize,
        Vector4 color,
        float spacing = 0f,
        float strokeWidth = 0f,
        Vector4 strokeColor = default,
        float glowRadius = 0f,
        float shadowOffsetX = 0f,
        float shadowOffsetY = 0f,
        float shadowBlur = 0f,
        Vector4 shadowColor = default)
    {
        DrawText(buffer, font, text, x, y, fontSize, color.X, color.Y, color.Z, color.W, spacing,
            strokeWidth, strokeColor, glowRadius,
            shadowOffsetX, shadowOffsetY, shadowBlur, shadowColor);
    }
}
