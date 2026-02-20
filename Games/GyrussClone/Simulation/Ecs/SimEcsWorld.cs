using System.Diagnostics;
using DerpLib.Ecs.Setup;

namespace GyrussClone.Simulation.Ecs;

public sealed partial class SimEcsWorld
{
    [Conditional(DerpEcsSetupConstants.ConditionalSymbol)]
    private static void Setup(DerpEcsSetupBuilder b)
    {
        b.Archetype<Enemy>()
            .Capacity(256)
            .With<PolarTransformComponent>()
            .With<EnemyComponent>();

        b.Archetype<PlayerBullet>()
            .Capacity(128)
            .With<PolarTransformComponent>()
            .With<BulletComponent>();
    }
}
