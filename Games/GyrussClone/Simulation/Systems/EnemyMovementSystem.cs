using DerpLib.Ecs;
using GyrussClone.Simulation.Ecs;
using FixedMath;

namespace GyrussClone.Simulation.Systems;

public sealed class EnemyMovementSystem : IEcsSystem<SimEcsWorld>
{
    private static readonly Fixed64 OuterRingRadius = Fixed64.FromInt(310);

    public void Update(SimEcsWorld world)
    {
        for (int row = 0; row < world.Enemy.Count; row++)
        {
            ref var transform = ref world.Enemy.PolarTransform(row);

            transform.Angle += transform.AngularSpeed * world.DeltaTime;
            transform.Radius += transform.RadialSpeed * world.DeltaTime;

            // Wrap angle
            if (transform.Angle < Fixed64.Zero)
            {
                transform.Angle += Fixed64.TwoPi;
            }
            else if (transform.Angle >= Fixed64.TwoPi)
            {
                transform.Angle -= Fixed64.TwoPi;
            }

            // Destroy if past outer ring
            if (transform.Radius > OuterRingRadius)
            {
                world.Enemy.QueueDestroy(world.Enemy.Entity(row));
            }
        }
    }
}
