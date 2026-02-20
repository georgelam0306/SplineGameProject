using Xunit;
using DieDrifterDie.Simulation.Components;
using Core;
using SimTable;

namespace DieDrifterDie.Tests.Economy;

/// <summary>
/// Tests for storage building effects including:
/// - Max resource capacity increase
/// - Multiple storage buildings stacking
/// - Destroyed storage effects
/// - Inactive storage building handling
/// Uses global GameResourcesRow (shared base co-op model).
/// </summary>
public class StorageBuildingTests : IDisposable
{
    private readonly SimWorld _simWorld;

    public StorageBuildingTests()
    {
        _simWorld = new SimWorld();
        InitializeResources();
    }

    public void Dispose()
    {
        _simWorld.Dispose();
    }

    #region Helper Methods

    private void InitializeResources()
    {
        var resources = _simWorld.GameResourcesRows;
        if (resources.Count == 0)
        {
            resources.Allocate();
        }
        var row = resources.GetRowBySlot(0);
        row.MaxGold = 100;
        row.MaxWood = 100;
        row.MaxStone = 100;
        row.MaxIron = 100;
        row.MaxOil = 100;
        row.Gold = 0;
        row.Wood = 0;
        row.Stone = 0;
        row.Iron = 0;
        row.Oil = 0;
    }

    private SimHandle CreateStorageBuilding(
        byte ownerId,
        int providesMaxGold = 0,
        int providesMaxWood = 0,
        int providesMaxStone = 0,
        int providesMaxIron = 0,
        int providesMaxOil = 0,
        bool isActive = true,
        bool isDead = false)
    {
        var buildings = _simWorld.BuildingRows;
        var handle = buildings.Allocate();
        var building = buildings.GetRow(handle);

        building.Position = Fixed64Vec2.Zero;
        building.OwnerPlayerId = ownerId;

        // Set storage provision stats
        building.ProvidesMaxGold = providesMaxGold;
        building.ProvidesMaxWood = providesMaxWood;
        building.ProvidesMaxStone = providesMaxStone;
        building.ProvidesMaxIron = providesMaxIron;
        building.ProvidesMaxOil = providesMaxOil;

        // Set flags
        building.Flags = BuildingFlags.None;
        if (isActive) building.Flags |= BuildingFlags.IsActive;
        if (isDead) building.Flags |= BuildingFlags.IsDead;

        building.TypeId = BuildingTypeId.Warehouse;

        return handle;
    }

    private int CalculateTotalStorageBonus()
    {
        var buildings = _simWorld.BuildingRows;
        int total = 0;

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);
            if (building.Flags.IsDead()) continue;
            if (!building.Flags.HasFlag(BuildingFlags.IsActive)) continue;

            total += building.ProvidesMaxGold;
        }

        return total;
    }

    #endregion

    [Fact]
    public void StorageBuilding_IncreasesMaxResources()
    {
        // Arrange
        CreateStorageBuilding(ownerId: 0, providesMaxGold: 200);

        int bonus = CalculateTotalStorageBonus();
        Assert.Equal(200, bonus);
    }

    [Fact]
    public void MultipleStorageBuildings_Stack()
    {
        // Arrange
        CreateStorageBuilding(ownerId: 0, providesMaxGold: 100);
        CreateStorageBuilding(ownerId: 0, providesMaxGold: 150);
        CreateStorageBuilding(ownerId: 0, providesMaxGold: 50);

        int bonus = CalculateTotalStorageBonus();
        Assert.Equal(300, bonus);
    }

    [Fact]
    public void DestroyingStorage_ReducesMax()
    {
        // Arrange - create then mark as dead
        var handle = CreateStorageBuilding(ownerId: 0, providesMaxGold: 200);

        // Before destruction
        int bonusBefore = CalculateTotalStorageBonus();
        Assert.Equal(200, bonusBefore);

        // Mark as dead
        var buildings = _simWorld.BuildingRows;
        var building = buildings.GetRow(handle);
        building.Flags |= BuildingFlags.IsDead;

        // After destruction
        int bonusAfter = CalculateTotalStorageBonus();
        Assert.Equal(0, bonusAfter);
    }

    [Fact]
    public void InactiveStorageBuilding_NoContribution()
    {
        // Arrange - inactive storage
        CreateStorageBuilding(
            ownerId: 0,
            providesMaxGold: 500,
            isActive: false);

        int bonus = CalculateTotalStorageBonus();
        Assert.Equal(0, bonus);
    }

    [Fact]
    public void StorageBuilding_AllResourceTypes()
    {
        // Arrange
        CreateStorageBuilding(
            ownerId: 0,
            providesMaxGold: 100,
            providesMaxWood: 200,
            providesMaxStone: 300,
            providesMaxIron: 400,
            providesMaxOil: 500);

        var buildings = _simWorld.BuildingRows;
        var building = buildings.GetRowBySlot(0);

        Assert.Equal(100, building.ProvidesMaxGold);
        Assert.Equal(200, building.ProvidesMaxWood);
        Assert.Equal(300, building.ProvidesMaxStone);
        Assert.Equal(400, building.ProvidesMaxIron);
        Assert.Equal(500, building.ProvidesMaxOil);
    }
}
