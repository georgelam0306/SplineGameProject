namespace DerpLib.Text;

/// <summary>
/// Tiny built-in ASCII bitmap font atlas for debugging.
/// Uses a 5x7 glyph drawn into an 8x8 cell, laid out in a 16x8 grid (128 ASCII codepoints).
/// Alpha channel is the glyph mask.
/// </summary>
public static class DebugAsciiFontAtlas
{
    public const int CellSize = 8;
    public const int Columns = 16;
    public const int Rows = 8;
    public const int Width = Columns * CellSize;
    public const int Height = Rows * CellSize;

    public static void GetUvRect(byte codepoint, out float u0, out float v0, out float u1, out float v1)
    {
        int cellX = codepoint & 0x0F;
        int cellY = codepoint >> 4;

        u0 = (cellX * CellSize) / (float)Width;
        v0 = (cellY * CellSize) / (float)Height;
        u1 = ((cellX * CellSize) + CellSize) / (float)Width;
        v1 = ((cellY * CellSize) + CellSize) / (float)Height;
    }

    public static byte[] CreateRgba8Atlas()
    {
        var pixels = new byte[Width * Height * 4];

        // Initialize to transparent black.
        // Then fill known glyphs with white RGB + alpha mask.
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = 255;
            pixels[i + 1] = 255;
            pixels[i + 2] = 255;
            pixels[i + 3] = 0;
        }

        Span<byte> rows = stackalloc byte[7];

        for (int codepoint = 0; codepoint < 128; codepoint++)
        {
            if (!TryGetGlyphRows5x7((byte)codepoint, rows))
            {
                continue;
            }

            int cellX = codepoint & 0x0F;
            int cellY = codepoint >> 4;

            int originX = (cellX * CellSize) + 1;
            int originY = (cellY * CellSize) + 1;

            for (int y = 0; y < 7; y++)
            {
                byte rowBits = rows[y];
                for (int x = 0; x < 5; x++)
                {
                    bool on = ((rowBits >> (4 - x)) & 1) != 0;
                    if (!on)
                    {
                        continue;
                    }

                    int px = originX + x;
                    int py = originY + y;
                    int p = (py * Width + px) * 4;
                    pixels[p + 3] = 255;
                }
            }
        }

        return pixels;
    }

    private static bool TryGetGlyphRows5x7(byte codepoint, Span<byte> rows7)
    {
        // 7 rows, 5 bits per row (MSB is leftmost pixel).
        // Only a small, useful subset is defined; unknown glyphs remain blank.
        switch (codepoint)
        {
            case (byte)' ': rows7.Fill(0); return true;
            case (byte)'.': SetRows(rows7, 0, 0, 0, 0, 0, 0, 0b00100); return true;
            case (byte)':': SetRows(rows7, 0, 0b00100, 0, 0, 0b00100, 0, 0); return true;
            case (byte)'-': SetRows(rows7, 0, 0, 0, 0b11111, 0, 0, 0); return true;
            case (byte)'_': SetRows(rows7, 0, 0, 0, 0, 0, 0, 0b11111); return true;
            case (byte)'/': SetRows(rows7, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0, 0); return true;
            case (byte)'?': SetRows(rows7, 0b01110, 0b10001, 0b00010, 0b00100, 0b00100, 0, 0b00100); return true;

            case (byte)'0': SetRows(rows7, 0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110); return true;
            case (byte)'1': SetRows(rows7, 0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110); return true;
            case (byte)'2': SetRows(rows7, 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111); return true;
            case (byte)'3': SetRows(rows7, 0b01110, 0b10001, 0b00001, 0b00110, 0b00001, 0b10001, 0b01110); return true;
            case (byte)'4': SetRows(rows7, 0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010); return true;
            case (byte)'5': SetRows(rows7, 0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110); return true;
            case (byte)'6': SetRows(rows7, 0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110); return true;
            case (byte)'7': SetRows(rows7, 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000); return true;
            case (byte)'8': SetRows(rows7, 0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110); return true;
            case (byte)'9': SetRows(rows7, 0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100); return true;

            case (byte)'A': SetRows(rows7, 0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001); return true;
            case (byte)'B': SetRows(rows7, 0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110); return true;
            case (byte)'C': SetRows(rows7, 0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110); return true;
            case (byte)'D': SetRows(rows7, 0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110); return true;
            case (byte)'E': SetRows(rows7, 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111); return true;
            case (byte)'F': SetRows(rows7, 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000); return true;
            case (byte)'G': SetRows(rows7, 0b01110, 0b10001, 0b10000, 0b10111, 0b10001, 0b10001, 0b01110); return true;
            case (byte)'H': SetRows(rows7, 0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001); return true;
            case (byte)'I': SetRows(rows7, 0b01110, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110); return true;
            case (byte)'J': SetRows(rows7, 0b00111, 0b00010, 0b00010, 0b00010, 0b00010, 0b10010, 0b01100); return true;
            case (byte)'K': SetRows(rows7, 0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001); return true;
            case (byte)'L': SetRows(rows7, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111); return true;
            case (byte)'M': SetRows(rows7, 0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001); return true;
            case (byte)'N': SetRows(rows7, 0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001); return true;
            case (byte)'O': SetRows(rows7, 0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110); return true;
            case (byte)'P': SetRows(rows7, 0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000); return true;
            case (byte)'Q': SetRows(rows7, 0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101); return true;
            case (byte)'R': SetRows(rows7, 0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001); return true;
            case (byte)'S': SetRows(rows7, 0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110); return true;
            case (byte)'T': SetRows(rows7, 0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100); return true;
            case (byte)'U': SetRows(rows7, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110); return true;
            case (byte)'V': SetRows(rows7, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100); return true;
            case (byte)'W': SetRows(rows7, 0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b11011, 0b10001); return true;
            case (byte)'X': SetRows(rows7, 0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001); return true;
            case (byte)'Y': SetRows(rows7, 0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100); return true;
            case (byte)'Z': SetRows(rows7, 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111); return true;
        }

        return false;
    }

    private static void SetRows(Span<byte> rows7, byte r0, byte r1, byte r2, byte r3, byte r4, byte r5, byte r6)
    {
        rows7[0] = r0;
        rows7[1] = r1;
        rows7[2] = r2;
        rows7[3] = r3;
        rows7[4] = r4;
        rows7[5] = r5;
        rows7[6] = r6;
    }
}
