using System.Diagnostics;
using DerpLib.Ecs.Setup;

namespace DerpTanks.Simulation.Ecs;

public sealed partial class SimEcsWorld
{
    [Conditional(DerpEcsSetupConstants.ConditionalSymbol)]
    private static void Setup(DerpEcsSetupBuilder b)
    {
        b.Archetype<Horde>()
            .Capacity(10000)
            .With<TransformComponent>()
            .With<CombatComponent>()
            // Spatial index needs a small cell size because we use it for dense boid separation queries.
            // Keep it aligned with HordeSeparationGrid so debug + sim use the same world-to-cell mapping.
            .Spatial(
                position: nameof(TransformComponent.Position),
                cellSize: HordeSeparationGrid.CellSize,
                gridSize: HordeSeparationGrid.GridSize,
                originX: HordeSeparationGrid.OriginX,
                originY: HordeSeparationGrid.OriginY)
            .QueryRadius(position: nameof(TransformComponent.Position), maxResults: 2048)
            .QueryAabb(position: nameof(TransformComponent.Position), maxResults: 2048);

        b.Archetype<Projectile>()
            .Capacity(512)
            .With<ProjectileTransformComponent>()
            .With<ProjectileComponent>()
            .SpawnQueueCapacity(256)
            .DestroyQueueCapacity(128);
    }
}
