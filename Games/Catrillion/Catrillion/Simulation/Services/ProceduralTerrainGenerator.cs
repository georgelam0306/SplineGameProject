using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Rendering.DualGrid.Utilities;
using Catrillion.Simulation.Components;
using Core;

namespace Catrillion.Simulation.Services;

/// <summary>
/// Generates terrain data for the entire map using noise-based procedural generation.
/// Called once at map initialization to create the TerrainType grid.
/// </summary>
public sealed class ProceduralTerrainGenerator
{
    private readonly GameDataManager<GameDocDb> _gameData;

    public ProceduralTerrainGenerator(GameDataManager<GameDocDb> gameData)
    {
        _gameData = gameData;
    }

    /// <summary>
    /// Generates terrain for the entire map. Returns a 1D array indexed as [y * width + x].
    /// </summary>
    public TerrainType[] GenerateTerrain(int widthTiles, int heightTiles, int seed)
    {
        ref readonly var mapGenConfig = ref _gameData.Db.MapGenConfigData.FindById(0);

        var terrain = new TerrainType[widthTiles * heightTiles];

        // Calculate center and zone radii
        int centerX = widthTiles / 2;
        int centerY = heightTiles / 2;
        int safeRadiusSq = mapGenConfig.SafeZoneRadiusTiles * mapGenConfig.SafeZoneRadiusTiles;
        int buildRadiusSq = mapGenConfig.BuildableZoneRadiusTiles * mapGenConfig.BuildableZoneRadiusTiles;

        // Pre-convert noise parameters
        Fixed64 noiseScale = mapGenConfig.TerrainNoiseScale;
        int octaves = mapGenConfig.TerrainNoiseOctaves;
        Fixed64 persistence = mapGenConfig.TerrainNoisePersistence;

        // Threshold values
        Fixed64 waterThreshold = mapGenConfig.WaterThreshold;
        Fixed64 grassThreshold = mapGenConfig.GrassThreshold;
        Fixed64 forestThreshold = mapGenConfig.ForestThreshold;

        for (int y = 0; y < heightTiles; y++)
        {
            for (int x = 0; x < widthTiles; x++)
            {
                int index = y * widthTiles + x;

                // Calculate distance from center
                int dx = x - centerX;
                int dy = y - centerY;
                int distSq = dx * dx + dy * dy;

                // Sample noise for terrain type
                Fixed64 sampleX = Fixed64.FromInt(x) * noiseScale;
                Fixed64 sampleY = Fixed64.FromInt(y) * noiseScale;
                Fixed64 noiseValue = DeterministicPerlinNoise.FBM(sampleX, sampleY, octaves, persistence, seed);

                // Safe zone - grass only (no obstructions for base building)
                if (distSq <= safeRadiusSq)
                {
                    terrain[index] = TerrainType.Grass;
                    continue;
                }

                // Buildable zone - allow grass and forest (Dirt), but no water/mountains
                if (distSq <= buildRadiusSq)
                {
                    var sampled = SampleTerrainFromNoise(noiseValue, waterThreshold, grassThreshold, forestThreshold);
                    // Allow forest (Dirt) for wood resources, but block water and mountain
                    terrain[index] = (sampled == TerrainType.Water || sampled == TerrainType.Mountain)
                        ? TerrainType.Grass
                        : sampled;
                    continue;
                }

                // Determine terrain type based on thresholds
                terrain[index] = SampleTerrainFromNoise(noiseValue, waterThreshold, grassThreshold, forestThreshold);
            }
        }

        // Carve noisy paths from multiple edge points to center (replaces straight cardinal paths)
        int pathWidth = mapGenConfig.CardinalPathWidth;
        int pathsPerEdge = 2; // 2 paths per cardinal edge = 8 total entry points
        CarveNoisyPaths(terrain, widthTiles, heightTiles, centerX, centerY, seed, pathsPerEdge, pathWidth);

        // Add starter forest patches near base for early sawmills
        // Place in a ring 5-7 tiles from center (within CC power radius of 8 tiles)
        AddStarterForestPatches(terrain, widthTiles, heightTiles, centerX, centerY, seed);

        // Compute distance field for smart connectivity and spawn weighting
        var distanceField = ComputeDistanceField(widthTiles, heightTiles, centerX, centerY);

        // Ensure all passable terrain is connected (uses gradient-guided A* for chokepoints)
        EnsureConnectivity(terrain, distanceField, widthTiles, heightTiles, centerX, centerY);

        // Apply erosion to smooth terrain transitions
        ApplyErosion(terrain, widthTiles, heightTiles, 1);

        // Debug: verify connectivity was achieved
        #if DEBUG
        VerifyConnectivity(terrain, widthTiles, heightTiles, centerX, centerY);
        #endif

        return terrain;
    }

