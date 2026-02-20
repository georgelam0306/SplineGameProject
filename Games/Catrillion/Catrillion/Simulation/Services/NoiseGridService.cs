using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Core;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Service for accessing and manipulating the noise grid.
/// Provides world-to-grid coordinate conversion and noise level queries.
///
/// The noise grid is 32x32 cells, each covering 256x256 pixels (8x8 tiles).
/// Grid constants are defined in GridDefinitions.cs and accessed via NoiseCell.
/// </summary>
public static class NoiseGridService
{
    public static readonly Fixed64 MaxNoise = Fixed64.FromInt(1000);
    // Decay rate per second (multiply by DeltaSeconds in systems)
    public static readonly Fixed64 DecayRatePerSecond = Fixed64.FromInt(60);  // 1 * 60fps
    public static readonly Fixed64 NoiseAttractionThreshold = Fixed64.FromInt(50);

    /// <summary>
    /// Convert world position to noise grid cell coordinates.
    /// </summary>
    public static NoiseCell WorldToCell(Fixed64Vec2 worldPos)
    {
        return NoiseCell.FromPixel(worldPos);
    }

    /// <summary>
    /// Get cell center in world coordinates.
    /// </summary>
    public static Fixed64Vec2 CellToWorld(NoiseCell cell)
    {
        return cell.ToPixelCenter();
    }

    /// <summary>
    /// Set noise at a world position to the max of current and new value.
    /// Use this for per-tick noise sources (buildings, units).
    /// </summary>
    public static void SetNoiseMax(NoiseGridStateRowTable table, Fixed64Vec2 worldPos, Fixed64 amount)
    {
        var cell = WorldToCell(worldPos);
        Fixed64 current = GetNoiseLevel(table, cell);
        if (amount > current)
        {
            SetNoiseLevel(table, cell, amount);
        }
    }

    /// <summary>
    /// Add noise at a world position (accumulative - use for one-time events like explosions).
    /// </summary>
    public static void AddNoise(NoiseGridStateRowTable table, Fixed64Vec2 worldPos, Fixed64 amount)
    {
        var cell = WorldToCell(worldPos);
        Fixed64 current = GetNoiseLevel(table, cell);
        Fixed64 newLevel = current + amount;
        if (newLevel > MaxNoise) newLevel = MaxNoise;
        SetNoiseLevel(table, cell, newLevel);
    }

    /// <summary>
    /// Clear the entire noise grid to zero.
    /// </summary>
    public static void ClearGrid(NoiseGridStateRowTable table)
    {
        for (int y = 0; y < NoiseCell.GridHeight; y++)
        {
            var rowSpan = GetRowSpan(table, y);
            rowSpan.Clear();
        }
    }

    /// <summary>
    /// Get noise level at grid cell.
    /// </summary>
    public static Fixed64 GetNoiseLevel(NoiseGridStateRowTable table, NoiseCell cell)
    {
        // Access via the table's 2D array accessor: Noise(slot, row, col)
        return table.Noise(0, cell.Y, cell.X);
    }

    /// <summary>
    /// Set noise level at grid cell.
    /// </summary>
    public static void SetNoiseLevel(NoiseGridStateRowTable table, NoiseCell cell, Fixed64 value)
    {
        // Access via the table's 2D array accessor - returns ref so we can assign
        table.Noise(0, cell.Y, cell.X) = value;
    }

    /// <summary>
    /// Get the row span for a given row index. Useful for bulk operations.
    /// </summary>
    public static Span<Fixed64> GetRowSpan(NoiseGridStateRowTable table, int rowY)
    {
        // Access via the table's 2D array row accessor: NoiseRow(slot, row)
        return table.NoiseRow(0, rowY);
    }

    /// <summary>
    /// Find the cell with the highest noise level within a search radius.
    /// Returns the cell coordinates and direction toward it from the given position.
    /// </summary>
    public static (NoiseCell cell, Fixed64 maxNoise, Fixed64Vec2 direction) FindHighestNoiseNearby(
        NoiseGridStateRowTable table,
        Fixed64Vec2 worldPos,
        int searchRadius)
    {
        var center = WorldToCell(worldPos);

        Fixed64 maxNoise = Fixed64.Zero;
        var maxNoiseCell = center;

        for (int dy = -searchRadius; dy <= searchRadius; dy++)
        {
            int checkY = center.Y + dy;
            if (checkY < 0 || checkY >= NoiseCell.GridHeight) continue;

            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                int checkX = center.X + dx;
                if (checkX < 0 || checkX >= NoiseCell.GridWidth) continue;

                var checkCell = new NoiseCell(checkX, checkY);
                Fixed64 noise = GetNoiseLevel(table, checkCell);
                if (noise > maxNoise)
                {
                    maxNoise = noise;
                    maxNoiseCell = checkCell;
                }
            }
        }

        // Calculate direction toward the noise cell
        Fixed64Vec2 direction = Fixed64Vec2.Zero;
        if (maxNoise > Fixed64.Zero)
        {
            Fixed64Vec2 noiseCenter = CellToWorld(maxNoiseCell);
            Fixed64 ddx = noiseCenter.X - worldPos.X;
            Fixed64 ddy = noiseCenter.Y - worldPos.Y;

            Fixed64 magSq = ddx * ddx + ddy * ddy;
            if (magSq > Fixed64.FromFloat(0.001f))
            {
                Fixed64 mag = Fixed64.Sqrt(magSq);
                direction = new Fixed64Vec2(ddx / mag, ddy / mag);
            }
        }

        return (maxNoiseCell, maxNoise, direction);
    }
}
