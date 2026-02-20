using System.Text.Json.Serialization;

namespace DerpLib.Vfs;

/// <summary>
/// Package file format for PackageFileProvider.
/// </summary>
internal sealed class PackageData
{
    [JsonPropertyName("files")]
    public Dictionary<string, PackageFileEntry> Files { get; set; } = new();
}

internal sealed class PackageFileEntry
{
    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;

    [JsonPropertyName("compressed")]
    public bool Compressed { get; set; }
}

[JsonSerializable(typeof(PackageData))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal partial class VfsJsonContext : JsonSerializerContext
{
}
