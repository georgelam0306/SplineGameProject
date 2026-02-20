using DerpLib.AssetPipeline;

namespace DerpLib.Build;

[Importer(".ttf")]
[Importer(".otf")]
public sealed class FontImporter : IAssetImporter
{
    public bool CanImport(string extension)
    {
        return extension is ".ttf" or ".otf";
    }

    public AssetItem Import(string rawPath, string location)
    {
        return new AssetItem
        {
            Location = location,
            Asset = new FontAsset
            {
                Source = rawPath
            }
        };
    }
}
