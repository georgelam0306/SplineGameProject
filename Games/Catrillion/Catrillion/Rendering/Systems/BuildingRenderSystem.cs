using System;
using System.Collections.Generic;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Rendering.Services;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders buildings directly from SimWorld.
/// Buildings use tile coordinates, converted to world space for rendering.
/// </summary>
public sealed class BuildingRenderSystem : BaseSystem
{
    private readonly SimWorld _simWorld;
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly Dictionary<BuildingTypeId, Texture2D> _textures = new();
    private readonly int _tileSize;

    public BuildingRenderSystem(SimWorld simWorld, GameDataManager<GameDocDb> gameData)
    {
        _simWorld = simWorld;
        _gameData = gameData;

        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _tileSize = mapConfig.TileSize;
    }

    protected override void OnUpdateGroup()
    {
        var buildings = _simWorld.BuildingRows;
        var fogTable = _simWorld.FogOfWarGridStateRows;
        bool hasFogOfWar = fogTable.Count > 0;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            bool isUnderConstruction = building.Flags.HasFlag(BuildingFlags.IsUnderConstruction);
            if (!building.Flags.HasFlag(BuildingFlags.IsActive) && !isUnderConstruction) continue;

            // Fog of war visibility check: show if any tile is explored (visible or fogged)
            if (hasFogOfWar && !IsBuildingExplored(fogTable, building.TileX, building.TileY, building.Width, building.Height))
            {
                continue; // Building is in unexplored area - don't render
            }

            // Convert tile to world position (top-left corner)
            float worldX = building.TileX * _tileSize;
            float worldY = building.TileY * _tileSize;
            float width = building.Width * _tileSize;
            float height = building.Height * _tileSize;

            // Get or load texture for this building type
            var texture = GetTexture(building.TypeId);

            // Use gray tint for buildings under construction
            var tintColor = isUnderConstruction ? new Color(128, 128, 128, 200) : Color.White;

            if (texture.Id != 0)
            {
                // Draw sprite
                var sourceRect = new System.Numerics.Vector4(0, 0, texture.Width, texture.Height);
                var destRect = new Rectangle(worldX, worldY, width, height);
                Raylib.DrawTexturePro(
                    texture,
                    new Rectangle(0, 0, texture.Width, texture.Height),
                    destRect,
                    System.Numerics.Vector2.Zero,
                    0f,
                    tintColor
                );
            }
            else
            {
                // Fallback: colored rectangle based on building type
                var color = GetBuildingColor(building.TypeId);
                if (isUnderConstruction)
                {
                    // Darken color for construction
                    color = new Color((byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2), (byte)200);
                }
                Raylib.DrawRectangle((int)worldX, (int)worldY, (int)width, (int)height, color);

                // Draw building name as text
                string name = building.TypeId.ToString();
                if (name.Length > 3) name = name[..3];
                int fontSize = 12;
                int textWidth = Raylib.MeasureText(name, fontSize);
                int textX = (int)(worldX + width / 2 - textWidth / 2);
                int textY = (int)(worldY + height / 2 - fontSize / 2);
                Raylib.DrawText(name, textX, textY, fontSize, Color.White);
            }

            // Draw construction progress overlay
            if (isUnderConstruction)
            {
                int progress = building.ConstructionBuildTime > 0
                    ? (int)(100f * building.ConstructionProgress / building.ConstructionBuildTime)
                    : 0;
                string progressText = $"{progress}%";
                int fontSize = 14;
                int textWidth = Raylib.MeasureText(progressText, fontSize);
                int textX = (int)(worldX + width / 2 - textWidth / 2);
                int textY = (int)(worldY + height / 2 - fontSize / 2);

                // Draw background for text readability
                Raylib.DrawRectangle(textX - 2, textY - 1, textWidth + 4, fontSize + 2, new Color(0, 0, 0, 180));
                Raylib.DrawText(progressText, textX, textY, fontSize, new Color(255, 200, 100, 255));
            }

            // Selection highlight (player-colored for identification)
            if (building.SelectedByPlayerId >= 0)
            {
                var selectionColor = PlayerColorService.GetPlayerColor(building.SelectedByPlayerId);
                Raylib.DrawRectangleLines((int)worldX, (int)worldY, (int)width, (int)height, selectionColor);
                // Double line for emphasis
                Raylib.DrawRectangleLines((int)worldX - 1, (int)worldY - 1, (int)width + 2, (int)height + 2, selectionColor);
            }
        }
    }

    private Texture2D GetTexture(BuildingTypeId typeId)
    {
        if (_textures.TryGetValue(typeId, out var texture))
        {
            return texture;
        }

        // Get sprite filename from data-driven BuildingTypeData
        ref readonly var buildingData = ref _gameData.Db.BuildingTypeData.FindById((int)typeId);
        string spriteFile = buildingData.SpriteFile; // StringHandle -> string via implicit conversion

        if (!string.IsNullOrEmpty(spriteFile))
        {
            string path = $"Assets/Buildings/{spriteFile}";
            texture = Raylib.LoadTexture(path);
            if (texture.Id != 0)
            {
                Raylib.SetTextureFilter(texture, TextureFilter.Point);
            }
        }

        _textures[typeId] = texture;
        return texture;
    }

    private Color GetBuildingColor(BuildingTypeId typeId)
    {
        ref readonly var buildingData = ref _gameData.Db.BuildingTypeData.FindById((int)typeId);
        var color = buildingData.Color;
        return new Color(color.R, color.G, color.B, color.A);
    }

    /// <summary>
    /// Checks if any tile of the building footprint has been explored (visible or fogged).
    /// Buildings remain visible once discovered.
    /// </summary>
    private static bool IsBuildingExplored(FogOfWarGridStateRowTable fogTable, int tileX, int tileY, int width, int height)
    {
        for (int ty = tileY; ty < tileY + height; ty++)
        {
            for (int tx = tileX; tx < tileX + width; tx++)
            {
                if (FogOfWarService.IsTileExplored(fogTable, tx, ty))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
