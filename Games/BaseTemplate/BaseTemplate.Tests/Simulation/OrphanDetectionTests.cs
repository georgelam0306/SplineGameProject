using Xunit;
using Friflo.Engine.ECS;
using SimTable;
using BaseTemplate.Simulation;
using BaseTemplate.Simulation.Components;
using BaseTemplate.Presentation.Rendering.Components;

namespace BaseTemplate.Tests.Simulation;

/// <summary>
/// Integration tests for SimWorldSyncManager orphan detection.
/// These tests verify that SyncSimWorldToFriflo() correctly detects and deletes
/// orphaned Friflo entities when their corresponding SimWorld slots are freed.
/// </summary>
public class OrphanDetectionTests : IDisposable
{
    private readonly EntityStore _store;
    private readonly SimWorld _simWorld;
    private readonly SimWorldSyncManager _syncManager;
    private readonly int _unitTableId;

    public OrphanDetectionTests()
    {
        _store = new EntityStore();
        _simWorld = new SimWorld();
        _syncManager = new SimWorldSyncManager(_store, _simWorld);
        _unitTableId = SimWorld.GetTableId<UnitRow>();
    }

    public void Dispose()
    {
        _simWorld.Dispose();
    }

    /// <summary>
    /// Helper to create a Friflo entity linked to a SimWorld unit.
    /// </summary>
    private Entity CreateLinkedEntity(int stableId)
    {
        var entity = _store.CreateEntity();
        entity.AddComponent(new SimSlotRef { TableId = _unitTableId, StableId = stableId });
        entity.AddComponent(new Transform2D { X = 0, Y = 0 });
        return entity;
    }

    /// <summary>
    /// Helper to create a Friflo entity linked to a SimWorld unit using a handle.
    /// </summary>
    private Entity CreateLinkedEntity(SimHandle handle)
    {
        return CreateLinkedEntity(handle.StableId);
    }

    /// <summary>
    /// Helper to allocate a unit and set it as active.
    /// </summary>
    private SimHandle AllocateActiveUnit()
    {
        var units = _simWorld.UnitRows;
        var handle = units.Allocate();
        units.IsActive(units.GetSlot(handle.StableId)) = true;
        return handle;
    }

    #region Test 10: Sync Deletes Orphaned Entity After Free

    [Fact]
    public void SyncDeletesOrphanedEntity_AfterFree()
    {
        // Scenario: Kill a unit, verify sync deletes the Friflo entity
        var units = _simWorld.UnitRows;

        // Spawn unit with Friflo entity (active)
        var handle = AllocateActiveUnit();
        int stableId = handle.StableId;
        var entity = CreateLinkedEntity(stableId);
        int entityId = entity.Id;

        // Verify entity exists before kill
        Assert.False(_store.GetEntityById(entityId).IsNull);

        // Kill unit
        int slot = units.GetSlot(stableId);
        units.IsActive(slot) = false;
        units.Free(stableId);

        // Sync
        _syncManager.SyncSimWorldToFriflo();

        // Assert Friflo entity was deleted
        Assert.True(_store.GetEntityById(entityId).IsNull,
            "Friflo entity should be deleted after its SimWorld unit is freed");
    }

    #endregion

    #region Test 11: Sync Keeps Valid Entities

    [Fact]
    public void SyncKeepsValidEntities_WhenNotFreed()
    {
        // Scenario: Don't delete entities that are still alive in SimWorld

        // Spawn and keep alive (active)
        var handle = AllocateActiveUnit();
        int stableId = handle.StableId;
        var entity = CreateLinkedEntity(stableId);
        int entityId = entity.Id;

        // Sync without killing
        _syncManager.SyncSimWorldToFriflo();

        // Assert entity still exists
        Assert.False(_store.GetEntityById(entityId).IsNull,
            "Friflo entity should remain when its SimWorld unit is still active");
    }

    #endregion

    #region Test 12: Sync Handles Multiple Frees

