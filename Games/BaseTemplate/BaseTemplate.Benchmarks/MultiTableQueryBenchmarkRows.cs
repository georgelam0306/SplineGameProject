using Core;
using SimTable;

namespace BaseTemplate.Benchmarks;

/// <summary>
/// First benchmark row type - simulates CombatUnitRow with 5000 capacity.
/// </summary>
[SimTable(Capacity = 5_000, CellSize = 16, GridSize = 256)]
public partial struct UnitBenchmarkRow
{
    public Fixed64Vec2 Position;
    public Fixed64Vec2 Velocity;
    public int Health;
    public int MaxHealth;
    public int Damage;
}

/// <summary>
/// Second benchmark row type - simulates ZombieRow with 20000 capacity.
/// </summary>
[SimTable(Capacity = 20_000, CellSize = 16, GridSize = 256)]
public partial struct EnemyBenchmarkRow
{
    public Fixed64Vec2 Position;
    public Fixed64Vec2 Velocity;
    public int Health;
    public int MaxHealth;
    public int Damage;
    public int ExtraField; // Extra field not in query
}

/// <summary>
/// Query interface for multi-table iteration benchmark.
/// Matches tables with Position, Velocity, Health, MaxHealth.
/// </summary>
[MultiTableQuery]
public interface IDamageableBenchmark
{
    ref Fixed64Vec2 Position { get; }
    ref Fixed64Vec2 Velocity { get; }
    ref int Health { get; }
    ref int MaxHealth { get; }
}
