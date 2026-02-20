using System;
using Catrillion.Rendering.DualGrid.Utilities;

namespace Catrillion.Rendering.DualGrid;

public static class DualGridCalculator
{
    public static void CalculateDualGrid(
        TerrainType[] terrain,
        int mapWidth,
        int mapHeight,
        out int[] grassTiles,
        out float[] grassRot,
        out int[] dirtTiles,
        out float[] dirtRot,
        out int[] waterTiles,
        out float[] waterRot,
        out int[] mountainTiles,
        out float[] mountainRot)
    {
        CalculateDualGrid(
            (x, y) =>
            {
                if (x < 0 || y < 0 || x >= mapWidth || y >= mapHeight)
                {
                    return TerrainType.None;
                }
                return terrain[x + y * mapWidth];
            },
            mapWidth,
            mapHeight,
            out grassTiles,
            out grassRot,
            out dirtTiles,
            out dirtRot,
            out waterTiles,
            out waterRot,
            out mountainTiles,
            out mountainRot);
    }

    public static void CalculateDualGrid(
        Func<int, int, TerrainType> getTerrain,
        int mapWidth,
        int mapHeight,
        out int[] grassTiles,
        out float[] grassRot,
        out int[] dirtTiles,
        out float[] dirtRot,
        out int[] waterTiles,
        out float[] waterRot,
        out int[] mountainTiles,
        out float[] mountainRot)
    {
        int dualGridWidth = mapWidth + 1;
        int dualGridHeight = mapHeight + 1;
        int total = dualGridWidth * dualGridHeight;

        grassTiles = new int[total];
        grassRot = new float[total];
        dirtTiles = new int[total];
        dirtRot = new float[total];
        waterTiles = new int[total];
        waterRot = new float[total];
        mountainTiles = new int[total];
        mountainRot = new float[total];

        Array.Fill(grassTiles, -1);
        Array.Fill(dirtTiles, -1);
        Array.Fill(waterTiles, -1);
        Array.Fill(mountainTiles, -1);

        for (int cornerY = 0; cornerY < dualGridHeight; cornerY++)
        {
            for (int cornerX = 0; cornerX < dualGridWidth; cornerX++)
            {
                TerrainType topLeft = getTerrain(cornerX - 1, cornerY - 1);
                TerrainType topRight = getTerrain(cornerX, cornerY - 1);
                TerrainType bottomLeft = getTerrain(cornerX - 1, cornerY);
                TerrainType bottomRight = getTerrain(cornerX, cornerY);

                int linearIndex = cornerX + cornerY * dualGridWidth;

                var waterCorner = DualGridLookup.GetCornerTileForTerrain(TerrainType.Water, topLeft, topRight, bottomLeft, bottomRight);
                waterTiles[linearIndex] = waterCorner.AtlasIndex;
                waterRot[linearIndex] = waterCorner.RotationDegrees;

                var dirtCorner = DualGridLookup.GetCornerTileForTerrain(TerrainType.Dirt, topLeft, topRight, bottomLeft, bottomRight);
                dirtTiles[linearIndex] = dirtCorner.AtlasIndex;
                dirtRot[linearIndex] = dirtCorner.RotationDegrees;

                var grassCorner = DualGridLookup.GetCornerTileForTerrain(TerrainType.Grass, topLeft, topRight, bottomLeft, bottomRight);
                grassTiles[linearIndex] = grassCorner.AtlasIndex;
                grassRot[linearIndex] = grassCorner.RotationDegrees;

                var mountainCorner = DualGridLookup.GetCornerTileForTerrain(TerrainType.Mountain, topLeft, topRight, bottomLeft, bottomRight);
                mountainTiles[linearIndex] = mountainCorner.AtlasIndex;
                mountainRot[linearIndex] = mountainCorner.RotationDegrees;
            }
        }
    }
}

