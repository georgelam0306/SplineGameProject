using System;
using System.Numerics;
using Core;
using DerpLib.Text;

namespace Derp.UI;

internal sealed class TextLayoutCache
{
    internal struct Line
    {
        public int GlyphStart;
        public int GlyphCount;
        public int CharStart;
        public int CharEnd;
        public float WidthPx;
        public float BaselineYPx;
    }

    internal struct Glyph
    {
        public int CharIndex;
        public Vector2 CenterPx;
        public Vector2 HalfSizePx;
        public Vector4 UvRect;
    }

    private StringHandle _textHandle;
    private StringHandle _fontHandle;
    private float _fontSizePx;
    private float _lineHeightScale;
    private float _letterSpacingPx;
    private bool _multiline;
    private bool _wrap;
    private int _overflow;
    private int _alignX;
    private int _alignY;
    private Vector2 _boxSizePx;

    private float _resolvedFontSizePx;

    private Line[] _lines = Array.Empty<Line>();
    private int _lineCount;

    private Glyph[] _glyphs = Array.Empty<Glyph>();
    private int _glyphCount;

    public float ResolvedFontSizePx => _resolvedFontSizePx;
    public ReadOnlySpan<Line> Lines => _lines.AsSpan(0, _lineCount);
    public ReadOnlySpan<Glyph> Glyphs => _glyphs.AsSpan(0, _glyphCount);

    public float MeasuredWidthPx { get; private set; }
    public float MeasuredHeightPx { get; private set; }

    public void Ensure(
        Font font,
        StringHandle textHandle,
        StringHandle fontHandle,
        float fontSizePx,
        float lineHeightScale,
        float letterSpacingPx,
        bool multiline,
        bool wrap,
        int overflow,
        int alignX,
        int alignY,
        Vector2 boxSizePx)
    {
        if (_textHandle == textHandle &&
            _fontHandle == fontHandle &&
            _fontSizePx == fontSizePx &&
            _lineHeightScale == lineHeightScale &&
            _letterSpacingPx == letterSpacingPx &&
            _multiline == multiline &&
            _wrap == wrap &&
            _overflow == overflow &&
            _alignX == alignX &&
            _alignY == alignY &&
            _boxSizePx.X == boxSizePx.X &&
            _boxSizePx.Y == boxSizePx.Y)
        {
            return;
        }

        _textHandle = textHandle;
        _fontHandle = fontHandle;
        _fontSizePx = fontSizePx;
        _lineHeightScale = lineHeightScale;
        _letterSpacingPx = letterSpacingPx;
        _multiline = multiline;
        _wrap = wrap;
        _overflow = overflow;
        _alignX = alignX;
        _alignY = alignY;
        _boxSizePx = boxSizePx;

        ReadOnlySpan<char> text = ((string)textHandle).AsSpan();

        float clampedLineHeightScale = float.IsFinite(lineHeightScale) ? Math.Clamp(lineHeightScale, 0.25f, 8f) : 1f;
        float clampedLetterSpacing = float.IsFinite(letterSpacingPx) ? Math.Clamp(letterSpacingPx, -64f, 256f) : 0f;
        float initialFontSize = float.IsFinite(fontSizePx) ? Math.Clamp(fontSizePx, 1f, 512f) : 24f;

        var overflowMode = (TextOverflowMode)Math.Clamp(overflow, (int)TextOverflowMode.Visible, (int)TextOverflowMode.Fit);

        if (overflowMode == TextOverflowMode.Fit)
        {
            float fitted = FitFontSize(font, text, initialFontSize, clampedLineHeightScale, clampedLetterSpacing, multiline, wrap, boxSizePx);
            _resolvedFontSizePx = fitted;
            Build(font, text, fitted, clampedLineHeightScale, clampedLetterSpacing, multiline, wrap, boxSizePx);
            return;
        }

        _resolvedFontSizePx = initialFontSize;
        Build(font, text, initialFontSize, clampedLineHeightScale, clampedLetterSpacing, multiline, wrap, boxSizePx);
    }

