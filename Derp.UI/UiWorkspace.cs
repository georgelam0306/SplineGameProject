using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Core;
using Pooled.Runtime;
using Property;
using Property.Runtime;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Windows;
using DerpLib.ImGui.Widgets;
using DerpLib.Rendering;
using DerpLib.Sdf;
using static Derp.UI.UiColor32;
using static Derp.UI.UiFillGradient;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private static readonly StringHandle TransformGroupHandle = "Transform";
    private static readonly StringHandle TransformPositionNameHandle = "Position";
    private static readonly StringHandle TransformScaleNameHandle = "Scale";
    private static readonly StringHandle TransformRotationNameHandle = "Rotation";

    private static bool TryGetPropertySlotByGroupAndName(AnyComponentHandle component, StringHandle group, StringHandle name, PropertyKind kind, out PropertySlot slot)
    {
        slot = default;
        if (component.IsNull)
        {
            return false;
        }

        int propertyCount = PropertyDispatcher.GetPropertyCount(component);
        if (propertyCount <= 0)
        {
            return false;
        }

        for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
        {
            if (!PropertyDispatcher.TryGetInfo(component, propertyIndex, out var info))
            {
                continue;
            }

            if (info.Group != group || info.Name != name || info.Kind != kind)
            {
                continue;
            }

            slot = PropertyDispatcher.GetSlot(component, propertyIndex);
            return true;
        }

        return false;
    }

    private static bool TryGetTransformPropertySlot(TransformComponentHandle handle, StringHandle name, PropertyKind kind, out PropertySlot slot)
    {
        AnyComponentHandle component = TransformComponentProperties.ToAnyHandle(handle);
        return TryGetPropertySlotByGroupAndName(component, TransformGroupHandle, name, kind, out slot);
    }

    public enum GroupKind : byte
    {
        Boolean = 0,
        Mask = 1,
        Plain = 2,
    }

    public enum CanvasTool
    {
        Select = 0,
        Prefab = 1,
        Rect = 2,
        Circle = 3,
        Pen = 4,
        Text = 5,
        Instance = 6,
    }

    public struct Prefab
    {
        public int Id;
        public ImRect RectWorld;
        public TransformComponentHandle Transform;
        internal AnimationDocument Animations;
        internal uint AnimationsCacheRevision;
    }

    internal enum StateMachineInspectorSelectionKind : byte
    {
        None = 0,
        Node = 1,
        Transition = 2,
        MultiNode = 3
    }

    internal struct StateMachineInspectorSelection
    {
        public int StateMachineId;
        public int LayerId;
        public StateMachineInspectorSelectionKind Kind;
        public StateMachineGraphNodeRef Node;
        public int TransitionId;
        public int NodeCount;
    }

    public struct Shape
    {
        public int Id;
        public int ParentFrameId;
        public int ParentGroupId; // 0 = direct child of frame or shape
        public int ParentShapeId; // 0 = not a child of another shape
        public ShapeComponentHandle Component;
        public TransformComponentHandle Transform;
        public BlendComponentHandle Blend;
        public PaintComponentHandle Paint;
        public RectGeometryComponentHandle RectGeometry;
        public CircleGeometryComponentHandle CircleGeometry;
        public PathComponentHandle Path;
        public int PointStart;
        public int PointCount;
    }

    public struct BooleanGroup
    {
        public int Id;
        public int ParentFrameId;
        public int ParentGroupId; // 0 = direct child of frame or shape
        public int ParentShapeId; // 0 = not a child of a shape
        public GroupKind Kind;
        public int OpIndex;
        public BooleanGroupComponentHandle Component;
        public MaskGroupComponentHandle MaskComponent;
        public TransformComponentHandle Transform;
        public BlendComponentHandle Blend;
    }

    public struct Text
    {
        public int Id;
        public int ParentFrameId;
        public int ParentGroupId; // 0 = direct child of frame or shape
        public int ParentShapeId; // 0 = not a child of another shape
        public TextComponentHandle Component;
        public TransformComponentHandle Transform;
        public BlendComponentHandle Blend;
        public PaintComponentHandle Paint;
        public RectGeometryComponentHandle RectGeometry;
    }

    internal static readonly string[] BooleanOpOptions =
    [
        "Union",
        "Subtract",
        "Intersect",
        "Exclude",
    ];

    // Workspace state is shared across UI subsystems (toolbar/layers/inspector/canvas).
    // Internal to allow subsystem types to operate on state without partial classes.
    internal readonly List<Prefab> _prefabs = new(capacity: 64);
    internal int _nextPrefabId = 1;

    internal readonly List<int> _prefabIndexById = new(capacity: 128);

    internal readonly List<Shape> _shapes = new(capacity: 256);
    internal int _nextShapeId = 1;

	    internal readonly List<Vector2> _polygonPointsLocal = new(capacity: 4096);
	    internal Vector2[] _polygonCanvasScratch = new Vector2[128];
	    private Vector2[] _pathTessellationLocalScratch = new Vector2[512];

    internal readonly List<BooleanGroup> _groups = new(capacity: 64);
    internal int _nextGroupId = 1;

    internal readonly List<Text> _texts = new(capacity: 256);
    internal int _nextTextId = 1;
    private readonly List<TextLayoutCache?> _textLayoutByTextComponentIndex = new(capacity: 256);
    private readonly List<int> _textLayoutGenerationByTextComponentIndex = new(capacity: 256);

    // ID -> index maps (index 0 unused since ids start at 1). Used to keep hierarchy rendering O(1) without dictionaries.
    internal readonly List<int> _shapeIndexById = new(capacity: 512);
    internal readonly List<int> _groupIndexById = new(capacity: 128);
    internal readonly List<int> _textIndexById = new(capacity: 256);
    internal readonly List<byte> _layersExpandedByPrefabId = new(capacity: 128);
    internal readonly List<byte> _layersExpandedByGroupId = new(capacity: 256);
    internal readonly List<byte> _layersExpandedByShapeId = new(capacity: 512);
    internal readonly List<byte> _layersExpandedByTextId = new(capacity: 256);
    // Editor-only layer metadata (by stable id). This is document state, not entity state.
    internal readonly UiEditorLayersDocument _editorLayersDocument = new();

    // Legacy id -> EntityId tables (index 0 unused since ids start at 1).
    internal readonly List<EntityId> _prefabEntityById = new(capacity: 128);
    internal readonly List<EntityId> _shapeEntityById = new(capacity: 512);
    internal readonly List<EntityId> _groupEntityById = new(capacity: 256);
    internal readonly List<EntityId> _textEntityById = new(capacity: 256);

    // Selection.
    internal int _selectedPrefabIndex = -1;
    internal readonly List<EntityId> _selectedEntities = new(capacity: 32);
    internal EntityId _selectedPrefabEntity;

    // Tool state
    public CanvasTool ActiveTool = CanvasTool.Select;

    // Toasts
    private string _toastMessage = string.Empty;
    private int _toastEndFrame;
    private int _toastStartFrame;

    // Canvas camera
    public Vector2 PanWorld;
    public float Zoom = 1f;

    private bool _hierarchyTransformsAreLocal;

    private int _lastCanvasTargetWidth;
    private int _lastCanvasTargetHeight;
    private bool _hasLastCanvasSurfaceSize;

    private EntityId _animationTargetOverrideEntity;
    private int _animationTargetOverrideFrame;

    internal void SetAnimationTargetOverride(EntityId entity)
    {
        _animationTargetOverrideEntity = entity;
        _animationTargetOverrideFrame = Im.Context.FrameCount;
    }

    internal bool TryGetAnimationTargetOverride(out EntityId entity)
    {
        entity = EntityId.Null;
        if (_animationTargetOverrideFrame != Im.Context.FrameCount)
        {
            return false;
        }

        entity = _animationTargetOverrideEntity;
        return !entity.IsNull;
    }

    internal CanvasInteractions CanvasInteractions => _canvasInteractions;
    private readonly CanvasInteractions _canvasInteractions;

    // Dragging
    private enum DragKind
    {
        None = 0,
        Prefab = 1,
        ResizePrefab = 12,
        Shape = 2,
        Group = 3,
        ResizeShape = 4,
        ResizeGroup = 5,
        RotateGroup = 6,
        RotateShape = 7,
        Text = 8,
        ResizeText = 9,
        RotateText = 10,
        Node = 11,
        ResizeNode = 13,
    }

    private enum PathEditDragKind : byte
    {
        None = 0,
        Vertex = 1,
        TangentIn = 2,
        TangentOut = 3
    }

    private enum GradientGizmoDragKind : byte
    {
        None = 0,
        Center = 1,
        LinearDirection = 2,
        RadialRadius = 3,
        AngularAngle = 4
    }

    private struct GradientGizmoDragState
    {
        public byte Active;
        public GradientGizmoDragKind Kind;
        public EntityId TargetEntity;
        public PaintComponentHandle PaintHandle;
        public int LayerIndex;
        public int WidgetId;
        public Vector2 ShapeCenterCanvas;
        public Vector2 ShapeSizeCanvas;
        public float RotationRadians;
    }

    private GradientGizmoDragState _gradientGizmoDrag;

    private enum LayoutConstraintGizmoDragKind : byte
    {
        None = 0,
        AnchorMin = 1,
        AnchorMax = 2,
    }

    private struct LayoutConstraintGizmoDragState
    {
        public byte Active;
        public LayoutConstraintGizmoDragKind Kind;
        public EntityId TargetEntity;
        public EntityId ParentEntity;
        public int WidgetId;

        public AnyComponentHandle TransformAny;
        public PropertySlot AnchorMinSlot;
        public PropertySlot AnchorMaxSlot;
        public PropertySlot OffsetMinSlot;
        public PropertySlot OffsetMaxSlot;

        public Vector2 ParentMinLocal;
        public Vector2 ParentSizeLocal;

        public Vector2 StartAnchorMin;
        public Vector2 StartAnchorMax;
        public Vector2 StartOffsetMin;
        public Vector2 StartOffsetMax;
        public bool StartIsStretch;

        public Vector2 StartRectMinLocal;
        public Vector2 StartRectMaxLocal;
        public Vector2 StartPivotPosLocal;
    }

    private LayoutConstraintGizmoDragState _layoutConstraintGizmoDrag;

    private CanvasSurface? _canvasSurface;
    internal bool _hasCanvasCompositeThisFrame;
    internal ImRect _canvasCompositeRectViewport;

    public void SetCanvasSurface(CanvasSurface canvasSurface)
    {
        _canvasSurface = canvasSurface;
    }

    public void BeginFrame()
    {
        EnsureComputedLayoutComponentsForNewEntities();

        ClearPropertyGizmosForFrame();
        UpdatePropertyGizmosFromInspectorState();
        _hasCanvasCompositeThisFrame = false;
        _canvasCompositeRectViewport = ImRect.Zero;

        if (_selectedPrefabIndex < 0 && _prefabs.Count > 0)
        {
            _selectedPrefabIndex = 0;
        }

        if (_selectedPrefabEntity.IsNull && _selectedPrefabIndex >= 0 && _selectedPrefabIndex < _prefabs.Count)
        {
            int prefabId = _prefabs[_selectedPrefabIndex].Id;
            TryGetEntityById(_prefabEntityById, prefabId, out _selectedPrefabEntity);
        }

        UpdatePrefabInstances();
        ApplySelectedPrefabDefinitionBindings();
    }

    public bool TryGetCanvasComposite(out ImRect rectScreen, out Texture texture)
    {
        rectScreen = ImRect.Zero;
        texture = default;

        if (!_hasCanvasCompositeThisFrame || _canvasSurface == null)
        {
            return false;
        }

        rectScreen = _canvasCompositeRectViewport;
        texture = _canvasSurface.Texture;
        return true;
    }

    private DragKind _dragKind;
    private int _dragPrefabIndex = -1;
    private int _dragShapeIndex = -1;
    private int _dragGroupIndex = -1;
    private int _dragTextIndex = -1;
    private EntityId _dragEntity;
    private Vector2 _dragLastMouseWorld;
    private Vector2 _lastCanvasMouseWorld;
    private bool _hasLastCanvasMouseWorld;
    private bool _isPrefabTransformSelectedOnCanvas;

    internal bool TryGetCanvasMouseWorld(out Vector2 world)
    {
        world = _lastCanvasMouseWorld;
        return _hasLastCanvasMouseWorld;
    }

    private enum MarqueeState : byte
    {
        None = 0,
        Pending = 1,
        Active = 2
    }

    private enum SelectionModifyMode : byte
    {
        Replace = 0,
        Add = 1,
        Subtract = 2,
        Toggle = 3
    }

    private MarqueeState _marqueeState;
    private SelectionModifyMode _marqueeModifyMode;
    private Vector2 _marqueeStartCanvas;
    private Vector2 _marqueeCurrentCanvas;
    private int _marqueePrefabId;
    private EntityId _marqueePrefabEntity;
    private readonly List<EntityId> _marqueeHits = new(capacity: 256);

    private struct CanvasEditCapture
    {
        public DragKind Kind;
        public int PrefabId;
        public ImRect PrefabRectWorldBefore;

        public TransformComponentHandle TransformHandle;
        public TransformComponent TransformBefore;
        public byte HasTransform;

        public RectGeometryComponentHandle RectGeometryHandle;
        public RectGeometryComponent RectGeometryBefore;
        public byte HasRectGeometry;

        public CircleGeometryComponentHandle CircleGeometryHandle;
        public CircleGeometryComponent CircleGeometryBefore;
        public byte HasCircleGeometry;

        public PrefabCanvasComponentHandle PrefabCanvasHandle;
        public PrefabCanvasComponent PrefabCanvasBefore;
        public byte HasPrefabCanvas;

        public byte Active;
    }

    private CanvasEditCapture _canvasEditCapture;

    private bool _isEditingPath;
    private EntityId _editingPathEntity;
    private PathEditDragKind _pathEditDragKind;
    private int _pathEditDragVertexIndex;
    private int _pathEditDragWidgetId;
    private int _pathEditVertexCount;

    private bool _pathEditHasSnapshot;
    private byte _pathEditSnapshotVertexCount;
    private Vector2 _pathEditSnapshotPivotLocal;
    private readonly Vector2[] _pathEditSnapshotPositionLocal = new Vector2[PathComponent.MaxVertices];
    private readonly Vector2[] _pathEditSnapshotTangentInLocal = new Vector2[PathComponent.MaxVertices];
    private readonly Vector2[] _pathEditSnapshotTangentOutLocal = new Vector2[PathComponent.MaxVertices];
    private readonly int[] _pathEditSnapshotVertexKind = new int[PathComponent.MaxVertices];

    private enum ShapeHandle
    {
        None = 0,
        TopLeft = 1,
        Top = 2,
        TopRight = 3,
        Right = 4,
        BottomRight = 5,
        Bottom = 6,
        BottomLeft = 7,
        Left = 8,
    }

    private ShapeHandle _resizeHandle;
    private ImRect _resizeStartRectWorld;
    private Vector2 _resizeStartMouseWorld;
    private Vector2 _resizeStartMouseLocal;
    private Vector2 _resizeAnchorWorld;
    private float _resizeRotationRadians;

    private bool _resizeGroupUsesRectSize;
    private ImRect _resizeGroupStartBoundsLocal;
    private ImRect _resizeGroupStartRectParentLocal;
    private Vector2 _resizeGroupStartMouseLocal;
    private Vector2 _resizeGroupStartScale;
    private Vector2 _resizeGroupPivotWorld;
    private float _resizeGroupRotationRadians;
    private Vector2 _resizeGroupAnchorParentLocal;

    private float _rotateGroupStartRotationDegrees;
    private float _rotateGroupStartMouseAngleRadians;
    private Vector2 _rotateGroupPivotWorld;

    private float _rotateShapeStartRotationDegrees;
    private float _rotateShapeStartMouseAngleRadians;
    private Vector2 _rotateShapePivotWorld;

    private float _rotateTextStartRotationDegrees;
    private float _rotateTextStartMouseAngleRadians;
    private Vector2 _rotateTextPivotWorld;

    // Inspector state
    internal int _pendingBooleanOpIndex;
    private StateMachineInspectorSelection _stateMachineInspectorSelection;

    private readonly World _propertyWorld = new();
    internal World PropertyWorld => _propertyWorld;
    private readonly PropertyInspector _propertyInspector;
    internal PropertyInspector PropertyInspector => _propertyInspector;
    internal readonly WorkspaceCommands Commands;

    private readonly UiWorld _world;
    internal UiWorld World => _world;

    internal void SetComponentWithStableId(EntityId entity, AnyComponentHandle component)
    {
        _world.SetComponent(entity, component);

        uint stableId = _world.GetStableId(entity);
        if (stableId == 0)
        {
            return;
        }

        UiComponentClipboardPacker.TryMapStableId(_propertyWorld, component, stableId);
    }

    internal UiWorkspace(UiWorld world)
    {
        _world = world;
        _userPreferences = UiEditorUserPreferencesFile.Read();
        _userPreferences.ClampInPlace();
        PropertyDispatcher.RegisterAll();
        _canvasInteractions = new CanvasInteractions(this);
        Commands = new WorkspaceCommands(this);
        _propertyInspector = new PropertyInspector(_propertyWorld, this, Commands);

        // Index 0 is unused (ids start at 1).
        _prefabEntityById.Add(EntityId.Null);
        _shapeEntityById.Add(EntityId.Null);
        _groupEntityById.Add(EntityId.Null);

        _ = _editorLayersDocument;
    }

    internal bool HasActivePrefab => _selectedPrefabIndex >= 0 && _selectedPrefabIndex < _prefabs.Count;

    internal bool TryGetActivePrefabId(out int prefabId)
    {
        prefabId = 0;
        if (_selectedPrefabIndex < 0 || _selectedPrefabIndex >= _prefabs.Count)
        {
            return false;
        }

        prefabId = _prefabs[_selectedPrefabIndex].Id;
        return prefabId > 0;
    }

    internal bool TryGetActivePrefabEntity(out EntityId prefabEntity)
    {
        prefabEntity = _selectedPrefabEntity;
        return !prefabEntity.IsNull;
    }

    internal bool TryGetActiveAnimationDocument(out AnimationDocument animations)
    {
        animations = null!;
        if (_selectedPrefabIndex < 0 || _selectedPrefabIndex >= _prefabs.Count)
        {
            return false;
        }

        if (!TryGetActivePrefabEntity(out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            return false;
        }

        if (!_world.TryGetComponent(prefabEntity, AnimationLibraryComponent.Api.PoolIdConst, out var any) || !any.IsValid)
        {
            if (!Commands.TryEnsureAnimationLibraryComponent(prefabEntity, out _))
            {
                return false;
            }

            if (!_world.TryGetComponent(prefabEntity, AnimationLibraryComponent.Api.PoolIdConst, out any) || !any.IsValid)
            {
                return false;
            }
        }

        var handle = new AnimationLibraryComponentHandle(any.Index, any.Generation);
        var lib = AnimationLibraryComponent.Api.FromHandle(_propertyWorld, handle);
        if (!lib.IsAlive)
        {
            return false;
        }

        Prefab prefab = _prefabs[_selectedPrefabIndex];
        bool prefabDirty = false;
        if (prefab.Animations == null)
        {
            prefab.Animations = new AnimationDocument();
            prefab.AnimationsCacheRevision = 0;
            prefabDirty = true;
        }

        if (prefab.AnimationsCacheRevision != lib.Revision)
        {
            AnimationLibraryOps.ImportToDocument(lib, prefab.Animations);
            prefab.AnimationsCacheRevision = lib.Revision;
            prefabDirty = true;
        }

        if (prefabDirty)
        {
            _prefabs[_selectedPrefabIndex] = prefab;
        }

        animations = prefab.Animations;
        return true;
    }

    internal void SyncActiveAnimationDocumentToLibrary()
    {
        if (!TryGetActiveAnimationDocument(out AnimationDocument animations) || animations == null)
        {
            return;
        }

        if (_selectedPrefabIndex < 0 || _selectedPrefabIndex >= _prefabs.Count)
        {
            return;
        }

        if (!TryGetActivePrefabEntity(out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            return;
        }

        if (!_world.TryGetComponent(prefabEntity, AnimationLibraryComponent.Api.PoolIdConst, out var any) || !any.IsValid)
        {
            return;
        }

        var handle = new AnimationLibraryComponentHandle(any.Index, any.Generation);
        var lib = AnimationLibraryComponent.Api.FromHandle(_propertyWorld, handle);
        if (!lib.IsAlive)
        {
            return;
        }

        uint newRevision = AnimationLibraryOps.ExportFromDocument(animations, lib);

        Prefab prefab = _prefabs[_selectedPrefabIndex];
        prefab.AnimationsCacheRevision = newRevision;
        _prefabs[_selectedPrefabIndex] = prefab;
    }

    internal bool TryGetActiveStateMachineDefinition(out StateMachineDefinitionComponent.ViewProxy definition)
    {
        definition = default;
        if (_selectedPrefabIndex < 0 || _selectedPrefabIndex >= _prefabs.Count)
        {
            return false;
        }

        int prefabId = _prefabs[_selectedPrefabIndex].Id;
        if (!TryGetEntityById(_prefabEntityById, prefabId, out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            return false;
        }

        if (!_world.TryGetComponent(prefabEntity, StateMachineDefinitionComponent.Api.PoolIdConst, out var any) || !any.IsValid)
        {
            return false;
        }

        var handle = new StateMachineDefinitionComponentHandle(any.Index, any.Generation);
        var view = StateMachineDefinitionComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return false;
        }

        definition = view;
        return true;
    }

    internal bool TryGetStateMachineInspectorSelection(out StateMachineInspectorSelection selection)
    {
        selection = _stateMachineInspectorSelection;
        return selection.Kind != StateMachineInspectorSelectionKind.None;
    }

    internal void ClearStateMachineInspectorSelection()
    {
        _stateMachineInspectorSelection = default;
    }

    internal void SetStateMachineInspectorNodeSelection(int stateMachineId, int layerId, StateMachineGraphNodeRef node)
    {
        _stateMachineInspectorSelection = new StateMachineInspectorSelection
        {
            StateMachineId = stateMachineId,
            LayerId = layerId,
            Kind = StateMachineInspectorSelectionKind.Node,
            Node = node,
            TransitionId = 0,
            NodeCount = 1
        };
    }

    internal void SetStateMachineInspectorTransitionSelection(int stateMachineId, int layerId, int transitionId)
    {
        _stateMachineInspectorSelection = new StateMachineInspectorSelection
        {
            StateMachineId = stateMachineId,
            LayerId = layerId,
            Kind = StateMachineInspectorSelectionKind.Transition,
            Node = default,
            TransitionId = transitionId,
            NodeCount = 0
        };
    }

    internal void SetStateMachineInspectorMultiNodeSelection(int stateMachineId, int layerId, int count)
    {
        _stateMachineInspectorSelection = new StateMachineInspectorSelection
        {
            StateMachineId = stateMachineId,
            LayerId = layerId,
            Kind = StateMachineInspectorSelectionKind.MultiNode,
            Node = default,
            TransitionId = 0,
            NodeCount = count
        };
    }

}
