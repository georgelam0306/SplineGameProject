using System;
using System.Numerics;
using Core;
using DerpLib.ImGui.Core;
using DerpLib.Text;
using Pooled.Runtime;
using Property;
using Property.Runtime;

namespace Derp.UI;

public sealed class UiRuntime
{
    private readonly UiWorld _world;
    private readonly UiWorkspace _workspace;

    private CompiledUi? _compiledUi;
    private EntityId[] _entitiesByNodeIndex = Array.Empty<EntityId>();
    private EntityId _activePrefab;
    private uint _activePrefabStableId;
    private bool _frameInputSetThisFrame;

    public int PrefabCount => _compiledUi?.Prefabs.Length ?? 0;
    public uint ActivePrefabStableId => _activePrefabStableId;

    public UiRuntimeInput Input => _workspace.RuntimeInput;

    public UiRuntime()
    {
        _world = new UiWorld();
        _workspace = new UiWorkspace(_world);

        // Runtime defaults: world units map 1:1 to canvas pixels.
        _workspace.PanWorld = Vector2.Zero;
        _workspace.Zoom = 1f;
    }

    public void SetFont(Font font)
    {
        if (font is null)
        {
            throw new ArgumentNullException(nameof(font));
        }

        _workspace.SetTextRenderFont(font);
    }

    public void Load(CompiledUi compiledUi)
    {
        if (compiledUi is null)
        {
            throw new ArgumentNullException(nameof(compiledUi));
        }

        _workspace.ResetDocumentForLoad();

        _compiledUi = compiledUi;
        CompiledUiInstantiator.InstantiateWorkspace(_workspace, compiledUi, out _entitiesByNodeIndex);

        // Prefab root transforms are editor placement (prefabs are often centered around world origin).
        // Runtime expects a top-left canvas coordinate space (0,0 at the canvas top-left), so normalize
        // prefab roots to position (0,0) at load time.
        NormalizePrefabRootTransforms();

        _workspace.PreparePrefabInstancesForExport();

        _workspace.PrepareForRuntimeAfterInstantiate();
        _workspace.ResolveLayout();

        _activePrefab = GetDefaultPrefabEntity(compiledUi, _entitiesByNodeIndex, out _activePrefabStableId);
        _frameInputSetThisFrame = false;
        _workspace.ResetStandaloneRuntimeEventState();
    }

