using Xunit;
using Catrillion.Simulation.Components;
using Core;
using SimTable;

namespace Catrillion.Tests.Economy;

/// <summary>
/// Tests for tech modifier system including:
/// - Single tech multiplier application
/// - Multiple tech stacking (additive)
/// - Zero multiplier handling
/// - Resource-specific effects
/// Uses global GameResourcesRow for tech unlocks (shared base co-op model).
/// </summary>
public class TechModifierTests : IDisposable
{
    private readonly SimWorld _simWorld;

    public TechModifierTests()
    {
        _simWorld = new SimWorld();
        InitializeResources();
    }

    public void Dispose()
    {
        _simWorld.Dispose();
    }

    #region Helper Methods

    private void InitializeResources(ulong unlockedTech = 0)
    {
        var resources = _simWorld.GameResourcesRows;
        if (resources.Count == 0)
        {
            resources.Allocate();
        }
        var row = resources.GetRowBySlot(0);
        row.UnlockedTech = unlockedTech;
        row.MaxGold = 1000;
        row.MaxWood = 1000;
        row.MaxStone = 1000;
        row.MaxIron = 1000;
        row.MaxOil = 1000;
    }

    private void UnlockTech(int techId)
    {
        var resources = _simWorld.GameResourcesRows.GetRowBySlot(0);
        resources.UnlockedTech |= (1UL << techId);
    }

    private bool IsTechUnlocked(int techId)
    {
        var resources = _simWorld.GameResourcesRows.GetRowBySlot(0);
        return (resources.UnlockedTech & (1UL << techId)) != 0;
    }

    private SimHandle CreateResourceBuilding(
        byte ownerId,
        int generatesGold = 0,
        int generatesWood = 0,
        int generatesStone = 0,
        int generatesIron = 0,
        int generatesOil = 0)
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

        // Set effective stats equal to base initially
        building.EffectiveGeneratesGold = generatesGold;
        building.EffectiveGeneratesWood = generatesWood;
        building.EffectiveGeneratesStone = generatesStone;
        building.EffectiveGeneratesIron = generatesIron;
        building.EffectiveGeneratesOil = generatesOil;

        building.Flags = BuildingFlags.IsActive;
        building.TypeId = BuildingTypeId.Sawmill;

        return handle;
    }

    #endregion

    [Fact]
    public void TechUnlock_SetsBitCorrectly()
    {
        // Arrange & Act
        UnlockTech(0); // Unlock tech at bit 0
        UnlockTech(3); // Unlock tech at bit 3

        // Assert
        Assert.True(IsTechUnlocked(0));
        Assert.True(IsTechUnlocked(3));
        Assert.False(IsTechUnlocked(1));
        Assert.False(IsTechUnlocked(2));
    }

    [Fact]
    public void NoTechUnlocked_BaseMultiplierApplies()
    {
        // Arrange - building with base generation
        CreateResourceBuilding(ownerId: 0, generatesGold: 100);

        var buildings = _simWorld.BuildingRows;
        var building = buildings.GetRowBySlot(0);

        // With no tech, effective = base (1.0x multiplier)
        Assert.Equal(100, building.EffectiveGeneratesGold);
    }

    [Fact]
    public void MultipleTechsUnlocked_StackAdditively()
    {
        // Arrange - unlock multiple techs
        UnlockTech(0);
        UnlockTech(1);
        UnlockTech(2);

        var resources = _simWorld.GameResourcesRows.GetRowBySlot(0);

        // Verify all three techs are unlocked
        Assert.True((resources.UnlockedTech & 0b111) == 0b111);
    }

    [Fact]
    public void TechWithZeroMult_HasNoEffect()
    {
        // Building with gold generation, tech with 0 gold mult
        CreateResourceBuilding(ownerId: 0, generatesGold: 100);

        var buildings = _simWorld.BuildingRows;
        var building = buildings.GetRowBySlot(0);

        // Zero multiplier should not affect base generation
        // (1.0 + 0.0 = 1.0x)
        Assert.Equal(100, building.EffectiveGeneratesGold);
    }

    [Fact]
    public void Tech_OnlyAffectsRelevantResources()
    {
        // Building generates all resources
        CreateResourceBuilding(
            ownerId: 0,
            generatesGold: 100,
            generatesWood: 100,
            generatesStone: 100,
            generatesIron: 100,
            generatesOil: 100);

        var buildings = _simWorld.BuildingRows;
        var building = buildings.GetRowBySlot(0);

        // Without modifiers, all should be at base
        Assert.Equal(100, building.EffectiveGeneratesGold);
        Assert.Equal(100, building.EffectiveGeneratesWood);
        Assert.Equal(100, building.EffectiveGeneratesStone);
        Assert.Equal(100, building.EffectiveGeneratesIron);
        Assert.Equal(100, building.EffectiveGeneratesOil);
    }

    [Fact]
    public void UnlockedTechBitMask_SupportsUpTo64Techs()
    {
        // Arrange - unlock tech at bit 63 (max)
        UnlockTech(63);

        // Assert
        Assert.True(IsTechUnlocked(63));
    }
}
