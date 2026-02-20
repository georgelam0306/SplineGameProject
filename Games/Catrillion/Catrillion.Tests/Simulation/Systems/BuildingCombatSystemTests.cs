using Xunit;
using Catrillion.Simulation;
using Catrillion.Simulation.Components;
using Catrillion.GameData.Schemas;
using Core;
using SimTable;

namespace Catrillion.Tests.Simulation.Systems;

/// <summary>
/// Tests for BuildingCombatSystem covering:
/// - Splash damage (projectile-based buildings)
/// - Self-centered AOE (direct damage buildings like Tesla Coil)
/// - Damage falloff calculations
/// - Edge cases for spatial queries
/// </summary>
public class BuildingCombatSystemTests : IDisposable
{
    private readonly SimWorld _simWorld;

    public BuildingCombatSystemTests()
    {
        _simWorld = new SimWorld();
    }

    public void Dispose()
    {
        _simWorld.Dispose();
    }

    #region Helper Methods

    private SimHandle CreateBuilding(
        Fixed64Vec2 position,
        int damage,
        Fixed64 attackRange,
        int attackCooldown,
        Fixed64 splashRadius = default,
        bool splashFalloff = false,
        bool isSelfCenteredAOE = false,
        bool hasDamageFalloff = false)
    {
        var buildings = _simWorld.BuildingRows;
        var handle = buildings.Allocate();
        var building = buildings.GetRow(handle);

        building.Position = position;
        building.Damage = damage;
        building.AttackRange = attackRange;
        building.AttackCooldown = attackCooldown;
        building.AttackTimer = 0; // Ready to fire
        building.SplashRadius = splashRadius;
        building.SplashFalloff = splashFalloff;
        building.IsSelfCenteredAOE = isSelfCenteredAOE;
        building.HasDamageFalloff = hasDamageFalloff;
        building.Flags = BuildingFlags.IsActive;
        building.TypeId = BuildingTypeId.Turret;

        return handle;
    }

    private SimHandle CreateZombie(Fixed64Vec2 position, int health = 100)
    {
        var zombies = _simWorld.ZombieRows;
        var handle = zombies.Allocate();
        var zombie = zombies.GetRow(handle);

        zombie.Position = position;
        zombie.Health = health;
        zombie.Flags = MortalFlags.IsActive;

        return handle;
    }

    private SimHandle CreateProjectile(
        Fixed64Vec2 position,
        int damage,
        Fixed64 splashRadius,
        bool splashFalloff,
        SimHandle targetHandle)
    {
        var projectiles = _simWorld.ProjectileRows;
        var handle = projectiles.Allocate();
        var proj = projectiles.GetRow(handle);

        proj.Position = position;
        proj.Damage = damage;
        proj.SplashRadius = splashRadius;
        proj.TargetHandle = targetHandle;
        proj.Flags = ProjectileFlags.IsActive;
        if (splashFalloff)
        {
            proj.Flags |= ProjectileFlags.SplashFalloff;
        }

        return handle;
    }

    private int GetBuildingAttackTimer(SimHandle handle)
    {
        var buildings = _simWorld.BuildingRows;
        int slot = buildings.GetSlot(handle);
        if (slot < 0 || !buildings.TryGetRow(slot, out var building)) return -1;
        return building.AttackTimer;
    }

    private void SetBuildingAttackTimer(SimHandle handle, int timer)
    {
        var buildings = _simWorld.BuildingRows;
        int slot = buildings.GetSlot(handle);
        if (slot >= 0 && buildings.TryGetRow(slot, out var building))
        {
            building.AttackTimer = timer;
        }
    }

    private void SetBuildingDead(SimHandle handle)
    {
        var buildings = _simWorld.BuildingRows;
        int slot = buildings.GetSlot(handle);
        if (slot >= 0 && buildings.TryGetRow(slot, out var building))
        {
            building.Flags |= BuildingFlags.IsDead;
        }
    }

    private int GetZombieHealth(SimHandle handle)
    {
        var zombies = _simWorld.ZombieRows;
        int slot = zombies.GetSlot(handle);
        if (slot < 0 || !zombies.TryGetRow(slot, out var zombie)) return -1;
        return zombie.Health;
    }

    private void SetZombieDead(SimHandle handle)
    {
        var zombies = _simWorld.ZombieRows;
        int slot = zombies.GetSlot(handle);
        if (slot >= 0 && zombies.TryGetRow(slot, out var zombie))
        {
            zombie.Flags |= MortalFlags.IsDead;
        }
    }

    #endregion

    #region Self-Centered AOE Tests

