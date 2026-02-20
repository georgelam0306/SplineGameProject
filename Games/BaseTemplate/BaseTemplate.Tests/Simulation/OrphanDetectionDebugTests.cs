using Xunit;
using Xunit.Abstractions;
using Friflo.Engine.ECS;
using SimTable;
using BaseTemplate.Simulation;
using BaseTemplate.Simulation.Components;
using BaseTemplate.Simulation.Systems;
using BaseTemplate.Presentation.Rendering.Components;
using BaseTemplate.Simulation.Utilities;
using BaseTemplate.Infrastructure.Rollback;

namespace BaseTemplate.Tests.Simulation;

/// <summary>
/// Diagnostic tests for debugging orphan detection issues.
/// These tests have verbose console output to help identify why
/// orphan detection might be failing in the actual game.
/// </summary>
public class OrphanDetectionDebugTests : IDisposable
{
    private readonly EntityStore _store;
    private readonly SimWorld _simWorld;
    private readonly SimWorldSyncManager _syncManager;
    private readonly int _unitTableId;
    private readonly ITestOutputHelper _output;
    private readonly MultiPlayerInputBuffer _inputBuffer;

    public OrphanDetectionDebugTests(ITestOutputHelper output)
    {
        _output = output;
        _store = new EntityStore();
        _simWorld = new SimWorld();
        _syncManager = new SimWorldSyncManager(_store, _simWorld);
        _unitTableId = SimWorld.GetTableId<UnitRow>();
        _inputBuffer = new MultiPlayerInputBuffer(60, 2); // 60 frames, 2 players
    }

    private SimulationContext CreateContext(int frame)
    {
        return new SimulationContext(frame, 1, 12345, _inputBuffer);
    }

    public void Dispose()
    {
        _simWorld.Dispose();
    }

    private Entity CreateLinkedEntity(SimHandle handle)
    {
        var entity = _store.CreateEntity();
        entity.AddComponent(new SimSlotRef
        {
            StableId = handle.StableId,
            TableId = handle.TableId
        });
        entity.AddComponent(new Transform2D());
        return entity;
    }

    private int CountUnitEntities()
    {
        int count = 0;
        var query = _store.Query<SimSlotRef>();
        foreach (var entity in query.Entities)
        {
            var slotRef = entity.GetComponent<SimSlotRef>();
            if (slotRef.TableId == _unitTableId)
                count++;
        }
        return count;
    }

    [Fact]
    public void Test1_AfterFree_GetStableIdReturnsNegativeOne()
    {
        var units = _simWorld.UnitRows;
        var handle = units.Allocate();
        int originalSlot = units.GetSlot(handle.StableId);

        _output.WriteLine($"Allocated: stableId={handle.StableId}, slot={originalSlot}");

        units.Free(handle.StableId);

        int slotStableId = units.GetStableId(originalSlot);
        _output.WriteLine($"After Free: GetStableId({originalSlot}) = {slotStableId}");

        Assert.Equal(-1, slotStableId);
    }

    [Fact]
    public void Test2_AfterKill_EntityCountShouldDecrease()
    {
        var handles = new List<SimHandle>();
        for (int i = 0; i < 5; i++)
        {
            var h = _simWorld.UnitRows.Allocate();
            var row = _simWorld.UnitRows.GetRow(h);
            row.IsActive = true;
            row.IsPlayer = false;
            handles.Add(h);
            CreateLinkedEntity(h);
        }

        int beforeKill = CountUnitEntities();
        _output.WriteLine($"Before kill: {beforeKill} entities");
        Assert.Equal(5, beforeKill);

        foreach (var h in handles)
        {
            _output.WriteLine($"Freeing stableId={h.StableId}");
            _simWorld.UnitRows.Free(h.StableId);
        }

        _syncManager.SyncSimWorldToFriflo();

        int afterKill = CountUnitEntities();
        _output.WriteLine($"After kill+sync: {afterKill} entities");
        Assert.Equal(0, afterKill);
    }

