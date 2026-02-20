using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Global shared resources for the entire game (shared base co-op).
/// Single instance - all players share these resources.
/// </summary>
[SimDataTable]
public partial struct GameResourcesRow
{
    // === Resources (current + max storage capacity) ===
    public int Gold;
    public int MaxGold;

    public int Wood;
    public int MaxWood;

    public int Stone;
    public int MaxStone;

    public int Iron;
    public int MaxIron;

    public int Oil;
    public int MaxOil;

    public int Food;
    public int MaxFood;

    // === Net Rates (generation - upkeep, calculated per second for UI display) ===
    public int NetGoldRate;
    public int NetWoodRate;
    public int NetStoneRate;
    public int NetIronRate;
    public int NetOilRate;
    public int NetFoodRate;

    // === Energy (power grid, not stockpiled) ===
    public int Energy;
    public int MaxEnergy;

    // === Population ===
    public int Population;
    public int MaxPopulation;

    // === Tech ===
    public ulong UnlockedTech;     // Bitflags for unlocked technologies (0-63)
}
