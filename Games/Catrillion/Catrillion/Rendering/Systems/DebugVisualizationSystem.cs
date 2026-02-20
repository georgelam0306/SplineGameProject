using System;
using System.Numerics;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Camera;
using Catrillion.Config;
using Catrillion.Rendering.Services;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using Catrillion.Rendering.DualGrid.Utilities;
using Core;
using FlowField;

namespace Catrillion.Rendering.Systems;

public sealed class DebugVisualizationSystem : BaseSystem
{
    private readonly ZoneFlowService _zoneFlowService;
    private readonly CameraManager _cameraManager;
    private readonly SeparationSystem _separationSystem;
    private readonly ChunkRegistry _chunkRegistry;
    private readonly SimWorld _simWorld;
    private readonly TerrainDataService _terrainData;

    private bool _enabled;
    private DebugMode _mode;

    // UI state for screen-space rendering (read by DebugVisualizationUISystem)
    public bool IsEnabled => _enabled;
    public string CurrentModeName => _mode.ToString();
    public int ZoneCount { get; private set; }
    public int PortalCount { get; private set; }
    public int NoiseGridCellCount { get; private set; }
    public int FlowArrowCount { get; private set; }
    public int SeedCount => _zoneFlowService.SeedCount;
    public int ThreatGridCellCount { get; private set; }
    public int ThreatGridMaxValue { get; private set; }
    public int PeakThreatCellCount { get; private set; }

    private const float ArrowLength = 12f;
    private const float ArrowHeadSize = 4f;
    private const int DensityThreshold = 5;
    private static readonly int TileSize = WorldTile.TileSize;
    private const int SectorSize = 32; // Tiles per sector (chunk)

    // Debug destination tracking
    private int _debugDestTileX;
    private int _debugDestTileY;
    private bool _hasDebugDest;
    private int _currentUnitIndex;

    // A* path tracking
    public int ZonePathLength { get; private set; }
    public string ZonePathInfo { get; private set; } = "";

    // Debug destination info
    public bool HasDebugDestination => _hasDebugDest;
    public int DebugDestTileX => _debugDestTileX;
    public int DebugDestTileY => _debugDestTileY;

    private enum DebugMode
    {
        Combined,       // Zones + Portals + Flow + Chunks together
        FlowField,      // Flow field for specific destination + A* path
        NoiseGrid,
        DensityGrid,
        ThreatGrid,     // Threat levels for zombie AI
        WorldTiles      // Original world tile grid visualization
    }

    public DebugVisualizationSystem(
        ZoneFlowService zoneFlowService,
        CameraManager cameraManager,
        SeparationSystem separationSystem,
        ChunkRegistry chunkRegistry,
        SimWorld simWorld,
        TerrainDataService terrainData)
    {
        _zoneFlowService = zoneFlowService;
        _cameraManager = cameraManager;
        _separationSystem = separationSystem;
        _chunkRegistry = chunkRegistry;
        _simWorld = simWorld;
        _terrainData = terrainData;
    }

    protected override void OnUpdateGroup()
    {
        // Reset stats
        FlowArrowCount = 0;
        NoiseGridCellCount = 0;
        ZoneCount = 0;
        PortalCount = 0;
        ThreatGridCellCount = 0;
        ThreatGridMaxValue = 0;
        PeakThreatCellCount = 0;

        if (Raylib.IsKeyPressed(KeyboardKey.F3))
        {
            if (!_enabled)
            {
                _enabled = true;
                _mode = DebugMode.Combined;
                Console.WriteLine($"[Debug] Enabled debug mode: {_mode}");
            }
            else
            {
                _mode = _mode switch
                {
                    DebugMode.Combined => DebugMode.FlowField,
                    DebugMode.FlowField => DebugMode.NoiseGrid,
                    DebugMode.NoiseGrid => DebugMode.DensityGrid,
                    DebugMode.DensityGrid => DebugMode.ThreatGrid,
                    DebugMode.ThreatGrid => DebugMode.WorldTiles,
                    DebugMode.WorldTiles => DebugMode.Combined,
                    _ => DebugMode.Combined
                };
                // After WorldTiles, wrap around and disable
                if (_mode == DebugMode.Combined)
                {
                    _enabled = false;
                    Console.WriteLine("[Debug] Disabled debug mode");
                }
                else
                {
                    Console.WriteLine($"[Debug] Switched to mode: {_mode}");
                }
            }
        }

        // Handle destination selection (works in any mode when debug is enabled)
        if (_enabled)
        {
            // Middle-click OR Shift+Right-click to set destination manually
            bool middleClick = Raylib.IsMouseButtonPressed(MouseButton.Middle);
            bool shiftRightClick = Raylib.IsKeyDown(KeyboardKey.LeftShift) &&
                                   Raylib.IsMouseButtonPressed(MouseButton.Right);

            if (middleClick || shiftRightClick)
            {
                var mousePos = Raylib.GetMousePosition();
                var worldPos = _cameraManager.ScreenToWorld(mousePos);
                _debugDestTileX = (int)(worldPos.X / TileSize);
                _debugDestTileY = (int)(worldPos.Y / TileSize);
                _hasDebugDest = true;
                Console.WriteLine($"[Debug] Set destination tile: ({_debugDestTileX}, {_debugDestTileY}) world: ({worldPos.X:F0}, {worldPos.Y:F0})");
            }
        }

        // In FlowField mode, also handle Tab to cycle through units
        if (_enabled && _mode == DebugMode.FlowField)
        {
            UpdateDebugDestinationFromUnits();
        }

        if (!_enabled)
        {
            return;
        }

        // Draw a test marker at fixed world position to verify rendering is working
        Raylib.DrawCircle(100, 100, 20, Color.Magenta);
        Raylib.DrawText($"DEBUG: {_mode}", 100, 130, 20, Color.Magenta);

        switch (_mode)
        {
            case DebugMode.Combined:
                RenderCombined();
                break;
            case DebugMode.FlowField:
                RenderFlowFieldDebug();
                break;
            case DebugMode.NoiseGrid:
                RenderNoiseGrid();
                break;
            case DebugMode.DensityGrid:
                RenderDensityGrid();
                break;
            case DebugMode.ThreatGrid:
                RenderThreatGrid();
                break;
            case DebugMode.WorldTiles:
                RenderWorldTiles();
                break;
        }

        // Always render zombie states when debug is enabled
        RenderZombieStates();

        // Always render combat unit debug info when debug is enabled
        RenderCombatUnitDebugInfo();

        // Render all targetable unit positions
        RenderTargetablePositions();
    }

