namespace Catrillion.Rendering.DualGrid.Utilities;

public static class DualGridLookup
{
    public readonly struct CornerTile
    {
        public readonly int AtlasIndex;
        public readonly float RotationDegrees;

        public CornerTile(int atlasIndex, float rotationDegrees)
        {
            AtlasIndex = atlasIndex;
            RotationDegrees = rotationDegrees;
        }
    }

    public static CornerTile GetCornerTileForTerrain(TerrainType owner, TerrainType topLeft, TerrainType topRight, TerrainType bottomLeft, TerrainType bottomRight)
    {
        if (owner == TerrainType.None)
        {
            return new CornerTile(-1, 0f);
        }

        bool topLeftInside = topLeft == owner;
        bool topRightInside = topRight == owner;
        bool bottomLeftInside = bottomLeft == owner;
        bool bottomRightInside = bottomRight == owner;

        int mask = (topLeftInside ? 1 << 3 : 0)
                 | (topRightInside ? 1 << 2 : 0)
                 | (bottomLeftInside ? 1 << 1 : 0)
                 | (bottomRightInside ? 1 : 0);

        return mask switch
        {
            0b0000 => new CornerTile(-1, 0f),

            0b0001 => new CornerTile(1, 0f),
            0b0010 => new CornerTile(1, 90f),
            0b0100 => new CornerTile(1, 270f),
            0b1000 => new CornerTile(1, 180f),

            0b0011 => new CornerTile(2, 180f),
            0b1100 => new CornerTile(2, 0f),
            0b1010 => new CornerTile(2, 270f),
            0b0101 => new CornerTile(2, 90f),

            0b1001 => new CornerTile(3, 0f),
            0b0110 => new CornerTile(3, 90f),

            0b1110 => new CornerTile(4, 270f),
            0b1101 => new CornerTile(4, 0f),
            0b1011 => new CornerTile(4, 180f),
            0b0111 => new CornerTile(4, 90f),

            0b1111 => new CornerTile(0, 0f),

            _ => new CornerTile(-1, 0f),
        };
    }

    public static TerrainType[] OrderedTerrains { get; } = new[]
    {
        TerrainType.Water,
        TerrainType.Dirt,
        TerrainType.Grass,
        TerrainType.Mountain,
    };
}