    [Fact]
    public void SyncHandlesMultipleFrees_AllOrphansDeleted()
    {
        // Scenario: Multiple units killed, all Friflo entities deleted
        var units = _simWorld.UnitRows;

        // Spawn 3 active units
        var handles = new List<(SimHandle handle, Entity entity, int entityId)>();
        for (int i = 0; i < 3; i++)
        {
            var h = AllocateActiveUnit();
            var e = CreateLinkedEntity(h.StableId);
            handles.Add((h, e, e.Id));
        }

        // Kill all
        foreach (var (handle, _, _) in handles)
        {
            int slot = units.GetSlot(handle.StableId);
            units.IsActive(slot) = false;
            units.Free(handle.StableId);
        }

        // Sync
        _syncManager.SyncSimWorldToFriflo();

        // All deleted
        foreach (var (_, _, entityId) in handles)
        {
            Assert.True(_store.GetEntityById(entityId).IsNull,
                $"Entity {entityId} should be deleted after its SimWorld unit is freed");
        }
    }

    #endregion

    #region Test 13: Sync Handles IsActive=false Without Free

    [Fact]
    public void SyncHandlesIsActiveFalse_WithoutFree()
    {
        // Scenario: Unit starts active, then marked inactive but not freed - should still be deleted
        // This tests the IsActive safety net in SimWorldSyncManager
        var units = _simWorld.UnitRows;

        // Allocate an ACTIVE unit first
        var handle = AllocateActiveUnit();
        int stableId = handle.StableId;
        int slot = units.GetSlot(stableId);

        var entity = CreateLinkedEntity(stableId);
        int entityId = entity.Id;

        // Verify entity exists and unit is active
        Assert.False(_store.GetEntityById(entityId).IsNull);
        Assert.True(units.IsActive(slot));

        // Mark inactive WITHOUT calling Free
        units.IsActive(slot) = false;

        // Sync
        _syncManager.SyncSimWorldToFriflo();

        // Entity should still be deleted due to IsActive check
        Assert.True(_store.GetEntityById(entityId).IsNull,
            "Friflo entity should be deleted when IsActive is false, even without Free()");
    }

    #endregion

    #region Test 14: StableId Recycling - Freed StableIds Return -1

    [Fact]
    public void StableIdRecycling_GetSlotReturnsNegativeOne_AfterFree()
    {
        // Scenario: Verify that after Free(), GetSlot returns -1, enabling reliable orphan detection.
        // This is the core fix - _stableIdToSlot is no longer repurposed as free list storage.
        var units = _simWorld.UnitRows;

        // Spawn unit A (active)
        var handleA = AllocateActiveUnit();
        int stableIdA = handleA.StableId;
        var entityA = CreateLinkedEntity(handleA);
        int entityIdA = entityA.Id;

        // Kill unit A
        int slotA = units.GetSlot(stableIdA);
        units.IsActive(slotA) = false;
        units.Free(stableIdA);

        // CRITICAL: GetSlot should now return -1 for freed stableId
        int slotAfterFree = units.GetSlot(stableIdA);
        Assert.Equal(-1, slotAfterFree);

        // Sync should detect orphan because slot < 0
        _syncManager.SyncSimWorldToFriflo();

        // Entity A should be deleted
        Assert.True(_store.GetEntityById(entityIdA).IsNull,
            "Entity A should be deleted because GetSlot returns -1 after Free");

        // Now allocate again - stableId should be recycled
        var handleB = AllocateActiveUnit();
        int stableIdB = handleB.StableId;

        // Verify stableId was recycled
        Assert.Equal(stableIdA, stableIdB);

        // GetSlot should now return valid slot for recycled stableId
        int slotB = units.GetSlot(stableIdB);
        Assert.True(slotB >= 0, "GetSlot should return valid slot after reallocation");
    }

    #endregion

    #region Test 16: Multiple Kills Same Frame - All Detected in Single Sync

