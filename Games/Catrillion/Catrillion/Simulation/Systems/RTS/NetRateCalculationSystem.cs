using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Calculates net resource rates (generation - upkeep) for UI display.
/// Runs after ResourceUpkeepSystem to provide accurate rate information.
///
/// Updates PlayerStateRow.NetGoldRate, NetWoodRate, etc. once per second.
/// </summary>
public sealed class NetRateCalculationSystem : SimTableSystem
{
    private const int FramesPerSecond = SimulationConfig.TickRate;

    public NetRateCalculationSystem(SimWorld world) : base(world) { }

    public override void Tick(in SimulationContext context)
    {
        if (!World.IsPlaying()) return;

        // Only calculate rates once per second (for UI display)
        int frame = context.CurrentFrame;
        if ((frame % FramesPerSecond) != 0) return;

        var buildings = World.BuildingRows;
        var resources = World.GameResourcesRows.GetRowBySlot(0);

        // Accumulate total generation and upkeep from all buildings
        int genGold = 0, genWood = 0, genStone = 0, genIron = 0, genOil = 0, genFood = 0;
        int upkeepGold = 0, upkeepWood = 0, upkeepStone = 0, upkeepIron = 0, upkeepOil = 0, upkeepFood = 0;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);

            // Skip inactive or dead buildings
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            // Accumulate effective generation rates
            genGold += building.EffectiveGeneratesGold;
            genWood += building.EffectiveGeneratesWood;
            genStone += building.EffectiveGeneratesStone;
            genIron += building.EffectiveGeneratesIron;
            genOil += building.EffectiveGeneratesOil;
            genFood += building.EffectiveGeneratesFood;

            // Accumulate upkeep costs
            upkeepGold += building.UpkeepGold;
            upkeepWood += building.UpkeepWood;
            upkeepStone += building.UpkeepStone;
            upkeepIron += building.UpkeepIron;
            upkeepOil += building.UpkeepOil;
            upkeepFood += building.UpkeepFood;
        }

        // Write net rates to global resources
        resources.NetGoldRate = genGold - upkeepGold;
        resources.NetWoodRate = genWood - upkeepWood;
        resources.NetStoneRate = genStone - upkeepStone;
        resources.NetIronRate = genIron - upkeepIron;
        resources.NetOilRate = genOil - upkeepOil;
        resources.NetFoodRate = genFood - upkeepFood;
    }
}
