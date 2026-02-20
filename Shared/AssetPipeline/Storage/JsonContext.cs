using System.Text.Json.Serialization;

namespace DerpLib.AssetPipeline;

/// <summary>
/// Bundle file format for FileObjectDatabase.
/// </summary>
internal sealed class BundleData
{
    [JsonPropertyName("compress")]
    public string Compress { get; set; } = "none";

    [JsonPropertyName("objects")]
    public Dictionary<string, string> Objects { get; set; } = new();

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }
}

[JsonSerializable(typeof(BuildCommandRecord))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(BundleData))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal partial class StorageJsonContext : JsonSerializerContext
{
}