    private void NormalizePrefabRootTransforms()
    {
        ReadOnlySpan<EntityId> rootChildren = _world.GetChildren(EntityId.Null);
        for (int i = 0; i < rootChildren.Length; i++)
        {
            EntityId entity = rootChildren[i];
            if (_world.GetNodeType(entity) != UiNodeType.Prefab)
            {
                continue;
            }

            if (!_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) || !transformAny.IsValid)
            {
                continue;
            }

            var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
            var transform = TransformComponent.Api.FromHandle(_workspace.PropertyWorld, transformHandle);
            if (!transform.IsAlive)
            {
                continue;
            }

            transform.Position = Vector2.Zero;
        }
    }

    public bool TryGetPrefabInfo(int prefabIndex, out uint stableId, out string name)
    {
        stableId = 0;
        name = string.Empty;

        CompiledUi? ui = _compiledUi;
        if (ui is null)
        {
            return false;
        }

        if ((uint)prefabIndex >= (uint)ui.Prefabs.Length)
        {
            return false;
        }

        var prefab = ui.Prefabs[prefabIndex];
        stableId = prefab.StableId;

        int nameIndex = prefab.NameStringIndex;
        if ((uint)nameIndex < (uint)ui.Strings.Length)
        {
            name = ui.Strings[nameIndex] ?? string.Empty;
        }

        return true;
    }

    public bool TrySetActivePrefabByIndex(int prefabIndex)
    {
        CompiledUi? ui = _compiledUi;
        if (ui is null)
        {
            return false;
        }

        if ((uint)prefabIndex >= (uint)ui.Prefabs.Length)
        {
            return false;
        }

        int rootNodeIndex = ui.Prefabs[prefabIndex].RootNodeIndex;
        if ((uint)rootNodeIndex >= (uint)_entitiesByNodeIndex.Length)
        {
            return false;
        }

        EntityId entity = _entitiesByNodeIndex[rootNodeIndex];
        if (entity.IsNull || _workspace.World.GetNodeType(entity) != UiNodeType.Prefab)
        {
            return false;
        }

        uint stableId = _workspace.World.GetStableId(entity);
        if (stableId != 0 && stableId == _activePrefabStableId)
        {
            return true;
        }

        _activePrefab = entity;
        _activePrefabStableId = stableId;
        _workspace.ResetStandaloneRuntimeEventState();
        return true;
    }

    public bool TrySetActivePrefabByStableId(uint stableId)
    {
        if (stableId == 0)
        {
            return false;
        }

        if (stableId == _activePrefabStableId)
        {
            return true;
        }

        EntityId entity = _workspace.World.GetEntityByStableId(stableId);
        if (entity.IsNull || _workspace.World.GetNodeType(entity) != UiNodeType.Prefab)
        {
            return false;
        }

        _activePrefab = entity;
        _activePrefabStableId = stableId;
        _workspace.ResetStandaloneRuntimeEventState();
        return true;
    }

    public bool TrySetActivePrefabByName(StringHandle prefabName)
    {
        if (!prefabName.IsValid)
        {
            return false;
        }

        string targetName = (string)prefabName;
        if (string.IsNullOrEmpty(targetName))
        {
            return false;
        }

        ReadOnlySpan<EntityId> rootChildren = _world.GetChildren(EntityId.Null);
        for (int i = 0; i < rootChildren.Length; i++)
        {
            EntityId entity = rootChildren[i];
            if (_world.GetNodeType(entity) != UiNodeType.Prefab)
            {
                continue;
            }

            uint stableId = _world.GetStableId(entity);
            if (stableId == 0)
            {
                continue;
            }

            if (_workspace.TryGetLayerName(stableId, out string name) && string.Equals(name, targetName, StringComparison.Ordinal))
            {
                return TrySetActivePrefabByStableId(stableId);
            }
        }

        return false;
    }

    public bool TrySetActivePrefabCanvasSize(int width, int height, bool resolveLayout = true)
    {
        if (_activePrefab.IsNull)
        {
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        bool ok = _workspace.TrySetCanvasSizeForEntityRuntime(_activePrefab, new Vector2(width, height));
        if (ok && resolveLayout)
        {
            _workspace.ResolveLayout();
        }

        return ok;
    }

    public bool TryGetActivePrefabCanvasSize(out Vector2 canvasSize)
    {
        canvasSize = Vector2.Zero;
        if (!TryGetActivePrefabCanvasBoundsWorld(out ImRect boundsWorld))
        {
            return false;
        }

        canvasSize = new Vector2(boundsWorld.Width, boundsWorld.Height);
        return canvasSize.X > 0f && canvasSize.Y > 0f;
    }

    public bool TryAutoFitActivePrefabToCanvas(
        int canvasWidth,
        int canvasHeight,
        float paddingFraction = 0.08f,
        float minZoom = 0.01f,
        float maxZoom = 8f)
    {
        if (_activePrefab.IsNull)
        {
            return false;
        }

        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return false;
        }

        if (minZoom <= 0f || !float.IsFinite(minZoom))
        {
            return false;
        }

        if (maxZoom <= 0f || !float.IsFinite(maxZoom))
        {
            return false;
        }

        if (maxZoom < minZoom)
        {
            return false;
        }

        if (!float.IsFinite(paddingFraction))
        {
            return false;
        }

        float clampedPaddingFraction = Math.Clamp(paddingFraction, 0f, 0.45f);
        if (!TryGetActivePrefabCanvasBoundsWorld(out ImRect boundsWorld))
        {
            ReadOnlySpan<EntityId> prefabChildren = _world.GetChildren(_activePrefab);
            if (prefabChildren.Length <= 0)
            {
                return false;
            }

            if (!_workspace.TryComputeSelectionBoundsWorldEcs(_activePrefab, prefabChildren, out boundsWorld))
            {
                return false;
            }
        }

        float boundsWidthWorld = MathF.Max(boundsWorld.Width, 1f);
        float boundsHeightWorld = MathF.Max(boundsWorld.Height, 1f);

        float availableWidthCanvas = MathF.Max(1f, canvasWidth * (1f - clampedPaddingFraction * 2f));
        float availableHeightCanvas = MathF.Max(1f, canvasHeight * (1f - clampedPaddingFraction * 2f));

        float fitZoomX = availableWidthCanvas / boundsWidthWorld;
        float fitZoomY = availableHeightCanvas / boundsHeightWorld;
        float fitZoom = MathF.Min(fitZoomX, fitZoomY);
        if (!float.IsFinite(fitZoom) || fitZoom <= 0f)
        {
            return false;
        }

        float zoom = Math.Clamp(fitZoom, minZoom, maxZoom);
        float boundsCenterXWorld = boundsWorld.X + boundsWorld.Width * 0.5f;
        float boundsCenterYWorld = boundsWorld.Y + boundsWorld.Height * 0.5f;
        float halfCanvasWidthWorld = canvasWidth * 0.5f / zoom;
        float halfCanvasHeightWorld = canvasHeight * 0.5f / zoom;

        _workspace.Zoom = zoom;
        _workspace.PanWorld = new Vector2(
            boundsCenterXWorld - halfCanvasWidthWorld,
            boundsCenterYWorld - halfCanvasHeightWorld);
        return true;
    }

    private bool TryGetActivePrefabCanvasBoundsWorld(out ImRect boundsWorld)
    {
        boundsWorld = ImRect.Zero;

        if (_activePrefab.IsNull)
        {
            return false;
        }

        if (!_world.TryGetComponent(_activePrefab, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny) || !canvasAny.IsValid)
        {
            return false;
        }

        var canvasHandle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
        var canvas = PrefabCanvasComponent.Api.FromHandle(_workspace.PropertyWorld, canvasHandle);
        if (!canvas.IsAlive || canvas.Size.X <= 0f || canvas.Size.Y <= 0f)
        {
            return false;
        }

        Vector2 positionWorld = Vector2.Zero;
        if (_world.TryGetComponent(_activePrefab, ComputedTransformComponent.Api.PoolIdConst, out AnyComponentHandle computedTransformAny) &&
            computedTransformAny.IsValid)
        {
            var computedTransformHandle = new ComputedTransformComponentHandle(computedTransformAny.Index, computedTransformAny.Generation);
            var computedTransform = ComputedTransformComponent.Api.FromHandle(_workspace.PropertyWorld, computedTransformHandle);
            if (computedTransform.IsAlive)
            {
                positionWorld = computedTransform.Position;
            }
        }
        else if (_world.TryGetComponent(_activePrefab, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) &&
                 transformAny.IsValid)
        {
            var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
            var transform = TransformComponent.Api.FromHandle(_workspace.PropertyWorld, transformHandle);
            if (transform.IsAlive)
            {
                positionWorld = transform.Position;
            }
        }

        boundsWorld = new ImRect(positionWorld.X, positionWorld.Y, canvas.Size.X, canvas.Size.Y);
        return true;
    }

    public bool TrySetPrefabInstanceCanvasSize(uint instanceStableId, int width, int height, bool markOverridden = true, bool resolveLayout = true)
    {
        if (instanceStableId == 0)
        {
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        EntityId entity = _workspace.World.GetEntityByStableId(instanceStableId);
        if (entity.IsNull)
        {
            return false;
        }

        bool ok = _workspace.TrySetPrefabInstanceCanvasSizeRuntime(entity, new Vector2(width, height), markOverridden);
        if (ok && resolveLayout)
        {
            _workspace.ResolveLayout();
        }

        return ok;
    }

    public bool TryResolvePrefabVariableId(uint prefabStableId, ReadOnlySpan<char> name, out ushort variableId, out PropertyKind kind)
    {
        return _workspace.TryResolvePrefabVariableId(prefabStableId, name, out variableId, out kind);
    }

    public bool TryGetStateMachineRuntimeDebug(uint ownerStableId, out UiRuntimeStateMachineDebug debug)
    {
        debug = default;

        if (ownerStableId == 0)
        {
            return false;
        }

        EntityId entity = _workspace.World.GetEntityByStableId(ownerStableId);
        if (entity.IsNull)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(entity, StateMachineRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle any) || !any.IsValid)
        {
            return false;
        }

        var handle = new StateMachineRuntimeComponentHandle(any.Index, any.Generation);
        var runtime = StateMachineRuntimeComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!runtime.IsAlive)
        {
            return false;
        }

        const int layerIndex = 0;
        debug = new UiRuntimeStateMachineDebug(
            runtime.ActiveMachineId,
            runtime.IsInitialized,
            runtime.DebugActiveLayerId,
            runtime.DebugActiveStateId,
            runtime.DebugLastTransitionId,
            runtime.LayerCurrentStateId[layerIndex],
            runtime.LayerPreviousStateId[layerIndex],
            runtime.LayerStateTimeUs[layerIndex],
            runtime.LayerTransitionId[layerIndex],
            runtime.LayerTransitionFromStateId[layerIndex],
            runtime.LayerTransitionToStateId[layerIndex],
            runtime.LayerTransitionTimeUs[layerIndex],
            runtime.LayerTransitionDurationUs[layerIndex]);
        return true;
    }

    public bool TryGetActivePrefabStateMachineRuntimeDebug(out UiRuntimeStateMachineDebug debug)
    {
        if (_activePrefabStableId == 0)
        {
            debug = default;
            return false;
        }

        return TryGetStateMachineRuntimeDebug(_activePrefabStableId, out debug);
    }

    public bool TryGetPrefabVariableValue(uint prefabStableId, ushort variableId, out UiRuntimeVariableDebug debug)
    {
        debug = default;

        if (prefabStableId == 0 || variableId == 0)
        {
            return false;
        }

        EntityId prefabEntity = _workspace.World.GetEntityByStableId(prefabStableId);
        if (prefabEntity.IsNull || _workspace.World.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(prefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return false;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive)
        {
            return false;
        }

        ushort count = vars.VariableCount;
        if (count > PrefabVariablesComponent.MaxVariables)
        {
            count = PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();

        int schemaIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == variableId)
            {
                schemaIndex = i;
                break;
            }
        }

        if (schemaIndex < 0)
        {
            return false;
        }

        var kind = (PropertyKind)kinds[schemaIndex];
        PropertyValue value = defaults[schemaIndex];

        if (_workspace.World.TryGetComponent(prefabEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) && instanceAny.IsValid)
        {
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
            if (instance.IsAlive && instance.ValueCount != 0)
            {
                ushort instanceCount = instance.ValueCount;
                if (instanceCount > PrefabInstanceComponent.MaxVariables)
                {
                    instanceCount = PrefabInstanceComponent.MaxVariables;
                }

                ReadOnlySpan<ushort> instanceIds = instance.VariableIdReadOnlySpan();
                ReadOnlySpan<PropertyValue> instanceValues = instance.ValueReadOnlySpan();

                int instanceIndex = -1;
                for (int i = 0; i < instanceCount; i++)
                {
                    if (instanceIds[i] == variableId)
                    {
                        instanceIndex = i;
                        break;
                    }
                }

                if (instanceIndex >= 0)
                {
                    bool isOverridden = (instance.OverrideMask & (1UL << instanceIndex)) != 0;
                    if (isOverridden)
                    {
                        value = instanceValues[instanceIndex];
                    }
                }
            }
        }

        debug = new UiRuntimeVariableDebug(kind, in value);
        return true;
    }

    public bool TryGetPrefabVariableValueDetailed(uint prefabStableId, ushort variableId, out PropertyKind kind, out PropertyValue value, out bool overridden, out StringHandle name)
    {
        kind = default;
        value = default;
        overridden = false;
        name = default;

        if (prefabStableId == 0 || variableId == 0)
        {
            return false;
        }

        EntityId prefabEntity = _workspace.World.GetEntityByStableId(prefabStableId);
        if (prefabEntity.IsNull || _workspace.World.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(prefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return false;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive)
        {
            return false;
        }

        ushort count = vars.VariableCount;
        if (count > PrefabVariablesComponent.MaxVariables)
        {
            count = PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();
        ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();

        int schemaIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == variableId)
            {
                schemaIndex = i;
                break;
            }
        }

        if (schemaIndex < 0)
        {
            return false;
        }

        kind = (PropertyKind)kinds[schemaIndex];
        value = defaults[schemaIndex];
        name = names[schemaIndex];

        if (_workspace.World.TryGetComponent(prefabEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) && instanceAny.IsValid)
        {
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, instanceHandle);
            if (instance.IsAlive && instance.ValueCount != 0)
            {
                ushort instanceCount = instance.ValueCount;
                if (instanceCount > PrefabInstanceComponent.MaxVariables)
                {
                    instanceCount = PrefabInstanceComponent.MaxVariables;
                }

                ReadOnlySpan<ushort> instanceIds = instance.VariableIdReadOnlySpan();
                ReadOnlySpan<PropertyValue> instanceValues = instance.ValueReadOnlySpan();

                int instanceIndex = -1;
                for (int i = 0; i < instanceCount; i++)
                {
                    if (instanceIds[i] == variableId)
                    {
                        instanceIndex = i;
                        break;
                    }
                }

                if (instanceIndex >= 0)
                {
                    bool isOverridden = (instance.OverrideMask & (1UL << instanceIndex)) != 0;
                    overridden = isOverridden;
                    if (isOverridden)
                    {
                        value = instanceValues[instanceIndex];
                    }
                }
            }
        }

        return true;
    }

    public bool TryGetPrefabVariableSchema(
        uint prefabStableId,
        Span<ushort> variableIds,
        Span<StringHandle> variableNames,
        Span<PropertyKind> variableKinds,
        Span<PropertyValue> defaultValues,
        out int count)
    {
        count = 0;

        if (prefabStableId == 0)
        {
            return false;
        }

        int capacity = variableIds.Length;
        if (variableNames.Length < capacity)
        {
            capacity = variableNames.Length;
        }
        if (variableKinds.Length < capacity)
        {
            capacity = variableKinds.Length;
        }
        if (defaultValues.Length < capacity)
        {
            capacity = defaultValues.Length;
        }

        if (capacity <= 0)
        {
            return false;
        }

        EntityId prefabEntity = _workspace.World.GetEntityByStableId(prefabStableId);
        if (prefabEntity.IsNull || _workspace.World.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (!_workspace.World.TryGetComponent(prefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return false;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(_workspace.PropertyWorld, varsHandle);
        if (!vars.IsAlive)
        {
            return false;
        }

        ushort schemaCount = vars.VariableCount;
        if (schemaCount > PrefabVariablesComponent.MaxVariables)
        {
            schemaCount = PrefabVariablesComponent.MaxVariables;
        }

        int written = Math.Min(capacity, schemaCount);
        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();

        for (int i = 0; i < written; i++)
        {
            variableIds[i] = ids[i];
            variableNames[i] = names[i];
            variableKinds[i] = (PropertyKind)kinds[i];
            defaultValues[i] = defaults[i];
        }

        count = written;
        return true;
    }

    public bool TryGetActivePrefabVariableSchema(
        Span<ushort> variableIds,
        Span<StringHandle> variableNames,
        Span<PropertyKind> variableKinds,
        Span<PropertyValue> defaultValues,
        out int count)
    {
        if (_activePrefabStableId == 0)
        {
            count = 0;
            return false;
        }

        return TryGetPrefabVariableSchema(_activePrefabStableId, variableIds, variableNames, variableKinds, defaultValues, out count);
    }

    public bool TryGetActivePrefabVariableValue(ushort variableId, out UiRuntimeVariableDebug debug)
    {
        if (_activePrefabStableId == 0)
        {
            debug = default;
            return false;
        }

        return TryGetPrefabVariableValue(_activePrefabStableId, variableId, out debug);
    }

    public bool TrySetPrefabInstanceVariableOverride(uint instanceStableId, ushort variableId, in PropertyValue value, bool resolveLayout = false)
    {
        if (instanceStableId == 0 || variableId == 0)
        {
            return false;
        }

        EntityId entity = _workspace.World.GetEntityByStableId(instanceStableId);
        if (entity.IsNull)
        {
            return false;
        }

        bool ok = _workspace.TrySetPrefabInstanceVariableOverrideRuntime(entity, variableId, value);
        if (ok && resolveLayout)
        {
            _workspace.ResolveLayout();
        }

        return ok;
    }

    public bool TrySetPrefabInstanceVariableOverrideByName(
        uint instanceStableId,
        uint prefabStableIdForSchema,
        ReadOnlySpan<char> variableName,
        in PropertyValue value,
        bool resolveLayout = false)
    {
        if (!TryResolvePrefabVariableId(prefabStableIdForSchema, variableName, out ushort variableId, out _))
        {
            return false;
        }

        return TrySetPrefabInstanceVariableOverride(instanceStableId, variableId, value, resolveLayout);
    }

    public void SetPointerFrameInput(in UiPointerFrameInput input)
    {
        _frameInputSetThisFrame = true;
        _workspace.SetRuntimePointerFrameInput(input);
    }

    public uint HitTestHoveredStableId(Vector2 pointerWorld)
    {
        return UiRuntimeHitTest.HitHoveredStableId(_workspace, _activePrefabStableId, pointerWorld);
    }

    public void ResolveLayout()
    {
        if (_activePrefab.IsNull)
        {
            return;
        }

        _workspace.ResolveLayout();
    }

    public void Tick(uint deltaMicroseconds)
    {
        if (_activePrefab.IsNull)
        {
            return;
        }

        if (!_frameInputSetThisFrame)
        {
            _workspace.SetRuntimePointerFrameInput(new UiPointerFrameInput(pointerValid: false, pointerWorld: default, primaryDown: false, wheelDelta: 0f, hoveredStableId: 0));
        }

        _frameInputSetThisFrame = false;
        _workspace.TickRuntime(_activePrefab, deltaMicroseconds);
    }

    public void Tick(uint deltaMicroseconds, in UiPointerFrameInput input)
    {
        SetPointerFrameInput(input);
        Tick(deltaMicroseconds);
    }

    public void BuildFrame(CanvasSurface surface, int width, int height)
    {
        if (surface is null)
        {
            throw new ArgumentNullException(nameof(surface));
        }

        if (_activePrefab.IsNull)
        {
            surface.BeginFrame(width, height);
            return;
        }

        surface.BeginFrame(width, height);
        var draw = new CanvasSdfDrawList(surface.Buffer);

        _workspace.BuildRuntimeUi(draw, _activePrefab, width, height);
    }

    private static EntityId GetDefaultPrefabEntity(CompiledUi compiledUi, EntityId[] entitiesByNodeIndex, out uint stableId)
    {
        stableId = 0;
        if (compiledUi.Prefabs.Length == 0)
        {
            return EntityId.Null;
        }

        var prefab = compiledUi.Prefabs[0];

        int rootNodeIndex = prefab.RootNodeIndex;
        if ((uint)rootNodeIndex >= (uint)entitiesByNodeIndex.Length)
        {
            return EntityId.Null;
        }

        EntityId entity = entitiesByNodeIndex[rootNodeIndex];
        stableId = prefab.StableId;
        return entity;
    }

    public bool TryGetHoveredListenerStableId(out uint listenerStableId)
    {
        listenerStableId = 0;

        if (_activePrefab.IsNull || _activePrefabStableId == 0)
        {
            return false;
        }

        uint hitStableId = Input.Current.HoveredStableId;
        if (hitStableId == 0 || hitStableId == UiPointerFrameInput.ComputeHoveredStableId)
        {
            return false;
        }

        UiWorld world = _workspace.World;
        EntityId current = world.GetEntityByStableId(hitStableId);
        if (current.IsNull)
        {
            return false;
        }

        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            if (world.TryGetComponent(current, EventListenerComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
            {
                uint stableId = world.GetStableId(current);
                listenerStableId = stableId;
                return stableId != 0;
            }

            if (current.Value == _activePrefab.Value)
            {
                break;
            }

            current = world.GetParent(current);
        }

        return false;
    }

    public bool TryGetOwningPrefabInstanceStableId(uint entityStableId, out uint instanceStableId)
    {
        instanceStableId = 0;

        if (_activePrefab.IsNull || _activePrefabStableId == 0 || entityStableId == 0)
        {
            return false;
        }

        UiWorld world = _workspace.World;
        EntityId current = world.GetEntityByStableId(entityStableId);
        if (current.IsNull)
        {
            return false;
        }

        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            UiNodeType type = world.GetNodeType(current);
            if (type == UiNodeType.PrefabInstance)
            {
                if (world.TryGetComponent(current, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) &&
                    instanceAny.IsValid)
                {
                    uint stableId = world.GetStableId(current);
                    instanceStableId = stableId;
                    return stableId != 0;
                }
            }

            if (current.Value == _activePrefab.Value)
            {
                break;
            }

            current = world.GetParent(current);
        }

        return false;
    }

    public bool TryGetRuntimeVariableStoreStableIdForListener(uint listenerStableId, out uint storeStableId)
    {
        storeStableId = 0;

        if (_activePrefab.IsNull || _activePrefabStableId == 0 || listenerStableId == 0)
        {
            return false;
        }

        UiWorld world = _workspace.World;
        EntityId listenerEntity = world.GetEntityByStableId(listenerStableId);
        if (listenerEntity.IsNull)
        {
            return false;
        }

        EntityId storeEntity = FindVariableStoreForListener(_activePrefab, listenerEntity);
        uint stableId = world.GetStableId(storeEntity);
        storeStableId = stableId;
        return stableId != 0;
    }

    private EntityId FindVariableStoreForListener(EntityId activePrefabEntity, EntityId listenerEntity)
    {
        UiWorld world = _workspace.World;

        EntityId current = listenerEntity;
        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            UiNodeType type = world.GetNodeType(current);
            if (type == UiNodeType.PrefabInstance)
            {
                // Expanded prefab-instance proxy roots are tagged as PrefabInstance nodes but do not carry
                // PrefabInstanceComponent (they are editor-only scaffolding). Skip those and keep walking
                // until we hit the real instance entity that owns variable overrides.
                if (world.TryGetComponent(current, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) &&
                    instanceAny.IsValid)
                {
                    return current;
                }
            }

            EntityId parent = world.GetParent(current);
            if (parent.IsNull)
            {
                break;
            }

            if (parent.Value == activePrefabEntity.Value)
            {
                break;
            }

            current = parent;
        }

        return activePrefabEntity;
    }

    public bool TryGetVariableSchemaStableIdForStore(uint storeStableId, out uint schemaPrefabStableId)
    {
        schemaPrefabStableId = 0;

        if (storeStableId == 0)
        {
            return false;
        }

        UiWorld world = _workspace.World;
        EntityId storeEntity = world.GetEntityByStableId(storeStableId);
        if (storeEntity.IsNull)
        {
            return false;
        }

        UiNodeType type = world.GetNodeType(storeEntity);
        if (type == UiNodeType.Prefab)
        {
            schemaPrefabStableId = storeStableId;
            return true;
        }

        if (type != UiNodeType.PrefabInstance)
        {
            return false;
        }

        if (!world.TryGetComponent(storeEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle any) || !any.IsValid)
        {
            return false;
        }

        var handle = new PrefabInstanceComponentHandle(any.Index, any.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!instance.IsAlive || instance.SourcePrefabStableId == 0)
        {
            return false;
        }

        schemaPrefabStableId = instance.SourcePrefabStableId;
        return true;
    }

    public bool TryGetStoreVariableValueDetailed(
        uint storeStableId,
        ushort variableId,
        out PropertyKind kind,
        out PropertyValue value,
        out bool overridden,
        out StringHandle name)
    {
        kind = default;
        value = default;
        overridden = false;
        name = default;

        if (storeStableId == 0 || variableId == 0)
        {
            return false;
        }

        UiWorld world = _workspace.World;
        World propertyWorld = _workspace.PropertyWorld;

        EntityId storeEntity = world.GetEntityByStableId(storeStableId);
        if (storeEntity.IsNull)
        {
            return false;
        }

        if (!TryGetVariableSchemaStableIdForStore(storeStableId, out uint schemaPrefabStableId) || schemaPrefabStableId == 0)
        {
            return false;
        }

        EntityId schemaPrefabEntity = world.GetEntityByStableId(schemaPrefabStableId);
        if (schemaPrefabEntity.IsNull || world.GetNodeType(schemaPrefabEntity) != UiNodeType.Prefab)
        {
            return false;
        }

        if (!world.TryGetComponent(schemaPrefabEntity, PrefabVariablesComponent.Api.PoolIdConst, out AnyComponentHandle varsAny) || !varsAny.IsValid)
        {
            return false;
        }

        var varsHandle = new PrefabVariablesComponentHandle(varsAny.Index, varsAny.Generation);
        var vars = PrefabVariablesComponent.Api.FromHandle(propertyWorld, varsHandle);
        if (!vars.IsAlive || vars.VariableCount == 0)
        {
            return false;
        }

        ushort count = vars.VariableCount;
        if (count > PrefabVariablesComponent.MaxVariables)
        {
            count = PrefabVariablesComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> ids = vars.VariableIdReadOnlySpan();
        ReadOnlySpan<int> kinds = vars.KindReadOnlySpan();
        ReadOnlySpan<PropertyValue> defaults = vars.DefaultValueReadOnlySpan();
        ReadOnlySpan<StringHandle> names = vars.NameReadOnlySpan();

        int schemaIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (ids[i] == variableId)
            {
                schemaIndex = i;
                break;
            }
        }

        if (schemaIndex < 0)
        {
            return false;
        }

        kind = (PropertyKind)kinds[schemaIndex];
        value = defaults[schemaIndex];
        name = names[schemaIndex];

        if (!world.TryGetComponent(storeEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return true;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(propertyWorld, instanceHandle);
        if (!instance.IsAlive || instance.ValueCount == 0)
        {
            return true;
        }

        ushort instanceCount = instance.ValueCount;
        if (instanceCount > PrefabInstanceComponent.MaxVariables)
        {
            instanceCount = PrefabInstanceComponent.MaxVariables;
        }

        ReadOnlySpan<ushort> instanceIds = instance.VariableIdReadOnlySpan();
        ReadOnlySpan<PropertyValue> instanceValues = instance.ValueReadOnlySpan();
        ulong mask = instance.OverrideMask;

        for (int i = 0; i < instanceCount; i++)
        {
            if (instanceIds[i] != variableId)
            {
                continue;
            }

            bool isOverridden = (mask & (1UL << i)) != 0;
            overridden = isOverridden;
            if (isOverridden)
            {
                value = instanceValues[i];
            }
            return true;
        }

        return true;
    }

    public bool TryGetPrefabInstanceSourcePrefabStableId(uint instanceStableId, out uint sourcePrefabStableId)
    {
        sourcePrefabStableId = 0;

        if (instanceStableId == 0)
        {
            return false;
        }

        UiWorld world = _workspace.World;
        EntityId entity = world.GetEntityByStableId(instanceStableId);
        if (entity.IsNull || world.GetNodeType(entity) != UiNodeType.PrefabInstance)
        {
            return false;
        }

        if (!world.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle any) || !any.IsValid)
        {
            return false;
        }

        var handle = new PrefabInstanceComponentHandle(any.Index, any.Generation);
        var view = PrefabInstanceComponent.Api.FromHandle(_workspace.PropertyWorld, handle);
        if (!view.IsAlive)
        {
            return false;
        }

        sourcePrefabStableId = view.SourcePrefabStableId;
        return sourcePrefabStableId != 0;
    }
}