    [Fact]
    public void MultipleKillsSameFrame_AllOrphansDetectedInSingleSync()
    {
        // Scenario: Kill 5 enemies in the same "frame", verify ALL are detected
        // and deleted in a single SyncSimWorldToFriflo call
        var units = _simWorld.UnitRows;

        // Spawn 10 active units with Friflo entities
        var allHandles = new List<(SimHandle handle, Entity entity, int entityId)>();
        for (int i = 0; i < 10; i++)
        {
            var h = AllocateActiveUnit();
            var e = CreateLinkedEntity(h.StableId);
            allHandles.Add((h, e, e.Id));
        }

        // Kill units 0, 2, 4, 6, 8 (every other one) - 5 kills in "same frame"
        var killedEntityIds = new List<int>();
        for (int i = 0; i < 10; i += 2)
        {
            var (handle, _, entityId) = allHandles[i];
            int slot = units.GetSlot(handle.StableId);
            units.IsActive(slot) = false;
            units.Free(handle.StableId);
            killedEntityIds.Add(entityId);
        }

        // Single sync call should detect and delete ALL 5 orphans
        _syncManager.SyncSimWorldToFriflo();

        // Verify all 5 killed entities are deleted
        foreach (var entityId in killedEntityIds)
        {
            Assert.True(_store.GetEntityById(entityId).IsNull,
                $"Entity {entityId} should be deleted - was it missed by orphan detection?");
        }

        // Verify the 5 alive entities still exist
        for (int i = 1; i < 10; i += 2)
        {
            var (_, _, entityId) = allHandles[i];
            Assert.False(_store.GetEntityById(entityId).IsNull,
                $"Entity {entityId} should still exist");
        }
    }

    #endregion

    #region Test 17: Batch Kill - Simulates Explosion Hitting Many Enemies

    [Fact]
    public void BatchKill_ExplosionScenario_AllOrphansDeleted()
    {
        // Scenario: Simulate an explosion killing 20 enemies at once
        var units = _simWorld.UnitRows;

        // Spawn 30 units
        var allHandles = new List<(SimHandle handle, Entity entity, int entityId)>();
        for (int i = 0; i < 30; i++)
        {
            var h = AllocateActiveUnit();
            var e = CreateLinkedEntity(h.StableId);
            allHandles.Add((h, e, e.Id));
        }

        // Kill first 20 (explosion hits them)
        var killedEntityIds = new List<int>();
        for (int i = 0; i < 20; i++)
        {
            var (handle, _, entityId) = allHandles[i];
            int slot = units.GetSlot(handle.StableId);
            units.IsActive(slot) = false;
            units.Free(handle.StableId);
            killedEntityIds.Add(entityId);
        }

        // Verify state before sync
        Assert.Equal(10, units.Count); // 30 - 20 = 10 remaining

        // Single sync should delete all 20 orphans
        _syncManager.SyncSimWorldToFriflo();

        // All 20 killed should be deleted
        int deletedCount = 0;
        foreach (var entityId in killedEntityIds)
        {
            if (_store.GetEntityById(entityId).IsNull)
                deletedCount++;
        }

        Assert.Equal(20, deletedCount);

        // 10 survivors should remain
        for (int i = 20; i < 30; i++)
        {
            var (_, _, entityId) = allHandles[i];
            Assert.False(_store.GetEntityById(entityId).IsNull,
                $"Survivor entity {entityId} should still exist");
        }
    }

    #endregion

    #region Test 18: Free List - All Freed StableIds Return -1

    [Fact]
    public void FreeList_AllFreedStableIds_ReturnNegativeOne()
    {
        // Scenario: After multiple frees, GetSlot returns -1 for ALL freed stableIds.
        // With the dedicated _stableIdNextFree array, _stableIdToSlot is always set to -1 on Free.
        var units = _simWorld.UnitRows;

        // Allocate 5 units
        var handles = new List<SimHandle>();
        var entities = new List<Entity>();
        for (int i = 0; i < 5; i++)
        {
            var h = AllocateActiveUnit();
            handles.Add(h);
            entities.Add(CreateLinkedEntity(h.StableId));
        }

        // Free in order: 0, 1, 2, 3, 4
        for (int i = 0; i < 5; i++)
        {
            int slot = units.GetSlot(handles[i].StableId);
            units.IsActive(slot) = false;
            units.Free(handles[i].StableId);
        }

        // With dedicated free list, ALL freed stableIds should return -1
        // (This is the fix - previously they returned free list pointers)
        for (int i = 0; i < 5; i++)
        {
            int getSlot = units.GetSlot(handles[i].StableId);
            Assert.Equal(-1, getSlot);
        }

        // Sync should detect ALL as orphans (slot < 0)
        _syncManager.SyncSimWorldToFriflo();

        // ALL 5 entities should be deleted
        for (int i = 0; i < 5; i++)
        {
            Assert.True(_store.GetEntityById(entities[i].Id).IsNull,
                $"Entity for stableId={handles[i].StableId} should be deleted");
        }
    }