    private void RenderTargetablePositions()
    {
        // Render building positions
        var buildings = _simWorld.BuildingRows;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            var pos = building.Position.ToVector2();
            int px = (int)building.Position.X.ToInt();
            int py = (int)building.Position.Y.ToInt();

            // Draw crosshair at building position
            Raylib.DrawLine((int)pos.X - 10, (int)pos.Y, (int)pos.X + 10, (int)pos.Y, Color.Red);
            Raylib.DrawLine((int)pos.X, (int)pos.Y - 10, (int)pos.X, (int)pos.Y + 10, Color.Red);

            // Show position coordinates
            Raylib.DrawText($"B:{px},{py}", (int)pos.X + 12, (int)pos.Y - 5, 8, Color.Red);
        }

        // Render combat unit positions
        var units = _simWorld.CombatUnitRows;
        for (int slot = 0; slot < units.Count; slot++)
        {
            var unit = units.GetRowBySlot(slot);
            if (unit.Flags.IsDead()) continue;

            var pos = unit.Position.ToVector2();
            int px = (int)unit.Position.X.ToInt();
            int py = (int)unit.Position.Y.ToInt();

            // Draw crosshair at unit position
            Raylib.DrawLine((int)pos.X - 8, (int)pos.Y, (int)pos.X + 8, (int)pos.Y, Color.SkyBlue);
            Raylib.DrawLine((int)pos.X, (int)pos.Y - 8, (int)pos.X, (int)pos.Y + 8, Color.SkyBlue);

            // Show position coordinates
            Raylib.DrawText($"U:{px},{py}", (int)pos.X + 10, (int)pos.Y - 5, 8, Color.SkyBlue);
        }
    }

    private void RenderFlowFieldArrows(int minTileX, int maxTileX, int minTileY, int maxTileY)
    {
        float halfTile = TileSize * 0.5f;
        int arrowCount = 0;

        for (int tileY = minTileY; tileY < maxTileY; tileY++)
        {
            for (int tileX = minTileX; tileX < maxTileX; tileX++)
            {
                // Convert tile center to world coordinates for flow query
                var worldPos = new Fixed64Vec2(
                    Fixed64.FromInt(tileX * TileSize) + Fixed64.FromInt(TileSize / 2),
                    Fixed64.FromInt(tileY * TileSize) + Fixed64.FromInt(TileSize / 2));

                var flow = _zoneFlowService.GetFlowDirection(worldPos);
                float flowXf = flow.X.ToFloat();
                float flowYf = flow.Y.ToFloat();

                if (flowXf == 0f && flowYf == 0f)
                {
                    continue;
                }

                arrowCount++;

                float worldX = tileX * TileSize + halfTile;
                float worldY = tileY * TileSize + halfTile;

                float endX = worldX + flowXf * ArrowLength;
                float endY = worldY + flowYf * ArrowLength;

                Raylib.DrawLine((int)worldX, (int)worldY, (int)endX, (int)endY, Color.Lime);

                float perpX = -flowYf;
                float perpY = flowXf;
                float headBaseX = endX - flowXf * ArrowHeadSize;
                float headBaseY = endY - flowYf * ArrowHeadSize;

                float leftX = headBaseX + perpX * ArrowHeadSize * 0.5f;
                float leftY = headBaseY + perpY * ArrowHeadSize * 0.5f;
                float rightX = headBaseX - perpX * ArrowHeadSize * 0.5f;
                float rightY = headBaseY - perpY * ArrowHeadSize * 0.5f;

                Raylib.DrawLine((int)endX, (int)endY, (int)leftX, (int)leftY, Color.Lime);
                Raylib.DrawLine((int)endX, (int)endY, (int)rightX, (int)rightY, Color.Lime);
            }
        }

        FlowArrowCount = arrowCount;
    }

    private void RenderCombined()
    {
        var zoneGraph = _zoneFlowService.GetZoneGraph();

        float cameraX = _cameraManager.TargetX;
        float cameraY = _cameraManager.TargetY;
        int gameWidth = _cameraManager.GameWidth;
        int gameHeight = _cameraManager.GameHeight;

        // For pixel-perfect rendering, zoom is always 1.0
        float halfViewWidth = gameWidth * 0.5f;
        float halfViewHeight = gameHeight * 0.5f;

        int minTileX = Math.Max(0, (int)((cameraX - halfViewWidth) / TileSize) - 1);
        int maxTileX = (int)((cameraX + halfViewWidth) / TileSize) + 2;
        int minTileY = Math.Max(0, (int)((cameraY - halfViewHeight) / TileSize) - 1);
        int maxTileY = (int)((cameraY + halfViewHeight) / TileSize) + 2;

        int halfTile = TileSize / 2;

        // Layer 1: Draw zone coloring (subtle background)
        for (int tileY = minTileY; tileY <= maxTileY; tileY++)
        {
            for (int tileX = minTileX; tileX <= maxTileX; tileX++)
            {
                var zoneId = zoneGraph.GetZoneIdAtTile(tileX, tileY);
                if (zoneId.HasValue)
                {
                    int hue = (zoneId.Value * 47) % 360;
                    var color = ColorFromHSV(hue, 0.3f, 0.8f);
                    color.A = 40;
                    Raylib.DrawRectangle(tileX * TileSize, tileY * TileSize, TileSize, TileSize, color);
                }
            }
        }

        // Layer 2: Draw sector/chunk boundaries with labels
        int sectorSizePx = SectorSize * TileSize;
        int minSectorX = (int)Math.Floor((cameraX - halfViewWidth) / sectorSizePx) - 1;
        int maxSectorX = (int)Math.Ceiling((cameraX + halfViewWidth) / sectorSizePx) + 1;
        int minSectorY = (int)Math.Floor((cameraY - halfViewHeight) / sectorSizePx) - 1;
        int maxSectorY = (int)Math.Ceiling((cameraY + halfViewHeight) / sectorSizePx) + 1;

        var sectorColor = new Color((byte)100, (byte)100, (byte)255, (byte)150);
        for (int sectorY = minSectorY; sectorY <= maxSectorY; sectorY++)
        {
            for (int sectorX = minSectorX; sectorX <= maxSectorX; sectorX++)
            {
                int pixelX = sectorX * sectorSizePx;
                int pixelY = sectorY * sectorSizePx;
                Raylib.DrawRectangleLines(pixelX, pixelY, sectorSizePx, sectorSizePx, sectorColor);

                // Draw sector label
                string label = $"S({sectorX},{sectorY})";
                Raylib.DrawText(label, pixelX + 4, pixelY + 4, 10, sectorColor);
            }
        }

        // Layer 3: Draw portals (thick colored lines)
        foreach (var portal in zoneGraph.AllPortals)
        {
            int portalMinX = Math.Min(portal.StartTileX, portal.EndTileX) * TileSize;
            int portalMaxX = Math.Max(portal.StartTileX, portal.EndTileX) * TileSize + TileSize;
            int portalMinY = Math.Min(portal.StartTileY, portal.EndTileY) * TileSize;
            int portalMaxY = Math.Max(portal.StartTileY, portal.EndTileY) * TileSize + TileSize;

            // Skip if not visible
            if (portalMaxX < cameraX - halfViewWidth || portalMinX > cameraX + halfViewWidth ||
                portalMaxY < cameraY - halfViewHeight || portalMinY > cameraY + halfViewHeight)
                continue;

            int startX = portal.StartTileX * TileSize + halfTile;
            int startY = portal.StartTileY * TileSize + halfTile;
            int endX = portal.EndTileX * TileSize + halfTile;
            int endY = portal.EndTileY * TileSize + halfTile;

            int portalHue = ((portal.FromZoneId + portal.ToZoneId) * 67) % 360;
            var portalColor = ColorFromHSV(portalHue, 0.9f, 1.0f);
            portalColor.A = 220;

            // Draw thick portal line (5 pixels)
            for (int offset = -2; offset <= 2; offset++)
            {
                if (portal.StartTileX == portal.EndTileX)
                {
                    Raylib.DrawLine(startX + offset, startY, endX + offset, endY, portalColor);
                }
                else
                {
                    Raylib.DrawLine(startX, startY + offset, endX, endY + offset, portalColor);
                }
            }

            // Draw portal center marker
            int centerX = portal.CenterTileX * TileSize + halfTile;
            int centerY = portal.CenterTileY * TileSize + halfTile;
            Raylib.DrawCircle(centerX, centerY, 4, portalColor);
        }

        // Layer 4: Draw flow field arrows
        RenderFlowFieldArrows(minTileX, maxTileX, minTileY, maxTileY);

        // Update stats
        ZoneCount = zoneGraph.GetZoneCount();
        PortalCount = zoneGraph.GetPortalCount();
    }

    private void UpdateDebugDestinationFromUnits()
    {
        // Tab to cycle through units with move orders
        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            var units = _simWorld.CombatUnitRows;
            int checkedCount = 0;

            while (checkedCount < units.Count)
            {
                _currentUnitIndex = (_currentUnitIndex + 1) % Math.Max(1, units.Count);
                checkedCount++;

                if (units.TryGetRow(_currentUnitIndex, out var unit) &&
                    unit.CurrentOrder == OrderType.Move &&
                    !unit.Flags.IsDead())
                {
                    _debugDestTileX = (unit.OrderTarget.X / Fixed64.FromInt(TileSize)).ToInt();
                    _debugDestTileY = (unit.OrderTarget.Y / Fixed64.FromInt(TileSize)).ToInt();
                    _hasDebugDest = true;
                    return;
                }
            }
        }

        // Auto-track first moving unit if no manual selection
        if (!_hasDebugDest)
        {
            var units = _simWorld.CombatUnitRows;
            for (int slot = 0; slot < units.Count; slot++)
            {
                if (units.TryGetRow(slot, out var unit) &&
                    unit.CurrentOrder == OrderType.Move &&
                    !unit.Flags.IsDead())
                {
                    _debugDestTileX = (unit.OrderTarget.X / Fixed64.FromInt(TileSize)).ToInt();
                    _debugDestTileY = (unit.OrderTarget.Y / Fixed64.FromInt(TileSize)).ToInt();
                    _hasDebugDest = true;
                    _currentUnitIndex = slot;
                    break;
                }
            }
        }
    }

    private void RenderFlowFieldDebug()
    {
        var zoneGraph = _zoneFlowService.GetZoneGraph();

        float cameraX = _cameraManager.TargetX;
        float cameraY = _cameraManager.TargetY;
        int gameWidth = _cameraManager.GameWidth;
        int gameHeight = _cameraManager.GameHeight;

        float halfViewWidth = gameWidth * 0.5f;
        float halfViewHeight = gameHeight * 0.5f;

        int minTileX = Math.Max(0, (int)((cameraX - halfViewWidth) / TileSize) - 1);
        int maxTileX = (int)((cameraX + halfViewWidth) / TileSize) + 2;
        int minTileY = Math.Max(0, (int)((cameraY - halfViewHeight) / TileSize) - 1);
        int maxTileY = (int)((cameraY + halfViewHeight) / TileSize) + 2;

        int halfTile = TileSize / 2;

        // Layer 1: Draw zone coloring (subtle background)
        for (int tileY = minTileY; tileY <= maxTileY; tileY++)
        {
            for (int tileX = minTileX; tileX <= maxTileX; tileX++)
            {
                var zoneId = zoneGraph.GetZoneIdAtTile(tileX, tileY);
                if (zoneId.HasValue)
                {
                    int hue = (zoneId.Value * 47) % 360;
                    var color = ColorFromHSV(hue, 0.3f, 0.8f);
                    color.A = 40;
                    Raylib.DrawRectangle(tileX * TileSize, tileY * TileSize, TileSize, TileSize, color);
                }
            }
        }

        // Layer 2: Draw sector boundaries
        int sectorSizePx = SectorSize * TileSize;
        int minSectorX = (int)Math.Floor((cameraX - halfViewWidth) / sectorSizePx) - 1;
        int maxSectorX = (int)Math.Ceiling((cameraX + halfViewWidth) / sectorSizePx) + 1;
        int minSectorY = (int)Math.Floor((cameraY - halfViewHeight) / sectorSizePx) - 1;
        int maxSectorY = (int)Math.Ceiling((cameraY + halfViewHeight) / sectorSizePx) + 1;

        var sectorColor = new Color((byte)100, (byte)100, (byte)255, (byte)150);
        for (int sectorY = minSectorY; sectorY <= maxSectorY; sectorY++)
        {
            for (int sectorX = minSectorX; sectorX <= maxSectorX; sectorX++)
            {
                int pixelX = sectorX * sectorSizePx;
                int pixelY = sectorY * sectorSizePx;
                Raylib.DrawRectangleLines(pixelX, pixelY, sectorSizePx, sectorSizePx, sectorColor);
                Raylib.DrawText($"S({sectorX},{sectorY})", pixelX + 4, pixelY + 4, 10, sectorColor);
            }
        }

        // Layer 3: Draw portals
        foreach (var portal in zoneGraph.AllPortals)
        {
            int portalMinX = Math.Min(portal.StartTileX, portal.EndTileX) * TileSize;
            int portalMaxX = Math.Max(portal.StartTileX, portal.EndTileX) * TileSize + TileSize;
            int portalMinY = Math.Min(portal.StartTileY, portal.EndTileY) * TileSize;
            int portalMaxY = Math.Max(portal.StartTileY, portal.EndTileY) * TileSize + TileSize;

            if (portalMaxX < cameraX - halfViewWidth || portalMinX > cameraX + halfViewWidth ||
                portalMaxY < cameraY - halfViewHeight || portalMinY > cameraY + halfViewHeight)
                continue;

            int startX = portal.StartTileX * TileSize + halfTile;
            int startY = portal.StartTileY * TileSize + halfTile;
            int endX = portal.EndTileX * TileSize + halfTile;
            int endY = portal.EndTileY * TileSize + halfTile;

            int portalHue = ((portal.FromZoneId + portal.ToZoneId) * 67) % 360;
            var portalColor = ColorFromHSV(portalHue, 0.9f, 1.0f);
            portalColor.A = 220;

            for (int offset = -2; offset <= 2; offset++)
            {
                if (portal.StartTileX == portal.EndTileX)
                    Raylib.DrawLine(startX + offset, startY, endX + offset, endY, portalColor);
                else
                    Raylib.DrawLine(startX, startY + offset, endX, endY + offset, portalColor);
            }

            int centerX = portal.CenterTileX * TileSize + halfTile;
            int centerY = portal.CenterTileY * TileSize + halfTile;
            Raylib.DrawCircle(centerX, centerY, 4, portalColor);
        }

        // Layer 4: Always draw cached flow field arrows
        RenderCachedFlowArrows(minTileX, maxTileX, minTileY, maxTileY);

        // Draw destination marker if set
        if (_hasDebugDest)
        {
            Raylib.DrawRectangle(
                _debugDestTileX * TileSize + 2, _debugDestTileY * TileSize + 2,
                TileSize - 4, TileSize - 4, new Color(255, 255, 0, 100));
            Raylib.DrawRectangleLines(
                _debugDestTileX * TileSize, _debugDestTileY * TileSize,
                TileSize, TileSize, Color.Yellow);
        }

        // Layer 5: Draw A* zone path
        RenderZonePath(zoneGraph, halfTile);

        // Update stats
        ZoneCount = zoneGraph.GetZoneCount();
        PortalCount = zoneGraph.GetPortalCount();
    }

    private void RenderDestinationFlowArrows(int minTileX, int maxTileX, int minTileY, int maxTileY)
    {
        int arrowCount = 0;
        float halfTile = TileSize * 0.5f;

        for (int tileY = minTileY; tileY < maxTileY; tileY++)
        {
            for (int tileX = minTileX; tileX < maxTileX; tileX++)
            {
                var worldPos = new Fixed64Vec2(
                    Fixed64.FromInt(tileX * TileSize) + Fixed64.FromInt(TileSize / 2),
                    Fixed64.FromInt(tileY * TileSize) + Fixed64.FromInt(TileSize / 2));

                // Use cached query to avoid triggering computation for all zones
                var flow = _zoneFlowService.GetFlowDirectionForDestinationCached(
                    worldPos, _debugDestTileX, _debugDestTileY);

                float flowXf = flow.X.ToFloat();
                float flowYf = flow.Y.ToFloat();

                if (flowXf == 0f && flowYf == 0f)
                    continue;

                arrowCount++;

                float worldX = tileX * TileSize + halfTile;
                float worldY = tileY * TileSize + halfTile;

                float endX = worldX + flowXf * ArrowLength;
                float endY = worldY + flowYf * ArrowLength;

                // Green arrows for destination-based flow
                Raylib.DrawLine((int)worldX, (int)worldY, (int)endX, (int)endY, Color.Lime);

                float perpX = -flowYf;
                float perpY = flowXf;
                float headBaseX = endX - flowXf * ArrowHeadSize;
                float headBaseY = endY - flowYf * ArrowHeadSize;

                float leftX = headBaseX + perpX * ArrowHeadSize * 0.5f;
                float leftY = headBaseY + perpY * ArrowHeadSize * 0.5f;
                float rightX = headBaseX - perpX * ArrowHeadSize * 0.5f;
                float rightY = headBaseY - perpY * ArrowHeadSize * 0.5f;

                Raylib.DrawLine((int)endX, (int)endY, (int)leftX, (int)leftY, Color.Lime);
                Raylib.DrawLine((int)endX, (int)endY, (int)rightX, (int)rightY, Color.Lime);
            }
        }

        FlowArrowCount = arrowCount;
    }

    private void RenderCachedFlowArrows(int minTileX, int maxTileX, int minTileY, int maxTileY)
    {
        int arrowCount = 0;
        float halfTile = TileSize * 0.5f;

        for (int tileY = minTileY; tileY < maxTileY; tileY++)
        {
            for (int tileX = minTileX; tileX < maxTileX; tileX++)
            {
                var (flow, cacheType) = _zoneFlowService.GetAnyCachedFlowDirectionWithType(tileX, tileY);

                float flowXf = flow.X.ToFloat();
                float flowYf = flow.Y.ToFloat();

                if (flowXf == 0f && flowYf == 0f)
                    continue;

                arrowCount++;

                float worldX = tileX * TileSize + halfTile;
                float worldY = tileY * TileSize + halfTile;

                float endX = worldX + flowXf * ArrowLength;
                float endY = worldY + flowYf * ArrowLength;

                // Color-code by cache type:
                // - Cyan: Target-set flows (player formation movement)
                // - Green: Single-dest flows (individual unit movement)
                // - Orange: Multi-target flows (enemy pathfinding)
                Color arrowColor = cacheType switch
                {
                    FlowCacheType.TargetSet => Color.SkyBlue,
                    FlowCacheType.SingleDest => Color.Lime,
                    FlowCacheType.MultiTarget => Color.Orange,
                    _ => Color.Gray
                };

                Raylib.DrawLine((int)worldX, (int)worldY, (int)endX, (int)endY, arrowColor);

                float perpX = -flowYf;
                float perpY = flowXf;
                float headBaseX = endX - flowXf * ArrowHeadSize;
                float headBaseY = endY - flowYf * ArrowHeadSize;

                float leftX = headBaseX + perpX * ArrowHeadSize * 0.5f;
                float leftY = headBaseY + perpY * ArrowHeadSize * 0.5f;
                float rightX = headBaseX - perpX * ArrowHeadSize * 0.5f;
                float rightY = headBaseY - perpY * ArrowHeadSize * 0.5f;

                Raylib.DrawLine((int)endX, (int)endY, (int)leftX, (int)leftY, arrowColor);
                Raylib.DrawLine((int)endX, (int)endY, (int)rightX, (int)rightY, arrowColor);
            }
        }

        FlowArrowCount = arrowCount;
    }

    private void RenderZonePath(ZoneGraph zoneGraph, int halfTile)
    {
        var zonePath = zoneGraph.GetLastZonePath();
        ZonePathLength = zonePath.Count;

        if (zonePath.Count <= 1)
        {
            ZonePathInfo = zonePath.Count == 0 ? "No path" : "Already at destination";
            return;
        }

        ZonePathInfo = $"{zonePath.Count} zones";

        // Draw lines between zone centers (thick red lines)
        for (int i = 0; i < zonePath.Count - 1; i++)
        {
            var center1 = zoneGraph.GetZoneCenter(zonePath[i]);
            var center2 = zoneGraph.GetZoneCenter(zonePath[i + 1]);

            if (!center1.HasValue || !center2.HasValue) continue;

            int x1 = center1.Value.centerTileX * TileSize + halfTile;
            int y1 = center1.Value.centerTileY * TileSize + halfTile;
            int x2 = center2.Value.centerTileX * TileSize + halfTile;
            int y2 = center2.Value.centerTileY * TileSize + halfTile;

            // Draw thick line
            for (int offset = -2; offset <= 2; offset++)
            {
                Raylib.DrawLine(x1 + offset, y1, x2 + offset, y2, new Color(255, 100, 100, 200));
                Raylib.DrawLine(x1, y1 + offset, x2, y2 + offset, new Color(255, 100, 100, 200));
            }
        }

        // Draw zone markers along path
        for (int i = 0; i < zonePath.Count; i++)
        {
            var center = zoneGraph.GetZoneCenter(zonePath[i]);
            if (!center.HasValue) continue;

            int pixelX = center.Value.centerTileX * TileSize + halfTile;
            int pixelY = center.Value.centerTileY * TileSize + halfTile;

            Color markerColor;
            int radius;
            if (i == 0)
            {
                markerColor = new Color(100, 255, 100, 255); // Green = start
                radius = 10;
            }
            else if (i == zonePath.Count - 1)
            {
                markerColor = new Color(255, 255, 100, 255); // Yellow = destination
                radius = 10;
            }
            else
            {
                markerColor = new Color(255, 100, 100, 200); // Red = middle
                radius = 6;
            }

            Raylib.DrawCircle(pixelX, pixelY, radius, markerColor);
            Raylib.DrawText($"Z{zonePath[i]}", pixelX + 12, pixelY - 6, 10, markerColor);
        }
    }

    private void RenderDensityGrid()
    {
        int densityCellSize = _separationSystem.CellSize;

        float cameraX = _cameraManager.TargetX;
        float cameraY = _cameraManager.TargetY;
        int gameWidth = _cameraManager.GameWidth;
        int gameHeight = _cameraManager.GameHeight;

        // For pixel-perfect rendering, zoom is always 1.0
        float halfViewWidth = gameWidth * 0.5f;
        float halfViewHeight = gameHeight * 0.5f;

        int minCellX = (int)((cameraX - halfViewWidth) / densityCellSize) - 1;
        int maxCellX = (int)((cameraX + halfViewWidth) / densityCellSize) + 2;
        int minCellY = (int)((cameraY - halfViewHeight) / densityCellSize) - 1;
        int maxCellY = (int)((cameraY + halfViewHeight) / densityCellSize) + 2;

        for (int cellY = minCellY; cellY < maxCellY; cellY++)
        {
            for (int cellX = minCellX; cellX < maxCellX; cellX++)
            {
                Fixed64 posX = Fixed64.FromInt(cellX * densityCellSize + densityCellSize / 2);
                Fixed64 posY = Fixed64.FromInt(cellY * densityCellSize + densityCellSize / 2);

                int cellIndex = _separationSystem.GetCellIndexForPos(posX, posY);
                int density = _separationSystem.GetCellDensity(cellIndex);

                if (density == 0)
                {
                    continue;
                }

                Color color;
                if (density <= DensityThreshold)
                {
                    float normalizedDensity = density / (float)DensityThreshold;
                    byte green = (byte)(255 * (1f - normalizedDensity * 0.5f));
                    byte red = (byte)(255 * normalizedDensity);
                    color = new Color(red, green, (byte)0, (byte)100);
                }
                else
                {
                    float excess = Math.Min((density - DensityThreshold) / 10f, 1f);
                    byte red = 255;
                    byte green = (byte)(100 * (1f - excess));
                    color = new Color(red, green, (byte)0, (byte)150);
                }

                int worldX = cellX * densityCellSize;
                int worldY = cellY * densityCellSize;

                Raylib.DrawRectangle(worldX, worldY, densityCellSize, densityCellSize, color);

                if (density > DensityThreshold)
                {
                    Raylib.DrawRectangleLines(worldX, worldY, densityCellSize, densityCellSize, Color.Red);
                }
            }
        }
    }

    private void RenderNoiseGrid()
    {
        var noiseTable = _simWorld.NoiseGridStateRows;
        bool hasNoiseGrid = noiseTable.Count > 0;

        int cellSizeInPixels = NoiseCell.CellSizePixels;

        float cameraX = _cameraManager.TargetX;
        float cameraY = _cameraManager.TargetY;
        int gameWidth = _cameraManager.GameWidth;
        int gameHeight = _cameraManager.GameHeight;

        // For pixel-perfect rendering, zoom is always 1.0
        float halfViewWidth = gameWidth * 0.5f;
        float halfViewHeight = gameHeight * 0.5f;

        int minCellX = Math.Max(0, (int)((cameraX - halfViewWidth) / cellSizeInPixels) - 1);
        int maxCellX = Math.Min(NoiseCell.GridWidth - 1, (int)((cameraX + halfViewWidth) / cellSizeInPixels) + 1);
        int minCellY = Math.Max(0, (int)((cameraY - halfViewHeight) / cellSizeInPixels) - 1);
        int maxCellY = Math.Min(NoiseCell.GridHeight - 1, (int)((cameraY + halfViewHeight) / cellSizeInPixels) + 1);

        Fixed64 maxNoise = NoiseGridService.MaxNoise;
        int activeCellCount = 0;

        for (int cellY = minCellY; cellY <= maxCellY; cellY++)
        {
            for (int cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                int worldX = cellX * cellSizeInPixels;
                int worldY = cellY * cellSizeInPixels;

                // Always draw grid lines
                Raylib.DrawRectangleLines(worldX, worldY, cellSizeInPixels, cellSizeInPixels,
                    new Color((byte)100, (byte)100, (byte)100, (byte)80));

                if (!hasNoiseGrid)
                    continue;

                Fixed64 noise = NoiseGridService.GetNoiseLevel(noiseTable, new NoiseCell(cellX, cellY));

                if (noise <= Fixed64.Zero)
                {
                    continue;
                }

                activeCellCount++;

                // Normalize noise to 0-1 range
                float normalizedNoise = (noise / maxNoise).ToFloat();
                normalizedNoise = Math.Clamp(normalizedNoise, 0f, 1f);

                // Color from blue (low) through yellow to red (high)
                Color color;
                if (normalizedNoise < 0.5f)
                {
                    // Blue to Yellow
                    float t = normalizedNoise * 2f;
                    byte r = (byte)(255 * t);
                    byte g = (byte)(255 * t);
                    byte b = (byte)(255 * (1f - t));
                    color = new Color(r, g, b, (byte)(100 + 100 * normalizedNoise));
                }
                else
                {
                    // Yellow to Red
                    float t = (normalizedNoise - 0.5f) * 2f;
                    byte r = 255;
                    byte g = (byte)(255 * (1f - t));
                    byte b = 0;
                    color = new Color(r, g, b, (byte)(150 + 50 * normalizedNoise));
                }

                Raylib.DrawRectangle(worldX, worldY, cellSizeInPixels, cellSizeInPixels, color);

                // Draw brighter cell border for noise cells
                Raylib.DrawRectangleLines(worldX, worldY, cellSizeInPixels, cellSizeInPixels,
                    new Color((byte)255, (byte)255, (byte)255, (byte)100));

                // Draw noise value in high-noise cells
                if (normalizedNoise > 0.3f)
                {
                    int noiseVal = noise.ToInt();
                    Raylib.DrawText(noiseVal.ToString(), worldX + 4, worldY + 4, 12, Color.White);
                }
            }
        }

        NoiseGridCellCount = activeCellCount;

        // Draw test noise spot locations as markers
        var testSpots = new (int x, int y)[]
        {
            (1000, 1000), (4096, 500), (7000, 1000),
            (500, 4096), (7500, 4096),
            (1000, 7000), (4096, 7500), (7000, 7000)
        };

        foreach (var spot in testSpots)
        {
            Raylib.DrawCircle(spot.x, spot.y, 8, new Color((byte)255, (byte)100, (byte)0, (byte)200));
            Raylib.DrawCircleLines(spot.x, spot.y, 10, Color.Orange);
        }
    }

    private void RenderThreatGrid()
    {
        var threatTable = _simWorld.ThreatGridStateRows;
        bool hasThreatGrid = threatTable.Count > 0;

        int cellSizeInPixels = ThreatCell.CellSizePixels;

        float cameraX = _cameraManager.TargetX;
        float cameraY = _cameraManager.TargetY;
        int gameWidth = _cameraManager.GameWidth;
        int gameHeight = _cameraManager.GameHeight;

        // For pixel-perfect rendering, zoom is always 1.0
        float halfViewWidth = gameWidth * 0.5f;
        float halfViewHeight = gameHeight * 0.5f;

        int minCellX = Math.Max(0, (int)((cameraX - halfViewWidth) / cellSizeInPixels) - 1);
        int maxCellX = Math.Min(ThreatCell.GridWidth - 1, (int)((cameraX + halfViewWidth) / cellSizeInPixels) + 1);
        int minCellY = Math.Max(0, (int)((cameraY - halfViewHeight) / cellSizeInPixels) - 1);
        int maxCellY = Math.Min(ThreatCell.GridHeight - 1, (int)((cameraY + halfViewHeight) / cellSizeInPixels) + 1);

        int activeThreatCells = 0;
        int activePeakCells = 0;
        int maxThreat = 0;

        // Threshold values from ThreatGridService
        int loseInterestThreshold = ThreatGridService.LoseInterestThreshold.ToInt();  // 20
        int chaseThreshold = ThreatGridService.ChaseThreshold.ToInt();                // 100

        for (int cellY = minCellY; cellY <= maxCellY; cellY++)
        {
            for (int cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                int worldX = cellX * cellSizeInPixels;
                int worldY = cellY * cellSizeInPixels;

                // Always draw grid lines
                Raylib.DrawRectangleLines(worldX, worldY, cellSizeInPixels, cellSizeInPixels,
                    new Color((byte)80, (byte)80, (byte)80, (byte)60));

                if (!hasThreatGrid)
                    continue;

                var cell = new ThreatCell(cellX, cellY);
                Fixed64 threat = ThreatGridService.GetThreatLevel(threatTable, cell);
                Fixed64 peakThreat = ThreatGridService.GetPeakThreatLevel(threatTable, cell);

                int threatInt = threat.ToInt();
                int peakInt = peakThreat.ToInt();

                if (threatInt > maxThreat) maxThreat = threatInt;

                // Track peak threat cells (even if current threat is 0)
                if (peakInt > 0 && threatInt == 0)
                {
                    activePeakCells++;
                    // Draw dashed border for peak-only cells (memory indicator)
                    float normalizedPeak = Math.Clamp(peakInt / 100f, 0f, 1f);
                    byte alpha = (byte)(60 + 80 * normalizedPeak);
                    var peakColor = new Color((byte)180, (byte)100, (byte)255, alpha);

                    // Draw corner markers for peak threat
                    int markerSize = 8;
                    Raylib.DrawRectangle(worldX, worldY, markerSize, markerSize, peakColor);
                    Raylib.DrawRectangle(worldX + cellSizeInPixels - markerSize, worldY, markerSize, markerSize, peakColor);
                    Raylib.DrawRectangle(worldX, worldY + cellSizeInPixels - markerSize, markerSize, markerSize, peakColor);
                    Raylib.DrawRectangle(worldX + cellSizeInPixels - markerSize, worldY + cellSizeInPixels - markerSize, markerSize, markerSize, peakColor);
                }

                if (threatInt <= 0)
                {
                    continue;
                }

                activeThreatCells++;

                // Color based on threat thresholds:
                // Blue: 0-20 (below lose interest)
                // Cyan: 20-50
                // Yellow: 50-100 (approaching chase)
                // Red: 100+ (chase threshold)
                Color color;
                if (threatInt < loseInterestThreshold)
                {
                    // Blue (low threat)
                    float t = threatInt / (float)loseInterestThreshold;
                    byte intensity = (byte)(100 + 100 * t);
                    color = new Color((byte)0, (byte)(50 * t), intensity, (byte)(80 + 60 * t));
                }
                else if (threatInt < 50)
                {
                    // Cyan (moderate)
                    float t = (threatInt - loseInterestThreshold) / 30f;
                    color = new Color((byte)0, (byte)(150 + 50 * t), (byte)(200 - 50 * t), (byte)(120 + 30 * t));
                }
                else if (threatInt < chaseThreshold)
                {
                    // Yellow (high, approaching chase)
                    float t = (threatInt - 50) / 50f;
                    byte r = (byte)(200 + 55 * t);
                    byte g = (byte)(200 - 50 * t);
                    color = new Color(r, g, (byte)0, (byte)(140 + 40 * t));
                }
                else
                {
                    // Red (chase threshold reached)
                    float t = Math.Clamp((threatInt - chaseThreshold) / 100f, 0f, 1f);
                    byte r = 255;
                    byte g = (byte)(50 * (1f - t));
                    color = new Color(r, g, (byte)0, (byte)(180 + 50 * t));
                }

                Raylib.DrawRectangle(worldX, worldY, cellSizeInPixels, cellSizeInPixels, color);

                // Draw brighter border for threat cells
                Color borderColor;
                if (threatInt >= chaseThreshold)
                {
                    borderColor = Color.Red;
                }
                else if (threatInt >= 50)
                {
                    borderColor = Color.Yellow;
                }
                else
                {
                    borderColor = new Color((byte)150, (byte)150, (byte)255, (byte)150);
                }
                Raylib.DrawRectangleLines(worldX, worldY, cellSizeInPixels, cellSizeInPixels, borderColor);

                // Draw threat value in significant cells
                if (threatInt >= loseInterestThreshold)
                {
                    string text = threatInt.ToString();
                    Color textColor = threatInt >= chaseThreshold ? Color.White : Color.Black;
                    Raylib.DrawText(text, worldX + 4, worldY + 4, 14, textColor);

                    // Show peak if different from current
                    if (peakInt > threatInt)
                    {
                        string peakText = $"P:{peakInt}";
                        Raylib.DrawText(peakText, worldX + 4, worldY + 20, 10, Color.Magenta);
                    }
                }
            }
        }

        ThreatGridCellCount = activeThreatCells;
        ThreatGridMaxValue = maxThreat;
        PeakThreatCellCount = activePeakCells;
    }

    private void RenderWorldTiles()
    {
        float cameraX = _cameraManager.TargetX;
        float cameraY = _cameraManager.TargetY;
        int gameWidth = _cameraManager.GameWidth;
        int gameHeight = _cameraManager.GameHeight;

        float halfViewWidth = gameWidth * 0.5f;
        float halfViewHeight = gameHeight * 0.5f;

        int minTileX = Math.Max(0, (int)((cameraX - halfViewWidth) / TileSize) - 1);
        int maxTileX = Math.Min(_terrainData.WidthTiles - 1, (int)((cameraX + halfViewWidth) / TileSize) + 1);
        int minTileY = Math.Max(0, (int)((cameraY - halfViewHeight) / TileSize) - 1);
        int maxTileY = Math.Min(_terrainData.HeightTiles - 1, (int)((cameraY + halfViewHeight) / TileSize) + 1);

        // Draw world tiles with semi-transparent colored fill
        for (int tileY = minTileY; tileY <= maxTileY; tileY++)
        {
            for (int tileX = minTileX; tileX <= maxTileX; tileX++)
            {
                var terrain = _terrainData.GetTerrainAt(tileX, tileY);
                int worldX = tileX * TileSize;
                int worldY = tileY * TileSize;

                // Get color based on terrain type
                Color fillColor = terrain switch
                {
                    TerrainType.None => new Color(50, 50, 50, 100),
                    TerrainType.Grass => new Color(50, 180, 50, 80),
                    TerrainType.Dirt => new Color(139, 90, 43, 80),
                    TerrainType.Sand => new Color(210, 180, 140, 80),
                    TerrainType.Water => new Color(64, 164, 223, 100),
                    TerrainType.Ramp => new Color(180, 180, 100, 80),
                    TerrainType.Mountain => new Color(128, 128, 128, 100),
                    _ => new Color(255, 0, 255, 100) // Magenta for unknown
                };

                // Draw filled tile
                Raylib.DrawRectangle(worldX, worldY, TileSize, TileSize, fillColor);

                // Draw tile grid lines
                Raylib.DrawRectangleLines(worldX, worldY, TileSize, TileSize,
                    new Color(255, 255, 255, 60));

                // Draw tile coordinates for every 4th tile
                if (tileX % 4 == 0 && tileY % 4 == 0)
                {
                    Raylib.DrawText($"{tileX},{tileY}", worldX + 2, worldY + 2, 8,
                        new Color(255, 255, 255, 150));
                }
            }
        }

        // Draw a legend in screen space
        int legendX = 10;
        int legendY = 160;
        Raylib.DrawText("World Tiles:", legendX, legendY, 12, Color.White);
        legendY += 16;

        var legendItems = new (TerrainType type, string name, Color color)[]
        {
            (TerrainType.Grass, "Grass", new Color(50, 180, 50, 255)),
            (TerrainType.Dirt, "Dirt", new Color(139, 90, 43, 255)),
            (TerrainType.Sand, "Sand", new Color(210, 180, 140, 255)),
            (TerrainType.Water, "Water", new Color(64, 164, 223, 255)),
            (TerrainType.Mountain, "Mountain", new Color(128, 128, 128, 255)),
        };

        foreach (var item in legendItems)
        {
            Raylib.DrawRectangle(legendX, legendY, 12, 12, item.color);
            Raylib.DrawText(item.name, legendX + 16, legendY, 10, Color.White);
            legendY += 14;
        }
    }

    private void RenderZombieStates()
    {
        var zombies = _simWorld.ZombieRows;

        float cameraX = _cameraManager.TargetX;
        float cameraY = _cameraManager.TargetY;
        int gameWidth = _cameraManager.GameWidth;
        int gameHeight = _cameraManager.GameHeight;

        // For pixel-perfect rendering, zoom is always 1.0
        float halfViewWidth = gameWidth * 0.5f;
        float halfViewHeight = gameHeight * 0.5f;

        // View bounds for culling
        float minX = cameraX - halfViewWidth - 50;
        float maxX = cameraX + halfViewWidth + 50;
        float minY = cameraY - halfViewHeight - 50;
        float maxY = cameraY + halfViewHeight + 50;

        for (int slot = 0; slot < zombies.Count; slot++)
        {
            var zombie = zombies.GetRowBySlot(slot);
            if (zombie.Flags.IsDead()) continue;

            var pos = zombie.Position.ToVector2();

            // Cull off-screen zombies
            if (pos.X < minX || pos.X > maxX || pos.Y < minY || pos.Y > maxY) continue;

            // Get state text and color
            string stateText;
            Color stateColor;
            switch (zombie.State)
            {
                case ZombieState.Idle:
                    stateText = "IDLE";
                    stateColor = Color.Gray;
                    break;
                case ZombieState.Wander:
                    stateText = "WANDER";
                    stateColor = Color.Blue;
                    break;
                case ZombieState.Chase:
                    // Show TableId to detect Invalid handles (TableId=-1)
                    var handle = zombie.TargetHandle;
                    bool canResolve = _simWorld.TryGetPlayerTargetableUnits(handle, out _);
                    stateText = $"CHASE Tbl:{handle.TableId} Id:{handle.RawId} Res:{(canResolve ? "Y" : "N")}";
                    stateColor = canResolve ? Color.Orange : Color.Magenta;
                    break;
                case ZombieState.Attack:
                    stateText = "ATTACK";
                    stateColor = Color.Red;
                    break;
                case ZombieState.WaveChase:
                    var waveHandle = zombie.TargetHandle;
                    bool waveCanResolve = _simWorld.TryGetPlayerTargetableUnits(waveHandle, out _);
                    stateText = $"WAVE Tbl:{waveHandle.TableId} Res:{(waveCanResolve ? "Y" : "N")}";
                    stateColor = Color.Yellow;
                    break;
                default:
                    stateText = "???";
                    stateColor = Color.White;
                    break;
            }

            // Draw text above zombie (offset by 20 pixels)
            int textX = (int)pos.X - 20;
            int textY = (int)pos.Y - 25;
            Raylib.DrawText(stateText, textX, textY, 10, stateColor);

            // Draw velocity info for Chase and WaveChase states
            if (zombie.State == ZombieState.Chase || zombie.State == ZombieState.WaveChase)
            {
                var vel = zombie.Velocity;
                var flow = zombie.Flow;
                string velText = $"V:({vel.X.ToFloat():F1},{vel.Y.ToFloat():F1}) F:({flow.X.ToFloat():F1},{flow.Y.ToFloat():F1})";
                Raylib.DrawText(velText, textX - 20, textY + 12, 8, Color.Lime);
            }

            // Draw line to target if chasing (supports both CombatUnits and Buildings)
            if (zombie.State == ZombieState.Chase && zombie.TargetHandle.IsValid)
            {
                if (_simWorld.TryGetPlayerTargetableUnits(zombie.TargetHandle, out var target))
                {
                    var targetPos = target.Position.ToVector2();
                    Raylib.DrawLine((int)pos.X, (int)pos.Y, (int)targetPos.X, (int)targetPos.Y, Color.Yellow);
                }
            }

            // Draw acquisition range circle and highlight found units for chase-state zombies
            if (zombie.State == ZombieState.Chase)
            {
                // TargetAcquisitionRange is in pixels (int)
                float range = zombie.TargetAcquisitionRange;
                Raylib.DrawCircleLines((int)pos.X, (int)pos.Y, range, Color.Yellow);

                // Highlight units found by QueryRadius (green dots)
                Fixed64 acquisitionRange = Fixed64.FromInt(zombie.TargetAcquisitionRange);
                var units = _simWorld.CombatUnitRows;
                foreach (int unitSlot in units.QueryRadius(zombie.Position, acquisitionRange))
                {
                    var foundUnit = units.GetRowBySlot(unitSlot);
                    if (foundUnit.Flags.IsDead()) continue;
                    var unitPos = foundUnit.Position.ToVector2();
                    Raylib.DrawCircle((int)unitPos.X, (int)unitPos.Y, 8, Color.Green);  // Found = green dot
                }
            }
        }
    }

    private void RenderCombatUnitDebugInfo()
    {
        var units = _simWorld.CombatUnitRows;

        float cameraX = _cameraManager.TargetX;
        float cameraY = _cameraManager.TargetY;
        int gameWidth = _cameraManager.GameWidth;
        int gameHeight = _cameraManager.GameHeight;

        float halfViewWidth = gameWidth * 0.5f;
        float halfViewHeight = gameHeight * 0.5f;

        // View bounds for culling
        float minX = cameraX - halfViewWidth - 50;
        float maxX = cameraX + halfViewWidth + 50;
        float minY = cameraY - halfViewHeight - 50;
        float maxY = cameraY + halfViewHeight + 50;

        for (int slot = 0; slot < units.Count; slot++)
        {
            var unit = units.GetRowBySlot(slot);
            var pos = unit.Position.ToVector2();

            // Cull off-screen units
            if (pos.X < minX || pos.X > maxX || pos.Y < minY || pos.Y > maxY) continue;

            // Slot number with color based on IsDead
            Color color = unit.Flags.IsDead() ? Color.Red : Color.Lime;
            Raylib.DrawText($"S:{slot}", (int)pos.X + 20, (int)pos.Y - 30, 10, color);

            // Dead indicator with diagnostic info
            if (unit.Flags.IsDead())
            {
                // Show Health and DeathFrame to diagnose why unit isn't being freed
                // Expected: Health <= 0 AND DeathFrame > 0
                // Bug: Health > 0 OR DeathFrame == 0 means death flow is broken
                string deadInfo = $"DEAD H:{unit.Health} DF:{unit.DeathFrame}";
                Color deadColor = (unit.Health <= 0 && unit.DeathFrame > 0) ? Color.Red : Color.Magenta;
                Raylib.DrawText(deadInfo, (int)pos.X + 20, (int)pos.Y - 20, 10, deadColor);
            }

            // Chunk coordinates (ChunkSize = 4096 for CombatUnitRow)
            int chunkX = (int)(unit.Position.X.ToFloat() / 4096);
            int chunkY = (int)(unit.Position.Y.ToFloat() / 4096);
            Raylib.DrawText($"C:{chunkX},{chunkY}", (int)pos.X + 20, (int)pos.Y - 10, 10, Color.Gray);
        }
    }

    private static Color ColorFromHSV(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1 - MathF.Abs((h / 60f) % 2 - 1));
        float m = v - c;

        float r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new Color(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255),
            (byte)255);
    }
}
