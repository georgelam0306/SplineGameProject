using System.Diagnostics;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Pure.DI;
using Catrillion.Camera;
using Catrillion.Camera.Systems;
using Catrillion.Config;
using Catrillion.Input;
using Catrillion.Rendering.DualGrid;
using Catrillion.Rendering.DualGrid.Systems;
using Catrillion.Rendering;
using Catrillion.Rendering.Services;
using Catrillion.Rendering.Systems;
using Catrillion.Simulation.Services;
using Catrillion.UI;
using FlowField;
using static Pure.DI.Lifetime;

namespace Catrillion;

partial class GameComposition
{
    [Conditional("DI")]
    static void SetupSystems() => DI.Setup()
        // Input and camera managers
        .Bind().As(Singleton).To<CameraManager>()
        .Bind().As(Singleton).To<UIInputManager>()
        .Bind().As(Singleton).To<InputActionBuffer>()
        .Bind().As(Singleton).To<GameInputManager>()
        // Terrain generation service
        .Bind().As(Singleton).To<TerrainDataService>()
        // Environment service for building placement and production
        .Bind().As(Singleton).To<EnvironmentService>()
        // Render interpolation state (for simulation-render decoupling)
        .Bind().As(Singleton).To<RenderInterpolation>()
        // Rendering services
        .Bind().As(Singleton).To(_ => new ChunkRegistry(GameConfig.Map.ChunkSize, GameConfig.Map.TileSize))
        .Bind().As(Singleton).To(ctx =>
        {
            ctx.Inject(out TerrainDataService terrainData);
            return new ChunkGenerator(GameConfig.Map.ChunkSize, GameConfig.Map.TileSize, terrainData);
        })
        .Bind().As(Singleton).To<DualGridTextureService>()
        // Update systems
        .Bind().As(Singleton).To<CameraPanSystem>()
        .Bind().As(Singleton).To<ChunkManagerSystem>()
        .Bind().As(Singleton).To<DualGridAtlasLoadSystem>()
        .Bind().As(Singleton).To<FogOfWarVisibilitySyncSystem>()
        // Render systems
        .Bind().As(Singleton).To<RenderInterpolationSystem>()
        .Bind().As(Singleton).To<ChunkRenderCacheSystem>()
        .Bind().As(Singleton).To<WorldCameraBeginSystem>()
        .Bind().As(Singleton).To<RenderDualGridSystem>()
        .Bind().As(Singleton).To<SelectionCircleRenderSystem>()
        .Bind().As(Singleton).To<SpriteRenderSystem>()
        .Bind().As(Singleton).To<CombatUnitRenderSystem>()
        .Bind().As(Singleton).To<BuildingRenderSystem>()
        .Bind().As(Singleton).To<BuildingPreviewRenderSystem>()
        .Bind().As(Singleton).To<AreaEffectVisualizationSystem>()
        .Bind().As(Singleton).To<HealthBarRenderSystem>()
        .Bind().As(Singleton).To<ProductionProgressRenderSystem>()
        .Bind().As(Singleton).To<BuildingHoverSystem>()
        .Bind().As(Singleton).To<ProjectileRenderSystem>()
        .Bind().As(Singleton).To<ResourceNodeRenderSystem>()
        .Bind().As(Singleton).To<TreeOverlayRenderSystem>()
        .Bind().As(Singleton).To<PowerGridRenderSystem>()
        .Bind().As(Singleton).To<DebugSlotInfoRenderSystem>()
        .Bind().As(Singleton).To<DebugVisualizationSystem>()
        .Bind().As(Singleton).To<FogOfWarRenderSystem>()
        .Bind().As(Singleton).To<WorldCameraEndSystem>()
        .Bind().As(Singleton).To<SelectionBoxRenderSystem>()
        .Bind().As(Singleton).To<DebugVisualizationUISystem>()
        .Bind().As(Singleton).To<BottomBarUI>()
        .Bind().As(Singleton).To<WaveInfoUI>()
        .Bind().As(Singleton).To<DebugMenuUI>()
        // SystemRoots
        .Bind<SystemRoot>("UpdateRoot").As(Singleton).To(ctx =>
        {
            ctx.Inject(out EntityStore store);
            ctx.Inject(out CameraPanSystem cameraPanSystem);
            ctx.Inject(out ChunkManagerSystem chunkManagerSystem);
            ctx.Inject(out DualGridAtlasLoadSystem dualGridAtlasLoadSystem);
            ctx.Inject(out FogOfWarVisibilitySyncSystem fogOfWarVisibilitySyncSystem);

            var root = new SystemRoot(store)
            {
                cameraPanSystem,
                chunkManagerSystem,
                dualGridAtlasLoadSystem,
                fogOfWarVisibilitySyncSystem,
            };
            root.SetMonitorPerf(true);
            return root;
        })
        .Bind<SystemRoot>("RenderRoot").As(Singleton).To(ctx =>
        {
            ctx.Inject(out EntityStore store);
            ctx.Inject(out RenderInterpolationSystem renderInterpolationSystem);
            ctx.Inject(out ChunkRenderCacheSystem chunkRenderCacheSystem);
            ctx.Inject(out WorldCameraBeginSystem worldCameraBeginSystem);
            ctx.Inject(out RenderDualGridSystem renderDualGridSystem);
            ctx.Inject(out SelectionCircleRenderSystem selectionCircleRenderSystem);
            ctx.Inject(out SpriteRenderSystem spriteRenderSystem);
            ctx.Inject(out CombatUnitRenderSystem combatUnitRenderSystem);
            ctx.Inject(out BuildingRenderSystem buildingRenderSystem);
            ctx.Inject(out BuildingPreviewRenderSystem buildingPreviewRenderSystem);
            ctx.Inject(out AreaEffectVisualizationSystem areaEffectVisualizationSystem);
            ctx.Inject(out HealthBarRenderSystem healthBarRenderSystem);
            ctx.Inject(out ProductionProgressRenderSystem productionProgressRenderSystem);
            ctx.Inject(out BuildingHoverSystem buildingHoverSystem);
            ctx.Inject(out ProjectileRenderSystem projectileRenderSystem);
            ctx.Inject(out ResourceNodeRenderSystem resourceNodeRenderSystem);
            ctx.Inject(out TreeOverlayRenderSystem treeOverlayRenderSystem);
            ctx.Inject(out PowerGridRenderSystem powerGridRenderSystem);
            ctx.Inject(out DebugSlotInfoRenderSystem debugSlotInfoRenderSystem);
            ctx.Inject(out DebugVisualizationSystem debugVisualizationSystem);
            ctx.Inject(out FogOfWarRenderSystem fogOfWarRenderSystem);
            ctx.Inject(out WorldCameraEndSystem worldCameraEndSystem);
            ctx.Inject(out SelectionBoxRenderSystem selectionBoxRenderSystem);
            ctx.Inject(out DebugVisualizationUISystem debugVisualizationUISystem);
            ctx.Inject(out BottomBarUI bottomBarUI);
            ctx.Inject(out WaveInfoUI waveInfoUI);
            ctx.Inject(out DebugMenuUI debugMenuUI);

            var root = new SystemRoot(store)
            {
                renderInterpolationSystem, // FIRST - interpolate positions before any rendering
                buildingHoverSystem,       // Detect hovered building BEFORE render
                chunkRenderCacheSystem,    // Cache chunks (uses its own render textures)
                worldCameraBeginSystem,
                renderDualGridSystem,
                resourceNodeRenderSystem,  // Resource nodes on terrain
                treeOverlayRenderSystem,   // Trees on forest/dirt tiles
                powerGridRenderSystem,     // Power grid coverage (in build mode)
                selectionCircleRenderSystem,  // Selection circles UNDER unit sprites
                spriteRenderSystem,
                buildingRenderSystem,
                combatUnitRenderSystem,  // Garrisoned units render ON TOP of buildings
                buildingPreviewRenderSystem,
                areaEffectVisualizationSystem,  // Area effect radius + affected building icons
                healthBarRenderSystem,
                productionProgressRenderSystem,  // Production progress bar above hovered building
                projectileRenderSystem,
                fogOfWarRenderSystem,     // Fog overlay - after all world entities, before debug
                debugSlotInfoRenderSystem,
                debugVisualizationSystem,
                worldCameraEndSystem,
                selectionBoxRenderSystem,  // Screen space UI
                debugVisualizationUISystem,  // Screen space - debug UI text
                bottomBarUI,  // Screen space - RTS bottom bar UI
                waveInfoUI,   // Screen space - Wave system status display
                debugMenuUI,  // Screen space - Debug menu overlay
            };
            root.SetMonitorPerf(true);
            return root;
        });
}
