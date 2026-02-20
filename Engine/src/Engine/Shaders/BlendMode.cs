namespace DerpLib.Shaders;

/// <summary>
/// Blend modes for rendering.
/// </summary>
public enum BlendMode
{
    /// <summary>No blending, overwrite destination.</summary>
    None,

    /// <summary>Standard alpha blending: srcAlpha, 1-srcAlpha.</summary>
    Alpha,

    /// <summary>Additive blending: src + dst.</summary>
    Additive,

    /// <summary>Multiply blending: src * dst.</summary>
    Multiply
}
