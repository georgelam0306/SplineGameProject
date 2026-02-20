using Catrillion.Config;
using Catrillion.Simulation.Components;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Service for accessing and manipulating the fog of war visibility grid.
/// The visibility grid is 256x256 cells (1:1 with tiles).
///
/// Visibility uses countdown values for hysteresis (prevents flickering):
/// 0 = Unexplored (never seen)
/// 1 = Fogged (was visible, now fogged)
/// 2-255 = Visible (countdown timer, decrements each tick)
///
/// When revealed, tiles are set to VisibleDuration.
/// Each tick, values > 1 decrement by 1. When reaching 1, tile becomes fogged.
/// </summary>
public static class FogOfWarService
{
    public const byte Unexplored = 0;
    public const byte Fogged = 1;
    public const byte VisibleMin = 2;  // Minimum value for "visible"
    public const byte VisibleDuration = SimulationConfig.TickRate / 2;  // Ticks to stay visible (~0.5s)

    public const int GridWidth = GameConfig.Map.WidthTiles;   // 256
    public const int GridHeight = GameConfig.Map.HeightTiles; // 256
    public const int TileSize = GameConfig.Map.TileSize;      // 32

    /// <summary>
    /// Initializes the fog grid to all unexplored.
    /// </summary>
    public static void Initialize(FogOfWarGridStateRowTable table)
    {
        // Ensure table has a row allocated
        if (table.Count == 0)
        {
            table.Allocate();
        }

        // Set all cells to unexplored
        for (int y = 0; y < GridHeight; y++)
        {
            var row = table.VisibilityRow(0, y);
            row.Fill(Unexplored);
        }
    }

    /// <summary>
    /// Decays visibility countdown timers.
    /// Values > 1 decrement by 1. When reaching 1, tile becomes fogged.
    /// Called at the start of each visibility update tick.
    /// </summary>
    public static void DecayVisibility(FogOfWarGridStateRowTable table)
    {
        for (int y = 0; y < GridHeight; y++)
        {
            var row = table.VisibilityRow(0, y);
            for (int x = 0; x < GridWidth; x++)
            {
                byte val = row[x];
                if (val > Fogged)  // If visible (countdown > 1)
                {
                    row[x] = (byte)(val - 1);  // Decrement countdown
                }
            }
        }
    }

    /// <summary>
    /// Reveals tiles within a circular radius from the given tile center.
    /// Sets tiles to VisibleDuration countdown value.
    /// </summary>
    public static void RevealRadius(FogOfWarGridStateRowTable table, int centerTileX, int centerTileY, int radiusTiles)
    {
        if (radiusTiles <= 0) return;

        int radiusSq = radiusTiles * radiusTiles;

        // Calculate bounding box
        int minX = Math.Max(0, centerTileX - radiusTiles);
        int maxX = Math.Min(GridWidth - 1, centerTileX + radiusTiles);
        int minY = Math.Max(0, centerTileY - radiusTiles);
        int maxY = Math.Min(GridHeight - 1, centerTileY + radiusTiles);

        for (int y = minY; y <= maxY; y++)
        {
            var row = table.VisibilityRow(0, y);
            int dy = y - centerTileY;
            int dySq = dy * dy;

            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - centerTileX;
                int distSq = dx * dx + dySq;

                if (distSq <= radiusSq)
                {
                    row[x] = VisibleDuration;  // Reset countdown to full duration
                }
            }
        }
    }

    /// <summary>
    /// Gets the visibility state at the given tile coordinates.
    /// </summary>
    public static byte GetVisibility(FogOfWarGridStateRowTable table, int tileX, int tileY)
    {
        if (tileX < 0 || tileX >= GridWidth) return Unexplored;
        if (tileY < 0 || tileY >= GridHeight) return Unexplored;
        return table.Visibility(0, tileY, tileX);
    }

    /// <summary>
    /// Sets the visibility state at the given tile coordinates.
    /// </summary>
    public static void SetVisibility(FogOfWarGridStateRowTable table, int tileX, int tileY, byte state)
    {
        if (tileX < 0 || tileX >= GridWidth) return;
        if (tileY < 0 || tileY >= GridHeight) return;
        table.Visibility(0, tileY, tileX) = state;
    }

    /// <summary>
    /// Checks if a tile is currently visible (countdown >= VisibleMin).
    /// </summary>
    public static bool IsTileVisible(FogOfWarGridStateRowTable table, int tileX, int tileY)
    {
        return GetVisibility(table, tileX, tileY) >= VisibleMin;
    }

    /// <summary>
    /// Checks if a tile has ever been explored (visible or fogged, i.e., != Unexplored).
    /// </summary>
    public static bool IsTileExplored(FogOfWarGridStateRowTable table, int tileX, int tileY)
    {
        return GetVisibility(table, tileX, tileY) != Unexplored;
    }

    /// <summary>
    /// Gets visibility row span for bulk operations.
    /// </summary>
    public static Span<byte> GetVisibilityRow(FogOfWarGridStateRowTable table, int y)
    {
        return table.VisibilityRow(0, y);
    }
}
