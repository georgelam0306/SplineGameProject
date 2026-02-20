using System;
using System.Numerics;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    internal bool TryGetParentLocalPointFromWorldEcs(EntityId parentEntity, Vector2 worldPoint, out Vector2 localPoint)
    {
        localPoint = worldPoint;

        if (parentEntity.IsNull)
        {
            return true;
        }

        if (!TryGetEntityWorldTransformEcs(parentEntity, out WorldTransform parentWorldTransform))
        {
            return false;
        }

        localPoint = InverseTransformPoint(parentWorldTransform, worldPoint);
        return true;
    }

    internal bool TryPreserveWorldTransformOnReparent(EntityId entity, EntityId oldParent, EntityId newParent)
    {
        if (entity.IsNull)
        {
            return false;
        }

        if (!_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            return true;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
        if (!transform.IsAlive)
        {
            return false;
        }

        WorldTransform oldParentWorld = IdentityWorldTransform;
        if (!oldParent.IsNull)
        {
            if (!TryGetEntityWorldTransformEcs(oldParent, out oldParentWorld))
            {
                return false;
            }
        }

        WorldTransform newParentWorld = IdentityWorldTransform;
        if (!newParent.IsNull)
        {
            if (!TryGetEntityWorldTransformEcs(newParent, out newParentWorld))
            {
                return false;
            }
        }

        Vector2 localScale = NormalizeScale(transform.Scale);
        float oldParentRotationDegrees = oldParentWorld.RotationRadians * (180f / MathF.PI);
        float newParentRotationDegrees = newParentWorld.RotationRadians * (180f / MathF.PI);

        Vector2 worldPosition = TransformPoint(oldParentWorld, transform.Position);
        Vector2 worldScale = new Vector2(oldParentWorld.Scale.X * localScale.X, oldParentWorld.Scale.Y * localScale.Y);
        float worldRotationDegrees = WrapDegrees(oldParentRotationDegrees + transform.Rotation);

        Vector2 newLocalPosition = InverseTransformPoint(newParentWorld, worldPosition);
        float newLocalScaleX = newParentWorld.Scale.X == 0f ? worldScale.X : worldScale.X / newParentWorld.Scale.X;
        float newLocalScaleY = newParentWorld.Scale.Y == 0f ? worldScale.Y : worldScale.Y / newParentWorld.Scale.Y;
        float newLocalRotationDegrees = WrapDegrees(worldRotationDegrees - newParentRotationDegrees);

        transform.Position = newLocalPosition;
        transform.Scale = new Vector2(newLocalScaleX, newLocalScaleY);
        transform.Rotation = newLocalRotationDegrees;
        return true;
    }

    private bool TryGetEntityWorldTransformEcs(EntityId entity, out WorldTransform worldTransform)
    {
        worldTransform = IdentityWorldTransform;
        if (entity.IsNull)
        {
            return true;
        }

        Span<EntityId> stack = stackalloc EntityId[64];
        int count = 0;

        EntityId current = entity;
        while (!current.IsNull)
        {
            UiNodeType type = _world.GetNodeType(current);
            if (type == UiNodeType.None)
            {
                current = _world.GetParent(current);
                continue;
            }

            if (count >= stack.Length)
            {
                return false;
            }

            stack[count++] = current;
            current = _world.GetParent(current);
        }

        WorldTransform accumulated = IdentityWorldTransform;
        for (int i = count - 1; i >= 0; i--)
        {
            EntityId node = stack[i];
            UiNodeType type = _world.GetNodeType(node);
            if (type == UiNodeType.Prefab)
            {
                if (!_world.TryGetComponent(node, TransformComponent.Api.PoolIdConst, out AnyComponentHandle prefabTransformAny))
                {
                    continue;
                }

                var prefabTransformHandle = new TransformComponentHandle(prefabTransformAny.Index, prefabTransformAny.Generation);
                var prefabTransform = TransformComponent.Api.FromHandle(_propertyWorld, prefabTransformHandle);
                if (!prefabTransform.IsAlive)
                {
                    return false;
                }

                Vector2 localPosition = prefabTransform.Position;
                if (TryGetComputedTransform(node, out ComputedTransformComponentHandle computedTransformHandle))
                {
                    var computedTransform = ComputedTransformComponent.Api.FromHandle(_propertyWorld, computedTransformHandle);
                    if (computedTransform.IsAlive)
                    {
                        localPosition = computedTransform.Position;
                    }
                }

                Vector2 prefabScale = NormalizeScale(prefabTransform.Scale);
                float prefabRotationRadians = prefabTransform.Rotation * (MathF.PI / 180f);
                accumulated = ComposeTransform(accumulated, localPosition, prefabScale, prefabRotationRadians, localIsVisible: true);
                continue;
            }

            if (type == UiNodeType.BooleanGroup)
            {
                if (!TryGetGroupWorldTransformEcs(node, accumulated, out WorldTransform groupWorldTransform, out _, out _))
                {
                    return false;
                }

                accumulated = groupWorldTransform;
                continue;
            }

            if (type == UiNodeType.PrefabInstance)
            {
                if (!TryGetGroupWorldTransformEcs(node, accumulated, out WorldTransform instanceWorldTransform, out _, out _))
                {
                    return false;
                }

                accumulated = instanceWorldTransform;
                continue;
            }

            if (type == UiNodeType.Shape)
            {
                if (!TryGetShapeWorldTransformEcs(node, accumulated, out ShapeWorldTransform shapeWorldTransform))
                {
                    return false;
                }

                accumulated = new WorldTransform(
                    shapeWorldTransform.PositionWorld,
                    shapeWorldTransform.ScaleWorld,
                    shapeWorldTransform.RotationRadians,
                    shapeWorldTransform.IsVisible);
                continue;
            }

            if (type == UiNodeType.Text)
            {
                if (!TryGetTextWorldTransformEcs(node, accumulated, out ShapeWorldTransform textWorldTransform))
                {
                    return false;
                }

                accumulated = new WorldTransform(
                    textWorldTransform.PositionWorld,
                    textWorldTransform.ScaleWorld,
                    textWorldTransform.RotationRadians,
                    textWorldTransform.IsVisible);
                continue;
            }
        }

        worldTransform = accumulated;
        return true;
    }

    private bool TryGetTransformParentWorldTransformEcs(EntityId entity, out WorldTransform parentWorldTransform)
    {
        parentWorldTransform = IdentityWorldTransform;
        if (entity.IsNull)
        {
            return true;
        }

        EntityId parent = _world.GetParent(entity);
        if (parent.IsNull)
        {
            return true;
        }

        return TryGetEntityWorldTransformEcs(parent, out parentWorldTransform);
    }

    internal void TranslateEntityPositionEcs(EntityId entity, Vector2 deltaWorld)
    {
        if (entity.IsNull)
        {
            return;
        }

        if (!_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out var transformAny))
        {
            return;
        }

        var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
        if (!transform.IsAlive)
        {
            return;
        }

        if (!TryGetTransformParentWorldTransformEcs(entity, out WorldTransform parentWorldTransform))
        {
            parentWorldTransform = IdentityWorldTransform;
        }

        Vector2 deltaLocal = InverseTransformVector(parentWorldTransform, deltaWorld);
        transform.Position = new Vector2(transform.Position.X + deltaLocal.X, transform.Position.Y + deltaLocal.Y);
    }
}
