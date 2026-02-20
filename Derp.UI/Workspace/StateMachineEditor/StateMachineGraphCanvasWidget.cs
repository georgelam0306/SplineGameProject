using System;
using System.Collections.Generic;
using System.Numerics;
using Core;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;
using DerpLib.ImGui.Widgets;
using Pooled.Runtime;
using Property.Runtime;

namespace Derp.UI;

internal static class StateMachineGraphCanvasWidget
{
    private const uint EntryFill = 0xFF2E4B3D;
    private const uint AnyStateFill = 0xFF1E3851;
    private const uint ExitFill = 0xFF4B2626;

    private const uint ActiveStateColorFrom = 0xFF2AC7FF;
    private const uint ActiveStateColorTo = 0xFFFFC857;

    private static readonly List<TransitionHandleDraw> TransitionHandles = new(capacity: 256);
    private const float TransitionHandleRadiusScale = 1.6f;

    public static void Draw(
        UiWorkspace workspace,
        StateMachineDefinitionComponent.ViewProxy def,
        ushort machineId,
        ushort layerId,
        int layerSlotIndex,
        StateMachineEditorState state,
        ImRect rect)
    {
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, 0xFF101010);

        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        var input = Im.Context.Input;

        Span<Vector2> layerPan = def.LayerPanSpan();
        Span<float> layerZoom = def.LayerZoomSpan();

        Vector2 pan = layerPan[layerSlotIndex];
        float zoom = layerZoom[layerSlotIndex];
        if (!float.IsFinite(zoom) || zoom <= 0.01f)
        {
            zoom = 1f;
        }

        Vector2 originScreen = new Vector2(
            rect.X + rect.Width * 0.5f + pan.X,
            rect.Y + rect.Height * 0.5f + pan.Y);

        Vector2 mouseScreen = Im.MousePos;
        Vector2 mouseGraph = ScreenToGraph(mouseScreen, originScreen, zoom);

        HandlePanZoom(state, rect, input, ref pan, ref zoom, mouseScreen, mouseGraph);
        layerPan[layerSlotIndex] = pan;
        layerZoom[layerSlotIndex] = zoom;

        originScreen = new Vector2(
            rect.X + rect.Width * 0.5f + pan.X,
            rect.Y + rect.Height * 0.5f + pan.Y);
        mouseGraph = ScreenToGraph(mouseScreen, originScreen, zoom);

        bool hovered = rect.Contains(mouseScreen);

        if (hovered && input.MouseRightPressed)
        {
            ImContextMenu.Open("sm_graph_context_menu");
            workspace.NotifyInspectorAnimationInteraction();
        }

        if (ImContextMenu.Begin("sm_graph_context_menu"))
        {
            if (ImContextMenu.Item("Add State"))
            {
                AddState(def, layerId, layerSlotIndex, StateMachineDefinitionComponent.StateKind.Timeline, mouseGraph);
            }
            if (ImContextMenu.Item("Add Blend State (1D)"))
            {
                AddState(def, layerId, layerSlotIndex, StateMachineDefinitionComponent.StateKind.Blend1D, mouseGraph);
            }
            if (ImContextMenu.Item("Add Blend State (Additive)"))
            {
                AddState(def, layerId, layerSlotIndex, StateMachineDefinitionComponent.StateKind.BlendAdditive, mouseGraph);
            }

            ImContextMenu.Separator();

            bool snapping = state.SnappingEnabled;
            if (ImContextMenu.Item(snapping ? "Snapping   ✓" : "Snapping"))
            {
                state.SnappingEnabled = !state.SnappingEnabled;
            }

            ImContextMenu.Separator();
            ImContextMenu.ItemDisabled("Delete Layer");

            ImContextMenu.End();
        }

        Im.PushClipRect(rect);

        UpdateInteraction(workspace, def, machineId, layerId, layerSlotIndex, state, rect, originScreen, zoom, mouseGraph, mouseScreen);

        RuntimeLayerViz runtimeViz = GetRuntimeLayerViz(workspace, layerSlotIndex);

        DrawTransitions(def, layerId, layerSlotIndex, state, originScreen, zoom, runtimeViz);
        DrawConnectPreview(state, originScreen, zoom, mouseGraph);
        DrawTimelineDropPreview(workspace, def, machineId, layerId, layerSlotIndex, rect, originScreen, zoom, mouseGraph);
        DrawNodes(def, layerId, layerSlotIndex, state, originScreen, zoom, runtimeViz);
        DrawHoverPortDot(def, layerId, layerSlotIndex, state, originScreen, zoom, mouseGraph);
        DrawMarquee(state, originScreen, zoom);