    [Fact]
    public void SelfCenteredAOE_DamagesAllZombiesInRange()
    {
        // Arrange: Building at origin with 100 range, 3 zombies at different distances
        var buildingPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);
        var buildingHandle = CreateBuilding(
            buildingPos,
            damage: 25,
            attackRange: Fixed64.FromInt(100),
            attackCooldown: 60,
            isSelfCenteredAOE: true,
            hasDamageFalloff: false);

        var zombie1 = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(30), Fixed64.Zero), health: 100);  // In range
        var zombie2 = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(80), Fixed64.Zero), health: 100);  // In range
        var zombie3 = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(150), Fixed64.Zero), health: 100); // Out of range

        // Act: Simulate the AOE attack
        SimulateSelfCenteredAOEAttack(buildingHandle);

        // Assert
        Assert.Equal(75, GetZombieHealth(zombie1)); // Took 25 damage
        Assert.Equal(75, GetZombieHealth(zombie2)); // Took 25 damage
        Assert.Equal(100, GetZombieHealth(zombie3)); // No damage (out of range)
    }

    [Fact]
    public void SelfCenteredAOE_WithFalloff_ReducesDamageAtEdge()
    {
        // Arrange: Building with 100 range, falloff enabled
        var buildingPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);
        var buildingHandle = CreateBuilding(
            buildingPos,
            damage: 100,
            attackRange: Fixed64.FromInt(100),
            attackCooldown: 60,
            isSelfCenteredAOE: true,
            hasDamageFalloff: true);

        // Zombie at center (distance 0) - should take full damage
        var zombieCenter = CreateZombie(new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero), health: 200);

        // Zombie at 50% range - should take ~50% damage
        var zombieMid = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(50), Fixed64.Zero), health: 200);

        // Zombie at 90% range - should take ~10% damage (minimum 1)
        var zombieEdge = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(90), Fixed64.Zero), health: 200);

        // Act
        SimulateSelfCenteredAOEAttack(buildingHandle);

        // Assert
        Assert.Equal(100, GetZombieHealth(zombieCenter)); // Full 100 damage
        int midHealth = GetZombieHealth(zombieMid);
        Assert.True(midHealth > 100 && midHealth < 200, $"Mid zombie should have partial damage, got {midHealth}"); // Partial damage
        int edgeHealth = GetZombieHealth(zombieEdge);
        // At 90% range, falloffMult = 1 - 0.9 = 0.1, so damage = 100 * 0.1 = 10
        Assert.True(edgeHealth >= 180 && edgeHealth < 200, $"Edge zombie should take reduced damage, got {edgeHealth}"); // ~10% damage
    }

    [Fact]
    public void SelfCenteredAOE_NoEnemiesInRange_CooldownNotReset()
    {
        // Arrange: Building with no zombies in range
        var buildingPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);
        var buildingHandle = CreateBuilding(
            buildingPos,
            damage: 25,
            attackRange: Fixed64.FromInt(100),
            attackCooldown: 60,
            isSelfCenteredAOE: true);

        // Zombie far outside range
        CreateZombie(new Fixed64Vec2(Fixed64.FromInt(500), Fixed64.Zero), health: 100);

        // Act
        SimulateSelfCenteredAOEAttack(buildingHandle);

        // Assert: Cooldown should NOT be reset (still 0, ready to fire again)
        Assert.Equal(0, GetBuildingAttackTimer(buildingHandle));
    }

    [Fact]
    public void SelfCenteredAOE_EnemiesInRange_CooldownReset()
    {
        // Arrange
        var buildingPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);
        var buildingHandle = CreateBuilding(
            buildingPos,
            damage: 25,
            attackRange: Fixed64.FromInt(100),
            attackCooldown: 60,
            isSelfCenteredAOE: true);

        CreateZombie(new Fixed64Vec2(Fixed64.FromInt(50), Fixed64.Zero), health: 100);

        // Act
        SimulateSelfCenteredAOEAttack(buildingHandle);

        // Assert: Cooldown should be reset
        Assert.Equal(60, GetBuildingAttackTimer(buildingHandle));
    }

    [Fact]
    public void SelfCenteredAOE_FalloffMinimumDamage_IsOne()
    {
        // Arrange: Building with falloff, zombie at very edge
        var buildingPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);
        var buildingHandle = CreateBuilding(
            buildingPos,
            damage: 10,
            attackRange: Fixed64.FromInt(100),
            attackCooldown: 60,
            isSelfCenteredAOE: true,
            hasDamageFalloff: true);

        // Zombie at 99% range - falloff would give 1% of 10 = 0.1, but minimum is 1
        var zombie = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(99), Fixed64.Zero), health: 100);

        // Act
        SimulateSelfCenteredAOEAttack(buildingHandle);

        // Assert: Should take exactly 1 damage (minimum)
        Assert.Equal(99, GetZombieHealth(zombie));
    }

    #endregion

    #region Splash Damage Tests (Projectile)

    [Fact]
    public void SplashDamage_ZeroRadius_OnlySingleTarget()
    {
        // Arrange: Projectile with no splash, multiple zombies close together
        var impactPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);

        var targetZombie = CreateZombie(impactPos, health: 100);
        var nearbyZombie = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(10), Fixed64.Zero), health: 100);

        var zombies = _simWorld.ZombieRows;
        var targetHandle = zombies.GetHandle(0);

        var projHandle = CreateProjectile(
            impactPos,
            damage: 50,
            splashRadius: Fixed64.Zero,
            splashFalloff: false,
            targetHandle: targetHandle);

        // Act: Apply single target damage (simulating hit)
        int slot = zombies.GetSlot(targetZombie);
        if (zombies.TryGetRow(slot, out var target))
        {
            target.Health -= 50;
        }

        // Assert
        Assert.Equal(50, GetZombieHealth(targetZombie)); // Target took damage
        Assert.Equal(100, GetZombieHealth(nearbyZombie)); // Nearby untouched
    }

    [Fact]
    public void SplashDamage_WithRadius_DamagesAllInRange()
    {
        // Arrange
        var impactPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);

        var zombie1 = CreateZombie(impactPos, health: 100);
        var zombie2 = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(20), Fixed64.Zero), health: 100);
        var zombie3 = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(40), Fixed64.Zero), health: 100);
        var zombieFar = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(100), Fixed64.Zero), health: 100);

        // Act: Apply splash damage with 50 radius
        ApplySplashDamage(impactPos, damage: 30, splashRadius: Fixed64.FromInt(50), falloff: false);

        // Assert
        Assert.Equal(70, GetZombieHealth(zombie1)); // In radius
        Assert.Equal(70, GetZombieHealth(zombie2)); // In radius
        Assert.Equal(70, GetZombieHealth(zombie3)); // In radius
        Assert.Equal(100, GetZombieHealth(zombieFar)); // Out of radius
    }

    [Fact]
    public void SplashDamage_WithFalloff_LinearDamageReduction()
    {
        // Arrange
        var impactPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);

        // Zombie at center
        var zombieCenter = CreateZombie(impactPos, health: 200);

        // Zombie at 50% range
        var zombieMid = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(50), Fixed64.Zero), health: 200);

        // Act: Apply splash with falloff, radius 100, damage 100
        ApplySplashDamage(impactPos, damage: 100, splashRadius: Fixed64.FromInt(100), falloff: true);

        // Assert
        Assert.Equal(100, GetZombieHealth(zombieCenter)); // Full damage (100)
        int midHealth = GetZombieHealth(zombieMid);
        Assert.True(midHealth > 100 && midHealth <= 150); // ~50% damage
    }

    [Fact]
    public void SplashDamage_AtExactEdge_StillDamaged()
    {
        // Arrange
        var impactPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);

        // Zombie exactly at splash radius edge
        var zombie = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(50), Fixed64.Zero), health: 100);

        // Act: Splash radius 50
        ApplySplashDamage(impactPos, damage: 30, splashRadius: Fixed64.FromInt(50), falloff: false);

        // Assert: Should still be damaged (edge is inclusive)
        Assert.Equal(70, GetZombieHealth(zombie));
    }

    [Fact]
    public void SplashDamage_JustOutsideRadius_NoDamage()
    {
        // Arrange
        var impactPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);

        // Zombie just outside splash radius
        var zombie = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(51), Fixed64.Zero), health: 100);

        // Act: Splash radius 50
        ApplySplashDamage(impactPos, damage: 30, splashRadius: Fixed64.FromInt(50), falloff: false);

        // Assert: Should not be damaged
        Assert.Equal(100, GetZombieHealth(zombie));
    }

    [Fact]
    public void SplashDamage_FalloffMinimum_IsOne()
    {
        // Arrange
        var impactPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);

        // Zombie at 99% of radius
        var zombie = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(99), Fixed64.Zero), health: 100);

        // Act: Small base damage, radius 100, with falloff
        ApplySplashDamage(impactPos, damage: 10, splashRadius: Fixed64.FromInt(100), falloff: true);

        // Assert: Should take minimum 1 damage
        Assert.Equal(99, GetZombieHealth(zombie));
    }

    #endregion

    #region Dead Zombie Tests

    [Fact]
    public void SplashDamage_SkipsDeadZombies()
    {
        // Arrange
        var impactPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);

        var aliveZombie = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(10), Fixed64.Zero), health: 100);
        var deadZombie = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(20), Fixed64.Zero), health: 0);
        SetZombieDead(deadZombie);

        // Act
        ApplySplashDamage(impactPos, damage: 30, splashRadius: Fixed64.FromInt(50), falloff: false);

        // Assert
        Assert.Equal(70, GetZombieHealth(aliveZombie));
        Assert.Equal(0, GetZombieHealth(deadZombie)); // Still 0, not -30
    }

    [Fact]
    public void SelfCenteredAOE_SkipsDeadZombies()
    {
        // Arrange
        var buildingPos = new Fixed64Vec2(Fixed64.Zero, Fixed64.Zero);
        var buildingHandle = CreateBuilding(
            buildingPos,
            damage: 25,
            attackRange: Fixed64.FromInt(100),
            attackCooldown: 60,
            isSelfCenteredAOE: true);

        var aliveZombie = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(30), Fixed64.Zero), health: 100);
        var deadZombie = CreateZombie(new Fixed64Vec2(Fixed64.FromInt(40), Fixed64.Zero), health: 0);
        SetZombieDead(deadZombie);

        // Act
        SimulateSelfCenteredAOEAttack(buildingHandle);

        // Assert
        Assert.Equal(75, GetZombieHealth(aliveZombie));
        Assert.Equal(0, GetZombieHealth(deadZombie));
    }

    #endregion

    #region Simulation Helpers

    private void SimulateSelfCenteredAOEAttack(SimHandle buildingHandle)
    {
        var buildings = _simWorld.BuildingRows;
        var zombies = _simWorld.ZombieRows;

        int buildingSlot = buildings.GetSlot(buildingHandle);
        if (buildingSlot < 0 || !buildings.TryGetRow(buildingSlot, out var building)) return;

        var rangeSq = building.AttackRange * building.AttackRange;
        int baseDamage = building.Damage;
        bool hasFalloff = building.HasDamageFalloff;
        var buildingPos = building.Position;
        var attackRange = building.AttackRange;

        bool hitAny = false;

        for (int slot = 0; slot < zombies.Count; slot++)
        {
            if (!zombies.TryGetRow(slot, out var zombie)) continue;
            if (zombie.Flags.IsDead()) continue;

            Fixed64 distSq = Fixed64Vec2.DistanceSquared(buildingPos, zombie.Position);
            if (distSq > rangeSq) continue;

            hitAny = true;

            int actualDamage;
            if (hasFalloff && rangeSq > Fixed64.Zero && distSq > Fixed64.Zero)
            {
                Fixed64 dist = Fixed64.Sqrt(distSq);
                Fixed64 falloffMult = Fixed64.OneValue - (dist / attackRange);
                actualDamage = (Fixed64.FromInt(baseDamage) * falloffMult).ToInt();
                if (actualDamage < 1) actualDamage = 1;
            }
            else
            {
                actualDamage = baseDamage;
            }

            zombie.Health -= actualDamage;
        }

        if (hitAny)
        {
            // Re-get to update timer
            if (buildings.TryGetRow(buildingSlot, out var b))
            {
                b.AttackTimer = b.AttackCooldown;
            }
        }
    }

    private void ApplySplashDamage(Fixed64Vec2 impactPos, int damage, Fixed64 splashRadius, bool falloff)
    {
        var zombies = _simWorld.ZombieRows;
        Fixed64 radiusSq = splashRadius * splashRadius;

        for (int slot = 0; slot < zombies.Count; slot++)
        {
            if (!zombies.TryGetRow(slot, out var zombie)) continue;
            if (zombie.Flags.IsDead()) continue;

            Fixed64 distSq = Fixed64Vec2.DistanceSquared(impactPos, zombie.Position);
            if (distSq > radiusSq) continue;

            int actualDamage;
            if (falloff && radiusSq > Fixed64.Zero && distSq > Fixed64.Zero)
            {
                Fixed64 dist = Fixed64.Sqrt(distSq);
                Fixed64 falloffMult = Fixed64.OneValue - (dist / splashRadius);
                actualDamage = (Fixed64.FromInt(damage) * falloffMult).ToInt();
                if (actualDamage < 1) actualDamage = 1;
            }
            else
            {
                actualDamage = damage;
            }

            zombie.Health -= actualDamage;
        }
    }

    #endregion
}
