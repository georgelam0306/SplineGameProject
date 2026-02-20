using DerpLib.Assets;
using DerpLib.Rendering;

namespace DerpLib.Text;

/// <summary>
/// Loads a CompiledFont asset into a runtime Font.
/// </summary>
public static class FontLoader
{
    /// <summary>
    /// Creates a Font from a CompiledFont by uploading the atlas texture to the GPU.
    /// </summary>
    public static Font Load(CompiledFont compiled, TextureArray textures)
    {
        var regularFont = CreateFaceFont(
            textures,
            compiled.Pixels,
            compiled.Width,
            compiled.Height,
            compiled.GetGlyphs(),
            compiled.FirstCodepoint,
            compiled.BaseSizePixels,
            compiled.LineHeightPixels,
            compiled.AscentPixels,
            compiled.DescentPixels);

        Font? boldFont = CreateOptionalFaceFont(
            textures,
            compiled.BoldPixels,
            compiled.BoldWidth,
            compiled.BoldHeight,
            compiled.GetBoldGlyphs(),
            compiled.BoldFirstCodepoint,
            compiled.BoldBaseSizePixels,
            compiled.BoldLineHeightPixels,
            compiled.BoldAscentPixels,
            compiled.BoldDescentPixels);

        Font? italicFont = CreateOptionalFaceFont(
            textures,
            compiled.ItalicPixels,
            compiled.ItalicWidth,
            compiled.ItalicHeight,
            compiled.GetItalicGlyphs(),
            compiled.ItalicFirstCodepoint,
            compiled.ItalicBaseSizePixels,
            compiled.ItalicLineHeightPixels,
            compiled.ItalicAscentPixels,
            compiled.ItalicDescentPixels);

        Font? boldItalicFont = CreateOptionalFaceFont(
            textures,
            compiled.BoldItalicPixels,
            compiled.BoldItalicWidth,
            compiled.BoldItalicHeight,
            compiled.GetBoldItalicGlyphs(),
            compiled.BoldItalicFirstCodepoint,
            compiled.BoldItalicBaseSizePixels,
            compiled.BoldItalicLineHeightPixels,
            compiled.BoldItalicAscentPixels,
            compiled.BoldItalicDescentPixels);

        regularFont.SetStyleVariants(boldFont, italicFont, boldItalicFont);
        return regularFont;
    }

    private static Font CreateFaceFont(
        TextureArray textures,
        byte[] pixels,
        int width,
        int height,
        FontGlyph[] glyphs,
        int firstCodepoint,
        int baseSizePixels,
        float lineHeightPixels,
        float ascentPixels,
        float descentPixels)
    {
        var texture = textures.LoadTexture(pixels, width, height);

        return new Font(
            texture,
            glyphs,
            firstCodepoint,
            baseSizePixels,
            lineHeightPixels,
            ascentPixels,
            descentPixels);
    }

    private static Font? CreateOptionalFaceFont(
        TextureArray textures,
        byte[] pixels,
        int width,
        int height,
        FontGlyph[] glyphs,
        int firstCodepoint,
        int baseSizePixels,
        float lineHeightPixels,
        float ascentPixels,
        float descentPixels)
    {
        if (pixels.Length == 0 || glyphs.Length == 0 || width <= 0 || height <= 0 || baseSizePixels <= 0)
        {
            return null;
        }

        return CreateFaceFont(
            textures,
            pixels,
            width,
            height,
            glyphs,
            firstCodepoint,
            baseSizePixels,
            lineHeightPixels,
            ascentPixels,
            descentPixels);
    }
}
