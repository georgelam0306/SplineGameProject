using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.AppState;
using Catrillion.Camera;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Grids;
using Catrillion.Simulation.Services;
using Core;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders the power grid coverage when the player is in build mode.
/// Uses PowerNetworkService to query which tiles have power.
/// Also shows preview for buildings being placed that would extend power.
/// </summary>
public sealed class PowerGridRenderSystem : BaseSystem
{
    private readonly GameplayStore _gameplayStore;
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly CameraManager _cameraManager;
    private readonly TerrainDataService _terrainData;
    private readonly PowerNetworkService _powerNetwork;
    private readonly int _tileSize;

    // Power grid visualization colors
    private static readonly Color PoweredTileColor = new(100, 200, 255, 40);      // Light blue for powered tiles
    private static readonly Color PreviewTileColor = new(150, 255, 150, 50);      // Green for preview extension

    public PowerGridRenderSystem(
        GameplayStore gameplayStore,
        GameDataManager<GameDocDb> gameData,
        CameraManager cameraManager,
        TerrainDataService terrainData,
        PowerNetworkService powerNetwork)
    {
        _gameplayStore = gameplayStore;
        _gameData = gameData;
        _cameraManager = cameraManager;
        _terrainData = terrainData;
        _powerNetwork = powerNetwork;

        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _tileSize = mapConfig.TileSize;
    }

    protected override void OnUpdateGroup()
    {
        // Only render when in build mode
        if (!_gameplayStore.IsInBuildMode.CurrentValue) return;
        if (!_terrainData.IsGenerated) return;

        // Calculate visible tile range based on camera
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        float camX = _cameraManager.TargetX;
        float camY = _cameraManager.TargetY;

        int startTileX = (int)((camX - screenWidth / 2) / _tileSize) - 1;
        int startTileY = (int)((camY - screenHeight / 2) / _tileSize) - 1;
        int endTileX = (int)((camX + screenWidth / 2) / _tileSize) + 2;
        int endTileY = (int)((camY + screenHeight / 2) / _tileSize) + 2;

        // Clamp to grid bounds
        startTileX = Math.Max(0, startTileX);
        startTileY = Math.Max(0, startTileY);
        endTileX = Math.Min(PowerCell.GridWidth - 1, endTileX);
        endTileY = Math.Min(PowerCell.GridHeight - 1, endTileY);

        // Get preview info for showing extended power range
        var previewPos = _gameplayStore.PlacementPreview.CurrentValue;
        var buildingType = _gameplayStore.BuildModeType.CurrentValue;
        float previewCenterX = 0, previewCenterY = 0, previewRadiusSq = 0;
        bool hasPreview = false;

        if (previewPos.HasValue && buildingType.HasValue)
        {
            ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById((int)buildingType.Value);
            if (typeData.PowerConnectionRadius > Fixed64.Zero)
            {
                hasPreview = true;
                previewCenterX = previewPos.Value.tileX * _tileSize + (typeData.Width * _tileSize) / 2.0f;
                previewCenterY = previewPos.Value.tileY * _tileSize + (typeData.Height * _tileSize) / 2.0f;
                float radius = typeData.PowerConnectionRadius.ToFloat();
                previewRadiusSq = radius * radius;
            }
        }

        // Draw powered tiles from grid
        for (int tileY = startTileY; tileY <= endTileY; tileY++)
        {
            for (int tileX = startTileX; tileX <= endTileX; tileX++)
            {
                bool isPowered = _powerNetwork.IsTilePowered(tileX, tileY);

                if (isPowered)
                {
                    Raylib.DrawRectangle(
                        tileX * _tileSize,
                        tileY * _tileSize,
                        _tileSize,
                        _tileSize,
                        PoweredTileColor);
                }
                else if (hasPreview)
                {
                    // Check if this tile would be powered by the preview building
                    float tileCenterX = tileX * _tileSize + _tileSize / 2.0f;
                    float tileCenterY = tileY * _tileSize + _tileSize / 2.0f;
                    float dx = tileCenterX - previewCenterX;
                    float dy = tileCenterY - previewCenterY;
                    float distSq = dx * dx + dy * dy;

                    if (distSq <= previewRadiusSq)
                    {
                        Raylib.DrawRectangle(
                            tileX * _tileSize,
                            tileY * _tileSize,
                            _tileSize,
                            _tileSize,
                            PreviewTileColor);
                    }
                }
            }
        }
    }
}