    #endregion

    #region Test 19: Verify Friflo Query Iteration Completeness

    [Fact]
    public void FrifloQueryIteration_AllEntitiesVisited()
    {
        // Scenario: Verify that the Friflo query visits ALL entities
        // This tests that no entities are skipped during iteration
        var units = _simWorld.UnitRows;

        // Create 50 units with entities
        var entityIds = new HashSet<int>();
        for (int i = 0; i < 50; i++)
        {
            var h = AllocateActiveUnit();
            var e = CreateLinkedEntity(h.StableId);
            entityIds.Add(e.Id);
        }

        // Count how many entities the sync query finds
        var query = _store.Query<SimSlotRef, Transform2D>();
        int queryCount = 0;
        foreach (var _ in query.Entities)
        {
            queryCount++;
        }

        Assert.Equal(50, queryCount);

        // Kill half
        var handles = new List<SimHandle>();
        for (int i = 0; i < 50; i++)
        {
            // Re-get the handles by iterating
            var h = _simWorld.UnitRows.Allocate();
            // Undo the allocation for this test
            _simWorld.UnitRows.Free(h.StableId);
        }
    }

    #endregion

    #region Test 20: Sync Called Multiple Times - No Double Delete

    [Fact]
    public void SyncCalledMultipleTimes_NoDoubleDeleteCrash()
    {
        // Scenario: Call sync multiple times after kills - should not crash
        var units = _simWorld.UnitRows;

        // Spawn and kill
        var h = AllocateActiveUnit();
        var e = CreateLinkedEntity(h.StableId);
        int entityId = e.Id;

        int slot = units.GetSlot(h.StableId);
        units.IsActive(slot) = false;
        units.Free(h.StableId);

        // First sync - should delete
        _syncManager.SyncSimWorldToFriflo();
        Assert.True(_store.GetEntityById(entityId).IsNull);

        // Second sync - should not crash (entity already gone)
        _syncManager.SyncSimWorldToFriflo();

        // Third sync - still no crash
        _syncManager.SyncSimWorldToFriflo();
    }

    #endregion

    #region Test 15: Partial Kill - Some Alive Some Dead

    [Fact]
    public void PartialKill_SomeAliveSomeDead_CorrectlyHandled()
    {
        // Scenario: Mix of alive and dead units, verify correct behavior
        var units = _simWorld.UnitRows;

        // Spawn 4 active units
        var alive1 = AllocateActiveUnit();
        var dead1 = AllocateActiveUnit();
        var alive2 = AllocateActiveUnit();
        var dead2 = AllocateActiveUnit();

        // Create linked entities
        var entityAlive1 = CreateLinkedEntity(alive1.StableId);
        var entityDead1 = CreateLinkedEntity(dead1.StableId);
        var entityAlive2 = CreateLinkedEntity(alive2.StableId);
        var entityDead2 = CreateLinkedEntity(dead2.StableId);

        int idAlive1 = entityAlive1.Id;
        int idDead1 = entityDead1.Id;
        int idAlive2 = entityAlive2.Id;
        int idDead2 = entityDead2.Id;

        // Kill only dead1 and dead2
        int slotDead1 = units.GetSlot(dead1.StableId);
        units.IsActive(slotDead1) = false;
        units.Free(dead1.StableId);
        int slotDead2 = units.GetSlot(dead2.StableId);
        units.IsActive(slotDead2) = false;
        units.Free(dead2.StableId);

        // Sync
        _syncManager.SyncSimWorldToFriflo();

        // Alive entities should remain
        Assert.False(_store.GetEntityById(idAlive1).IsNull, "Alive entity 1 should remain");
        Assert.False(_store.GetEntityById(idAlive2).IsNull, "Alive entity 2 should remain");

        // Dead entities should be deleted
        Assert.True(_store.GetEntityById(idDead1).IsNull, "Dead entity 1 should be deleted");
        Assert.True(_store.GetEntityById(idDead2).IsNull, "Dead entity 2 should be deleted");
    }

    #endregion
}
