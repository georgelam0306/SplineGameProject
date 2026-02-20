# Algorithms Reference

This document describes the core algorithms used in the simulation.

---

## Flow Field Pathfinding

The pathfinding system uses a hierarchical approach: zone graph for high-level routing, Dijkstra flow fields for local movement.

### Zone Graph Structure

**Sectors**: The world is divided into 32×32 tile sectors. Each sector contains:
- `ZoneIds[]` - list of zone IDs in this sector
- `TileZoneIndices[]` - per-tile mapping to zone index (or -1 for blocked)
- `WallDistances[]` - per-tile distance to nearest wall (Fixed64)

**Zones**: Contiguous walkable regions within a sector, identified by flood fill.

**Portals**: Connections between adjacent zones at sector edges. A portal spans consecutive walkable tiles along a sector boundary.

### Zone Detection Algorithm

```
BuildSectorZonesOnly(sectorX, sectorY):
  1. Compute wall distance field via EDT
  2. Initialize tileZoneIndices to -1
  3. For each tile in sector:
     - If already assigned or blocked (wallDistance == 0), skip
     - Create new zone ID
     - Flood fill from this tile, marking all connected walkable tiles
```

### Portal Detection Algorithm

```
DetectPortalsForSector(sector):
  For each edge (left, right, top, bottom):
    Scan along edge tiles
    Track spans of consecutive walkable tiles
    When zone changes or span ends:
      Create portal connecting fromZone to toZone
      Register portal in both zones' portal lists
```

### Zone-Level A* Pathfinding

```
FindZonePath(startZoneId, destZoneId):
  1. A* search through zone graph
  2. Neighbors = zones connected via portals
  3. Heuristic = Manhattan distance between sector coordinates
  4. Build neighbor sectors on-demand during search
  5. Return list of zone IDs from start to destination
```

### Dijkstra Flow Field (Per-Zone)

Flow fields are computed per-zone with costs seeded from:
- **Multi-target flows**: Seeds are attack targets (walls, structures)
- **Single-destination flows**: Seed is the destination tile

```
ComputeZoneFlow(zoneId, seeds):
  1. Initialize distances to MaxDistance
  2. Seed tiles: distance = seedCost, enqueue in priority queue
  3. Seed from downstream zones via portals (for cross-zone routing)
  4. Dijkstra expansion:
     For each tile in priority order:
       For each neighbor (4 cardinal + 4 diagonal):
         If same zone and not blocked:
           newDist = currentDist + moveCost + wallCost
           If newDist < neighborDist:
             Update and enqueue
  5. Compute gradients:
     For each tile:
       gradX = rightDist - leftDist
       gradY = downDist - upDist
       direction = -normalize(grad)
```

**Cost Factors**:
- Cardinal move: 1.0
- Diagonal move: 1.414
- Wall proximity cost: `0.5 / (1 + wallDistance)` - discourages hugging walls

### Flow Field Caching

- **LRU Cache**: Up to 256 cached zone flows
- **Multi-target cache**: Keyed by zone ID
- **Single-dest cache**: Keyed by (zoneId, destTileX, destTileY)
- **Invalidation**: When sectors rebuild, affected zone flows are evicted

---

## Spatial Hashing

### Structure

```
SpatialHashService:
  _chunkEntities[1024][]  - 32×32 grid of entity arrays
  _chunkEntityCounts[1024] - entity count per chunk
  PositionsX[200000]      - X positions indexed by entity ID
  PositionsY[200000]      - Y positions indexed by entity ID
```

**Chunk Size**: `ChunkSize × TileSize` pixels (configurable, typically 512px)

**Grid Layout**: 32×32 chunks centered at origin (-16 to +15 in each axis)

### Operations

**Insert**:
```
InsertEntity(entityId, simX, simY, isAttackTarget, isUnit):
  1. Store position in PositionsX/Y arrays
  2. Compute chunk coords: chunkX = floor(worldX / chunkSizePx)
  3. Compute grid index: gridX + gridY * 32
  4. Append to chunk's entity array
```

**Query**:
```
GetChunkEntities(chunkGridIndex):
  Return span of entities in that chunk
```

**Neighbor Search** (used in combat/separation):
```
For each of 9 chunks (3×3 around entity):
  For each entity in chunk:
    If within radius, process
```

### Region Ownership

Regions define which chunks a parallel worker owns:
- `Region.OwnedChunkGridIndices[]` - array of 9 chunk indices (3×3)
- Multiple regions can exist for multiplayer/parallel processing
- `SpatialHashService.AddOrigin()` marks region centers for LOD calculation

### LOD via Tick Divisor

