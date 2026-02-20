using Xunit;
using Catrillion.Simulation.Components;
using SimTable;

namespace Catrillion.Tests.SimTableTests;

/// <summary>
/// Tests for SimTable Free/GetSlot behavior and orphan detection logic.
/// These tests verify the behavior of the free list mechanism and
/// generational handle validation for detecting stale references.
/// </summary>
public class UnitRowFreeTests : IDisposable
{
    private readonly SimWorld _simWorld;

    public UnitRowFreeTests()
    {
        _simWorld = new SimWorld();
    }

    public void Dispose()
    {
        _simWorld.Dispose();
    }

    #region Test 1: Single Free - GetSlot Returns -1

    [Fact]
    public void SingleFree_GetSlotReturnsNegativeOne_WhenFirstFreed()
    {
        // Scenario: Free the first entity when no prior frees exist
        var units = _simWorld.UnitRows;

        // Allocate first entity
        var handle = units.Allocate();
        int originalSlot = units.GetSlot(handle);

        Assert.True(originalSlot >= 0);
        Assert.Equal(handle.StableId, units.GetStableId(originalSlot));

        // Free it (first free, so _stableIdFreeListHead was -1)
        units.Free(handle);

        // GetSlot should return -1 because the slot is freed
        int slotAfterFree = units.GetSlot(handle);
        Assert.Equal(-1, slotAfterFree);

        // GetStableId for the original slot should return -1
        int stableIdAtOriginalSlot = units.GetStableId(originalSlot);
        Assert.Equal(-1, stableIdAtOriginalSlot);
    }

    #endregion

    #region Test 2: Multiple Frees - GetSlot Always Returns -1

    [Fact]
    public void MultipleFrees_GetSlotAlwaysReturnsNegativeOne()
    {
        // Scenario: Free multiple entities, verify GetSlot ALWAYS returns -1
        // With the dedicated _stableIdNextFree array, _stableIdToSlot is no longer repurposed
        var units = _simWorld.UnitRows;

        // Allocate 3 entities
        var handle0 = units.Allocate();
        var handle1 = units.Allocate();
        var handle2 = units.Allocate();

        // Free in order: 0, 1, 2
        // After Free(0): _stableIdToSlot[0] = -1
        units.Free(handle0);
        Assert.Equal(-1, units.GetSlot(handle0));

        // After Free(1): _stableIdToSlot[1] = -1 (NOT free list pointer anymore)
        units.Free(handle1);
        Assert.Equal(-1, units.GetSlot(handle1));

        // After Free(2): _stableIdToSlot[2] = -1 (NOT free list pointer anymore)
        units.Free(handle2);
        Assert.Equal(-1, units.GetSlot(handle2));

        // All freed handles reliably return -1, making orphan detection simple
    }

    #endregion

    #region Test 3: Orphan Detection - Single Free Works

    [Fact]
    public void OrphanDetection_SingleFree_DetectsOrphan()
    {
        // Scenario: Verify orphan check detects freed entity
        var units = _simWorld.UnitRows;

        var handle = units.Allocate();

        units.Free(handle);

        // Orphan detection logic from SimWorldSyncManager:
        int slot = units.GetSlot(handle);

        // slot < 0 means orphan detected
        Assert.True(slot < 0, "Orphan should be detected when slot < 0");
    }

    #endregion

    #region Test 4: Orphan Detection - Multiple Frees All Return -1

    [Fact]
    public void OrphanDetection_MultipleFrees_AllReturnNegativeOne()
    {
        // Scenario: Verify orphan check with multiple frees
        // With dedicated stableId free list, GetSlot always returns -1 for freed handles
        var units = _simWorld.UnitRows;

        var handle0 = units.Allocate();
        var handle1 = units.Allocate();

        // Free both
        units.Free(handle0);
        units.Free(handle1);

        // Both should return -1 (no more free list pointer confusion)
        int slot0 = units.GetSlot(handle0);
        int slot1 = units.GetSlot(handle1);

        Assert.Equal(-1, slot0);
        Assert.Equal(-1, slot1);

        // Orphan detection is now simple: slot < 0 means orphan
        Assert.True(slot0 < 0, "Orphan should be detected for handle0");
        Assert.True(slot1 < 0, "Orphan should be detected for handle1");
    }

    #endregion

    #region Test 5: StableId Recycling - Generational Handles Detect Stale References

    [Fact]
    public void StableIdRecycling_GenerationMismatch_DetectsStaleHandle()
    {
        // Scenario: With generational handles, stale references are detected
        // even when rawId is recycled before sync runs
        var units = _simWorld.UnitRows;

        // Allocate entity A
        var handleA = units.Allocate();
        int genA = handleA.Generation;

        // Free entity A
        units.Free(handleA);

        // Allocate again - should reuse the same rawId but with incremented generation
        var handleB = units.Allocate();
        int genB = handleB.Generation;

        // Verify rawId was recycled but generation incremented
        Assert.Equal(handleA.RawId, handleB.RawId);
        Assert.Equal(genA + 1, genB);

        // The old handle A is now STALE - GetSlot should return -1
        // because the generation doesn't match the current generation
        int slotA = units.GetSlot(handleA);
        Assert.Equal(-1, slotA);

        // The new handle B is valid
        int slotB = units.GetSlot(handleB);
        Assert.True(slotB >= 0, "New handle should resolve to valid slot");

        // This proves generational handles solve the ABA problem
    }

