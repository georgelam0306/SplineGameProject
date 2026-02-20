using DerpLib.AssetPipeline;
using DerpLib.Assets;
using DerpLib.Text;
using Serilog;
using StbTrueTypeSharp;

namespace DerpLib.Build;

[Compiler(typeof(FontAsset))]
public sealed class FontCompiler : IAssetCompiler
{
    private const int GlyphPadding = 2;
    private const int SdfPadding = 16;     // Extra padding for SDF field to extend beyond glyph (supports ~16px effects)
    private const byte SdfOnEdge = 128;    // Value at the glyph edge (128 = middle of 0-255 range)
    private const float SdfPixelDistScale = 8.0f;  // Distance field scale (lower = wider range for effects)

    private readonly ILogger _log;

    public FontCompiler(ILogger log)
    {
        _log = log;
    }

    public IEnumerable<string> GetInputFiles(AssetItem item)
    {
        var asset = (FontAsset)item.Asset;
        yield return asset.Source;
        if (!string.IsNullOrWhiteSpace(asset.BoldSource))
        {
            yield return asset.BoldSource;
        }
        if (!string.IsNullOrWhiteSpace(asset.ItalicSource))
        {
            yield return asset.ItalicSource;
        }
        if (!string.IsNullOrWhiteSpace(asset.BoldItalicSource))
        {
            yield return asset.BoldItalicSource;
        }
    }

    public unsafe ObjectId Compile(AssetItem item, IObjectDatabase db, IBlobSerializer serializer)
    {
        var asset = (FontAsset)item.Asset;

        int atlasSize = asset.AtlasSizePixels;
        int firstCodepoint = asset.FirstCodepoint;
        int lastCodepoint = asset.LastCodepoint;
        int fontSizePixels = asset.FontSizePixels;

        if (atlasSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(asset.AtlasSizePixels));
        if (fontSizePixels <= 0)
            throw new ArgumentOutOfRangeException(nameof(asset.FontSizePixels));
        if (firstCodepoint < 0 || firstCodepoint > 0x10FFFF)
            throw new ArgumentOutOfRangeException(nameof(asset.FirstCodepoint));
        if (lastCodepoint < firstCodepoint || lastCodepoint > 0x10FFFF)
            throw new ArgumentOutOfRangeException(nameof(asset.LastCodepoint));

        var regularFace = CompileFace(asset.Source, atlasSize, firstCodepoint, lastCodepoint, fontSizePixels);

        var compiled = new CompiledFont
        {
            Width = regularFace.Width,
            Height = regularFace.Height,
            Pixels = regularFace.Pixels,
            FirstCodepoint = regularFace.FirstCodepoint,
            BaseSizePixels = regularFace.BaseSizePixels,
            LineHeightPixels = regularFace.LineHeightPixels,
            AscentPixels = regularFace.AscentPixels,
            DescentPixels = regularFace.DescentPixels,
        };
        compiled.SetGlyphs(regularFace.Glyphs);

        if (!string.IsNullOrWhiteSpace(asset.BoldSource))
        {
            var boldFace = CompileFace(asset.BoldSource, atlasSize, firstCodepoint, lastCodepoint, fontSizePixels);
            compiled.BoldWidth = boldFace.Width;
            compiled.BoldHeight = boldFace.Height;
            compiled.BoldPixels = boldFace.Pixels;
            compiled.BoldFirstCodepoint = boldFace.FirstCodepoint;
            compiled.BoldBaseSizePixels = boldFace.BaseSizePixels;
            compiled.BoldLineHeightPixels = boldFace.LineHeightPixels;
            compiled.BoldAscentPixels = boldFace.AscentPixels;
            compiled.BoldDescentPixels = boldFace.DescentPixels;
            compiled.SetBoldGlyphs(boldFace.Glyphs);
        }

        if (!string.IsNullOrWhiteSpace(asset.ItalicSource))
        {
            var italicFace = CompileFace(asset.ItalicSource, atlasSize, firstCodepoint, lastCodepoint, fontSizePixels);
            compiled.ItalicWidth = italicFace.Width;
            compiled.ItalicHeight = italicFace.Height;
            compiled.ItalicPixels = italicFace.Pixels;
            compiled.ItalicFirstCodepoint = italicFace.FirstCodepoint;
            compiled.ItalicBaseSizePixels = italicFace.BaseSizePixels;
            compiled.ItalicLineHeightPixels = italicFace.LineHeightPixels;
            compiled.ItalicAscentPixels = italicFace.AscentPixels;
            compiled.ItalicDescentPixels = italicFace.DescentPixels;
            compiled.SetItalicGlyphs(italicFace.Glyphs);
        }

        if (!string.IsNullOrWhiteSpace(asset.BoldItalicSource))
        {
            var boldItalicFace = CompileFace(asset.BoldItalicSource, atlasSize, firstCodepoint, lastCodepoint, fontSizePixels);
            compiled.BoldItalicWidth = boldItalicFace.Width;
            compiled.BoldItalicHeight = boldItalicFace.Height;
            compiled.BoldItalicPixels = boldItalicFace.Pixels;
            compiled.BoldItalicFirstCodepoint = boldItalicFace.FirstCodepoint;
            compiled.BoldItalicBaseSizePixels = boldItalicFace.BaseSizePixels;
            compiled.BoldItalicLineHeightPixels = boldItalicFace.LineHeightPixels;
            compiled.BoldItalicAscentPixels = boldItalicFace.AscentPixels;
            compiled.BoldItalicDescentPixels = boldItalicFace.DescentPixels;
            compiled.SetBoldItalicGlyphs(boldItalicFace.Glyphs);
        }

        _log.Information(
            "Compiled font {Font} -> regular:{GlyphCount} bold:{HasBold} italic:{HasItalic} boldItalic:{HasBoldItalic}",
            Path.GetFileName(asset.Source),
            regularFace.Glyphs.Length,
            !string.IsNullOrWhiteSpace(asset.BoldSource),
            !string.IsNullOrWhiteSpace(asset.ItalicSource),
            !string.IsNullOrWhiteSpace(asset.BoldItalicSource));

        return db.Put(serializer.Serialize(compiled));
    }

