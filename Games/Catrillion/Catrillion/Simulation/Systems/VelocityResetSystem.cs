using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems;

/// <summary>
/// Resets velocity to zero each frame before movement systems add forces.
/// This prevents velocity from accumulating and causing drift/inertia.
/// </summary>
public sealed class VelocityResetSystem : SimTableSystem
{
    public VelocityResetSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        // RTS mode: CombatUnits handle their own velocity, zombies need reset
        ResetZombieVelocities();
    }

    private void ResetZombieVelocities()
    {
        var zombies = World.ZombieRows;
        int count = zombies.Count;

        for (int slot = 0; slot < count; slot++)
        {
            if (!zombies.TryGetRow(slot, out var row)) continue;
            row.Velocity = Fixed64Vec2.Zero;
        }
    }
}
