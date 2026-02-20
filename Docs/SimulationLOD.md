# Simulation LOD Strategy

Deterministic level-of-detail for simulation scaling to 100k entities.

---

## Overview

LOD reduces simulation cost for distant or less important entities while maintaining determinism. All LOD decisions must be based solely on deterministic state (chunk coordinates, frame number, entity ID).

---

## Current Implementation

### Tick Rate Reduction

`SpatialHashService.GetTickDivisor()` returns different update frequencies based on distance from player origin:

| Distance from Origin | Tick Divisor | Update Rate |
|---------------------|--------------|-------------|
| 0 chunks            | 1            | Every frame |
| 1 chunk             | 2            | Every 2 frames |
| 2 chunks            | 4            | Every 4 frames |
| 3+ chunks           | 8            | Every 8 frames |

### Frame Staggering

`UnitIdleSystem` uses `FrameSpread = 4` to process 1/4 of entities each frame:

```csharp
for (int entityIndex = frameOffset; entityIndex < count; entityIndex += FrameSpread)
```

This spreads separation force computation across multiple frames.

---

## Deterministic Frame Assignment

Entities must be assigned to LOD tiers deterministically. The formula:

```csharp
bool shouldUpdateThisFrame = (entityId + frameNumber) % tickDivisor == 0;
```

This ensures:
- Same entity updates on same frames across all clients
- Even distribution of updates across frames
- No random or time-based decisions

For chunk-based LOD:

```csharp
bool shouldProcessChunk = (chunkGridIndex + frameNumber) % tickDivisor == 0;
```

---

## Physics Simplification Tiers

### Tier 0: Full Fidelity (Near)
- Full separation force calculation (all neighbors)
- Per-frame flow field lookups
- Full collision detection
- Distance: 0-1 chunks from origin

### Tier 1: Reduced (Mid)
- Reduced neighbor search (skip every other neighbor)
- Cached flow directions (update every N frames)
- Simplified collision (axis-aligned only)
- Distance: 2-3 chunks from origin

### Tier 2: Coarse (Far)
- Skip separation forces entirely
- Use cached/interpolated movement
- No local collision (rely on flow field)
- Distance: 4+ chunks from origin

### Tier 3: Aggregate (Very Far)
- Group entities into "blobs"
- Single position represents N entities
- Re-expand when entering higher LOD tier
- Distance: 6+ chunks from origin

---

## Entity Aggregation

For very distant entities, aggregate into group representations:

### Aggregation Process

```
When chunk enters Tier 3:
  1. Group units by destination/behavior
  2. Create aggregate entity with:
     - Center position = average of group
     - Unit count
     - Movement direction
     - Aggregate health
  3. Store original entity IDs for expansion
  4. Deactivate individual entities
```

### Expansion Process

```
When chunk leaves Tier 3:
  1. Retrieve stored entity IDs
  2. Distribute positions around aggregate center
  3. Restore individual entity state
  4. Delete aggregate entity
```

### Determinism Requirements

- Aggregation criteria must be deterministic (same entities always group together)
- Position distribution on expansion must be deterministic (seeded by aggregate ID)
- Group membership stored as sorted entity ID list

---

## LOD Transitions

### Hysteresis

Prevent thrashing when entities are near LOD boundaries:

```csharp
const int TransitionInDistance = 3;   // Enter higher LOD at 3 chunks
const int TransitionOutDistance = 4;  // Exit to lower LOD at 4 chunks

int currentLOD = entity.LODTier;
int distance = GetDistanceToOrigin(entity);

if (currentLOD == 1 && distance <= TransitionInDistance)
    entity.LODTier = 0;
else if (currentLOD == 0 && distance >= TransitionOutDistance)
    entity.LODTier = 1;
```

### Smooth Transitions

When transitioning between tiers:
1. Interpolate velocities over several frames
2. Gradually enable/disable physics features
3. Avoid sudden visual jumps

---

## Determinism Constraints

### Allowed Inputs for LOD Decisions

- Entity ID (int)
- Chunk coordinates (int, int)
- Frame number (int)
- Fixed64 positions
- Deterministic distance calculations

### Forbidden Inputs

- Wall clock time
- Random numbers (unless seeded deterministically)
- Floating-point calculations
- Hash table iteration order
- Thread IDs or execution order

### Verification

LOD tier assignment can be verified by:
```csharp
int ComputeLODTier(int entityId, int chunkX, int chunkY, int frameNumber) {
    // Pure function of deterministic inputs
    // Must return same value on all clients
}
```

---

## Performance Targets

| Entity Count | Target Frame Time | Strategy |
|-------------|-------------------|----------|
| 10k         | < 2ms             | Full fidelity all |
| 50k         | < 4ms             | Tier 0/1 only |
| 100k        | < 8ms             | All tiers + aggregation |
| 200k        | < 16ms            | Aggressive aggregation |

---

## Implementation Checklist

- [ ] Centralize LOD tier calculation in single function
- [ ] Add LOD tier to entity components (or compute on-demand)
- [ ] Implement tiered physics in UnitIdleSystem
- [ ] Implement tiered flow lookups in UnitMovementSystem
- [ ] Add hysteresis to prevent thrashing
- [ ] Implement entity aggregation for Tier 3
- [ ] Add determinism tests for LOD consistency
- [ ] Benchmark each tier's performance impact