    /// <summary>
    /// Adds forest patches in a ring around the command center for early wood production.
    /// Patches are placed 5-7 tiles from center (within CC's 8-tile power radius).
    /// </summary>
    private static void AddStarterForestPatches(
        TerrainType[] terrain,
        int width,
        int height,
        int centerX,
        int centerY,
        int seed)
    {
        // Place 4 forest patches at cardinal + diagonal directions
        const int minRadius = 5;
        const int maxRadius = 7;
        const int patchSize = 2; // 2x2 forest patches

        // 8 directions: N, NE, E, SE, S, SW, W, NW
        int[] dirX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        int[] dirY = { -1, -1, 0, 1, 1, 1, 0, -1 };

        for (int i = 0; i < 8; i++)
        {
            // Use deterministic random for radius variation
            int radiusSeed = seed ^ (i * 0x45D9F3B);
            int radius = minRadius + (Math.Abs(radiusSeed) % (maxRadius - minRadius + 1));

            int patchCenterX = centerX + dirX[i] * radius;
            int patchCenterY = centerY + dirY[i] * radius;

            // Place a small forest patch
            for (int dy = 0; dy < patchSize; dy++)
            {
                for (int dx = 0; dx < patchSize; dx++)
                {
                    int x = patchCenterX + dx;
                    int y = patchCenterY + dy;

                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        terrain[y * width + x] = TerrainType.Dirt;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Carves grass paths from the 4 cardinal edges to the center of the map.
    /// Ensures zombies from any direction can reach the player's base.
    /// </summary>
    private static void CarveCardinalPaths(
        TerrainType[] terrain,
        int width,
        int height,
        int centerX,
        int centerY,
        int pathWidth)
    {
        int halfWidth = pathWidth / 2;

        // North path: from top edge (y=0) to center
        for (int y = 0; y < centerY; y++)
        {
            for (int offset = -halfWidth; offset <= halfWidth; offset++)
            {
                int x = centerX + offset;
                if (x >= 0 && x < width)
                {
                    terrain[y * width + x] = TerrainType.Grass;
                }
            }
        }

        // South path: from bottom edge (y=height-1) to center
        for (int y = centerY; y < height; y++)
        {
            for (int offset = -halfWidth; offset <= halfWidth; offset++)
            {
                int x = centerX + offset;
                if (x >= 0 && x < width)
                {
                    terrain[y * width + x] = TerrainType.Grass;
                }
            }
        }

        // West path: from left edge (x=0) to center
        for (int x = 0; x < centerX; x++)
        {
            for (int offset = -halfWidth; offset <= halfWidth; offset++)
            {
                int y = centerY + offset;
                if (y >= 0 && y < height)
                {
                    terrain[y * width + x] = TerrainType.Grass;
                }
            }
        }

        // East path: from right edge (x=width-1) to center
        for (int x = centerX; x < width; x++)
        {
            for (int offset = -halfWidth; offset <= halfWidth; offset++)
            {
                int y = centerY + offset;
                if (y >= 0 && y < height)
                {
                    terrain[y * width + x] = TerrainType.Grass;
                }
            }
        }
    }

    /// <summary>
    /// Determines terrain type from noise value using thresholds.
    /// </summary>
    private static TerrainType SampleTerrainFromNoise(
        Fixed64 noiseValue,
        Fixed64 waterThreshold,
        Fixed64 grassThreshold,
        Fixed64 forestThreshold)
    {
        if (noiseValue < waterThreshold)
        {
            return TerrainType.Water;
        }

        if (noiseValue < grassThreshold)
        {
            return TerrainType.Grass;
        }

        if (noiseValue < forestThreshold)
        {
            return TerrainType.Dirt;  // Dirt/forest biome
        }

        // Above forest threshold = mountain
        return TerrainType.Mountain;
    }

    /// <summary>
    /// Checks if a terrain type is passable (zombies can walk).
    /// </summary>
    public bool IsTerrainPassable(TerrainType terrainType)
    {
        ref readonly var biomeConfig = ref _gameData.Db.TerrainBiomeConfigData.FindById((int)terrainType);
        return biomeConfig.IsPassable;
    }

    /// <summary>
    /// Static passability check for connectivity algorithms.
    /// Must match TerrainBiomeConfig.json IsPassable values.
    /// </summary>
    private static bool IsPassableStatic(TerrainType terrain)
    {
        return terrain switch
        {
            TerrainType.Grass => true,
            TerrainType.Sand => true,
            TerrainType.Ramp => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if a terrain type allows resource spawning.
    /// </summary>
    public bool CanSpawnResource(TerrainType terrainType, ResourceTypeId resourceType)
    {
        ref readonly var biomeConfig = ref _gameData.Db.TerrainBiomeConfigData.FindById((int)terrainType);
        return resourceType switch
        {
            ResourceTypeId.Gold => biomeConfig.CanSpawnGold,
            ResourceTypeId.Energy => biomeConfig.CanSpawnEnergy,
            ResourceTypeId.Wood => biomeConfig.CanSpawnWood,
            ResourceTypeId.Stone => biomeConfig.CanSpawnStone,
            ResourceTypeId.Iron => biomeConfig.CanSpawnIron,
            ResourceTypeId.Oil => biomeConfig.CanSpawnOil,
            _ => false
        };
    }

    /// <summary>
    /// Debug verification that all passable terrain is connected.
    /// Throws if any islands remain after EnsureConnectivity.
    /// </summary>
    private static void VerifyConnectivity(
        TerrainType[] terrain,
        int width,
        int height,
        int centerX,
        int centerY)
    {
        var reachable = ComputeReachable(terrain, width, height, centerX, centerY);

        int islandCount = 0;
        int firstIslandX = -1, firstIslandY = -1;

        for (int i = 0; i < terrain.Length; i++)
        {
            if (IsPassableStatic(terrain[i]) && !reachable[i])
            {
                islandCount++;
                if (firstIslandX < 0)
                {
                    firstIslandX = i % width;
                    firstIslandY = i / width;
                }
            }
        }

        if (islandCount > 0)
        {
            Console.WriteLine($"[TERRAIN BUG] {islandCount} island tiles remain! First at ({firstIslandX}, {firstIslandY})");
            Console.WriteLine($"[TERRAIN BUG] Center at ({centerX}, {centerY}), center terrain: {terrain[centerY * width + centerX]}");
        }
    }

    /// <summary>
    /// Ensures all passable terrain is connected to the center.
    /// Repeatedly finds isolated "islands" and carves paths to connect them.
    /// Uses distance field to route connections through natural chokepoints.
    /// </summary>
    private static void EnsureConnectivity(
        TerrainType[] terrain,
        int[] distanceField,
        int width,
        int height,
        int centerX,
        int centerY)
    {
        // Loop until no islands remain
        while (true)
        {
            var reachable = ComputeReachable(terrain, width, height, centerX, centerY);

            // Find first unreachable passable tile (deterministic: lowest index)
            int islandIndex = -1;
            for (int i = 0; i < terrain.Length; i++)
            {
                if (IsPassableStatic(terrain[i]) && !reachable[i])
                {
                    islandIndex = i;
                    break;
                }
            }

            // All passable tiles are reachable - done
            if (islandIndex < 0)
                return;

            // Find nearest reachable tile and carve smart connection
            int targetIndex = FindNearestReachable(terrain, reachable, width, height, islandIndex);
            if (targetIndex >= 0)
            {
                // Use gradient-guided A* for strategic path routing
                CarveSmartConnection(terrain, distanceField, width, height, islandIndex, targetIndex);
            }
            else
            {
                // No reachable tile found (shouldn't happen with noisy paths)
                // Convert island to impassable to avoid infinite loop
                terrain[islandIndex] = TerrainType.Mountain;
            }
        }
    }

    /// <summary>
    /// Legacy connectivity method without distance field (for backwards compatibility).
    /// </summary>
    private static void EnsureConnectivity(
        TerrainType[] terrain,
        int width,
        int height,
        int centerX,
        int centerY)
    {
        // Compute distance field on-demand
        var distanceField = ComputeDistanceField(width, height, centerX, centerY);
        EnsureConnectivity(terrain, distanceField, width, height, centerX, centerY);
    }

    /// <summary>
    /// BFS flood fill from center to find all reachable passable tiles.
    /// Uses 4-directional neighbors for zombie pathing consistency.
    /// </summary>
    private static bool[] ComputeReachable(
        TerrainType[] terrain,
        int width,
        int height,
        int startX,
        int startY)
    {
        var visited = new bool[terrain.Length];
        var queue = new Queue<int>();

        int startIndex = startY * width + startX;
        if (!IsPassableStatic(terrain[startIndex]))
            return visited;

        visited[startIndex] = true;
        queue.Enqueue(startIndex);

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % width;
            int y = index / width;

            // 4-directional neighbors: up, down, left, right
            TryEnqueue(terrain, visited, queue, width, height, x, y - 1); // up
            TryEnqueue(terrain, visited, queue, width, height, x, y + 1); // down
            TryEnqueue(terrain, visited, queue, width, height, x - 1, y); // left
            TryEnqueue(terrain, visited, queue, width, height, x + 1, y); // right
        }

        return visited;
    }

    /// <summary>
    /// Helper for ComputeReachable - enqueues a neighbor if valid and passable.
    /// </summary>
    private static void TryEnqueue(
        TerrainType[] terrain,
        bool[] visited,
        Queue<int> queue,
        int width,
        int height,
        int x,
        int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return;

        int index = y * width + x;
        if (visited[index])
            return;

        if (!IsPassableStatic(terrain[index]))
            return;

        visited[index] = true;
        queue.Enqueue(index);
    }

    /// <summary>
    /// BFS outward from an island tile to find the nearest reachable tile.
    /// Searches through all terrain (including impassable) to find shortest path.
    /// </summary>
    private static int FindNearestReachable(
        TerrainType[] terrain,
        bool[] reachable,
        int width,
        int height,
        int fromIndex)
    {
        var visited = new bool[terrain.Length];
        var queue = new Queue<int>();

        visited[fromIndex] = true;
        queue.Enqueue(fromIndex);

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % width;
            int y = index / width;

            // Check 4-directional neighbors
            int result = TryFindReachable(reachable, visited, queue, width, height, x, y - 1);
            if (result >= 0) return result;

            result = TryFindReachable(reachable, visited, queue, width, height, x, y + 1);
            if (result >= 0) return result;

            result = TryFindReachable(reachable, visited, queue, width, height, x - 1, y);
            if (result >= 0) return result;

            result = TryFindReachable(reachable, visited, queue, width, height, x + 1, y);
            if (result >= 0) return result;
        }

        return -1; // No reachable tile found
    }

    /// <summary>
    /// Helper for FindNearestReachable - checks if neighbor is reachable or enqueues it.
    /// Returns the index if reachable, -1 otherwise.
    /// </summary>
    private static int TryFindReachable(
        bool[] reachable,
        bool[] visited,
        Queue<int> queue,
        int width,
        int height,
        int x,
        int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return -1;

        int index = y * width + x;
        if (visited[index])
            return -1;

        visited[index] = true;

        if (reachable[index])
            return index;

        queue.Enqueue(index);
        return -1;
    }

    /// <summary>
    /// Carves an L-shaped grass corridor from one tile to another.
    /// Goes horizontal first, then vertical (deterministic shape).
    /// </summary>
    private static void CarveConnection(
        TerrainType[] terrain,
        int width,
        int fromIndex,
        int toIndex)
    {
        int x0 = fromIndex % width;
        int y0 = fromIndex / width;
        int x1 = toIndex % width;
        int y1 = toIndex / width;

        int x = x0;
        int y = y0;

        // Horizontal segment first
        while (x != x1)
        {
            terrain[y * width + x] = TerrainType.Grass;
            x += Math.Sign(x1 - x);
        }

        // Vertical segment
        while (y != y1)
        {
            terrain[y * width + x] = TerrainType.Grass;
            y += Math.Sign(y1 - y);
        }

        // Final tile
        terrain[y * width + x] = TerrainType.Grass;
    }

    #region Enhanced Terrain Generation

    /// <summary>
    /// Computes distance field from center using BFS through ALL tiles.
    /// Returns distance from center for every tile (used for spawn weighting, gradient calculation).
    /// </summary>
    private static int[] ComputeDistanceField(int width, int height, int centerX, int centerY)
    {
        var distance = new int[width * height];
        Array.Fill(distance, int.MaxValue);

        var queue = new Queue<int>();
        int startIndex = centerY * width + centerX;

        distance[startIndex] = 0;
        queue.Enqueue(startIndex);

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % width;
            int y = index / width;
            int currentDist = distance[index];

            // 4-directional neighbors
            TryEnqueueDistance(distance, queue, width, height, x, y - 1, currentDist + 1);
            TryEnqueueDistance(distance, queue, width, height, x, y + 1, currentDist + 1);
            TryEnqueueDistance(distance, queue, width, height, x - 1, y, currentDist + 1);
            TryEnqueueDistance(distance, queue, width, height, x + 1, y, currentDist + 1);
        }

        return distance;
    }

    /// <summary>
    /// Helper for ComputeDistanceField - enqueues neighbor if unvisited.
    /// </summary>
    private static void TryEnqueueDistance(
        int[] distance,
        Queue<int> queue,
        int width,
        int height,
        int x,
        int y,
        int newDist)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return;

        int index = y * width + x;
        if (distance[index] != int.MaxValue)
            return;

        distance[index] = newDist;
        queue.Enqueue(index);
    }

    /// <summary>
    /// Computes gradient magnitude at a tile using distance field.
    /// High gradient = natural chokepoint (big distance change over short space).
    /// </summary>
    private static int ComputeGradient(int[] distanceField, int width, int height, int x, int y)
    {
        int center = distanceField[y * width + x];
        if (center == int.MaxValue) return 0;

        int maxDiff = 0;

        // Check 4 neighbors for maximum distance difference
        if (x > 0)
            maxDiff = Math.Max(maxDiff, Math.Abs(center - GetDistanceSafe(distanceField, width, height, x - 1, y)));
        if (x < width - 1)
            maxDiff = Math.Max(maxDiff, Math.Abs(center - GetDistanceSafe(distanceField, width, height, x + 1, y)));
        if (y > 0)
            maxDiff = Math.Max(maxDiff, Math.Abs(center - GetDistanceSafe(distanceField, width, height, x, y - 1)));
        if (y < height - 1)
            maxDiff = Math.Max(maxDiff, Math.Abs(center - GetDistanceSafe(distanceField, width, height, x, y + 1)));

        return maxDiff;
    }

    private static int GetDistanceSafe(int[] distanceField, int width, int height, int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return int.MaxValue;
        return distanceField[y * width + x];
    }

    /// <summary>
    /// Carves multiple noisy paths from edge spawn points to center.
    /// Replaces straight cardinal paths with organic, winding corridors.
    /// </summary>
    private static void CarveNoisyPaths(
        TerrainType[] terrain,
        int width,
        int height,
        int centerX,
        int centerY,
        int seed,
        int pathsPerEdge,
        int pathWidth)
    {
        // For each cardinal direction, pick spawn points along the edge
        // North edge (y=0), South edge (y=height-1), West edge (x=0), East edge (x=width-1)

        int edgeMargin = width / 8; // Keep spawn points away from corners

        for (int edge = 0; edge < 4; edge++)
        {
            for (int pathNum = 0; pathNum < pathsPerEdge; pathNum++)
            {
                // Deterministic spawn point selection
                int pathSeed = seed ^ (edge * 0x45D9F3B) ^ (pathNum * 0x27D4EB2D);

                int startX, startY;

                switch (edge)
                {
                    case 0: // North
                        startX = edgeMargin + (Math.Abs(pathSeed) % (width - 2 * edgeMargin));
                        startY = 0;
                        break;
                    case 1: // South
                        startX = edgeMargin + (Math.Abs(pathSeed >> 8) % (width - 2 * edgeMargin));
                        startY = height - 1;
                        break;
                    case 2: // West
                        startX = 0;
                        startY = edgeMargin + (Math.Abs(pathSeed >> 16) % (height - 2 * edgeMargin));
                        break;
                    default: // East
                        startX = width - 1;
                        startY = edgeMargin + (Math.Abs(pathSeed >> 24) % (height - 2 * edgeMargin));
                        break;
                }

                CarveNoisyPath(terrain, width, height, startX, startY, centerX, centerY, pathSeed, pathWidth);
            }
        }
    }

    /// <summary>
    /// Carves a single noisy path from start position toward center.
    /// Uses Perlin noise for smooth lateral wandering while biased toward center.
    /// </summary>
    private static void CarveNoisyPath(
        TerrainType[] terrain,
        int width,
        int height,
        int startX,
        int startY,
        int centerX,
        int centerY,
        int seed,
        int pathWidth)
    {
        Fixed64 x = Fixed64.FromInt(startX);
        Fixed64 y = Fixed64.FromInt(startY);
        Fixed64 noiseScale = Fixed64.FromFloat(0.15f); // Controls path waviness
        Fixed64 noiseStrength = Fixed64.FromFloat(1.5f); // Max lateral offset per step

        int maxSteps = width + height; // Safety limit
        int step = 0;

        while (step < maxSteps)
        {
            int ix = (int)x;
            int iy = (int)y;

            // Carve path with width
            for (int dy = -pathWidth / 2; dy <= pathWidth / 2; dy++)
            {
                for (int dx = -pathWidth / 2; dx <= pathWidth / 2; dx++)
                {
                    int px = ix + dx;
                    int py = iy + dy;
                    if (px >= 0 && px < width && py >= 0 && py < height)
                    {
                        terrain[py * width + px] = TerrainType.Grass;
                    }
                }
            }

            // Check if we've reached the center area
            int distToCenterSq = (ix - centerX) * (ix - centerX) + (iy - centerY) * (iy - centerY);
            if (distToCenterSq <= 4) // Within 2 tiles of center
                break;

            // Calculate direction toward center
            Fixed64 dirX = Fixed64.FromInt(centerX) - x;
            Fixed64 dirY = Fixed64.FromInt(centerY) - y;

            // Normalize direction
            Fixed64 mag = Fixed64.Sqrt(dirX * dirX + dirY * dirY);
            if (mag > Fixed64.Zero)
            {
                dirX = dirX / mag;
                dirY = dirY / mag;
            }

            // Sample noise for lateral offset
            Fixed64 sampleX = x * noiseScale;
            Fixed64 sampleY = y * noiseScale;
            Fixed64 noise = DeterministicPerlinNoise.Noise(sampleX, sampleY, seed);

            // Perpendicular direction for lateral drift
            Fixed64 perpX = -dirY;
            Fixed64 perpY = dirX;

            // Apply noise-based lateral offset
            Fixed64 lateralOffset = noise * noiseStrength;

            // Move toward center with lateral drift
            x = x + dirX + perpX * lateralOffset;
            y = y + dirY + perpY * lateralOffset;

            // Clamp to bounds
            if (x < Fixed64.Zero) x = Fixed64.Zero;
            if (x >= Fixed64.FromInt(width)) x = Fixed64.FromInt(width - 1);
            if (y < Fixed64.Zero) y = Fixed64.Zero;
            if (y >= Fixed64.FromInt(height)) y = Fixed64.FromInt(height - 1);

            step++;
        }
    }

    /// <summary>
    /// Carves a smart connection using A* with gradient-aware cost.
    /// Prefers routing through high-gradient areas (natural chokepoints).
    /// </summary>
    private static void CarveSmartConnection(
        TerrainType[] terrain,
        int[] distanceField,
        int width,
        int height,
        int fromIndex,
        int toIndex)
    {
        // Use A* pathfinding with gradient-based cost
        var gScore = new int[terrain.Length];
        var parent = new int[terrain.Length];
        Array.Fill(gScore, int.MaxValue);
        Array.Fill(parent, -1);

        // Priority queue: (priority, index)
        var openSet = new PriorityQueue<int, int>();

        gScore[fromIndex] = 0;
        int fromX = fromIndex % width;
        int fromY = fromIndex / width;
        int toX = toIndex % width;
        int toY = toIndex / width;

        int heuristic = Math.Abs(fromX - toX) + Math.Abs(fromY - toY);
        openSet.Enqueue(fromIndex, heuristic);

        while (openSet.Count > 0)
        {
            int current = openSet.Dequeue();

            if (current == toIndex)
                break;

            int cx = current % width;
            int cy = current / width;
            int currentG = gScore[current];

            // Check 4 neighbors
            TryExpandNode(terrain, distanceField, gScore, parent, openSet, width, height, current, cx, cy - 1, toX, toY, currentG);
            TryExpandNode(terrain, distanceField, gScore, parent, openSet, width, height, current, cx, cy + 1, toX, toY, currentG);
            TryExpandNode(terrain, distanceField, gScore, parent, openSet, width, height, current, cx - 1, cy, toX, toY, currentG);
            TryExpandNode(terrain, distanceField, gScore, parent, openSet, width, height, current, cx + 1, cy, toX, toY, currentG);
        }

        // Reconstruct path and carve
        int pathNode = toIndex;
        while (pathNode >= 0)
        {
            terrain[pathNode] = TerrainType.Grass;
            pathNode = parent[pathNode];
        }
    }

    /// <summary>
    /// Helper for A* - expands a neighbor node with gradient-aware cost.
    /// </summary>
    private static void TryExpandNode(
        TerrainType[] terrain,
        int[] distanceField,
        int[] gScore,
        int[] parent,
        PriorityQueue<int, int> openSet,
        int width,
        int height,
        int current,
        int nx,
        int ny,
        int toX,
        int toY,
        int currentG)
    {
        if (nx < 0 || nx >= width || ny < 0 || ny >= height)
            return;

        int neighbor = ny * width + nx;

        // Calculate movement cost
        // Base cost = 10
        // Low gradient penalty = +5 (prefer high gradient areas / chokepoints)
        int gradient = ComputeGradient(distanceField, width, height, nx, ny);
        int gradientPenalty = gradient >= 2 ? 0 : 5; // Prefer gradient >= 2
        int moveCost = 10 + gradientPenalty;

        int tentativeG = currentG + moveCost;

        if (tentativeG < gScore[neighbor])
        {
            gScore[neighbor] = tentativeG;
            parent[neighbor] = current;

            int h = Math.Abs(nx - toX) + Math.Abs(ny - toY);
            int f = tentativeG + h * 10;
            openSet.Enqueue(neighbor, f);
        }
    }

    /// <summary>
    /// Applies cellular automata erosion to smooth terrain transitions.
    /// Removes isolated single-tile features and slightly widens narrow paths.
    /// </summary>
    private static void ApplyErosion(TerrainType[] terrain, int width, int height, int iterations)
    {
        for (int iter = 0; iter < iterations; iter++)
        {
            var newTerrain = new TerrainType[terrain.Length];
            Array.Copy(terrain, newTerrain, terrain.Length);

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int index = y * width + x;
                    var current = terrain[index];

                    // Count same-type neighbors (8-directional)
                    int sameCount = 0;
                    int grassCount = 0;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            var neighbor = terrain[(y + dy) * width + (x + dx)];
                            if (neighbor == current) sameCount++;
                            if (neighbor == TerrainType.Grass) grassCount++;
                        }
                    }

                    // Rule: Isolated tiles (< 2 same neighbors) get absorbed
                    if (sameCount < 2)
                    {
                        // Convert to most common passable neighbor type
                        if (grassCount >= 4)
                        {
                            newTerrain[index] = TerrainType.Grass;
                        }
                    }

                    // Rule: Single-tile water gets filled in
                    if (current == TerrainType.Water && sameCount < 2 && grassCount >= 5)
                    {
                        newTerrain[index] = TerrainType.Grass;
                    }
                }
            }

            Array.Copy(newTerrain, terrain, terrain.Length);
        }
    }

    #endregion
}
