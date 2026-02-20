using System;
using System.Collections.Generic;
using System.Linq;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.AppState;
using Catrillion.Camera;
using Catrillion.Config;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Catrillion.Stores;
using SimTable;

namespace Catrillion.UI;

/// <summary>
/// RTS-style bottom bar UI like They Are Billions.
/// Layout: [Minimap] [Selection Info + Action Buttons] [Resources]
/// </summary>
public sealed class BottomBarUI : BaseSystem
{
    private readonly SimWorld _simWorld;
    private readonly GameplayStore _gameplayStore;
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly NetworkStore _networkStore;
    private readonly CameraManager _camera;
    private readonly InputStore _inputStore;

    // Loaded from GameDocDb
    private readonly int _barHeight;

    // Minimap settings
    private const int MinimapSize = 110;
    private const int UnitDotSize = 2;
    private const int BuildingMinSize = 2;
    private const int ZombieDotSize = 1;
    private readonly float _minimapScale;  // World coords to minimap pixels

    private const int ResourcePanelWidth = 200;  // Wider for 5 resources + population
    private const int Padding = 5;
    private const int ButtonSize = 48;
    private const int ButtonMargin = 4;

    // Category name lookup (GameDataId doesn't store the actual string)
    private static readonly string[] CategoryNames = { "Housing", "Economy", "Defense", "Infrastructure", "Research" };

    // Cached category and building data (sorted at init, no per-frame allocations)
    private readonly int[] _sortedCategoryIds;
    private readonly Dictionary<int, int[]> _buildingsByCategory;  // CategoryId -> sorted BuildingTypeIds
    private readonly Dictionary<int, int[]> _trainableUnitsByBuilding;  // BuildingTypeId -> UnitTypeIds that can be trained
    private readonly HashSet<int> _workshopBuildingIds;  // Building IDs that have research mappings

    public BottomBarUI(SimWorld simWorld, GameplayStore gameplayStore, GameDataManager<GameDocDb> gameData, NetworkStore networkStore, CameraManager camera, InputStore inputStore)
    {
        _simWorld = simWorld;
        _gameplayStore = gameplayStore;
        _gameData = gameData;
        _networkStore = networkStore;
        _camera = camera;
        _inputStore = inputStore;

        // Load UI config from GameDocDb
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _barHeight = mapConfig.BottomBarHeight;

        // Pre-calculate minimap scale factor (world to minimap pixels)
        float worldSize = GameConfig.Map.WidthTiles * GameConfig.Map.TileSize;
        _minimapScale = MinimapSize / worldSize;

        // Initialize cached category and building data
        _buildingsByCategory = new Dictionary<int, int[]>();
        var categoryDb = gameData.Db.BuildingCategoryData;
        var buildingDb = gameData.Db.BuildingTypeData;

        // Collect and sort categories by DisplayOrder
        var categoryList = new List<(int id, int order)>();
        for (int i = 0; i < categoryDb.Count; i++)
        {
            ref readonly var cat = ref categoryDb.FindById(i);
            categoryList.Add((cat.Id, cat.DisplayOrder));
        }
        categoryList.Sort((a, b) => a.order.CompareTo(b.order));
        _sortedCategoryIds = categoryList.Select(c => c.id).ToArray();

        // Group buildings by category, sorted by DisplayOrder
        var buildingGroups = new Dictionary<int, List<(int id, int order)>>();
        for (int i = 0; i < buildingDb.Count; i++)
        {
            ref readonly var bld = ref buildingDb.FindById(i);
            // Skip buildings with RequiredTechId = -1 (not buildable, e.g., CommandCenter)
            if (bld.RequiredTechId < 0) continue;

            int catId = bld.CategoryId;
            if (!buildingGroups.TryGetValue(catId, out var list))
            {
                list = new List<(int id, int order)>();
                buildingGroups[catId] = list;
            }
            list.Add((bld.Id, bld.DisplayOrder));
        }

        // Sort each category's buildings and store
        foreach (var kvp in buildingGroups)
        {
            kvp.Value.Sort((a, b) => a.order.CompareTo(b.order));
            _buildingsByCategory[kvp.Key] = kvp.Value.Select(b => b.id).ToArray();
        }

        // Cache trainable units by building type
        _trainableUnitsByBuilding = new Dictionary<int, int[]>();
        var unitDb = gameData.Db.UnitTypeData;
        var unitsByBuilding = new Dictionary<int, List<int>>();

        for (int i = 0; i < unitDb.Count; i++)
        {
            ref readonly var unit = ref unitDb.FindById(i);
            if (unit.TrainedAtBuildingType < 0) continue;  // Not trainable

            if (!unitsByBuilding.TryGetValue(unit.TrainedAtBuildingType, out var list))
            {
                list = new List<int>();
                unitsByBuilding[unit.TrainedAtBuildingType] = list;
            }
            list.Add(unit.Id);
        }

        foreach (var kvp in unitsByBuilding)
        {
            _trainableUnitsByBuilding[kvp.Key] = kvp.Value.ToArray();
        }

        // Cache workshop building IDs (buildings that have research mappings)
        _workshopBuildingIds = new HashSet<int>();
        var researchDb = gameData.Db.BuildingResearchData;
        for (int i = 0; i < researchDb.Count; i++)
        {
            ref readonly var mapping = ref researchDb.FindById(i);
            _workshopBuildingIds.Add(mapping.BuildingTypeId);
        }
    }

    protected override void OnUpdateGroup()
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();
        int barY = screenHeight - _barHeight;

        // Draw main bar background
        ImmediateModeUI.DrawPanel(0, barY, screenWidth, _barHeight, ImmediateModeUI.PanelColor, ImmediateModeUI.PanelBorderColor);

        // Left: Minimap
        DrawMinimapSection(Padding, barY + Padding);

        // Right: Resources
        int resourceX = screenWidth - ResourcePanelWidth - Padding;
        DrawResourcesSection(resourceX, barY + Padding, ResourcePanelWidth);

        // Center: Action area (selection info + build buttons)
        int actionX = MinimapSize + Padding * 3;
        int actionWidth = screenWidth - MinimapSize - ResourcePanelWidth - Padding * 6;
        DrawActionSection(actionX, barY + Padding, actionWidth);

