using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Core;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Service for accessing and manipulating the threat grid.
/// Threat accumulates from multiple sources and influences zombie state transitions.
///
/// The threat grid is 64x64 cells, each covering 128x128 pixels (4x4 tiles).
/// Grid constants are defined in GridDefinitions.cs and accessed via ThreatCell.
/// </summary>
public static class ThreatGridService
{
    // Threat thresholds for state transitions
    // Any threat triggers chase - threat values are about priority, not triggering
    public static readonly Fixed64 ChaseThreshold = Fixed64.FromInt(1);
    public static readonly Fixed64 LoseInterestThreshold = Fixed64.FromInt(1);

    // Decay rates per second (multiply by DeltaSeconds in systems)
    public static readonly Fixed64 ThreatDecayRatePerSecond = Fixed64.FromInt(30);      // 0.5 * 60fps
    public static readonly Fixed64 PeakThreatDecayRatePerSecond = Fixed64.FromInt(6);   // 0.1 * 60fps

    // Maximum value
    public static readonly Fixed64 MaxThreat = Fixed64.FromInt(1000);

    /// <summary>
    /// Convert world position to threat grid cell coordinates.
    /// </summary>
    public static ThreatCell WorldToCell(Fixed64Vec2 worldPos)
    {
        return ThreatCell.FromPixel(worldPos);
    }

    /// <summary>
    /// Get cell center in world coordinates.
    /// </summary>
    public static Fixed64Vec2 CellToWorld(ThreatCell cell)
    {
        return cell.ToPixelCenter();
    }

    /// <summary>
    /// Add threat at a world position (accumulates).
    /// </summary>
    public static void AddThreat(ThreatGridStateRowTable table, Fixed64Vec2 worldPos, Fixed64 amount)
    {
        var cell = WorldToCell(worldPos);
        Fixed64 current = GetThreatLevel(table, cell);
        Fixed64 newLevel = current + amount;
        if (newLevel > MaxThreat) newLevel = MaxThreat;
        SetThreatLevel(table, cell, newLevel);

        // Update peak threat if current exceeds it
        Fixed64 peak = GetPeakThreatLevel(table, cell);
        if (newLevel > peak)
        {
            SetPeakThreatLevel(table, cell, newLevel);
        }
    }

    /// <summary>
    /// Set threat at a world position to at least the given amount (non-accumulating).
    /// Use this for continuous threat sources like buildings/units - threat disappears when source is gone.
    /// </summary>
    public static void SetThreatMax(ThreatGridStateRowTable table, Fixed64Vec2 worldPos, Fixed64 amount)
    {
        var cell = WorldToCell(worldPos);
        Fixed64 current = GetThreatLevel(table, cell);
        if (amount > current)
        {
            SetThreatLevel(table, cell, amount);

            // Update peak threat if current exceeds it
            Fixed64 peak = GetPeakThreatLevel(table, cell);
            if (amount > peak)
            {
                SetPeakThreatLevel(table, cell, amount);
            }
        }
    }

