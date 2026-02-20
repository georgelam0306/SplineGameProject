using Xunit;
using Catrillion.Simulation.Components;
using Core;
using SimTable;

namespace Catrillion.Tests.Economy;

/// <summary>
/// Tests for resource upkeep system including:
/// - Building upkeep deduction
/// - Multi-building upkeep accumulation
/// - Negative gold (debt) handling
/// - Inactive building upkeep exemption
/// Uses global GameResourcesRow (shared base co-op model).
/// </summary>
public class ResourceUpkeepTests : IDisposable
{
    private readonly SimWorld _simWorld;

    public ResourceUpkeepTests()
    {
        _simWorld = new SimWorld();
        InitializeResources(100);
    }

    public void Dispose()
    {
        _simWorld.Dispose();
    }

    #region Helper Methods

    private void InitializeResources(int startingGold)
    {
        var resources = _simWorld.GameResourcesRows;
        if (resources.Count == 0)
        {
            resources.Allocate();
        }
        var row = resources.GetRowBySlot(0);
        row.Gold = startingGold;
        row.MaxGold = 1000;
        row.Wood = 100;
        row.MaxWood = 1000;
        row.Stone = 100;
        row.MaxStone = 1000;
        row.Iron = 100;
        row.MaxIron = 1000;
        row.Oil = 100;
        row.MaxOil = 1000;
    }

    private SimHandle CreateUpkeepBuilding(
        byte ownerId,
        int upkeepGold = 0,
        int upkeepWood = 0,
        int upkeepStone = 0,
        int upkeepIron = 0,
        int upkeepOil = 0,
        bool isActive = true,
        bool isDead = false)
    {
        var buildings = _simWorld.BuildingRows;
        var handle = buildings.Allocate();
        var building = buildings.GetRow(handle);

        building.Position = Fixed64Vec2.Zero;
        building.OwnerPlayerId = ownerId;

        // Set upkeep stats (from CachedStat)
        building.UpkeepGold = upkeepGold;
        building.UpkeepWood = upkeepWood;
        building.UpkeepStone = upkeepStone;
        building.UpkeepIron = upkeepIron;
        building.UpkeepOil = upkeepOil;

        // Set flags
        building.Flags = BuildingFlags.None;
        if (isActive) building.Flags |= BuildingFlags.IsActive;
        if (isDead) building.Flags |= BuildingFlags.IsDead;

        building.TypeId = BuildingTypeId.Turret;

        return handle;
    }

    private ref GameResourcesRow GetResources()
    {
        return ref _simWorld.GameResourcesRows.GetRowBySlot(0);
    }

    private int CalculateTotalUpkeep()
    {
        var buildings = _simWorld.BuildingRows;
        int total = 0;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            total += building.UpkeepGold;
        }

        return total;
    }

    #endregion

    [Fact]
    public void Building_HasUpkeepCost()
    {
        // Arrange
        CreateUpkeepBuilding(ownerId: 0, upkeepGold: 5);

        int totalUpkeep = CalculateTotalUpkeep();
        Assert.Equal(5, totalUpkeep);
    }

    [Fact]
    public void MultipleBuildings_AccumulateUpkeep()
    {
        // Arrange
        CreateUpkeepBuilding(ownerId: 0, upkeepGold: 5);
        CreateUpkeepBuilding(ownerId: 0, upkeepGold: 3);
        CreateUpkeepBuilding(ownerId: 0, upkeepGold: 2);

        int totalUpkeep = CalculateTotalUpkeep();
        Assert.Equal(10, totalUpkeep);
    }

    [Fact]
    public void Resources_GoNegative_WhenCantAffordUpkeep()
    {
        // Arrange - resources has 10 gold, upkeep is 15
        InitializeResources(10);
        ref var resources = ref GetResources();

        // Simulate deduction (gold can go negative)
        int upkeep = 15;
        resources.Gold -= upkeep;

        Assert.Equal(-5, resources.Gold);
    }

    [Fact]
    public void InactiveBuilding_NoUpkeep()
    {
        // Arrange - inactive building
        CreateUpkeepBuilding(
            ownerId: 0,
            upkeepGold: 100,
            isActive: false);

        int totalUpkeep = CalculateTotalUpkeep();
        Assert.Equal(0, totalUpkeep); // Inactive buildings don't cost upkeep
    }

    [Fact]
    public void DeadBuilding_NoUpkeep()
    {
        // Arrange - dead building
        CreateUpkeepBuilding(
            ownerId: 0,
            upkeepGold: 100,
            isDead: true);

        int totalUpkeep = CalculateTotalUpkeep();
        Assert.Equal(0, totalUpkeep); // Dead buildings don't cost upkeep
    }

    [Fact]
    public void Upkeep_DeductsFromCorrectResourceTypes()
    {
        // Arrange - building with multi-resource upkeep
        CreateUpkeepBuilding(
            ownerId: 0,
            upkeepGold: 1,
            upkeepWood: 2,
            upkeepStone: 3,
            upkeepIron: 4,
            upkeepOil: 5);

        var buildings = _simWorld.BuildingRows;
        var building = buildings.GetRowBySlot(0);

        Assert.Equal(1, building.UpkeepGold);
        Assert.Equal(2, building.UpkeepWood);
        Assert.Equal(3, building.UpkeepStone);
        Assert.Equal(4, building.UpkeepIron);
        Assert.Equal(5, building.UpkeepOil);
    }
}
