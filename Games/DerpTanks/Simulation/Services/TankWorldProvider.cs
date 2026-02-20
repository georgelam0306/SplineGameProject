using FlowField;
using FixedMath;

namespace DerpTanks.Simulation.Services;

public sealed class TankWorldProvider : IWorldProvider
{
    public const int TileSizeValue = 32;

    public const int ArenaMin = -400;

    public const int ArenaMax = 400;

    public int TileSize => TileSizeValue;

    public bool IsBlocked(Fixed64 worldX, Fixed64 worldY)
    {
        int x = worldX.ToInt();
        int y = worldY.ToInt();
        return x < ArenaMin || x > ArenaMax || y < ArenaMin || y > ArenaMax;
    }

    public bool IsBlockedByTerrain(Fixed64 worldX, Fixed64 worldY)
    {
        return IsBlocked(worldX, worldY);
    }
}
