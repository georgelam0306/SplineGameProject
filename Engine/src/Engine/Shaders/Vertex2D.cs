using System.Numerics;
using System.Runtime.InteropServices;

namespace DerpLib.Shaders;

/// <summary>
/// Standard 2D vertex format: position, texcoord, color.
/// 20 bytes total, matches shader layout.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex2D
{
    public Vector2 Position;   // location 0, offset 0
    public Vector2 TexCoord;   // location 1, offset 8
    public uint Color;         // location 2, offset 16 (packed RGBA)

    public Vertex2D(Vector2 position, Vector2 texCoord, uint color)
    {
        Position = position;
        TexCoord = texCoord;
        Color = color;
    }

    public Vertex2D(float x, float y, float u, float v, uint color)
    {
        Position = new Vector2(x, y);
        TexCoord = new Vector2(u, v);
        Color = color;
    }

    /// <summary>
    /// Pack RGBA bytes into a uint (ABGR order for little-endian).
    /// </summary>
    public static uint PackColor(byte r, byte g, byte b, byte a = 255)
    {
        return (uint)(r | (g << 8) | (b << 16) | (a << 24));
    }

    public static readonly uint White = PackColor(255, 255, 255);
    public static readonly uint Black = PackColor(0, 0, 0);
    public static readonly uint Red = PackColor(255, 0, 0);
    public static readonly uint Green = PackColor(0, 255, 0);
    public static readonly uint Blue = PackColor(0, 0, 255);

    public static unsafe int SizeInBytes => sizeof(Vertex2D);
}
