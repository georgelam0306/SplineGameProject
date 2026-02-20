using Friflo.Engine.ECS;

namespace Catrillion.Rendering.DualGrid.Components;

public struct DualGridData : IComponent
{
    public int Width;
    public int Height;

    public int[]? GrassCornerTileIndices;
    public float[]? GrassCornerRotations;

    public int[]? DirtCornerTileIndices;
    public float[]? DirtCornerRotations;

    public int[]? WaterCornerTileIndices;
    public float[]? WaterCornerRotations;

    public int[]? MountainCornerTileIndices;
    public float[]? MountainCornerRotations;
}

