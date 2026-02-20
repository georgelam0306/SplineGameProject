using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Camera;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Rendering.DualGrid.Utilities;
using Catrillion.Simulation.Services;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders tree sprites on forest/dirt terrain tiles.
/// Trees are placed in a semi-random pattern based on tile position.
/// </summary>
public sealed class TreeOverlayRenderSystem : BaseSystem
{
    private readonly TerrainDataService _terrainData;
    private readonly CameraManager _cameraManager;
    private readonly int _tileSize;
    private Texture2D _treeTexture;
    private bool _textureLoaded;

    public TreeOverlayRenderSystem(
        TerrainDataService terrainData,
        CameraManager cameraManager,
        GameDataManager<GameDocDb> gameData)
    {
        _terrainData = terrainData;
        _cameraManager = cameraManager;
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _tileSize = mapConfig.TileSize;
    }

    protected override void OnUpdateGroup()
    {
        if (!_terrainData.IsGenerated) return;

        // Load texture on first use
        if (!_textureLoaded)
        {
            _treeTexture = Raylib.LoadTexture("Resources/tree_placeholder.png");
            Raylib.SetTextureFilter(_treeTexture, TextureFilter.Point);
            _textureLoaded = true;
        }

        // Calculate visible tile range based on camera
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        float camX = _cameraManager.TargetX;
        float camY = _cameraManager.TargetY;

        int startTileX = (int)((camX - screenWidth / 2) / _tileSize) - 1;
        int startTileY = (int)((camY - screenHeight / 2) / _tileSize) - 1;
        int endTileX = (int)((camX + screenWidth / 2) / _tileSize) + 2;
        int endTileY = (int)((camY + screenHeight / 2) / _tileSize) + 2;

        // Clamp to terrain bounds
        startTileX = Math.Max(0, startTileX);
        startTileY = Math.Max(0, startTileY);
        endTileX = Math.Min(_terrainData.WidthTiles - 1, endTileX);
        endTileY = Math.Min(_terrainData.HeightTiles - 1, endTileY);

        // Render trees on dirt/forest tiles
        for (int tileY = startTileY; tileY <= endTileY; tileY++)
        {
            for (int tileX = startTileX; tileX <= endTileX; tileX++)
            {
                var terrain = _terrainData.GetTerrainAt(tileX, tileY);
                if (terrain != TerrainType.Dirt) continue;

                // Use tile position hash to determine tree placement
                // This creates consistent but varied tree patterns
                int hash = HashTile(tileX, tileY);

                // ~70% of forest tiles get trees
                if ((hash & 0xFF) > 180) continue;

                // Calculate world position with slight offset based on hash
                float offsetX = ((hash >> 8) & 0x7) - 3; // -3 to 4 pixel offset
                float offsetY = ((hash >> 11) & 0x7) - 3;

                // Center tree on tile (matching dual grid's half-tile offset)
                float centerX = tileX * _tileSize + (_tileSize * 0.5f) + offsetX;
                float centerY = tileY * _tileSize + (_tileSize * 0.5f) + offsetY;

                Raylib.DrawTexture(_treeTexture,
                    (int)(centerX - _treeTexture.Width * 0.5f),
                    (int)(centerY - _treeTexture.Height * 0.5f),
                    Color.White);
            }
        }
    }

    private static int HashTile(int x, int y)
    {
        // Simple hash for deterministic pseudo-random tree placement
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        return h;
    }
}
