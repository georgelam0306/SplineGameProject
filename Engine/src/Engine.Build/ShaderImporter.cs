using DerpLib.AssetPipeline;

namespace DerpLib.Build;

[Importer(".vert")]
[Importer(".frag")]
[Importer(".comp")]
public sealed class ShaderImporter : IAssetImporter
{
    public bool CanImport(string extension)
    {
        return extension is ".vert" or ".frag" or ".comp";
    }

    public AssetItem Import(string rawPath, string location)
    {
        var extension = Path.GetExtension(rawPath).ToLowerInvariant();
        var stage = extension switch
        {
            ".vert" => ShaderStage.Vertex,
            ".frag" => ShaderStage.Fragment,
            ".comp" => ShaderStage.Compute,
            _ => throw new ArgumentException($"Unsupported shader extension: {extension}")
        };

        return new AssetItem
        {
            Location = location,
            Asset = new ShaderStageAsset
            {
                Source = rawPath,
                Stage = stage
            }
        };
    }
}