    /// <summary>
    /// Add threat with radius spread (for explosions, loud events, player proximity).
    /// </summary>
    public static void AddThreatWithRadius(ThreatGridStateRowTable table, Fixed64Vec2 worldPos, Fixed64 amount, int radiusCells)
    {
        var center = WorldToCell(worldPos);
        int radiusSq = radiusCells * radiusCells;

        for (int dy = -radiusCells; dy <= radiusCells; dy++)
        {
            int checkY = center.Y + dy;
            if (checkY < 0 || checkY >= ThreatCell.GridHeight) continue;

            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                int checkX = center.X + dx;
                if (checkX < 0 || checkX >= ThreatCell.GridWidth) continue;

                // Fall off with distance
                int distSq = dx * dx + dy * dy;
                if (distSq > radiusSq) continue;

                Fixed64 falloff = Fixed64.FromInt(radiusSq - distSq) / Fixed64.FromInt(radiusSq);
                Fixed64 scaledAmount = amount * falloff;

                var checkCell = new ThreatCell(checkX, checkY);
                Fixed64 current = GetThreatLevel(table, checkCell);
                Fixed64 newLevel = current + scaledAmount;
                if (newLevel > MaxThreat) newLevel = MaxThreat;
                SetThreatLevel(table, checkCell, newLevel);

                Fixed64 peak = GetPeakThreatLevel(table, checkCell);
                if (newLevel > peak)
                {
                    SetPeakThreatLevel(table, checkCell, newLevel);
                }
            }
        }
    }

    /// <summary>
    /// Get threat level at grid cell.
    /// </summary>
    public static Fixed64 GetThreatLevel(ThreatGridStateRowTable table, ThreatCell cell)
    {
        return table.Threat(0, cell.Y, cell.X);
    }

    /// <summary>
    /// Set threat level at grid cell.
    /// </summary>
    public static void SetThreatLevel(ThreatGridStateRowTable table, ThreatCell cell, Fixed64 value)
    {
        table.Threat(0, cell.Y, cell.X) = value;
    }

    /// <summary>
    /// Get peak threat level at grid cell (zombie memory).
    /// </summary>
    public static Fixed64 GetPeakThreatLevel(ThreatGridStateRowTable table, ThreatCell cell)
    {
        return table.PeakThreat(0, cell.Y, cell.X);
    }

    /// <summary>
    /// Set peak threat level at grid cell.
    /// </summary>
    public static void SetPeakThreatLevel(ThreatGridStateRowTable table, ThreatCell cell, Fixed64 value)
    {
        table.PeakThreat(0, cell.Y, cell.X) = value;
    }

    /// <summary>
    /// Get the row span for threat values. Useful for bulk operations.
    /// </summary>
    public static Span<Fixed64> GetThreatRowSpan(ThreatGridStateRowTable table, int rowY)
    {
        return table.ThreatRow(0, rowY);
    }

    /// <summary>
    /// Get the row span for peak threat values. Useful for bulk operations.
    /// </summary>
    public static Span<Fixed64> GetPeakThreatRowSpan(ThreatGridStateRowTable table, int rowY)
    {
        return table.PeakThreatRow(0, rowY);
    }

    /// <summary>
    /// Get threat level at a world position.
    /// </summary>
    public static Fixed64 GetThreatAtPosition(ThreatGridStateRowTable table, Fixed64Vec2 worldPos)
    {
        var cell = WorldToCell(worldPos);
        return GetThreatLevel(table, cell);
    }

    /// <summary>
    /// Find the cell with highest threat within search radius.
    /// Returns direction toward highest threat source.
    /// </summary>
    public static (ThreatCell cell, Fixed64 maxThreat, Fixed64Vec2 direction) FindHighestThreatNearby(
        ThreatGridStateRowTable table,
        Fixed64Vec2 worldPos,
        int searchRadius)
    {
        var center = WorldToCell(worldPos);

        Fixed64 maxThreat = Fixed64.Zero;
        var maxThreatCell = center;

        for (int dy = -searchRadius; dy <= searchRadius; dy++)
        {
            int checkY = center.Y + dy;
            if (checkY < 0 || checkY >= ThreatCell.GridHeight) continue;

            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                int checkX = center.X + dx;
                if (checkX < 0 || checkX >= ThreatCell.GridWidth) continue;

                var checkCell = new ThreatCell(checkX, checkY);
                Fixed64 threat = GetThreatLevel(table, checkCell);
                if (threat > maxThreat)
                {
                    maxThreat = threat;
                    maxThreatCell = checkCell;
                }
            }
        }

        // Calculate direction toward threat cell
        Fixed64Vec2 direction = Fixed64Vec2.Zero;
        if (maxThreat > Fixed64.Zero && maxThreatCell != center)
        {
            Fixed64Vec2 threatCenter = CellToWorld(maxThreatCell);
            Fixed64 ddx = threatCenter.X - worldPos.X;
            Fixed64 ddy = threatCenter.Y - worldPos.Y;

            Fixed64 magSq = ddx * ddx + ddy * ddy;
            if (magSq > Fixed64.FromFloat(0.001f))
            {
                Fixed64 mag = Fixed64.Sqrt(magSq);
                direction = new Fixed64Vec2(ddx / mag, ddy / mag);
            }
        }

        return (maxThreatCell, maxThreat, direction);
    }
}