    [Fact]
    public void Test3_DebugTrace_WhatHappensOnSync()
    {
        var handle = _simWorld.UnitRows.Allocate();
        var row = _simWorld.UnitRows.GetRow(handle);
        row.IsActive = true;
        row.Health = 1;
        row.IsPlayer = false;

        var entity = CreateLinkedEntity(handle);
        var slotRef = entity.GetComponent<SimSlotRef>();

        _output.WriteLine($"Created: stableId={handle.StableId}");
        _output.WriteLine($"SimSlotRef: StableId={slotRef.StableId}");

        // Kill it
        _simWorld.UnitRows.Free(handle.StableId);

        // Check what GetSlot returns NOW - should be -1 after Free()
        int slot = _simWorld.UnitRows.GetSlot(handle.StableId);
        int slotStableId = slot >= 0 ? _simWorld.UnitRows.GetStableId(slot) : -1;

        _output.WriteLine($"After Free:");
        _output.WriteLine($"  GetSlot({handle.StableId}) = {slot}");
        _output.WriteLine($"  GetStableId({slot}) = {slotStableId}");

        // Which orphan condition triggers?
        bool cond1 = slot < 0;
        bool cond2 = slotStableId != handle.StableId;

        _output.WriteLine($"Orphan conditions:");
        _output.WriteLine($"  slot<0: {cond1}");
        _output.WriteLine($"  stableId mismatch ({slotStableId} != {handle.StableId}): {cond2}");
        _output.WriteLine($"Should be orphan: {cond1 || cond2}");

        // With dedicated free list, GetSlot should return -1
        Assert.True(slot < 0, "GetSlot should return -1 after Free()");

        // Now sync
        _syncManager.SyncSimWorldToFriflo();

        _output.WriteLine($"After sync - entity alive: {!_store.GetEntityById(entity.Id).IsNull}");
        Assert.True(_store.GetEntityById(entity.Id).IsNull, "Entity should be deleted");
    }

    [Fact]
    public void Test4_FullKillFlow_UnitDeathSystem_ThenSync()
    {
        var units = _simWorld.UnitRows;

        // Create unit with Health=1
        var handle = units.Allocate();
        var row = units.GetRow(handle);
        row.Position = new Fixed64Vec2(Fixed64.FromInt(100), Fixed64.FromInt(200));
        row.IsActive = true;
        row.Health = 1;
        row.IsPlayer = false;

        _output.WriteLine($"Created unit: stableId={handle.StableId}, Health={row.Health}, IsActive={row.IsActive}, IsPlayer={row.IsPlayer}");

        var entity = CreateLinkedEntity(handle);

        // Deal lethal damage
        row.Health = 0;
        _output.WriteLine($"Dealt damage: Health={row.Health}");

        // Run UnitDeathSystem
        var deathSystem = new UnitDeathSystem(_simWorld);
        deathSystem.Tick(CreateContext(1));

        // Check state after death system
        int slot = units.GetSlot(handle.StableId);
        _output.WriteLine($"After UnitDeathSystem:");
        _output.WriteLine($"  GetSlot({handle.StableId}) = {slot}");
        _output.WriteLine($"  units.Count = {units.Count}");

        // Run sync
        _syncManager.SyncSimWorldToFriflo();

        _output.WriteLine($"After sync - entity alive: {!_store.GetEntityById(entity.Id).IsNull}");
        Assert.True(_store.GetEntityById(entity.Id).IsNull, "Entity should be deleted after UnitDeathSystem + Sync");
    }

    [Fact]
    public void Test5_FreeListPointerCoincidence_StillDetectsOrphan()
    {
        var units = _simWorld.UnitRows;

        // Allocate 3 units
        var h0 = units.Allocate();
        var h1 = units.Allocate();
        var h2 = units.Allocate();

        _output.WriteLine($"Allocated: h0.stableId={h0.StableId}, h1.stableId={h1.StableId}, h2.stableId={h2.StableId}");

        // Set all as active non-players
        foreach (var h in new[] { h0, h1, h2 })
        {
            var row = units.GetRow(h);
            row.IsActive = true;
            row.IsPlayer = false;
        }

        var e2 = CreateLinkedEntity(h2);

        // Free in sequence: 0, 1, 2
        _output.WriteLine("Freeing in sequence: 0, 1, 2");
        units.Free(h0.StableId);
        units.Free(h1.StableId);
        units.Free(h2.StableId);

        // Debug: what does GetSlot return?
        int slot0 = units.GetSlot(h0.StableId);
        int slot1 = units.GetSlot(h1.StableId);
        int slot2 = units.GetSlot(h2.StableId);

        _output.WriteLine($"After freeing 0,1,2:");
        _output.WriteLine($"  GetSlot(0) = {slot0}");
        _output.WriteLine($"  GetSlot(1) = {slot1}");
        _output.WriteLine($"  GetSlot(2) = {slot2}");

        if (slot2 >= 0)
        {
            int stableIdAtSlot = units.GetStableId(slot2);
            _output.WriteLine($"  GetStableId({slot2}) = {stableIdAtSlot}");
        }

        // Sync should still detect orphan
        _syncManager.SyncSimWorldToFriflo();
        Assert.True(_store.GetEntityById(e2.Id).IsNull, "Entity should be deleted even with free list pointer coincidence");
    }

