using System.Collections.Generic;
using System.Diagnostics;
using Pure.DI;
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

namespace Catrillion;

partial class GameComposition
{
    [Conditional("DI")]
    static void SetupSimulation() => DI.Setup()
        // Simulation world and core components
        .Bind().As(Singleton).To<SimWorld>()
        .Bind().As(Singleton).To<EntitySpawner>()
        // Derived state systems (rebuilt on rollback, not serialized)
        // Register individual derived systems first
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
        .Bind<MultiPlayerInputBuffer<GameInput>>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out PlayerConfig config);
            return new MultiPlayerInputBuffer<GameInput>(InputBufferFrames, config.MaxPlayers);
        })
        .Bind<RollbackManager<GameInput>>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out SimWorld simWorld);
            ctx.Inject(out MultiPlayerInputBuffer<GameInput> inputBuffer);
            ctx.Inject(out PlayerConfig config);
            ctx.Inject(out Serilog.ILogger logger);

            var rm = new RollbackManager<GameInput>(simWorld, inputBuffer, logger);
            rm.SetPlayerCount(config.PlayerCount);
            rm.SetLocalPlayerId(config.LocalPlayerSlot);
            return rm;
        })
        // Game data (static data tables)
        .Bind().As(Singleton).To(ctx => GameDataManagerFactory.Create())
        // Simulation services
        .Bind<IPoolRegistry>().As(Singleton).To<World>()
        .Bind<IWorldProvider>().As(Singleton).To<TileWorldProvider>()
        // Flow field services - use derived system wrappers
        // Original implementations (wrapped by derived systems)
        .Bind().As(Singleton).To<ZoneGraph>()
        .Bind().As(Singleton).To<ZoneFlowService>()
        // Interface bindings point to derived system wrappers
        .Bind<FlowField.IZoneGraph>().As(Singleton).To<ZoneGraphSystem>()
        .Bind<FlowField.IZoneFlowService>().As(Singleton).To<ZoneFlowSystem>()
        .Bind().As(Singleton).To<CenterFlowService>()
        .Bind().As(Singleton).To<ZoneTierService>()
        // Constraint system services
        .Bind().As(Singleton).To<PowerNetworkService>()
        .Bind().As(Singleton).To<BuildingConstraintValidator>()
        // Zombie spawn service (shared between EnemySpawnSystem and WaveSpawnSystem)
        .Bind().As(Singleton).To<ZombieSpawnerService>()
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
        .Bind().As(Singleton).To<PlayerStateSpawnSystem>()
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
        .Bind().As(Singleton).To<Simulation.Systems.RTS.CombatUnitTargetAcquisitionSystem>()
        .Bind().As(Singleton).To<Simulation.Systems.RTS.CombatUnitCombatSystem>()
        .Bind().As(Singleton).To<Simulation.Systems.RTS.BuildingCombatSystem>()
        .Bind().As(Singleton).To<ZombieCombatSystem>()
        .Bind().As(Singleton).To<ProjectileSystem>()
        // Wave systems
        .Bind().As(Singleton).To<DayNightSystem>()
        .Bind().As(Singleton).To<WaveSchedulerSystem>()
        .Bind().As(Singleton).To<WaveSpawnSystem>()
        // System list (order matters for determinism)
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
            ctx.Inject(out PlayerStateSpawnSystem playerStateSpawn);
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
            ctx.Inject(out Simulation.Systems.RTS.CombatUnitTargetAcquisitionSystem combatTargetAcquisition);
            ctx.Inject(out Simulation.Systems.RTS.CombatUnitCombatSystem combatUnitCombat);
            ctx.Inject(out Simulation.Systems.RTS.BuildingCombatSystem buildingCombat);
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
                terrainGen,    // Must run before spawn systems
                resourceSpawn, // Spawn resources after terrain
                unitSpawn,
                buildingSpawn,
                playerStateSpawn,  // Must run before SelectionSystem
                buildingPlacement,
                buildingConstruction,   // Progress building construction
                trainUnitCommand,       // Process unit training commands
                unitProduction,         // Progress unit production queues
                flowFieldInvalidation,  // Flush pending invalidations before movement systems
                // Economy systems - run early to calculate effective stats
                modifierApplication,    // Calculate effective stats from modifiers
                resourceGeneration,     // Generate resources using effective stats
                resourceUpkeep,         // Deduct upkeep costs
                netRateCalculation,     // Calculate net rates for UI display
                // Research systems - after economy, before enemy spawn
                researchCommand,        // Process research commands from input
                research,               // Tick research progress
                upgradeCommand,         // Process building upgrade commands
                destroyBuildingCommand, // Process building destroy commands
                repairCommand,          // Process repair start/cancel commands
                buildingRepair,         // Tick building repair progress
                enemySpawn,
                // Wave systems - after enemy spawn, before selection
                dayNight,        // Track day progression
                waveScheduler,   // Schedule and trigger waves
                waveSpawn,       // Spawn wave zombies
                selection,
                moveCommand,
                garrisonCommand,           // Process garrison enter commands
                combatMovement,            // Units move toward destinations (including garrison targets)
                garrisonEnter,             // Units entering buildings
                garrisonExit,              // Units exiting buildings
                combatTargetAcquisition,   // Target acquisition (uses building position for garrisoned units)
                // Fog of war - update visibility after movement
                fogOfWarUpdate,
                // Original systems
                velocityReset,
                // Threat grid - decay first, then update from sources
                threatDecay,
                threatUpdate,
                // Noise systems - decay, then zombie attraction update
                noiseDecay,
                noiseAttraction,
                // Zombie AI - transitions then movement
                zombieStateTransition,
                zombieMovement,
                rvo,           // RVO collision avoidance (close-range)
                separation,    // Density-based separation (long-range)
                moveableApply,
                // Combat systems - after movement, before death
                combatUnitCombat, // Units fire projectiles
                buildingCombat,   // Turrets fire projectiles
                projectile,       // Projectiles move and deal damage
                zombieCombat,     // Zombies deal melee damage
                mortalDeath,
                buildingDeath,
                gameEndCondition,      // Check win/lose after death systems
                garrisonPositionSync,  // Sync garrisoned unit positions last for EndFrame rendering
            };
        })
        .Bind().As(Singleton).To<GameSimulation>()
        .Bind<ReplayManager<GameInput>>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out ReplayConfig replayConfig);
            ctx.Inject(out RollbackManager<GameInput> rollbackManager);
            ctx.Inject(out PlayerConfig playerConfig);

            var replayManager = new ReplayManager<GameInput>();
            if (!string.IsNullOrEmpty(replayConfig.ReplayFilePath))
            {
                int playerCount = replayManager.StartReplay(replayConfig.ReplayFilePath);
                rollbackManager.SetPlayerCount(playerCount);
            }
            else
            {
                // Always record - use explicit path or generate default with timestamp and player ID
                string recordPath = replayConfig.RecordFilePath;
                if (string.IsNullOrEmpty(recordPath))
                {
                    // Ensure Logs directory exists
                    System.IO.Directory.CreateDirectory("Logs");
                    string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    recordPath = $"Logs/replay_{timestamp}_p{playerConfig.LocalPlayerSlot}.bin";
                }
                // Defer recording start until after session seed is known (in Game.Restart)
                // Pass local player slot so RestartRecording can generate new paths
                replayManager.SetPendingRecordPath(recordPath, playerConfig.LocalPlayerSlot);
            }
            return replayManager;
        });
}
