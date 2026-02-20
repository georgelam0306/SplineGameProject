using System.Numerics;
using DerpLib.ImGui.Core;
using Property;

namespace Derp.UI;

internal enum UndoRecordKind : byte
{
    None = 0,
    SetProperty = 1,
    SetPropertyBatch = 2,
    SetFillComponent = 3,
    SetPropertyMany = 4,
    SetComponentSnapshot = 14,
    SetComponentSnapshotMany = 26,
    CreatePrefab = 5,
    CreateShape = 6,
    CreateText = 13,
    CreateBooleanGroup = 7,
    SetTransformComponent = 8,
    ResizeShape = 9,
    MovePrefab = 10,
    ResizePrefab = 25,
    SetBooleanGroupOpIndex = 11,
    AddEntitiesToGroup = 12,
    MoveEntity = 15,
    SetPaintComponent = 16,
    CreateMaskGroup = 17,
    DeleteShape = 18,
    DeleteText = 19,
    DeleteBooleanGroup = 20,
    DeleteMaskGroup = 21,
    DeletePrefab = 22,
    CreatePrefabInstance = 23,
    DeletePrefabInstance = 24,
    ResizePrefabInstance = 27,
}

internal struct UndoRecord
{
    public UndoRecordKind Kind;

    // SetComponentSnapshot (byte payload in WorkspaceCommands list)
    public int ComponentSnapshotIndex;

    // MoveEntity (ECS hierarchy)
    public uint MoveEntityStableId;
    public uint MoveSourceParentStableId;
    public int MoveSourceIndex;
    public uint MoveTargetParentStableId;
    public int MoveTargetInsertIndex;
    public int MoveTargetIndexAfter;

    public byte HasTransformSnapshot;
    public TransformComponentHandle TransformHandle;
    public TransformComponent TransformBefore;
    public TransformComponent TransformAfter;

    // ResizeShape (Transform + RectGeometry/CircleGeometry)
    public byte ResizeShapeKind; // 1 = rect, 2 = circle
    public RectGeometryComponentHandle RectGeometryHandle;
    public RectGeometryComponent RectGeometryBefore;
    public RectGeometryComponent RectGeometryAfter;
    public CircleGeometryComponentHandle CircleGeometryHandle;
    public CircleGeometryComponent CircleGeometryBefore;
    public CircleGeometryComponent CircleGeometryAfter;

    // MovePrefab
    public int PrefabId;
    public Vector2 PrefabMoveDeltaWorld;

    // ResizePrefab
    public ImRect PrefabRectWorldBefore;
    public ImRect PrefabRectWorldAfter;

    // ResizePrefabInstance (Transform + PrefabCanvas)
    public PrefabCanvasComponentHandle PrefabCanvasHandle;
    public PrefabCanvasComponent PrefabCanvasBefore;
    public PrefabCanvasComponent PrefabCanvasAfter;
    public byte PrefabInstanceCanvasSizeIsOverriddenBefore;
    public byte PrefabInstanceCanvasSizeIsOverriddenAfter;

    // SetBooleanGroupOpIndex
    public int BooleanGroupId;
    public int BooleanGroupOpIndexBefore;
    public int BooleanGroupOpIndexAfter;

    // AddEntitiesToGroup (variable-length payload in WorkspaceCommands list)
    public uint AddToGroupGroupStableId;
    public uint AddToGroupParentStableId;
    public int AddToGroupDestStartIndex;
    public int AddToGroupMovedCount;
    public int PayloadStart;
    public int PayloadCount;

    // CreatePrefab
    public UiWorkspace.Prefab Prefab;
    public int PrefabListIndex;
    public uint PrefabEntityStableId;
    public int PrefabInsertIndex;

    // CreateShape
    public UiWorkspace.Shape Shape;
    public int ShapeListIndex;
    public uint ShapeEntityStableId;
    public uint ShapeParentStableId;
    public int ShapeInsertIndex;

    // CreateText
    public UiWorkspace.Text Text;
    public int TextListIndex;
    public uint TextEntityStableId;
    public uint TextParentStableId;
    public int TextInsertIndex;

    // CreateBooleanGroup
    public UiWorkspace.BooleanGroup Group;
    public int GroupListIndex;
    public uint GroupEntityStableId;
    public uint GroupParentStableId;
    public int GroupInsertIndex;

    // CreatePrefabInstance
    public uint PrefabInstanceEntityStableId;
    public uint PrefabInstanceParentStableId;
    public int PrefabInstanceInsertIndex;

    // SetProperty / SetPropertyBatch
    public byte PropertyCount;
    public PropertyKey Property0;
    public PropertyValue Before0;
    public PropertyValue After0;

