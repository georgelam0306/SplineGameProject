using System;
using System.Collections.Generic;
using System.Diagnostics;
using Pure.DI;
using Serilog;
using Catrillion;
using Catrillion.Core;
using Catrillion.Entities;
using Catrillion.GameData;
using Catrillion.Rollback;
using Catrillion.Simulation;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.DerivedSystems;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems;
using Catrillion.Simulation.Systems.RTS;
using Catrillion.Simulation.Systems.SimTable;
using Core;
using FlowField;
using DerpTech.Rollback;
using Pooled.Runtime;
using static Pure.DI.Lifetime;

namespace Catrillion.Headless;

/// <summary>
/// Minimal composition for headless replay.
/// Creates only simulation components without UI/ECS/Raylib dependencies.
/// Mirrors GameComposition.Simulation bindings for deterministic replay.
/// </summary>
public sealed partial class HeadlessComposition : IGameComposition
{
    private const int InputBufferFrames = 256;

    [Conditional("DI")]
    private static void Setup() => DI.Setup(nameof(HeadlessComposition))
        // Arguments
        .Arg<ReplayConfig>("replayConfig")
        .Arg<SessionSeed>("sessionSeed")
        // Simulation world and core components
        .Bind().As(Singleton).To<SimWorld>()
        .Bind().As(Singleton).To<EntitySpawner>()
        // Game data (static data tables)
        .Bind().As(Singleton).To(ctx => GameDataManagerFactory.Create())
        // Simulation services
        .Bind<IPoolRegistry>().As(Singleton).To<World>()
        .Bind().As(Singleton).To<TerrainDataService>()
        .Bind<IWorldProvider>().As(Singleton).To<TileWorldProvider>()
        // Derived state systems (rebuilt on rollback, not serialized)
        .Bind().As(Singleton).To<BuildingOccupancyGrid>()
        .Bind().As(Singleton).To<PowerGrid>()
        .Bind().As(Singleton).To<ZoneGraphSystem>()
        .Bind().As(Singleton).To<ZoneFlowSystem>()
        // DerivedSystemRunner - generated from Setup method in DerivedSystemRunner.cs
        // Constructor is generated, we just need to wire up dependencies
        .Bind().As(Singleton).To(ctx =>
        {
            ctx.Inject(out SimWorld simWorld);
            ctx.Inject(out BuildingOccupancyGrid buildingOccupancy);
            ctx.Inject(out PowerGrid powerGrid);
            ctx.Inject(out ZoneGraphSystem zoneGraph);
            ctx.Inject(out ZoneFlowSystem zoneFlow);
            return new DerivedSystemRunner(simWorld, buildingOccupancy, powerGrid, zoneGraph, zoneFlow);
        })
        // Original flow field implementations (wrapped by derived systems)
        .Bind().As(Singleton).To<ZoneGraph>()
        .Bind().As(Singleton).To<ZoneFlowService>()
        // Interface bindings point to derived system wrappers
        .Bind<IZoneGraph>().As(Singleton).To<ZoneGraphSystem>()
        .Bind<IZoneFlowService>().As(Singleton).To<ZoneFlowSystem>()
        .Bind().As(Singleton).To<CenterFlowService>()
        .Bind().As(Singleton).To<ZoneTierService>()
        // Constraint system services
        .Bind().As(Singleton).To<PowerNetworkService>()
        .Bind().As(Singleton).To<BuildingConstraintValidator>()
        // Zombie spawn service
        .Bind().As(Singleton).To<ZombieSpawnerService>()
        // Input buffer and rollback
        .Bind<MultiPlayerInputBuffer<GameInput>>().As(Singleton).To(ctx =>
        {
            return new MultiPlayerInputBuffer<GameInput>(InputBufferFrames, 8); // Max 8 players
        })
        .Bind<RollbackManager<GameInput>>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out SimWorld simWorld);
            ctx.Inject(out MultiPlayerInputBuffer<GameInput> inputBuffer);
            return new RollbackManager<GameInput>(simWorld, inputBuffer, Log.Logger);
        })
        // Replay manager - start replay from file
        .Bind<ReplayManager<GameInput>>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out ReplayConfig replayConfig);
            ctx.Inject(out RollbackManager<GameInput> rollbackManager);

            var replayManager = new ReplayManager<GameInput>();
            if (!string.IsNullOrEmpty(replayConfig.ReplayFilePath))
            {
                int playerCount = replayManager.StartReplay(replayConfig.ReplayFilePath);
                rollbackManager.SetPlayerCount(playerCount);
                rollbackManager.SetLocalPlayerId(0);
            }
            return replayManager;
        })
        // SimTable systems
        .Bind().As(Singleton).To<CountdownSystem>()
        .Bind().As(Singleton).To<VelocityResetSystem>()
        .Bind().As(Singleton).To<ThreatGridDecaySystem>()
        .Bind().As(Singleton).To<ThreatGridUpdateSystem>()
        .Bind().As(Singleton).To<NoiseDecaySystem>()
        .Bind().As(Singleton).To<NoiseAttractionUpdateSystem>()
        .Bind().As(Singleton).To<ZombieStateTransitionSystem>()
        .Bind().As(Singleton).To<ZombieMovementSystem>()
        .Bind().As(Singleton).To<RVOSystem>()
        .Bind().As(Singleton).To<SeparationSystem>()
        .Bind().As(Singleton).To<MoveableApplyMovementSystem>()
        .Bind().As(Singleton).To<MortalDeathSystem>()
        .Bind().As(Singleton).To<BuildingDeathSystem>()
        // RTS systems
        .Bind().As(Singleton).To<TerrainGenerationSystem>()
        .Bind().As(Singleton).To<ResourceNodeSpawnSystem>()
        .Bind().As(Singleton).To<UnitSpawnSystem>()
        .Bind().As(Singleton).To<BuildingSpawnSystem>()
        .Bind().As(Singleton).To<EnemySpawnSystem>()
        .Bind().As(Singleton).To<SelectionSystem>()
        .Bind().As(Singleton).To<MoveCommandSystem>()
        .Bind().As(Singleton).To<GarrisonCommandSystem>()
        .Bind().As(Singleton).To<GarrisonEnterSystem>()
        .Bind().As(Singleton).To<GarrisonExitSystem>()
        .Bind().As(Singleton).To<GarrisonPositionSyncSystem>()
        .Bind().As(Singleton).To<CombatUnitMovementSystem>()
        .Bind().As(Singleton).To<BuildingPlacementSystem>()
        .Bind().As(Singleton).To<BuildingConstructionSystem>()
        .Bind().As(Singleton).To<TrainUnitCommandSystem>()
        .Bind().As(Singleton).To<UnitProductionSystem>()
        .Bind().As(Singleton).To<FlowFieldInvalidationSystem>()
        .Bind().As(Singleton).To<FogOfWarUpdateSystem>()
        // Economy systems
        .Bind().As(Singleton).To<ModifierApplicationSystem>()
        .Bind().As(Singleton).To<ResourceGenerationSystem>()
        .Bind().As(Singleton).To<ResourceUpkeepSystem>()
        .Bind().As(Singleton).To<NetRateCalculationSystem>()
        // Research systems
        .Bind().As(Singleton).To<ResearchCommandSystem>()
        .Bind().As(Singleton).To<ResearchSystem>()
        // Upgrade system
        .Bind().As(Singleton).To<UpgradeCommandSystem>()
        // Building destroy/repair systems
        .Bind().As(Singleton).To<DestroyBuildingCommandSystem>()
        .Bind().As(Singleton).To<RepairCommandSystem>()
        .Bind().As(Singleton).To<BuildingRepairSystem>()
        // Game end conditions
        .Bind().As(Singleton).To<GameEndConditionSystem>()
        // Combat systems
        .Bind().As(Singleton).To<CombatUnitTargetAcquisitionSystem>()
        .Bind().As(Singleton).To<CombatUnitCombatSystem>()
        .Bind().As(Singleton).To<BuildingCombatSystem>()
        .Bind().As(Singleton).To<ZombieCombatSystem>()
        .Bind().As(Singleton).To<ProjectileSystem>()
        // Wave systems
        .Bind().As(Singleton).To<DayNightSystem>()
        .Bind().As(Singleton).To<WaveSchedulerSystem>()
        .Bind().As(Singleton).To<WaveSpawnSystem>()
        // System list (order matters for determinism) - must match GameComposition.Simulation
        .Bind<IReadOnlyList<SimTableSystem>>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out CountdownSystem countdown);
            ctx.Inject(out VelocityResetSystem velocityReset);
            ctx.Inject(out ThreatGridDecaySystem threatDecay);
            ctx.Inject(out ThreatGridUpdateSystem threatUpdate);
            ctx.Inject(out NoiseDecaySystem noiseDecay);
            ctx.Inject(out NoiseAttractionUpdateSystem noiseAttraction);
            ctx.Inject(out ZombieStateTransitionSystem zombieStateTransition);
            ctx.Inject(out ZombieMovementSystem zombieMovement);
            ctx.Inject(out RVOSystem rvo);
            ctx.Inject(out SeparationSystem separation);
            ctx.Inject(out MoveableApplyMovementSystem moveableApply);
            ctx.Inject(out MortalDeathSystem mortalDeath);
            ctx.Inject(out BuildingDeathSystem buildingDeath);
            // RTS systems
            ctx.Inject(out TerrainGenerationSystem terrainGen);
            ctx.Inject(out ResourceNodeSpawnSystem resourceSpawn);
            ctx.Inject(out UnitSpawnSystem unitSpawn);
            ctx.Inject(out BuildingSpawnSystem buildingSpawn);
            ctx.Inject(out EnemySpawnSystem enemySpawn);
            ctx.Inject(out SelectionSystem selection);
            ctx.Inject(out MoveCommandSystem moveCommand);
            ctx.Inject(out GarrisonCommandSystem garrisonCommand);
            ctx.Inject(out GarrisonEnterSystem garrisonEnter);
            ctx.Inject(out GarrisonExitSystem garrisonExit);
            ctx.Inject(out GarrisonPositionSyncSystem garrisonPositionSync);
            ctx.Inject(out CombatUnitMovementSystem combatMovement);
            ctx.Inject(out BuildingPlacementSystem buildingPlacement);
            ctx.Inject(out BuildingConstructionSystem buildingConstruction);
            ctx.Inject(out TrainUnitCommandSystem trainUnitCommand);
            ctx.Inject(out UnitProductionSystem unitProduction);
            ctx.Inject(out FlowFieldInvalidationSystem flowFieldInvalidation);
            ctx.Inject(out FogOfWarUpdateSystem fogOfWarUpdate);
            // Economy systems
            ctx.Inject(out ModifierApplicationSystem modifierApplication);
            ctx.Inject(out ResourceGenerationSystem resourceGeneration);
            ctx.Inject(out ResourceUpkeepSystem resourceUpkeep);
            ctx.Inject(out NetRateCalculationSystem netRateCalculation);
            // Research systems
            ctx.Inject(out ResearchCommandSystem researchCommand);
            ctx.Inject(out ResearchSystem research);
            // Upgrade system
            ctx.Inject(out UpgradeCommandSystem upgradeCommand);
            // Building destroy/repair systems
            ctx.Inject(out DestroyBuildingCommandSystem destroyBuildingCommand);
            ctx.Inject(out RepairCommandSystem repairCommand);
            ctx.Inject(out BuildingRepairSystem buildingRepair);
            // Game end conditions
            ctx.Inject(out GameEndConditionSystem gameEndCondition);
            // Combat systems
            ctx.Inject(out CombatUnitTargetAcquisitionSystem combatTargetAcquisition);
            ctx.Inject(out CombatUnitCombatSystem combatUnitCombat);
            ctx.Inject(out BuildingCombatSystem buildingCombat);
            ctx.Inject(out ZombieCombatSystem zombieCombat);
            ctx.Inject(out ProjectileSystem projectile);
            // Wave systems
            ctx.Inject(out DayNightSystem dayNight);
            ctx.Inject(out WaveSchedulerSystem waveScheduler);
            ctx.Inject(out WaveSpawnSystem waveSpawn);
            return new List<SimTableSystem>
            {
                countdown,
                // RTS systems - terrain first, then resources, then spawn, then placement, then flush flow fields
                terrainGen,
                resourceSpawn,
                unitSpawn,
                buildingSpawn,
                buildingPlacement,
                buildingConstruction,
                trainUnitCommand,
                unitProduction,
                flowFieldInvalidation,
                // Economy systems
                modifierApplication,
                resourceGeneration,
                resourceUpkeep,
                netRateCalculation,
                // Research systems
                researchCommand,
                research,
                upgradeCommand,
                destroyBuildingCommand,
                repairCommand,
                buildingRepair,
                enemySpawn,
                // Wave systems
                dayNight,
                waveScheduler,
                waveSpawn,
                selection,
                moveCommand,
                garrisonCommand,
                combatMovement,
                garrisonEnter,
                garrisonExit,
                combatTargetAcquisition,
                fogOfWarUpdate,
                // Original systems
                velocityReset,
                threatDecay,
                threatUpdate,
                noiseDecay,
                noiseAttraction,
                zombieStateTransition,
                zombieMovement,
                rvo,
                separation,
                moveableApply,
                // Combat systems
                combatUnitCombat,
                buildingCombat,
                projectile,
                zombieCombat,
                mortalDeath,
                buildingDeath,
                gameEndCondition,
                garrisonPositionSync,
            };
        })
        .Bind().As(Singleton).To<GameSimulation>()
        // Roots
        .Root<GameSimulation>("GameSimulation")
        .Root<RollbackManager<GameInput>>("RollbackManager")
        .Root<ReplayManager<GameInput>>("ReplayManager")
        .Root<DerivedSystemRunner>("DerivedSystemRunner")
        .Root<TerrainDataService>("TerrainDataService");

    public HeadlessComposition(string replayFilePath)
        : this(new ReplayConfig(replayFilePath, null), new SessionSeed(0))
    {
    }

    // Note: Dispose is generated by Pure.DI
}
