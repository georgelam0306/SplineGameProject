using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using Core;
using SimTable;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Handles combat unit attacks by spawning projectiles when attack timer completes.
/// Runs AFTER CombatUnitMovementSystem.
/// </summary>
public sealed class CombatUnitCombatSystem : SimTableSystem
{
    // Projectile settings
    private static readonly Fixed64 ProjectileSpeed = Fixed64.FromInt(240); // tiles per second
    private const int ProjectileLifetime = SimulationConfig.TickRate * 2; // 2 seconds

    public CombatUnitCombatSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var units = World.CombatUnitRows;
        var zombies = World.ZombieRows;
        var projectiles = World.ProjectileRows;
        var buildings = World.BuildingRows;

        for (int slot = 0; slot < units.Count; slot++)
        {
            var unit = units.GetRowBySlot(slot);
            if (unit.Flags.IsDead()) continue;
            if (!unit.TargetHandle.IsValid) continue;

            // Decrement attack timer
            if (unit.AttackTimer > Fixed32.Zero)
            {
                unit.AttackTimer = unit.AttackTimer - DeltaSeconds32;
                continue;
            }

            // Determine fire position - building center if garrisoned, else unit position
            Fixed64Vec2 firePosition = unit.Position;
            if (unit.GarrisonedInHandle.IsValid)
            {
                int buildingSlot = buildings.GetSlot(unit.GarrisonedInHandle);
                if (buildings.TryGetRow(buildingSlot, out var building))
                {
                    firePosition = building.Position;
                }
            }

            // Attack timer is 0 - time to fire!
            // Validate target still exists and is in range
            int targetSlot = zombies.GetSlot(unit.TargetHandle);
            if (targetSlot < 0)
            {
                unit.TargetHandle = SimHandle.Invalid;
                continue;
            }
            if (!zombies.TryGetRow(targetSlot, out var zombie))
            {
                unit.TargetHandle = SimHandle.Invalid;
                continue;
            }
            if (zombie.Flags.IsDead())
            {
                unit.TargetHandle = SimHandle.Invalid;
                continue;
            }

            // Range check (from fire position, not unit position)
            Fixed64 dx = zombie.Position.X - firePosition.X;
            Fixed64 dy = zombie.Position.Y - firePosition.Y;
            Fixed64 distSq = dx * dx + dy * dy;
            Fixed64 rangeSq = unit.AttackRange * unit.AttackRange;

            if (distSq > rangeSq)
            {
                // Target out of range - clear it, target acquisition will find a new one
                unit.TargetHandle = SimHandle.Invalid;
                continue;
            }

            // Spawn projectile
            SimHandle projHandle = projectiles.Allocate();
            var proj = projectiles.GetRow(projHandle);

            proj.Position = firePosition;  // Fire from building if garrisoned
            proj.Type = ProjectileType.Bullet;
            proj.OwnerPlayerId = unit.OwnerPlayerId;
            proj.SourceHandle = units.GetHandle(slot);
            proj.Damage = unit.Damage;
            proj.SplashRadius = Fixed64.Zero;
            proj.PierceCount = 0;
            proj.TargetHandle = unit.TargetHandle;
            proj.HomingStrength = Fixed64.FromRaw(6554); // ~0.1 turn rate per frame (homing)
            proj.MaxRange = Fixed64.Zero;
            proj.DistanceTraveled = Fixed64.Zero;
            proj.Flags = ProjectileFlags.IsActive | ProjectileFlags.IsHoming;

            // Calculate velocity toward target and time to impact
            Fixed64 dist = Fixed64.Sqrt(distSq);
            if (dist > Fixed64.Zero)
            {
                Fixed64 dirX = dx / dist;
                Fixed64 dirY = dy / dist;
                proj.Velocity = new Fixed64Vec2(dirX * ProjectileSpeed, dirY * ProjectileSpeed);

                // Calculate frames until impact (seconds to impact * tick rate)
                Fixed64 secondsToImpact = dist / ProjectileSpeed;
                proj.LifetimeFrames = (secondsToImpact * SimulationConfig.TickRate).ToInt();
                if (proj.LifetimeFrames < 1) proj.LifetimeFrames = 1;
            }
            else
            {
                proj.Velocity = Fixed64Vec2.Zero;
                proj.LifetimeFrames = 1; // Instant hit
            }

            // Reset attack timer
            unit.AttackTimer = unit.BaseAttackCooldown;
        }
    }
}
