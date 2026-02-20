namespace DerpLib.Rendering;

/// <summary>
/// A handle to a texture in the bindless texture array.
/// Similar to MeshHandle for consistency.
/// </summary>
public readonly struct TextureHandle
{
    /// <summary>Index into the bindless texture array.</summary>
    public readonly int Index;

    public TextureHandle(int index)
    {
        Index = index;
    }

    /// <summary>Invalid texture handle (index -1).</summary>
    public static readonly TextureHandle Invalid = new(-1);

    /// <summary>White 1x1 texture (index 0, always first loaded).</summary>
    public static readonly TextureHandle White = new(0);

    /// <summary>Whether this handle points to a valid texture.</summary>
    public bool IsValid => Index >= 0;
}