    private float FitFontSize(
        Font font,
        ReadOnlySpan<char> text,
        float startFontSizePx,
        float lineHeightScale,
        float letterSpacingPx,
        bool multiline,
        bool wrap,
        Vector2 boxSizePx)
    {
        float boxW = Math.Max(0f, boxSizePx.X);
        float boxH = Math.Max(0f, boxSizePx.Y);
        if (boxW <= 0.0001f || boxH <= 0.0001f)
        {
            return startFontSizePx;
        }

        float minFontSizePx = 1f;
        float size = startFontSizePx;

        for (int i = 0; i < 16; i++)
        {
            Build(font, text, size, lineHeightScale, letterSpacingPx, multiline, wrap, boxSizePx);
            if (MeasuredWidthPx <= boxW + 0.0001f && MeasuredHeightPx <= boxH + 0.0001f)
            {
                return size;
            }

            float next = size * 0.9f;
            if (next < minFontSizePx)
            {
                return minFontSizePx;
            }

            size = next;
        }

        return size;
    }

    private void EnsureLineCapacity(int min)
    {
        if (_lines.Length >= min)
        {
            return;
        }

        int next = _lines.Length == 0 ? 8 : _lines.Length;
        while (next < min)
        {
            next *= 2;
        }

        Array.Resize(ref _lines, next);
    }

    private void EnsureGlyphCapacity(int min)
    {
        if (_glyphs.Length >= min)
        {
            return;
        }

        int next = _glyphs.Length == 0 ? 64 : _glyphs.Length;
        while (next < min)
        {
            next *= 2;
        }

        Array.Resize(ref _glyphs, next);
    }

