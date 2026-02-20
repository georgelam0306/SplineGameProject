using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems;

/// <summary>
/// Moves projectiles and applies damage when they reach their target.
/// Supports single-target and splash damage (AOE).
/// Uses time-based impact calculation instead of collision detection.
/// Runs AFTER CombatUnitCombatSystem.
/// </summary>
public sealed class ProjectileSystem : SimTableSystem
{
    public ProjectileSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var projectiles = World.ProjectileRows;
        var zombies = World.ZombieRows;

        // Iterate backwards for safe Free() during iteration
        for (int slot = projectiles.Count - 1; slot >= 0; slot--)
        {
            if (!projectiles.TryGetRow(slot, out var proj)) continue;
            if (!proj.Flags.IsActive()) continue;

            // Handle homing - adjust velocity toward target
            if (proj.Flags.IsHoming() && proj.TargetHandle.IsValid)
            {
                int targetSlot = zombies.GetSlot(proj.TargetHandle);
                if (targetSlot >= 0 && zombies.TryGetRow(targetSlot, out var target) && !target.Flags.IsDead())
                {
                    // Calculate direction to target
                    Fixed64 dx = target.Position.X - proj.Position.X;
                    Fixed64 dy = target.Position.Y - proj.Position.Y;
                    Fixed64 distSq = dx * dx + dy * dy;

                    if (distSq > Fixed64.Zero)
                    {
                        Fixed64 dist = Fixed64.Sqrt(distSq);
                        Fixed64 targetDirX = dx / dist;
                        Fixed64 targetDirY = dy / dist;

                        // Get current speed
                        Fixed64 speed = Fixed64.Sqrt(proj.Velocity.X * proj.Velocity.X + proj.Velocity.Y * proj.Velocity.Y);

                        // Blend toward target direction based on homing strength
                        Fixed64 currentDirX = proj.Velocity.X / speed;
                        Fixed64 currentDirY = proj.Velocity.Y / speed;

                        Fixed64 newDirX = currentDirX + (targetDirX - currentDirX) * proj.HomingStrength;
                        Fixed64 newDirY = currentDirY + (targetDirY - currentDirY) * proj.HomingStrength;

                        // Normalize and apply speed
                        Fixed64 newDirLen = Fixed64.Sqrt(newDirX * newDirX + newDirY * newDirY);
                        if (newDirLen > Fixed64.Zero)
                        {
                            proj.Velocity = new Fixed64Vec2(
                                newDirX / newDirLen * speed,
                                newDirY / newDirLen * speed
                            );
                        }
                    }
                }
            }

            // Update position (velocity is in tiles/second, multiply by delta time)
            proj.Position = proj.Position + proj.Velocity * DeltaSeconds;

            // Update distance traveled
            Fixed64 velMag = Fixed64.Sqrt(proj.Velocity.X * proj.Velocity.X + proj.Velocity.Y * proj.Velocity.Y);
            proj.DistanceTraveled = proj.DistanceTraveled + velMag * DeltaSeconds;

            // Check max range
            if (proj.MaxRange > Fixed64.Zero && proj.DistanceTraveled >= proj.MaxRange)
            {
                projectiles.Free(proj);
                continue;
            }

            // Proximity-based hit detection - check if we're close enough to target
            bool hitTarget = false;
            if (proj.TargetHandle.IsValid)
            {
                int targetSlot = zombies.GetSlot(proj.TargetHandle);
                if (targetSlot >= 0 && zombies.TryGetRow(targetSlot, out var zombie) && !zombie.Flags.IsDead())
                {
                    Fixed64 dx = zombie.Position.X - proj.Position.X;
                    Fixed64 dy = zombie.Position.Y - proj.Position.Y;
                    Fixed64 distSq = dx * dx + dy * dy;

                    // Hit radius - projectile hits when within ~16 pixels of target center
                    Fixed64 hitRadiusSq = Fixed64.FromInt(16 * 16);
                    if (distSq <= hitRadiusSq)
                    {
                        // Check for splash damage
                        if (proj.SplashRadius > Fixed64.Zero)
                        {
                            ApplySplashDamage(zombies, slot, projectiles);
                        }
                        else
                        {
                            // Single target damage
                            zombie.Health -= proj.Damage;
                            zombie.AggroHandle = proj.SourceHandle;
                        }
                        hitTarget = true;
                    }
                }
            }

            if (hitTarget)
            {
                projectiles.Free(proj);
                continue;
            }

            // Fallback: expire after lifetime (in case target dies or becomes invalid)
            proj.LifetimeFrames--;
            if (proj.LifetimeFrames <= 0)
            {
                projectiles.Free(proj);
                continue;
            }
        }
    }

    /// <summary>
    /// Applies splash damage to all zombies within the projectile's splash radius.
    /// </summary>
    private void ApplySplashDamage(ZombieRowTable zombies, int projSlot, ProjectileRowTable projectiles)
    {
        var proj = projectiles.GetRowBySlot(projSlot);
        Fixed64 radiusSq = proj.SplashRadius * proj.SplashRadius;
        int baseDamage = proj.Damage;
        bool hasFalloff = proj.Flags.HasFlag(ProjectileFlags.SplashFalloff);
        var impactPos = proj.Position;
        var splashRadius = proj.SplashRadius;

        foreach (int slot in zombies.QueryRadius(impactPos, splashRadius))
        {
            if (!zombies.TryGetRow(slot, out var zombie)) continue;
            if (zombie.Flags.IsDead()) continue;

            Fixed64 distSq = Fixed64Vec2.DistanceSquared(impactPos, zombie.Position);

            // Calculate damage with optional falloff
            int actualDamage;
            if (hasFalloff && radiusSq > Fixed64.Zero && distSq > Fixed64.Zero)
            {
                // Linear falloff: full damage at center, zero at edge
                Fixed64 dist = Fixed64.Sqrt(distSq);
                Fixed64 falloffMult = Fixed64.OneValue - (dist / splashRadius);
                actualDamage = (Fixed64.FromInt(baseDamage) * falloffMult).ToInt();
                if (actualDamage < 1) actualDamage = 1; // Minimum 1 damage
            }
            else
            {
                actualDamage = baseDamage;
            }

            zombie.Health -= actualDamage;
            zombie.AggroHandle = proj.SourceHandle;
        }
    }
}
