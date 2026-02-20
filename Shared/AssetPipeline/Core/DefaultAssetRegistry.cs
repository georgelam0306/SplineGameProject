namespace DerpLib.AssetPipeline;

public sealed class DefaultAssetRegistry : IAssetRegistry
{
    private readonly Dictionary<string, IAssetImporter> _importersByExtension = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, IAssetCompiler> _compilersByType = new();

    public void RegisterImporter(string extension, IAssetImporter importer) => _importersByExtension[extension] = importer;
    public void RegisterCompiler<TAsset>(IAssetCompiler compiler) where TAsset : IAsset => _compilersByType[typeof(TAsset)] = compiler;

    public IAssetImporter ResolveImporter(string extension) => _importersByExtension[extension];
    public IAssetCompiler ResolveCompiler(Type assetType) => _compilersByType[assetType];

    public bool TryGetImporter(string extension, out IAssetImporter importer)
        => _importersByExtension.TryGetValue(extension, out importer!);

    public bool TryGetCompiler(Type assetType, out IAssetCompiler compiler)
        => _compilersByType.TryGetValue(assetType, out compiler!);
}