    private void Build(
        Font font,
        ReadOnlySpan<char> text,
        float fontSizePx,
        float lineHeightScale,
        float letterSpacingPx,
        bool multiline,
        bool wrap,
        Vector2 boxSizePx)
    {
        _lineCount = 0;
        _glyphCount = 0;
        MeasuredWidthPx = 0f;
        MeasuredHeightPx = 0f;

        float boxW = Math.Max(0f, boxSizePx.X);

        float scale = fontSizePx / font.BaseSizePixels;
        float lineHeightPx = font.LineHeightPixels * scale * lineHeightScale;
        float baselineY = font.AscentPixels * scale;

        int lineGlyphStart = 0;
        int lineCharStart = 0;
        float cursorX = 0f;
        float lineWidth = 0f;

        EnsureLineCapacity(4);
        EnsureGlyphCapacity(Math.Max(4, text.Length));

        int charIndex = 0;
        while (charIndex < text.Length)
        {
            char c = text[charIndex];

            if (c == '\n')
            {
                if (multiline)
                {
                    CommitLine(lineGlyphStart, _glyphCount - lineGlyphStart, lineCharStart, charIndex, lineWidth, baselineY);
                    baselineY += lineHeightPx;
                    lineGlyphStart = _glyphCount;
                    charIndex++;
                    lineCharStart = charIndex;
                    cursorX = 0f;
                    lineWidth = 0f;
                    continue;
                }

                c = ' ';
            }

            if (c == '\r')
            {
                charIndex++;
                continue;
            }

            bool isWhitespace = c == ' ' || c == '\t';
            if (!isWhitespace)
            {
                int wordStart = charIndex;
                int wordEnd = wordStart;
                float wordWidth = 0f;

                while (wordEnd < text.Length)
                {
                    char wc = text[wordEnd];
                    if (wc == '\r')
                    {
                        wordEnd++;
                        continue;
                    }
                    if (wc == '\n' || wc == ' ' || wc == '\t')
                    {
                        break;
                    }

                    if (!font.TryGetGlyph(wc, out FontGlyph wGlyph))
                    {
                        if (font.TryGetGlyph(' ', out FontGlyph wSpace))
                        {
                            wordWidth += wSpace.AdvanceX * scale + letterSpacingPx;
                        }
                        wordEnd++;
                        continue;
                    }

                    wordWidth += wGlyph.AdvanceX * scale + letterSpacingPx;
                    wordEnd++;
                }

                if (wrap && multiline && boxW > 0.0001f && cursorX > 0.0001f && cursorX + wordWidth > boxW)
                {
                    CommitLine(lineGlyphStart, _glyphCount - lineGlyphStart, lineCharStart, wordStart, lineWidth, baselineY);
                    baselineY += lineHeightPx;
                    lineGlyphStart = _glyphCount;
                    lineCharStart = wordStart;
                    cursorX = 0f;
                    lineWidth = 0f;
                }

                for (int wi = wordStart; wi < wordEnd; wi++)
                {
                    char wc = text[wi];
                    if (wc == '\r')
                    {
                        continue;
                    }

                    if (!font.TryGetGlyph(wc, out FontGlyph wordGlyph))
                    {
                        if (font.TryGetGlyph(' ', out FontGlyph space))
                        {
                            cursorX += space.AdvanceX * scale + letterSpacingPx;
                            if (cursorX > lineWidth)
                            {
                                lineWidth = cursorX;
                            }
                        }
                        continue;
                    }

                    float wordAdvance = wordGlyph.AdvanceX * scale + letterSpacingPx;

                    if (wordGlyph.Width > 0 && wordGlyph.Height > 0)
                    {
                        float glyphW = wordGlyph.Width * scale;
                        float glyphH = wordGlyph.Height * scale;
                        float glyphX = cursorX + wordGlyph.OffsetX * scale;
                        float glyphY = baselineY + wordGlyph.OffsetY * scale;

                        var g = new Glyph
                        {
                            CharIndex = wi,
                            CenterPx = new Vector2(glyphX + glyphW * 0.5f, glyphY + glyphH * 0.5f),
                            HalfSizePx = new Vector2(glyphW * 0.5f, glyphH * 0.5f),
                            UvRect = new Vector4(wordGlyph.U0, wordGlyph.V0, wordGlyph.U1, wordGlyph.V1)
                        };

                        _glyphs[_glyphCount++] = g;
                    }

                    cursorX += wordAdvance;
                    if (cursorX > lineWidth)
                    {
                        lineWidth = cursorX;
                    }
                }

                charIndex = wordEnd;
                continue;
            }

            if (!font.TryGetGlyph(c, out FontGlyph glyph))
            {
                if (font.TryGetGlyph(' ', out FontGlyph space))
                {
                    cursorX += space.AdvanceX * scale + letterSpacingPx;
                    if (cursorX > lineWidth)
                    {
                        lineWidth = cursorX;
                    }
                }
                continue;
            }

            float advance = glyph.AdvanceX * scale + letterSpacingPx;

            if (glyph.Width > 0 && glyph.Height > 0)
            {
                float glyphW = glyph.Width * scale;
                float glyphH = glyph.Height * scale;
                float glyphX = cursorX + glyph.OffsetX * scale;
                float glyphY = baselineY + glyph.OffsetY * scale;

                var g = new Glyph
                {
                    CharIndex = charIndex,
                    CenterPx = new Vector2(glyphX + glyphW * 0.5f, glyphY + glyphH * 0.5f),
                    HalfSizePx = new Vector2(glyphW * 0.5f, glyphH * 0.5f),
                    UvRect = new Vector4(glyph.U0, glyph.V0, glyph.U1, glyph.V1)
                };

                _glyphs[_glyphCount++] = g;
            }

            cursorX += advance;
            if (cursorX > lineWidth)
            {
                lineWidth = cursorX;
            }

            charIndex++;
        }

        CommitLine(lineGlyphStart, _glyphCount - lineGlyphStart, lineCharStart, text.Length, lineWidth, baselineY);

        float maxWidth = 0f;
        ReadOnlySpan<Line> lines = _lines.AsSpan(0, _lineCount);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].WidthPx > maxWidth)
            {
                maxWidth = lines[i].WidthPx;
            }
        }

        MeasuredWidthPx = maxWidth;
        MeasuredHeightPx = _lineCount <= 0 ? 0f : (_lineCount * lineHeightPx);
    }

    private void CommitLine(int glyphStart, int glyphCount, int charStart, int charEnd, float width, float baselineY)
    {
        EnsureLineCapacity(_lineCount + 1);
        _lines[_lineCount++] = new Line
        {
            GlyphStart = glyphStart,
            GlyphCount = glyphCount,
            CharStart = charStart,
            CharEnd = charEnd,
            WidthPx = width,
            BaselineYPx = baselineY
        };
    }
}