    [Fact]
    public void StableIdRecycling_MultipleGenerations_AllStaleHandlesDetected()
    {
        // Scenario: Verify multiple cycles of alloc/free increment generation
        var units = _simWorld.UnitRows;

        // First allocation
        var handle1 = units.Allocate();
        Assert.Equal(0, handle1.Generation);

        // Free and reallocate multiple times
        units.Free(handle1);

        var handle2 = units.Allocate();
        Assert.Equal(handle1.RawId, handle2.RawId);
        Assert.Equal(1, handle2.Generation);

        units.Free(handle2);

        var handle3 = units.Allocate();
        Assert.Equal(handle1.RawId, handle3.RawId);
        Assert.Equal(2, handle3.Generation);

        // All old handles should be detected as stale
        Assert.Equal(-1, units.GetSlot(handle1));
        Assert.Equal(-1, units.GetSlot(handle2));
        Assert.True(units.GetSlot(handle3) >= 0);
    }

    #endregion

    #region Test 6: Duplicate Free - No Corruption (Head Case)

    [Fact]
    public void DuplicateFree_HeadCase_NoCorruption()
    {
        // Scenario: Calling Free twice when handle is at head of free list
        var units = _simWorld.UnitRows;

        var handle = units.Allocate();

        // First free
        units.Free(handle);
        int countAfterFirstFree = units.Count;

        // Second free - should be a no-op (slot = -1, returns early)
        units.Free(handle);
        int countAfterSecondFree = units.Count;

        // Count should not change on duplicate free
        Assert.Equal(countAfterFirstFree, countAfterSecondFree);

        // GetSlot should still return -1
        Assert.Equal(-1, units.GetSlot(handle));
    }

    #endregion

    #region Test 7: Duplicate Free - Chain Case

    [Fact]
    public void DuplicateFree_ChainCase_NoCorruption()
    {
        // Scenario: Calling Free twice when another free happened in between
        var units = _simWorld.UnitRows;

        var handle0 = units.Allocate();
        var handle1 = units.Allocate();

        // Free 0 first (now _stableIdToSlot[0] = -1)
        units.Free(handle0);

        // Free 1 (now _stableIdToSlot[1] = -1, NOT a free list pointer anymore)
        units.Free(handle1);

        int countBeforeDuplicate = units.Count;

        // Try to free 0 again
        // _stableIdToSlot[0] = -1, so slot = -1, returns early (safe!)
        units.Free(handle0);

        int countAfterDuplicate = units.Count;

        // Should be safe, no corruption
        Assert.Equal(countBeforeDuplicate, countAfterDuplicate);
    }

    #endregion

    #region Test 8: IsActive Check as Safety Net

    [Fact]
    public void IsActiveCheck_CatchesInactiveWithoutFree()
    {
        // Scenario: Entity passes orphan check but IsActive is false
        var units = _simWorld.UnitRows;

        var handle = units.Allocate();
        int slot = units.GetSlot(handle);

        // Mark inactive WITHOUT calling Free
        units.IsActive(slot) = false;

        // Orphan check passes (still valid mapping, generation matches)
        int checkSlot = units.GetSlot(handle);
        bool passesOrphanCheck = checkSlot >= 0;

        Assert.True(passesOrphanCheck, "Orphan check should pass (mapping still valid)");

        // But IsActive check catches it
        bool isActive = units.IsActive(slot);
        Assert.False(isActive, "IsActive should be false - entity should still be deleted");
    }

    #endregion

    #region Test 9: Cross-Frame Safety (Normal Flow)

    [Fact]
    public void CrossFrameSafety_NormalKillSyncRespawnFlow()
    {
        // Scenario: Verify normal kill→sync→respawn flow works
        var units = _simWorld.UnitRows;

        // Frame N: Allocate
        var handleN = units.Allocate();

        // Frame N: Kill (Free)
        units.Free(handleN);

        // Frame N: Sync would detect orphan via generation mismatch or slot = -1
        int slot = units.GetSlot(handleN);
        Assert.True(slot < 0, "Orphan should be detected - Friflo entity would be deleted");

        // Frame N+1: Allocate again (reuses rawId with new generation)
        var handleN1 = units.Allocate();

        // RawId was recycled but generation incremented
        Assert.Equal(handleN.RawId, handleN1.RawId);
        Assert.Equal(handleN.Generation + 1, handleN1.Generation);

        // Old handle is stale
        Assert.Equal(-1, units.GetSlot(handleN));

        // New handle is valid
        int newSlot = units.GetSlot(handleN1);
        Assert.True(newSlot >= 0);
    }

    #endregion

    #region Test 10: SimHandle Validation Properties

    [Fact]
    public void SimHandle_IsValid_ReturnsCorrectValue()
    {
        // Test SimHandle.IsValid property
        var units = _simWorld.UnitRows;

        var validHandle = units.Allocate();
        Assert.True(validHandle.IsValid, "Allocated handle should be valid");

        var invalidHandle = SimHandle.Invalid;
        Assert.False(invalidHandle.IsValid, "Invalid handle should not be valid");
    }

    [Fact]
    public void SimHandle_RawIdAndGeneration_PackedCorrectly()
    {
        // Verify bit packing is correct
        var units = _simWorld.UnitRows;

        var handle = units.Allocate();
        int rawId = handle.RawId;
        int generation = handle.Generation;

        // First allocation should have rawId=0, generation=0
        Assert.Equal(0, rawId);
        Assert.Equal(0, generation);

        // StableId should be the packed value
        int expectedPacked = (generation << 16) | rawId;
        Assert.Equal(expectedPacked, handle.StableId);

        // Free and reallocate
        units.Free(handle);
        var handle2 = units.Allocate();

        // Same rawId, incremented generation
        Assert.Equal(0, handle2.RawId);
        Assert.Equal(1, handle2.Generation);
        Assert.Equal((1 << 16) | 0, handle2.StableId);
    }

    #endregion
}
