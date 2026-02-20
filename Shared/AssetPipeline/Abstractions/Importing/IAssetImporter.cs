namespace DerpLib.AssetPipeline;

public interface IAssetImporter
{
    bool CanImport(string extension);
    AssetItem Import(string rawPath, string location);
}
