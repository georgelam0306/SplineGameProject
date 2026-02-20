using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using Core;
using SimTable;

namespace Catrillion.Simulation.Systems;

/// <summary>
/// Evaluates state transition conditions for zombies and updates their state.
/// Runs BEFORE ZombieMovementSystem - movement system only reads state.
/// </summary>
public sealed class ZombieStateTransitionSystem : SimTableSystem
{
    // Salt values for deterministic random
    private const int SaltIdleDur = 0x49444C45;   // "IDLE"
    private const int SaltWanderDur = 0x574E4452; // "WNDR"
    private const int SaltWanderDir = 0x57414E44; // "WAND"

    public ZombieStateTransitionSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        // Zombies don't transition states during countdown
        if (!World.IsPlaying()) return;

        var zombies = World.ZombieRows;
        var threatTable = World.ThreatGridStateRows;
        bool hasThreatGrid = threatTable.Count > 0;

        int frame = context.CurrentFrame;
        int seed = context.SessionSeed;

        for (int slot = 0; slot < zombies.Count; slot++)
        {
            var zombie = zombies.GetRowBySlot(slot);
            if (zombie.Flags.IsDead()) continue;

            // Get local threat level
            Fixed64 localThreat = Fixed64.Zero;
            if (hasThreatGrid)
            {
                var (_, maxThreat, _) = ThreatGridService.FindHighestThreatNearby(
                    threatTable, zombie.Position, zombie.ThreatSearchRadius
                );
                localThreat = maxThreat;
            }

            // Evaluate transitions based on current state
            ZombieState newState = EvaluateTransitions(
                ref zombie, slot, frame, seed, localThreat
            );

            // If state changed, handle entry/exit actions
            if (newState != zombie.State)
            {
                HandleStateExit(ref zombie, zombie.State);
                zombie.State = newState;
                HandleStateEntry(ref zombie, newState, slot, frame, seed);
            }
            else
            {
                // No transition - decrement timer if applicable
                if (zombie.StateTimer > 0)
                {
                    zombie.StateTimer--;
                }
            }
        }
    }

    private ZombieState EvaluateTransitions(
        ref ZombieRowRowRef zombie,
        int slot, int frame, int seed,
        Fixed64 localThreat)
    {
        switch (zombie.State)
        {
            case ZombieState.Idle:
                // Idle -> Chase: High threat
                if (localThreat >= ThreatGridService.ChaseThreshold)
                {
                    return ZombieState.Chase;
                }
                // Idle -> Wander: Timer expired
                if (zombie.StateTimer <= 0)
                {
                    return ZombieState.Wander;
                }
                return ZombieState.Idle;

            case ZombieState.Wander:
                // Wander -> Chase: High threat
                if (localThreat >= ThreatGridService.ChaseThreshold)
                {
                    return ZombieState.Chase;
                }
                // Wander -> Idle: Timer expired
                if (zombie.StateTimer <= 0)
                {
                    return ZombieState.Idle;
                }
                return ZombieState.Wander;

            case ZombieState.Chase:
                // Chase -> Idle: Lost threat
                if (localThreat < ThreatGridService.LoseInterestThreshold)
                {
                    zombie.TargetHandle = SimHandle.Invalid;
                    return ZombieState.Idle;
                }
                if (zombie.TargetHandle.IsValid == false)
                {
                    zombie.TargetHandle = SimHandle.Invalid;
                    return ZombieState.Idle;
                }
                // Always re-evaluate for closer targets while chasing
                AcquireNearestTarget(ref zombie);
                // Chase -> Attack: Target in range
                if (zombie.TargetHandle.IsValid)
                {
                    Fixed64 distSq = GetDistanceToTargetSq(zombie);
                    // AttackRange is in tiles, convert to pixels (32px per tile) and square
                    Fixed64 attackRangePx = zombie.AttackRange * Fixed64.FromInt(32);
                    Fixed64 attackRangeSq = attackRangePx * attackRangePx;
                    if (distSq <= attackRangeSq)
                    {
                        return ZombieState.Attack;
                    }
                }
                return ZombieState.Chase;

            case ZombieState.Attack:
                // Attack -> Chase/Attack: Attack cooldown done
                if (zombie.StateTimer <= 0)
                {
                    if (zombie.TargetHandle.IsValid)
                    {
                        Fixed64 distSq = GetDistanceToTargetSq(zombie);
                        // AttackRange is in tiles, convert to pixels (32px per tile) and square
                        Fixed64 attackRangePx = zombie.AttackRange * Fixed64.FromInt(32);
                        Fixed64 attackRangeSq = attackRangePx * attackRangePx;
                        if (distSq <= attackRangeSq)
                        {
                            // Still in range - re-enter attack (reset timer)
                            return ZombieState.Attack;
                        }
                    }
                    // Target out of range or dead - wave zombies return to WaveChase
                    if (zombie.Flags.HasFlag(MortalFlags.IsWaveZombie))
                    {
                        return ZombieState.WaveChase;
                    }
                    return ZombieState.Idle;
                }
                return ZombieState.Attack;

            case ZombieState.WaveChase:
                // WaveChase -> Attack: Target in attack range
                // Never transitions to Idle/Wander - always pursues command center
                AcquireNearestTarget(ref zombie);
                if (zombie.TargetHandle.IsValid)
                {
                    Fixed64 distSq = GetDistanceToTargetSq(zombie);
                    Fixed64 attackRangePx = zombie.AttackRange * Fixed64.FromInt(32);
                    Fixed64 attackRangeSq = attackRangePx * attackRangePx;
                    if (distSq <= attackRangeSq)
                    {
                        return ZombieState.Attack;
                    }
                }
                return ZombieState.WaveChase;
        }

        return zombie.State;
    }

    private void HandleStateEntry(
        ref ZombieRowRowRef zombie,
        ZombieState newState,
        int slot, int frame, int seed)
    {
        switch (newState)
        {
            case ZombieState.Idle:
                zombie.StateTimer = DeterministicRandom.RangeWithSeed(
                    seed, frame, slot, SaltIdleDur, zombie.IdleDurationMin, zombie.IdleDurationMax
                );
                zombie.Velocity = Fixed64Vec2.Zero;
                break;

            case ZombieState.Wander:
                zombie.StateTimer = DeterministicRandom.RangeWithSeed(
                    seed, frame, slot, SaltWanderDur, zombie.WanderDurationMin, zombie.WanderDurationMax
                );
                zombie.WanderDirectionSeed = DeterministicRandom.RangeWithSeed(
                    seed, frame, slot, SaltWanderDir, 0, 360
                );
                break;

            case ZombieState.Chase:
                zombie.StateTimer = 0; // No timer for chase
                AcquireNearestTarget(ref zombie);
                break;

            case ZombieState.Attack:
                // Convert AttackCooldown (seconds) to frames for StateTimer
                int cooldownFrames = (zombie.AttackCooldown * SimulationConfig.TickRate).ToInt();
                zombie.StateTimer = cooldownFrames > 0 ? cooldownFrames : SimulationConfig.TickRate;
                zombie.Velocity = Fixed64Vec2.Zero;
                // TODO: Apply damage to target here or in separate combat system
                break;

            case ZombieState.WaveChase:
                zombie.StateTimer = 0; // No timer for wave chase
                AcquireNearestTarget(ref zombie);
                break;
        }
    }

    private void HandleStateExit(ref ZombieRowRowRef zombie, ZombieState oldState)
    {
        // Clear velocity on state exit (movement system will set appropriate velocity)
        zombie.Velocity = Fixed64Vec2.Zero;
    }

    private Fixed64 GetDistanceToTargetSq(ZombieRowRowRef zombie)
    {
        var handle = zombie.TargetHandle;
        if (!handle.IsValid) return Fixed64.MaxValue;

        // Determine target type from TableId
        if (handle.TableId == BuildingRowTable.TableIdConst)
        {
            var buildings = World.BuildingRows;
            int slot = buildings.GetSlot(handle);
            if (slot < 0) return Fixed64.MaxValue;
            if (!buildings.TryGetRow(slot, out var building)) return Fixed64.MaxValue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) return Fixed64.MaxValue;
            if (building.Health <= 0) return Fixed64.MaxValue;

            // Calculate distance to nearest edge of building bounding box (not center)
            Fixed64 minX = Fixed64.FromInt(building.TileX * 32);
            Fixed64 minY = Fixed64.FromInt(building.TileY * 32);
            Fixed64 maxX = Fixed64.FromInt((building.TileX + building.Width) * 32);
            Fixed64 maxY = Fixed64.FromInt((building.TileY + building.Height) * 32);

            Fixed64 closestX = Fixed64.Clamp(zombie.Position.X, minX, maxX);
            Fixed64 closestY = Fixed64.Clamp(zombie.Position.Y, minY, maxY);

            Fixed64 dx = closestX - zombie.Position.X;
            Fixed64 dy = closestY - zombie.Position.Y;
            return dx * dx + dy * dy;
        }

        if (handle.TableId == CombatUnitRowTable.TableIdConst)
        {
            var units = World.CombatUnitRows;
            int slot = units.GetSlot(handle);
            if (slot < 0) return Fixed64.MaxValue;
            if (!units.TryGetRow(slot, out var unit)) return Fixed64.MaxValue;
            if (unit.Flags.IsDead()) return Fixed64.MaxValue;

            Fixed64 dx = unit.Position.X - zombie.Position.X;
            Fixed64 dy = unit.Position.Y - zombie.Position.Y;
            return dx * dx + dy * dy;
        }

        return Fixed64.MaxValue;
    }

    /// <summary>
    /// Get squared distance from a position to any valid handle (building or unit).
    /// Returns MaxValue if handle is invalid or target is dead.
    /// </summary>
    private Fixed64 GetDistanceToHandleSq(Fixed64Vec2 position, SimHandle handle)
    {
        if (!handle.IsValid) return Fixed64.MaxValue;

        if (handle.TableId == BuildingRowTable.TableIdConst)
        {
            var buildings = World.BuildingRows;
            int slot = buildings.GetSlot(handle);
            if (slot < 0) return Fixed64.MaxValue;
            if (!buildings.TryGetRow(slot, out var building)) return Fixed64.MaxValue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) return Fixed64.MaxValue;
            if (building.Health <= 0) return Fixed64.MaxValue;

            Fixed64 dx = building.Position.X - position.X;
            Fixed64 dy = building.Position.Y - position.Y;
            return dx * dx + dy * dy;
        }

        if (handle.TableId == CombatUnitRowTable.TableIdConst)
        {
            var units = World.CombatUnitRows;
            int slot = units.GetSlot(handle);
            if (slot < 0) return Fixed64.MaxValue;
            if (!units.TryGetRow(slot, out var unit)) return Fixed64.MaxValue;
            if (unit.Flags.IsDead()) return Fixed64.MaxValue;

            Fixed64 dx = unit.Position.X - position.X;
            Fixed64 dy = unit.Position.Y - position.Y;
            return dx * dx + dy * dy;
        }

        return Fixed64.MaxValue;
    }

    /// <summary>
    /// Acquire nearest valid target within acquisition range.
    /// Priority order: AggroHandle (attacker) > buildings > units.
    /// Preserves current target if no better target found.
    /// </summary>
    private void AcquireNearestTarget(ref ZombieRowRowRef zombie)
    {
        // TargetAcquisitionRange is in pixels
        Fixed64 acquisitionRange = Fixed64.FromInt(zombie.TargetAcquisitionRange);
        Fixed64 acquisitionRangeSq = acquisitionRange * acquisitionRange;

        // Priority 1: Check aggro target (entity that attacked us)
        if (zombie.AggroHandle.IsValid)
        {
            Fixed64 aggroDistSq = GetDistanceToHandleSq(zombie.Position, zombie.AggroHandle);
            if (aggroDistSq <= acquisitionRangeSq)
            {
                zombie.TargetHandle = zombie.AggroHandle;
                zombie.AggroHandle = SimHandle.Invalid; // Clear aggro once we've locked on
                return;
            }
            // Aggro target out of range - clear it
            zombie.AggroHandle = SimHandle.Invalid;
        }

        // Start with current target if valid (preserve it if nothing better found)
        SimHandle bestHandle = zombie.TargetHandle;
        Fixed64 bestDistSq = acquisitionRangeSq;

        // If we have a current target, use its distance as the baseline
        if (bestHandle.TableId >= 0)
        {
            Fixed64 currentDistSq = GetDistanceToTargetSq(zombie);
            if (currentDistSq < Fixed64.MaxValue)
            {
                bestDistSq = currentDistSq;
            }
        }

        // If current target is a building, mark as found to preserve building priority
        bool foundBuilding = bestHandle.TableId == BuildingRowTable.TableIdConst;

        // Phase 1: Search buildings using spatial query
        var buildings = World.BuildingRows;
        foreach (int slot in buildings.QueryRadius(zombie.Position, acquisitionRange))
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if (building.Health <= 0) continue;

            Fixed64 dx = building.Position.X - zombie.Position.X;
            Fixed64 dy = building.Position.Y - zombie.Position.Y;
            Fixed64 distSq = dx * dx + dy * dy;

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestHandle = buildings.GetHandle(slot);
                foundBuilding = true;
            }
        }

        // Phase 2: Search combat units only if no building found
        // (buildings-first priority: closest building always wins)
        if (!foundBuilding)
        {
            var units = World.CombatUnitRows;
            foreach (int slot in units.QueryRadius(zombie.Position, acquisitionRange))
            {
                if (!units.TryGetRow(slot, out var unit)) continue;
                if (unit.Flags.IsDead()) continue;

                Fixed64 dx = unit.Position.X - zombie.Position.X;
                Fixed64 dy = unit.Position.Y - zombie.Position.Y;
                Fixed64 distSq = dx * dx + dy * dy;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestHandle = units.GetHandle(slot);
                }
            }
        }

        zombie.TargetHandle = bestHandle;
    }

    /// <summary>
    /// Validate that current target exists, is alive, and within loss range.
    /// Returns false if target is invalid (clears TargetHandle).
    /// Handles both BuildingRow and CombatUnitRow targets.
    /// </summary>
    private bool ValidateTarget(ref ZombieRowRowRef zombie)
    {
        var handle = zombie.TargetHandle;
        if (!handle.IsValid) return false;

        // TargetLossRange is in pixels
        Fixed64 lossRange = Fixed64.FromInt(zombie.TargetLossRange);
        Fixed64 lossRangeSq = lossRange * lossRange;

        Fixed64Vec2 targetPos;

        // Determine target type from TableId
        if (handle.TableId == BuildingRowTable.TableIdConst)
        {
            var buildings = World.BuildingRows;
            int slot = buildings.GetSlot(handle);
            if (slot < 0)
            {
                zombie.TargetHandle = SimHandle.Invalid;
                return false;
            }
            if (!buildings.TryGetRow(slot, out var building))
            {
                zombie.TargetHandle = SimHandle.Invalid;
                return false;
            }
            if (!building.Flags.HasFlag(BuildingFlags.IsActive) || building.Health <= 0)
            {
                zombie.TargetHandle = SimHandle.Invalid;
                return false;
            }
            targetPos = building.Position;
        }
        else if (handle.TableId == CombatUnitRowTable.TableIdConst)
        {
            var units = World.CombatUnitRows;
            int slot = units.GetSlot(handle);
            if (slot < 0)
            {
                zombie.TargetHandle = SimHandle.Invalid;
                return false;
            }
            if (!units.TryGetRow(slot, out var unit))
            {
                zombie.TargetHandle = SimHandle.Invalid;
                return false;
            }
            if (unit.Flags.IsDead())
            {
                zombie.TargetHandle = SimHandle.Invalid;
                return false;
            }
            targetPos = unit.Position;
        }
        else
        {
            zombie.TargetHandle = SimHandle.Invalid;
            return false;
        }

        // Check if target moved out of range
        Fixed64 dx = targetPos.X - zombie.Position.X;
        Fixed64 dy = targetPos.Y - zombie.Position.Y;
        Fixed64 distSq = dx * dx + dy * dy;

        if (distSq > lossRangeSq)
        {
            zombie.TargetHandle = SimHandle.Invalid;
            return false;
        }

        return true;
    }
}
