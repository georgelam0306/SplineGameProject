using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Calculates effective stats for all entities each tick.
/// Runs early in the tick so other systems can read pre-calculated effective stats.
///
/// Order of operations:
/// 0. Update power network connectivity
/// 1. Reset player max storage to base values
/// 2. Reset building effective stats to base (from CachedStat)
/// 3. Apply storage buildings → update player MaxGold, MaxWood, etc.
/// 4. Apply tech modifiers → multiply building effective stats
/// 5. Apply area-effect buildings → boost nearby buildings
/// </summary>
public sealed class ModifierApplicationSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly PowerNetworkService _powerNetwork;

    // Default base storage values per player
    private const int BaseMaxGold = 500;
    private const int BaseMaxWood = 200;
    private const int BaseMaxStone = 200;
    private const int BaseMaxIron = 100;
    private const int BaseMaxOil = 50;
    private const int BaseMaxFood = 100;
    private const int BaseMaxPopulation = 0; // Population comes only from housing buildings

    public ModifierApplicationSystem(
        SimWorld world,
        GameDataManager<GameDocDb> gameData,
        PowerNetworkService powerNetwork) : base(world)
    {
        _gameData = gameData;
        _powerNetwork = powerNetwork;
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        var gameResources = World.GameResourcesRows;
        var buildings = World.BuildingRows;

        // Step 1: Reset global max storage to base
        ResetGlobalMaxStorage(gameResources);

        // Step 2: Reset building effective stats to base (from CachedStat values)
        ResetBuildingEffectiveStats(buildings);

        // Step 2.5: Update power state from PowerGrid (derived system already rebuilt)
        UpdateBuildingPowerState(buildings);

        // Step 3: Apply environment-based production bonuses
        ApplyEnvironmentBonuses(buildings);

        // Step 4: Apply storage buildings → update global max resources
        ApplyStorageBuildings(gameResources, buildings);

        // Step 5: Apply tech modifiers → multiply building effective stats
        ApplyTechModifiers(gameResources, buildings);

        // Step 6: Apply area-effect buildings → boost nearby buildings
        ApplyAreaEffectBuildings(buildings);
    }

    private void ResetGlobalMaxStorage(GameResourcesRowTable gameResources)
    {
        var resources = gameResources.GetRowBySlot(0);
        resources.MaxGold = BaseMaxGold;
        resources.MaxWood = BaseMaxWood;
        resources.MaxStone = BaseMaxStone;
        resources.MaxIron = BaseMaxIron;
        resources.MaxOil = BaseMaxOil;
        resources.MaxFood = BaseMaxFood;
        resources.MaxPopulation = BaseMaxPopulation;
    }

    private static void ResetBuildingEffectiveStats(BuildingRowTable buildings)
    {
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Reset effective stats to base (CachedStat values)
            building.EffectiveGeneratesGold = building.GeneratesGold;
            building.EffectiveGeneratesWood = building.GeneratesWood;
            building.EffectiveGeneratesStone = building.GeneratesStone;
            building.EffectiveGeneratesIron = building.GeneratesIron;
            building.EffectiveGeneratesOil = building.GeneratesOil;
            building.EffectiveGeneratesFood = building.GeneratesFood;
        }
    }

    private void UpdateBuildingPowerState(BuildingRowTable buildings)
    {
        var db = _gameData.Db;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            ref readonly var typeData = ref db.BuildingTypeData.FindById((int)building.TypeId);

            // Generators (negative power consumption) and zero-power buildings are always powered
            if (typeData.PowerConsumption <= 0)
            {
                building.Flags |= BuildingFlags.IsPowered;
            }
            else
            {
                // Check if building is within powered tile range
                bool isPowered = _powerNetwork.IsBuildingPowered(
                    building.TileX, building.TileY, building.Width, building.Height);

                if (isPowered)
                {
                    building.Flags |= BuildingFlags.IsPowered;
                }
                else
                {
                    building.Flags &= ~BuildingFlags.IsPowered;
                }
            }
        }
    }

    private static void ApplyEnvironmentBonuses(BuildingRowTable buildings)
    {
        // Environment-dependent buildings (Sawmill, Quarry, Farm, etc.) have base rate = 0
        // and get their production from BaseRatePerNode × EnvironmentNodeCount
        const int AnyOreRequirement = -2;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Skip non-environment buildings
            if (building.BaseRatePerNode <= 0) continue;
            if (building.EnvironmentNodeCount == 0) continue;

            int bonus = building.BaseRatePerNode * building.EnvironmentNodeCount;

            // Terrain-based buildings - Sawmill produces Wood, others produce Food
            if (building.RequiredTerrainType >= 0)
            {
                if (building.TypeId == BuildingTypeId.Sawmill)
                {
                    building.EffectiveGeneratesWood += bonus;
                }
                else
                {
                    building.EffectiveGeneratesFood += bonus;
                }
                continue;
            }

            // Resource node-based buildings - determine output type
            int resourceType = building.RequiredResourceNodeType;

            // Adaptive quarry: use detected resource type
            if (resourceType == AnyOreRequirement)
            {
                resourceType = building.DetectedResourceType;
            }

            // Apply bonus to the appropriate resource
            switch (resourceType)
            {
                case 0: // Gold
                    building.EffectiveGeneratesGold += bonus;
                    break;
                case 2: // Wood
                    building.EffectiveGeneratesWood += bonus;
                    break;
                case 3: // Stone
                    building.EffectiveGeneratesStone += bonus;
                    break;
                case 4: // Iron
                    building.EffectiveGeneratesIron += bonus;
                    break;
                case 5: // Oil
                    building.EffectiveGeneratesOil += bonus;
                    break;
            }
        }
    }

    private static void ApplyStorageBuildings(GameResourcesRowTable gameResources, BuildingRowTable buildings)
    {
        // Accumulate storage contributions from all active buildings into global pool
        int maxGoldBonus = 0;
        int maxWoodBonus = 0;
        int maxStoneBonus = 0;
        int maxIronBonus = 0;
        int maxOilBonus = 0;
        int maxFoodBonus = 0;
        int maxPopulationBonus = 0;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            maxGoldBonus += building.ProvidesMaxGold;
            maxWoodBonus += building.ProvidesMaxWood;
            maxStoneBonus += building.ProvidesMaxStone;
            maxIronBonus += building.ProvidesMaxIron;
            maxOilBonus += building.ProvidesMaxOil;
            maxFoodBonus += building.ProvidesMaxFood;
            maxPopulationBonus += building.ProvidesMaxPopulation;
        }

        // Apply bonuses to global resources
        var resources = gameResources.GetRowBySlot(0);
        resources.MaxGold += maxGoldBonus;
        resources.MaxWood += maxWoodBonus;
        resources.MaxStone += maxStoneBonus;
        resources.MaxIron += maxIronBonus;
        resources.MaxOil += maxOilBonus;
        resources.MaxFood += maxFoodBonus;
        resources.MaxPopulation += maxPopulationBonus;
    }

    private void ApplyTechModifiers(GameResourcesRowTable gameResources, BuildingRowTable buildings)
    {
        var techDb = _gameData.Db.TechTypeData;

        // Calculate global tech multipliers from shared UnlockedTech
        Fixed64 goldMult = Fixed64.OneValue;
        Fixed64 woodMult = Fixed64.OneValue;
        Fixed64 stoneMult = Fixed64.OneValue;
        Fixed64 ironMult = Fixed64.OneValue;
        Fixed64 oilMult = Fixed64.OneValue;
        Fixed64 foodMult = Fixed64.OneValue;

        var resources = gameResources.GetRowBySlot(0);
        ulong unlockedTech = resources.UnlockedTech;

        if (unlockedTech != 0)
        {
            // Iterate through set bits in unlockedTech (max 64 techs)
            ulong remaining = unlockedTech;
            while (remaining != 0)
            {
                // Get the lowest set bit index
                int techId = System.Numerics.BitOperations.TrailingZeroCount(remaining);
                remaining &= remaining - 1; // Clear the lowest set bit

                ref readonly var tech = ref techDb.FindById(techId);

                // Add multiplier bonuses (additive stacking)
                goldMult += tech.GoldGenMult;
                woodMult += tech.WoodGenMult;
                stoneMult += tech.StoneGenMult;
                ironMult += tech.IronGenMult;
                oilMult += tech.OilGenMult;
                foodMult += tech.FoodGenMult;
            }
        }

        // Apply multipliers to all building effective stats
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Apply multipliers (convert from Fixed64 to int)
            if (building.EffectiveGeneratesGold > 0)
            {
                building.EffectiveGeneratesGold = (Fixed64.FromInt(building.EffectiveGeneratesGold) * goldMult).ToInt();
            }
            if (building.EffectiveGeneratesWood > 0)
            {
                building.EffectiveGeneratesWood = (Fixed64.FromInt(building.EffectiveGeneratesWood) * woodMult).ToInt();
            }
            if (building.EffectiveGeneratesStone > 0)
            {
                building.EffectiveGeneratesStone = (Fixed64.FromInt(building.EffectiveGeneratesStone) * stoneMult).ToInt();
            }
            if (building.EffectiveGeneratesIron > 0)
            {
                building.EffectiveGeneratesIron = (Fixed64.FromInt(building.EffectiveGeneratesIron) * ironMult).ToInt();
            }
            if (building.EffectiveGeneratesOil > 0)
            {
                building.EffectiveGeneratesOil = (Fixed64.FromInt(building.EffectiveGeneratesOil) * oilMult).ToInt();
            }
            if (building.EffectiveGeneratesFood > 0)
            {
                building.EffectiveGeneratesFood = (Fixed64.FromInt(building.EffectiveGeneratesFood) * foodMult).ToInt();
            }
        }
    }

    private static void ApplyAreaEffectBuildings(BuildingRowTable buildings)
    {
        // For each area effect building, boost nearby production buildings
        // O(buildings²) - acceptable for ~100 buildings

        for (int srcSlot = 0; srcSlot < buildings.Count; srcSlot++)
        {
            var source = buildings.GetRowBySlot(srcSlot);
            if (source.Flags.IsDead()) continue;
            if (!source.Flags.HasFlag(BuildingFlags.IsActive)) continue;
            if (source.EffectRadius <= Fixed64.Zero) continue;

            // Check if source has any area bonus
            bool hasAnyBonus = source.AreaGoldBonus > Fixed64.Zero
                || source.AreaWoodBonus > Fixed64.Zero
                || source.AreaStoneBonus > Fixed64.Zero
                || source.AreaIronBonus > Fixed64.Zero
                || source.AreaOilBonus > Fixed64.Zero
                || source.AreaFoodBonus > Fixed64.Zero;
            if (!hasAnyBonus) continue;

            Fixed64 radiusSq = source.EffectRadius * source.EffectRadius;
            var sourcePos = source.Position;
            byte owner = source.OwnerPlayerId;

            // Apply per-resource bonuses to all buildings in range (same owner only)
            for (int targetSlot = 0; targetSlot < buildings.Count; targetSlot++)
            {
                if (targetSlot == srcSlot) continue; // Don't boost self

                var target = buildings.GetRowBySlot(targetSlot);
                if (target.Flags.IsDead()) continue;
                if (!target.Flags.HasFlag(BuildingFlags.IsActive)) continue;
                if (target.OwnerPlayerId != owner) continue; // Same owner only

                // Check distance
                Fixed64 distSq = Fixed64Vec2.DistanceSquared(sourcePos, target.Position);
                if (distSq > radiusSq) continue;

                // Apply per-resource bonuses (multiplicative on top of existing effective stats)
                if (source.AreaGoldBonus > Fixed64.Zero && target.EffectiveGeneratesGold > 0)
                {
                    Fixed64 mult = Fixed64.OneValue + source.AreaGoldBonus;
                    target.EffectiveGeneratesGold = (Fixed64.FromInt(target.EffectiveGeneratesGold) * mult).ToInt();
                }
                if (source.AreaWoodBonus > Fixed64.Zero && target.EffectiveGeneratesWood > 0)
                {
                    Fixed64 mult = Fixed64.OneValue + source.AreaWoodBonus;
                    target.EffectiveGeneratesWood = (Fixed64.FromInt(target.EffectiveGeneratesWood) * mult).ToInt();
                }
                if (source.AreaStoneBonus > Fixed64.Zero && target.EffectiveGeneratesStone > 0)
                {
                    Fixed64 mult = Fixed64.OneValue + source.AreaStoneBonus;
                    target.EffectiveGeneratesStone = (Fixed64.FromInt(target.EffectiveGeneratesStone) * mult).ToInt();
                }
                if (source.AreaIronBonus > Fixed64.Zero && target.EffectiveGeneratesIron > 0)
                {
                    Fixed64 mult = Fixed64.OneValue + source.AreaIronBonus;
                    target.EffectiveGeneratesIron = (Fixed64.FromInt(target.EffectiveGeneratesIron) * mult).ToInt();
                }
                if (source.AreaOilBonus > Fixed64.Zero && target.EffectiveGeneratesOil > 0)
                {
                    Fixed64 mult = Fixed64.OneValue + source.AreaOilBonus;
                    target.EffectiveGeneratesOil = (Fixed64.FromInt(target.EffectiveGeneratesOil) * mult).ToInt();
                }
                if (source.AreaFoodBonus > Fixed64.Zero && target.EffectiveGeneratesFood > 0)
                {
                    Fixed64 mult = Fixed64.OneValue + source.AreaFoodBonus;
                    target.EffectiveGeneratesFood = (Fixed64.FromInt(target.EffectiveGeneratesFood) * mult).ToInt();
                }
            }
        }
    }
}
