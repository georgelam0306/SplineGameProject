using Xunit;
using DieDrifterDie.Simulation.Components;
using DieDrifterDie.Simulation.Utilities;
using SimTable;

namespace DieDrifterDie.Tests.SimTableTests;

public class SuperPickupRowTests : IDisposable
{
    private readonly SimWorld _simWorld;

    public SuperPickupRowTests()
    {
        _simWorld = new SimWorld();
    }

    public void Dispose()
    {
        _simWorld.Dispose();
    }

    [Fact]
    public void Allocate_IncrementsCount()
    {
        var superPickups = _simWorld.SuperPickupRows;

        Assert.Equal(0, superPickups.Count);

        superPickups.Allocate();
        Assert.Equal(1, superPickups.Count);

        superPickups.Allocate();
        Assert.Equal(2, superPickups.Count);

        superPickups.Allocate();
        Assert.Equal(3, superPickups.Count);
    }

    [Fact]
    public void Free_DecrementsCount()
    {
        var superPickups = _simWorld.SuperPickupRows;

        var handle1 = superPickups.Allocate();
        var handle2 = superPickups.Allocate();
        var handle3 = superPickups.Allocate();

        Assert.Equal(3, superPickups.Count);

        superPickups.Free(handle1);
        Assert.Equal(2, superPickups.Count);

        superPickups.Free(handle2);
        Assert.Equal(1, superPickups.Count);

        superPickups.Free(handle3);
        Assert.Equal(0, superPickups.Count);
    }

    [Fact]
    public void Free_ByRowRef_DecrementsCount()
    {
        var superPickups = _simWorld.SuperPickupRows;

        var handle1 = superPickups.Allocate();
        var handle2 = superPickups.Allocate();
        var handle3 = superPickups.Allocate();

        Assert.Equal(3, superPickups.Count);

        // Get row refs and free them
        var row1 = superPickups.GetRow(handle1);
        var row2 = superPickups.GetRow(handle2);
        var row3 = superPickups.GetRow(handle3);

        superPickups.Free(row1);
        Assert.Equal(2, superPickups.Count);

        superPickups.Free(row2);
        Assert.Equal(1, superPickups.Count);

        superPickups.Free(row3);
        Assert.Equal(0, superPickups.Count);
    }

    [Fact]
    public void TryGetRow_ReturnsFalse_AfterFree()
    {
        var superPickups = _simWorld.SuperPickupRows;

        var handle = superPickups.Allocate();
        int slot = superPickups.GetSlot(handle.StableId);

        Assert.True(superPickups.TryGetRow(slot, out _));

        superPickups.Free(handle);

        Assert.False(superPickups.TryGetRow(slot, out _));
    }

    [Fact]
    public void AllocateAfterFree_ReusesSlot()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Allocate 3
        var handle1 = superPickups.Allocate();
        var handle2 = superPickups.Allocate();
        var handle3 = superPickups.Allocate();

        Assert.Equal(3, superPickups.Count);

        // Free all 3
        superPickups.Free(handle1);
        superPickups.Free(handle2);
        superPickups.Free(handle3);

        Assert.Equal(0, superPickups.Count);

        // Allocate 3 more - should succeed without overflow
        var handle4 = superPickups.Allocate();
        var handle5 = superPickups.Allocate();
        var handle6 = superPickups.Allocate();

