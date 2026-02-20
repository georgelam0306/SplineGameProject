using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

public enum ProjectileType : byte
{
    Bullet = 0,
    Arrow = 1,
    Shell = 2,
    Rocket = 3,
    AcidSpit = 4
}

[SimTable(Capacity = 2_000, CellSize = 16, GridSize = 256)]
public partial struct ProjectileRow
{
    // Transform
    public Fixed64Vec2 Position;
    public Fixed64Vec2 Velocity;

    // Identity
    public ProjectileType Type;
    public byte OwnerPlayerId;
    public SimHandle SourceHandle;    // Entity that fired this projectile

    // Combat
    public int Damage;
    public Fixed64 SplashRadius;      // 0 = no splash, >0 = AOE radius
    public byte PierceCount;          // 0 = single target, >0 = can hit N enemies

    // Targeting (for homing projectiles)
    public SimHandle TargetHandle;    // Target entity for homing
    public Fixed64 HomingStrength;    // 0 = straight, >0 = turn rate toward target

    // Lifetime
    public int LifetimeFrames;        // Frames until despawn
    public Fixed64 MaxRange;          // 0 = unlimited, >0 = despawn after traveling this distance
    public Fixed64 DistanceTraveled;

    // State
    public ProjectileFlags Flags;
}
