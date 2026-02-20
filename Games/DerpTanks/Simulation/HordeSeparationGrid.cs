namespace DerpTanks.Simulation;

public static class HordeSeparationGrid
{
    public const int GridSize = 256;

    public const int CellSize = 4;

    public const int TotalCells = GridSize * GridSize;

    // Keep symmetric around 0 so (0,0) sits near the center of the field.
    // This avoids clamping most of the arena into cell (0,0) when world coordinates are negative.
    public const int OriginX = -(GridSize * CellSize) / 2;

    public const int OriginY = -(GridSize * CellSize) / 2;

    public static int GetCellIndex(FixedMath.Fixed64Vec2 worldPos)
    {
        int cellX = GetCellX(worldPos);
        int cellY = GetCellY(worldPos);
        return cellX + cellY * GridSize;
    }

    public static int GetInnerCellIndex(FixedMath.Fixed64Vec2 worldPos)
    {
        int cellX = GetCellXUnclamped(worldPos);
        int cellY = GetCellYUnclamped(worldPos);

        cellX = System.Math.Clamp(cellX, 1, GridSize - 2);
        cellY = System.Math.Clamp(cellY, 1, GridSize - 2);
        return cellX + cellY * GridSize;
    }

    public static int GetCellX(FixedMath.Fixed64Vec2 worldPos)
    {
        int cellX = GetCellXUnclamped(worldPos);
        return System.Math.Clamp(cellX, 0, GridSize - 1);
    }

    public static int GetCellY(FixedMath.Fixed64Vec2 worldPos)
    {
        int cellY = GetCellYUnclamped(worldPos);
        return System.Math.Clamp(cellY, 0, GridSize - 1);
    }

    public static int GetCellWorldX(int cellX) => OriginX + cellX * CellSize;

    public static int GetCellWorldY(int cellY) => OriginY + cellY * CellSize;

    private static int GetCellXUnclamped(FixedMath.Fixed64Vec2 worldPos)
    {
        int posX = worldPos.X.ToInt();
        return (posX - OriginX) / CellSize;
    }

    private static int GetCellYUnclamped(FixedMath.Fixed64Vec2 worldPos)
    {
        int posY = worldPos.Y.ToInt();
        return (posY - OriginY) / CellSize;
    }
}
