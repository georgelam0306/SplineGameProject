using System;
using System.Collections.Generic;
using System.Numerics;
using Core;
using Property;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
	    internal void EnsureHierarchyTransformsAreLocal()
	    {
	        if (_hierarchyTransformsAreLocal)
	        {
	            return;
	        }

	        int shapeCount = _shapes.Count;
	        var shapeWorldPositions = new Vector2[shapeCount];
	        var shapeWorldScales = new Vector2[shapeCount];
	        var shapeWorldRotationsDegrees = new float[shapeCount];

	        for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++)
	        {
	            var shape = _shapes[shapeIndex];
	            var shapeTransform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
	            if (!shapeTransform.IsAlive)
	            {
	                shapeWorldPositions[shapeIndex] = Vector2.Zero;
	                shapeWorldScales[shapeIndex] = Vector2.One;
	                shapeWorldRotationsDegrees[shapeIndex] = 0f;
	                continue;
	            }

	            shapeWorldPositions[shapeIndex] = shapeTransform.Position;
	            shapeWorldScales[shapeIndex] = NormalizeScale(shapeTransform.Scale);
	            shapeWorldRotationsDegrees[shapeIndex] = shapeTransform.Rotation;
	        }

	        int prefabCount = _prefabs.Count;
	        var prefabWorldPositions = new Vector2[prefabCount];
	        var prefabWorldScales = new Vector2[prefabCount];
	        var prefabWorldRotationsDegrees = new float[prefabCount];

	        for (int prefabIndex = 0; prefabIndex < prefabCount; prefabIndex++)
	        {
	            var prefab = _prefabs[prefabIndex];
	            var prefabTransform = TransformComponent.Api.FromHandle(_propertyWorld, prefab.Transform);
	            if (!prefabTransform.IsAlive)
	            {
	                prefabWorldPositions[prefabIndex] = Vector2.Zero;
	                prefabWorldScales[prefabIndex] = Vector2.One;
	                prefabWorldRotationsDegrees[prefabIndex] = 0f;
	                continue;
	            }

	            prefabWorldPositions[prefabIndex] = prefabTransform.Position;
	            prefabWorldScales[prefabIndex] = NormalizeScale(prefabTransform.Scale);
	            prefabWorldRotationsDegrees[prefabIndex] = prefabTransform.Rotation;
	        }

	        int groupCount = _groups.Count;
	        var groupWorldPositions = new Vector2[groupCount];
	        var groupWorldScales = new Vector2[groupCount];
	        var groupWorldRotationsDegrees = new float[groupCount];
	        var groupWorldResolved = new byte[groupCount]; // 0 = unresolved, 1 = resolving, 2 = resolved

	        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
	        {
	            var group = _groups[groupIndex];
	            if (group.Transform.IsNull)
	            {
	                groupWorldPositions[groupIndex] = Vector2.Zero;
	                groupWorldScales[groupIndex] = Vector2.One;
	                groupWorldRotationsDegrees[groupIndex] = 0f;
	                groupWorldResolved[groupIndex] = 0;
	                continue;
	            }

	            var transform = TransformComponent.Api.FromHandle(_propertyWorld, group.Transform);
	            if (!transform.IsAlive)
	            {
	                groupWorldPositions[groupIndex] = Vector2.Zero;
	                groupWorldScales[groupIndex] = Vector2.One;
	                groupWorldRotationsDegrees[groupIndex] = 0f;
	                groupWorldResolved[groupIndex] = 2;
	                continue;
	            }

	            groupWorldPositions[groupIndex] = transform.Position;
	            groupWorldScales[groupIndex] = NormalizeScale(transform.Scale);
	            groupWorldRotationsDegrees[groupIndex] = transform.Rotation;
	            groupWorldResolved[groupIndex] = 2;
	        }

	        var resolveStack = new int[groupCount];
	        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
	        {
	            if (groupWorldResolved[groupIndex] == 2)
	            {
	                continue;
	            }

	            int stackCount = 0;
	            int currentGroupIndex = groupIndex;
	            while (true)
	            {
	                if (groupWorldResolved[currentGroupIndex] == 2)
	                {
	                    break;
	                }

	                if (groupWorldResolved[currentGroupIndex] == 1)
	                {
	                    // Cycle: treat as identity for this chain.
	                    groupWorldPositions[currentGroupIndex] = Vector2.Zero;
	                    groupWorldScales[currentGroupIndex] = Vector2.One;
	                    groupWorldRotationsDegrees[currentGroupIndex] = 0f;
	                    groupWorldResolved[currentGroupIndex] = 2;
	                    break;
	                }

	                groupWorldResolved[currentGroupIndex] = 1;
	                resolveStack[stackCount++] = currentGroupIndex;

	                var currentGroup = _groups[currentGroupIndex];
	                if (currentGroup.Transform.IsNull)
	                {
	                    int parentGroupId = currentGroup.ParentGroupId;
	                    int parentShapeId = currentGroup.ParentShapeId;
	                    if (parentShapeId != 0)
	                    {
	                        if (TryGetShapeIndexById(parentShapeId, out int parentShapeIndex))
	                        {
	                            groupWorldPositions[currentGroupIndex] = shapeWorldPositions[parentShapeIndex];
	                            groupWorldScales[currentGroupIndex] = shapeWorldScales[parentShapeIndex];
	                            groupWorldRotationsDegrees[currentGroupIndex] = shapeWorldRotationsDegrees[parentShapeIndex];
	                            groupWorldResolved[currentGroupIndex] = 2;
	                        }
	                        break;
	                    }

	                    if (parentGroupId != 0)
	                    {
	                        if (TryGetGroupIndexById(parentGroupId, out int parentGroupIndex))
	                        {
	                            currentGroupIndex = parentGroupIndex;
	                            continue;
	                        }

	                        break;
	                    }

	                    groupWorldPositions[currentGroupIndex] = Vector2.Zero;
	                    groupWorldScales[currentGroupIndex] = Vector2.One;
	                    groupWorldRotationsDegrees[currentGroupIndex] = 0f;
	                    groupWorldResolved[currentGroupIndex] = 2;
	                    break;
	                }

	                // Group has a transform and was marked resolved in the initial pass.
	                break;
	            }

	            for (int stackIndex = stackCount - 1; stackIndex >= 0; stackIndex--)
	            {
	                int unresolvedGroupIndex = resolveStack[stackIndex];
	                if (groupWorldResolved[unresolvedGroupIndex] == 2)
	                {
	                    continue;
	                }

	                var unresolvedGroup = _groups[unresolvedGroupIndex];
	                int parentGroupId = unresolvedGroup.ParentGroupId;
	                int parentShapeId = unresolvedGroup.ParentShapeId;

	                Vector2 parentPositionWorld = Vector2.Zero;
	                Vector2 parentScaleWorld = Vector2.One;
	                float parentRotationDegrees = 0f;

	                if (parentShapeId != 0 && TryGetShapeIndexById(parentShapeId, out int parentShapeIndex))
	                {
	                    parentPositionWorld = shapeWorldPositions[parentShapeIndex];
	                    parentScaleWorld = shapeWorldScales[parentShapeIndex];
	                    parentRotationDegrees = shapeWorldRotationsDegrees[parentShapeIndex];
	                }
	                else if (parentGroupId != 0 && TryGetGroupIndexById(parentGroupId, out int parentGroupIndex))
	                {
	                    parentPositionWorld = groupWorldPositions[parentGroupIndex];
	                    parentScaleWorld = groupWorldScales[parentGroupIndex];
	                    parentRotationDegrees = groupWorldRotationsDegrees[parentGroupIndex];
	                }

	                groupWorldPositions[unresolvedGroupIndex] = parentPositionWorld;
	                groupWorldScales[unresolvedGroupIndex] = parentScaleWorld;
	                groupWorldRotationsDegrees[unresolvedGroupIndex] = parentRotationDegrees;
	                groupWorldResolved[unresolvedGroupIndex] = 2;
	            }
	        }

	        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
	        {
	            var group = _groups[groupIndex];
	            if (group.Transform.IsNull)
	            {
	                continue;
	            }

		            int parentGroupId = group.ParentGroupId;
		            int parentShapeId = group.ParentShapeId;
		            int parentFrameId = group.ParentFrameId;

	            var transform = TransformComponent.Api.FromHandle(_propertyWorld, group.Transform);
	            if (!transform.IsAlive)
	            {
	                continue;
	            }

	            Vector2 parentScaleWorld = Vector2.One;
	            float parentRotationDegrees = 0f;
	            Vector2 parentPositionWorld = Vector2.Zero;
	            bool hasParent = false;

	            if (parentShapeId != 0 && TryGetShapeIndexById(parentShapeId, out int parentShapeIndex))
	            {
	                parentPositionWorld = shapeWorldPositions[parentShapeIndex];
	                parentScaleWorld = shapeWorldScales[parentShapeIndex];
	                parentRotationDegrees = shapeWorldRotationsDegrees[parentShapeIndex];
	                hasParent = true;
	            }
	            else if (parentGroupId != 0 && TryGetGroupIndexById(parentGroupId, out int parentGroupIndex))
	            {
	                parentPositionWorld = groupWorldPositions[parentGroupIndex];
	                parentScaleWorld = groupWorldScales[parentGroupIndex];
	                parentRotationDegrees = groupWorldRotationsDegrees[parentGroupIndex];
	                hasParent = true;
	            }
	            else if (parentFrameId != 0 && TryGetPrefabIndexById(parentFrameId, out int parentPrefabIndex))
	            {
	                parentPositionWorld = prefabWorldPositions[parentPrefabIndex];
	                parentScaleWorld = prefabWorldScales[parentPrefabIndex];
	                parentRotationDegrees = prefabWorldRotationsDegrees[parentPrefabIndex];
	                hasParent = true;
	            }

	            if (!hasParent)
	            {
	                continue;
	            }

	            float parentRotationRadians = parentRotationDegrees * (MathF.PI / 180f);
	            var parentWorldTransform = new WorldTransform(parentPositionWorld, parentScaleWorld, parentRotationRadians, isVisible: true);

	            Vector2 groupPositionWorld = groupWorldPositions[groupIndex];
	            Vector2 groupPositionLocal = InverseTransformPoint(parentWorldTransform, groupPositionWorld);

	            float groupRotationLocal = WrapDegrees(groupWorldRotationsDegrees[groupIndex] - parentRotationDegrees);

	            Vector2 groupScaleWorld = groupWorldScales[groupIndex];
	            float localScaleX = parentScaleWorld.X == 0f ? groupScaleWorld.X : groupScaleWorld.X / parentScaleWorld.X;
	            float localScaleY = parentScaleWorld.Y == 0f ? groupScaleWorld.Y : groupScaleWorld.Y / parentScaleWorld.Y;

	            transform.Position = groupPositionLocal;
	            transform.Rotation = groupRotationLocal;
	            transform.Scale = new Vector2(localScaleX, localScaleY);
	        }

		        for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++)
		        {
		            var shape = _shapes[shapeIndex];
		            int parentGroupId = shape.ParentGroupId;
		            int parentShapeId = shape.ParentShapeId;
		            int parentFrameId = shape.ParentFrameId;

		            var shapeTransform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
		            if (!shapeTransform.IsAlive)
		            {
		                continue;
		            }

		            Vector2 parentScaleWorld = Vector2.One;
		            float parentRotationDegrees = 0f;
		            Vector2 parentPositionWorld = Vector2.Zero;
		            bool hasParent = false;

		            if (parentShapeId != 0 && TryGetShapeIndexById(parentShapeId, out int parentShapeIndex))
		            {
		                parentPositionWorld = shapeWorldPositions[parentShapeIndex];
		                parentScaleWorld = shapeWorldScales[parentShapeIndex];
		                parentRotationDegrees = shapeWorldRotationsDegrees[parentShapeIndex];
		                hasParent = true;
		            }
		            else if (parentGroupId != 0 && TryGetGroupIndexById(parentGroupId, out int parentGroupIndex))
		            {
		                parentPositionWorld = groupWorldPositions[parentGroupIndex];
		                parentScaleWorld = groupWorldScales[parentGroupIndex];
		                parentRotationDegrees = groupWorldRotationsDegrees[parentGroupIndex];
		                hasParent = true;
		            }
		            else if (parentFrameId != 0 && TryGetPrefabIndexById(parentFrameId, out int parentPrefabIndex))
		            {
		                parentPositionWorld = prefabWorldPositions[parentPrefabIndex];
		                parentScaleWorld = prefabWorldScales[parentPrefabIndex];
		                parentRotationDegrees = prefabWorldRotationsDegrees[parentPrefabIndex];
		                hasParent = true;
		            }

		            if (!hasParent)
		            {
		                continue;
		            }

	            float parentRotationRadians = parentRotationDegrees * (MathF.PI / 180f);
	            var parentWorldTransform = new WorldTransform(parentPositionWorld, parentScaleWorld, parentRotationRadians, isVisible: true);

	            Vector2 shapePositionWorld = shapeWorldPositions[shapeIndex];
	            Vector2 shapePositionLocal = InverseTransformPoint(parentWorldTransform, shapePositionWorld);

	            float shapeRotationLocal = WrapDegrees(shapeWorldRotationsDegrees[shapeIndex] - parentRotationDegrees);

	            Vector2 shapeScaleWorld = shapeWorldScales[shapeIndex];
	            float localScaleX = parentScaleWorld.X == 0f ? shapeScaleWorld.X : shapeScaleWorld.X / parentScaleWorld.X;
	            float localScaleY = parentScaleWorld.Y == 0f ? shapeScaleWorld.Y : shapeScaleWorld.Y / parentScaleWorld.Y;

		            shapeTransform.Position = shapePositionLocal;
		            shapeTransform.Rotation = shapeRotationLocal;
		            shapeTransform.Scale = new Vector2(localScaleX, localScaleY);
		        }

		        for (int prefabIndex = 0; prefabIndex < prefabCount; prefabIndex++)
		        {
		            var prefab = _prefabs[prefabIndex];
		            if (prefab.Animations == null)
		            {
		                continue;
		            }

		            if (!TryGetEntityById(_prefabEntityById, prefab.Id, out EntityId prefabEntity) || prefabEntity.IsNull)
		            {
		                continue;
		            }

		            float rotationRadians = prefabWorldRotationsDegrees[prefabIndex] * (MathF.PI / 180f);
		            var prefabWorldTransform = new WorldTransform(prefabWorldPositions[prefabIndex], prefabWorldScales[prefabIndex], rotationRadians, isVisible: true);
		            ConvertPrefabDirectChildPositionKeysToLocal(prefab.Animations, prefabEntity, prefabWorldTransform);
		        }

	        _hierarchyTransformsAreLocal = true;
	    }

	    private void ConvertPrefabDirectChildPositionKeysToLocal(AnimationDocument animations, EntityId prefabEntity, in WorldTransform prefabWorldTransform)
	    {
	        if (animations.Timelines.Count <= 0)
	        {
	            return;
	        }

	        ReadOnlySpan<EntityId> children = _world.GetChildren(prefabEntity);
	        if (children.Length <= 0)
	        {
	            return;
	        }

	        Span<uint> childStableIds = children.Length <= 128 ? stackalloc uint[128] : new uint[children.Length];
	        int childStableIdCount = 0;

	        for (int i = 0; i < children.Length; i++)
	        {
	            EntityId child = children[i];
	            UiNodeType type = _world.GetNodeType(child);
	            if (type != UiNodeType.Shape && type != UiNodeType.BooleanGroup)
	            {
	                continue;
	            }

	            uint stableId = _world.GetStableId(child);
	            if (stableId == 0)
	            {
	                continue;
	            }

	            childStableIds[childStableIdCount++] = stableId;
	        }

	        if (childStableIdCount <= 0)
	        {
	            return;
	        }

	        if (!TryGetTransformPositionChannelGroupId(prefabEntity, children, out ulong positionChannelGroupId))
	        {
	            return;
	        }

	        float scaleX = prefabWorldTransform.Scale.X == 0f ? 1f : prefabWorldTransform.Scale.X;
	        float scaleY = prefabWorldTransform.Scale.Y == 0f ? 1f : prefabWorldTransform.Scale.Y;

	        for (int timelineIndex = 0; timelineIndex < animations.Timelines.Count; timelineIndex++)
	        {
	            AnimationDocument.AnimationTimeline timeline = animations.Timelines[timelineIndex];
	            if (timeline.Targets.Count <= 0 || timeline.Tracks.Count <= 0)
	            {
	                continue;
	            }

	            for (int targetIndex = 0; targetIndex < timeline.Targets.Count; targetIndex++)
	            {
	                uint targetStableId = timeline.Targets[targetIndex].StableId;
	                if (!ContainsStableId(childStableIds, childStableIdCount, targetStableId))
	                {
	                    continue;
	                }

	                AnimationDocument.AnimationTrack? xTrack = null;
	                AnimationDocument.AnimationTrack? yTrack = null;
	                AnimationDocument.AnimationTrack? vec2Track = null;

	                for (int trackIndex = 0; trackIndex < timeline.Tracks.Count; trackIndex++)
	                {
	                    AnimationDocument.AnimationTrack track = timeline.Tracks[trackIndex];
	                    AnimationDocument.AnimationBinding binding = track.Binding;
	                    if (binding.TargetIndex != targetIndex)
	                    {
	                        continue;
	                    }

	                    if (binding.ComponentKind != TransformComponent.Api.PoolIdConst)
	                    {
	                        continue;
	                    }

	                    if (!AnimationEditorHelpers.TryResolveSlot(this, timeline, binding, out PropertySlot slot))
	                    {
	                        continue;
	                    }

	                    if (!PropertyDispatcher.TryGetInfo(slot.Component, (int)slot.PropertyIndex, out PropertyInfo info))
	                    {
	                        continue;
	                    }

	                    if (binding.PropertyKind == PropertyKind.Float && info.IsChannel && info.ChannelGroupId == positionChannelGroupId)
	                    {
	                        if (info.ChannelIndex == 0)
	                        {
	                            xTrack = track;
	                        }
	                        else if (info.ChannelIndex == 1)
	                        {
	                            yTrack = track;
	                        }
	                        continue;
	                    }

	                    if (binding.PropertyKind == PropertyKind.Vec2 && !info.IsChannel && info.Kind == PropertyKind.Vec2 && info.Order == 0)
	                    {
	                        vec2Track = track;
	                    }
	                }

	                if (xTrack == null && yTrack == null)
	                {
	                    if (vec2Track == null || vec2Track.Keys.Count <= 0)
	                    {
	                        continue;
	                    }

	                    for (int keyIndex = 0; keyIndex < vec2Track.Keys.Count; keyIndex++)
	                    {
	                        var k = vec2Track.Keys[keyIndex];
	                        Vector2 world = k.Value.Vec2;
	                        Vector2 local = InverseTransformPoint(prefabWorldTransform, world);
	                        k.Value = PropertyValue.FromVec2(local);
	                        vec2Track.Keys[keyIndex] = k;
	                    }

	                    continue;
	                }

	                AnimationDocument.AnimationKeyframe[]? xKeysWorld = xTrack == null ? null : CopyKeys(xTrack.Keys);
	                AnimationDocument.AnimationKeyframe[]? yKeysWorld = yTrack == null ? null : CopyKeys(yTrack.Keys);

	                if (xTrack != null && xTrack.Keys.Count > 0)
	                {
	                    for (int keyIndex = 0; keyIndex < xTrack.Keys.Count; keyIndex++)
	                    {
	                        AnimationDocument.AnimationKeyframe worldKey = xKeysWorld![keyIndex];
	                        float worldX = worldKey.Value.Float;
	                        float localX;

	                        if (yKeysWorld != null && TryEvaluateFloatKeysAtFrame(yKeysWorld, worldKey.Frame, out float worldY))
	                        {
	                            Vector2 local = InverseTransformPoint(prefabWorldTransform, new Vector2(worldX, worldY));
	                            localX = local.X;
	                        }
	                        else
	                        {
	                            localX = (worldX - prefabWorldTransform.OriginWorld.X) / scaleX;
	                        }

	                        var k = xTrack.Keys[keyIndex];
	                        k.Value = PropertyValue.FromFloat(localX);
	                        xTrack.Keys[keyIndex] = k;
	                    }
	                }

	                if (yTrack != null && yTrack.Keys.Count > 0)
	                {
	                    for (int keyIndex = 0; keyIndex < yTrack.Keys.Count; keyIndex++)
	                    {
	                        AnimationDocument.AnimationKeyframe worldKey = yKeysWorld![keyIndex];
	                        float worldY = worldKey.Value.Float;
	                        float localY;

	                        if (xKeysWorld != null && TryEvaluateFloatKeysAtFrame(xKeysWorld, worldKey.Frame, out float worldX))
	                        {
	                            Vector2 local = InverseTransformPoint(prefabWorldTransform, new Vector2(worldX, worldY));
	                            localY = local.Y;
	                        }
	                        else
	                        {
	                            localY = (worldY - prefabWorldTransform.OriginWorld.Y) / scaleY;
	                        }

	                        var k = yTrack.Keys[keyIndex];
	                        k.Value = PropertyValue.FromFloat(localY);
	                        yTrack.Keys[keyIndex] = k;
	                    }
	                }
	            }
	        }
	    }

	    private bool TryGetTransformPositionChannelGroupId(EntityId prefabEntity, ReadOnlySpan<EntityId> prefabChildren, out ulong channelGroupId)
	    {
	        channelGroupId = 0;

	        if (TryGetTransformPositionChannelGroupId(prefabEntity, out channelGroupId))
	        {
	            return true;
	        }

	        for (int i = 0; i < prefabChildren.Length; i++)
	        {
	            EntityId child = prefabChildren[i];
	            UiNodeType type = _world.GetNodeType(child);
	            if (type != UiNodeType.Shape && type != UiNodeType.BooleanGroup)
	            {
	                continue;
	            }

	            if (TryGetTransformPositionChannelGroupId(child, out channelGroupId))
	            {
	                return true;
	            }
	        }

	        return false;
	    }

	    private bool TryGetTransformPositionChannelGroupId(EntityId entity, out ulong channelGroupId)
	    {
	        channelGroupId = 0;
	        if (!_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
	        {
	            return false;
	        }

	        int propertyCount = PropertyDispatcher.GetPropertyCount(transformAny);
	        for (ushort propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
	        {
	            if (!PropertyDispatcher.TryGetInfo(transformAny, propertyIndex, out PropertyInfo info))
	            {
	                continue;
	            }

	            if (!info.HasChannels || info.IsChannel || info.Kind != PropertyKind.Vec2 || info.Order != 0)
	            {
	                continue;
	            }

	            channelGroupId = info.ChannelGroupId;
	            return channelGroupId != 0;
	        }

	        return false;
	    }

	    private static bool ContainsStableId(ReadOnlySpan<uint> stableIds, int count, uint stableId)
	    {
	        if (stableId == 0)
	        {
	            return false;
	        }

	        for (int i = 0; i < count; i++)
	        {
	            if (stableIds[i] == stableId)
	            {
	                return true;
	            }
	        }

	        return false;
	    }

	    private static AnimationDocument.AnimationKeyframe[] CopyKeys(List<AnimationDocument.AnimationKeyframe> keys)
	    {
	        int count = keys.Count;
	        var copy = new AnimationDocument.AnimationKeyframe[count];
	        for (int i = 0; i < count; i++)
	        {
	            copy[i] = keys[i];
	        }
	        return copy;
	    }

	    private static bool TryEvaluateFloatKeysAtFrame(ReadOnlySpan<AnimationDocument.AnimationKeyframe> keys, int frame, out float value)
	    {
	        value = 0f;
	        int keyCount = keys.Length;
	        if (keyCount <= 0)
	        {
	            return false;
	        }

	        int firstFrame = keys[0].Frame;
	        if (frame <= firstFrame || keyCount == 1)
	        {
	            value = keys[0].Value.Float;
	            return true;
	        }

	        int lastIndex = keyCount - 1;
	        int lastFrame = keys[lastIndex].Frame;
	        if (frame >= lastFrame)
	        {
	            value = keys[lastIndex].Value.Float;
	            return true;
	        }

	        int rightIndex = -1;
	        for (int i = 1; i < keyCount; i++)
	        {
	            if (keys[i].Frame >= frame)
	            {
	                rightIndex = i;
	                break;
	            }
	        }

	        if (rightIndex <= 0)
	        {
	            value = keys[0].Value.Float;
	            return true;
	        }

	        int leftIndex = rightIndex - 1;
	        var a = keys[leftIndex];
	        var b = keys[rightIndex];

	        if (a.Frame == b.Frame)
	        {
	            value = a.Value.Float;
	            return true;
	        }

	        float t = (frame - a.Frame) / (float)(b.Frame - a.Frame);
	        t = Math.Clamp(t, 0f, 1f);

	        if (a.Interpolation == AnimationDocument.Interpolation.Step)
	        {
	            value = a.Value.Float;
	            return true;
	        }

	        float af = a.Value.Float;
	        float bf = b.Value.Float;

	        value = a.Interpolation == AnimationDocument.Interpolation.Linear
	            ? af + (bf - af) * t
	            : HermiteInterpolateFloat(af, a.OutTangent, bf, b.InTangent, t);
	        return true;
	    }

	    private static float HermiteInterpolateFloat(float p0, float m0, float p1, float m1, float t)
	    {
	        float t2 = t * t;
	        float t3 = t2 * t;

	        float h00 = 2f * t3 - 3f * t2 + 1f;
	        float h10 = t3 - 2f * t2 + t;
	        float h01 = -2f * t3 + 3f * t2;
	        float h11 = t3 - t2;

	        return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
	    }

}
