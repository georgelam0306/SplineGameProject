using System.Numerics;

namespace DerpLib.Text;

public sealed class Font
{
    public Font(
        Rendering.Texture atlas,
        FontGlyph[] glyphs,
        int firstCodepoint,
        int baseSizePixels,
        float lineHeightPixels,
        float ascentPixels,
        float descentPixels)
    {
        Atlas = atlas;
        Glyphs = glyphs;
        FirstCodepoint = firstCodepoint;
        BaseSizePixels = baseSizePixels;
        LineHeightPixels = lineHeightPixels;
        AscentPixels = ascentPixels;
        DescentPixels = descentPixels;
    }

    public Rendering.Texture Atlas { get; }
    public FontGlyph[] Glyphs { get; }
    public int FirstCodepoint { get; }
    public int BaseSizePixels { get; }
    public float LineHeightPixels { get; }
    public float AscentPixels { get; }
    public float DescentPixels { get; }

    public Font? BoldVariant { get; private set; }
    public Font? ItalicVariant { get; private set; }
    public Font? BoldItalicVariant { get; private set; }

    public bool TryGetGlyph(char c, out FontGlyph glyph)
    {
        int index = c - FirstCodepoint;
        if ((uint)index >= (uint)Glyphs.Length)
        {
            glyph = default;
            return false;
        }

        glyph = Glyphs[index];
        return true;
    }

    public Vector2 MeasureText(ReadOnlySpan<char> text, float fontSizePixels, float spacingPixels = 0)
    {
        if (text.Length == 0)
            return Vector2.Zero;

        float scale = fontSizePixels / BaseSizePixels;
        float width = 0f;

        for (int i = 0; i < text.Length; i++)
        {
            if (!TryGetGlyph(text[i], out var glyph))
            {
                continue;
            }
            width += glyph.AdvanceX * scale + spacingPixels;
        }

        if (text.Length > 0 && spacingPixels != 0)
            width -= spacingPixels;

        float height = LineHeightPixels * scale;
        return new Vector2(width, height);
    }

    public void SetStyleVariants(Font? boldVariant, Font? italicVariant, Font? boldItalicVariant)
    {
        BoldVariant = boldVariant;
        ItalicVariant = italicVariant;
        BoldItalicVariant = boldItalicVariant;
    }
}
