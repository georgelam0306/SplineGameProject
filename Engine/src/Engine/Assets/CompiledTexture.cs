using DerpLib.AssetPipeline;

namespace DerpLib.Assets;

/// <summary>
/// A compiled texture asset containing raw RGBA8 pixel data.
/// Produced by the build pipeline, loaded at runtime.
/// </summary>
public sealed class CompiledTexture : IAsset
{
    /// <summary>
    /// Texture width in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Texture height in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Raw RGBA8 pixel data (4 bytes per pixel).
    /// </summary>
    public byte[] Pixels { get; set; } = Array.Empty<byte>();
}
