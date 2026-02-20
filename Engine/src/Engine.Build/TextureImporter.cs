using DerpLib.AssetPipeline;

namespace DerpLib.Build;

[Importer(".png")]
[Importer(".jpg")]
[Importer(".jpeg")]
[Importer(".bmp")]
[Importer(".tga")]
public sealed class TextureImporter : IAssetImporter
{
    public bool CanImport(string extension)
    {
        return extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga";
    }

    public AssetItem Import(string rawPath, string location)
    {
        return new AssetItem
        {
            Location = location,
            Asset = new TextureAsset
            {
                Source = rawPath
            }
        };
    }
}