        Im.PopClipRect();
    }

    private readonly struct RuntimeLayerViz
    {
        public readonly bool HasData;
        public readonly ushort ActiveStateId;
        public readonly ushort TransitionId;
        public readonly ushort TransitionFromStateId;
        public readonly ushort TransitionToStateId;
        public readonly float TransitionT01;

        public RuntimeLayerViz(
            bool hasData,
            ushort activeStateId,
            ushort transitionId,
            ushort transitionFromStateId,
            ushort transitionToStateId,
            float transitionT01)
        {
            HasData = hasData;
            ActiveStateId = activeStateId;
            TransitionId = transitionId;
            TransitionFromStateId = transitionFromStateId;
            TransitionToStateId = transitionToStateId;
            TransitionT01 = transitionT01;
        }
    }

    private static RuntimeLayerViz GetRuntimeLayerViz(UiWorkspace workspace, int layerSlotIndex)
    {
        if (!workspace.IsRuntimeMode)
        {
            return default;
        }

        if (!workspace.TryGetActivePrefabEntity(out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            return default;
        }

        UiWorld world = workspace.World;
        World propertyWorld = workspace.PropertyWorld;

        if (!world.TryGetComponent(prefabEntity, StateMachineRuntimeComponent.Api.PoolIdConst, out AnyComponentHandle runtimeAny) || !runtimeAny.IsValid)
        {
            return default;
        }

        var runtimeHandle = new StateMachineRuntimeComponentHandle(runtimeAny.Index, runtimeAny.Generation);
        var runtime = StateMachineRuntimeComponent.Api.FromHandle(propertyWorld, runtimeHandle);
        if (!runtime.IsAlive)
        {
            return default;
        }

        Span<ushort> current = runtime.LayerCurrentStateIdSpan();
        Span<ushort> transitionId = runtime.LayerTransitionIdSpan();
        Span<ushort> transitionFrom = runtime.LayerTransitionFromStateIdSpan();
        Span<ushort> transitionTo = runtime.LayerTransitionToStateIdSpan();
        Span<uint> transitionTime = runtime.LayerTransitionTimeUsSpan();
        Span<uint> transitionDur = runtime.LayerTransitionDurationUsSpan();

        if ((uint)layerSlotIndex >= (uint)current.Length)
        {
            return default;
        }

        ushort activeStateId = current[layerSlotIndex];
        ushort trId = transitionId[layerSlotIndex];
        ushort fromStateId = transitionFrom[layerSlotIndex];
        ushort toStateId = transitionTo[layerSlotIndex];
        uint timeUs = transitionTime[layerSlotIndex];
        uint durationUs = transitionDur[layerSlotIndex];

        float t01 = 0f;
        if (trId != 0 && durationUs > 0)
        {
            t01 = (float)timeUs / durationUs;
            if (!float.IsFinite(t01))
            {
                t01 = 0f;
            }
            t01 = Math.Clamp(t01, 0f, 1f);
        }

        return new RuntimeLayerViz(
            hasData: true,
            activeStateId: activeStateId,
            transitionId: trId,
            transitionFromStateId: fromStateId,
            transitionToStateId: toStateId,
            transitionT01: t01);
    }

    private static uint LerpColor(uint from, uint to, float t01)
    {
        t01 = Math.Clamp(t01, 0f, 1f);

        int a0 = (byte)(from >> 24);
        int b0 = (byte)(from >> 16);
        int g0 = (byte)(from >> 8);
        int r0 = (byte)from;

        int a1 = (byte)(to >> 24);
        int b1 = (byte)(to >> 16);
        int g1 = (byte)(to >> 8);
        int r1 = (byte)to;

        int a = (int)MathF.Round(a0 + (a1 - a0) * t01);
        int b = (int)MathF.Round(b0 + (b1 - b0) * t01);
        int g = (int)MathF.Round(g0 + (g1 - g0) * t01);
        int r = (int)MathF.Round(r0 + (r1 - r0) * t01);

        return (uint)((a << 24) | (b << 16) | (g << 8) | r);
    }

    private static uint GetActiveHighlightColor(in RuntimeLayerViz viz)
    {
        if (!viz.HasData)
        {
            return 0;
        }

        if (viz.TransitionId != 0)
        {
            return LerpColor(ActiveStateColorFrom, ActiveStateColorTo, viz.TransitionT01);
        }

        return ActiveStateColorTo;
    }

    private static void HandlePanZoom(
        StateMachineEditorState state,
        ImRect rect,
        in ImInput input,
        ref Vector2 pan,
        ref float zoom,
        Vector2 mouseScreen,
        Vector2 mouseGraph)
    {
        if (!rect.Contains(mouseScreen))
        {
            if (state.IsPanning && !input.MouseMiddleDown)
            {
                state.IsPanning = false;
            }
            return;
        }

        if (input.ScrollDelta != 0f)
        {
            float factor = MathF.Pow(1.15f, input.ScrollDelta);
            float newZoom = Math.Clamp(zoom * factor, 0.15f, 4.0f);
            if (newZoom != zoom)
            {
                zoom = newZoom;

                Vector2 desiredOrigin = new Vector2(rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f);
                Vector2 originScreen = desiredOrigin + pan;

                // Keep the point under cursor fixed in graph space.
                Vector2 anchorGraph = ScreenToGraph(mouseScreen, originScreen, zoom / factor);
                Vector2 newOrigin = mouseScreen - anchorGraph * zoom;
                pan = newOrigin - desiredOrigin;
            }
        }

        if (input.MouseMiddlePressed)
        {
            state.IsPanning = true;
            state.PanStartMouseScreen = mouseScreen;
            state.PanStartValue = pan;
        }

        if (state.IsPanning)
        {
            if (!input.MouseMiddleDown)
            {
                state.IsPanning = false;
            }
            else
            {
                Vector2 delta = mouseScreen - state.PanStartMouseScreen;
                pan = state.PanStartValue + delta;
            }
        }
    }

    private static int CountStatesInLayer(StateMachineDefinitionComponent.ViewProxy def, ushort layerId)
    {
        int count = 0;
        int total = def.StateCount;
        for (int i = 0; i < total; i++)
        {
            if (def.StateLayerId[i] == layerId)
            {
                count++;
            }
        }
        return count;
    }

    private static void AddState(
        StateMachineDefinitionComponent.ViewProxy def,
        ushort layerId,
        int layerSlotIndex,
        StateMachineDefinitionComponent.StateKind kind,
        Vector2 pos)
    {
        int stateSlot = def.StateCount;
        if (stateSlot >= StateMachineDefinitionComponent.MaxStates)
        {
            return;
        }

        int countInLayer = CountStatesInLayer(def, layerId) + 1;
        string name = $"State {countInLayer}";
        if (kind != StateMachineDefinitionComponent.StateKind.Timeline)
        {
            name = kind == StateMachineDefinitionComponent.StateKind.Blend1D ? $"Blend {countInLayer}" : $"Blend Add {countInLayer}";
        }

        ushort id = def.LayerNextStateId[layerSlotIndex];
        if (id == 0)
        {
            id = 1;
        }

        Span<ushort> layerNextStateId = def.LayerNextStateIdSpan();
        layerNextStateId[layerSlotIndex] = (ushort)(id + 1);

        Span<ushort> stateId = def.StateIdSpan();
        Span<ushort> stateLayerId = def.StateLayerIdSpan();
        Span<StringHandle> stateName = def.StateNameSpan();
        Span<byte> stateKind = def.StateKindValueSpan();
        Span<int> stateTimelineId = def.StateTimelineIdSpan();
        Span<float> statePlaybackSpeed = def.StatePlaybackSpeedSpan();
        Span<ushort> stateBlendParam = def.StateBlendParameterVariableIdSpan();
        Span<ushort> stateBlendChildStart = def.StateBlendChildStartSpan();
        Span<byte> stateBlendChildCount = def.StateBlendChildCountSpan();
        Span<Vector2> statePos = def.StatePosSpan();

        stateId[stateSlot] = id;
        stateLayerId[stateSlot] = layerId;
        stateName[stateSlot] = name;
        stateKind[stateSlot] = (byte)kind;
        stateTimelineId[stateSlot] = 0;
        statePlaybackSpeed[stateSlot] = 1f;
        stateBlendParam[stateSlot] = 0;
        stateBlendChildStart[stateSlot] = def.BlendChildCount;
        stateBlendChildCount[stateSlot] = 0;
        statePos[stateSlot] = pos;
        def.StateCount = (ushort)(stateSlot + 1);
    }

    private static int AddTimelineState(
        StateMachineDefinitionComponent.ViewProxy def,
        ushort layerId,
        int layerSlotIndex,
        int timelineId,
        string timelineName,
        Vector2 pos)
    {
        int stateSlot = def.StateCount;
        if (stateSlot >= StateMachineDefinitionComponent.MaxStates)
        {
            return 0;
        }

        ushort id = def.LayerNextStateId[layerSlotIndex];
        if (id == 0)
        {
            id = 1;
        }

        Span<ushort> layerNextStateId = def.LayerNextStateIdSpan();
        layerNextStateId[layerSlotIndex] = (ushort)(id + 1);

        Span<ushort> stateId = def.StateIdSpan();
        Span<ushort> stateLayerId = def.StateLayerIdSpan();
        Span<StringHandle> stateName = def.StateNameSpan();
        Span<byte> stateKind = def.StateKindValueSpan();
        Span<int> stateTimelineId = def.StateTimelineIdSpan();
        Span<float> statePlaybackSpeed = def.StatePlaybackSpeedSpan();
        Span<ushort> stateBlendParam = def.StateBlendParameterVariableIdSpan();
        Span<ushort> stateBlendChildStart = def.StateBlendChildStartSpan();
        Span<byte> stateBlendChildCount = def.StateBlendChildCountSpan();
        Span<Vector2> statePos = def.StatePosSpan();

        stateId[stateSlot] = id;
        stateLayerId[stateSlot] = layerId;
        stateName[stateSlot] = timelineName;
        stateKind[stateSlot] = (byte)StateMachineDefinitionComponent.StateKind.Timeline;
        stateTimelineId[stateSlot] = timelineId;
        statePlaybackSpeed[stateSlot] = 1f;
        stateBlendParam[stateSlot] = 0;
        stateBlendChildStart[stateSlot] = def.BlendChildCount;
        stateBlendChildCount[stateSlot] = 0;
        statePos[stateSlot] = pos;
        def.StateCount = (ushort)(stateSlot + 1);
        return id;
    }

    private static bool TryFindStateSlot(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int stateId, out int stateSlot)
    {
        stateSlot = -1;
        if (stateId <= 0)
        {
            return false;
        }

        int total = def.StateCount;
        for (int i = 0; i < total; i++)
        {
            if (def.StateLayerId[i] == layerId && def.StateId[i] == (ushort)stateId)
            {
                stateSlot = i;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindTransitionSlot(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int transitionId, out int transitionSlot)
    {
        transitionSlot = -1;
        if (transitionId <= 0)
        {
            return false;
        }

        int total = def.TransitionCount;
        for (int i = 0; i < total; i++)
        {
            if (def.TransitionLayerId[i] == layerId && def.TransitionId[i] == (ushort)transitionId)
            {
                transitionSlot = i;
                return true;
            }
        }

        return false;
    }

    private static void DeleteTransitionSlot(StateMachineDefinitionComponent.ViewProxy def, int transitionSlot)
    {
        int last = def.TransitionCount - 1;
        if (transitionSlot < 0 || transitionSlot > last)
        {
            return;
        }

        StateMachineDefinitionOps.RemoveAllTransitionConditions(def, transitionSlot);

        Span<ushort> transitionId = def.TransitionIdSpan();
        Span<ushort> transitionLayerId = def.TransitionLayerIdSpan();
        Span<byte> transitionFromKind = def.TransitionFromKindValueSpan();
        Span<ushort> transitionFromStateId = def.TransitionFromStateIdSpan();
        Span<byte> transitionToKind = def.TransitionToKindValueSpan();
        Span<ushort> transitionToStateId = def.TransitionToStateIdSpan();
        Span<float> transitionDuration = def.TransitionDurationSecondsSpan();
        Span<byte> transitionHasExitTime = def.TransitionHasExitTimeValueSpan();
        Span<float> transitionExitTime01 = def.TransitionExitTime01Span();
        Span<ushort> transitionConditionStart = def.TransitionConditionStartSpan();
        Span<byte> transitionConditionCount = def.TransitionConditionCountSpan();

        if (transitionSlot != last)
        {
            transitionId[transitionSlot] = transitionId[last];
            transitionLayerId[transitionSlot] = transitionLayerId[last];
            transitionFromKind[transitionSlot] = transitionFromKind[last];
            transitionFromStateId[transitionSlot] = transitionFromStateId[last];
            transitionToKind[transitionSlot] = transitionToKind[last];
            transitionToStateId[transitionSlot] = transitionToStateId[last];
            transitionDuration[transitionSlot] = transitionDuration[last];
            transitionHasExitTime[transitionSlot] = transitionHasExitTime[last];
            transitionExitTime01[transitionSlot] = transitionExitTime01[last];
            transitionConditionStart[transitionSlot] = transitionConditionStart[last];
            transitionConditionCount[transitionSlot] = transitionConditionCount[last];
        }

        transitionId[last] = 0;
        transitionLayerId[last] = 0;
        transitionFromKind[last] = 0;
        transitionFromStateId[last] = 0;
        transitionToKind[last] = 0;
        transitionToStateId[last] = 0;
        transitionDuration[last] = 0f;
        transitionHasExitTime[last] = 0;
        transitionExitTime01[last] = 0f;
        transitionConditionStart[last] = 0;
        transitionConditionCount[last] = 0;

        def.TransitionCount = (ushort)last;
    }

    private static void DeleteTransitionById(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int transitionId)
    {
        if (!TryFindTransitionSlot(def, layerId, transitionId, out int transitionSlot))
        {
            return;
        }

        DeleteTransitionSlot(def, transitionSlot);
    }

    private static void RemoveTransitionsForState(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int stateId)
    {
        int total = def.TransitionCount;
        for (int i = 0; i < total; i++)
        {
            if (def.TransitionLayerId[i] != layerId)
            {
                continue;
            }

            StateMachineDefinitionComponent.TransitionFromKind fromKind = (StateMachineDefinitionComponent.TransitionFromKind)def.TransitionFromKindValue[i];
            StateMachineDefinitionComponent.TransitionToKind toKind = (StateMachineDefinitionComponent.TransitionToKind)def.TransitionToKindValue[i];

            bool fromMatches = fromKind == StateMachineDefinitionComponent.TransitionFromKind.State && def.TransitionFromStateId[i] == (ushort)stateId;
            bool toMatches = toKind == StateMachineDefinitionComponent.TransitionToKind.State && def.TransitionToStateId[i] == (ushort)stateId;
            if (!fromMatches && !toMatches)
            {
                continue;
            }

            DeleteTransitionSlot(def, i);
            total = def.TransitionCount;
            i--;
        }
    }

    private static void DeleteStateSlot(StateMachineDefinitionComponent.ViewProxy def, int stateSlot)
    {
        int last = def.StateCount - 1;
        if (stateSlot < 0 || stateSlot > last)
        {
            return;
        }

        Span<ushort> stateId = def.StateIdSpan();
        Span<ushort> stateLayerId = def.StateLayerIdSpan();
        Span<StringHandle> stateName = def.StateNameSpan();
        Span<byte> stateKind = def.StateKindValueSpan();
        Span<int> stateTimelineId = def.StateTimelineIdSpan();
        Span<float> statePlaybackSpeed = def.StatePlaybackSpeedSpan();
        Span<Vector2> statePos = def.StatePosSpan();

        if (stateSlot != last)
        {
            stateId[stateSlot] = stateId[last];
            stateLayerId[stateSlot] = stateLayerId[last];
            stateName[stateSlot] = stateName[last];
            stateKind[stateSlot] = stateKind[last];
            stateTimelineId[stateSlot] = stateTimelineId[last];
            statePlaybackSpeed[stateSlot] = statePlaybackSpeed[last];
            statePos[stateSlot] = statePos[last];
        }

        stateId[last] = 0;
        stateLayerId[last] = 0;
        stateName[last] = StringHandle.Invalid;
        stateKind[last] = 0;
        stateTimelineId[last] = 0;
        statePlaybackSpeed[last] = 0f;
        statePos[last] = default;

        def.StateCount = (ushort)last;
    }

    private static bool DeleteSelectedStates(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, StateMachineEditorState state)
    {
        if (state.SelectedNodes.Count == 0)
        {
            return false;
        }

        bool anyDeleted = false;
        for (int i = state.SelectedNodes.Count - 1; i >= 0; i--)
        {
            var node = state.SelectedNodes[i];
            if (node.Kind != StateMachineGraphNodeKind.State)
            {
                continue;
            }

            int stateId = node.StateId;
            if (!TryFindStateSlot(def, layerId, stateId, out int stateSlot))
            {
                continue;
            }

            RemoveTransitionsForState(def, layerId, stateId);
            DeleteStateSlot(def, stateSlot);
            anyDeleted = true;
        }

        if (anyDeleted)
        {
            state.SelectedNodes.Clear();
            state.SelectedNodeDragStartPositions.Clear();
            state.SelectedTransitionId = 0;
            state.ConnectDrag = default;
        }

        return anyDeleted;
    }

    private static bool NodeAllowsOutgoing(StateMachineGraphNodeKind kind)
    {
        return kind == StateMachineGraphNodeKind.State ||
            kind == StateMachineGraphNodeKind.Entry ||
            kind == StateMachineGraphNodeKind.AnyState;
    }

    private static bool NodeAllowsIncoming(StateMachineGraphNodeKind kind)
    {
        return kind == StateMachineGraphNodeKind.State ||
            kind == StateMachineGraphNodeKind.Exit;
    }

    private static Vector2 GraphToScreen(Vector2 graph, Vector2 originScreen, float zoom)
    {
        return originScreen + graph * zoom;
    }

    private static Vector2 ScreenToGraph(Vector2 screen, Vector2 originScreen, float zoom)
    {
        if (!float.IsFinite(zoom) || MathF.Abs(zoom) <= 0.000001f)
        {
            return Vector2.Zero;
        }

        return (screen - originScreen) / zoom;
    }

    private static Vector2 GetNodeHalfSize(StateMachineGraphNodeKind kind)
    {
        if (kind == StateMachineGraphNodeKind.State)
        {
            return new Vector2(StateMachineEditorLayout.StateNodeWidth * 0.5f, StateMachineEditorLayout.StateNodeHeight * 0.5f);
        }

        return new Vector2(StateMachineEditorLayout.SpecialNodeWidth * 0.5f, StateMachineEditorLayout.SpecialNodeHeight * 0.5f);
    }

    private static ImRect GetNodeRectGraph(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineGraphNodeRef node)
    {
        Vector2 pos = GetNodePosGraph(def, layerId, layerSlotIndex, node);
        Vector2 half = GetNodeHalfSize(node.Kind);
        return new ImRect(pos.X - half.X, pos.Y - half.Y, half.X * 2f, half.Y * 2f);
    }

    private static Vector2 GetNodePosGraph(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineGraphNodeRef node)
    {
        switch (node.Kind)
        {
            case StateMachineGraphNodeKind.Entry:
                return def.LayerEntryPos[layerSlotIndex];
            case StateMachineGraphNodeKind.AnyState:
                return def.LayerAnyStatePos[layerSlotIndex];
            case StateMachineGraphNodeKind.Exit:
                return def.LayerExitPos[layerSlotIndex];
            case StateMachineGraphNodeKind.State:
            default:
            {
                if (TryFindStateSlot(def, layerId, node.StateId, out int slot))
                {
                    return def.StatePos[slot];
                }
                return Vector2.Zero;
            }
        }
    }

    private static void SetNodePosGraph(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineGraphNodeRef node, Vector2 pos)
    {
        Span<Vector2> entryPos = def.LayerEntryPosSpan();
        Span<Vector2> anyPos = def.LayerAnyStatePosSpan();
        Span<Vector2> exitPos = def.LayerExitPosSpan();
        Span<Vector2> statePos = def.StatePosSpan();

        switch (node.Kind)
        {
            case StateMachineGraphNodeKind.Entry:
                entryPos[layerSlotIndex] = pos;
                return;
            case StateMachineGraphNodeKind.AnyState:
                anyPos[layerSlotIndex] = pos;
                return;
            case StateMachineGraphNodeKind.Exit:
                exitPos[layerSlotIndex] = pos;
                return;
            case StateMachineGraphNodeKind.State:
            default:
            {
                if (TryFindStateSlot(def, layerId, node.StateId, out int slot))
                {
                    statePos[slot] = pos;
                }
                return;
            }
        }
    }

    private static Vector2 GetBorderPointToward(Vector2 center, Vector2 toward, Vector2 half)
    {
        Vector2 dir = toward - center;
        if (dir.LengthSquared() <= 0.0001f)
        {
            return center;
        }

        float scaleX = MathF.Abs(dir.X) > 0.0001f ? half.X / MathF.Abs(dir.X) : float.MaxValue;
        float scaleY = MathF.Abs(dir.Y) > 0.0001f ? half.Y / MathF.Abs(dir.Y) : float.MaxValue;
        float scale = MathF.Min(scaleX, scaleY);
        return center + dir * scale;
    }

    private static void DrawTransitionArrow(Vector2 handleCenter, Vector2 startScreen, Vector2 endScreen, float radius, bool selected)
    {
        Vector2 dir = endScreen - startScreen;
        float len2 = dir.LengthSquared();
        if (len2 <= 0.0001f)
        {
            return;
        }

        float invLen = 1.0f / MathF.Sqrt(len2);
        dir *= invLen;
        Vector2 perp = new Vector2(-dir.Y, dir.X);

        float tipOffset = radius * 0.35f;
        float baseOffset = radius * 0.30f;
        float halfWidth = radius * 0.35f;

        Vector2 tip = handleCenter + dir * tipOffset;
        Vector2 baseCenter = handleCenter - dir * baseOffset;
        Vector2 left = baseCenter + perp * halfWidth;
        Vector2 right = baseCenter - perp * halfWidth;

        uint arrowColor = selected ? 0xFFFFFFFF : ImStyle.WithAlphaF(Im.Style.TextSecondary, 0.95f);
        float thickness = MathF.Max(1.5f, radius * 0.18f);
        Im.DrawLine(tip.X, tip.Y, left.X, left.Y, thickness, arrowColor);
        Im.DrawLine(tip.X, tip.Y, right.X, right.Y, thickness, arrowColor);
    }

    private static void DrawLineWithCenterGap(Vector2 start, Vector2 end, Vector2 center, float gapRadius, float thickness, uint color)
    {
        Vector2 dir = end - start;
        float len2 = dir.LengthSquared();
        if (len2 <= 0.0001f)
        {
            return;
        }

        float invLen = 1.0f / MathF.Sqrt(len2);
        dir *= invLen;

        float fullLen = MathF.Sqrt(len2);
        float maxGap = fullLen * 0.48f;
        float gap = gapRadius;
        if (gap > maxGap)
        {
            gap = maxGap;
        }

        Vector2 a = center - dir * gap;
        Vector2 b = center + dir * gap;

        Im.DrawLine(start.X, start.Y, a.X, a.Y, thickness, color);
        Im.DrawLine(b.X, b.Y, end.X, end.Y, thickness, color);
    }

    private static Vector2 GetTransitionHandleCenter(
        StateMachineDefinitionComponent.ViewProxy def,
        ushort layerId,
        int transitionSlot,
        Vector2 startScreen,
        Vector2 endScreen,
        float handleRadius)
    {
        Vector2 center = (startScreen + endScreen) * 0.5f;

        var fromKind = (StateMachineDefinitionComponent.TransitionFromKind)def.TransitionFromKindValue[transitionSlot];
        var toKind = (StateMachineDefinitionComponent.TransitionToKind)def.TransitionToKindValue[transitionSlot];
        if (fromKind != StateMachineDefinitionComponent.TransitionFromKind.State ||
            toKind != StateMachineDefinitionComponent.TransitionToKind.State)
        {
            return center;
        }

        int fromId = def.TransitionFromStateId[transitionSlot];
        int toId = def.TransitionToStateId[transitionSlot];
        if (fromId == toId)
        {
            return center;
        }

        Vector2 axis = endScreen - startScreen;
        float axisLen2 = axis.LengthSquared();
        if (axisLen2 <= 0.0001f)
        {
            return center;
        }

        float invAxisLen = 1.0f / MathF.Sqrt(axisLen2);
        axis *= invAxisLen;

        int minId = fromId < toId ? fromId : toId;
        int maxId = fromId < toId ? toId : fromId;
        bool directionLowToHigh = fromId < toId;
        int transitionId = def.TransitionId[transitionSlot];

        int totalBetween = 0;
        int directionCount = 0;
        int directionOrdinal = 0;

        int total = def.TransitionCount;
        for (int i = 0; i < total; i++)
        {
            if (def.TransitionLayerId[i] != layerId)
            {
                continue;
            }

            var tFromKind = (StateMachineDefinitionComponent.TransitionFromKind)def.TransitionFromKindValue[i];
            var tToKind = (StateMachineDefinitionComponent.TransitionToKind)def.TransitionToKindValue[i];
            if (tFromKind != StateMachineDefinitionComponent.TransitionFromKind.State ||
                tToKind != StateMachineDefinitionComponent.TransitionToKind.State)
            {
                continue;
            }

            int tFrom = def.TransitionFromStateId[i];
            int tTo = def.TransitionToStateId[i];
            if (tFrom == tTo)
            {
                continue;
            }

            int tMin = tFrom < tTo ? tFrom : tTo;
            int tMax = tFrom < tTo ? tTo : tFrom;
            if (tMin != minId || tMax != maxId)
            {
                continue;
            }

            totalBetween++;

            bool tLowToHigh = tFrom < tTo;
            if (tLowToHigh == directionLowToHigh)
            {
                directionCount++;
                if (def.TransitionId[i] < transitionId)
                {
                    directionOrdinal++;
                }
            }
        }

        if (totalBetween <= 1)
        {
            return center;
        }

        float step = handleRadius * 2.15f;
        int oppositeCount = totalBetween - directionCount;
        if (oppositeCount > 0)
        {
            float offset = (0.75f + directionOrdinal) * step;
            center += axis * offset;
            return center;
        }

        float centered = directionOrdinal - (directionCount - 1) * 0.5f;
        center += axis * (centered * step);

        return center;
    }

	    private static void DrawTransitions(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineEditorState state, Vector2 originScreen, float zoom, in RuntimeLayerViz runtimeViz)
	    {
	        float handleRadius = StateMachineEditorLayout.TransitionHandleRadius * TransitionHandleRadiusScale * zoom;
	        if (!float.IsFinite(handleRadius) || handleRadius <= 0.01f)
	        {
	            handleRadius = StateMachineEditorLayout.TransitionHandleRadius * TransitionHandleRadiusScale;
	        }

        TransitionHandles.Clear();

        uint lineColor = ImStyle.WithAlphaF(Im.Style.Border, 0.8f);
        uint selectedLineColor = Im.Style.Primary;
        uint hoveredLineColor = ImStyle.WithAlphaF(Im.Style.Primary, 0.65f);
        uint handleFill = 0xFF2A2A2A;
        uint handleStroke = ImStyle.WithAlphaF(Im.Style.Border, 0.9f);
        uint handleFillSelected = ImStyle.WithAlphaF(Im.Style.Primary, 0.30f);
        uint handleStrokeSelected = Im.Style.Primary;
        uint handleFillHovered = ImStyle.WithAlphaF(Im.Style.Primary, 0.22f);
        uint handleStrokeHovered = hoveredLineColor;

        bool highlightActiveTransition = runtimeViz.HasData && runtimeViz.TransitionId != 0;
        uint runtimeHighlightColor = GetActiveHighlightColor(runtimeViz);

        ushort entryTargetStateId = def.LayerEntryTargetStateId[layerSlotIndex];
        if (entryTargetStateId != 0 && TryFindStateSlot(def, layerId, entryTargetStateId, out int entrySlot))
        {
            Vector2 fromGraph = def.LayerEntryPos[layerSlotIndex];
            Vector2 toGraph = def.StatePos[entrySlot];
            Vector2 startGraph = GetBorderPointToward(fromGraph, toGraph, GetNodeHalfSize(StateMachineGraphNodeKind.Entry));
            Vector2 endGraph = GetBorderPointToward(toGraph, fromGraph, GetNodeHalfSize(StateMachineGraphNodeKind.State));
            Vector2 startScreen = GraphToScreen(startGraph, originScreen, zoom);
            Vector2 endScreen = GraphToScreen(endGraph, originScreen, zoom);
            Im.DrawLine(startScreen.X, startScreen.Y, endScreen.X, endScreen.Y, 2f, lineColor);
        }

        int total = def.TransitionCount;
        for (int i = 0; i < total; i++)
        {
            if (def.TransitionLayerId[i] != layerId)
            {
                continue;
            }

            var fromKind = (StateMachineDefinitionComponent.TransitionFromKind)def.TransitionFromKindValue[i];
            var toKind = (StateMachineDefinitionComponent.TransitionToKind)def.TransitionToKindValue[i];
            int fromStateId = def.TransitionFromStateId[i];
            int toStateId = def.TransitionToStateId[i];

            if (!TryGetTransitionEndpoints(fromKind, fromStateId, toKind, toStateId, out var fromNode, out var toNode))
            {
                continue;
            }

            Vector2 fromGraph = GetNodePosGraph(def, layerId, layerSlotIndex, fromNode);
            Vector2 toGraph = GetNodePosGraph(def, layerId, layerSlotIndex, toNode);

            Vector2 startGraph = GetBorderPointToward(fromGraph, toGraph, GetNodeHalfSize(fromNode.Kind));
            Vector2 endGraph = GetBorderPointToward(toGraph, fromGraph, GetNodeHalfSize(toNode.Kind));
            Vector2 startScreen = GraphToScreen(startGraph, originScreen, zoom);
            Vector2 endScreen = GraphToScreen(endGraph, originScreen, zoom);

	            int trId = def.TransitionId[i];
	            bool selected = state.SelectedTransitionId == trId;
		            bool hovered = state.HoveredTransitionId == trId;

                Vector2 handleCenter = GetTransitionHandleCenter(def, layerId, i, startScreen, endScreen, handleRadius);

		            bool isRuntimeActive = highlightActiveTransition && runtimeViz.TransitionId == trId;
		            uint c = isRuntimeActive ? runtimeHighlightColor : (selected ? selectedLineColor : (hovered ? hoveredLineColor : lineColor));
		            float lineThickness = isRuntimeActive ? 3.2f : (selected ? 2.6f : 2f);

                if (selected || hovered)
                {
                    DrawLineWithCenterGap(startScreen, endScreen, handleCenter, handleRadius + 2.5f, lineThickness, c);
                }
                else if (isRuntimeActive)
                {
                    DrawLineWithCenterGap(startScreen, endScreen, handleCenter, handleRadius + 2.5f, lineThickness, c);
                }
                else
                {
                    Im.DrawLine(startScreen.X, startScreen.Y, endScreen.X, endScreen.Y, lineThickness, c);
                }

            uint hs = isRuntimeActive ? c : (selected ? handleStrokeSelected : (hovered ? handleStrokeHovered : handleStroke));
            uint hf = isRuntimeActive ? ImStyle.WithAlphaF(c, 0.25f) : (selected ? handleFillSelected : (hovered ? handleFillHovered : handleFill));

            TransitionHandles.Add(new TransitionHandleDraw(trId, handleCenter, startScreen, endScreen, handleRadius, hf, hs, selected));
        }

	        for (int i = 0; i < TransitionHandles.Count; i++)
		        {
		            var h = TransitionHandles[i];
		            Im.DrawCircle(h.Center.X, h.Center.Y, h.Radius, h.Stroke);
		            Im.DrawCircle(h.Center.X, h.Center.Y, MathF.Max(0f, h.Radius - 1.5f), h.Fill);
		            DrawTransitionArrow(h.Center, h.Start, h.End, h.Radius, h.Selected);
		        }
		    }

	    private static bool TryGetTransitionEndpoints(
	        StateMachineDefinitionComponent.TransitionFromKind fromKind,
	        int fromStateId,
	        StateMachineDefinitionComponent.TransitionToKind toKind,
        int toStateId,
        out StateMachineGraphNodeRef fromNode,
        out StateMachineGraphNodeRef toNode)
    {
        fromNode = default;
        toNode = default;

        switch (fromKind)
        {
            case StateMachineDefinitionComponent.TransitionFromKind.Entry:
                fromNode = StateMachineGraphNodeRef.Entry();
                break;
            case StateMachineDefinitionComponent.TransitionFromKind.AnyState:
                fromNode = StateMachineGraphNodeRef.AnyState();
                break;
            case StateMachineDefinitionComponent.TransitionFromKind.State:
            default:
                if (fromStateId <= 0)
                {
                    return false;
                }
                fromNode = StateMachineGraphNodeRef.State(fromStateId);
                break;
        }

        switch (toKind)
        {
            case StateMachineDefinitionComponent.TransitionToKind.Exit:
                toNode = StateMachineGraphNodeRef.Exit();
                break;
            case StateMachineDefinitionComponent.TransitionToKind.State:
            default:
                if (toStateId <= 0)
                {
                    return false;
                }
                toNode = StateMachineGraphNodeRef.State(toStateId);
                break;
        }

	        return true;
	    }

	    private static void DrawNodes(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineEditorState state, Vector2 originScreen, float zoom, in RuntimeLayerViz runtimeViz)
	    {
        uint activeOutlineColor = GetActiveHighlightColor(runtimeViz);
        float activeOutlineThickness = MathF.Max(2.5f, 2.2f * zoom);

	        DrawSpecialNode(def.LayerEntryPos[layerSlotIndex], StateMachineGraphNodeKind.Entry, "Entry".AsSpan(), originScreen, zoom, IsNodeSelected(state, StateMachineGraphNodeRef.Entry()), isActive: false, activeOutlineColor, activeOutlineThickness);
	        DrawSpecialNode(def.LayerAnyStatePos[layerSlotIndex], StateMachineGraphNodeKind.AnyState, "Any State".AsSpan(), originScreen, zoom, IsNodeSelected(state, StateMachineGraphNodeRef.AnyState()), isActive: false, activeOutlineColor, activeOutlineThickness);
	        DrawSpecialNode(def.LayerExitPos[layerSlotIndex], StateMachineGraphNodeKind.Exit, "Exit".AsSpan(), originScreen, zoom, IsNodeSelected(state, StateMachineGraphNodeRef.Exit()), isActive: runtimeViz.HasData && runtimeViz.ActiveStateId == 0, activeOutlineColor, activeOutlineThickness);

        int total = def.StateCount;
        for (int i = 0; i < total; i++)
        {
            if (def.StateLayerId[i] != layerId)
            {
                continue;
            }

	            int id = def.StateId[i];
	            string name = def.StateName[i];

            var node = StateMachineGraphNodeRef.State(id);
            Vector2 pos = def.StatePos[i];
            bool selected = IsNodeSelected(state, node);
	            DrawStateNode(pos, name.AsSpan(), originScreen, zoom, selected, isActive: runtimeViz.HasData && (ushort)id == runtimeViz.ActiveStateId, activeOutlineColor, activeOutlineThickness);
	        }
	    }

		    private static void DrawSpecialNode(Vector2 posGraph, StateMachineGraphNodeKind kind, ReadOnlySpan<char> label, Vector2 originScreen, float zoom, bool selected, bool isActive, uint activeOutlineColor, float activeOutlineThickness)
		    {
		        Vector2 half = GetNodeHalfSize(kind);
		        Vector2 center = GraphToScreen(posGraph, originScreen, zoom);

			        var rect = new ImRect(center.X - half.X * zoom, center.Y - half.Y * zoom, half.X * 2f * zoom, half.Y * 2f * zoom);
			        uint fill = kind switch
			        {
			            StateMachineGraphNodeKind.Entry => EntryFill,
			            StateMachineGraphNodeKind.AnyState => AnyStateFill,
			            StateMachineGraphNodeKind.Exit => ExitFill,
			            _ => Im.Style.Surface
			        };

		        uint bg = selected ? Im.Style.Primary : fill;
		        uint border = Im.Style.Border;
		        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, bg);
		        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

            if (isActive && activeOutlineColor != 0)
            {
                Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, activeOutlineColor, activeOutlineThickness);
            }

		        float padding = Im.Style.Padding * zoom;
		        float textX = rect.X + padding;
		        float fontSize = GetGraphFontSize(zoom);
		        float textY = rect.Y + (rect.Height - fontSize) * 0.5f;
		        Im.Text(label, textX, textY, fontSize, 0xFFFFFFFF);
		    }

		    private static void DrawStateNode(Vector2 posGraph, ReadOnlySpan<char> label, Vector2 originScreen, float zoom, bool selected, bool isActive, uint activeOutlineColor, float activeOutlineThickness)
		    {
		        Vector2 half = GetNodeHalfSize(StateMachineGraphNodeKind.State);
		        Vector2 center = GraphToScreen(posGraph, originScreen, zoom);

		        var rect = new ImRect(center.X - half.X * zoom, center.Y - half.Y * zoom, half.X * 2f * zoom, half.Y * 2f * zoom);
		        uint bg = selected ? ImStyle.WithAlphaF(Im.Style.Primary, 0.70f) : ImStyle.WithAlphaF(Im.Style.Surface, 0.75f);
		        uint border = Im.Style.Border;
		        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, bg);
		        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, border, Im.Style.BorderWidth);

            if (isActive && activeOutlineColor != 0)
            {
                Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.CornerRadius, activeOutlineColor, activeOutlineThickness);
            }

		        float padding = Im.Style.Padding * zoom;
		        float textX = rect.X + padding;
		        float fontSize = GetGraphFontSize(zoom);
		        float textY = rect.Y + (rect.Height - fontSize) * 0.5f;
		        Im.Text(label, textX, textY, fontSize, Im.Style.TextPrimary);
		    }

    private static float GetGraphFontSize(float zoom)
    {
        float baseSize = Im.Style.FontSize;
        float size = baseSize * zoom;
        if (!float.IsFinite(size))
        {
            return baseSize;
        }

        return Math.Clamp(size, 0.5f, 256f);
    }

    private static bool IsNodeSelected(StateMachineEditorState state, StateMachineGraphNodeRef node)
    {
        for (int i = 0; i < state.SelectedNodes.Count; i++)
        {
            if (state.SelectedNodes[i].Equals(node))
            {
                return true;
            }
        }
        return false;
    }

    private static void ToggleNodeSelection(StateMachineEditorState state, StateMachineGraphNodeRef node, bool shift)
    {
        if (!shift)
        {
            state.SelectedNodes.Clear();
            state.SelectedNodeDragStartPositions.Clear();
            state.SelectedTransitionId = 0;
            state.SelectedNodes.Add(node);
            return;
        }

        for (int i = 0; i < state.SelectedNodes.Count; i++)
        {
            if (state.SelectedNodes[i].Equals(node))
            {
                state.SelectedNodes.RemoveAt(i);
                if (i < state.SelectedNodeDragStartPositions.Count)
                {
                    state.SelectedNodeDragStartPositions.RemoveAt(i);
                }
                return;
            }
        }

        state.SelectedTransitionId = 0;
        state.SelectedNodes.Add(node);
    }

    private static bool TryHitSpecialNode(StateMachineGraphNodeKind kind, Vector2 posGraph, Vector2 mouseGraph, out StateMachineGraphNodeRef node)
    {
        node = default;
        Vector2 half = GetNodeHalfSize(kind);
        var rectGraph = new ImRect(posGraph.X - half.X, posGraph.Y - half.Y, half.X * 2f, half.Y * 2f);
        if (!rectGraph.Contains(mouseGraph))
        {
            return false;
        }

        node = kind switch
        {
            StateMachineGraphNodeKind.Entry => StateMachineGraphNodeRef.Entry(),
            StateMachineGraphNodeKind.AnyState => StateMachineGraphNodeRef.AnyState(),
            StateMachineGraphNodeKind.Exit => StateMachineGraphNodeRef.Exit(),
            _ => default
        };
        return true;
    }

    private static bool TrySelectNode(
        StateMachineDefinitionComponent.ViewProxy def,
        ushort layerId,
        int layerSlotIndex,
        StateMachineEditorState state,
        Vector2 mouseGraph,
        bool shift)
    {
        if (TryHitSpecialNode(StateMachineGraphNodeKind.Exit, def.LayerExitPos[layerSlotIndex], mouseGraph, out var specialNode) ||
            TryHitSpecialNode(StateMachineGraphNodeKind.AnyState, def.LayerAnyStatePos[layerSlotIndex], mouseGraph, out specialNode) ||
            TryHitSpecialNode(StateMachineGraphNodeKind.Entry, def.LayerEntryPos[layerSlotIndex], mouseGraph, out specialNode))
        {
            ToggleNodeSelection(state, specialNode, shift);
            return true;
        }

        int total = def.StateCount;
        for (int i = total - 1; i >= 0; i--)
        {
            if (def.StateLayerId[i] != layerId)
            {
                continue;
            }

            Vector2 pos = def.StatePos[i];
            Vector2 half = GetNodeHalfSize(StateMachineGraphNodeKind.State);
            var rectGraph = new ImRect(pos.X - half.X, pos.Y - half.Y, half.X * 2f, half.Y * 2f);
            if (!rectGraph.Contains(mouseGraph))
            {
                continue;
            }

            var node = StateMachineGraphNodeRef.State(def.StateId[i]);
            ToggleNodeSelection(state, node, shift);
            return true;
        }

        return false;
    }

    private static void BeginNodeDrag(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineEditorState state, Vector2 mouseGraph)
    {
        state.IsDraggingNodes = true;
        state.NodeDragStartMouseGraph = mouseGraph;
        state.SelectedNodeDragStartPositions.Clear();

        for (int i = 0; i < state.SelectedNodes.Count; i++)
        {
            Vector2 pos = GetNodePosGraph(def, layerId, layerSlotIndex, state.SelectedNodes[i]);
            state.SelectedNodeDragStartPositions.Add(pos);
        }
    }

    private static void UpdateNodeDrag(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineEditorState state, Vector2 mouseGraph)
    {
        Vector2 delta = mouseGraph - state.NodeDragStartMouseGraph;
        for (int i = 0; i < state.SelectedNodes.Count && i < state.SelectedNodeDragStartPositions.Count; i++)
        {
            Vector2 start = state.SelectedNodeDragStartPositions[i];
            Vector2 pos = start + delta;
            if (state.SnappingEnabled)
            {
                pos = Snap(pos, 10f);
            }
            SetNodePosGraph(def, layerId, layerSlotIndex, state.SelectedNodes[i], pos);
        }
    }

    private static Vector2 Snap(Vector2 v, float grid)
    {
        if (grid <= 0.0001f)
        {
            return v;
        }
        float x = MathF.Round(v.X / grid) * grid;
        float y = MathF.Round(v.Y / grid) * grid;
        return new Vector2(x, y);
    }

	    private static bool TrySelectTransition(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineEditorState state, Vector2 originScreen, float zoom, Vector2 mouseScreen)
	    {
	        float radius = StateMachineEditorLayout.TransitionHandleRadius * TransitionHandleRadiusScale * zoom + 3f;
	        float radius2 = radius * radius;

	        int hovered = 0;
	        float bestDist2 = radius2;

        for (int i = 0; i < TransitionHandles.Count; i++)
        {
            var h = TransitionHandles[i];
            float d2 = (mouseScreen - h.Center).LengthSquared();
            if (d2 <= bestDist2)
            {
                bestDist2 = d2;
                hovered = h.TransitionId;
            }
        }

        if (hovered != 0)
        {
            state.SelectedTransitionId = hovered;
            state.SelectedNodes.Clear();
            state.SelectedNodeDragStartPositions.Clear();
            state.IsDraggingNodes = false;
            return true;
        }

        return false;
    }

	    private static void UpdateHoveredTransition(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineEditorState state, Vector2 originScreen, float zoom, Vector2 mouseScreen)
	    {
	        int hovered = 0;
	        float radius = StateMachineEditorLayout.TransitionHandleRadius * TransitionHandleRadiusScale * zoom + 3f;
	        float radius2 = radius * radius;
	        float bestDist2 = radius2;

        for (int i = 0; i < TransitionHandles.Count; i++)
        {
            var h = TransitionHandles[i];
            float d2 = (mouseScreen - h.Center).LengthSquared();
            if (d2 <= bestDist2)
            {
                bestDist2 = d2;
                hovered = h.TransitionId;
            }
        }

        state.HoveredTransitionId = hovered;
    }

    private static bool TryBeginConnectDrag(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineEditorState state, float zoom, Vector2 mouseGraph)
    {
        float bestDist2 = float.MaxValue;
        StateMachineGraphNodeRef bestNode = StateMachineGraphNodeRef.None();
        Vector2 bestDotGraph = default;

        ConsiderNodeForPort(def, layerId, layerSlotIndex, StateMachineGraphNodeRef.Entry(), mouseGraph, ref bestDist2, ref bestNode, ref bestDotGraph);
        ConsiderNodeForPort(def, layerId, layerSlotIndex, StateMachineGraphNodeRef.AnyState(), mouseGraph, ref bestDist2, ref bestNode, ref bestDotGraph);

        int total = def.StateCount;
        for (int i = 0; i < total; i++)
        {
            if (def.StateLayerId[i] != layerId)
            {
                continue;
            }
            ConsiderNodeForPort(def, layerId, layerSlotIndex, StateMachineGraphNodeRef.State(def.StateId[i]), mouseGraph, ref bestDist2, ref bestNode, ref bestDotGraph);
        }

        float snapRadius = StateMachineEditorLayout.SnapRadius / MathF.Max(0.1f, zoom);
        if (bestDist2 > snapRadius * snapRadius || bestNode.Kind == StateMachineGraphNodeKind.None)
        {
            return false;
        }

        state.ConnectDrag = new StateMachineEditorState.ConnectDragState
        {
            Active = true,
            Source = bestNode,
            StartGraph = bestDotGraph,
            SnapTarget = default,
            SnapTargetPortGraph = default,
            HasSnapTarget = false
        };

        return true;
    }

    private static void ConsiderNodeForPort(
        StateMachineDefinitionComponent.ViewProxy def,
        ushort layerId,
        int layerSlotIndex,
        StateMachineGraphNodeRef node,
        Vector2 mouseGraph,
        ref float bestDist2,
        ref StateMachineGraphNodeRef bestNode,
        ref Vector2 bestDotGraph)
    {
        StateMachineGraphNodeKind kind = node.Kind;
        if (!NodeAllowsOutgoing(kind))
        {
            return;
        }

        Vector2 nodePosGraph = GetNodePosGraph(def, layerId, layerSlotIndex, node);
        Vector2 half = GetNodeHalfSize(kind);
        var rect = new ImRect(nodePosGraph.X - half.X, nodePosGraph.Y - half.Y, half.X * 2f, half.Y * 2f);
        if (rect.Contains(mouseGraph))
        {
            return;
        }

        float inflate = StateMachineEditorLayout.PortDotHoverInflate;
        var hoverRect = new ImRect(rect.X - inflate, rect.Y - inflate, rect.Width + inflate * 2f, rect.Height + inflate * 2f);
        if (!hoverRect.Contains(mouseGraph))
        {
            return;
        }

        Vector2 dir = mouseGraph - nodePosGraph;
        if (dir.LengthSquared() <= 0.0001f)
        {
            dir = new Vector2(1f, 0f);
        }

        float scaleX = MathF.Abs(dir.X) > 0.0001f ? half.X / MathF.Abs(dir.X) : float.MaxValue;
        float scaleY = MathF.Abs(dir.Y) > 0.0001f ? half.Y / MathF.Abs(dir.Y) : float.MaxValue;
        float scale = MathF.Min(scaleX, scaleY);
        Vector2 dot = nodePosGraph + dir * scale;

        float dist2 = (mouseGraph - dot).LengthSquared();
        if (dist2 < bestDist2)
        {
            bestDist2 = dist2;
            bestNode = node;
            bestDotGraph = dot;
        }
    }

    private static void UpdateConnectDrag(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineEditorState state, float zoom, Vector2 mouseGraph)
    {
        var drag = state.ConnectDrag;
        drag.HasSnapTarget = false;

        float bestDist2 = float.MaxValue;
        StateMachineGraphNodeRef bestNode = StateMachineGraphNodeRef.None();
        Vector2 bestDotGraph = default;

        ConsiderNodeForIncomingPort(def, layerId, layerSlotIndex, StateMachineGraphNodeRef.Exit(), mouseGraph, ref bestDist2, ref bestNode, ref bestDotGraph);

        int total = def.StateCount;
        for (int i = 0; i < total; i++)
        {
            if (def.StateLayerId[i] != layerId)
            {
                continue;
            }
            ConsiderNodeForIncomingPort(def, layerId, layerSlotIndex, StateMachineGraphNodeRef.State(def.StateId[i]), mouseGraph, ref bestDist2, ref bestNode, ref bestDotGraph);
        }

        float snapRadius = StateMachineEditorLayout.SnapRadius / MathF.Max(0.1f, zoom);
        if (bestNode.Kind != StateMachineGraphNodeKind.None && bestDist2 <= snapRadius * snapRadius)
        {
            drag.HasSnapTarget = true;
            drag.SnapTarget = bestNode;
            drag.SnapTargetPortGraph = bestDotGraph;
        }

        state.ConnectDrag = drag;
    }

    private static void ConsiderNodeForIncomingPort(
        StateMachineDefinitionComponent.ViewProxy def,
        ushort layerId,
        int layerSlotIndex,
        StateMachineGraphNodeRef node,
        Vector2 mouseGraph,
        ref float bestDist2,
        ref StateMachineGraphNodeRef bestNode,
        ref Vector2 bestDotGraph)
    {
        StateMachineGraphNodeKind kind = node.Kind;
        if (!NodeAllowsIncoming(kind))
        {
            return;
        }

        Vector2 nodePosGraph = GetNodePosGraph(def, layerId, layerSlotIndex, node);
        Vector2 half = GetNodeHalfSize(kind);
        Vector2 dir = mouseGraph - nodePosGraph;
        if (dir.LengthSquared() <= 0.0001f)
        {
            dir = new Vector2(1f, 0f);
        }

        float scaleX = MathF.Abs(dir.X) > 0.0001f ? half.X / MathF.Abs(dir.X) : float.MaxValue;
        float scaleY = MathF.Abs(dir.Y) > 0.0001f ? half.Y / MathF.Abs(dir.Y) : float.MaxValue;
        float scale = MathF.Min(scaleX, scaleY);
        Vector2 dot = nodePosGraph + dir * scale;

        float dist2 = (mouseGraph - dot).LengthSquared();
        if (dist2 < bestDist2)
        {
            bestDist2 = dist2;
            bestNode = node;
            bestDotGraph = dot;
        }
    }

    private static void CommitConnectDrag(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineEditorState state)
    {
        if (!state.ConnectDrag.HasSnapTarget)
        {
            return;
        }

        StateMachineGraphNodeRef src = state.ConnectDrag.Source;
        StateMachineGraphNodeRef dst = state.ConnectDrag.SnapTarget;

        if (src.Equals(dst))
        {
            return;
        }

        int transitionSlot = def.TransitionCount;
        if (transitionSlot >= StateMachineDefinitionComponent.MaxTransitions)
        {
            return;
        }

        var fromKind = src.Kind == StateMachineGraphNodeKind.AnyState
            ? StateMachineDefinitionComponent.TransitionFromKind.AnyState
            : src.Kind == StateMachineGraphNodeKind.Entry
                ? StateMachineDefinitionComponent.TransitionFromKind.Entry
                : StateMachineDefinitionComponent.TransitionFromKind.State;

        var toKind = dst.Kind == StateMachineGraphNodeKind.Exit
            ? StateMachineDefinitionComponent.TransitionToKind.Exit
            : StateMachineDefinitionComponent.TransitionToKind.State;

        Span<ushort> layerNextTransitionId = def.LayerNextTransitionIdSpan();
        ushort id = layerNextTransitionId[layerSlotIndex];
        if (id == 0)
        {
            id = 1;
        }
        layerNextTransitionId[layerSlotIndex] = (ushort)(id + 1);

        Span<ushort> transitionId = def.TransitionIdSpan();
        Span<ushort> transitionLayerId = def.TransitionLayerIdSpan();
        Span<byte> transitionFromKindValue = def.TransitionFromKindValueSpan();
        Span<ushort> transitionFromStateId = def.TransitionFromStateIdSpan();
        Span<byte> transitionToKindValue = def.TransitionToKindValueSpan();
        Span<ushort> transitionToStateId = def.TransitionToStateIdSpan();
        Span<float> transitionDuration = def.TransitionDurationSecondsSpan();
        Span<byte> transitionHasExitTime = def.TransitionHasExitTimeValueSpan();
        Span<float> transitionExitTime01 = def.TransitionExitTime01Span();
        Span<ushort> transitionConditionStart = def.TransitionConditionStartSpan();
        Span<byte> transitionConditionCount = def.TransitionConditionCountSpan();

        transitionId[transitionSlot] = id;
        transitionLayerId[transitionSlot] = layerId;
        transitionFromKindValue[transitionSlot] = (byte)fromKind;
        transitionFromStateId[transitionSlot] = (ushort)src.StateId;
        transitionToKindValue[transitionSlot] = (byte)toKind;
        transitionToStateId[transitionSlot] = (ushort)dst.StateId;
        transitionDuration[transitionSlot] = 0.12f;
        transitionHasExitTime[transitionSlot] = 0;
        transitionExitTime01[transitionSlot] = 0f;
        transitionConditionStart[transitionSlot] = def.ConditionCount;
        transitionConditionCount[transitionSlot] = 0;
        def.TransitionCount = (ushort)(transitionSlot + 1);

        state.SelectedTransitionId = id;
        state.SelectedNodes.Clear();
        state.SelectedNodeDragStartPositions.Clear();
    }

    private static void DrawConnectPreview(StateMachineEditorState state, Vector2 originScreen, float zoom, Vector2 mouseGraph)
    {
        if (!state.ConnectDrag.Active)
        {
            return;
        }

        Vector2 start = state.ConnectDrag.StartGraph;
        Vector2 end = state.ConnectDrag.HasSnapTarget ? state.ConnectDrag.SnapTargetPortGraph : mouseGraph;

        Vector2 startScreen = GraphToScreen(start, originScreen, zoom);
        Vector2 endScreen = GraphToScreen(end, originScreen, zoom);
        Im.DrawLine(startScreen.X, startScreen.Y, endScreen.X, endScreen.Y, 2f, ImStyle.WithAlphaF(Im.Style.Primary, 0.75f));
    }

    private static void DrawTimelineDropPreview(
        UiWorkspace workspace,
        StateMachineDefinitionComponent.ViewProxy def,
        ushort machineId,
        ushort layerId,
        int layerSlotIndex,
        ImRect rect,
        Vector2 originScreen,
        float zoom,
        Vector2 mouseGraph)
    {
        if (!AnimationsLibraryDragDrop.IsDragging || AnimationsLibraryDragDrop.Kind != AnimationsLibraryDragDrop.DragKind.Timeline)
        {
            return;
        }

        if (!rect.Contains(Im.MousePos))
        {
            return;
        }

        AnimationsLibraryDragDropPreviewState.SuppressGlobalThisFrame(Im.Context.FrameCount);
        DrawStateNode(mouseGraph, "Drop Timeline".AsSpan(), originScreen, zoom, selected: false, isActive: false, activeOutlineColor: 0, activeOutlineThickness: 0f);

        if (AnimationsLibraryDragDrop.ReleaseFrame == Im.Context.FrameCount)
        {
            int timelineId = AnimationsLibraryDragDrop.TimelineId;
            if (workspace.TryGetActiveAnimationDocument(out var animations) &&
                animations.TryGetTimelineById(timelineId, out var timeline) &&
                timeline != null)
            {
                int newStateId = AddTimelineState(def, layerId, layerSlotIndex, timeline.Id, timeline.Name, mouseGraph);
                if (newStateId > 0)
                {
                    workspace.NotifyInspectorAnimationInteraction();
                    stateMachineSelection(workspace, machineId, layerId, StateMachineGraphNodeRef.State(newStateId));
                }
            }
            AnimationsLibraryDragDrop.Clear();
        }
    }

    private static void stateMachineSelection(UiWorkspace workspace, ushort machineId, ushort layerId, StateMachineGraphNodeRef node)
    {
        workspace.SetStateMachineInspectorNodeSelection(machineId, layerId, node);
    }

    private static void UpdateInteraction(
        UiWorkspace workspace,
        StateMachineDefinitionComponent.ViewProxy def,
        ushort machineId,
        ushort layerId,
        int layerSlotIndex,
        StateMachineEditorState state,
        ImRect rect,
        Vector2 originScreen,
        float zoom,
        Vector2 mouseGraph,
        Vector2 mouseScreen)
    {
        var input = Im.Context.Input;

        bool canHoverTransitions =
            rect.Contains(mouseScreen) &&
            !state.ConnectDrag.Active &&
            !state.IsDraggingNodes &&
            !state.IsMarqueeActive &&
            !AnimationsLibraryDragDrop.IsDragging;

        if (!canHoverTransitions)
        {
            state.HoveredTransitionId = 0;
        }
        else
        {
            UpdateHoveredTransition(def, layerId, layerSlotIndex, state, originScreen, zoom, mouseScreen);
        }

        if (AnimationsLibraryDragDrop.IsDragging && AnimationsLibraryDragDrop.Kind == AnimationsLibraryDragDrop.DragKind.Timeline)
        {
            if (rect.Contains(Im.MousePos))
            {
                return;
            }
        }

        if (rect.Contains(mouseScreen) && (input.KeyDelete || input.KeyBackspace) && Im.Context.FocusId == 0 && !Im.Context.AnyActive && !Im.Context.WantCaptureKeyboard)
        {
            if (state.SelectedTransitionId > 0)
            {
                DeleteTransitionById(def, layerId, state.SelectedTransitionId);
                state.SelectedTransitionId = 0;
                workspace.ClearStateMachineInspectorSelection();
                return;
            }

            if (DeleteSelectedStates(def, layerId, state))
            {
                workspace.ClearStateMachineInspectorSelection();
                return;
            }
        }

        if (state.ConnectDrag.Active)
        {
            UpdateConnectDrag(def, layerId, layerSlotIndex, state, zoom, mouseGraph);
            if (!input.MouseDown)
            {
                CommitConnectDrag(def, layerId, layerSlotIndex, state);
                state.ConnectDrag.Active = false;
            }
            return;
        }

        if (state.IsDraggingNodes)
        {
            if (!input.MouseDown)
            {
                state.IsDraggingNodes = false;
                if (state.SelectedNodes.Count == 1)
                {
                    workspace.SetStateMachineInspectorNodeSelection(machineId, layerId, state.SelectedNodes[0]);
                }
                else if (state.SelectedNodes.Count > 1)
                {
                    workspace.SetStateMachineInspectorMultiNodeSelection(machineId, layerId, state.SelectedNodes.Count);
                }
            }
            else
            {
                UpdateNodeDrag(def, layerId, layerSlotIndex, state, mouseGraph);
            }
            return;
        }

        if (state.IsMarqueeActive)
        {
            state.MarqueeEndGraph = mouseGraph;
            if (!input.MouseDown)
            {
                CommitMarqueeSelection(def, layerId, layerSlotIndex, state);
                state.IsMarqueeActive = false;
            }
            return;
        }

        if (!rect.Contains(mouseScreen))
        {
            return;
        }

        if (input.MousePressed || input.MouseRightPressed || input.MouseMiddlePressed)
        {
            workspace.NotifyInspectorAnimationInteraction();
        }

        bool shift = input.KeyShift;

        if (input.MousePressed)
        {
            if (TrySelectTransition(def, layerId, layerSlotIndex, state, originScreen, zoom, mouseScreen))
            {
                workspace.SetStateMachineInspectorTransitionSelection(machineId, layerId, state.SelectedTransitionId);
                return;
            }

            if (TryBeginConnectDrag(def, layerId, layerSlotIndex, state, zoom, mouseGraph))
            {
                return;
            }

            if (TrySelectNode(def, layerId, layerSlotIndex, state, mouseGraph, shift))
            {
                state.SelectedTransitionId = 0;
                BeginNodeDrag(def, layerId, layerSlotIndex, state, mouseGraph);
                if (state.SelectedNodes.Count == 1)
                {
                    workspace.SetStateMachineInspectorNodeSelection(machineId, layerId, state.SelectedNodes[0]);
                }
                return;
            }

            if (!shift)
            {
                state.SelectedNodes.Clear();
                state.SelectedNodeDragStartPositions.Clear();
                state.SelectedTransitionId = 0;
                workspace.ClearStateMachineInspectorSelection();
            }

            state.IsMarqueeActive = true;
            state.MarqueeStartGraph = mouseGraph;
            state.MarqueeEndGraph = mouseGraph;
        }
    }

    private static void CommitMarqueeSelection(StateMachineDefinitionComponent.ViewProxy def, ushort layerId, int layerSlotIndex, StateMachineEditorState state)
    {
        var rectGraph = GetMarqueeRect(state.MarqueeStartGraph, state.MarqueeEndGraph);
        state.SelectedNodes.Clear();
        state.SelectedNodeDragStartPositions.Clear();
        state.SelectedTransitionId = 0;

        if (rectGraph.Width <= 0.001f || rectGraph.Height <= 0.001f)
        {
            return;
        }

        int total = def.StateCount;
        for (int i = 0; i < total; i++)
        {
            if (def.StateLayerId[i] != layerId)
            {
                continue;
            }

            var node = StateMachineGraphNodeRef.State(def.StateId[i]);
            var nodeRect = GetNodeRectGraph(def, layerId, layerSlotIndex, node);
            if (rectGraph.Overlaps(nodeRect))
            {
                state.SelectedNodes.Add(node);
            }
        }
    }

    private static ImRect GetMarqueeRect(Vector2 a, Vector2 b)
    {
        float x0 = MathF.Min(a.X, b.X);
        float y0 = MathF.Min(a.Y, b.Y);
        float x1 = MathF.Max(a.X, b.X);
        float y1 = MathF.Max(a.Y, b.Y);
        return new ImRect(x0, y0, x1 - x0, y1 - y0);
    }

    private static void DrawMarquee(StateMachineEditorState state, Vector2 originScreen, float zoom)
    {
        if (!state.IsMarqueeActive)
        {
            return;
        }

        var rect = GetMarqueeRect(state.MarqueeStartGraph, state.MarqueeEndGraph);
        Vector2 a = GraphToScreen(new Vector2(rect.X, rect.Y), originScreen, zoom);
        Vector2 b = GraphToScreen(new Vector2(rect.Right, rect.Bottom), originScreen, zoom);

        float x = MathF.Min(a.X, b.X);
        float y = MathF.Min(a.Y, b.Y);
        float w = MathF.Abs(b.X - a.X);
        float h = MathF.Abs(b.Y - a.Y);

        uint fill = ImStyle.WithAlphaF(Im.Style.Primary, 0.12f);
        uint stroke = ImStyle.WithAlphaF(Im.Style.Primary, 0.65f);
        Im.DrawRect(x, y, w, h, fill);
        Im.DrawRoundedRectStroke(x, y, w, h, 0f, stroke, 1f);
    }

    private static void DrawHoverPortDot(
        StateMachineDefinitionComponent.ViewProxy def,
        ushort layerId,
        int layerSlotIndex,
        StateMachineEditorState state,
        Vector2 originScreen,
        float zoom,
        Vector2 mouseGraph)
    {
        if (state.ConnectDrag.Active || state.IsDraggingNodes || state.IsMarqueeActive || AnimationsLibraryDragDrop.IsDragging)
        {
            return;
        }

        float bestDist2 = float.MaxValue;
        StateMachineGraphNodeRef bestNode = StateMachineGraphNodeRef.None();
        Vector2 bestDotGraph = default;

        ConsiderNodeForPort(def, layerId, layerSlotIndex, StateMachineGraphNodeRef.Entry(), mouseGraph, ref bestDist2, ref bestNode, ref bestDotGraph);
        ConsiderNodeForPort(def, layerId, layerSlotIndex, StateMachineGraphNodeRef.AnyState(), mouseGraph, ref bestDist2, ref bestNode, ref bestDotGraph);

        int total = def.StateCount;
        for (int i = 0; i < total; i++)
        {
            if (def.StateLayerId[i] != layerId)
            {
                continue;
            }
            ConsiderNodeForPort(def, layerId, layerSlotIndex, StateMachineGraphNodeRef.State(def.StateId[i]), mouseGraph, ref bestDist2, ref bestNode, ref bestDotGraph);
        }

        float snapRadius = StateMachineEditorLayout.SnapRadius / MathF.Max(0.1f, zoom);
        if (bestNode.Kind == StateMachineGraphNodeKind.None || bestDist2 > snapRadius * snapRadius)
        {
            return;
        }

        Vector2 dotScreen = GraphToScreen(bestDotGraph, originScreen, zoom);
        float radius = StateMachineEditorLayout.PortDotRadius * zoom;
        uint fill = ImStyle.WithAlphaF(Im.Style.Primary, 0.9f);
        Im.DrawCircle(dotScreen.X, dotScreen.Y, radius, fill);
    }

    private readonly struct TransitionHandleDraw
    {
        public readonly int TransitionId;
        public readonly Vector2 Center;
        public readonly Vector2 Start;
        public readonly Vector2 End;
        public readonly float Radius;
        public readonly uint Fill;
        public readonly uint Stroke;
        public readonly bool Selected;

        public TransitionHandleDraw(int transitionId, Vector2 center, Vector2 start, Vector2 end, float radius, uint fill, uint stroke, bool selected)
        {
            TransitionId = transitionId;
            Center = center;
            Start = start;
            End = end;
            Radius = radius;
            Fill = fill;
            Stroke = stroke;
            Selected = selected;
        }
    }
}
