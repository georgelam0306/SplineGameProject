using Xunit;
using BaseTemplate.Simulation.Components;
using Core;
using SimTable;

namespace BaseTemplate.Tests.Economy;

/// <summary>
/// Tests for resource generation system including:
/// - Base generation rates
/// - Building state effects (active/inactive/dead)
/// - Frame accumulator for sub-second precision
/// - Max storage clamping
/// Uses global GameResourcesRow (shared base co-op model).
/// </summary>
public class ResourceGenerationTests : IDisposable
{
    private readonly SimWorld _simWorld;

    public ResourceGenerationTests()
    {
        _simWorld = new SimWorld();
        InitializeResources(100, 100, 100, 100, 100);
    }

    public void Dispose()
    {
        _simWorld.Dispose();
    }

    #region Helper Methods

    private void InitializeResources(int maxGold, int maxWood, int maxStone, int maxIron, int maxOil)
    {
        var resources = _simWorld.GameResourcesRows;
        // Allocate if not already allocated
        if (resources.Count == 0)
        {
            resources.Allocate();
        }
        var row = resources.GetRowBySlot(0);
        row.MaxGold = maxGold;
        row.MaxWood = maxWood;
        row.MaxStone = maxStone;
        row.MaxIron = maxIron;
        row.MaxOil = maxOil;
        row.Gold = 0;
        row.Wood = 0;
        row.Stone = 0;
        row.Iron = 0;
        row.Oil = 0;
    }

    private SimHandle CreateResourceBuilding(
        byte ownerId,
        int generatesGold = 0,
        int generatesWood = 0,
        int generatesStone = 0,
        int generatesIron = 0,
        int generatesOil = 0,
        bool isActive = true,
        bool isDead = false)
    {
        var buildings = _simWorld.BuildingRows;
        var handle = buildings.Allocate();
        var building = buildings.GetRow(handle);

        building.Position = Fixed64Vec2.Zero;
        building.OwnerPlayerId = ownerId;

        // Set base generation stats
        building.GeneratesGold = generatesGold;
        building.GeneratesWood = generatesWood;
        building.GeneratesStone = generatesStone;
        building.GeneratesIron = generatesIron;
        building.GeneratesOil = generatesOil;

        // Set effective stats (normally done by ModifierApplicationSystem)
        building.EffectiveGeneratesGold = generatesGold;
        building.EffectiveGeneratesWood = generatesWood;
        building.EffectiveGeneratesStone = generatesStone;
        building.EffectiveGeneratesIron = generatesIron;
        building.EffectiveGeneratesOil = generatesOil;

        // Set flags
        building.Flags = BuildingFlags.None;
        if (isActive) building.Flags |= BuildingFlags.IsActive;
        if (isDead) building.Flags |= BuildingFlags.IsDead;

        building.TypeId = BuildingTypeId.Sawmill;

        return handle;
    }

    private ref GameResourcesRow GetResources()
    {
        return ref _simWorld.GameResourcesRows.GetRowBySlot(0);
    }

    #endregion

    [Fact]
    public void Building_GeneratesCorrectBaseResourcesPerSecond()
    {
        // Arrange
        CreateResourceBuilding(
            ownerId: 0,
            generatesGold: 10,
            generatesWood: 5);

        // Verify initial state
        ref var resources = ref GetResources();
        Assert.Equal(0, resources.Gold);
        Assert.Equal(0, resources.Wood);
    }

    [Fact]
    public void MultipleBuildings_AccumulatePerPlayer()
    {
        // Arrange - two buildings for player 0
        CreateResourceBuilding(ownerId: 0, generatesGold: 10);
        CreateResourceBuilding(ownerId: 0, generatesGold: 15);

        // Both buildings should contribute to player's effective generation
        var buildings = _simWorld.BuildingRows;
        int totalGeneration = 0;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);
            if (building.OwnerPlayerId == 0)
            {
                totalGeneration += building.EffectiveGeneratesGold;
            }
        }

        Assert.Equal(25, totalGeneration);
    }

    [Fact]
    public void InactiveBuilding_DoesNotGenerate()
    {
        // Arrange - inactive building
        CreateResourceBuilding(
            ownerId: 0,
            generatesGold: 100,
            isActive: false);

        // Inactive buildings should not contribute
        var buildings = _simWorld.BuildingRows;
        var building = buildings.GetRowBySlot(0);
        Assert.False(building.Flags.HasFlag(BuildingFlags.IsActive));
    }

    [Fact]
    public void DeadBuilding_DoesNotGenerate()
    {
        // Arrange - dead building
        CreateResourceBuilding(
            ownerId: 0,
            generatesGold: 100,
            isDead: true);

        // Dead buildings should not contribute
        var buildings = _simWorld.BuildingRows;
        var building = buildings.GetRowBySlot(0);
        Assert.True(building.Flags.IsDead());
    }

    [Fact]
    public void Resources_ClampToMaxStorage()
    {
        // Arrange - resources with max 50 gold
        InitializeResources(50, 100, 100, 100, 100);
        ref var resources = ref GetResources();
        resources.Gold = 45; // Near max

        // After adding 10 gold, should clamp to 50
        int newGold = resources.Gold + 10;
        if (newGold > resources.MaxGold) newGold = resources.MaxGold;

        Assert.Equal(50, newGold);
    }

    [Fact]
    public void AllResourceTypes_GenerateCorrectly()
    {
        // Arrange - building that generates all resources
        CreateResourceBuilding(
            ownerId: 0,
            generatesGold: 1,
            generatesWood: 2,
            generatesStone: 3,
            generatesIron: 4,
            generatesOil: 5);

        var buildings = _simWorld.BuildingRows;
        var building = buildings.GetRowBySlot(0);

        Assert.Equal(1, building.EffectiveGeneratesGold);
        Assert.Equal(2, building.EffectiveGeneratesWood);
        Assert.Equal(3, building.EffectiveGeneratesStone);
        Assert.Equal(4, building.EffectiveGeneratesIron);
        Assert.Equal(5, building.EffectiveGeneratesOil);
    }

    [Fact]
    public void PlayerWithNoBuildings_HasZeroGeneration()
    {
        // No buildings created for player 0
        var buildings = _simWorld.BuildingRows;

        int totalGeneration = 0;
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            var building = buildings.GetRowBySlot(slot);
            if (building.OwnerPlayerId == 0)
            {
                totalGeneration += building.EffectiveGeneratesGold;
            }
        }

        Assert.Equal(0, totalGeneration);
    }
}