```
GetTickDivisor(chunkGridIndex):
  distance = min distance to any origin
  return distance switch:
    0 => 1   (every frame)
    1 => 2   (every 2 frames)
    2 => 4   (every 4 frames)
    _ => 8   (every 8 frames)
```

---

## Euclidean Distance Transform (EDT)

Computes per-tile distance to nearest wall/obstacle. Used for:
- Zone detection (blocked = distance 0)
- Wall avoidance in pathfinding
- Wall repulsion in separation

### Algorithm: Felzenszwalb-Huttenlocher

Two-pass separable algorithm:

```
ComputeWallDistanceField(sector):
  1. Initialize grid:
     blocked tile => 0
     open tile => infinity
  
  2. Horizontal pass:
     For each row:
       TransformRow1D(row)
  
  3. Vertical pass:
     For each column:
       TransformRow1D(column)
  
  4. Square root:
     For each cell:
       distance = sqrt(squaredDistance)
```

**TransformRow1D** (Lower Envelope Algorithm):
```
TransformRow1D(values, length):
  1. Build lower envelope of parabolas
     - Each cell i defines parabola: y = (x-i)² + values[i]
  2. Track intersection points between consecutive parabolas
  3. For each output position:
     - Find which parabola is lowest at that x
     - Output squared distance from that parabola
```

**Complexity**: O(n) per row/column, O(n²) total for n×n grid.

---

## Unit Separation (Flocking)

Prevents units from overlapping when idle or moving.

### Local Spatial Grid

Within each chunk, build a finer 32×32 cell grid (cell size = 32px):

```
ProcessChunk(chunkGridIndex):
  1. Allocate cell heads array [1024], init to -1
  2. Allocate next-in-cell array [entityCount]
  
  3. Insert entities into cells:
     For each entity:
       cellX = (entityX - chunkBaseX) / cellSize
       cellY = (entityY - chunkBaseY) / cellSize
       cellIndex = cellX + cellY * 32
       nextInCell[entityIndex] = cellHeads[cellIndex]
       cellHeads[cellIndex] = entityIndex
```

### Separation Force Calculation

```
For each entity (staggered by frame):
  separationX, separationY = 0
  
  For each cell in 3×3 neighborhood:
    For each other entity in cell (via linked list):
      diff = myPos - otherPos
      distSq = diff.lengthSq
      
      If distSq < separationRadiusSq and distSq > minDistSq:
        weight = (radiusSq - distSq) / (clampedDistSq * radiusSq)
        separation += diff * weight
        separation = clamp(separation, -maxForce, maxForce)
```

### Wall Repulsion

```
wallDistance = zoneGraph.GetWallDistance(tileX, tileY)
If wallDistance > 0 and wallDistance < threshold:
  gradient = (rightDist - leftDist, downDist - upDist)
  repulsion = normalize(gradient) * (threshold - wallDistance) / threshold
```

### Frame Staggering

To reduce per-frame cost:
- `FrameSpread = 4`
- Each frame processes entities where `(entityIndex + frameOffset) % 4 == 0`
- Spreads separation computation across 4 frames

---

## Fixed64 Arithmetic

48.16 fixed-point arithmetic for deterministic math.

### Representation

```csharp
struct Fixed64 {
    long _raw;  // 48 bits integer, 16 bits fractional
}

FractionalBits = 16
One = 1L << 16 = 65536
```

### Range

- Integer range: approximately ±140 trillion
- Fractional precision: 1/65536 ≈ 0.000015

### Operations

**Conversion**:
```csharp
FromInt(x)   => x << 16
FromFloat(x) => (long)(x * 65536)
ToFloat()    => _raw / 65536.0f
ToInt()      => _raw >> 16
```

**Arithmetic**:
```csharp
a + b => new Fixed64(a._raw + b._raw)  // with overflow check
a - b => new Fixed64(a._raw - b._raw)  // with overflow check
a * b => new Fixed64((a._raw * b._raw) >> 16)
a / b => new Fixed64((a._raw << 16) / b._raw)
```

**Overflow Handling**:
- Addition/subtraction check for sign overflow
- Return MaxValue or MinValue on overflow
- Multiplication checks for overflow before computing

### Square Root (Integer Algorithm)

```csharp
Sqrt(value):
  num = value._raw << 16  // Scale up for precision
  result = 0
  bit = 1L << 62
  
  // Find starting bit
  while (bit > num) bit >>= 2
  
  // Binary search
  while (bit != 0):
    if num >= result + bit:
      num -= result + bit
      result = (result >> 1) + bit
    else:
      result >>= 1
    bit >>= 2
  
  return new Fixed64(result)
```

### Determinism Guarantees

- No floating-point operations in simulation
- All math uses integer operations
- Overflow behavior is deterministic (saturates to min/max)
- Same input produces same output on all platforms

