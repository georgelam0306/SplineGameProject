using DerpLib.AssetPipeline;

namespace DerpLib.AssetPipeline.AotTest;

/// <summary>
/// Test asset type for AOT verification.
/// Implements IAsset - generator will auto-register it.
/// </summary>
public sealed class TestAsset : IAsset
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Another test type to verify multiple type registration.
/// Implements IAsset - generator will auto-register it.
/// </summary>
public sealed class TestConfig : IAsset
{
    public bool Enabled { get; set; }
    public float Scale { get; set; } = 1.0f;
}
