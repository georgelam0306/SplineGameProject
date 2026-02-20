using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using Core;
using SimTable;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Handles building combat by either:
/// - Spawning projectiles (optionally with splash damage) for projectile-based buildings
/// - Dealing direct AOE damage for self-centered AOE buildings
/// </summary>
public sealed class BuildingCombatSystem : SimTableSystem
{
    private static readonly Fixed64 ProjectileSpeed = Fixed64.FromInt(8);

    public BuildingCombatSystem(SimWorld world) : base(world) { }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var buildings = World.BuildingRows;
        var zombies = World.ZombieRows;
        var projectiles = World.ProjectileRows;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);

            // Only process buildings with damage capability
            if (building.Flags.IsDead()) continue;
            if (building.Damage == 0) continue;

            // Decrement attack timer
            if (building.AttackTimer > Fixed32.Zero)
            {
                building.AttackTimer = building.AttackTimer - DeltaSeconds32;
                continue;
            }

            // Branch based on combat mode
            if (building.IsSelfCenteredAOE)
            {
                ProcessSelfCenteredAOE(buildings, zombies, slot);
            }
            else
            {
                ProcessProjectileFire(buildings, zombies, projectiles, slot);
            }
        }
    }

    /// <summary>
    /// Self-centered AOE: damage all zombies in range directly (no projectile).
    /// </summary>
    private void ProcessSelfCenteredAOE(
        BuildingRowTable buildings,
        ZombieRowTable zombies,
        int buildingSlot)
    {
        var building = buildings.GetRowBySlot(buildingSlot);
        var rangeSq = building.AttackRange * building.AttackRange;
        int baseDamage = building.Damage;
        bool hasFalloff = building.HasDamageFalloff;
        var buildingPos = building.Position;
        var attackRange = building.AttackRange;

        bool hitAny = false;

        foreach (int zombieSlot in zombies.QueryRadius(buildingPos, attackRange))
        {
            if (!zombies.TryGetRow(zombieSlot, out var zombie)) continue;
            if (zombie.Flags.IsDead()) continue;

            hitAny = true;

            Fixed64 distSq = Fixed64Vec2.DistanceSquared(buildingPos, zombie.Position);

            // Calculate damage with optional falloff
            int actualDamage;
            if (hasFalloff && rangeSq > Fixed64.Zero && distSq > Fixed64.Zero)
            {
                // Linear falloff: full damage at center, zero at edge
                Fixed64 dist = Fixed64.Sqrt(distSq);
                Fixed64 falloffMult = Fixed64.OneValue - (dist / attackRange);
                actualDamage = (Fixed64.FromInt(baseDamage) * falloffMult).ToInt();
                if (actualDamage < 1) actualDamage = 1; // Minimum 1 damage
            }
            else
            {
                actualDamage = baseDamage;
            }

            // Apply damage directly (no projectile)
            zombie.Health -= actualDamage;

            // Set aggro so zombie will target this building
            zombie.AggroHandle = buildings.GetHandle(buildingSlot);
        }

        // Only reset cooldown if we actually hit something
        if (hitAny)
        {
            // Re-get building to update timer
            var buildingRef = buildings.GetRowBySlot(buildingSlot);
            buildingRef.AttackTimer = buildingRef.AttackCooldown;
        }
    }

    /// <summary>
    /// Projectile-based attack: find target and spawn projectile.
    /// </summary>
    private void ProcessProjectileFire(
        BuildingRowTable buildings,
        ZombieRowTable zombies,
        ProjectileRowTable projectiles,
        int buildingSlot)
    {
        var building = buildings.GetRowBySlot(buildingSlot);

        // Find nearest zombie in range
        var rangeSq = building.AttackRange * building.AttackRange;
        SimHandle bestTarget = SimHandle.Invalid;
        Fixed64 bestDistSq = rangeSq;
        var buildingPos = building.Position;

        for (int zSlot = 0; zSlot < zombies.Count; zSlot++)
        {
            var zombie = zombies.GetRowBySlot(zSlot);
            if (zombie.Flags.IsDead()) continue;

            Fixed64 dx = zombie.Position.X - buildingPos.X;
            Fixed64 dy = zombie.Position.Y - buildingPos.Y;
            Fixed64 distSq = dx * dx + dy * dy;

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestTarget = zombies.GetHandle(zSlot);
            }
        }

        // No target found - skip but don't reset timer
        if (!bestTarget.IsValid) return;

        // Get target position for projectile direction
        int targetSlot = zombies.GetSlot(bestTarget);
        if (targetSlot < 0) return;
        if (!zombies.TryGetRow(targetSlot, out var target)) return;
        if (target.Flags.IsDead()) return;

        // Spawn projectile
        SimHandle projHandle = projectiles.Allocate();
        var proj = projectiles.GetRow(projHandle);

        proj.Position = buildingPos;
        proj.Type = ProjectileType.Bullet;
        proj.OwnerPlayerId = building.OwnerPlayerId;
        proj.SourceHandle = buildings.GetHandle(buildingSlot);
        proj.Damage = building.Damage;
        proj.SplashRadius = building.SplashRadius;
        proj.PierceCount = 0;
        proj.TargetHandle = bestTarget;
        proj.HomingStrength = Fixed64.FromRaw(6554); // ~0.1 homing
        proj.MaxRange = Fixed64.Zero;
        proj.DistanceTraveled = Fixed64.Zero;

        // Set flags
        proj.Flags = ProjectileFlags.IsActive | ProjectileFlags.IsHoming;
        if (building.SplashFalloff)
        {
            proj.Flags |= ProjectileFlags.SplashFalloff;
        }

        Fixed64 dx2 = target.Position.X - buildingPos.X;
        Fixed64 dy2 = target.Position.Y - buildingPos.Y;
        Fixed64 dist = Fixed64.Sqrt(bestDistSq);
        if (dist > Fixed64.Zero)
        {
            Fixed64 dirX = dx2 / dist;
            Fixed64 dirY = dy2 / dist;
            proj.Velocity = new Fixed64Vec2(dirX * ProjectileSpeed, dirY * ProjectileSpeed);
            // Convert seconds to frames: (distance / tiles-per-second) * ticks-per-second
            proj.LifetimeFrames = (dist / ProjectileSpeed * SimulationConfig.TickRate).ToInt();
            if (proj.LifetimeFrames < 1) proj.LifetimeFrames = 1;
        }
        else
        {
            proj.Velocity = Fixed64Vec2.Zero;
            proj.LifetimeFrames = 1;
        }

        // Re-get building to update timer
        var buildingRef = buildings.GetRowBySlot(buildingSlot);
        buildingRef.AttackTimer = buildingRef.AttackCooldown;
    }
}
