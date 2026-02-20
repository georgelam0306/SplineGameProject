namespace DerpLib.Rendering;

/// <summary>
/// A loaded texture. Internally references a slot in the bindless texture array.
/// </summary>
public readonly struct Texture
{
    /// <summary>Internal index into the bindless texture array.</summary>
    internal readonly int Index;

    /// <summary>Texture width in pixels.</summary>
    public readonly int Width;

    /// <summary>Texture height in pixels.</summary>
    public readonly int Height;

    internal Texture(int index, int width, int height)
    {
        Index = index;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Default white 1x1 texture (index 0).
    /// </summary>
    public static readonly Texture White = new(0, 1, 1);

    /// <summary>
    /// Gets the bindless array index for advanced/low-level usage.
    /// </summary>
    public int GetIndex() => Index;
}
