using Friflo.Engine.ECS.Systems;
using Catrillion.Config;
using Catrillion.Rendering.DualGrid.Components;
using Catrillion.Rendering.Services;

namespace Catrillion.Rendering.DualGrid.Systems;

/// <summary>
/// Assigns shared texture atlases to chunks that have not yet been initialized.
/// Textures are loaded once via DualGridTextureService and shared across all chunks.
/// </summary>
public sealed class DualGridAtlasLoadSystem : QuerySystem<DualGridAtlases>
{
    private readonly DualGridTextureService _textureService;

    public DualGridAtlasLoadSystem(DualGridTextureService textureService)
    {
        _textureService = textureService;
    }

    protected override void OnUpdate()
    {
        // Ensure textures are loaded (happens once)
        if (!_textureService.IsLoaded)
        {
            _textureService.LoadTextures();
        }

        foreach (var entity in Query.Entities)
        {
            ref var atlases = ref entity.GetComponent<DualGridAtlases>();
            if (atlases.Loaded)
            {
                continue;
            }

            // Assign shared textures (not owned by chunk - don't unload on chunk delete)
            atlases.Grass = _textureService.Grass;
            atlases.Dirt = _textureService.Dirt;
            atlases.Water = _textureService.Water;
            atlases.Mountain = _textureService.Mountain;
            atlases.SrcTileWidth = GameConfig.DualGrid.AtlasTileWidth;
            atlases.SrcTileHeight = GameConfig.DualGrid.AtlasTileHeight;
            atlases.Columns = GameConfig.DualGrid.AtlasColumns;
            atlases.Loaded = true;
        }
    }
}