        // Draw destroy confirmation modal on top of everything
        DrawDestroyConfirmationModal();
    }

    private void DrawMinimapSection(int x, int y)
    {
        // Background
        Raylib.DrawRectangle(x, y, MinimapSize, MinimapSize, new Color(20, 30, 20, 255));

        // Get fog of war state
        var fogTable = _simWorld.FogOfWarGridStateRows;
        bool hasFogOfWar = fogTable.Count > 0 && !GameConfig.Debug.DisableFogOfWar;
        int localPlayer = _networkStore.LocalSlot.Value;

        // Draw fog overlay first (unexplored/fogged areas)
        if (hasFogOfWar)
        {
            DrawMinimapFog(x, y, fogTable);
        }

        // Draw entities (order: buildings, zombies, units - so units are on top)
        DrawMinimapBuildings(x, y, fogTable, hasFogOfWar, localPlayer);
        DrawMinimapZombies(x, y, fogTable, hasFogOfWar);
        DrawMinimapUnits(x, y, fogTable, hasFogOfWar, localPlayer);

        // Draw camera viewport rectangle
        DrawMinimapViewport(x, y);

        // Border on top
        Raylib.DrawRectangleLines(x, y, MinimapSize, MinimapSize, ImmediateModeUI.PanelBorderColor);

        // Handle click-to-pan
        HandleMinimapClick(x, y);
    }

    private void DrawMinimapFog(int mapX, int mapY, FogOfWarGridStateRowTable fogTable)
    {
        // Draw fog overlay on minimap - sample at lower resolution for performance
        const int sampleStep = 8;  // Sample every 8 tiles (32 samples across 256 tiles)
        float pixelsPerSample = MinimapSize / (float)(GameConfig.Map.WidthTiles / sampleStep);

        for (int ty = 0; ty < GameConfig.Map.HeightTiles; ty += sampleStep)
        {
            for (int tx = 0; tx < GameConfig.Map.WidthTiles; tx += sampleStep)
            {
                byte visibility = FogOfWarService.GetVisibility(fogTable, tx, ty);

                if (visibility == 0)  // Unexplored
                {
                    int px = mapX + (int)(tx * _minimapScale);
                    int py = mapY + (int)(ty * _minimapScale);
                    int size = (int)Math.Ceiling(pixelsPerSample);
                    Raylib.DrawRectangle(px, py, size, size, new Color(0, 0, 0, 200));
                }
                else if (visibility == 1)  // Fogged
                {
                    int px = mapX + (int)(tx * _minimapScale);
                    int py = mapY + (int)(ty * _minimapScale);
                    int size = (int)Math.Ceiling(pixelsPerSample);
                    Raylib.DrawRectangle(px, py, size, size, new Color(20, 20, 30, 120));
                }
            }
        }
    }

    private void DrawMinimapBuildings(int mapX, int mapY, FogOfWarGridStateRowTable fogTable, bool hasFogOfWar, int localPlayer)
    {
        var buildings = _simWorld.BuildingRows;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var b = buildings.GetRowBySlot(slot);
            if (b.Flags.IsDead()) continue;

            // Fog of war check - own buildings always visible, enemy buildings need explored
            if (hasFogOfWar && b.OwnerPlayerId != localPlayer)
            {
                // Check if any tile of building is explored
                bool isExplored = false;
                for (int ty = b.TileY; ty < b.TileY + b.Height && !isExplored; ty++)
                {
                    for (int tx = b.TileX; tx < b.TileX + b.Width && !isExplored; tx++)
                    {
                        if (FogOfWarService.IsTileExplored(fogTable, tx, ty))
                        {
                            isExplored = true;
                        }
                    }
                }
                if (!isExplored) continue;
            }

            // Convert world position to minimap position
            int mx = mapX + (int)(b.Position.X.ToFloat() * _minimapScale);
            int my = mapY + (int)(b.Position.Y.ToFloat() * _minimapScale);

            // Size based on building dimensions (min 2px)
            int w = Math.Max(BuildingMinSize, (int)(b.Width * GameConfig.Map.TileSize * _minimapScale));
            int h = Math.Max(BuildingMinSize, (int)(b.Height * GameConfig.Map.TileSize * _minimapScale));

            Color color = GetPlayerColor(b.OwnerPlayerId);
            Raylib.DrawRectangle(mx - w / 2, my - h / 2, w, h, color);
        }
    }

    private void DrawMinimapZombies(int mapX, int mapY, FogOfWarGridStateRowTable fogTable, bool hasFogOfWar)
    {
        var zombies = _simWorld.ZombieRows;
        for (int slot = 0; slot < zombies.Count; slot++)
        {
            var z = zombies.GetRowBySlot(slot);
            if (z.Flags.IsDead()) continue;

            // Fog of war check - zombies only visible in currently visible tiles
            if (hasFogOfWar)
            {
                int tileX = (int)(z.Position.X.ToFloat() / GameConfig.Map.TileSize);
                int tileY = (int)(z.Position.Y.ToFloat() / GameConfig.Map.TileSize);
                if (!FogOfWarService.IsTileVisible(fogTable, tileX, tileY))
                {
                    continue;
                }
            }

            int mx = mapX + (int)(z.Position.X.ToFloat() * _minimapScale);
            int my = mapY + (int)(z.Position.Y.ToFloat() * _minimapScale);

            Raylib.DrawRectangle(mx, my, ZombieDotSize, ZombieDotSize, Color.Red);
        }
    }

    private void DrawMinimapUnits(int mapX, int mapY, FogOfWarGridStateRowTable fogTable, bool hasFogOfWar, int localPlayer)
    {
        var units = _simWorld.CombatUnitRows;
        for (int slot = 0; slot < units.Count; slot++)
        {
            var u = units.GetRowBySlot(slot);
            if (u.Flags.IsDead()) continue;

            // Fog of war check - own units always visible, enemy units need visible tile
            if (hasFogOfWar && u.OwnerPlayerId != localPlayer)
            {
                int tileX = (int)(u.Position.X.ToFloat() / GameConfig.Map.TileSize);
                int tileY = (int)(u.Position.Y.ToFloat() / GameConfig.Map.TileSize);
                if (!FogOfWarService.IsTileVisible(fogTable, tileX, tileY))
                {
                    continue;
                }
            }

            int mx = mapX + (int)(u.Position.X.ToFloat() * _minimapScale);
            int my = mapY + (int)(u.Position.Y.ToFloat() * _minimapScale);

            Color color = GetPlayerColor(u.OwnerPlayerId);
            Raylib.DrawRectangle(mx, my, UnitDotSize, UnitDotSize, color);
        }
    }

    private void DrawMinimapViewport(int mapX, int mapY)
    {
        // Camera center in minimap coords
        float camX = _camera.TargetX * _minimapScale;
        float camY = _camera.TargetY * _minimapScale;

        // Viewport size in minimap coords
        float vpW = _camera.GameWidth * _minimapScale;
        float vpH = _camera.GameHeight * _minimapScale;

        int rx = mapX + (int)(camX - vpW * 0.5f);
        int ry = mapY + (int)(camY - vpH * 0.5f);

        Raylib.DrawRectangleLines(rx, ry, (int)vpW, (int)vpH, Color.White);
    }

    private void HandleMinimapClick(int mapX, int mapY)
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;

        var mouse = _inputStore.InputManager.Device.MousePosition;
        var minimapRect = new Rectangle(mapX, mapY, MinimapSize, MinimapSize);

        if (!Raylib.CheckCollisionPointRec(mouse, minimapRect)) return;

        // Convert minimap click to world position
        float relX = (mouse.X - mapX) / MinimapSize;
        float relY = (mouse.Y - mapY) / MinimapSize;

        float worldX = relX * GameConfig.Map.WidthTiles * GameConfig.Map.TileSize;
        float worldY = relY * GameConfig.Map.HeightTiles * GameConfig.Map.TileSize;

        _camera.SetTarget(worldX, worldY);
    }

    private static Color GetPlayerColor(byte playerId)
    {
        return playerId switch
        {
            0 => new Color(100, 150, 255, 255),  // Blue
            1 => new Color(255, 100, 100, 255),  // Red
            2 => new Color(100, 255, 100, 255),  // Green
            3 => new Color(255, 255, 100, 255),  // Yellow
            _ => new Color(200, 200, 200, 255),  // Gray
        };
    }

    private void DrawResourcesSection(int x, int y, int width)
    {
        int height = MinimapSize;

        // Section background
        Raylib.DrawRectangle(x, y, width, height, new Color(25, 25, 35, 255));
        Raylib.DrawRectangleLines(x, y, width, height, ImmediateModeUI.PanelBorderColor);

        // Get team resources (shared base co-op: all players share slot 0's resources)
        var resources = GetPlayerResources(0);

        // Two columns: left for resources, right for power/pop
        int iconSize = 12;
        int fontSize = 11;
        int rowHeight = 14;
        int colWidth = width / 2;
        int leftX = x + 6;
        int rightX = x + colWidth + 4;

        // Left column: Gold, Wood, Stone, Iron, Oil
        int rowY = y + 6;

        // Gold
        Raylib.DrawRectangle(leftX, rowY, iconSize, iconSize, new Color(255, 215, 0, 255));
        DrawResourceWithMax(leftX + iconSize + 4, rowY, resources.gold, resources.maxGold, new Color(255, 215, 0, 255), fontSize, resources.netGoldRate);
        rowY += rowHeight + 2;

        // Wood
        Raylib.DrawRectangle(leftX, rowY, iconSize, iconSize, new Color(139, 90, 43, 255));
        DrawResourceWithMax(leftX + iconSize + 4, rowY, resources.wood, resources.maxWood, new Color(180, 130, 70, 255), fontSize, resources.netWoodRate);
        rowY += rowHeight + 2;

        // Stone
        Raylib.DrawRectangle(leftX, rowY, iconSize, iconSize, new Color(128, 128, 128, 255));
        DrawResourceWithMax(leftX + iconSize + 4, rowY, resources.stone, resources.maxStone, new Color(160, 160, 160, 255), fontSize, resources.netStoneRate);
        rowY += rowHeight + 2;

        // Iron
        Raylib.DrawRectangle(leftX, rowY, iconSize, iconSize, new Color(100, 120, 140, 255));
        DrawResourceWithMax(leftX + iconSize + 4, rowY, resources.iron, resources.maxIron, new Color(140, 160, 180, 255), fontSize, resources.netIronRate);
        rowY += rowHeight + 2;

        // Oil
        Raylib.DrawRectangle(leftX, rowY, iconSize, iconSize, new Color(40, 40, 40, 255));
        DrawResourceWithMax(leftX + iconSize + 4, rowY, resources.oil, resources.maxOil, new Color(80, 80, 100, 255), fontSize, resources.netOilRate);

        // Right column: Power and Population
        rowY = y + 6;

        // Power (Energy)
        Raylib.DrawRectangle(rightX, rowY, iconSize, iconSize, new Color(255, 255, 100, 255));
        string powerText = $"{resources.energy}/{resources.maxEnergy}";
        Color powerColor = resources.energy >= 0 ? new Color(255, 255, 100, 255) : new Color(255, 100, 100, 255);
        Raylib.DrawText(powerText, rightX + iconSize + 4, rowY, fontSize, powerColor);
        rowY += rowHeight + 2;

        // Population
        Raylib.DrawRectangle(rightX, rowY, iconSize, iconSize, new Color(100, 200, 255, 255));
        string popText = $"{resources.population}/{resources.maxPopulation}";
        Color popColor = resources.population <= resources.maxPopulation ? new Color(100, 200, 255, 255) : new Color(255, 100, 100, 255);
        Raylib.DrawText(popText, rightX + iconSize + 4, rowY, fontSize, popColor);

        // Labels at bottom
        int labelY = y + height - 16;
        Raylib.DrawText("Resources", leftX, labelY, 10, ImmediateModeUI.TextDisabledColor);
        Raylib.DrawText("Capacity", rightX, labelY, 10, ImmediateModeUI.TextDisabledColor);
    }

    private static void DrawResourceWithMax(int x, int y, int value, int max, Color color, int fontSize, int netRate = 0)
    {
        string text = $"{value}/{max}";
        // Show red if negative (debt) or at/over capacity
        Color textColor = value < 0 ? new Color(255, 100, 100, 255) :
                          value >= max ? new Color(255, 200, 100, 255) : color;
        Raylib.DrawText(text, x, y, fontSize, textColor);

        // Show net rate if non-zero
        if (netRate != 0)
        {
            int textWidth = Raylib.MeasureText(text, fontSize);
            string rateText = netRate > 0 ? $"+{netRate}" : $"{netRate}";
            Color rateColor = netRate > 0 ? new Color(100, 255, 100, 255) : new Color(255, 100, 100, 255);
            Raylib.DrawText(rateText, x + textWidth + 3, y, fontSize - 1, rateColor);
        }
    }

    private void DrawActionSection(int x, int y, int width)
    {
        int height = MinimapSize;

        // Section background
        Raylib.DrawRectangle(x, y, width, height, new Color(25, 25, 35, 255));
        Raylib.DrawRectangleLines(x, y, width, height, ImmediateModeUI.PanelBorderColor);

        int localPlayerId = _networkStore.LocalSlot.Value;
        if (localPlayerId < 0) localPlayerId = 0; // Default to 0 if slot not yet assigned

        var selectedBuilding = GetSelectedBuilding(localPlayerId);

        // === 3-Section Layout: Portrait | Stats/Production | Action Grid ===
        const int PortraitSize = 80;
        const int StatsWidth = 160;

        // Section 1: Portrait (left)
        int portraitX = x + Padding;
        int portraitY = y + Padding;
        DrawPortraitSection(portraitX, portraitY, PortraitSize, height - Padding * 2, selectedBuilding, localPlayerId);

        // Divider 1
        int divider1X = portraitX + PortraitSize + Padding;
        Raylib.DrawLine(divider1X, y + 5, divider1X, y + height - 5, ImmediateModeUI.PanelBorderColor);

        // Section 2: Stats/Production panel (middle)
        int statsX = divider1X + Padding;
        int statsY = y + Padding;
        DrawStatsOrProductionPanel(statsX, statsY, StatsWidth, height - Padding * 2, selectedBuilding, localPlayerId);

        // Divider 2
        int divider2X = statsX + StatsWidth + Padding;
        Raylib.DrawLine(divider2X, y + 5, divider2X, y + height - 5, ImmediateModeUI.PanelBorderColor);

        // Section 3: Action Grid (right) - 5x3 grid
        int gridX = divider2X + Padding;
        int gridWidth = width - (gridX - x) - Padding;
        DrawActionGrid(gridX, y + Padding, gridWidth, height - Padding * 2, selectedBuilding, localPlayerId);
    }

    /// <summary>
    /// Section 1: Portrait - Shows building/unit image or build mode preview
    /// </summary>
    private void DrawPortraitSection(int x, int y, int size, int height,
        (bool found, int buildingSlot, BuildingTypeId typeId, int health, int maxHealth, byte ownerPlayerId, ushort tileX, ushort tileY, byte garrisonCount, int garrisonCapacity, byte currentResearchId, int researchProgress, BuildingFlags flags) building,
        int playerId)
    {
        // Background
        Raylib.DrawRectangle(x, y, size, size, new Color(35, 35, 45, 255));
        Raylib.DrawRectangleLines(x, y, size, size, ImmediateModeUI.PanelBorderColor);

        // Build mode preview
        var buildModeType = _gameplayStore.BuildModeType.Value;
        if (buildModeType.HasValue)
        {
            string abbrev = GetAbbreviation(buildModeType.Value);
            int fontSize = 24;
            int textWidth = Raylib.MeasureText(abbrev, fontSize);
            Raylib.DrawText(abbrev, x + (size - textWidth) / 2, y + size / 2 - 12, fontSize, ImmediateModeUI.TextColor);

            string name = buildModeType.Value.ToString();
            int nameWidth = Raylib.MeasureText(name, 10);
            Raylib.DrawText(name, x + (size - nameWidth) / 2, y + size - 16, 10, ImmediateModeUI.TextDisabledColor);
            return;
        }

        // Selected building
        if (building.found)
        {
            string abbrev = GetAbbreviation(building.typeId);
            int fontSize = 24;
            int textWidth = Raylib.MeasureText(abbrev, fontSize);
            Raylib.DrawText(abbrev, x + (size - textWidth) / 2, y + size / 2 - 12, fontSize, ImmediateModeUI.TextColor);

            string name = building.typeId.ToString();
            int nameWidth = Raylib.MeasureText(name, 10);
            Raylib.DrawText(name, x + (size - nameWidth) / 2, y + size - 16, 10, ImmediateModeUI.TextDisabledColor);
            return;
        }

        // Selected units
        int unitCount = GetSelectedUnitCount(playerId);
        if (unitCount > 0)
        {
            string abbrev = unitCount > 1 ? $"{unitCount}x" : "UNIT";
            int fontSize = 20;
            int textWidth = Raylib.MeasureText(abbrev, fontSize);
            Raylib.DrawText(abbrev, x + (size - textWidth) / 2, y + size / 2 - 10, fontSize, ImmediateModeUI.TextColor);
            return;
        }

        // Nothing selected
        Raylib.DrawText("---", x + size / 2 - 12, y + size / 2 - 8, 16, ImmediateModeUI.TextDisabledColor);
    }

    /// <summary>
    /// Section 2: Stats/Production panel - Shows stats OR production queue (mutually exclusive)
    /// </summary>
    private void DrawStatsOrProductionPanel(int x, int y, int width, int height,
        (bool found, int buildingSlot, BuildingTypeId typeId, int health, int maxHealth, byte ownerPlayerId, ushort tileX, ushort tileY, byte garrisonCount, int garrisonCapacity, byte currentResearchId, int researchProgress, BuildingFlags flags) building,
        int playerId)
    {
        // Build mode preview - show building costs and description
        var buildModeType = _gameplayStore.BuildModeType.Value;
        if (buildModeType.HasValue)
        {
            DrawBuildModeStats(x, y, width, height, buildModeType.Value);
            return;
        }

        // No building selected
        if (!building.found)
        {
            int unitCount = GetSelectedUnitCount(playerId);
            if (unitCount > 0)
            {
                Raylib.DrawText($"{unitCount} unit{(unitCount > 1 ? "s" : "")}", x, y + 10, 14, ImmediateModeUI.TextColor);
                Raylib.DrawText("selected", x, y + 28, 12, ImmediateModeUI.TextDisabledColor);
            }
            else
            {
                Raylib.DrawText("No selection", x, y + height / 2 - 8, 12, ImmediateModeUI.TextDisabledColor);
            }
            return;
        }

        // Get building row for production check
        var buildings = _simWorld.BuildingRows;
        if (!buildings.TryGetRow(building.buildingSlot, out var row)) return;

        // Check if building is under construction
        if (row.Flags.HasFlag(BuildingFlags.IsUnderConstruction))
        {
            DrawConstructionProgressPanel(x, y, width, height, ref row, building.typeId);
            return;
        }

        // Check if building is currently training a unit
        bool isTraining = row.ProductionQueue0 != 255;

        if (isTraining)
        {
            // Show production queue (replaces stats)
            DrawProductionQueuePanel(x, y, width, height, ref row, building.buildingSlot);
        }
        else
        {
            // Show stats
            DrawBuildingStatsPanel(x, y, width, height, building, ref row);
        }
    }

    /// <summary>
    /// Shows building stats when NOT training
    /// </summary>
    private void DrawBuildingStatsPanel(int x, int y, int width, int height,
        (bool found, int buildingSlot, BuildingTypeId typeId, int health, int maxHealth, byte ownerPlayerId, ushort tileX, ushort tileY, byte garrisonCount, int garrisonCapacity, byte currentResearchId, int researchProgress, BuildingFlags flags) building,
        ref BuildingRowRowRef row)
    {
        // Health bar
        DrawHealthBar(x, y, width - 10, 12, building.health, building.maxHealth);

        int statY = y + 18;
        int fontSize = 10;

        // Resource generation
        var genParts = new List<string>();
        if (row.EffectiveGeneratesGold > 0) genParts.Add($"+{row.EffectiveGeneratesGold}g");
        if (row.EffectiveGeneratesWood > 0) genParts.Add($"+{row.EffectiveGeneratesWood}w");
        if (row.EffectiveGeneratesStone > 0) genParts.Add($"+{row.EffectiveGeneratesStone}s");
        if (row.EffectiveGeneratesIron > 0) genParts.Add($"+{row.EffectiveGeneratesIron}i");
        if (row.EffectiveGeneratesOil > 0) genParts.Add($"+{row.EffectiveGeneratesOil}o");

        if (genParts.Count > 0)
        {
            Raylib.DrawText(string.Join(" ", genParts), x, statY, fontSize, new Color(100, 255, 100, 255));
            statY += 14;
        }

        // Storage capacity
        var storageParts = new List<string>();
        if (row.ProvidesMaxGold > 0) storageParts.Add($"{row.ProvidesMaxGold}g");
        if (row.ProvidesMaxWood > 0) storageParts.Add($"{row.ProvidesMaxWood}w");
        if (row.ProvidesMaxStone > 0) storageParts.Add($"{row.ProvidesMaxStone}s");
        if (row.ProvidesMaxIron > 0) storageParts.Add($"{row.ProvidesMaxIron}i");
        if (row.ProvidesMaxOil > 0) storageParts.Add($"{row.ProvidesMaxOil}o");

        if (storageParts.Count > 0)
        {
            Raylib.DrawText($"Cap: {string.Join(" ", storageParts)}", x, statY, fontSize, new Color(180, 180, 200, 255));
            statY += 14;
        }

        // Population
        if (row.ProvidesMaxPopulation > 0)
        {
            Raylib.DrawText($"Housing: +{row.ProvidesMaxPopulation}", x, statY, fontSize, new Color(100, 200, 255, 255));
            statY += 14;
        }

        // Garrison
        if (building.garrisonCapacity > 0)
        {
            Raylib.DrawText($"Garrison: {building.garrisonCount}/{building.garrisonCapacity}", x, statY, fontSize, ImmediateModeUI.TextDisabledColor);
            statY += 14;
        }

        // Combat stats for turrets
        if (row.Damage > 0)
        {
            Raylib.DrawText($"DMG:{row.Damage} RNG:{row.AttackRange.ToFloat():F0}", x, statY, fontSize, new Color(255, 150, 100, 255));
            statY += 14;
        }

        // Upgrade button if building can be upgraded
        ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById((int)building.typeId);
        if (typeData.UpgradesTo >= 0)
        {
            ref readonly var upgradeTypeData = ref _gameData.Db.BuildingTypeData.FindById(typeData.UpgradesTo);

            // Check if upgrade is unlocked (tech requirement met)
            var resources = GetPlayerResources(0);
            ulong unlockedTech = GetPlayerUnlockedTech(building.ownerPlayerId);
            bool isUnlocked = upgradeTypeData.RequiredTechId <= 0 || ((unlockedTech >> upgradeTypeData.RequiredTechId) & 1) == 1;

            // Check affordability
            bool canAfford = resources.gold >= upgradeTypeData.CostGold
                          && resources.wood >= upgradeTypeData.CostWood
                          && resources.stone >= upgradeTypeData.CostStone
                          && resources.iron >= upgradeTypeData.CostIron
                          && resources.oil >= upgradeTypeData.CostOil;

            bool canUpgrade = isUnlocked && canAfford;

            // Draw upgrade button
            int btnY = y + height - 28;
            int btnWidth = width - 10;
            int btnHeight = 24;

            var mousePos = _inputStore.InputManager.Device.MousePosition;
            var btnRect = new Rectangle(x, btnY, btnWidth, btnHeight);
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
            bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left) && canUpgrade;

            Color btnColor;
            if (!isUnlocked)
                btnColor = new Color(60, 40, 40, 255); // Locked
            else if (!canAfford)
                btnColor = new Color(40, 40, 50, 255); // Unaffordable
            else if (isHovered)
                btnColor = new Color(80, 120, 80, 255); // Hover
            else
                btnColor = new Color(60, 100, 60, 255); // Available

            Raylib.DrawRectangle(x, btnY, btnWidth, btnHeight, btnColor);
            Raylib.DrawRectangleLines(x, btnY, btnWidth, btnHeight, ImmediateModeUI.PanelBorderColor);

            // Button text
            string upgradeName = ((BuildingTypeId)typeData.UpgradesTo).ToString();
            string btnText = $"Upgrade: {upgradeName}";
            Color textColor = canUpgrade ? ImmediateModeUI.TextColor : ImmediateModeUI.TextDisabledColor;
            Raylib.DrawText(btnText, x + 4, btnY + 5, 10, textColor);

            // Cost on right side
            string costText = $"{upgradeTypeData.CostGold}g";
            Color costColor = canAfford ? new Color(255, 215, 0, 200) : new Color(200, 80, 80, 200);
            int costWidth = Raylib.MeasureText(costText, 10);
            Raylib.DrawText(costText, x + btnWidth - costWidth - 4, btnY + 5, 10, costColor);

            if (isClicked)
            {
                var buildings = _simWorld.BuildingRows;
                var buildingHandle = buildings.GetHandle(building.buildingSlot);
                _gameplayStore.UpgradeBuilding(buildingHandle);
            }
        }
    }

    /// <summary>
    /// Shows production queue when training (replaces stats)
    /// Clicking on the queued unit cancels training.
    /// Shows all 5 queue slots.
    /// </summary>
    private void DrawProductionQueuePanel(int x, int y, int width, int height, ref BuildingRowRowRef row, int buildingSlot)
    {
        // Header
        Raylib.DrawText("Training", x, y, 12, ImmediateModeUI.TextColor);

        var queue = row.ProductionQueueArray;
        var mousePos = _inputStore.InputManager.Device.MousePosition;

        // Draw all queued units (up to 5 slots)
        int btnSize = 28;  // Smaller buttons to fit 5
        int btnMargin = 3;
        int btnY = y + 18;

        for (int i = 0; i < queue.Length; i++)
        {
            if (queue[i] == 255) continue;  // Empty slot

            int btnX = x + i * (btnSize + btnMargin);
            var btnRect = new Rectangle(btnX, btnY, btnSize, btnSize);
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
            bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

            // Button with hover effect (red tint when hovered to indicate cancel)
            Color btnColor = isHovered ? new Color(180, 80, 80, 255) :
                             i == 0 ? new Color(80, 100, 120, 255) :  // Slot 0 (in progress) slightly brighter
                             new Color(60, 80, 100, 255);
            Raylib.DrawRectangle(btnX, btnY, btnSize, btnSize, btnColor);
            Raylib.DrawRectangleLines(btnX, btnY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);

            // Unit abbreviation
            string unitAbbrev = GetUnitAbbreviation((UnitTypeId)queue[i]);
            int fontSize = 10;
            int textWidth = Raylib.MeasureText(unitAbbrev, fontSize);
            Raylib.DrawText(unitAbbrev, btnX + (btnSize - textWidth) / 2, btnY + 4, fontSize, ImmediateModeUI.TextColor);

            // Queue position indicator
            Raylib.DrawText($"{i + 1}", btnX + 2, btnY + btnSize - 10, 8, ImmediateModeUI.TextDisabledColor);

            // Cancel hint on hover
            if (isHovered)
            {
                Raylib.DrawText("X", btnX + btnSize - 10, btnY + 1, 10, new Color(255, 100, 100, 255));
            }

            // Cancel on click
            if (isClicked)
            {
                var buildingHandle = _simWorld.BuildingRows.GetHandle(buildingSlot);
                _gameplayStore.CancelTraining(buildingHandle, (byte)i);
            }
        }

        // Progress bar for slot 0 (only if something is training)
        if (queue[0] != 255)
        {
            int progBarY = btnY + btnSize + 4;
            int progBarWidth = width - 10;
            float progress = row.ProductionBuildTime > 0
                ? (float)row.ProductionProgress / row.ProductionBuildTime
                : 0;
            DrawProductionProgress(x, progBarY, progBarWidth, 8, progress);

            // Time remaining and unit name
            float timeRemaining = row.ProductionBuildTime > 0
                ? (row.ProductionBuildTime - row.ProductionProgress) / 60f
                : 0;
            string unitName = ((UnitTypeId)queue[0]).ToString();
            Raylib.DrawText($"{unitName}: {timeRemaining:F1}s", x, progBarY + 12, 10, ImmediateModeUI.TextDisabledColor);
        }
    }

    /// <summary>
    /// Shows construction progress for buildings under construction
    /// </summary>
    private void DrawConstructionProgressPanel(int x, int y, int width, int height, ref BuildingRowRowRef row, BuildingTypeId typeId)
    {
        // Header
        Raylib.DrawText("Under Construction", x, y, 12, new Color(255, 200, 100, 255));

        // Building name
        string buildingName = typeId.ToString();
        Raylib.DrawText(buildingName, x, y + 16, 10, ImmediateModeUI.TextDisabledColor);

        // Progress bar
        int progBarY = y + 32;
        int progBarWidth = width - 10;
        float progress = row.ConstructionBuildTime > 0
            ? (float)row.ConstructionProgress / row.ConstructionBuildTime
            : 0;

        // Use orange color for construction progress
        Raylib.DrawRectangle(x, progBarY, progBarWidth, 10, new Color(40, 40, 50, 255));
        int fillWidth = (int)(progBarWidth * progress);
        if (fillWidth > 0)
        {
            Raylib.DrawRectangle(x, progBarY, fillWidth, 10, new Color(255, 180, 80, 255));
        }
        Raylib.DrawRectangleLines(x, progBarY, progBarWidth, 10, ImmediateModeUI.PanelBorderColor);

        // Time remaining
        float timeRemaining = row.ConstructionBuildTime > 0
            ? (row.ConstructionBuildTime - row.ConstructionProgress) / 60f
            : 0;
        Raylib.DrawText($"{timeRemaining:F1}s remaining", x, progBarY + 14, 10, ImmediateModeUI.TextDisabledColor);

        // Progress percentage
        int percentage = (int)(progress * 100);
        Raylib.DrawText($"{percentage}%", x + progBarWidth - 25, progBarY + 14, 10, new Color(255, 200, 100, 255));

        // Health bar (buildings under construction can be attacked)
        int hpBarY = progBarY + 32;
        DrawHealthBar(x, hpBarY, progBarWidth, 8, row.Health, row.MaxHealth);
        Raylib.DrawText($"HP: {row.Health}/{row.MaxHealth}", x, hpBarY + 10, 9, ImmediateModeUI.TextDisabledColor);
    }

    /// <summary>
    /// Shows building costs/description in build mode
    /// </summary>
    private void DrawBuildModeStats(int x, int y, int width, int height, BuildingTypeId buildingType)
    {
        ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById((int)buildingType);

        // Costs
        string costText = FormatBuildingCosts(typeData);
        Raylib.DrawText(costText, x, y, 11, new Color(255, 215, 0, 200));

        // Size
        Raylib.DrawText($"Size: {typeData.Width}x{typeData.Height}", x, y + 14, 10, ImmediateModeUI.TextDisabledColor);

        // HP
        Raylib.DrawText($"HP: {typeData.Health}", x, y + 28, 10, ImmediateModeUI.TextDisabledColor);

        // Brief description
        string desc = GetBuildingDescription(typeData);
        int descY = y + 44;
        DrawWrappedText(desc, x, descY, width - 5, 9, ImmediateModeUI.TextDisabledColor);
    }

    /// <summary>
    /// Section 3: Action Grid - 5x3 context-sensitive buttons
    /// </summary>
    private void DrawActionGrid(int x, int y, int width, int height,
        (bool found, int buildingSlot, BuildingTypeId typeId, int health, int maxHealth, byte ownerPlayerId, ushort tileX, ushort tileY, byte garrisonCount, int garrisonCapacity, byte currentResearchId, int researchProgress, BuildingFlags flags) building,
        int playerId)
    {
        // Check contexts
        bool showGarrisonUI = building.found &&
                              building.garrisonCapacity > 0 &&
                              building.garrisonCount > 0;

        bool isProductionBuilding = building.found &&
                                    CanBuildingTrainUnits(building.typeId);

        bool isResearchBuilding = building.found &&
                                  IsWorkshopBuilding(building.typeId);

        // Use fixed 5 columns, 3 rows grid
        const int GridCols = 5;
        const int GridRows = 3;
        int btnSize = (width - (GridCols - 1) * ButtonMargin) / GridCols;
        if (btnSize > 32) btnSize = 32; // Cap button size

        if (showGarrisonUI)
        {
            DrawGarrisonGrid(x, y, width, height, btnSize, building.buildingSlot, building.garrisonCount);
        }
        else if (isResearchBuilding)
        {
            DrawResearchGrid(x, y, width, height, btnSize, building.buildingSlot, building.typeId, building.currentResearchId, building.researchProgress, playerId);
        }
        else if (isProductionBuilding)
        {
            DrawUnitTrainingGrid(x, y, width, height, btnSize, building.buildingSlot, building.typeId, playerId);
        }
        else
        {
            // Check if units are selected - show unit commands instead of build grid
            int unitCount = GetSelectedUnitCount(playerId);
            if (unitCount > 0 && !building.found)
            {
                DrawUnitCommandGrid(x, y, width, height, btnSize, playerId);
            }
            else
            {
                DrawBuildGrid(x, y, width, height, btnSize, playerId);
            }
        }

        // Draw repair/destroy buttons for player's own active buildings (bottom-right)
        if (building.found &&
            building.ownerPlayerId == playerId &&
            building.flags.HasFlag(BuildingFlags.IsActive) &&
            !building.flags.HasFlag(BuildingFlags.IsUnderConstruction))
        {
            DrawBuildingActionButtons(x, y, btnSize, building.buildingSlot, building.typeId, building.health, building.maxHealth, building.flags, playerId);
        }
    }

    /// <summary>
    /// Action grid for garrison - shows garrisoned units with eject
    /// </summary>
    private void DrawGarrisonGrid(int x, int y, int width, int height, int btnSize, int buildingSlot, byte garrisonCount)
    {
        var buildings = _simWorld.BuildingRows;
        var units = _simWorld.CombatUnitRows;

        if (!buildings.TryGetRow(buildingSlot, out var building)) return;

        var mousePos = _inputStore.InputManager.Device.MousePosition;

        // Draw garrisoned unit buttons
        for (int i = 0; i < garrisonCount && i < 6; i++)
        {
            var unitHandle = building.GetGarrisonSlot(i);
            if (!unitHandle.IsValid) continue;

            int unitSlot = units.GetSlot(unitHandle);
            if (!units.TryGetRow(unitSlot, out var unit)) continue;

            int col = i % 5;
            int row = i / 5;
            int btnX = x + col * (btnSize + ButtonMargin);
            int btnY = y + row * (btnSize + ButtonMargin);

            var btnRect = new Rectangle(btnX, btnY, btnSize, btnSize);
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
            bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

            Color btnColor = isHovered ? new Color(100, 150, 200, 255) : new Color(60, 100, 140, 255);
            Raylib.DrawRectangle(btnX, btnY, btnSize, btnSize, btnColor);
            Raylib.DrawRectangleLines(btnX, btnY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);

            // Unit abbreviation
            string abbrev = GetUnitAbbreviation(unit.TypeId);
            int fontSize = 10;
            int textWidth = Raylib.MeasureText(abbrev, fontSize);
            Raylib.DrawText(abbrev, btnX + (btnSize - textWidth) / 2, btnY + 4, fontSize, ImmediateModeUI.TextColor);

            // Mini health bar
            int hpBarWidth = btnSize - 4;
            int hpBarHeight = 4;
            float hpRatio = unit.MaxHealth > 0 ? (float)unit.Health / unit.MaxHealth : 0;
            Raylib.DrawRectangle(btnX + 2, btnY + btnSize - 6, hpBarWidth, hpBarHeight, new Color(40, 20, 20, 255));
            Raylib.DrawRectangle(btnX + 2, btnY + btnSize - 6, (int)(hpBarWidth * hpRatio), hpBarHeight, new Color(80, 180, 80, 255));

            if (isClicked)
            {
                _gameplayStore.EjectSingleUnit(unitHandle);
            }
        }

        // Eject All button in last position
        int ejectCol = garrisonCount % 5;
        int ejectRow = garrisonCount / 5;
        if (ejectCol == 0 && garrisonCount > 0) { ejectCol = 0; ejectRow++; }
        int ejectX = x + ejectCol * (btnSize + ButtonMargin);
        int ejectY = y + ejectRow * (btnSize + ButtonMargin);

        if (ejectY + btnSize <= y + height)
        {
            var ejectRect = new Rectangle(ejectX, ejectY, btnSize, btnSize);
            bool ejectHovered = Raylib.CheckCollisionPointRec(mousePos, ejectRect);
            bool ejectClicked = ejectHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

            Color ejectColor = ejectHovered ? new Color(200, 120, 80, 255) : new Color(180, 100, 60, 255);
            Raylib.DrawRectangle(ejectX, ejectY, btnSize, btnSize, ejectColor);
            Raylib.DrawRectangleLines(ejectX, ejectY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);

            Raylib.DrawText("ALL", ejectX + 4, ejectY + 8, 10, ImmediateModeUI.TextColor);

            if (ejectClicked)
            {
                _gameplayStore.EjectGarrison();
            }
        }
    }

    /// <summary>
    /// Action grid for research - shows available research items for workshop buildings
    /// </summary>
    private void DrawResearchGrid(int x, int y, int width, int height, int btnSize, int buildingSlot, BuildingTypeId buildingType, byte currentResearchId, int researchProgress, int playerId)
    {
        var mousePos = _inputStore.InputManager.Device.MousePosition;
        var resources = GetPlayerResources(0);
        ulong unlockedTech = GetPlayerUnlockedTech(playerId);

        // Get research items for this building type
        var researchItems = new List<int>();
        var researchDb = _gameData.Db.BuildingResearchData;
        for (int i = 0; i < researchDb.Count; i++)
        {
            ref readonly var mapping = ref researchDb.FindById(i);
            if (mapping.BuildingTypeId == (int)buildingType)
            {
                researchItems.Add(mapping.ResearchItemId);
            }
        }

        int buttonIndex = 0;
        foreach (int researchId in researchItems)
        {
            ref readonly var research = ref _gameData.Db.ResearchItemData.FindById(researchId);

            // Check if already researched (tech bit set)
            bool alreadyResearched = research.UnlocksTechId >= 0 && ((unlockedTech >> research.UnlocksTechId) & 1) == 1;

            // Check if prerequisite is met
            bool prereqMet = research.PrerequisiteTechId < 0 || ((unlockedTech >> research.PrerequisiteTechId) & 1) == 1;

            // Check if currently researching this item
            bool isCurrentlyResearching = currentResearchId == researchId + 1;

            // Check affordability
            bool canAfford = resources.gold >= research.CostGold
                          && resources.wood >= research.CostWood
                          && resources.stone >= research.CostStone
                          && resources.iron >= research.CostIron
                          && resources.oil >= research.CostOil;

            bool canStart = !alreadyResearched && prereqMet && canAfford && currentResearchId == 0;

            int col = buttonIndex % 5;
            int row = buttonIndex / 5;
            int btnX = x + col * (btnSize + ButtonMargin);
            int btnY = y + row * (btnSize + ButtonMargin);
            buttonIndex++;

            if (btnY + btnSize > y + height) break;

            var btnRect = new Rectangle(btnX, btnY, btnSize, btnSize);
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
            bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

            // Button color
            Color bgColor;
            if (alreadyResearched)
                bgColor = new Color(40, 80, 40, 255); // Green - completed
            else if (isCurrentlyResearching)
                bgColor = new Color(80, 80, 40, 255); // Yellow - in progress
            else if (!prereqMet)
                bgColor = new Color(60, 40, 40, 255); // Dark red - locked
            else if (!canAfford)
                bgColor = new Color(40, 40, 50, 255); // Grey - unaffordable
            else if (isHovered)
                bgColor = ImmediateModeUI.ButtonHoverColor;
            else
                bgColor = ImmediateModeUI.ButtonColor;

            Raylib.DrawRectangle(btnX, btnY, btnSize, btnSize, bgColor);
            Raylib.DrawRectangleLines(btnX, btnY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);

            // Research abbreviation
            string abbrev = research.Abbreviation.ToString();
            int fontSize = 10;
            int textWidth = Raylib.MeasureText(abbrev, fontSize);
            Color textColor = canStart || isCurrentlyResearching ? ImmediateModeUI.TextColor : ImmediateModeUI.TextDisabledColor;
            Raylib.DrawText(abbrev, btnX + (btnSize - textWidth) / 2, btnY + 4, fontSize, textColor);

            // Status or cost
            if (alreadyResearched)
            {
                Raylib.DrawText("DONE", btnX + 8, btnY + btnSize - 14, 8, new Color(100, 200, 100, 255));
            }
            else if (isCurrentlyResearching)
            {
                // Progress bar
                ref readonly var researchData = ref _gameData.Db.ResearchItemData.FindById(researchId);
                float progress = researchData.ResearchTime > 0 ? (float)researchProgress / researchData.ResearchTime : 0;
                int barWidth = btnSize - 4;
                Raylib.DrawRectangle(btnX + 2, btnY + btnSize - 8, barWidth, 4, new Color(40, 40, 40, 255));
                Raylib.DrawRectangle(btnX + 2, btnY + btnSize - 8, (int)(barWidth * progress), 4, new Color(200, 200, 80, 255));
            }
            else
            {
                string costText = $"{research.CostGold}g";
                Color costColor = canAfford ? new Color(255, 215, 0, 200) : new Color(200, 80, 80, 200);
                Raylib.DrawText(costText, btnX + 4, btnY + btnSize - 14, 8, costColor);
            }

            // Click to start research
            if (isClicked && canStart)
            {
                _gameplayStore.StartResearch(researchId + 1); // +1 because 0 means cancel
            }

            // Tooltip on hover
            if (isHovered)
            {
                string tooltip = GetResearchName(researchId);
                if (!prereqMet)
                    tooltip += " (Locked)";
                else if (alreadyResearched)
                    tooltip += " (Complete)";
                else if (isCurrentlyResearching)
                    tooltip += " (In Progress)";
                else
                    tooltip += $" ({research.CostGold}g {research.CostWood}w {research.CostStone}s)";

                int tooltipWidth = Raylib.MeasureText(tooltip, 10) + 8;
                int tooltipX = btnX;
                int tooltipY = btnY - 18;
                Raylib.DrawRectangle(tooltipX, tooltipY, tooltipWidth, 16, new Color(20, 20, 30, 240));
                Raylib.DrawText(tooltip, tooltipX + 4, tooltipY + 3, 10, ImmediateModeUI.TextColor);
            }
        }

        // Cancel button if researching
        if (currentResearchId > 0)
        {
            int cancelBtnX = x + width - btnSize;
            int cancelBtnY = y;
            var cancelRect = new Rectangle(cancelBtnX, cancelBtnY, btnSize, btnSize);
            bool cancelHovered = Raylib.CheckCollisionPointRec(mousePos, cancelRect);
            bool cancelClicked = cancelHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

            Color cancelColor = cancelHovered ? new Color(200, 80, 80, 255) : new Color(150, 60, 60, 255);
            Raylib.DrawRectangle(cancelBtnX, cancelBtnY, btnSize, btnSize, cancelColor);
            Raylib.DrawRectangleLines(cancelBtnX, cancelBtnY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);
            Raylib.DrawText("X", cancelBtnX + btnSize / 2 - 4, cancelBtnY + btnSize / 2 - 6, 14, ImmediateModeUI.TextColor);

            if (cancelClicked)
            {
                _gameplayStore.StartResearch(0); // 0 = cancel
            }
        }
    }

    private string GetResearchName(int researchId)
    {
        var researchDb = _gameData.Db.ResearchItemData;
        if (researchId >= 0 && researchId < researchDb.Count)
        {
            ref readonly var research = ref researchDb.FindById(researchId);
            return research.DisplayName.ToString();
        }
        return $"Research {researchId}";
    }

    /// <summary>
    /// Action grid for unit training
    /// </summary>
    private void DrawUnitTrainingGrid(int x, int y, int width, int height, int btnSize, int buildingSlot, BuildingTypeId buildingType, int playerId)
    {
        var buildings = _simWorld.BuildingRows;
        if (!buildings.TryGetRow(buildingSlot, out var building)) return;

        if (!_trainableUnitsByBuilding.TryGetValue((int)buildingType, out var trainableUnitIds))
            return;

        var resources = GetPlayerResources(0);
        ulong unlockedTech = GetPlayerUnlockedTech(playerId);
        var buildingHandle = buildings.GetHandle(buildingSlot);
        var mousePos = _inputStore.InputManager.Device.MousePosition;

        // Check if there's any empty slot in the queue
        var queue = building.ProductionQueueArray;
        bool hasEmptySlot = false;
        for (int s = 0; s < queue.Length; s++)
        {
            if (queue[s] == 255) { hasEmptySlot = true; break; }
        }

        int buttonIndex = 0;
        for (int i = 0; i < trainableUnitIds.Length; i++)
        {
            int unitTypeId = trainableUnitIds[i];
            ref readonly var unitData = ref _gameData.Db.UnitTypeData.FindById(unitTypeId);

            // Check tech unlock
            bool isUnlocked = unitData.RequiredTechId == 0 ||
                              ((unlockedTech >> unitData.RequiredTechId) & 1) == 1;
            if (!isUnlocked) continue;

            bool canAfford = resources.gold >= unitData.CostGold &&
                            resources.wood >= unitData.CostWood &&
                            resources.stone >= unitData.CostStone &&
                            resources.iron >= unitData.CostIron &&
                            resources.oil >= unitData.CostOil;

            bool hasPopSpace = resources.population + unitData.PopulationCost <= resources.maxPopulation;
            bool isClickable = canAfford && hasPopSpace && hasEmptySlot;

            int col = buttonIndex % 5;
            int row = buttonIndex / 5;
            int btnX = x + col * (btnSize + ButtonMargin);
            int btnY = y + row * (btnSize + ButtonMargin);
            buttonIndex++;

            if (btnY + btnSize > y + height) break;

            var btnRect = new Rectangle(btnX, btnY, btnSize, btnSize);
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
            bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left) && isClickable;

            Color bgColor;
            if (!isClickable)
                bgColor = new Color(40, 40, 50, 255);
            else if (isHovered)
                bgColor = ImmediateModeUI.ButtonHoverColor;
            else
                bgColor = ImmediateModeUI.ButtonColor;

            Raylib.DrawRectangle(btnX, btnY, btnSize, btnSize, bgColor);
            Raylib.DrawRectangleLines(btnX, btnY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);

            string abbrev = GetUnitAbbreviation((UnitTypeId)unitTypeId);
            int fontSize = 10;
            int textWidth = Raylib.MeasureText(abbrev, fontSize);
            Color textColor = isClickable ? ImmediateModeUI.TextColor : ImmediateModeUI.TextDisabledColor;
            Raylib.DrawText(abbrev, btnX + (btnSize - textWidth) / 2, btnY + 4, fontSize, textColor);

            // Cost
            string costText = $"{unitData.CostGold}";
            int costWidth = Raylib.MeasureText(costText, 8);
            Color costColor = canAfford ? new Color(255, 215, 0, 200) : new Color(200, 80, 80, 200);
            Raylib.DrawText(costText, btnX + (btnSize - costWidth) / 2, btnY + btnSize - 10, 8, costColor);

            if (isClicked)
            {
                _gameplayStore.QueueTrainUnit(buildingHandle, (byte)unitTypeId);
            }
        }
    }

    /// <summary>
    /// Action grid for building placement (categories and buildings)
    /// </summary>
    private void DrawBuildGrid(int x, int y, int width, int height, int btnSize, int playerId)
    {
        ulong unlockedTech = GetPlayerUnlockedTech(playerId);
        var mousePos = _inputStore.InputManager.Device.MousePosition;
        int? openCategory = _gameplayStore.OpenCategoryId.Value;

        if (openCategory == null)
        {
            // Draw category buttons
            for (int i = 0; i < _sortedCategoryIds.Length; i++)
            {
                int categoryId = _sortedCategoryIds[i];

                int col = i % 5;
                int row = i / 5;
                int btnX = x + col * (btnSize + ButtonMargin);
                int btnY = y + row * (btnSize + ButtonMargin);

                if (btnY + btnSize > y + height) break;

                var btnRect = new Rectangle(btnX, btnY, btnSize, btnSize);
                bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
                bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

                Color bgColor = isHovered ? new Color(80, 100, 120, 255) : new Color(50, 70, 90, 255);
                Raylib.DrawRectangle(btnX, btnY, btnSize, btnSize, bgColor);
                Raylib.DrawRectangleLines(btnX, btnY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);

                string name = categoryId < CategoryNames.Length ? CategoryNames[categoryId] : $"C{categoryId}";
                string abbrev = GetCategoryAbbreviation(name);
                int fontSize = 9;
                int textWidth = Raylib.MeasureText(abbrev, fontSize);
                Raylib.DrawText(abbrev, btnX + (btnSize - textWidth) / 2, btnY + btnSize / 2 - 5, fontSize, ImmediateModeUI.TextColor);

                // Folder indicator
                Raylib.DrawText(">", btnX + btnSize - 8, btnY + 2, 8, ImmediateModeUI.TextDisabledColor);

                if (isClicked)
                {
                    _gameplayStore.OpenCategory(categoryId);
                }
            }
        }
        else
        {
            // Draw back button + buildings
            DrawBuildingsInGrid(x, y, width, height, btnSize, openCategory.Value, unlockedTech, playerId, mousePos);
        }

        // Build mode hint
        if (_gameplayStore.IsInBuildMode.CurrentValue)
        {
            Raylib.DrawText("[ESC]", x, y + height - 10, 8, ImmediateModeUI.TextDisabledColor);
        }
    }

    /// <summary>
    /// Draws unit command buttons (Attack Move, Patrol) when units are selected.
    /// </summary>
    private void DrawUnitCommandGrid(int x, int y, int width, int height, int btnSize, int playerId)
    {
        var mousePos = _inputStore.InputManager.Device.MousePosition;
        var commandMode = _gameplayStore.UnitCommandMode.CurrentValue;

        // Attack Move button (position 0)
        {
            int btnX = x;
            int btnY = y;

            var btnRect = new Rectangle(btnX, btnY, btnSize, btnSize);
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
            bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);
            bool isActive = commandMode == AppState.UnitCommandMode.AttackMove;

            Color bgColor;
            if (isActive)
                bgColor = new Color(180, 80, 80, 255); // Red when active
            else if (isHovered)
                bgColor = new Color(120, 80, 80, 255);
            else
                bgColor = new Color(90, 60, 60, 255);

            Raylib.DrawRectangle(btnX, btnY, btnSize, btnSize, bgColor);
            Raylib.DrawRectangleLines(btnX, btnY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);

            // Label
            int fontSize = 8;
            Raylib.DrawText("ATK", btnX + 4, btnY + 4, fontSize, ImmediateModeUI.TextColor);
            Raylib.DrawText("MOV", btnX + 4, btnY + 14, fontSize, ImmediateModeUI.TextColor);

            // Hotkey hint
            Raylib.DrawText("[A]", btnX + 4, btnY + btnSize - 10, 7, ImmediateModeUI.TextDisabledColor);

            if (isClicked)
            {
                if (isActive)
                    _gameplayStore.CancelUnitCommandMode();
                else
                    _gameplayStore.EnterAttackMoveMode();
            }
        }

        // Patrol button (position 1)
        {
            int btnX = x + btnSize + ButtonMargin;
            int btnY = y;

            var btnRect = new Rectangle(btnX, btnY, btnSize, btnSize);
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
            bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);
            bool isActive = commandMode == AppState.UnitCommandMode.Patrol;

            Color bgColor;
            if (isActive)
                bgColor = new Color(80, 120, 180, 255); // Blue when active
            else if (isHovered)
                bgColor = new Color(60, 90, 130, 255);
            else
                bgColor = new Color(50, 70, 100, 255);

            Raylib.DrawRectangle(btnX, btnY, btnSize, btnSize, bgColor);
            Raylib.DrawRectangleLines(btnX, btnY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);

            // Label
            int fontSize = 8;
            Raylib.DrawText("PAT", btnX + 4, btnY + 4, fontSize, ImmediateModeUI.TextColor);
            Raylib.DrawText("ROL", btnX + 4, btnY + 14, fontSize, ImmediateModeUI.TextColor);

            // Hotkey hint
            Raylib.DrawText("[P]", btnX + 4, btnY + btnSize - 10, 7, ImmediateModeUI.TextDisabledColor);

            if (isClicked)
            {
                if (isActive)
                    _gameplayStore.CancelUnitCommandMode();
                else
                    _gameplayStore.EnterPatrolMode();
            }
        }

        // Command mode hint
        if (commandMode != AppState.UnitCommandMode.None)
        {
            string hint = commandMode == AppState.UnitCommandMode.AttackMove ? "Click to attack-move" : "Click to set patrol point";
            Raylib.DrawText(hint, x, y + height - 10, 8, ImmediateModeUI.TextDisabledColor);
        }
    }

    private void DrawBuildingsInGrid(int x, int y, int width, int height, int btnSize, int categoryId, ulong unlockedTech, int playerId, System.Numerics.Vector2 mousePos)
    {
        // Back button first
        var backRect = new Rectangle(x, y, btnSize, btnSize);
        bool backHovered = Raylib.CheckCollisionPointRec(mousePos, backRect);
        bool backClicked = backHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

        Color backColor = backHovered ? new Color(120, 80, 80, 255) : new Color(90, 60, 60, 255);
        Raylib.DrawRectangle(x, y, btnSize, btnSize, backColor);
        Raylib.DrawRectangleLines(x, y, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);
        Raylib.DrawText("<", x + btnSize / 2 - 4, y + btnSize / 2 - 6, 12, ImmediateModeUI.TextColor);

        if (backClicked)
        {
            _gameplayStore.CloseCategory();
            return;
        }

        if (!_buildingsByCategory.TryGetValue(categoryId, out var buildingIds))
            return;

        var resources = GetPlayerResources(0);

        int buttonIndex = 1; // After back button
        for (int i = 0; i < buildingIds.Length; i++)
        {
            int buildingId = buildingIds[i];
            ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById(buildingId);
            var buildingType = (BuildingTypeId)buildingId;

            bool isUnlocked = typeData.RequiredTechId == 0 || ((unlockedTech >> typeData.RequiredTechId) & 1) == 1;
            if (!isUnlocked) continue;

            // Skip unique buildings that have already been built (active or under construction)
            if (typeData.IsUnique && HasBuiltUniqueBuilding(buildingType))
                continue;

            bool canAfford = resources.gold >= typeData.CostGold
                          && resources.wood >= typeData.CostWood
                          && resources.stone >= typeData.CostStone
                          && resources.iron >= typeData.CostIron
                          && resources.oil >= typeData.CostOil;

            int col = buttonIndex % 5;
            int row = buttonIndex / 5;
            int btnX = x + col * (btnSize + ButtonMargin);
            int btnY = y + row * (btnSize + ButtonMargin);
            buttonIndex++;

            if (btnY + btnSize > y + height) break;

            bool isSelected = _gameplayStore.BuildModeType.Value == buildingType;

            if (isSelected)
            {
                Raylib.DrawRectangle(btnX - 1, btnY - 1, btnSize + 2, btnSize + 2, ImmediateModeUI.ReadyColor);
            }

            var btnRect = new Rectangle(btnX, btnY, btnSize, btnSize);
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
            bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left) && canAfford;

            Color bgColor;
            if (!canAfford)
                bgColor = new Color(40, 40, 50, 255);
            else if (isHovered)
                bgColor = ImmediateModeUI.ButtonHoverColor;
            else
                bgColor = ImmediateModeUI.ButtonColor;

            Raylib.DrawRectangle(btnX, btnY, btnSize, btnSize, bgColor);
            Raylib.DrawRectangleLines(btnX, btnY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);

            string abbrev = GetAbbreviation(buildingType);
            int fontSize = 9;
            int textWidth = Raylib.MeasureText(abbrev, fontSize);
            Color textColor = canAfford ? ImmediateModeUI.TextColor : ImmediateModeUI.TextDisabledColor;
            Raylib.DrawText(abbrev, btnX + (btnSize - textWidth) / 2, btnY + 4, fontSize, textColor);

            // Cost
            string costText = $"{typeData.CostGold}";
            int costWidth = Raylib.MeasureText(costText, 8);
            Color costColor = canAfford ? new Color(255, 215, 0, 200) : new Color(200, 80, 80, 200);
            Raylib.DrawText(costText, btnX + (btnSize - costWidth) / 2, btnY + btnSize - 10, 8, costColor);

            if (isClicked)
            {
                if (isSelected)
                    _gameplayStore.CancelBuildMode();
                else
                    _gameplayStore.EnterBuildMode(buildingType);
            }
        }
    }

    /// <summary>
    /// Draws Repair and Destroy buttons in bottom-right corner of action grid.
    /// Position 13 (col 3, row 2) = Repair, Position 14 (col 4, row 2) = Destroy.
    /// </summary>
    private void DrawBuildingActionButtons(int x, int y, int btnSize, int buildingSlot, BuildingTypeId typeId, int health, int maxHealth, BuildingFlags flags, int playerId)
    {
        var buildings = _simWorld.BuildingRows;
        if (!buildings.TryGetRow(buildingSlot, out var row)) return;

        var mousePos = _inputStore.InputManager.Device.MousePosition;
        ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById((int)typeId);

        // Grid position calculations (5 cols, 3 rows)
        const int RepairCol = 3;
        const int RepairRow = 2;
        const int DestroyCol = 4;
        const int DestroyRow = 2;

        // === REPAIR BUTTON (Position 13: col 3, row 2) ===
        bool isDamaged = health < maxHealth;
        bool isRepairing = flags.HasFlag(BuildingFlags.IsRepairing);

        int repairBtnX = x + RepairCol * (btnSize + ButtonMargin);
        int repairBtnY = y + RepairRow * (btnSize + ButtonMargin);

        if (isDamaged || isRepairing)
        {
            // Calculate repair cost (proportional to damage)
            int missingHealth = maxHealth - health;
            int repairCostGold = maxHealth > 0 ? typeData.CostGold * missingHealth / maxHealth : 0;
            int repairCostWood = maxHealth > 0 ? typeData.CostWood * missingHealth / maxHealth : 0;
            int repairCostStone = maxHealth > 0 ? typeData.CostStone * missingHealth / maxHealth : 0;
            int repairCostIron = maxHealth > 0 ? typeData.CostIron * missingHealth / maxHealth : 0;
            int repairCostOil = maxHealth > 0 ? typeData.CostOil * missingHealth / maxHealth : 0;

            var resources = GetPlayerResources(0);
            bool canAfford = resources.gold >= repairCostGold &&
                            resources.wood >= repairCostWood &&
                            resources.stone >= repairCostStone &&
                            resources.iron >= repairCostIron &&
                            resources.oil >= repairCostOil;

            var repairRect = new Rectangle(repairBtnX, repairBtnY, btnSize, btnSize);
            bool repairHovered = Raylib.CheckCollisionPointRec(mousePos, repairRect);
            bool repairClicked = repairHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

            // Button color based on state
            Color repairColor;
            if (isRepairing)
                repairColor = new Color(80, 150, 80, 255); // Green when actively repairing
            else if (!canAfford)
                repairColor = new Color(50, 50, 60, 255); // Disabled gray
            else if (repairHovered)
                repairColor = new Color(80, 120, 80, 255); // Hover
            else
                repairColor = new Color(60, 100, 60, 255); // Available

            Raylib.DrawRectangle(repairBtnX, repairBtnY, btnSize, btnSize, repairColor);
            Raylib.DrawRectangleLines(repairBtnX, repairBtnY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);

            // Button label
            string repairLabel = isRepairing ? "..." : "REP";
            int fontSize = 9;
            int textWidth = Raylib.MeasureText(repairLabel, fontSize);
            Color textColor = (canAfford || isRepairing) ? ImmediateModeUI.TextColor : ImmediateModeUI.TextDisabledColor;
            Raylib.DrawText(repairLabel, repairBtnX + (btnSize - textWidth) / 2, repairBtnY + 4, fontSize, textColor);

            // Show cost if not repairing
            if (!isRepairing && repairCostGold > 0)
            {
                string costText = $"{repairCostGold}";
                int costWidth = Raylib.MeasureText(costText, 8);
                Color costColor = canAfford ? new Color(255, 215, 0, 200) : new Color(200, 80, 80, 200);
                Raylib.DrawText(costText, repairBtnX + (btnSize - costWidth) / 2, repairBtnY + btnSize - 10, 8, costColor);
            }

            // Handle click
            if (repairClicked)
            {
                var buildingHandle = buildings.GetHandle(buildingSlot);
                if (isRepairing)
                {
                    // Cancel repair
                    _gameplayStore.CancelRepair(buildingHandle);
                }
                else if (canAfford)
                {
                    // Start repair
                    _gameplayStore.RepairBuilding(buildingHandle);
                }
            }
        }

        // === DESTROY BUTTON (Position 14: col 4, row 2) ===
        // Command Center is not destroyable
        if (typeId == BuildingTypeId.CommandCenter) return;

        int destroyBtnX = x + DestroyCol * (btnSize + ButtonMargin);
        int destroyBtnY = y + DestroyRow * (btnSize + ButtonMargin);

        bool canDestroy = flags.HasFlag(BuildingFlags.IsActive) && !flags.IsDead();

        var destroyRect = new Rectangle(destroyBtnX, destroyBtnY, btnSize, btnSize);
        bool destroyHovered = Raylib.CheckCollisionPointRec(mousePos, destroyRect);
        bool destroyClicked = destroyHovered && Raylib.IsMouseButtonReleased(MouseButton.Left) && canDestroy;

        Color destroyColor = destroyHovered && canDestroy ? new Color(180, 80, 80, 255) : new Color(120, 60, 60, 255);
        if (!canDestroy) destroyColor = new Color(60, 40, 40, 255);

        Raylib.DrawRectangle(destroyBtnX, destroyBtnY, btnSize, btnSize, destroyColor);
        Raylib.DrawRectangleLines(destroyBtnX, destroyBtnY, btnSize, btnSize, ImmediateModeUI.PanelBorderColor);

        string destroyLabel = "DEL";
        int delFontSize = 9;
        int delTextWidth = Raylib.MeasureText(destroyLabel, delFontSize);
        Color delTextColor = canDestroy ? ImmediateModeUI.TextColor : ImmediateModeUI.TextDisabledColor;
        Raylib.DrawText(destroyLabel, destroyBtnX + (btnSize - delTextWidth) / 2, destroyBtnY + 4, delFontSize, delTextColor);

        // Show refund amount
        int refundGold = typeData.CostGold * 50 / 100;
        if (refundGold > 0)
        {
            string refundText = $"+{refundGold}";
            int refundWidth = Raylib.MeasureText(refundText, 8);
            Raylib.DrawText(refundText, destroyBtnX + (btnSize - refundWidth) / 2, destroyBtnY + btnSize - 10, 8, new Color(100, 255, 100, 200));
        }

        if (destroyClicked)
        {
            // Collect all selected destroyable buildings
            var selectedBuildings = GetSelectedDestroyableBuildings(playerId);
            if (selectedBuildings.Count == 0) return;

            // Calculate total refunds for all buildings
            int totalRefundGold = 0, totalRefundWood = 0, totalRefundStone = 0, totalRefundIron = 0, totalRefundOil = 0;
            var buildingHandles = new List<SimTable.SimHandle>();

            foreach (var (slot, bTypeId) in selectedBuildings)
            {
                var handle = buildings.GetHandle(slot);
                buildingHandles.Add(handle);

                ref readonly var bTypeData = ref _gameData.Db.BuildingTypeData.FindById((int)bTypeId);
                totalRefundGold += bTypeData.CostGold * 50 / 100;
                totalRefundWood += bTypeData.CostWood * 50 / 100;
                totalRefundStone += bTypeData.CostStone * 50 / 100;
                totalRefundIron += bTypeData.CostIron * 50 / 100;
                totalRefundOil += bTypeData.CostOil * 50 / 100;
            }

            _gameplayStore.RequestDestroyBuildings(buildingHandles, totalRefundGold, totalRefundWood, totalRefundStone, totalRefundIron, totalRefundOil);
        }
    }

    private void DrawSelectionInfo(int x, int y, int width, int height, int playerId)
    {
        // If in build mode, show building preview info
        var buildModeType = _gameplayStore.BuildModeType.Value;
        if (buildModeType.HasValue)
        {
            DrawBuildModeInfo(x, y, width, height, buildModeType.Value);
            return;
        }

        var selectedBuilding = GetSelectedBuilding(playerId);

        if (selectedBuilding.found)
        {
            // Always show building info on left side (garrison shown in action bar)
            DrawBuildingInfo(x, y, width, height, selectedBuilding);
        }
        else
        {
            int unitCount = GetSelectedUnitCount(playerId);
            if (unitCount > 0)
            {
                string text = $"{unitCount} unit{(unitCount > 1 ? "s" : "")}";
                Raylib.DrawText(text, x, y + 10, 16, ImmediateModeUI.TextColor);
                Raylib.DrawText("selected", x, y + 30, 14, ImmediateModeUI.TextDisabledColor);
            }
            else
            {
                Raylib.DrawText("No selection", x, y + height / 2 - 8, 14, ImmediateModeUI.TextDisabledColor);
            }
        }
    }

    private void DrawBuildModeInfo(int x, int y, int width, int height, BuildingTypeId buildingType)
    {
        ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById((int)buildingType);

        // Building name
        string buildingName = buildingType.ToString();
        Raylib.DrawText(buildingName, x, y, 16, ImmediateModeUI.TextColor);

        // Description (generated from stats)
        string description = GetBuildingDescription(typeData);
        int descY = y + 20;
        int descFontSize = 11;
        DrawWrappedText(description, x, descY, width - 10, descFontSize, ImmediateModeUI.TextDisabledColor);

        // Stats at bottom
        int statsY = y + height - 32;

        // HP and Size
        string statsText = $"HP:{typeData.Health} Size:{typeData.Width}x{typeData.Height}";
        Raylib.DrawText(statsText, x, statsY, 10, ImmediateModeUI.TextDisabledColor);

        // Cost line
        string costText = FormatBuildingCosts(typeData);
        Raylib.DrawText(costText, x, statsY + 12, 10, new Color(255, 215, 0, 200));
    }

    private static void DrawWrappedText(string text, int x, int y, int maxWidth, int fontSize, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;

        string[] words = text.Split(' ');
        string currentLine = "";
        int currentY = y;
        int lineHeight = fontSize + 2;

        foreach (string word in words)
        {
            string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            int lineWidth = Raylib.MeasureText(testLine, fontSize);

            if (lineWidth > maxWidth && !string.IsNullOrEmpty(currentLine))
            {
                Raylib.DrawText(currentLine, x, currentY, fontSize, color);
                currentY += lineHeight;
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
        }

        // Draw remaining text
        if (!string.IsNullOrEmpty(currentLine))
        {
            Raylib.DrawText(currentLine, x, currentY, fontSize, color);
        }
    }

    private static string GetBuildingDescription(in BuildingTypeData data)
    {
        var parts = new List<string>();

        // Power generation/consumption
        if (data.PowerConsumption < 0)
            parts.Add($"Generates {-data.PowerConsumption} power.");
        else if (data.PowerConsumption > 0 && data.RequiresPower)
            parts.Add($"Uses {data.PowerConsumption} power.");

        // Resource generation
        if (data.GeneratesGold > 0) parts.Add($"+{data.GeneratesGold} gold/s.");
        if (data.GeneratesWood > 0) parts.Add($"+{data.GeneratesWood} wood/s.");
        if (data.GeneratesStone > 0) parts.Add($"+{data.GeneratesStone} stone/s.");
        if (data.GeneratesIron > 0) parts.Add($"+{data.GeneratesIron} iron/s.");
        if (data.GeneratesOil > 0) parts.Add($"+{data.GeneratesOil} oil/s.");

        // Population
        if (data.ProvidesMaxPopulation > 0)
            parts.Add($"Houses {data.ProvidesMaxPopulation} pop.");

        // Combat
        if (data.Damage > 0)
            parts.Add($"{data.Damage} dmg, {data.Range.ToFloat():F0} range.");

        // Garrison
        if (data.GarrisonCapacity > 0)
            parts.Add($"Garrison: {data.GarrisonCapacity} units.");

        // Storage
        var storage = new List<string>();
        if (data.ProvidesMaxGold > 0) storage.Add($"{data.ProvidesMaxGold}g");
        if (data.ProvidesMaxWood > 0) storage.Add($"{data.ProvidesMaxWood}w");
        if (data.ProvidesMaxStone > 0) storage.Add($"{data.ProvidesMaxStone}s");
        if (data.ProvidesMaxIron > 0) storage.Add($"{data.ProvidesMaxIron}i");
        if (data.ProvidesMaxOil > 0) storage.Add($"{data.ProvidesMaxOil}o");
        if (storage.Count > 0) parts.Add($"Storage: {string.Join(" ", storage)}.");

        // Area bonuses (per-resource)
        var bonuses = new List<string>();
        if (data.AreaGoldBonus.ToFloat() > 0) bonuses.Add($"+{data.AreaGoldBonus.ToFloat() * 100:F0}% gold");
        if (data.AreaWoodBonus.ToFloat() > 0) bonuses.Add($"+{data.AreaWoodBonus.ToFloat() * 100:F0}% wood");
        if (data.AreaStoneBonus.ToFloat() > 0) bonuses.Add($"+{data.AreaStoneBonus.ToFloat() * 100:F0}% stone");
        if (data.AreaIronBonus.ToFloat() > 0) bonuses.Add($"+{data.AreaIronBonus.ToFloat() * 100:F0}% iron");
        if (data.AreaOilBonus.ToFloat() > 0) bonuses.Add($"+{data.AreaOilBonus.ToFloat() * 100:F0}% oil");
        if (bonuses.Count > 0) parts.Add($"Nearby: {string.Join(", ", bonuses)}.");

        return parts.Count > 0 ? string.Join(" ", parts) : "Basic structure.";
    }

    private static string FormatBuildingCosts(in BuildingTypeData data)
    {
        var costs = new List<string>();
        if (data.CostGold > 0) costs.Add($"{data.CostGold}g");
        if (data.CostWood > 0) costs.Add($"{data.CostWood}w");
        if (data.CostStone > 0) costs.Add($"{data.CostStone}s");
        if (data.CostIron > 0) costs.Add($"{data.CostIron}i");
        if (data.CostOil > 0) costs.Add($"{data.CostOil}o");
        return costs.Count > 0 ? string.Join(" ", costs) : "Free";
    }

    private void DrawBuildingInfo(int x, int y, int width, int height,
        (bool found, int buildingSlot, BuildingTypeId typeId, int health, int maxHealth, byte ownerPlayerId, ushort tileX, ushort tileY, byte garrisonCount, int garrisonCapacity, byte currentResearchId, int researchProgress, BuildingFlags flags) building)
    {
        // Building name
        string buildingName = building.typeId.ToString();
        Raylib.DrawText(buildingName, x, y, 16, ImmediateModeUI.TextColor);

        // Health bar
        DrawHealthBar(x, y + 22, width - 10, 14, building.health, building.maxHealth);

        // Owner
        string ownerText = $"Player {building.ownerPlayerId + 1}";
        Raylib.DrawText(ownerText, x, y + 42, 12, ImmediateModeUI.TextDisabledColor);

        // Position
        string posText = $"({building.tileX}, {building.tileY})";
        Raylib.DrawText(posText, x, y + 58, 12, ImmediateModeUI.TextDisabledColor);

        // Check if production building - show progress
        var buildings = _simWorld.BuildingRows;
        if (buildings.TryGetRow(building.buildingSlot, out var row))
        {
            bool isProduction = row.EffectiveGeneratesGold > 0 || row.EffectiveGeneratesWood > 0 ||
                               row.EffectiveGeneratesStone > 0 || row.EffectiveGeneratesIron > 0 ||
                               row.EffectiveGeneratesOil > 0;

            if (isProduction)
            {
                // Production progress bar
                int cycleDuration = row.ProductionCycleDuration > 0 ? row.ProductionCycleDuration : 300;
                float progress = (float)row.ResourceAccumulator / cycleDuration;
                float timeRemaining = (cycleDuration - row.ResourceAccumulator) / 60f;

                DrawProductionProgress(x, y + 74, width - 10, 10, progress);
                string prodText = $"Next batch: {timeRemaining:F1}s";
                Raylib.DrawText(prodText, x, y + 88, 10, ImmediateModeUI.TextDisabledColor);
            }
            else if (building.garrisonCapacity > 0)
            {
                // Garrison capacity hint (if building can garrison but is empty)
                string garrisonText = $"Garrison: 0/{building.garrisonCapacity}";
                Raylib.DrawText(garrisonText, x, y + 74, 12, ImmediateModeUI.TextDisabledColor);
            }
        }
    }

    private static void DrawProductionProgress(int x, int y, int width, int height, float progress)
    {
        // Background
        Raylib.DrawRectangle(x, y, width, height, new Color(40, 40, 50, 255));

        // Fill
        int fillWidth = (int)(width * progress);
        if (fillWidth > 0)
        {
            Raylib.DrawRectangle(x, y, fillWidth, height, new Color(100, 200, 255, 255));
        }

        // Border
        Raylib.DrawRectangleLines(x, y, width, height, ImmediateModeUI.PanelBorderColor);
    }

    private void DrawGarrisonActionBar(int x, int y, int width, int height, int buildingSlot, byte garrisonCount)
    {
        var buildings = _simWorld.BuildingRows;
        var units = _simWorld.CombatUnitRows;

        if (!buildings.TryGetRow(buildingSlot, out var building)) return;

        // Header
        string headerText = $"Garrisoned Units ({garrisonCount}/{building.GarrisonCapacity})";
        Raylib.DrawText(headerText, x, y, 14, ImmediateModeUI.TextColor);

        // Draw unit buttons - larger size for action bar
        int unitBtnSize = ButtonSize;  // Same size as build buttons
        int unitBtnMargin = ButtonMargin;
        int unitsPerRow = (width - Padding) / (unitBtnSize + unitBtnMargin);
        if (unitsPerRow < 1) unitsPerRow = 1;

        int startY = y + 22;

        for (int i = 0; i < garrisonCount && i < 6; i++)
        {
            var unitHandle = building.GetGarrisonSlot(i);
            if (!unitHandle.IsValid) continue;

            int unitSlot = units.GetSlot(unitHandle);
            if (!units.TryGetRow(unitSlot, out var unit)) continue;

            int col = i % unitsPerRow;
            int row = i / unitsPerRow;
            int btnX = x + col * (unitBtnSize + unitBtnMargin);
            int btnY = startY + row * (unitBtnSize + unitBtnMargin);

            // Skip if would overflow height (leave room for eject button)
            if (btnY + unitBtnSize > y + height - 28) break;

            // Unit button
            var btnRect = new Rectangle(btnX, btnY, unitBtnSize, unitBtnSize);
            var mousePos = _inputStore.InputManager.Device.MousePosition;
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);

            // Click to eject this unit
            if (isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left))
            {
                _gameplayStore.EjectSingleUnit(unitHandle);
            }

            // Unit color based on type
            Color btnColor = isHovered ? new Color(100, 150, 200, 255) : new Color(60, 100, 140, 255);
            Raylib.DrawRectangle(btnX, btnY, unitBtnSize, unitBtnSize, btnColor);
            Raylib.DrawRectangleLines(btnX, btnY, unitBtnSize, unitBtnSize, ImmediateModeUI.PanelBorderColor);

            // Unit type name
            string unitName = unit.TypeId.ToString();
            int fontSize = 12;
            int textWidth = Raylib.MeasureText(unitName, fontSize);
            Raylib.DrawText(unitName, btnX + (unitBtnSize - textWidth) / 2, btnY + 10, fontSize, ImmediateModeUI.TextColor);

            // Health bar under name
            int hpBarWidth = unitBtnSize - 8;
            int hpBarHeight = 6;
            int hpBarX = btnX + 4;
            int hpBarY = btnY + unitBtnSize - 12;
            float hpRatio = unit.MaxHealth > 0 ? (float)unit.Health / unit.MaxHealth : 0;
            Raylib.DrawRectangle(hpBarX, hpBarY, hpBarWidth, hpBarHeight, new Color(40, 20, 20, 255));
            Raylib.DrawRectangle(hpBarX, hpBarY, (int)(hpBarWidth * hpRatio), hpBarHeight, new Color(80, 180, 80, 255));

            // Tooltip on hover
            if (isHovered)
            {
                string tooltip = $"{unit.TypeId} - HP: {unit.Health}/{unit.MaxHealth} - DMG: {unit.Damage}";
                Raylib.DrawText(tooltip, x, y + height - 14, 11, ImmediateModeUI.TextColor);
            }
        }

        // Eject All button at bottom-right
        int ejectBtnWidth = 90;
        int ejectBtnHeight = 24;
        var ejectBtnRect = new Rectangle(x + width - ejectBtnWidth, y + height - ejectBtnHeight - 2, ejectBtnWidth, ejectBtnHeight);
        var ejectMousePos = _inputStore.InputManager.Device.MousePosition;
        bool ejectHovered = Raylib.CheckCollisionPointRec(ejectMousePos, ejectBtnRect);
        bool ejectClicked = ejectHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

        Color ejectColor = ejectHovered ? new Color(200, 120, 80, 255) : new Color(180, 100, 60, 255);
        Raylib.DrawRectangle((int)ejectBtnRect.X, (int)ejectBtnRect.Y, (int)ejectBtnRect.Width, (int)ejectBtnRect.Height, ejectColor);
        Raylib.DrawRectangleLines((int)ejectBtnRect.X, (int)ejectBtnRect.Y, (int)ejectBtnRect.Width, (int)ejectBtnRect.Height, ImmediateModeUI.PanelBorderColor);

        string ejectText = "Eject All [G]";
        int ejectFontSize = 11;
        int ejectWidth = Raylib.MeasureText(ejectText, ejectFontSize);
        Raylib.DrawText(ejectText, (int)ejectBtnRect.X + ((int)ejectBtnRect.Width - ejectWidth) / 2,
            (int)ejectBtnRect.Y + 6, ejectFontSize, ImmediateModeUI.TextColor);

        if (ejectClicked)
        {
            _gameplayStore.EjectGarrison();
        }
    }

    private void DrawGarrisonedUnitsUI(int x, int y, int width, int height, int buildingSlot, byte garrisonCount)
    {
        var buildings = _simWorld.BuildingRows;
        var units = _simWorld.CombatUnitRows;

        if (!buildings.TryGetRow(buildingSlot, out var building)) return;

        // Header
        string headerText = $"Garrisoned ({garrisonCount})";
        Raylib.DrawText(headerText, x, y, 14, ImmediateModeUI.TextColor);

        // Draw individual unit buttons
        int unitBtnSize = 24;
        int unitBtnMargin = 4;
        int unitsPerRow = (width - 10) / (unitBtnSize + unitBtnMargin);
        if (unitsPerRow < 1) unitsPerRow = 1;

        int startY = y + 20;

        for (int i = 0; i < garrisonCount && i < 6; i++)
        {
            var unitHandle = building.GetGarrisonSlot(i);
            if (!unitHandle.IsValid) continue;

            int unitSlot = units.GetSlot(unitHandle);
            if (!units.TryGetRow(unitSlot, out var unit)) continue;

            int col = i % unitsPerRow;
            int row = i / unitsPerRow;
            int btnX = x + col * (unitBtnSize + unitBtnMargin);
            int btnY = startY + row * (unitBtnSize + unitBtnMargin);

            // Unit button
            var btnRect = new Rectangle(btnX, btnY, unitBtnSize, unitBtnSize);
            var mousePos = _inputStore.InputManager.Device.MousePosition;
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);

            Color btnColor = isHovered ? new Color(100, 150, 200, 255) : new Color(60, 100, 140, 255);
            Raylib.DrawRectangle(btnX, btnY, unitBtnSize, unitBtnSize, btnColor);
            Raylib.DrawRectangleLines(btnX, btnY, unitBtnSize, unitBtnSize, ImmediateModeUI.PanelBorderColor);

            // Unit type abbreviation
            string unitAbbrev = GetUnitAbbreviation(unit.TypeId);
            int fontSize = 10;
            int textWidth = Raylib.MeasureText(unitAbbrev, fontSize);
            Raylib.DrawText(unitAbbrev, btnX + (unitBtnSize - textWidth) / 2, btnY + 7, fontSize, ImmediateModeUI.TextColor);

            // Tooltip on hover
            if (isHovered)
            {
                string tooltip = $"{unit.TypeId} - HP: {unit.Health}/{unit.MaxHealth}";
                Raylib.DrawText(tooltip, x, y + height - 16, 10, ImmediateModeUI.TextColor);
            }
        }

        // Eject All button at bottom
        var ejectBtnRect = new Rectangle(x, y + height - 26, 70, 22);
        var ejectMousePos = _inputStore.InputManager.Device.MousePosition;
        bool ejectHovered = Raylib.CheckCollisionPointRec(ejectMousePos, ejectBtnRect);
        bool ejectClicked = ejectHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

        Color ejectColor = ejectHovered ? new Color(200, 120, 80, 255) : new Color(180, 100, 60, 255);
        Raylib.DrawRectangle((int)ejectBtnRect.X, (int)ejectBtnRect.Y, (int)ejectBtnRect.Width, (int)ejectBtnRect.Height, ejectColor);
        Raylib.DrawRectangleLines((int)ejectBtnRect.X, (int)ejectBtnRect.Y, (int)ejectBtnRect.Width, (int)ejectBtnRect.Height, ImmediateModeUI.PanelBorderColor);

        string ejectText = "Eject All";
        int ejectFontSize = 10;
        int ejectWidth = Raylib.MeasureText(ejectText, ejectFontSize);
        Raylib.DrawText(ejectText, (int)ejectBtnRect.X + ((int)ejectBtnRect.Width - ejectWidth) / 2,
            (int)ejectBtnRect.Y + 6, ejectFontSize, ImmediateModeUI.TextColor);

        if (ejectClicked)
        {
            _gameplayStore.EjectGarrison();
        }
    }

    private string GetUnitAbbreviation(UnitTypeId typeId)
    {
        // Get abbreviation from data-driven UnitTypeData
        ref readonly var unitData = ref _gameData.Db.UnitTypeData.FindById((int)typeId);
        string abbrev = unitData.Abbreviation; // StringHandle -> string via implicit conversion
        return string.IsNullOrEmpty(abbrev) ? "???" : abbrev;
    }

    private void DrawBuildButtons(int x, int y, int width, int height, int playerId)
    {
        // Get player's unlocked tech bitfield
        ulong unlockedTech = GetPlayerUnlockedTech(playerId);

        // Calculate how many buttons fit per row
        int buttonsPerRow = (width - Padding) / (ButtonSize + ButtonMargin);
        if (buttonsPerRow < 1) buttonsPerRow = 1;

        int? openCategory = _gameplayStore.OpenCategoryId.Value;

        if (openCategory == null)
        {
            // Draw category folder buttons
            DrawCategoryButtons(x, y, width, height, buttonsPerRow);
        }
        else
        {
            // Draw back button + buildings in category
            DrawBuildingsInCategory(x, y, width, height, buttonsPerRow, openCategory.Value, unlockedTech, playerId);
        }

        // Build mode hint
        if (_gameplayStore.IsInBuildMode.CurrentValue)
        {
            Raylib.DrawText("[ESC] Cancel", x, y + height - 14, 11, ImmediateModeUI.TextDisabledColor);
        }
    }

    private void DrawCategoryButtons(int x, int y, int width, int height, int buttonsPerRow)
    {
        for (int i = 0; i < _sortedCategoryIds.Length; i++)
        {
            int categoryId = _sortedCategoryIds[i];
            ref readonly var catData = ref _gameData.Db.BuildingCategoryData.FindById(categoryId);

            int col = i % buttonsPerRow;
            int row = i / buttonsPerRow;
            int bx = x + col * (ButtonSize + ButtonMargin);
            int by = y + row * (ButtonSize + ButtonMargin);

            // Skip if would overflow height
            if (by + ButtonSize > y + height) break;

            // Button interaction
            var mousePos = _inputStore.InputManager.Device.MousePosition;
            var btnRect = new Rectangle(bx, by, ButtonSize, ButtonSize);
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
            bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

            // Folder-style button with different color
            Color bgColor = isHovered ? new Color(80, 100, 120, 255) : new Color(50, 70, 90, 255);
            Raylib.DrawRectangle(bx, by, ButtonSize, ButtonSize, bgColor);
            Raylib.DrawRectangleLines(bx, by, ButtonSize, ButtonSize, ImmediateModeUI.PanelBorderColor);

            // Category name (use lookup since GameDataId doesn't store the string)
            string name = categoryId < CategoryNames.Length ? CategoryNames[categoryId] : $"Category {categoryId}";

            // Category abbreviation (derived from name)
            string abbrev = GetCategoryAbbreviation(name);
            int fontSize = 14;
            int textWidth = Raylib.MeasureText(abbrev, fontSize);
            Raylib.DrawText(abbrev, bx + (ButtonSize - textWidth) / 2, by + 10, fontSize, ImmediateModeUI.TextColor);

            // Category name below
            int nameFontSize = 9;
            int nameWidth = Raylib.MeasureText(name, nameFontSize);
            Raylib.DrawText(name, bx + (ButtonSize - nameWidth) / 2, by + ButtonSize - 14, nameFontSize, ImmediateModeUI.TextDisabledColor);

            // Folder icon hint
            Raylib.DrawText(">", bx + ButtonSize - 12, by + 4, 12, ImmediateModeUI.TextDisabledColor);

            // Click handler - open category
            if (isClicked)
            {
                _gameplayStore.OpenCategory(categoryId);
            }
        }
    }

    private void DrawBuildingsInCategory(int x, int y, int width, int height, int buttonsPerRow, int categoryId, ulong unlockedTech, int playerId)
    {
        // Draw back button first
        int bx = x;
        int by = y;

        var mousePos = _inputStore.InputManager.Device.MousePosition;
        var backRect = new Rectangle(bx, by, ButtonSize, ButtonSize);
        bool backHovered = Raylib.CheckCollisionPointRec(mousePos, backRect);
        bool backClicked = backHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

        Color backColor = backHovered ? new Color(120, 80, 80, 255) : new Color(90, 60, 60, 255);
        Raylib.DrawRectangle(bx, by, ButtonSize, ButtonSize, backColor);
        Raylib.DrawRectangleLines(bx, by, ButtonSize, ButtonSize, ImmediateModeUI.PanelBorderColor);

        // Back arrow
        string backText = "<-";
        int backFontSize = 16;
        int backWidth = Raylib.MeasureText(backText, backFontSize);
        Raylib.DrawText(backText, bx + (ButtonSize - backWidth) / 2, by + 10, backFontSize, ImmediateModeUI.TextColor);
        Raylib.DrawText("Back", bx + (ButtonSize - Raylib.MeasureText("Back", 10)) / 2, by + ButtonSize - 14, 10, ImmediateModeUI.TextDisabledColor);

        if (backClicked)
        {
            _gameplayStore.CloseCategory();
            return;
        }

        // Get buildings in this category
        if (!_buildingsByCategory.TryGetValue(categoryId, out var buildingIds))
            return;

        // Get player resources for affordability check (shared base co-op uses slot 0)
        var resources = GetPlayerResources(0);

        // Draw building buttons (offset by 1 for back button)
        int buttonIndex = 1; // Start after back button
        for (int i = 0; i < buildingIds.Length; i++)
        {
            int buildingId = buildingIds[i];
            ref readonly var typeData = ref _gameData.Db.BuildingTypeData.FindById(buildingId);
            var buildingType = (BuildingTypeId)buildingId;

            // Check unlock status - skip locked buildings entirely (hide them)
            bool isUnlocked = typeData.RequiredTechId == 0 || ((unlockedTech >> typeData.RequiredTechId) & 1) == 1;
            if (!isUnlocked) continue;

            // Skip unique buildings that have already been built (active or under construction)
            if (typeData.IsUnique && HasBuiltUniqueBuilding(buildingType))
                continue;

            // Check affordability
            bool canAfford = resources.gold >= typeData.CostGold
                          && resources.wood >= typeData.CostWood
                          && resources.stone >= typeData.CostStone
                          && resources.iron >= typeData.CostIron
                          && resources.oil >= typeData.CostOil;

            // Position with offset for back button
            int col = buttonIndex % buttonsPerRow;
            int row = buttonIndex / buttonsPerRow;
            int btnX = x + col * (ButtonSize + ButtonMargin);
            int btnY = y + row * (ButtonSize + ButtonMargin);
            buttonIndex++;

            // Skip if would overflow height
            if (btnY + ButtonSize > y + height) break;

            bool isSelected = _gameplayStore.BuildModeType.Value == buildingType;

            // Selection highlight
            if (isSelected)
            {
                Raylib.DrawRectangle(btnX - 2, btnY - 2, ButtonSize + 4, ButtonSize + 4, ImmediateModeUI.ReadyColor);
            }

            // Button interaction - only clickable if affordable
            var btnRect = new Rectangle(btnX, btnY, ButtonSize, ButtonSize);
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
            bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left) && canAfford;

            // Button color - grayed out if unaffordable
            Color bgColor;
            if (!canAfford)
                bgColor = new Color(40, 40, 50, 255);
            else if (isHovered)
                bgColor = ImmediateModeUI.ButtonHoverColor;
            else
                bgColor = ImmediateModeUI.ButtonColor;

            Raylib.DrawRectangle(btnX, btnY, ButtonSize, ButtonSize, bgColor);
            Raylib.DrawRectangleLines(btnX, btnY, ButtonSize, ButtonSize, ImmediateModeUI.PanelBorderColor);

            // Abbreviation
            string abbrev = GetAbbreviation(buildingType);
            int fontSize = 14;
            int textWidth = Raylib.MeasureText(abbrev, fontSize);
            Color textColor = canAfford ? ImmediateModeUI.TextColor : ImmediateModeUI.TextDisabledColor;
            Raylib.DrawText(abbrev, btnX + (ButtonSize - textWidth) / 2, btnY + 8, fontSize, textColor);

            // Cost (show gold cost) - red if can't afford gold specifically
            string costText = $"{typeData.CostGold}g";
            int costFontSize = 10;
            int costWidth = Raylib.MeasureText(costText, costFontSize);
            Color costColor = canAfford ? new Color(255, 215, 0, 200) : new Color(200, 80, 80, 200);
            Raylib.DrawText(costText, btnX + (ButtonSize - costWidth) / 2, btnY + ButtonSize - 14, costFontSize, costColor);

            // Click handler
            if (isClicked)
            {
                if (isSelected)
                    _gameplayStore.CancelBuildMode();
                else
                    _gameplayStore.EnterBuildMode(buildingType);
            }
        }
    }

    private ulong GetPlayerUnlockedTech(int playerId)
    {
        // Shared base co-op: tech is global (playerId ignored)
        var resources = _simWorld.GameResourcesRows;
        if (resources.Count == 0) return 0;
        return resources.GetRowBySlot(0).UnlockedTech;
    }

    private void DrawHealthBar(int x, int y, int width, int height, int current, int max)
    {
        Raylib.DrawRectangle(x, y, width, height, new Color(40, 20, 20, 255));

        if (max > 0)
        {
            float ratio = (float)current / max;
            int fillWidth = (int)(width * ratio);
            Color healthColor = ratio > 0.5f ? new Color(80, 180, 80, 255) :
                               ratio > 0.25f ? new Color(180, 180, 80, 255) :
                               new Color(180, 80, 80, 255);
            Raylib.DrawRectangle(x, y, fillWidth, height, healthColor);
        }

        Raylib.DrawRectangleLines(x, y, width, height, ImmediateModeUI.PanelBorderColor);
    }

    private static string GetCategoryAbbreviation(string categoryName)
    {
        return categoryName switch
        {
            "Housing" => "HSG",
            "Economy" => "ECO",
            "Defense" => "DEF",
            "Infrastructure" => "INF",
            _ => categoryName.Length >= 3 ? categoryName[..3].ToUpperInvariant() : categoryName.ToUpperInvariant()
        };
    }

    private string GetAbbreviation(BuildingTypeId typeId)
    {
        // Get abbreviation from data-driven BuildingTypeData
        ref readonly var buildingData = ref _gameData.Db.BuildingTypeData.FindById((int)typeId);
        string abbrev = buildingData.Abbreviation; // StringHandle -> string via implicit conversion
        return string.IsNullOrEmpty(abbrev) ? "???" : abbrev;
    }

    /// <summary>
    /// Check if a building type can train any units.
    /// </summary>
    private bool CanBuildingTrainUnits(BuildingTypeId buildingType)
    {
        return _trainableUnitsByBuilding.ContainsKey((int)buildingType);
    }

    /// <summary>
    /// Draw the unit training bar for production buildings.
    /// Shows trainable units and production progress.
    /// </summary>
    private void DrawUnitTrainingBar(int x, int y, int width, int height, int buildingSlot, BuildingTypeId buildingType, int playerId)
    {
        var buildings = _simWorld.BuildingRows;
        if (!buildings.TryGetRow(buildingSlot, out var building)) return;

        // Get trainable units for this building type
        if (!_trainableUnitsByBuilding.TryGetValue((int)buildingType, out var trainableUnitIds))
            return;

        // Get resources and tech for affordability/unlock checks
        var resources = GetPlayerResources(0);
        ulong unlockedTech = GetPlayerUnlockedTech(playerId);

        // Header
        string headerText = $"Train Units - {buildingType}";
        Raylib.DrawText(headerText, x, y, 14, ImmediateModeUI.TextColor);

        // Get building handle for command
        var buildingHandle = buildings.GetHandle(buildingSlot);

        // Draw unit buttons
        int startY = y + 22;
        int buttonsPerRow = (width - Padding) / (ButtonSize + ButtonMargin);
        if (buttonsPerRow < 1) buttonsPerRow = 1;

        var mousePos = _inputStore.InputManager.Device.MousePosition;

        // Check if there's any empty slot in the queue
        var queue = building.ProductionQueueArray;
        bool hasEmptySlot = false;
        for (int s = 0; s < queue.Length; s++)
        {
            if (queue[s] == 255) { hasEmptySlot = true; break; }
        }

        for (int i = 0; i < trainableUnitIds.Length; i++)
        {
            int unitTypeId = trainableUnitIds[i];
            ref readonly var unitData = ref _gameData.Db.UnitTypeData.FindById(unitTypeId);

            // Check tech unlock
            bool isUnlocked = unitData.RequiredTechId == 0 ||
                              ((unlockedTech >> unitData.RequiredTechId) & 1) == 1;
            if (!isUnlocked) continue;

            // Check affordability
            bool canAfford = resources.gold >= unitData.CostGold &&
                            resources.wood >= unitData.CostWood &&
                            resources.stone >= unitData.CostStone &&
                            resources.iron >= unitData.CostIron &&
                            resources.oil >= unitData.CostOil;

            // Check population space
            bool hasPopSpace = resources.population + unitData.PopulationCost <= resources.maxPopulation;

            int col = i % buttonsPerRow;
            int row = i / buttonsPerRow;
            int btnX = x + col * (ButtonSize + ButtonMargin);
            int btnY = startY + row * (ButtonSize + ButtonMargin);

            // Skip if would overflow
            if (btnY + ButtonSize > y + height - 20) break;

            var btnRect = new Rectangle(btnX, btnY, ButtonSize, ButtonSize);
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, btnRect);
            bool isClickable = canAfford && hasPopSpace && hasEmptySlot;
            bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left) && isClickable;

            // Button color
            Color bgColor;
            if (!isClickable)
                bgColor = new Color(40, 40, 50, 255);
            else if (isHovered)
                bgColor = ImmediateModeUI.ButtonHoverColor;
            else
                bgColor = ImmediateModeUI.ButtonColor;

            Raylib.DrawRectangle(btnX, btnY, ButtonSize, ButtonSize, bgColor);
            Raylib.DrawRectangleLines(btnX, btnY, ButtonSize, ButtonSize, ImmediateModeUI.PanelBorderColor);

            // Unit abbreviation
            string abbrev = GetUnitAbbreviation((UnitTypeId)unitTypeId);
            int fontSize = 14;
            int textWidth = Raylib.MeasureText(abbrev, fontSize);
            Color textColor = isClickable ? ImmediateModeUI.TextColor : ImmediateModeUI.TextDisabledColor;
            Raylib.DrawText(abbrev, btnX + (ButtonSize - textWidth) / 2, btnY + 8, fontSize, textColor);

            // Cost display
            string costText = $"{unitData.CostGold}g";
            int costFontSize = 10;
            int costWidth = Raylib.MeasureText(costText, costFontSize);
            Color costColor = canAfford ? new Color(255, 215, 0, 200) : new Color(200, 80, 80, 200);
            Raylib.DrawText(costText, btnX + (ButtonSize - costWidth) / 2, btnY + ButtonSize - 14, costFontSize, costColor);

            // Pop cost indicator if > 1
            if (unitData.PopulationCost > 1)
            {
                string popText = $"+{unitData.PopulationCost}p";
                Raylib.DrawText(popText, btnX + 2, btnY + 24, 9, hasPopSpace ? new Color(100, 200, 255, 200) : new Color(200, 80, 80, 200));
            }

            // Click handler - queue training
            if (isClicked)
            {
                _gameplayStore.QueueTrainUnit(buildingHandle, (byte)unitTypeId);
            }

            // Tooltip on hover
            if (isHovered)
            {
                string tooltip = $"{(UnitTypeId)unitTypeId} - {unitData.CostGold}g";
                if (unitData.CostWood > 0) tooltip += $" {unitData.CostWood}w";
                if (unitData.CostIron > 0) tooltip += $" {unitData.CostIron}i";
                tooltip += $" - {unitData.BuildTime / 60f:F1}s";
                if (!hasPopSpace) tooltip += " [NO POP SPACE]";
                else if (!hasEmptySlot) tooltip += " [QUEUE FULL]";
                else if (!canAfford) tooltip += " [CAN'T AFFORD]";
                Raylib.DrawText(tooltip, x, y + height - 14, 11, ImmediateModeUI.TextColor);
            }
        }

        // Show current production progress if producing
        if (queue[0] != 255)
        {
            int prodY = y + height - 34;
            float progress = building.ProductionBuildTime > 0
                ? (float)building.ProductionProgress / building.ProductionBuildTime
                : 0;
            float timeRemaining = building.ProductionBuildTime > 0
                ? (building.ProductionBuildTime - building.ProductionProgress) / 60f
                : 0;

            // Count queued items
            int queueCount = 0;
            for (int s = 0; s < queue.Length; s++)
            {
                if (queue[s] != 255) queueCount++;
            }

            // Progress bar
            DrawProductionProgress(x, prodY, width - 10, 10, progress);

            // Training text with queue count
            string unitName = ((UnitTypeId)queue[0]).ToString();
            string prodText = queueCount > 1
                ? $"Training: {unitName} - {timeRemaining:F1}s (+{queueCount - 1} queued)"
                : $"Training: {unitName} - {timeRemaining:F1}s";
            Raylib.DrawText(prodText, x, prodY + 12, 10, ImmediateModeUI.TextColor);
        }
    }

    private (bool found, int buildingSlot, BuildingTypeId typeId, int health, int maxHealth, byte ownerPlayerId, ushort tileX, ushort tileY, byte garrisonCount, int garrisonCapacity, byte currentResearchId, int researchProgress, BuildingFlags flags) GetSelectedBuilding(int playerId)
    {
        var buildings = _simWorld.BuildingRows;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var row)) continue;
            // Include both active and under-construction buildings
            if (!row.Flags.HasFlag(BuildingFlags.IsActive) &&
                !row.Flags.HasFlag(BuildingFlags.IsUnderConstruction)) continue;
            if (row.SelectedByPlayerId == playerId)
            {
                return (true, slot, row.TypeId, row.Health, row.MaxHealth, row.OwnerPlayerId, row.TileX, row.TileY, row.GarrisonCount, row.GarrisonCapacity, row.CurrentResearchId, row.ResearchProgress, row.Flags);
            }
        }
        return (false, -1, default, 0, 0, 0, 0, 0, 0, 0, 0, 0, default);
    }

    /// <summary>
    /// Gets all selected buildings for the player (for multi-delete).
    /// Excludes Command Center which is not destroyable.
    /// </summary>
    private List<(int slot, BuildingTypeId typeId)> GetSelectedDestroyableBuildings(int playerId)
    {
        var result = new List<(int slot, BuildingTypeId typeId)>();
        var buildings = _simWorld.BuildingRows;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if (row.Flags.IsDead()) continue;
            if (row.SelectedByPlayerId != playerId) continue;
            // Command Center is not destroyable
            if (row.TypeId == BuildingTypeId.CommandCenter) continue;
            result.Add((slot, row.TypeId));
        }
        return result;
    }

    private bool IsWorkshopBuilding(BuildingTypeId typeId)
    {
        return _workshopBuildingIds.Contains((int)typeId);
    }

    private bool HasBuiltUniqueBuilding(BuildingTypeId typeId)
    {
        // Shared base co-op: check if ANY player has built this unique building
        var buildings = _simWorld.BuildingRows;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (building.Flags.IsDead()) continue;
            if (building.TypeId != typeId) continue;
            // Check active OR under construction
            if (building.Flags.HasFlag(BuildingFlags.IsActive) ||
                building.Flags.HasFlag(BuildingFlags.IsUnderConstruction))
                return true;
        }
        return false;
    }

    private (int gold, int maxGold, int wood, int maxWood, int stone, int maxStone, int iron, int maxIron, int oil, int maxOil, int energy, int maxEnergy, int population, int maxPopulation, int netGoldRate, int netWoodRate, int netStoneRate, int netIronRate, int netOilRate) GetPlayerResources(int playerId)
    {
        // Shared base co-op: all players use global resources (playerId ignored)
        var resources = _simWorld.GameResourcesRows;
        if (resources.Count == 0)
        {
            return (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var row = resources.GetRowBySlot(0);
        return (row.Gold, row.MaxGold, row.Wood, row.MaxWood, row.Stone, row.MaxStone, row.Iron, row.MaxIron, row.Oil, row.MaxOil, row.Energy, row.MaxEnergy, row.Population, row.MaxPopulation, row.NetGoldRate, row.NetWoodRate, row.NetStoneRate, row.NetIronRate, row.NetOilRate);
    }

    private int GetSelectedUnitCount(int playerId)
    {
        int count = 0;
        var units = _simWorld.CombatUnitRows;
        for (int slot = 0; slot < units.Count; slot++)
        {
            if (!units.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(MortalFlags.IsActive)) continue;
            if (row.SelectedByPlayerId == playerId)
            {
                count++;
            }
        }
        return count;
    }

    private bool IsCommandCenterSelected(int playerId)
    {
        var buildings = _simWorld.BuildingRows;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if (row.TypeId != BuildingTypeId.CommandCenter) continue;
            if (row.SelectedByPlayerId == playerId) return true;
        }
        return false;
    }

    /// <summary>
    /// Draws the destroy confirmation modal overlay if active.
    /// </summary>
    private void DrawDestroyConfirmationModal()
    {
        var confirmation = _gameplayStore.DestroyConfirmation.Value;
        if (!confirmation.HasValue) return;

        var (buildings, refundGold, refundWood, refundStone, refundIron, refundOil, buildingCount) = confirmation.Value;

        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Semi-transparent overlay
        Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(0, 0, 0, 150));

        // Modal box dimensions
        const int ModalWidth = 280;
        const int ModalHeight = 160;
        int modalX = (screenWidth - ModalWidth) / 2;
        int modalY = (screenHeight - ModalHeight) / 2;

        // Modal background
        Raylib.DrawRectangle(modalX, modalY, ModalWidth, ModalHeight, ImmediateModeUI.PanelColor);
        Raylib.DrawRectangleLines(modalX, modalY, ModalWidth, ModalHeight, ImmediateModeUI.PanelBorderColor);
        Raylib.DrawRectangleLines(modalX + 1, modalY + 1, ModalWidth - 2, ModalHeight - 2, new Color(80, 80, 100, 255));

        // Title
        string title = buildingCount == 1 ? "Destroy Building?" : $"Destroy {buildingCount} Buildings?";
        int titleFontSize = 18;
        int titleWidth = Raylib.MeasureText(title, titleFontSize);
        Raylib.DrawText(title, modalX + (ModalWidth - titleWidth) / 2, modalY + 15, titleFontSize, ImmediateModeUI.TextColor);

        // Building count subtitle
        string subtitle = buildingCount == 1 ? "This action cannot be undone." : $"{buildingCount} buildings will be destroyed.";
        int nameFontSize = 14;
        int nameWidth = Raylib.MeasureText(subtitle, nameFontSize);
        Raylib.DrawText(subtitle, modalX + (ModalWidth - nameWidth) / 2, modalY + 42, nameFontSize, ImmediateModeUI.TextDisabledColor);

        // Refund info - show non-zero resources
        var refunds = new System.Collections.Generic.List<string>();
        if (refundGold > 0) refunds.Add($"{refundGold}g");
        if (refundWood > 0) refunds.Add($"{refundWood}w");
        if (refundStone > 0) refunds.Add($"{refundStone}s");
        if (refundIron > 0) refunds.Add($"{refundIron}i");
        if (refundOil > 0) refunds.Add($"{refundOil}o");

        string refundText = refunds.Count > 0 ? "Refund: " + string.Join(" ", refunds) : "No refund";
        int refundFontSize = 12;
        int refundWidth = Raylib.MeasureText(refundText, refundFontSize);
        Raylib.DrawText(refundText, modalX + (ModalWidth - refundWidth) / 2, modalY + 65, refundFontSize, new Color(100, 255, 100, 255));

        var mousePos = _inputStore.InputManager.Device.MousePosition;

        // Button dimensions
        const int BtnWidth = 90;
        const int BtnHeight = 28;
        int btnY = modalY + ModalHeight - 45;

        // Confirm button (left, red-ish)
        int confirmX = modalX + 35;
        var confirmRect = new Rectangle(confirmX, btnY, BtnWidth, BtnHeight);
        bool confirmHovered = Raylib.CheckCollisionPointRec(mousePos, confirmRect);
        bool confirmClicked = confirmHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

        Color confirmColor = confirmHovered ? new Color(180, 80, 80, 255) : new Color(140, 60, 60, 255);
        Raylib.DrawRectangle(confirmX, btnY, BtnWidth, BtnHeight, confirmColor);
        Raylib.DrawRectangleLines(confirmX, btnY, BtnWidth, BtnHeight, ImmediateModeUI.PanelBorderColor);

        string confirmText = "Confirm";
        int confirmTextWidth = Raylib.MeasureText(confirmText, 12);
        Raylib.DrawText(confirmText, confirmX + (BtnWidth - confirmTextWidth) / 2, btnY + 8, 12, ImmediateModeUI.TextColor);

        // Cancel button (right, gray)
        int cancelX = modalX + ModalWidth - BtnWidth - 35;
        var cancelRect = new Rectangle(cancelX, btnY, BtnWidth, BtnHeight);
        bool cancelHovered = Raylib.CheckCollisionPointRec(mousePos, cancelRect);
        bool cancelClicked = cancelHovered && Raylib.IsMouseButtonReleased(MouseButton.Left);

        Color cancelColor = cancelHovered ? ImmediateModeUI.ButtonHoverColor : ImmediateModeUI.ButtonColor;
        Raylib.DrawRectangle(cancelX, btnY, BtnWidth, BtnHeight, cancelColor);
        Raylib.DrawRectangleLines(cancelX, btnY, BtnWidth, BtnHeight, ImmediateModeUI.PanelBorderColor);

        string cancelText = "Cancel";
        int cancelTextWidth = Raylib.MeasureText(cancelText, 12);
        Raylib.DrawText(cancelText, cancelX + (BtnWidth - cancelTextWidth) / 2, btnY + 8, 12, ImmediateModeUI.TextColor);

        // Handle clicks
        if (confirmClicked)
        {
            _gameplayStore.ConfirmDestroy();
        }
        else if (cancelClicked || Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            _gameplayStore.CancelDestroy();
        }
    }
}