        Assert.Equal(3, superPickups.Count);
    }

    [Fact]
    public void MultipleWaves_FreeAndReallocate_NoOverflow()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Wave 1: Fresh allocation
        Assert.Equal(0, superPickups.Count);
        var h1 = superPickups.Allocate();
        Assert.Equal(1, superPickups.Count);
        var h2 = superPickups.Allocate();
        Assert.Equal(2, superPickups.Count);
        var h3 = superPickups.Allocate();
        Assert.Equal(3, superPickups.Count);

        // Wave 1 free
        superPickups.Free(h1);
        Assert.Equal(2, superPickups.Count);
        superPickups.Free(h2);
        Assert.Equal(1, superPickups.Count);
        superPickups.Free(h3);
        Assert.Equal(0, superPickups.Count);

        // Wave 2: Allocation from free list
        var h4 = superPickups.Allocate();
        Assert.Equal(1, superPickups.Count);
        var h5 = superPickups.Allocate();
        Assert.Equal(2, superPickups.Count);
        var h6 = superPickups.Allocate();
        Assert.Equal(3, superPickups.Count);

        // Wave 2 free
        superPickups.Free(h4);
        Assert.Equal(2, superPickups.Count);
        superPickups.Free(h5);
        Assert.Equal(1, superPickups.Count);
        superPickups.Free(h6);
        Assert.Equal(0, superPickups.Count);
    }

    [Fact]
    public void FreeWithIteration_WorksCorrectly()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Allocate 3 pickups
        superPickups.Allocate();
        superPickups.Allocate();
        superPickups.Allocate();

        Assert.Equal(3, superPickups.Count);

        // Free using the iteration pattern from DeactivateAllSuperPickups
        int count = superPickups.Count;
        int freedCount = 0;

        for (int slot = 0; slot < count; slot++)
        {
            if (!superPickups.TryGetRow(slot, out var pickup)) continue;
            superPickups.Free(pickup);
            freedCount++;
        }

        Assert.Equal(3, freedCount);
        Assert.Equal(0, superPickups.Count);
    }

    [Fact]
    public void Reset_ClearsAllPickups()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Allocate some pickups
        superPickups.Allocate();
        superPickups.Allocate();
        superPickups.Allocate();

        Assert.Equal(3, superPickups.Count);

        // Reset the table
        superPickups.Reset();

        Assert.Equal(0, superPickups.Count);

        // Should be able to allocate again
        superPickups.Allocate();
        Assert.Equal(1, superPickups.Count);
    }

    [Fact]
    public void CapacityLimit_ThrowsWhenExceeded()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // SuperPickupRow has Capacity = 8
        for (int i = 0; i < 8; i++)
        {
            superPickups.Allocate();
        }

        Assert.Equal(8, superPickups.Count);

        // 9th allocation should throw
        Assert.Throws<IndexOutOfRangeException>(() => superPickups.Allocate());
    }

    [Fact]
    public void CapacityLimit_CanAllocateAfterFree()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Fill to capacity
        var handles = new SimTable.SimHandle[8];
        for (int i = 0; i < 8; i++)
        {
            handles[i] = superPickups.Allocate();
        }

        Assert.Equal(8, superPickups.Count);

        // Free one
        superPickups.Free(handles[0]);
        Assert.Equal(7, superPickups.Count);

        // Should be able to allocate one more
        superPickups.Allocate();
        Assert.Equal(8, superPickups.Count);
    }

    [Fact]
    public void ManyWaves_StableIdGrowth_DoesNotOverflowPool()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Run 100 waves of fill-and-free to stress test stableId growth
        // StableIds will grow to 800+ but slots should always be reused from free list
        for (int wave = 0; wave < 100; wave++)
        {
            // Allocate to capacity
            var handles = new SimHandle[8];
            for (int i = 0; i < 8; i++)
            {
                handles[i] = superPickups.Allocate();
            }

            Assert.Equal(8, superPickups.Count);

            // Free all
            for (int i = 0; i < 8; i++)
            {
                superPickups.Free(handles[i]);
            }

            Assert.Equal(0, superPickups.Count);
        }
    }

    [Fact]
    public void ManyWaves_WithIterationFree_DoesNotOverflowPool()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // This mimics the actual game pattern where we iterate and free
        for (int wave = 0; wave < 100; wave++)
        {
            // Allocate to capacity
            for (int i = 0; i < 8; i++)
            {
                superPickups.Allocate();
            }

            Assert.Equal(8, superPickups.Count);

            // Free using iteration pattern (like DeactivateAllSuperPickups)
            int count = superPickups.Count;
            for (int slot = 0; slot < count; slot++)
            {
                if (!superPickups.TryGetRow(slot, out var pickup)) continue;
                superPickups.Free(pickup);
            }

            Assert.Equal(0, superPickups.Count);
        }
    }

    [Fact]
    public void PartialFree_ThenAllocate_ReusesFreedSlots()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Allocate to capacity
        var handles = new SimHandle[8];
        for (int i = 0; i < 8; i++)
        {
            handles[i] = superPickups.Allocate();
        }

        Assert.Equal(8, superPickups.Count);

        // Free half
        for (int i = 0; i < 4; i++)
        {
            superPickups.Free(handles[i]);
        }

        Assert.Equal(4, superPickups.Count);

        // Allocate 4 more - should reuse freed slots, not overflow
        for (int i = 0; i < 4; i++)
        {
            superPickups.Allocate();
        }

        Assert.Equal(8, superPickups.Count);
    }

    [Fact]
    public void FreeMiddleSlot_ThenAllocate_ReusesSlot()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Allocate 3
        var h1 = superPickups.Allocate();
        var h2 = superPickups.Allocate();
        var h3 = superPickups.Allocate();

        Assert.Equal(3, superPickups.Count);

        // Free the middle one
        superPickups.Free(h2);
        Assert.Equal(2, superPickups.Count);

        // Allocate - should reuse freed slot
        var h4 = superPickups.Allocate();
        Assert.Equal(3, superPickups.Count);

        // Verify the slot was reused by checking we can still allocate up to capacity
        for (int i = 3; i < 8; i++)
        {
            superPickups.Allocate();
        }

        Assert.Equal(8, superPickups.Count);
    }

    [Fact]
    public void SlotReuse_VerifySlotIndicesStayBounded()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Track all slots used across multiple waves
        var slotsUsed = new HashSet<int>();

        for (int wave = 0; wave < 10; wave++)
        {
            var handles = new SimHandle[8];
            for (int i = 0; i < 8; i++)
            {
                handles[i] = superPickups.Allocate();
                int slot = superPickups.GetSlot(handles[i].StableId);

                // All slots should be in range [0, Capacity)
                Assert.True(slot >= 0 && slot < 8,
                    $"Wave {wave}, allocation {i}: slot {slot} out of bounds [0, 8)");

                slotsUsed.Add(slot);
            }

            // Free all
            for (int i = 0; i < 8; i++)
            {
                superPickups.Free(handles[i]);
            }
        }

        // All slots used should be in valid range
        foreach (var slot in slotsUsed)
        {
            Assert.True(slot >= 0 && slot < 8, $"Slot {slot} out of bounds");
        }
    }

    [Fact]
    public void StableIdToSlot_MappingRemainsConsistent()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Allocate some items
        var h1 = superPickups.Allocate();
        var h2 = superPickups.Allocate();

        int slot1 = superPickups.GetSlot(h1.StableId);
        int slot2 = superPickups.GetSlot(h2.StableId);

        Assert.NotEqual(slot1, slot2);

        // Free first, allocate new
        superPickups.Free(h1);
        var h3 = superPickups.Allocate();

        // With stableId recycling, h3 reuses h1's stableId
        Assert.Equal(h1.StableId, h3.StableId);

        // h2's slot should still be valid
        Assert.Equal(slot2, superPickups.GetSlot(h2.StableId));

        // h3 should have reused h1's slot
        int slot3 = superPickups.GetSlot(h3.StableId);
        Assert.Equal(slot1, slot3);
    }

    [Fact]
    public void Debug_FreeByHandle_VerifyEachStep()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Allocate 3 items and capture detailed info
        var h0 = superPickups.Allocate();
        var h1 = superPickups.Allocate();
        var h2 = superPickups.Allocate();

        Assert.Equal(3, superPickups.Count);

        // Verify each handle has unique stableId
        Assert.NotEqual(h0.StableId, h1.StableId);
        Assert.NotEqual(h1.StableId, h2.StableId);
        Assert.NotEqual(h0.StableId, h2.StableId);

        // Free one at a time and check count
        int countBefore = superPickups.Count;
        superPickups.Free(h0);
        int countAfter = superPickups.Count;

        Assert.Equal(countBefore - 1, countAfter);
        Assert.Equal(2, superPickups.Count);

        // Free second
        countBefore = superPickups.Count;
        superPickups.Free(h1);
        countAfter = superPickups.Count;

        Assert.Equal(countBefore - 1, countAfter);
        Assert.Equal(1, superPickups.Count);

        // Free third
        countBefore = superPickups.Count;
        superPickups.Free(h2);
        countAfter = superPickups.Count;

        Assert.Equal(countBefore - 1, countAfter);
        Assert.Equal(0, superPickups.Count);
    }

    [Fact]
    public void Debug_FreeByStableId_VerifyEachStep()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Allocate 3 items
        var h0 = superPickups.Allocate();
        var h1 = superPickups.Allocate();
        var h2 = superPickups.Allocate();

        Assert.Equal(3, superPickups.Count);

        // Capture stableIds
        int sid0 = h0.StableId;
        int sid1 = h1.StableId;
        int sid2 = h2.StableId;

        // Free by stableId directly
        superPickups.Free(sid0);
        Assert.Equal(2, superPickups.Count);

        superPickups.Free(sid1);
        Assert.Equal(1, superPickups.Count);

        superPickups.Free(sid2);
        Assert.Equal(0, superPickups.Count);
    }

    [Fact]
    public void Debug_Allocate_VerifyStableIdsAreUnique()
    {
        var superPickups = _simWorld.SuperPickupRows;

        var handles = new SimHandle[8];
        for (int i = 0; i < 8; i++)
        {
            handles[i] = superPickups.Allocate();
        }

        // Verify all stableIds are unique
        var stableIds = new HashSet<int>();
        for (int i = 0; i < 8; i++)
        {
            Assert.True(stableIds.Add(handles[i].StableId),
                $"Duplicate stableId {handles[i].StableId} at index {i}");
        }

        // Verify stableIds are 0-7 (sequential)
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(i, handles[i].StableId);
        }
    }

    [Fact]
    public void Debug_TwoWaves_DetailedTrace()
    {
        var superPickups = _simWorld.SuperPickupRows;

        // Wave 1
        var handles1 = new SimHandle[8];
        for (int i = 0; i < 8; i++)
        {
            handles1[i] = superPickups.Allocate();
        }

        Assert.Equal(8, superPickups.Count);

        // Free wave 1 one by one
        for (int i = 0; i < 8; i++)
        {
            int countBefore = superPickups.Count;
            superPickups.Free(handles1[i]);
            int countAfter = superPickups.Count;
            Assert.Equal(countBefore - 1, countAfter);
        }

        Assert.Equal(0, superPickups.Count);

        // Wave 2
        var handles2 = new SimHandle[8];
        for (int i = 0; i < 8; i++)
        {
            handles2[i] = superPickups.Allocate();
            int slot = superPickups.GetSlot(handles2[i].StableId);
            Assert.True(slot >= 0 && slot < 8,
                $"Wave 2 allocation {i}: slot {slot} out of bounds, stableId={handles2[i].StableId}");
        }

        Assert.Equal(8, superPickups.Count);

        // Free wave 2 one by one
        for (int i = 0; i < 8; i++)
        {
            int countBefore = superPickups.Count;
            int stableId = handles2[i].StableId;
            int slotBefore = superPickups.GetSlot(stableId);

            superPickups.Free(handles2[i]);

            int countAfter = superPickups.Count;
            Assert.Equal(countBefore - 1, countAfter);
        }

        Assert.Equal(0, superPickups.Count);
    }
}
