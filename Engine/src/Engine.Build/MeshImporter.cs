using DerpLib.AssetPipeline;

namespace DerpLib.Build;

[Importer(".obj")]
[Importer(".fbx")]
[Importer(".gltf")]
[Importer(".glb")]
[Importer(".dae")]
[Importer(".3ds")]
public sealed class MeshImporter : IAssetImporter
{
    public bool CanImport(string extension)
    {
        return extension is ".obj" or ".fbx" or ".gltf" or ".glb" or ".dae" or ".3ds";
    }

    public AssetItem Import(string rawPath, string location)
    {
        return new AssetItem
        {
            Location = location,
            Asset = new ModelAsset
            {
                Source = rawPath
            }
        };
    }
}
