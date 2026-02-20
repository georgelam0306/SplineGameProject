using DerpLib.Ecs;
using GyrussClone.Simulation.Ecs;
using FixedMath;

namespace GyrussClone.Simulation.Systems;

public sealed class PlayerMovementSystem : IEcsSystem<SimEcsWorld>
{
    public void Update(SimEcsWorld world)
    {
        if (!world.PlayerAlive)
        {
            return;
        }

        world.PlayerAngle += world.PlayerAngularInput * world.PlayerAngularSpeed * world.DeltaTime;

        // Wrap to [0, 2pi)
        if (world.PlayerAngle < Fixed64.Zero)
        {
            world.PlayerAngle += Fixed64.TwoPi;
        }
        else if (world.PlayerAngle >= Fixed64.TwoPi)
        {
            world.PlayerAngle -= Fixed64.TwoPi;
        }
    }
}
