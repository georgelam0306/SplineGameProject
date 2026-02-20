using System;
using System.Collections.Generic;
using System.Numerics;
using Pooled.Runtime;
using Property.Runtime;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private uint _runtimeHoveredLeafStableId;
    private uint _runtimePressedLeafStableId;

    private uint _runtimeScrollDragHandleStableId;
    private uint _runtimeScrollDragObjectStableId;
    private byte _runtimeScrollDragAxis; // 0 = none, 1 = vertical, 2 = horizontal
    private float _runtimeScrollDragGrabOffset;

    private readonly List<uint> _runtimeHoveredAncestorStableIds = new(capacity: 64);
    private readonly List<uint> _runtimePrevHoveredAncestorStableIds = new(capacity: 64);
    private readonly List<TriggerReset> _runtimeTriggerResets = new(capacity: 128);

    private bool _runtimeAddedPrefabVariableStore;
    private bool _runtimePointerInputOverriddenThisFrame;
    private bool _runtimeConsumedPrimaryThisFrame;

    public UiRuntimeInput RuntimeInput { get; } = new();

    public void SetRuntimePointerFrameInput(in UiPointerFrameInput input)
    {
        _runtimePointerInputOverriddenThisFrame = true;
        RuntimeInput.SetFrameInput(input);
    }

    private void ResetRuntimeEventState()
    {
        _runtimeHoveredLeafStableId = 0;
        _runtimePressedLeafStableId = 0;
        _runtimeScrollDragHandleStableId = 0;
        _runtimeScrollDragObjectStableId = 0;
        _runtimeScrollDragAxis = 0;
        _runtimeScrollDragGrabOffset = 0f;
        _runtimeHoveredAncestorStableIds.Clear();
        _runtimePrevHoveredAncestorStableIds.Clear();
        _runtimeTriggerResets.Clear();
        _runtimePointerInputOverriddenThisFrame = false;
        _runtimeConsumedPrimaryThisFrame = false;

        RuntimeInput.SetFrameInput(new UiPointerFrameInput(pointerValid: false, pointerWorld: default, primaryDown: false, wheelDelta: 0f, hoveredStableId: 0));
    }

    private void EnsureRuntimePrefabVariableStore(EntityId prefabEntity)
    {
        if (prefabEntity.IsNull || _world.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return;
        }

        if (_world.TryGetComponent(prefabEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            return;
        }

        uint sourcePrefabStableId = _world.GetStableId(prefabEntity);
        var created = PrefabInstanceComponent.Api.Create(_propertyWorld, new PrefabInstanceComponent
        {
            SourcePrefabStableId = sourcePrefabStableId,
            SourcePrefabRevisionAtBuild = 0,
            SourcePrefabBindingsRevisionAtBuild = 0,
            ValueCount = 0,
            OverrideMask = 0,
            CanvasSizeIsOverridden = 0
        });
        var handle = new AnyComponentHandle(PrefabInstanceComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation);
        SetComponentWithStableId(prefabEntity, handle);
        _runtimeAddedPrefabVariableStore = true;
    }

    private void RemoveRuntimePrefabVariableStore(EntityId prefabEntity)
    {
        if (!_runtimeAddedPrefabVariableStore)
        {
            return;
        }

        _runtimeAddedPrefabVariableStore = false;

        if (prefabEntity.IsNull || _world.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return;
        }

        if (_world.TryGetComponent(prefabEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
        {
            var handle = new PrefabInstanceComponentHandle(any.Index, any.Generation);
            var view = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive)
            {
                PrefabInstanceComponent.Api.Destroy(_propertyWorld, view);
            }
            _world.RemoveComponent(prefabEntity, PrefabInstanceComponent.Api.PoolIdConst);
        }
    }

    internal void TickRuntimeEvents(EntityId prefabEntity)
    {
        if (!IsRuntimeMode)
        {
            return;
        }

        _runtimeConsumedPrimaryThisFrame = false;

        if (!_runtimePointerInputOverriddenThisFrame)
        {
            var contentRect = Im.WindowContentRect;
            Vector2 mouseCanvas = Im.MousePos;
            bool canvasHovered = contentRect.Contains(mouseCanvas);

            var canvasOrigin = new Vector2(
                contentRect.X + contentRect.Width * 0.5f,
                contentRect.Y + contentRect.Height * 0.5f);

            bool pointerValid = canvasHovered;
            Vector2 pointerWorld = pointerValid ? CanvasToWorld(mouseCanvas, canvasOrigin) : default;
            bool primaryDown = Im.MouseDown;
            float wheelDelta = canvasHovered ? Im.Context.Input.ScrollDelta : 0f;

            uint hoveredStableId = 0;
            if (pointerValid)
            {
                hoveredStableId = HitTestHoveredStableIdForRuntime(prefabEntity, pointerWorld);
            }

            RuntimeInput.SetFrameInput(new UiPointerFrameInput(pointerValid, pointerWorld, primaryDown, wheelDelta, hoveredStableId));
        }

        ref readonly UiPointerFrameInput input = ref RuntimeInput.Current;
        uint hoveredHitStableIdForFrame = 0;
        if (input.PointerValid)
        {
            uint hoveredStableId = input.HoveredStableId;
            if (hoveredStableId == UiPointerFrameInput.ComputeHoveredStableId)
            {
                hoveredStableId = HitTestHoveredStableIdForRuntime(prefabEntity, input.PointerWorld);
            }
            hoveredHitStableIdForFrame = hoveredStableId;
        }

        RuntimeInput.SetComputedHoveredStableId(hoveredHitStableIdForFrame);

        uint hoveredListenerStableIdForFrame = ResolveHoveredListenerStableId(prefabEntity, hoveredHitStableIdForFrame);

        bool consumedPrimary = UpdateRuntimeScrollDrag(hoveredHitStableIdForFrame);
        _runtimeConsumedPrimaryThisFrame = consumedPrimary;

        uint prevHovered = _runtimeHoveredLeafStableId;
        if (prevHovered != hoveredListenerStableIdForFrame)
        {
            if (prevHovered != 0)
            {
                EntityId prevEntity = _world.GetEntityByStableId(prevHovered);
                if (!prevEntity.IsNull)
                {
                    ApplyLeafHover(prevEntity, prefabEntity, isHovered: false, fireEnterExit: true, entering: false);
                }
            }

            if (hoveredListenerStableIdForFrame != 0)
            {
                EntityId hoveredEntity = _world.GetEntityByStableId(hoveredListenerStableIdForFrame);
                if (!hoveredEntity.IsNull)
                {
                    ApplyLeafHover(hoveredEntity, prefabEntity, isHovered: true, fireEnterExit: true, entering: true);
                }
            }

            _runtimeHoveredLeafStableId = hoveredListenerStableIdForFrame;
        }
        else if (hoveredListenerStableIdForFrame != 0)
        {
            // Keep held hover variables set while hovering.
            EntityId hoveredEntity = _world.GetEntityByStableId(hoveredListenerStableIdForFrame);
            if (!hoveredEntity.IsNull)
            {
                ApplyLeafHover(hoveredEntity, prefabEntity, isHovered: true, fireEnterExit: false, entering: false);
            }
        }

        UpdateChildHover(prefabEntity, hoveredHitStableIdForFrame);
        UpdateRuntimeScrollWheel(prefabEntity, hoveredHitStableIdForFrame, input.WheelDelta);

        if (!consumedPrimary && RuntimeInput.PrimaryPressed && hoveredListenerStableIdForFrame != 0)
        {
            EntityId hoveredEntity = _world.GetEntityByStableId(hoveredListenerStableIdForFrame);
            if (!hoveredEntity.IsNull)
            {
                ApplyPress(hoveredEntity, prefabEntity);
                _runtimePressedLeafStableId = hoveredListenerStableIdForFrame;
            }
        }

        if (RuntimeInput.PrimaryReleased && _runtimePressedLeafStableId != 0)
        {
            EntityId pressedEntity = _world.GetEntityByStableId(_runtimePressedLeafStableId);
            if (!pressedEntity.IsNull)
            {
                bool isClick = hoveredListenerStableIdForFrame != 0 && hoveredListenerStableIdForFrame == _runtimePressedLeafStableId;
                ApplyRelease(pressedEntity, prefabEntity, isClick);
            }

            _runtimePressedLeafStableId = 0;
        }

        _runtimePointerInputOverriddenThisFrame = false;
    }

    private uint ResolveHoveredListenerStableId(EntityId activePrefabEntity, uint hoveredHitStableId)
    {
        if (hoveredHitStableId == 0)
        {
            return 0;
        }

        EntityId current = _world.GetEntityByStableId(hoveredHitStableId);
        if (current.IsNull)
        {
            return 0;
        }

        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            if (_world.TryGetComponent(current, EventListenerComponent.Api.PoolIdConst, out AnyComponentHandle any) && any.IsValid)
            {
                uint stableId = _world.GetStableId(current);
                if (stableId != 0)
                {
                    return stableId;
                }
                return 0;
            }

            if (current.Value == activePrefabEntity.Value)
            {
                break;
            }

            EntityId parent = _world.GetParent(current);
            if (parent.IsNull)
            {
                break;
            }

            current = parent;
        }

        return 0;
    }

    private void UpdateRuntimeScrollWheel(EntityId prefabEntity, uint hoveredStableIdForFrame, float wheelDelta)
    {
        if (wheelDelta == 0f || hoveredStableIdForFrame == 0)
        {
            return;
        }

        if (_runtimeScrollDragHandleStableId != 0)
        {
            return;
        }

        EntityId hoveredEntity = _world.GetEntityByStableId(hoveredStableIdForFrame);
        if (hoveredEntity.IsNull)
        {
            return;
        }

        if (!TryFindScrollConstraintForWheel(prefabEntity, hoveredEntity, out EntityId handleEntity, out EntityId scrollObjectEntity))
        {
            return;
        }

        if (!TryComputeScrollDragMetrics(handleEntity, scrollObjectEntity, out ScrollDragMetrics metrics))
        {
            return;
        }

        ApplyScrollWheel(metrics, wheelDelta);
    }

    private void ApplyScrollWheel(in ScrollDragMetrics metrics, float wheelDelta)
    {
        const float ScrollWheelSpeed = 64f;

        float overflow = metrics.Overflow;
        if (overflow <= 0.0001f)
        {
            return;
        }

        float viewportMin = metrics.UseVertical ? metrics.ViewportRectLocal.Y : metrics.ViewportRectLocal.X;
        float scrollMin = metrics.UseVertical ? metrics.ScrollBoundsRectLocal.Y : metrics.ScrollBoundsRectLocal.X;

        float t = (viewportMin - scrollMin) / overflow;
        if (!float.IsFinite(t))
        {
            t = 0f;
        }
        t = Math.Clamp(t, 0f, 1f);

        float deltaT = (wheelDelta * ScrollWheelSpeed) / overflow;
        if (!float.IsFinite(deltaT))
        {
            return;
        }

        float nextT = Math.Clamp(t - deltaT, 0f, 1f);

        if (metrics.UseVertical)
        {
            float desiredScrollMin = metrics.ViewportRectLocal.Y - nextT * overflow;
            var desiredBounds = new ImRect(metrics.ScrollBoundsRectLocal.X, desiredScrollMin, metrics.ScrollBoundsRectLocal.Width, metrics.ScrollBoundsRectLocal.Height);
            var desiredRect = RectTransformMath.Inset(desiredBounds, metrics.ScrollObjectMargin);
            Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(desiredRect, metrics.ScrollObjectPivot01);
            _ = TrySetTransformPivotPositionNoUndo(metrics.ScrollObjectEntity, pivotPos);
        }
        else
        {
            float desiredScrollMin = metrics.ViewportRectLocal.X - nextT * overflow;
            var desiredBounds = new ImRect(desiredScrollMin, metrics.ScrollBoundsRectLocal.Y, metrics.ScrollBoundsRectLocal.Width, metrics.ScrollBoundsRectLocal.Height);
            var desiredRect = RectTransformMath.Inset(desiredBounds, metrics.ScrollObjectMargin);
            Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(desiredRect, metrics.ScrollObjectPivot01);
            _ = TrySetTransformPivotPositionNoUndo(metrics.ScrollObjectEntity, pivotPos);
        }
    }

    private bool TryFindScrollConstraintForWheel(EntityId prefabEntity, EntityId hoveredEntity, out EntityId handleEntity, out EntityId scrollObjectEntity)
    {
        handleEntity = EntityId.Null;
        scrollObjectEntity = EntityId.Null;

        uint candidateStableId0 = _world.GetStableId(hoveredEntity);
        if (candidateStableId0 == 0)
        {
            return false;
        }

        // Candidate viewport stable-ids ordered from leaf -> root.
        Span<uint> candidateStableIds = stackalloc uint[64];
        int candidateCount = 0;
        candidateStableIds[candidateCount++] = candidateStableId0;

        EntityId current = hoveredEntity;
        int guard = 512;
        while (!current.IsNull && guard-- > 0 && candidateCount < candidateStableIds.Length)
        {
            EntityId parent = _world.GetParent(current);
            if (parent.IsNull)
            {
                break;
            }

            UiNodeType parentType = _world.GetNodeType(parent);
            if (parentType == UiNodeType.Prefab || parent.Value == prefabEntity.Value)
            {
                break;
            }

            uint stableId = _world.GetStableId(parent);
            if (stableId != 0)
            {
                candidateStableIds[candidateCount++] = stableId;
            }

            current = parent;
        }

        int bestCandidateIndex = int.MaxValue;

        // Scan scroll constraints within the active runtime subtree.
        for (int i = 0; i < _runtimeModeEntities.Count; i++)
        {
            EntityId candidateHandle = _runtimeModeEntities[i];
            if (candidateHandle.IsNull)
            {
                continue;
            }

            if (!TryGetScrollConstraintTargetForRuntimeHandle(candidateHandle, out EntityId candidateObject))
            {
                continue;
            }

            EntityId viewport = _world.GetParent(candidateObject);
            if (viewport.IsNull)
            {
                continue;
            }

            uint viewportStableId = _world.GetStableId(viewport);
            if (viewportStableId == 0)
            {
                continue;
            }

            int candidateIndex = -1;
            for (int c = 0; c < candidateCount; c++)
            {
                if (candidateStableIds[c] == viewportStableId)
                {
                    candidateIndex = c;
                    break;
                }
            }

            if (candidateIndex < 0 || candidateIndex >= bestCandidateIndex)
            {
                continue;
            }

            bestCandidateIndex = candidateIndex;
            handleEntity = candidateHandle;
            scrollObjectEntity = candidateObject;
            if (bestCandidateIndex == 0)
            {
                break;
            }
        }

        return !handleEntity.IsNull && !scrollObjectEntity.IsNull;
    }

    private readonly struct ScrollDragMetrics
    {
        public readonly EntityId HandleEntity;
        public readonly EntityId TrackEntity;
        public readonly EntityId ScrollObjectEntity;
        public readonly EntityId ScrollViewportEntity;
        public readonly bool UseVertical;
        public readonly ImRect TrackRectLocal;
        public readonly ImRect HandleRectLocal;
        public readonly ImRect ViewportRectLocal;
        public readonly ImRect ScrollObjectRectLocal;
        public readonly ImRect ScrollBoundsRectLocal;
        public readonly Vector2 ScrollObjectPivot01;
        public readonly Vector2 ScrollObjectSize;
        public readonly Insets ScrollObjectMargin;
        public readonly float Overflow;
        public readonly float Travel;
        public readonly float HandleLength;

        public ScrollDragMetrics(
            EntityId handleEntity,
            EntityId trackEntity,
            EntityId scrollObjectEntity,
            EntityId scrollViewportEntity,
            bool useVertical,
            ImRect trackRectLocal,
            ImRect handleRectLocal,
            ImRect viewportRectLocal,
            ImRect scrollObjectRectLocal,
            ImRect scrollBoundsRectLocal,
            Vector2 scrollObjectPivot01,
            Vector2 scrollObjectSize,
            Insets scrollObjectMargin,
            float overflow,
            float travel,
            float handleLength)
        {
            HandleEntity = handleEntity;
            TrackEntity = trackEntity;
            ScrollObjectEntity = scrollObjectEntity;
            ScrollViewportEntity = scrollViewportEntity;
            UseVertical = useVertical;
            TrackRectLocal = trackRectLocal;
            HandleRectLocal = handleRectLocal;
            ViewportRectLocal = viewportRectLocal;
            ScrollObjectRectLocal = scrollObjectRectLocal;
            ScrollBoundsRectLocal = scrollBoundsRectLocal;
            ScrollObjectPivot01 = scrollObjectPivot01;
            ScrollObjectSize = scrollObjectSize;
            ScrollObjectMargin = scrollObjectMargin;
            Overflow = overflow;
            Travel = travel;
            HandleLength = handleLength;
        }
    }

    private bool UpdateRuntimeScrollDrag(uint hoveredStableIdForFrame)
    {
        ref readonly UiPointerFrameInput input = ref RuntimeInput.Current;

        if (_runtimeScrollDragHandleStableId != 0)
        {
            if (!input.PointerValid || !input.PrimaryDown)
            {
                _runtimeScrollDragHandleStableId = 0;
                _runtimeScrollDragObjectStableId = 0;
                _runtimeScrollDragAxis = 0;
                _runtimeScrollDragGrabOffset = 0f;
                return true;
            }

            EntityId dragHandleEntity = _world.GetEntityByStableId(_runtimeScrollDragHandleStableId);
            EntityId dragObjectEntity = _world.GetEntityByStableId(_runtimeScrollDragObjectStableId);
            if (dragHandleEntity.IsNull || dragObjectEntity.IsNull)
            {
                _runtimeScrollDragHandleStableId = 0;
                _runtimeScrollDragObjectStableId = 0;
                _runtimeScrollDragAxis = 0;
                _runtimeScrollDragGrabOffset = 0f;
                return true;
            }

            if (TryComputeScrollDragMetrics(dragHandleEntity, dragObjectEntity, out ScrollDragMetrics metrics))
            {
                TryApplyScrollDrag(metrics, input.PointerWorld);
            }

            return true;
        }

        if (!RuntimeInput.PrimaryPressed || hoveredStableIdForFrame == 0 || !input.PointerValid)
        {
            return false;
        }

        EntityId hoveredEntity = _world.GetEntityByStableId(hoveredStableIdForFrame);
        if (hoveredEntity.IsNull)
        {
            return false;
        }

        if (!TryFindScrollHandleForRuntimeDrag(hoveredEntity, out EntityId handleEntity, out EntityId scrollObjectEntity))
        {
            return false;
        }

        if (!TryComputeScrollDragMetrics(handleEntity, scrollObjectEntity, out ScrollDragMetrics beginMetrics))
        {
            return false;
        }

        if (!TryGetParentLocalPointFromWorldEcs(beginMetrics.TrackEntity, input.PointerWorld, out Vector2 pointerLocalTrack))
        {
            return false;
        }

        float grabOffset = beginMetrics.UseVertical
            ? (pointerLocalTrack.Y - beginMetrics.HandleRectLocal.Y)
            : (pointerLocalTrack.X - beginMetrics.HandleRectLocal.X);

        float maxGrab = beginMetrics.HandleLength;
        if (grabOffset < 0f)
        {
            grabOffset = 0f;
        }
        if (grabOffset > maxGrab)
        {
            grabOffset = maxGrab;
        }

        _runtimeScrollDragHandleStableId = _world.GetStableId(handleEntity);
        _runtimeScrollDragObjectStableId = _world.GetStableId(scrollObjectEntity);
        _runtimeScrollDragAxis = beginMetrics.UseVertical ? (byte)1 : (byte)2;
        _runtimeScrollDragGrabOffset = grabOffset;
        return true;
    }

    private bool TryFindScrollHandleForRuntimeDrag(EntityId hoveredEntity, out EntityId handleEntity, out EntityId scrollObjectEntity)
    {
        handleEntity = EntityId.Null;
        scrollObjectEntity = EntityId.Null;

        EntityId current = hoveredEntity;
        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            UiNodeType type = _world.GetNodeType(current);
            if (type == UiNodeType.Prefab)
            {
                break;
            }

            if (TryGetScrollConstraintTargetForRuntimeHandle(current, out scrollObjectEntity))
            {
                EnsureRuntimeDraggableEnabled(current);
                handleEntity = current;
                return true;
            }

            current = _world.GetParent(current);
        }

        return false;
    }

    private void EnsureRuntimeDraggableEnabled(EntityId entity)
    {
        if (!_world.TryGetComponent(entity, DraggableComponent.Api.PoolIdConst, out AnyComponentHandle dragAny) || !dragAny.IsValid)
        {
            return;
        }

        var handle = new DraggableComponentHandle(dragAny.Index, dragAny.Generation);
        var drag = DraggableComponent.Api.FromHandle(_propertyWorld, handle);
        if (!drag.IsAlive || drag.Enabled)
        {
            return;
        }

        drag.Enabled = true;
    }


    private bool TryGetScrollConstraintTargetForRuntimeHandle(EntityId handleEntity, out EntityId scrollObjectEntity)
    {
        scrollObjectEntity = EntityId.Null;

        if (!_world.TryGetComponent(handleEntity, ConstraintListComponent.Api.PoolIdConst, out AnyComponentHandle constraintsAny) || !constraintsAny.IsValid)
        {
            return false;
        }

        var handle = new ConstraintListComponentHandle(constraintsAny.Index, constraintsAny.Generation);
        var constraints = ConstraintListComponent.Api.FromHandle(_propertyWorld, handle);
        if (!constraints.IsAlive)
        {
            return false;
        }

        ushort count = constraints.Count;
        if (count == 0)
        {
            return false;
        }
        if (count > ConstraintListComponent.MaxConstraints)
        {
            count = ConstraintListComponent.MaxConstraints;
        }

        uint instanceRootStableId = GetConstraintInstanceRootStableId(handleEntity);

        ReadOnlySpan<byte> enabled = constraints.EnabledValueReadOnlySpan();
        ReadOnlySpan<byte> kind = constraints.KindValueReadOnlySpan();
        ReadOnlySpan<uint> target = constraints.TargetSourceStableIdReadOnlySpan();

        for (int i = 0; i < count; i++)
        {
            if (enabled[i] == 0)
            {
                continue;
            }

            if ((ConstraintListComponent.ConstraintKind)kind[i] != ConstraintListComponent.ConstraintKind.Scroll)
            {
                continue;
            }

            uint targetStableId = target[i];
            if (targetStableId == 0)
            {
                continue;
            }

            if (!TryResolveConstraintTargetEntity(instanceRootStableId, targetStableId, out EntityId resolved))
            {
                continue;
            }

            if (resolved.IsNull)
            {
                continue;
            }

            scrollObjectEntity = resolved;
            return true;
        }

        return false;
    }

    private bool TryComputeScrollDragMetrics(EntityId handleEntity, EntityId scrollObjectEntity, out ScrollDragMetrics metrics)
    {
        metrics = default;

        EntityId trackEntity = _world.GetParent(handleEntity);
        if (trackEntity.IsNull)
        {
            return false;
        }

        EntityId viewportEntity = _world.GetParent(scrollObjectEntity);
        if (viewportEntity.IsNull)
        {
            return false;
        }

        if (!TryGetEntitySizeTarget(scrollObjectEntity, out Vector2 scrollObjectSize, out _))
        {
            return false;
        }
        if (!TryGetEntitySizeTarget(viewportEntity, out Vector2 viewportSize, out _))
        {
            return false;
        }
        if (!TryGetEntitySizeTarget(trackEntity, out Vector2 trackSize, out _))
        {
            return false;
        }
        if (!TryGetEntitySizeTarget(handleEntity, out Vector2 handleSize, out _))
        {
            return false;
        }

        if (!TryGetComputedPivotPosition(scrollObjectEntity, out Vector2 scrollObjectPivotPos))
        {
            return false;
        }
        if (!TryGetComputedPivotPosition(handleEntity, out Vector2 handlePivotPos))
        {
            return false;
        }

        Vector2 scrollObjectPivot01 = GetTransformPivot01OrDefault(scrollObjectEntity);
        Vector2 viewportPivot01 = GetTransformPivot01OrDefault(viewportEntity);
        Vector2 trackPivot01 = GetTransformPivot01OrDefault(trackEntity);
        Vector2 handlePivot01 = GetTransformPivot01OrDefault(handleEntity);

        ImRect viewportRectLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, viewportPivot01, viewportSize);
        ImRect scrollObjectRectLocal = RectTransformMath.GetRectFromPivot(scrollObjectPivotPos, scrollObjectPivot01, scrollObjectSize);
        ImRect trackRectLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, trackPivot01, trackSize);
        ImRect handleRectLocal = RectTransformMath.GetRectFromPivot(handlePivotPos, handlePivot01, handleSize);

        Insets viewportPadding = GetLayoutContainerPaddingOrZero(viewportEntity);
        Insets trackPadding = GetLayoutContainerPaddingOrZero(trackEntity);
        Insets handleMargin = GetLayoutChildMarginOrZero(handleEntity);
        Insets scrollMargin = GetLayoutChildMarginOrZero(scrollObjectEntity);

        ImRect viewportContentRectLocal = RectTransformMath.Inset(viewportRectLocal, viewportPadding);
        ImRect trackContentRectLocal = RectTransformMath.Inset(trackRectLocal, trackPadding);
        trackContentRectLocal = RectTransformMath.Inset(trackContentRectLocal, handleMargin);

        ImRect scrollBoundsRectLocal = ExpandRect(scrollObjectRectLocal, scrollMargin);

        float overflowY = scrollBoundsRectLocal.Height - viewportContentRectLocal.Height;
        float overflowX = scrollBoundsRectLocal.Width - viewportContentRectLocal.Width;

        bool hasVertical = overflowY > 0.01f;
        bool hasHorizontal = overflowX > 0.01f;
        if (!hasVertical && !hasHorizontal)
        {
            return false;
        }

        bool useVertical = hasVertical && (!hasHorizontal || overflowY >= overflowX);
        float overflow = useVertical ? overflowY : overflowX;
        float handleLength = useVertical ? handleSize.Y : handleSize.X;
        float trackLength = useVertical ? trackContentRectLocal.Height : trackContentRectLocal.Width;
        float travel = trackLength - handleLength;

        if (overflow <= 0.0001f)
        {
            return false;
        }

        metrics = new ScrollDragMetrics(
            handleEntity,
            trackEntity,
            scrollObjectEntity,
            viewportEntity,
            useVertical,
            trackContentRectLocal,
            handleRectLocal,
            viewportContentRectLocal,
            scrollObjectRectLocal,
            scrollBoundsRectLocal,
            scrollObjectPivot01,
            scrollObjectSize,
            scrollMargin,
            overflow,
            travel,
            handleLength);

        return true;
    }

    private void TryApplyScrollDrag(in ScrollDragMetrics metrics, Vector2 pointerWorld)
    {
        if (!TryGetParentLocalPointFromWorldEcs(metrics.TrackEntity, pointerWorld, out Vector2 pointerLocalTrack))
        {
            return;
        }

        float travel = metrics.Travel;
        if (travel < 0f)
        {
            travel = 0f;
        }

        float desiredMin;
        float trackMin;
        if (metrics.UseVertical)
        {
            desiredMin = pointerLocalTrack.Y - _runtimeScrollDragGrabOffset;
            trackMin = metrics.TrackRectLocal.Y;
        }
        else
        {
            desiredMin = pointerLocalTrack.X - _runtimeScrollDragGrabOffset;
            trackMin = metrics.TrackRectLocal.X;
        }

        if (desiredMin < trackMin)
        {
            desiredMin = trackMin;
        }
        if (desiredMin > trackMin + travel)
        {
            desiredMin = trackMin + travel;
        }

        float t = travel <= 0.0001f ? 0f : (desiredMin - trackMin) / travel;
        t = Math.Clamp(t, 0f, 1f);

        float desiredScrollMin;
        if (metrics.UseVertical)
        {
            desiredScrollMin = metrics.ViewportRectLocal.Y - t * metrics.Overflow;
            var desiredBounds = new ImRect(metrics.ScrollBoundsRectLocal.X, desiredScrollMin, metrics.ScrollBoundsRectLocal.Width, metrics.ScrollBoundsRectLocal.Height);
            var desiredRect = RectTransformMath.Inset(desiredBounds, metrics.ScrollObjectMargin);
            Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(desiredRect, metrics.ScrollObjectPivot01);
            _ = TrySetTransformPivotPositionNoUndo(metrics.ScrollObjectEntity, pivotPos);
        }
        else
        {
            desiredScrollMin = metrics.ViewportRectLocal.X - t * metrics.Overflow;
            var desiredBounds = new ImRect(desiredScrollMin, metrics.ScrollBoundsRectLocal.Y, metrics.ScrollBoundsRectLocal.Width, metrics.ScrollBoundsRectLocal.Height);
            var desiredRect = RectTransformMath.Inset(desiredBounds, metrics.ScrollObjectMargin);
            Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(desiredRect, metrics.ScrollObjectPivot01);
            _ = TrySetTransformPivotPositionNoUndo(metrics.ScrollObjectEntity, pivotPos);
        }
    }

    private bool TrySetTransformPivotPositionNoUndo(EntityId entity, Vector2 pivotPos)
    {
        if (!_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle any) || !any.IsValid)
        {
            return false;
        }

        var handle = new TransformComponentHandle(any.Index, any.Generation);
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, handle);
        if (!transform.IsAlive)
        {
            return false;
        }

        transform.Position = pivotPos;
        return true;
    }

    internal void EndFrameClearRuntimeTriggers()
    {
        for (int i = 0; i < _runtimeTriggerResets.Count; i++)
        {
            TriggerReset reset = _runtimeTriggerResets[i];
            EntityId storeEntity = _world.GetEntityByStableId(reset.StoreStableId);
            if (storeEntity.IsNull)
            {
                continue;
            }

            WritePrefabInstanceBoolOverrideNoUndo(storeEntity, reset.VariableId, value: false);
        }

        _runtimeTriggerResets.Clear();
    }

    private void UpdateChildHover(EntityId activePrefabEntity, uint hoveredLeafStableId)
    {
        _runtimeHoveredAncestorStableIds.Clear();

        if (hoveredLeafStableId != 0)
        {
            EntityId hoveredEntity = _world.GetEntityByStableId(hoveredLeafStableId);
            if (!hoveredEntity.IsNull)
            {
                EntityId current = hoveredEntity;
                int guard = 512;
                while (!current.IsNull && guard-- > 0)
                {
                    EntityId parent = _world.GetParent(current);
                    if (parent.IsNull)
                    {
                        break;
                    }

                    UiNodeType parentType = _world.GetNodeType(parent);
                    if (parentType == UiNodeType.Prefab)
                    {
                        break;
                    }

                    uint stableId = _world.GetStableId(parent);
                    if (stableId != 0)
                    {
                        _runtimeHoveredAncestorStableIds.Add(stableId);
                    }

                    current = parent;
                }
            }
        }

        // Clear leaves that are no longer child-hovered.
        for (int i = 0; i < _runtimePrevHoveredAncestorStableIds.Count; i++)
        {
            uint prevStableId = _runtimePrevHoveredAncestorStableIds[i];
            if (ContainsStableId(_runtimeHoveredAncestorStableIds, prevStableId))
            {
                continue;
            }

            EntityId entity = _world.GetEntityByStableId(prevStableId);
            if (entity.IsNull)
            {
                continue;
            }

            ApplyChildHover(entity, activePrefabEntity, isHovered: false, fireEnterExit: true, entering: false);
        }

        // Apply/enter for newly hovered.
        for (int i = 0; i < _runtimeHoveredAncestorStableIds.Count; i++)
        {
            uint stableId = _runtimeHoveredAncestorStableIds[i];
            bool wasHovered = ContainsStableId(_runtimePrevHoveredAncestorStableIds, stableId);

            EntityId entity = _world.GetEntityByStableId(stableId);
            if (entity.IsNull)
            {
                continue;
            }

            ApplyChildHover(entity, activePrefabEntity, isHovered: true, fireEnterExit: !wasHovered, entering: true);
        }

        _runtimePrevHoveredAncestorStableIds.Clear();
        for (int i = 0; i < _runtimeHoveredAncestorStableIds.Count; i++)
        {
            _runtimePrevHoveredAncestorStableIds.Add(_runtimeHoveredAncestorStableIds[i]);
        }
    }

    private static bool ContainsStableId(List<uint> list, uint stableId)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == stableId)
            {
                return true;
            }
        }
        return false;
    }

    private void ApplyLeafHover(EntityId listenerEntity, EntityId activePrefabEntity, bool isHovered, bool fireEnterExit, bool entering)
    {
        if (!_world.TryGetComponent(listenerEntity, EventListenerComponent.Api.PoolIdConst, out AnyComponentHandle any) || !any.IsValid)
        {
            return;
        }

        var handle = new EventListenerComponentHandle(any.Index, any.Generation);
        var view = EventListenerComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return;
        }

        ushort hoverVarId = ClampVariableId(view.HoverVarId);
        if (hoverVarId != 0)
        {
            WriteListenerBool(activePrefabEntity, listenerEntity, hoverVarId, isHovered);
        }

        if (fireEnterExit)
        {
            ushort triggerVarId = entering ? ClampVariableId(view.HoverEnterTriggerId) : ClampVariableId(view.HoverExitTriggerId);
            if (triggerVarId != 0)
            {
                FireListenerTrigger(activePrefabEntity, listenerEntity, triggerVarId);
            }
        }
    }

    private void ApplyChildHover(EntityId listenerEntity, EntityId activePrefabEntity, bool isHovered, bool fireEnterExit, bool entering)
    {
        if (!_world.TryGetComponent(listenerEntity, EventListenerComponent.Api.PoolIdConst, out AnyComponentHandle any) || !any.IsValid)
        {
            return;
        }

        var handle = new EventListenerComponentHandle(any.Index, any.Generation);
        var view = EventListenerComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return;
        }

        ushort childHoverVarId = ClampVariableId(view.ChildHoverVarId);
        if (childHoverVarId != 0)
        {
            WriteListenerBool(activePrefabEntity, listenerEntity, childHoverVarId, isHovered);
        }

        if (fireEnterExit)
        {
            ushort triggerVarId = entering ? ClampVariableId(view.ChildHoverEnterTriggerId) : ClampVariableId(view.ChildHoverExitTriggerId);
            if (triggerVarId != 0)
            {
                FireListenerTrigger(activePrefabEntity, listenerEntity, triggerVarId);
            }
        }
    }

    private void ApplyPress(EntityId listenerEntity, EntityId activePrefabEntity)
    {
        if (!_world.TryGetComponent(listenerEntity, EventListenerComponent.Api.PoolIdConst, out AnyComponentHandle any) || !any.IsValid)
        {
            return;
        }

        var handle = new EventListenerComponentHandle(any.Index, any.Generation);
        var view = EventListenerComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return;
        }

        ushort pressTriggerId = ClampVariableId(view.PressTriggerId);
        if (pressTriggerId != 0)
        {
            FireListenerTrigger(activePrefabEntity, listenerEntity, pressTriggerId);
        }
    }

    private void ApplyRelease(EntityId listenerEntity, EntityId activePrefabEntity, bool isClick)
    {
        if (!_world.TryGetComponent(listenerEntity, EventListenerComponent.Api.PoolIdConst, out AnyComponentHandle any) || !any.IsValid)
        {
            return;
        }

        var handle = new EventListenerComponentHandle(any.Index, any.Generation);
        var view = EventListenerComponent.Api.FromHandle(_propertyWorld, handle);
        if (!view.IsAlive)
        {
            return;
        }

        ushort releaseTriggerId = ClampVariableId(view.ReleaseTriggerId);
        if (releaseTriggerId != 0)
        {
            FireListenerTrigger(activePrefabEntity, listenerEntity, releaseTriggerId);
        }

        ushort clickTriggerId = ClampVariableId(view.ClickTriggerId);
        if (isClick && clickTriggerId != 0)
        {
            FireListenerTrigger(activePrefabEntity, listenerEntity, clickTriggerId);
        }
    }

    private void FireListenerTrigger(EntityId activePrefabEntity, EntityId listenerEntity, ushort variableId)
    {
        EntityId store = FindVariableStoreForListener(activePrefabEntity, listenerEntity);
        if (store.IsNull)
        {
            return;
        }

        uint storeStableId = _world.GetStableId(store);
        if (storeStableId == 0)
        {
            return;
        }

        if (!WritePrefabInstanceBoolOverrideNoUndo(store, variableId, value: true))
        {
            return;
        }

        _runtimeTriggerResets.Add(new TriggerReset(storeStableId, variableId));
    }

    private void WriteListenerBool(EntityId activePrefabEntity, EntityId listenerEntity, ushort variableId, bool value)
    {
        EntityId store = FindVariableStoreForListener(activePrefabEntity, listenerEntity);
        if (store.IsNull)
        {
            return;
        }

        _ = WritePrefabInstanceBoolOverrideNoUndo(store, variableId, value);
    }

    private EntityId FindVariableStoreForListener(EntityId activePrefabEntity, EntityId listenerEntity)
    {
        EntityId current = listenerEntity;
        int guard = 512;
        while (!current.IsNull && guard-- > 0)
        {
            UiNodeType type = _world.GetNodeType(current);
            if (type == UiNodeType.PrefabInstance)
            {
                // Expanded prefab-instance proxy roots are tagged as PrefabInstance nodes but do not carry
                // PrefabInstanceComponent (they are editor-only scaffolding). Skip those and keep walking
                // until we hit the real instance entity that owns variable overrides.
                if (_world.TryGetComponent(current, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) &&
                    instanceAny.IsValid)
                {
                    return current;
                }
            }

            EntityId parent = _world.GetParent(current);
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

    private bool WritePrefabInstanceBoolOverrideNoUndo(EntityId storeEntity, ushort variableId, bool value)
    {
        if (variableId == 0)
        {
            return false;
        }

        if (!_world.TryGetComponent(storeEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return false;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
        if (!instance.IsAlive)
        {
            return false;
        }

        ushort count = instance.ValueCount;
        int n = Math.Min(count, (ushort)PrefabInstanceComponent.MaxVariables);
        Span<ushort> ids = instance.VariableIdSpan();
        Span<PropertyValue> values = instance.ValueSpan();
        ulong overrideMask = instance.OverrideMask;

        int index = -1;
        for (int i = 0; i < n; i++)
        {
            if (ids[i] == variableId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            if (n >= PrefabInstanceComponent.MaxVariables)
            {
                return false;
            }

            index = n;
            ids[index] = variableId;
            instance.ValueCount = (ushort)(n + 1);
        }

        values[index] = PropertyValue.FromBool(value);
        instance.OverrideMask = overrideMask | (1UL << index);
        return true;
    }

    private readonly struct TriggerReset
    {
        public readonly uint StoreStableId;
        public readonly ushort VariableId;

        public TriggerReset(uint storeStableId, ushort variableId)
        {
            StoreStableId = storeStableId;
            VariableId = variableId;
        }
    }

    private static ushort ClampVariableId(int id)
    {
        if (id <= 0)
        {
            return 0;
        }

        if (id >= ushort.MaxValue)
        {
            return ushort.MaxValue;
        }

        return (ushort)id;
    }

    internal readonly struct RuntimeEventDebug
    {
        public readonly uint HoveredListenerStableId;
        public readonly uint PressedListenerStableId;
        public readonly uint ScrollDragHandleStableId;
        public readonly uint ScrollDragObjectStableId;
        public readonly byte ScrollDragAxis;
        public readonly bool ConsumedPrimaryThisFrame;

        public RuntimeEventDebug(
            uint hoveredListenerStableId,
            uint pressedListenerStableId,
            uint scrollDragHandleStableId,
            uint scrollDragObjectStableId,
            byte scrollDragAxis,
            bool consumedPrimaryThisFrame)
        {
            HoveredListenerStableId = hoveredListenerStableId;
            PressedListenerStableId = pressedListenerStableId;
            ScrollDragHandleStableId = scrollDragHandleStableId;
            ScrollDragObjectStableId = scrollDragObjectStableId;
            ScrollDragAxis = scrollDragAxis;
            ConsumedPrimaryThisFrame = consumedPrimaryThisFrame;
        }
    }

    internal RuntimeEventDebug GetRuntimeEventDebug()
    {
        return new RuntimeEventDebug(
            _runtimeHoveredLeafStableId,
            _runtimePressedLeafStableId,
            _runtimeScrollDragHandleStableId,
            _runtimeScrollDragObjectStableId,
            _runtimeScrollDragAxis,
            _runtimeConsumedPrimaryThisFrame);
    }

    internal EntityId GetRuntimeVariableStoreForListener(EntityId activePrefabEntity, EntityId listenerEntity)
    {
        return FindVariableStoreForListener(activePrefabEntity, listenerEntity);
    }
}
