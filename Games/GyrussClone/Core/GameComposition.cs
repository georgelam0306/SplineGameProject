using DerpLib.DI;
using DerpLib.Ecs;
using GyrussClone.Simulation.Ecs;
using GyrussClone.Simulation.Systems;
using static DerpLib.DI.DI;

namespace GyrussClone.Core;

[Composition]
partial class GameComposition
{
    static void Setup() => DI.Setup()
        .Arg<int>("sessionSeed")
        .Bind<SimEcsWorld>().As(Singleton).To(ctx =>
        {
            ctx.Inject<int>(out var seed);
            return SimEcsWorld.Create(seed);
        })
        .BindAll<IEcsSystem<SimEcsWorld>>().As(Singleton)
            .Add<PlayerMovementSystem>()
            .Add<PlayerFireSystem>()
            .Add<EnemySpawnSystem>()
            .Add<EnemyMovementSystem>()
            .Add<BulletMovementSystem>()
            .Add<CollisionSystem>()
            .Add<DeathSystem>()
        .Bind<EcsSystemPipeline<SimEcsWorld>>().As(Singleton)
        .Root<SimEcsWorld>("World")
        .Root<EcsSystemPipeline<SimEcsWorld>>("Pipeline");
}
