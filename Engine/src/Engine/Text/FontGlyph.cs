namespace DerpLib.Text;

/// <summary>
/// Glyph metrics and atlas UVs.
/// </summary>
public struct FontGlyph
{
    public int Codepoint;
    public float AdvanceX;
    public int OffsetX;
    public int OffsetY;
    public int Width;
    public int Height;
    public float U0;
    public float V0;
    public float U1;
    public float V1;
}