    [Fact]
    public void Test6_IsActiveFalse_WithoutFree_StillDeletes()
    {
        var units = _simWorld.UnitRows;

        var handle = units.Allocate();
        var row = units.GetRow(handle);
        row.IsActive = true;
        row.IsPlayer = false;

        var entity = CreateLinkedEntity(handle);

        _output.WriteLine($"Created: stableId={handle.StableId}, IsActive={row.IsActive}");

        // Set IsActive = false but DON'T call Free()
        row.IsActive = false;
        _output.WriteLine($"Set IsActive = false (without Free)");

        _syncManager.SyncSimWorldToFriflo();

        _output.WriteLine($"After sync - entity alive: {!_store.GetEntityById(entity.Id).IsNull}");
        Assert.True(_store.GetEntityById(entity.Id).IsNull, "Entity should be deleted by IsActive check");
    }

    [Fact]
    public void Test7_MultipleWaves_NoOrphanEntitiesAccumulate()
    {
        for (int wave = 0; wave < 3; wave++)
        {
            _output.WriteLine($"=== Wave {wave} ===");

            // Spawn 10 enemies
            var handles = new List<SimHandle>();
            for (int i = 0; i < 10; i++)
            {
                var h = _simWorld.UnitRows.Allocate();
                var row = _simWorld.UnitRows.GetRow(h);
                row.IsActive = true;
                row.IsPlayer = false;
                row.Health = 1;
                handles.Add(h);
                CreateLinkedEntity(h);
            }

            int countAfterSpawn = CountUnitEntities();
            _output.WriteLine($"After spawn: {countAfterSpawn} Friflo entities, {_simWorld.UnitRows.Count} SimWorld units");

            // Kill all via UnitDeathSystem (set health to 0)
            foreach (var h in handles)
            {
                var row = _simWorld.UnitRows.GetRow(h);
                row.Health = 0;
            }

            // Run death system
            var deathSystem = new UnitDeathSystem(_simWorld);
            deathSystem.Tick(CreateContext(wave * 100));

            _output.WriteLine($"After UnitDeathSystem: {_simWorld.UnitRows.Count} SimWorld units");

            // Sync
            _syncManager.SyncSimWorldToFriflo();

            int countAfterKill = CountUnitEntities();
            _output.WriteLine($"After sync: {countAfterKill} Friflo entities");

            Assert.Equal(0, countAfterKill);
        }
    }

    [Fact]
    public void Test8_VerifyUnitDeathSystemDetectsDeadUnits()
    {
        var units = _simWorld.UnitRows;

        // Create 5 units - 3 with Health=0, 2 with Health=1
        var deadHandles = new List<SimHandle>();
        var aliveHandles = new List<SimHandle>();

        for (int i = 0; i < 3; i++)
        {
            var h = units.Allocate();
            var row = units.GetRow(h);
            row.IsActive = true;
            row.IsPlayer = false;
            row.Health = 0; // DEAD
            deadHandles.Add(h);
            _output.WriteLine($"Created dead unit: stableId={h.StableId}, Health=0");
        }

        for (int i = 0; i < 2; i++)
        {
            var h = units.Allocate();
            var row = units.GetRow(h);
            row.IsActive = true;
            row.IsPlayer = false;
            row.Health = 1; // ALIVE
            aliveHandles.Add(h);
            _output.WriteLine($"Created alive unit: stableId={h.StableId}, Health=1");
        }

        _output.WriteLine($"Before UnitDeathSystem: {units.Count} units");

        // Run death system
        var deathSystem = new UnitDeathSystem(_simWorld);
        deathSystem.Tick(CreateContext(1));

        _output.WriteLine($"After UnitDeathSystem: {units.Count} units");

        // Check dead units were freed
        foreach (var h in deadHandles)
        {
            int slot = units.GetSlot(h.StableId);
            _output.WriteLine($"Dead unit stableId={h.StableId}: GetSlot={slot}");
        }

        // Check alive units still exist
        foreach (var h in aliveHandles)
        {
            int slot = units.GetSlot(h.StableId);
            bool isActive = slot >= 0 && units.IsActive(slot);
            _output.WriteLine($"Alive unit stableId={h.StableId}: GetSlot={slot}, IsActive={isActive}");
            Assert.True(slot >= 0, $"Alive unit {h.StableId} should still have valid slot");
        }

        Assert.Equal(2, units.Count);
    }
}
