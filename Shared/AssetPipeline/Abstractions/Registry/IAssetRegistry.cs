namespace DerpLib.AssetPipeline;

public interface IAssetRegistry
{
    void RegisterImporter(string extension, IAssetImporter importer);
    void RegisterCompiler<TAsset>(IAssetCompiler compiler) where TAsset : IAsset;
    IAssetImporter ResolveImporter(string extension);
    IAssetCompiler ResolveCompiler(Type assetType);
    bool TryGetImporter(string extension, out IAssetImporter importer);
    bool TryGetCompiler(Type assetType, out IAssetCompiler compiler);
}
