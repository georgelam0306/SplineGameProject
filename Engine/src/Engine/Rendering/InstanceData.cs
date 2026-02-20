using System.Runtime.InteropServices;

namespace DerpLib.Rendering;

/// <summary>
/// Per-instance vertex data for bindless instanced rendering.
/// 16 bytes, fed as vertex attributes with InputRate.Instance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InstanceData
{
    public uint TransformIndex;   // Index into TransformBuffer SSBO
    public uint TextureIndex;     // Index into bindless texture array
    public uint PackedColor;      // RGBA8 tint (unpackUnorm4x8 in shader)
    public uint PackedUVOffset;   // Two float16s (unpackHalf2x16 in shader)

    public static uint PackColor(byte r, byte g, byte b, byte a)
        => (uint)(r | (g << 8) | (b << 16) | (a << 24));

    public static uint PackHalf2(float x, float y)
    {
        var hx = BitConverter.HalfToUInt16Bits((Half)x);
        var hy = BitConverter.HalfToUInt16Bits((Half)y);
        return (uint)(hx | (hy << 16));
    }
}