    private readonly struct CompiledFaceData
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public byte[] Pixels { get; init; }
        public int FirstCodepoint { get; init; }
        public int BaseSizePixels { get; init; }
        public float LineHeightPixels { get; init; }
        public float AscentPixels { get; init; }
        public float DescentPixels { get; init; }
        public FontGlyph[] Glyphs { get; init; }
    }

    private unsafe CompiledFaceData CompileFace(
        string sourceFile,
        int atlasSize,
        int firstCodepoint,
        int lastCodepoint,
        int fontSizePixels)
    {
        if (!File.Exists(sourceFile))
        {
            throw new FileNotFoundException($"Font source not found: {sourceFile}");
        }

        var fontData = File.ReadAllBytes(sourceFile);

        var fontInfo = new StbTrueType.stbtt_fontinfo();
        float scaledAscent;
        float scaledDescent;
        float scaledLineHeight;
        int glyphCount = lastCodepoint - firstCodepoint + 1;
        var glyphs = new FontGlyph[glyphCount];
        var atlasBitmap = new byte[atlasSize * atlasSize];

        fixed (byte* pFontData = fontData)
        {
            if (StbTrueType.stbtt_InitFont(fontInfo, pFontData, 0) == 0)
            {
                throw new InvalidOperationException($"Failed to initialize font: {sourceFile}");
            }

            float scale = StbTrueType.stbtt_ScaleForPixelHeight(fontInfo, fontSizePixels);

            int ascent, descent, lineGap;
            StbTrueType.stbtt_GetFontVMetrics(fontInfo, &ascent, &descent, &lineGap);
            scaledAscent = ascent * scale;
            scaledDescent = descent * scale;
            scaledLineHeight = (ascent - descent + lineGap) * scale;

            int cursorX = GlyphPadding;
            int cursorY = GlyphPadding;
            int rowHeight = 0;

            fixed (byte* pAtlas = atlasBitmap)
            {
                for (int i = 0; i < glyphCount; i++)
                {
                    int codepoint = firstCodepoint + i;

                    int advanceWidth, leftSideBearing;
                    StbTrueType.stbtt_GetCodepointHMetrics(fontInfo, codepoint, &advanceWidth, &leftSideBearing);

                    int sdfWidth, sdfHeight, sdfXoff, sdfYoff;
                    byte* sdfData = StbTrueType.stbtt_GetCodepointSDF(
                        fontInfo, scale, codepoint,
                        SdfPadding, SdfOnEdge, SdfPixelDistScale,
                        &sdfWidth, &sdfHeight, &sdfXoff, &sdfYoff);

                    if (sdfData != null && sdfWidth > 0 && sdfHeight > 0)
                    {
                        if (cursorX + sdfWidth + GlyphPadding > atlasSize)
                        {
                            cursorX = GlyphPadding;
                            cursorY += rowHeight + GlyphPadding;
                            rowHeight = 0;
                        }

                        if (cursorY + sdfHeight + GlyphPadding > atlasSize)
                        {
                            StbTrueType.stbtt_FreeSDF(sdfData, null);
                            throw new InvalidOperationException(
                                $"Font atlas too small ({atlasSize}x{atlasSize}) for {Path.GetFileName(sourceFile)} at {fontSizePixels}px. Increase atlas size.");
                        }

                        for (int row = 0; row < sdfHeight; row++)
                        {
                            byte* src = sdfData + row * sdfWidth;
                            byte* dst = pAtlas + ((cursorY + row) * atlasSize) + cursorX;
                            for (int col = 0; col < sdfWidth; col++)
                            {
                                dst[col] = src[col];
                            }
                        }

                        glyphs[i] = new FontGlyph
                        {
                            Codepoint = codepoint,
                            AdvanceX = advanceWidth * scale,
                            OffsetX = sdfXoff,
                            OffsetY = sdfYoff,
                            Width = sdfWidth,
                            Height = sdfHeight,
                            U0 = (float)cursorX / atlasSize,
                            V0 = (float)cursorY / atlasSize,
                            U1 = (float)(cursorX + sdfWidth) / atlasSize,
                            V1 = (float)(cursorY + sdfHeight) / atlasSize
                        };

                        cursorX += sdfWidth + GlyphPadding;
                        rowHeight = Math.Max(rowHeight, sdfHeight);

                        StbTrueType.stbtt_FreeSDF(sdfData, null);
                    }
                    else
                    {
                        glyphs[i] = new FontGlyph
                        {
                            Codepoint = codepoint,
                            AdvanceX = advanceWidth * scale,
                            OffsetX = 0,
                            OffsetY = 0,
                            Width = 0,
                            Height = 0,
                            U0 = 0,
                            V0 = 0,
                            U1 = 0,
                            V1 = 0
                        };

                        if (sdfData != null)
                        {
                            StbTrueType.stbtt_FreeSDF(sdfData, null);
                        }
                    }
                }
            }
        }

        var rgbaData = new byte[atlasSize * atlasSize * 4];
        for (int i = 0, j = 0; i < atlasBitmap.Length; i++, j += 4)
        {
            byte alpha = atlasBitmap[i];
            rgbaData[j + 0] = 255;
            rgbaData[j + 1] = 255;
            rgbaData[j + 2] = 255;
            rgbaData[j + 3] = alpha;
        }

        return new CompiledFaceData
        {
            Width = atlasSize,
            Height = atlasSize,
            Pixels = rgbaData,
            FirstCodepoint = firstCodepoint,
            BaseSizePixels = fontSizePixels,
            LineHeightPixels = scaledLineHeight,
            AscentPixels = scaledAscent,
            DescentPixels = scaledDescent,
            Glyphs = glyphs,
        };
    }
}
