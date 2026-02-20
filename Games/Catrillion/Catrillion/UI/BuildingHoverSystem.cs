using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.AppState;
using Catrillion.Camera;
using Catrillion.Config;
using Catrillion.Simulation.Components;
using Catrillion.Stores;

namespace Catrillion.UI;

/// <summary>
/// Detects which building the mouse is hovering over.
/// Updates GameplayStore.HoveredBuildingSlot for use by rendering systems.
/// </summary>
public sealed class BuildingHoverSystem : BaseSystem
{
    private readonly SimWorld _simWorld;
    private readonly GameplayStore _gameplayStore;
    private readonly CameraManager _cameraManager;
    private readonly InputStore _inputStore;

    private const int TileSize = GameConfig.Map.TileSize;

    public BuildingHoverSystem(SimWorld simWorld, GameplayStore gameplayStore, CameraManager cameraManager, InputStore inputStore)
    {
        _simWorld = simWorld;
        _gameplayStore = gameplayStore;
        _cameraManager = cameraManager;
        _inputStore = inputStore;
    }

    protected override void OnUpdateGroup()
    {
        // Get world mouse position (use InputStore for software cursor support)
        var screenPos = _inputStore.InputManager.Device.MousePosition;
        var worldPos = _cameraManager.ScreenToWorld(screenPos);

        // Check if mouse is within any building bounds
        int hoveredSlot = -1;
        var buildings = _simWorld.BuildingRows;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            // Include both active and under-construction buildings
            if (!building.Flags.HasFlag(BuildingFlags.IsActive) &&
                !building.Flags.HasFlag(BuildingFlags.IsUnderConstruction)) continue;
            if (building.Flags.IsDead()) continue;

            // Calculate building bounds in world coordinates
            float minX = building.TileX * TileSize;
            float minY = building.TileY * TileSize;
            float maxX = minX + building.Width * TileSize;
            float maxY = minY + building.Height * TileSize;

            if (worldPos.X >= minX && worldPos.X < maxX &&
                worldPos.Y >= minY && worldPos.Y < maxY)
            {
                hoveredSlot = slot;
                break; // Take first match
            }
        }

        // Update store (only if changed to avoid unnecessary reactive updates)
        if (_gameplayStore.HoveredBuildingSlot.CurrentValue != hoveredSlot)
        {
            _gameplayStore.HoveredBuildingSlot.Value = hoveredSlot;
        }
    }
}
