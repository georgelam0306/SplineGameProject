using System;
using System.Numerics;
using Core;
using DerpLib.Ecs;
using DerpLib.Ecs.Editor;
using FixedMath;

namespace DerpLib.Ecs.Editor.Smoke;

internal static class SmokeUse
{
    private sealed class AutoKeyCaptureSink : IEcsEditorAutoKeySink
    {
        public int ChangeCommandCount { get; private set; }

        public void OnCommand(in EcsEditorCommand command)
        {
            ChangeCommandCount++;
        }
    }

    public static void Touch()
    {
        var world = new DemoWorld();
        DemoWorldUgcBakedAssets.LoadFromFile("DemoWorld.derpentitydata", world);
        if (world.DemoKind.Count != 1)
        {
            throw new Exception("Expected baked world to spawn one entity.");
        }

        int row = 0;
        EntityHandle entity = world.DemoKind.Entity(row);

        if (world.DemoKind.UgcStats(row).Hp != 7 ||
            world.DemoKind.UgcStats(row).State != UgcState.Aggro)
        {
            throw new Exception("Unexpected baked component values.");
        }

        DemoKindTable.UgcStatsProxy ugcStatsProxy = world.DemoKind.Row(row).UgcStats;
        ResizableReadOnlyView<int> waypoints = ugcStatsProxy.Waypoints;
        if (waypoints.Count != 4 || waypoints[0] != 1 || waypoints[3] != 4)
        {
            throw new Exception("Unexpected baked var-heap list contents.");
        }

        // Scalar type assertions
        ref readonly UgcStatsComponent stats = ref world.DemoKind.UgcStats(row);
        if (!stats.Active)
        {
            throw new Exception("Expected Active == true.");
        }

        if (stats.Offset != new Vector2(5f, -3f))
        {
            throw new Exception("Expected Offset == (5, -3).");
        }

        Fixed64 expectedSimSpeed = Fixed64.FromFloat(1.5f);
        if (stats.SimSpeed != expectedSimSpeed)
        {
            throw new Exception("Expected SimSpeed ≈ 1.5.");
        }

        Fixed64Vec2 expectedSimPos = new Fixed64Vec2(
            Fixed64.FromInt(100),
            Fixed64.FromInt(200));
        if (stats.SimPos != expectedSimPos)
        {
            throw new Exception("Expected SimPos ≈ (100, 200).");
        }

        StringHandle expectedTag = "hero";
        if (stats.Tag != expectedTag)
        {
            throw new Exception("Expected Tag == \"hero\".");
        }

        // New list type assertions
        ResizableReadOnlyView<float> speeds = ugcStatsProxy.Speeds;
        if (speeds.Count != 3 || speeds[0] != 1.0f || speeds[1] != 2.5f || speeds[2] != 3.0f)
        {
            throw new Exception("Unexpected baked Speeds list contents.");
        }

        ResizableReadOnlyView<bool> flags = ugcStatsProxy.Flags;
        if (flags.Count != 3 || !flags[0] || flags[1] || !flags[2])
        {
            throw new Exception("Unexpected baked Flags list contents.");
        }

        ResizableReadOnlyView<Vector2> points = ugcStatsProxy.Points;
        if (points.Count != 2 || points[0] != new Vector2(1f, 2f) || points[1] != new Vector2(3f, 4f))
        {
            throw new Exception("Unexpected baked Points list contents.");
        }

        // Dedup verification: WaypointsCopy should share same var-heap offset as Waypoints
        ResizableReadOnlyView<int> waypointsCopy = ugcStatsProxy.WaypointsCopy;
        if (waypointsCopy.Count != 4 || waypointsCopy[0] != 1 || waypointsCopy[3] != 4)
        {
            throw new Exception("Unexpected baked WaypointsCopy list contents.");
        }
        if (stats.Waypoints.OffsetBytes != stats.WaypointsCopy.OffsetBytes)
        {
            throw new Exception("Expected dedup: Waypoints and WaypointsCopy should share the same var-heap offset.");
        }

        var undoStack = new EcsEditorUndoStack(maxTransactions: 8);
        var autoKeySink = new AutoKeyCaptureSink();
        var pipeline = new EcsEditorCommandPipeline(
            processors: new IEcsEditorCommandProcessor[]
            {
                new EcsEditorCoalesceSetPropertyProcessor(),
                new EcsEditorAutoKeyProcessor(autoKeySink)
            },
            scratchCapacity: 32);

        EditorContext.UndoStack = undoStack;
        EditorContext.Pipeline = pipeline;

        try
        {
            EditorContext.UndoStack = null;
            using (var edit = world.BeginChange(entity))
            {
                edit.Demo.Speed = 5f;
                edit.UgcStats.Hp = 7;
                edit.UgcStats.Speed = 2.5f;
                edit.UgcStats.State = UgcState.Aggro;
                edit.UgcStats.Tint = new Color32(1, 2, 3, 255);
            }
            EditorContext.UndoStack = undoStack;

            EditorContext.Pipeline = null;
            using (var edit = world.BeginChange(entity))
            {
                edit.Demo.Speed = 1f;
                edit.Demo.Speed = 42f;
                edit.Demo.SimSpeed = Fixed64.FromInt(123);
                edit.Demo.SimPos = Fixed64Vec2.FromInt(1, 2);
                edit.Demo.ViewOffset = new Vector2(3f, 4f);
                edit.Demo.Tint = new Color32(8, 9, 10, 255);
                StringHandle handle = "Hello";
                edit.Demo.Label = handle;
                edit.UgcStats.Hp = 123;
            }
            EditorContext.Pipeline = pipeline;
        }
        finally
        {
            EditorContext.UndoStack = null;
            EditorContext.Pipeline = null;
        }

        float afterEditSpeed = world.DemoKind.Demo(row).Speed;
        if (afterEditSpeed != 42f)
        {
            throw new Exception("Expected edit session to apply changes.");
        }

        if (!undoStack.TryUndo(out ReadOnlySpan<EcsEditorCommand> undoCommands))
        {
            throw new Exception("Expected undo to be available.");
        }

        world.ApplyEditorCommands(undoCommands);

        float afterUndoSpeed = world.DemoKind.Demo(row).Speed;
        if (afterUndoSpeed != 0f)
        {
            throw new Exception("Expected undo to restore previous values.");
        }

        if (!undoStack.TryRedo(out ReadOnlySpan<EcsEditorCommand> redoCommands))
        {
            throw new Exception("Expected redo to be available.");
        }

        world.ApplyEditorCommands(redoCommands);

        float afterRedoSpeed = world.DemoKind.Demo(row).Speed;
        if (afterRedoSpeed != 42f)
        {
            throw new Exception("Expected redo to re-apply changes.");
        }

        if (autoKeySink.ChangeCommandCount == 0)
        {
            throw new Exception("Expected auto-key sink to observe Change commands when pipeline enabled.");
        }
    }
}