    public PropertyKey Property1;
    public PropertyValue Before1;
    public PropertyValue After1;

    public PropertyKey Property2;
    public PropertyValue Before2;
    public PropertyValue After2;

    public PropertyKey Property3;
    public PropertyValue Before3;
    public PropertyValue After3;

    // SetFillComponent
    public FillComponentHandle FillHandle;
    public FillComponent FillBefore;
    public FillComponent FillAfter;

    // SetPaintComponent
    public PaintComponentHandle PaintHandle;
    public PaintComponent PaintBefore;
    public PaintComponent PaintAfter;

    public static UndoRecord ForProperty(PropertyKey key, PropertyValue before, PropertyValue after)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.SetProperty,
            PropertyCount = 1,
            Property0 = key,
            Before0 = before,
            After0 = after
        };
    }

    public static UndoRecord ForResizePrefabInstance(
        uint prefabInstanceEntityStableId,
        TransformComponentHandle transformHandle,
        in TransformComponent transformBefore,
        in TransformComponent transformAfter,
        PrefabCanvasComponentHandle canvasHandle,
        in PrefabCanvasComponent canvasBefore,
        in PrefabCanvasComponent canvasAfter,
        byte canvasSizeIsOverriddenBefore,
        byte canvasSizeIsOverriddenAfter)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.ResizePrefabInstance,
            PrefabInstanceEntityStableId = prefabInstanceEntityStableId,
            HasTransformSnapshot = 1,
            TransformHandle = transformHandle,
            TransformBefore = transformBefore,
            TransformAfter = transformAfter,
            PrefabCanvasHandle = canvasHandle,
            PrefabCanvasBefore = canvasBefore,
            PrefabCanvasAfter = canvasAfter,
            PrefabInstanceCanvasSizeIsOverriddenBefore = canvasSizeIsOverriddenBefore,
            PrefabInstanceCanvasSizeIsOverriddenAfter = canvasSizeIsOverriddenAfter
        };
    }

    public static UndoRecord ForPropertyMany(int payloadStart, int payloadCount)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.SetPropertyMany,
            PayloadStart = payloadStart,
            PayloadCount = payloadCount
        };
    }

    public static UndoRecord ForPropertyBatch(
        PropertyKey key0, PropertyValue before0, PropertyValue after0,
        PropertyKey key1, PropertyValue before1, PropertyValue after1,
        byte count)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.SetPropertyBatch,
            PropertyCount = count,
            Property0 = key0,
            Before0 = before0,
            After0 = after0,
            Property1 = key1,
            Before1 = before1,
            After1 = after1
        };
    }

    public static UndoRecord ForPropertyBatch(
        PropertyKey key0, PropertyValue before0, PropertyValue after0,
        PropertyKey key1, PropertyValue before1, PropertyValue after1,
        PropertyKey key2, PropertyValue before2, PropertyValue after2,
        byte count)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.SetPropertyBatch,
            PropertyCount = count,
            Property0 = key0,
            Before0 = before0,
            After0 = after0,
            Property1 = key1,
            Before1 = before1,
            After1 = after1,
            Property2 = key2,
            Before2 = before2,
            After2 = after2
        };
    }

    public static UndoRecord ForPropertyBatch(
        PropertyKey key0, PropertyValue before0, PropertyValue after0,
        PropertyKey key1, PropertyValue before1, PropertyValue after1,
        PropertyKey key2, PropertyValue before2, PropertyValue after2,
        PropertyKey key3, PropertyValue before3, PropertyValue after3,
        byte count)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.SetPropertyBatch,
            PropertyCount = count,
            Property0 = key0,
            Before0 = before0,
            After0 = after0,
            Property1 = key1,
            Before1 = before1,
            After1 = after1,
            Property2 = key2,
            Before2 = before2,
            After2 = after2,
            Property3 = key3,
            Before3 = before3,
            After3 = after3
        };
    }

    public static UndoRecord ForFill(FillComponentHandle handle, in FillComponent before, in FillComponent after)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.SetFillComponent,
            FillHandle = handle,
            FillBefore = before,
            FillAfter = after
        };
    }

    public static UndoRecord ForCreatePrefabInstance(uint entityStableId, uint parentStableId, int insertIndex)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.CreatePrefabInstance,
            PrefabInstanceEntityStableId = entityStableId,
            PrefabInstanceParentStableId = parentStableId,
            PrefabInstanceInsertIndex = insertIndex
        };
    }

    public static UndoRecord ForDeletePrefabInstance(uint entityStableId, uint parentStableId, int insertIndex)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.DeletePrefabInstance,
            PrefabInstanceEntityStableId = entityStableId,
            PrefabInstanceParentStableId = parentStableId,
            PrefabInstanceInsertIndex = insertIndex
        };
    }

    public static UndoRecord ForPaintComponent(PaintComponentHandle handle, in PaintComponent before, in PaintComponent after)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.SetPaintComponent,
            PaintHandle = handle,
            PaintBefore = before,
            PaintAfter = after
        };
    }

    public static UndoRecord ForComponentSnapshot(int payloadIndex)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.SetComponentSnapshot,
            ComponentSnapshotIndex = payloadIndex
        };
    }

    public static UndoRecord ForComponentSnapshotMany(int payloadStart, int payloadCount)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.SetComponentSnapshotMany,
            PayloadStart = payloadStart,
            PayloadCount = payloadCount
        };
    }

    public static UndoRecord ForMoveEntity(
        uint entityStableId,
        uint sourceParentStableId,
        int sourceIndex,
        uint targetParentStableId,
        int targetInsertIndex,
        int targetIndexAfter,
        TransformComponentHandle transformHandle,
        in TransformComponent transformBefore,
        in TransformComponent transformAfter,
        bool hasTransformSnapshot)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.MoveEntity,
            MoveEntityStableId = entityStableId,
            MoveSourceParentStableId = sourceParentStableId,
            MoveSourceIndex = sourceIndex,
            MoveTargetParentStableId = targetParentStableId,
            MoveTargetInsertIndex = targetInsertIndex,
            MoveTargetIndexAfter = targetIndexAfter,
            TransformHandle = transformHandle,
            TransformBefore = transformBefore,
            TransformAfter = transformAfter,
            HasTransformSnapshot = (byte)(hasTransformSnapshot ? 1 : 0)
        };
    }

    public static UndoRecord ForCreatePrefab(in UiWorkspace.Prefab prefab, int prefabListIndex, uint prefabEntityStableId, int insertIndex)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.CreatePrefab,
            Prefab = prefab,
            PrefabListIndex = prefabListIndex,
            PrefabEntityStableId = prefabEntityStableId,
            PrefabInsertIndex = insertIndex
        };
    }

    public static UndoRecord ForDeletePrefab(in UiWorkspace.Prefab prefab, int prefabListIndex, uint prefabEntityStableId, int insertIndex)
    {
        var record = ForCreatePrefab(prefab, prefabListIndex, prefabEntityStableId, insertIndex);
        record.Kind = UndoRecordKind.DeletePrefab;
        return record;
    }

    public static UndoRecord ForCreateText(in UiWorkspace.Text text, int textListIndex, uint textEntityStableId, uint parentStableId, int insertIndex)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.CreateText,
            Text = text,
            TextListIndex = textListIndex,
            TextEntityStableId = textEntityStableId,
            TextParentStableId = parentStableId,
            TextInsertIndex = insertIndex
        };
    }

    public static UndoRecord ForCreateShape(in UiWorkspace.Shape shape, int shapeListIndex, uint shapeEntityStableId, uint parentStableId, int insertIndex)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.CreateShape,
            Shape = shape,
            ShapeListIndex = shapeListIndex,
            ShapeEntityStableId = shapeEntityStableId,
            ShapeParentStableId = parentStableId,
            ShapeInsertIndex = insertIndex
        };
    }

    public static UndoRecord ForDeleteShape(in UiWorkspace.Shape shape, int shapeListIndex, uint shapeEntityStableId, uint parentStableId, int insertIndex)
    {
        var record = ForCreateShape(shape, shapeListIndex, shapeEntityStableId, parentStableId, insertIndex);
        record.Kind = UndoRecordKind.DeleteShape;
        return record;
    }

    public static UndoRecord ForCreateBooleanGroup(in UiWorkspace.BooleanGroup group, int groupListIndex, uint groupEntityStableId, uint parentStableId, int insertIndex, int payloadStart, int payloadCount)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.CreateBooleanGroup,
            Group = group,
            GroupListIndex = groupListIndex,
            GroupEntityStableId = groupEntityStableId,
            GroupParentStableId = parentStableId,
            GroupInsertIndex = insertIndex,
            PayloadStart = payloadStart,
            PayloadCount = payloadCount
        };
    }

    public static UndoRecord ForDeleteBooleanGroup(in UiWorkspace.BooleanGroup group, int groupListIndex, uint groupEntityStableId, uint parentStableId, int insertIndex)
    {
        var record = ForCreateBooleanGroup(group, groupListIndex, groupEntityStableId, parentStableId, insertIndex, payloadStart: 0, payloadCount: 0);
        record.Kind = UndoRecordKind.DeleteBooleanGroup;
        return record;
    }

    public static UndoRecord ForAddEntitiesToGroup(uint groupStableId, uint parentStableId, int destStartIndex, int movedCount, int payloadStart, int payloadCount)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.AddEntitiesToGroup,
            AddToGroupGroupStableId = groupStableId,
            AddToGroupParentStableId = parentStableId,
            AddToGroupDestStartIndex = destStartIndex,
            AddToGroupMovedCount = movedCount,
            PayloadStart = payloadStart,
            PayloadCount = payloadCount
        };
    }

    public static UndoRecord ForCreateMaskGroup(in UiWorkspace.BooleanGroup group, int groupListIndex, uint groupEntityStableId, uint parentStableId, int insertIndex, int payloadStart, int payloadCount)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.CreateMaskGroup,
            Group = group,
            GroupListIndex = groupListIndex,
            GroupEntityStableId = groupEntityStableId,
            GroupParentStableId = parentStableId,
            GroupInsertIndex = insertIndex,
            PayloadStart = payloadStart,
            PayloadCount = payloadCount
        };
    }

    public static UndoRecord ForDeleteMaskGroup(in UiWorkspace.BooleanGroup group, int groupListIndex, uint groupEntityStableId, uint parentStableId, int insertIndex)
    {
        var record = ForCreateMaskGroup(group, groupListIndex, groupEntityStableId, parentStableId, insertIndex, payloadStart: 0, payloadCount: 0);
        record.Kind = UndoRecordKind.DeleteMaskGroup;
        return record;
    }

    public static UndoRecord ForDeleteText(in UiWorkspace.Text text, int textListIndex, uint textEntityStableId, uint parentStableId, int insertIndex)
    {
        var record = ForCreateText(text, textListIndex, textEntityStableId, parentStableId, insertIndex);
        record.Kind = UndoRecordKind.DeleteText;
        return record;
    }

    public static UndoRecord ForTransformComponent(TransformComponentHandle handle, in TransformComponent before, in TransformComponent after)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.SetTransformComponent,
            TransformHandle = handle,
            TransformBefore = before,
            TransformAfter = after,
            HasTransformSnapshot = 1
        };
    }

    public static UndoRecord ForResizeRectShape(
        TransformComponentHandle transformHandle,
        in TransformComponent transformBefore,
        in TransformComponent transformAfter,
        RectGeometryComponentHandle geometryHandle,
        in RectGeometryComponent geometryBefore,
        in RectGeometryComponent geometryAfter)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.ResizeShape,
            ResizeShapeKind = 1,
            TransformHandle = transformHandle,
            TransformBefore = transformBefore,
            TransformAfter = transformAfter,
            HasTransformSnapshot = 1,
            RectGeometryHandle = geometryHandle,
            RectGeometryBefore = geometryBefore,
            RectGeometryAfter = geometryAfter
        };
    }

    public static UndoRecord ForResizeCircleShape(
        TransformComponentHandle transformHandle,
        in TransformComponent transformBefore,
        in TransformComponent transformAfter,
        CircleGeometryComponentHandle geometryHandle,
        in CircleGeometryComponent geometryBefore,
        in CircleGeometryComponent geometryAfter)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.ResizeShape,
            ResizeShapeKind = 2,
            TransformHandle = transformHandle,
            TransformBefore = transformBefore,
            TransformAfter = transformAfter,
            HasTransformSnapshot = 1,
            CircleGeometryHandle = geometryHandle,
            CircleGeometryBefore = geometryBefore,
            CircleGeometryAfter = geometryAfter
        };
    }

    public static UndoRecord ForMovePrefab(int prefabId, Vector2 deltaWorld)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.MovePrefab,
            PrefabId = prefabId,
            PrefabMoveDeltaWorld = deltaWorld
        };
    }

    public static UndoRecord ForResizePrefab(int prefabId, ImRect rectWorldBefore, ImRect rectWorldAfter)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.ResizePrefab,
            PrefabId = prefabId,
            PrefabRectWorldBefore = rectWorldBefore,
            PrefabRectWorldAfter = rectWorldAfter
        };
    }

    public static UndoRecord ForBooleanGroupOpIndex(int groupId, int before, int after)
    {
        return new UndoRecord
        {
            Kind = UndoRecordKind.SetBooleanGroupOpIndex,
            BooleanGroupId = groupId,
            BooleanGroupOpIndexBefore = before,
            BooleanGroupOpIndexAfter = after
        };
    }

}
