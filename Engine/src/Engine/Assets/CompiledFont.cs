using System.Runtime.InteropServices;
using DerpLib.AssetPipeline;
using DerpLib.Text;

namespace DerpLib.Assets;

/// <summary>
/// A compiled font asset containing a glyph atlas and metrics.
/// Produced by the build pipeline, loaded at runtime.
/// </summary>
public sealed class CompiledFont : IAsset
{
    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// Raw RGBA8 pixel data (4 bytes per pixel).
    /// Alpha channel contains the glyph mask.
    /// </summary>
    public byte[] Pixels { get; set; } = Array.Empty<byte>();

    public int FirstCodepoint { get; set; }

    public int BaseSizePixels { get; set; }

    public float LineHeightPixels { get; set; }

    public float AscentPixels { get; set; }

    public float DescentPixels { get; set; }

    /// <summary>
    /// Glyphs serialized as raw bytes (FontGlyph is blittable).
    /// Use GetGlyphs() to decode.
    /// </summary>
    public byte[] GlyphData { get; set; } = Array.Empty<byte>();

    // Optional style variants baked into the same asset.
    // Empty pixel/glyph arrays mean the variant is not present.
    public int BoldWidth { get; set; }
    public int BoldHeight { get; set; }
    public byte[] BoldPixels { get; set; } = Array.Empty<byte>();
    public int BoldFirstCodepoint { get; set; }
    public int BoldBaseSizePixels { get; set; }
    public float BoldLineHeightPixels { get; set; }
    public float BoldAscentPixels { get; set; }
    public float BoldDescentPixels { get; set; }
    public byte[] BoldGlyphData { get; set; } = Array.Empty<byte>();

    public int ItalicWidth { get; set; }
    public int ItalicHeight { get; set; }
    public byte[] ItalicPixels { get; set; } = Array.Empty<byte>();
    public int ItalicFirstCodepoint { get; set; }
    public int ItalicBaseSizePixels { get; set; }
    public float ItalicLineHeightPixels { get; set; }
    public float ItalicAscentPixels { get; set; }
    public float ItalicDescentPixels { get; set; }
    public byte[] ItalicGlyphData { get; set; } = Array.Empty<byte>();

    public int BoldItalicWidth { get; set; }
    public int BoldItalicHeight { get; set; }
    public byte[] BoldItalicPixels { get; set; } = Array.Empty<byte>();
    public int BoldItalicFirstCodepoint { get; set; }
    public int BoldItalicBaseSizePixels { get; set; }
    public float BoldItalicLineHeightPixels { get; set; }
    public float BoldItalicAscentPixels { get; set; }
    public float BoldItalicDescentPixels { get; set; }
    public byte[] BoldItalicGlyphData { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Decodes the GlyphData byte array into FontGlyph structs.
    /// </summary>
    public FontGlyph[] GetGlyphs()
    {
        if (GlyphData.Length == 0)
            return Array.Empty<FontGlyph>();

        int glyphSize = Marshal.SizeOf<FontGlyph>();
        int count = GlyphData.Length / glyphSize;
        var glyphs = new FontGlyph[count];

        var span = MemoryMarshal.Cast<byte, FontGlyph>(GlyphData.AsSpan());
        span.CopyTo(glyphs);

        return glyphs;
    }

    /// <summary>
    /// Encodes FontGlyph structs into the GlyphData byte array.
    /// </summary>
    public void SetGlyphs(FontGlyph[] glyphs)
    {
        if (glyphs.Length == 0)
        {
            GlyphData = Array.Empty<byte>();
            return;
        }

        int glyphSize = Marshal.SizeOf<FontGlyph>();
        GlyphData = new byte[glyphs.Length * glyphSize];

        var byteSpan = MemoryMarshal.AsBytes(glyphs.AsSpan());
        byteSpan.CopyTo(GlyphData);
    }

    public FontGlyph[] GetBoldGlyphs() => DecodeGlyphs(BoldGlyphData);
    public FontGlyph[] GetItalicGlyphs() => DecodeGlyphs(ItalicGlyphData);
    public FontGlyph[] GetBoldItalicGlyphs() => DecodeGlyphs(BoldItalicGlyphData);

    public void SetBoldGlyphs(FontGlyph[] glyphs)
    {
        EncodeGlyphs(glyphs, out var data);
        BoldGlyphData = data;
    }

    public void SetItalicGlyphs(FontGlyph[] glyphs)
    {
        EncodeGlyphs(glyphs, out var data);
        ItalicGlyphData = data;
    }

    public void SetBoldItalicGlyphs(FontGlyph[] glyphs)
    {
        EncodeGlyphs(glyphs, out var data);
        BoldItalicGlyphData = data;
    }

    private static FontGlyph[] DecodeGlyphs(byte[] glyphData)
    {
        if (glyphData.Length == 0)
        {
            return Array.Empty<FontGlyph>();
        }

        int glyphSize = Marshal.SizeOf<FontGlyph>();
        int count = glyphData.Length / glyphSize;
        var glyphs = new FontGlyph[count];

        var span = MemoryMarshal.Cast<byte, FontGlyph>(glyphData.AsSpan());
        span.CopyTo(glyphs);

        return glyphs;
    }

    private static void EncodeGlyphs(FontGlyph[] glyphs, out byte[] glyphData)
    {
        if (glyphs.Length == 0)
        {
            glyphData = Array.Empty<byte>();
            return;
        }

        int glyphSize = Marshal.SizeOf<FontGlyph>();
        glyphData = new byte[glyphs.Length * glyphSize];
        var byteSpan = MemoryMarshal.AsBytes(glyphs.AsSpan());
        byteSpan.CopyTo(glyphData);
    }
}
