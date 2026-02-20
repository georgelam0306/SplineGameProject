using System;
using System.Numerics;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private int _computedLayoutEnsureNextEntityIndex = 1;

    private void EnsureComputedLayoutComponentsForNewEntities()
    {
        int entityCount = _world.EntityCount;
        if (_computedLayoutEnsureNextEntityIndex < 1)
        {
            _computedLayoutEnsureNextEntityIndex = 1;
        }

        if (_computedLayoutEnsureNextEntityIndex > entityCount)
        {
            return;
        }

        for (int entityIndex = _computedLayoutEnsureNextEntityIndex; entityIndex <= entityCount; entityIndex++)
        {
            EntityId entity = new EntityId(entityIndex);
            if (_world.GetNodeType(entity) == UiNodeType.None)
            {
                continue;
            }

            if (!_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) || !transformAny.IsValid)
            {
                continue;
            }

            if (!_world.TryGetComponent(entity, ComputedTransformComponent.Api.PoolIdConst, out AnyComponentHandle computedTransformAny) || !computedTransformAny.IsValid)
            {
                Vector2 position = Vector2.Zero;
                var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
                var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
                if (transform.IsAlive)
                {
                    position = transform.Position;
                }

                var created = ComputedTransformComponent.Api.Create(_propertyWorld, new ComputedTransformComponent
                {
                    Position = position
                });
                SetComponentWithStableId(entity, new AnyComponentHandle(ComputedTransformComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
            }

            if (!_world.TryGetComponent(entity, ComputedSizeComponent.Api.PoolIdConst, out AnyComponentHandle computedSizeAny) || !computedSizeAny.IsValid)
            {
                Vector2 size = GetPreferredSizeForComputed(entity);
                var created = ComputedSizeComponent.Api.Create(_propertyWorld, new ComputedSizeComponent
                {
                    Size = size
                });
                SetComponentWithStableId(entity, new AnyComponentHandle(ComputedSizeComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
            }

            UiNodeType type = _world.GetNodeType(entity);
            if (type == UiNodeType.Shape || type == UiNodeType.BooleanGroup || type == UiNodeType.Prefab)
            {
                if (!_world.TryGetComponent(entity, ClipRectComponent.Api.PoolIdConst, out AnyComponentHandle clipAny) || !clipAny.IsValid)
                {
                    bool enabledByDefault = type == UiNodeType.Prefab;
                    var created = ClipRectComponent.Api.Create(_propertyWorld, new ClipRectComponent
                    {
                        Enabled = enabledByDefault
                    });
                    SetComponentWithStableId(entity, new AnyComponentHandle(ClipRectComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
                }

                if (!_world.TryGetComponent(entity, DraggableComponent.Api.PoolIdConst, out AnyComponentHandle dragAny) || !dragAny.IsValid)
                {
                    bool enabled = false;
                    if (_world.TryGetComponent(entity, ConstraintListComponent.Api.PoolIdConst, out AnyComponentHandle constraintsAny) && constraintsAny.IsValid)
                    {
                        var constraintsHandle = new ConstraintListComponentHandle(constraintsAny.Index, constraintsAny.Generation);
                        var constraints = ConstraintListComponent.Api.FromHandle(_propertyWorld, constraintsHandle);
                        if (constraints.IsAlive)
                        {
                            ushort count = constraints.Count;
                            if (count > ConstraintListComponent.MaxConstraints)
                            {
                                count = ConstraintListComponent.MaxConstraints;
                            }

                            ReadOnlySpan<byte> enabledValue = constraints.EnabledValueReadOnlySpan();
                            ReadOnlySpan<byte> kindValue = constraints.KindValueReadOnlySpan();
                            for (int constraintIndex = 0; constraintIndex < count; constraintIndex++)
                            {
                                if (enabledValue[constraintIndex] == 0)
                                {
                                    continue;
                                }

                                if ((ConstraintListComponent.ConstraintKind)kindValue[constraintIndex] == ConstraintListComponent.ConstraintKind.Scroll)
                                {
                                    enabled = true;
                                    break;
                                }
                            }
                        }
                    }

                    var created = DraggableComponent.Api.Create(_propertyWorld, new DraggableComponent
                    {
                        Enabled = enabled
                    });
                    SetComponentWithStableId(entity, new AnyComponentHandle(DraggableComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
                }
            }

            if (type == UiNodeType.Shape || type == UiNodeType.BooleanGroup || type == UiNodeType.Text || type == UiNodeType.Prefab || type == UiNodeType.PrefabInstance)
            {
                if (!_world.TryGetComponent(entity, ModifierStackComponent.Api.PoolIdConst, out AnyComponentHandle modifiersAny) || !modifiersAny.IsValid)
                {
                    var created = ModifierStackComponent.Api.Create(_propertyWorld, new ModifierStackComponent { Count = 0 });
                    SetComponentWithStableId(entity, new AnyComponentHandle(ModifierStackComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
                }
            }

            if (type == UiNodeType.Shape || type == UiNodeType.BooleanGroup || type == UiNodeType.Text || type == UiNodeType.PrefabInstance)
            {
                if (!_world.TryGetComponent(entity, ConstraintListComponent.Api.PoolIdConst, out AnyComponentHandle constraintsAny) || !constraintsAny.IsValid)
                {
                    var created = ConstraintListComponent.Api.Create(_propertyWorld, new ConstraintListComponent { Count = 0 });
                    SetComponentWithStableId(entity, new AnyComponentHandle(ConstraintListComponent.Api.PoolIdConst, created.Handle.Index, created.Handle.Generation));
                }
            }
        }

        _computedLayoutEnsureNextEntityIndex = entityCount + 1;
    }

    private void ResetComputedLayoutFromPreferred()
    {
        int entityCount = _world.EntityCount;
        for (int entityIndex = 1; entityIndex <= entityCount; entityIndex++)
        {
            EntityId entity = new EntityId(entityIndex);
            if (_world.GetNodeType(entity) == UiNodeType.None)
            {
                continue;
            }

            if (_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) && transformAny.IsValid &&
                _world.TryGetComponent(entity, ComputedTransformComponent.Api.PoolIdConst, out AnyComponentHandle computedTransformAny) && computedTransformAny.IsValid)
            {
                var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
                var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
                if (transform.IsAlive)
                {
                    var computedHandle = new ComputedTransformComponentHandle(computedTransformAny.Index, computedTransformAny.Generation);
                    var computed = ComputedTransformComponent.Api.FromHandle(_propertyWorld, computedHandle);
                    if (computed.IsAlive)
                    {
                        computed.Position = transform.Position;
                    }
                }
            }

            if (_world.TryGetComponent(entity, ComputedSizeComponent.Api.PoolIdConst, out AnyComponentHandle computedSizeAny) && computedSizeAny.IsValid)
            {
                var computedHandle = new ComputedSizeComponentHandle(computedSizeAny.Index, computedSizeAny.Generation);
                var computed = ComputedSizeComponent.Api.FromHandle(_propertyWorld, computedHandle);
                if (computed.IsAlive)
                {
                    computed.Size = GetPreferredSizeForComputed(entity);
                }
            }
        }
    }

    private Vector2 GetPreferredSizeForComputed(EntityId entity)
    {
        if (_world.TryGetComponent(entity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny) && canvasAny.IsValid)
        {
            var canvasHandle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
            var canvas = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, canvasHandle);
            if (canvas.IsAlive)
            {
                return canvas.Size;
            }
        }

        if (_world.TryGetComponent(entity, RectGeometryComponent.Api.PoolIdConst, out AnyComponentHandle rectAny) && rectAny.IsValid)
        {
            var rectHandle = new RectGeometryComponentHandle(rectAny.Index, rectAny.Generation);
            var rect = RectGeometryComponent.Api.FromHandle(_propertyWorld, rectHandle);
            if (rect.IsAlive)
            {
                return rect.Size;
            }
        }

        if (_world.GetNodeType(entity) == UiNodeType.PrefabInstance &&
            _world.TryGetComponent(entity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) &&
            instanceAny.IsValid)
        {
            var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
            var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
            if (instance.IsAlive && instance.SourcePrefabStableId != 0)
            {
                EntityId sourcePrefabEntity = _world.GetEntityByStableId(instance.SourcePrefabStableId);
                if (!sourcePrefabEntity.IsNull && _world.GetNodeType(sourcePrefabEntity) == UiNodeType.Prefab)
                {
                    if (_world.TryGetComponent(sourcePrefabEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle sourceCanvasAny) && sourceCanvasAny.IsValid)
                    {
                        var sourceCanvasHandle = new PrefabCanvasComponentHandle(sourceCanvasAny.Index, sourceCanvasAny.Generation);
                        var sourceCanvas = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, sourceCanvasHandle);
                        if (sourceCanvas.IsAlive)
                        {
                            Vector2 sourceSize = sourceCanvas.Size;
                            Vector2 scale = Vector2.One;
                            if (_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny) && transformAny.IsValid)
                            {
                                var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
                                var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
                                if (transform.IsAlive)
                                {
                                    scale = NormalizeScale(transform.Scale);
                                }
                            }

                            return new Vector2(sourceSize.X * MathF.Abs(scale.X), sourceSize.Y * MathF.Abs(scale.Y));
                        }
                    }
                }
            }
        }

        return Vector2.Zero;
    }

    private bool TryGetComputedSize(EntityId entity, out Vector2 size)
    {
        size = Vector2.Zero;
        if (!_world.TryGetComponent(entity, ComputedSizeComponent.Api.PoolIdConst, out AnyComponentHandle computedAny) || !computedAny.IsValid)
        {
            return false;
        }

        var handle = new ComputedSizeComponentHandle(computedAny.Index, computedAny.Generation);
        var computed = ComputedSizeComponent.Api.FromHandle(_propertyWorld, handle);
        if (!computed.IsAlive)
        {
            return false;
        }

        size = computed.Size;
        return true;
    }
}
