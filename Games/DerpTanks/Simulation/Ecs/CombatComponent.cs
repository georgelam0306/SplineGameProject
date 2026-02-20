using DerpLib.Ecs;
using FixedMath;

namespace DerpTanks.Simulation.Ecs;

public struct CombatComponent : ISimComponent
{
    public int Health;
    public Fixed64 MoveSpeed;
}

