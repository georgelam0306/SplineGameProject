using Property;
using Property.Runtime;

namespace Derp.UI;

internal static class AnimationSelectionSync
{
    public static bool TrySyncSelection(
        UiWorkspace workspace,
        EntityId prefabEntity,
        out AnimationSelectionComponentKind selectionKind,
        out AnyComponentHandle selectionAny)
    {
        selectionKind = AnimationSelectionComponentKind.None;
        selectionAny = default;

        if (workspace == null || prefabEntity.IsNull)
        {
            return false;
        }

        if (!AnimationEditorWindow.IsTimelineMode())
        {
            return false;
        }

        if (!workspace.TryGetActiveAnimationDocument(out var doc) || doc == null)
        {
            return false;
        }

        if (AnimationEditorWindow.TryGetSelectedKeyframe(out int timelineId, out AnimationDocument.AnimationBinding binding, out int keyIndex))
        {
            if (TryCreateOrUpdateKeyframeSelection(workspace, doc, timelineId, binding, keyIndex, prefabEntity, out selectionAny))
            {
                selectionKind = AnimationSelectionComponentKind.Keyframe;
                return true;
            }
        }

        if (doc.TryGetSelectedTimeline(out var timeline) && timeline != null)
        {
            if (TryCreateOrUpdateTimelineSelection(workspace, doc, timeline, prefabEntity, out selectionAny))
            {
                selectionKind = AnimationSelectionComponentKind.Timeline;
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateOrUpdateTimelineSelection(
        UiWorkspace workspace,
        AnimationDocument doc,
        AnimationDocument.AnimationTimeline timeline,
        EntityId prefabEntity,
        out AnyComponentHandle selectionAny)
    {
        selectionAny = default;

        uint stableId = workspace.World.GetStableId(prefabEntity);
        if (stableId == 0)
        {
            return false;
        }

        var view = AnimationTimelineSelectionComponent.Api.GetOrCreateById(
            workspace.PropertyWorld,
            new AnimationTimelineSelectionComponentId((ulong)stableId),
            default(AnimationTimelineSelectionComponent));

        if (!view.IsAlive)
        {
            return false;
        }

        selectionAny = new AnyComponentHandle(AnimationTimelineSelectionComponent.Api.PoolIdConst, view.Handle.Index, view.Handle.Generation);

        view.TimelineId = timeline.Id;

        int timelineIndex = -1;
        for (int i = 0; i < doc.Timelines.Count; i++)
        {
            if (doc.Timelines[i].Id == timeline.Id)
            {
                timelineIndex = i;
                break;
            }
        }
        view.TimelineIndex = timelineIndex;

        view.DurationFrames = timeline.DurationFrames;
        view.WorkStartFrame = timeline.WorkStartFrame;
        view.WorkEndFrame = timeline.WorkEndFrame;
        view.SnapFps = timeline.SnapFps;
        view.PlaybackSpeed = timeline.PlaybackSpeed;
        view.PlaybackMode = (int)timeline.Mode;

        return true;
    }

    private static bool TryCreateOrUpdateKeyframeSelection(
        UiWorkspace workspace,
        AnimationDocument doc,
        int timelineId,
        in AnimationDocument.AnimationBinding binding,
        int keyIndex,
        EntityId prefabEntity,
        out AnyComponentHandle selectionAny)
    {
        selectionAny = default;

        if (!doc.TryGetTimelineById(timelineId, out var timeline) || timeline == null)
        {
            return false;
        }

        AnimationDocument.AnimationTrack? track = AnimationEditorHelpers.FindTrack(timeline, binding);
        if (track == null)
        {
            return false;
        }

        if ((uint)keyIndex >= (uint)track.Keys.Count)
        {
            return false;
        }

        uint stableId = workspace.World.GetStableId(prefabEntity);
        if (stableId == 0)
        {
            return false;
        }

        var key = track.Keys[keyIndex];

        switch (binding.PropertyKind)
        {
            case PropertyKind.Int:
            {
                var view = AnimationKeyframeSelectionIntComponent.Api.GetOrCreateById(
                    workspace.PropertyWorld,
                    new AnimationKeyframeSelectionIntComponentId((ulong)stableId),
                    default(AnimationKeyframeSelectionIntComponent));
                if (!view.IsAlive)
                {
                    return false;
                }
                selectionAny = new AnyComponentHandle(AnimationKeyframeSelectionIntComponent.Api.PoolIdConst, view.Handle.Index, view.Handle.Generation);
                view.TimelineId = timelineId;
                view.KeyIndex = keyIndex;
                view.TargetIndex = binding.TargetIndex;
                view.ComponentKind = binding.ComponentKind;
                view.PropertyIndexHint = binding.PropertyIndexHint;
                view.PropertyId = binding.PropertyId;
                view.BoundPropertyKind = binding.PropertyKind;
                view.Frame = key.Frame;
                view.Value = key.Value.Int;
                view.Interpolation = (int)key.Interpolation;
                view.InTangent = key.InTangent;
                view.OutTangent = key.OutTangent;
                return true;
            }
            case PropertyKind.Bool:
            {
                var view = AnimationKeyframeSelectionBoolComponent.Api.GetOrCreateById(
                    workspace.PropertyWorld,
                    new AnimationKeyframeSelectionBoolComponentId((ulong)stableId),
                    default(AnimationKeyframeSelectionBoolComponent));
                if (!view.IsAlive)
                {
                    return false;
                }
                selectionAny = new AnyComponentHandle(AnimationKeyframeSelectionBoolComponent.Api.PoolIdConst, view.Handle.Index, view.Handle.Generation);
                view.TimelineId = timelineId;
                view.KeyIndex = keyIndex;
                view.TargetIndex = binding.TargetIndex;
                view.ComponentKind = binding.ComponentKind;
                view.PropertyIndexHint = binding.PropertyIndexHint;
                view.PropertyId = binding.PropertyId;
                view.BoundPropertyKind = binding.PropertyKind;
                view.Frame = key.Frame;
                view.Value = key.Value.Bool;
                view.Interpolation = (int)key.Interpolation;
                view.InTangent = key.InTangent;
                view.OutTangent = key.OutTangent;
                return true;
            }
            case PropertyKind.Vec2:
            {
                var view = AnimationKeyframeSelectionVec2Component.Api.GetOrCreateById(
                    workspace.PropertyWorld,
                    new AnimationKeyframeSelectionVec2ComponentId((ulong)stableId),
                    default(AnimationKeyframeSelectionVec2Component));
                if (!view.IsAlive)
                {
                    return false;
                }
                selectionAny = new AnyComponentHandle(AnimationKeyframeSelectionVec2Component.Api.PoolIdConst, view.Handle.Index, view.Handle.Generation);
                view.TimelineId = timelineId;
                view.KeyIndex = keyIndex;
                view.TargetIndex = binding.TargetIndex;
                view.ComponentKind = binding.ComponentKind;
                view.PropertyIndexHint = binding.PropertyIndexHint;
                view.PropertyId = binding.PropertyId;
                view.BoundPropertyKind = binding.PropertyKind;
                view.Frame = key.Frame;
                view.Value = key.Value.Vec2;
                view.Interpolation = (int)key.Interpolation;
                view.InTangent = key.InTangent;
                view.OutTangent = key.OutTangent;
                return true;
            }
            case PropertyKind.Vec3:
            {
                var view = AnimationKeyframeSelectionVec3Component.Api.GetOrCreateById(
                    workspace.PropertyWorld,
                    new AnimationKeyframeSelectionVec3ComponentId((ulong)stableId),
                    default(AnimationKeyframeSelectionVec3Component));
                if (!view.IsAlive)
                {
                    return false;
                }
                selectionAny = new AnyComponentHandle(AnimationKeyframeSelectionVec3Component.Api.PoolIdConst, view.Handle.Index, view.Handle.Generation);
                view.TimelineId = timelineId;
                view.KeyIndex = keyIndex;
                view.TargetIndex = binding.TargetIndex;
                view.ComponentKind = binding.ComponentKind;
                view.PropertyIndexHint = binding.PropertyIndexHint;
                view.PropertyId = binding.PropertyId;
                view.BoundPropertyKind = binding.PropertyKind;
                view.Frame = key.Frame;
                view.Value = key.Value.Vec3;
                view.Interpolation = (int)key.Interpolation;
                view.InTangent = key.InTangent;
                view.OutTangent = key.OutTangent;
                return true;
            }
            case PropertyKind.Vec4:
            {
                var view = AnimationKeyframeSelectionVec4Component.Api.GetOrCreateById(
                    workspace.PropertyWorld,
                    new AnimationKeyframeSelectionVec4ComponentId((ulong)stableId),
                    default(AnimationKeyframeSelectionVec4Component));
                if (!view.IsAlive)
                {
                    return false;
                }
                selectionAny = new AnyComponentHandle(AnimationKeyframeSelectionVec4Component.Api.PoolIdConst, view.Handle.Index, view.Handle.Generation);
                view.TimelineId = timelineId;
                view.KeyIndex = keyIndex;
                view.TargetIndex = binding.TargetIndex;
                view.ComponentKind = binding.ComponentKind;
                view.PropertyIndexHint = binding.PropertyIndexHint;
                view.PropertyId = binding.PropertyId;
                view.BoundPropertyKind = binding.PropertyKind;
                view.Frame = key.Frame;
                view.Value = key.Value.Vec4;
                view.Interpolation = (int)key.Interpolation;
                view.InTangent = key.InTangent;
                view.OutTangent = key.OutTangent;
                return true;
            }
            case PropertyKind.Color32:
            {
                var view = AnimationKeyframeSelectionColor32Component.Api.GetOrCreateById(
                    workspace.PropertyWorld,
                    new AnimationKeyframeSelectionColor32ComponentId((ulong)stableId),
                    default(AnimationKeyframeSelectionColor32Component));
                if (!view.IsAlive)
                {
                    return false;
                }
                selectionAny = new AnyComponentHandle(AnimationKeyframeSelectionColor32Component.Api.PoolIdConst, view.Handle.Index, view.Handle.Generation);
                view.TimelineId = timelineId;
                view.KeyIndex = keyIndex;
                view.TargetIndex = binding.TargetIndex;
                view.ComponentKind = binding.ComponentKind;
                view.PropertyIndexHint = binding.PropertyIndexHint;
                view.PropertyId = binding.PropertyId;
                view.BoundPropertyKind = binding.PropertyKind;
                view.Frame = key.Frame;
                view.Value = key.Value.Color32;
                view.Interpolation = (int)key.Interpolation;
                view.InTangent = key.InTangent;
                view.OutTangent = key.OutTangent;
                return true;
            }
            default:
            {
                var view = AnimationKeyframeSelectionFloatComponent.Api.GetOrCreateById(
                    workspace.PropertyWorld,
                    new AnimationKeyframeSelectionFloatComponentId((ulong)stableId),
                    default(AnimationKeyframeSelectionFloatComponent));
                if (!view.IsAlive)
                {
                    return false;
                }
                selectionAny = new AnyComponentHandle(AnimationKeyframeSelectionFloatComponent.Api.PoolIdConst, view.Handle.Index, view.Handle.Generation);
                view.TimelineId = timelineId;
                view.KeyIndex = keyIndex;
                view.TargetIndex = binding.TargetIndex;
                view.ComponentKind = binding.ComponentKind;
                view.PropertyIndexHint = binding.PropertyIndexHint;
                view.PropertyId = binding.PropertyId;
                view.BoundPropertyKind = binding.PropertyKind;
                view.Frame = key.Frame;
                view.Value = key.Value.Float;
                view.Interpolation = (int)key.Interpolation;
                view.InTangent = key.InTangent;
                view.OutTangent = key.OutTangent;
                return true;
            }
        }
    }
}
