using System;
using System.Numerics;
using DerpLib.ImGui.Core;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private enum LayoutSizeTargetKind : byte
    {
        None = 0,
        ComputedSize = 1,
    }

    private readonly struct LayoutSizeTarget
    {
        public readonly LayoutSizeTargetKind Kind;
        public readonly AnyComponentHandle Handle;
        public readonly EntityId Entity;

        public LayoutSizeTarget(LayoutSizeTargetKind kind, AnyComponentHandle handle, EntityId entity)
        {
            Kind = kind;
            Handle = handle;
            Entity = entity;
        }
    }

    private readonly struct LayoutStackItem
    {
        public readonly EntityId Entity;
        public readonly ImRect RectLocal;

        public LayoutStackItem(EntityId entity, ImRect rectLocal)
        {
            Entity = entity;
            RectLocal = rectLocal;
        }
    }

    private readonly struct LayoutMeasureStackItem
    {
        public readonly EntityId Entity;
        public readonly byte Phase;

        public LayoutMeasureStackItem(EntityId entity, byte phase)
        {
            Entity = entity;
            Phase = phase;
        }
    }

    private void PreMeasureHugContainerSizes()
    {
        Span<LayoutMeasureStackItem> stack = stackalloc LayoutMeasureStackItem[256];
        int stackCount = 0;

        ReadOnlySpan<EntityId> rootChildren = _world.GetChildren(EntityId.Null);
        for (int rootIndex = rootChildren.Length - 1; rootIndex >= 0; rootIndex--)
        {
            EntityId rootEntity = rootChildren[rootIndex];
            if (_world.GetNodeType(rootEntity) != UiNodeType.Prefab)
            {
                continue;
            }

            if (stackCount >= stack.Length)
            {
                return;
            }

            stack[stackCount++] = new LayoutMeasureStackItem(rootEntity, phase: 0);
        }

        while (stackCount > 0)
        {
            LayoutMeasureStackItem item = stack[--stackCount];
            if (item.Phase == 0)
            {
                if (stackCount >= stack.Length)
                {
                    return;
                }

                stack[stackCount++] = new LayoutMeasureStackItem(item.Entity, phase: 1);

                ReadOnlySpan<EntityId> children = _world.GetChildren(item.Entity);
                for (int childIndex = children.Length - 1; childIndex >= 0; childIndex--)
                {
                    EntityId child = children[childIndex];
                    if (_world.GetNodeType(child) == UiNodeType.None)
                    {
                        continue;
                    }

                    if (stackCount >= stack.Length)
                    {
                        return;
                    }

                    stack[stackCount++] = new LayoutMeasureStackItem(child, phase: 0);
                }

                continue;
            }

            MeasureHugContainerSize(item.Entity);
            MeasureBooleanGroupIntrinsicSize(item.Entity);
        }
    }

    private void MeasureBooleanGroupIntrinsicSize(EntityId entity)
    {
        if (_world.GetNodeType(entity) != UiNodeType.BooleanGroup)
        {
            return;
        }

        if (!TryGetEntitySizeTarget(entity, out Vector2 currentSize, out LayoutSizeTarget sizeTarget))
        {
            return;
        }

        if (currentSize.X > 0f && currentSize.Y > 0f)
        {
            return;
        }

        ReadOnlySpan<EntityId> children = _world.GetChildren(entity);
        if (children.IsEmpty)
        {
            return;
        }

        bool hasBounds = false;
        float minX = 0f;
        float minY = 0f;
        float maxX = 0f;
        float maxY = 0f;

        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            EntityId child = children[childIndex];
            if (!TryGetTransform(child, out TransformComponentHandle childTransformHandle))
            {
                continue;
            }

            var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, childTransformHandle);
            if (!childTransform.IsAlive)
            {
                continue;
            }

            if (!TryGetComputedTransform(child, out ComputedTransformComponentHandle computedTransformHandle))
            {
                continue;
            }

            var computedTransform = ComputedTransformComponent.Api.FromHandle(_propertyWorld, computedTransformHandle);
            if (!computedTransform.IsAlive)
            {
                continue;
            }

            if (!TryGetEntityIntrinsicSize(child, out Vector2 size, out _))
            {
                continue;
            }

            Vector2 pivotPosLocal = computedTransform.Position;
            Vector2 pivot01 = childTransform.Anchor;
            ImRect rectLocal = RectTransformMath.GetRectFromPivot(pivotPosLocal, pivot01, size);

            if (!hasBounds)
            {
                hasBounds = true;
                minX = rectLocal.X;
                minY = rectLocal.Y;
                maxX = rectLocal.Right;
                maxY = rectLocal.Bottom;
            }
            else
            {
                if (rectLocal.X < minX) minX = rectLocal.X;
                if (rectLocal.Y < minY) minY = rectLocal.Y;
                if (rectLocal.Right > maxX) maxX = rectLocal.Right;
                if (rectLocal.Bottom > maxY) maxY = rectLocal.Bottom;
            }
        }

        if (!hasBounds)
        {
            return;
        }

        float width = maxX - minX;
        float height = maxY - minY;
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        TrySetEntitySize(sizeTarget, new Vector2(width, height));
    }

    internal void ResolveLayout()
    {
        EnsureComputedLayoutComponentsForNewEntities();
        ResetComputedLayoutFromPreferred();

        PreMeasureHugContainerSizes();

        Span<LayoutStackItem> stack = stackalloc LayoutStackItem[256];
        int stackCount = 0;

        ReadOnlySpan<EntityId> rootChildren = _world.GetChildren(EntityId.Null);
        for (int rootIndex = rootChildren.Length - 1; rootIndex >= 0; rootIndex--)
        {
            EntityId rootEntity = rootChildren[rootIndex];
            if (_world.GetNodeType(rootEntity) != UiNodeType.Prefab)
            {
                continue;
            }

            if (!TryGetEntityRectLocalForLayout(rootEntity, out ImRect rectLocal))
            {
                continue;
            }

            if (stackCount >= stack.Length)
            {
                return;
            }

            stack[stackCount++] = new LayoutStackItem(rootEntity, rectLocal);
        }

        while (stackCount > 0)
        {
            LayoutStackItem parentItem = stack[--stackCount];
            EntityId parentEntity = parentItem.Entity;
            ImRect parentRectLocal = parentItem.RectLocal;

            if (TryGetTransform(parentEntity, out TransformComponentHandle parentTransformHandle))
            {
                var parentTransform = TransformComponent.Api.FromHandle(_propertyWorld, parentTransformHandle);
                if (parentTransform.IsAlive && parentTransform.LayoutContainerEnabled)
                {
                    ApplyContainerLayout(parentEntity, parentRectLocal, parentTransformHandle);
                }
                else
                {
                    ApplyConstraintsToChildren(parentEntity, parentRectLocal);
                }
            }
            else
            {
                ApplyConstraintsToChildren(parentEntity, parentRectLocal);
            }

            ReadOnlySpan<EntityId> children = _world.GetChildren(parentEntity);
            for (int childIndex = children.Length - 1; childIndex >= 0; childIndex--)
            {
                EntityId child = children[childIndex];
                if (!TryGetEntityRectLocalForLayout(child, out ImRect childRectLocal))
                {
                    continue;
                }

                if (stackCount >= stack.Length)
                {
                    return;
                }

                stack[stackCount++] = new LayoutStackItem(child, childRectLocal);
            }
        }

        ApplyConstraintListPass();
    }

    private void ApplyConstraintsToChildren(EntityId parentEntity, ImRect parentRectLocal)
    {
        ReadOnlySpan<EntityId> children = _world.GetChildren(parentEntity);
        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            EntityId child = children[childIndex];
            if (!TryGetTransform(child, out TransformComponentHandle childTransformHandle))
            {
                continue;
            }

            ApplyConstraint(parentRectLocal, child, childTransformHandle);
        }
    }

    private void ApplyContainerLayout(EntityId parentEntity, ImRect parentRectLocal, TransformComponentHandle parentTransformHandle)
    {
        var parentTransform = TransformComponent.Api.FromHandle(_propertyWorld, parentTransformHandle);
        if (!parentTransform.IsAlive)
        {
            ApplyConstraintsToChildren(parentEntity, parentRectLocal);
            return;
        }

        var layoutType = (LayoutType)Math.Clamp(parentTransform.LayoutContainerLayout, 0, 2);
        if (layoutType == LayoutType.Stack)
        {
            ApplyStackLayout(parentEntity, parentRectLocal, parentTransformHandle);
            return;
        }

        if (layoutType == LayoutType.Flex)
        {
            ApplyFlexLayout(parentEntity, parentRectLocal, parentTransformHandle);
            return;
        }

        ApplyGridLayout(parentEntity, parentRectLocal, parentTransformHandle);
    }

    private void ApplyStackLayout(EntityId parentEntity, ImRect parentRectLocal, TransformComponentHandle parentTransformHandle)
    {
        var parentTransform = TransformComponent.Api.FromHandle(_propertyWorld, parentTransformHandle);
        if (!parentTransform.IsAlive)
        {
            ApplyConstraintsToChildren(parentEntity, parentRectLocal);
            return;
        }

        var axis = (LayoutAxis)Math.Clamp(parentTransform.LayoutContainerDirection, 0, 1);
        var alignment = (LayoutAlignment)Math.Clamp(parentTransform.LayoutContainerAlignItems, 0, 3);
        var justify = (LayoutJustify)Math.Clamp(parentTransform.LayoutContainerJustify, 0, 5);

        ImRect contentRect = RectTransformMath.Inset(parentRectLocal, Insets.FromVector4(parentTransform.LayoutContainerPadding));
        float spacing = MathF.Max(0f, parentTransform.LayoutContainerSpacing);

        ReadOnlySpan<EntityId> children = _world.GetChildren(parentEntity);
        float availableMain = axis == LayoutAxis.Column ? contentRect.Height : contentRect.Width;
        float usedMainNoSpacing = 0f;
        int includedCount = 0;

        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            EntityId child = children[childIndex];
            if (!TryGetTransform(child, out TransformComponentHandle childTransformHandle))
            {
                continue;
            }

            var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, childTransformHandle);
            if (!childTransform.IsAlive)
            {
                continue;
            }

            if (childTransform.LayoutChildIgnoreLayout)
            {
                ApplyConstraint(parentRectLocal, child, childTransformHandle);
                continue;
            }

            if (!TryGetEntityIntrinsicSize(child, out Vector2 size, out _))
            {
                continue;
            }

            Insets margin = Insets.FromVector4(childTransform.LayoutChildMargin);
            ApplyPreferredSizeOverride(ref size, childTransform.LayoutChildPreferredSize);
            if (childTransform.LayoutConstraintEnabled)
            {
                size = RectTransformMath.ClampSize(size, childTransform.LayoutConstraintMinSize, childTransform.LayoutConstraintMaxSize);
            }

            usedMainNoSpacing += MathF.Max(0f, GetMainSize(axis, size)) + GetMarginStart(axis, margin) + GetMarginEnd(axis, margin);
            includedCount++;
        }

        float startOffset = 0f;
        float gapSpacing = spacing;
        ComputeJustifyOffsets(justify, spacing, includedCount, availableMain, usedMainNoSpacing, out startOffset, out gapSpacing);

        float cursor = (axis == LayoutAxis.Column ? contentRect.Y : contentRect.X) + startOffset;

        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            EntityId child = children[childIndex];

            if (!TryGetTransform(child, out TransformComponentHandle childTransformHandle))
            {
                continue;
            }

            var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, childTransformHandle);
            if (!childTransform.IsAlive)
            {
                continue;
            }

            if (childTransform.LayoutChildIgnoreLayout)
            {
                ApplyConstraint(parentRectLocal, child, childTransformHandle);
                continue;
            }

            if (!TryGetEntityIntrinsicSize(child, out Vector2 size, out LayoutSizeTarget sizeTarget))
            {
                continue;
            }

            Insets margin = Insets.FromVector4(childTransform.LayoutChildMargin);
            ApplyPreferredSizeOverride(ref size, childTransform.LayoutChildPreferredSize);
            if (childTransform.LayoutConstraintEnabled)
            {
                size = RectTransformMath.ClampSize(size, childTransform.LayoutConstraintMinSize, childTransform.LayoutConstraintMaxSize);
            }

            Vector2 pivot01 = childTransform.Anchor;

            float childX;
            float childY;
            float childWidth = MathF.Max(0f, size.X);
            float childHeight = MathF.Max(0f, size.Y);

            byte alignSelf = (byte)Math.Clamp(childTransform.LayoutChildAlignSelf, 0, 4);
            LayoutAlignment childAlignment = GetEffectiveAlignment(alignment, alignSelf);

            if (axis == LayoutAxis.Column)
            {
                childY = cursor + margin.Top;

                float availableWidth = MathF.Max(0f, contentRect.Width - margin.Left - margin.Right);
                if (childAlignment == LayoutAlignment.Stretch)
                {
                    childWidth = availableWidth;
                    childX = contentRect.X + margin.Left;
                }
                else
                {
                    childX = GetAlignedPosition(contentRect.X, availableWidth, childWidth, margin.Left, childAlignment);
                }

                cursor = childY + childHeight + margin.Bottom + gapSpacing;
            }
            else
            {
                childX = cursor + margin.Left;

                float availableHeight = MathF.Max(0f, contentRect.Height - margin.Top - margin.Bottom);
                if (childAlignment == LayoutAlignment.Stretch)
                {
                    childHeight = availableHeight;
                    childY = contentRect.Y + margin.Top;
                }
                else
                {
                    childY = GetAlignedPosition(contentRect.Y, availableHeight, childHeight, margin.Top, childAlignment);
                }

                cursor = childX + childWidth + margin.Right + gapSpacing;
            }

            var childRect = new ImRect(childX, childY, childWidth, childHeight);
            Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(childRect, pivot01);

            TrySetComputedPivotPosition(child, pivotPos);
            TrySetEntitySize(sizeTarget, new Vector2(childWidth, childHeight));
        }
    }

    private static void ApplyPreferredSizeOverride(ref Vector2 size, Vector2 preferredSize)
    {
        if (preferredSize.X > 0f)
        {
            size.X = preferredSize.X;
        }

        if (preferredSize.Y > 0f)
        {
            size.Y = preferredSize.Y;
        }
    }

    private void MeasureHugContainerSize(EntityId entity)
    {
        if (!TryGetTransform(entity, out TransformComponentHandle transformHandle))
        {
            return;
        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
        if (!transform.IsAlive || !transform.LayoutContainerEnabled)
        {
            return;
        }

        var widthMode = (LayoutSizeMode)Math.Clamp(transform.LayoutContainerWidthMode, 0, 2);
        var heightMode = (LayoutSizeMode)Math.Clamp(transform.LayoutContainerHeightMode, 0, 2);
        if (widthMode != LayoutSizeMode.Hug && heightMode != LayoutSizeMode.Hug)
        {
            return;
        }

        if (!TryGetEntitySizeTarget(entity, out Vector2 currentSize, out LayoutSizeTarget sizeTarget))
        {
            return;
        }

        if (!TryMeasureContainerContentSize(entity, transformHandle, out Vector2 contentSize, out Insets padding))
        {
            return;
        }

        float desiredWidth = currentSize.X;
        float desiredHeight = currentSize.Y;

        if (widthMode == LayoutSizeMode.Hug)
        {
            desiredWidth = padding.Left + padding.Right + contentSize.X;
        }

        if (heightMode == LayoutSizeMode.Hug)
        {
            desiredHeight = padding.Top + padding.Bottom + contentSize.Y;
        }

        Vector2 desired = new Vector2(MathF.Max(0f, desiredWidth), MathF.Max(0f, desiredHeight));
        TrySetEntitySize(sizeTarget, desired);
    }

    private bool TryMeasureContainerContentSize(EntityId parentEntity, TransformComponentHandle parentTransformHandle, out Vector2 contentSize, out Insets padding)
    {
        contentSize = Vector2.Zero;
        padding = default;

        var parentTransform = TransformComponent.Api.FromHandle(_propertyWorld, parentTransformHandle);
        if (!parentTransform.IsAlive || !parentTransform.LayoutContainerEnabled)
        {
            return false;
        }

        padding = Insets.FromVector4(parentTransform.LayoutContainerPadding);
        float spacing = MathF.Max(0f, parentTransform.LayoutContainerSpacing);

        ReadOnlySpan<EntityId> children = _world.GetChildren(parentEntity);

        var layoutType = (LayoutType)Math.Clamp(parentTransform.LayoutContainerLayout, 0, 2);
        if (layoutType == LayoutType.Grid)
        {
            int columns = Math.Clamp(parentTransform.LayoutContainerGridColumns, 1, 64);

            Span<FlexChildInfo> infos = stackalloc FlexChildInfo[256];
            int count = 0;
            for (int childIndex = 0; childIndex < children.Length; childIndex++)
            {
                EntityId child = children[childIndex];
                if (!TryGetTransform(child, out TransformComponentHandle childTransformHandle))
                {
                    continue;
                }

                var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, childTransformHandle);
                if (!childTransform.IsAlive || childTransform.LayoutChildIgnoreLayout)
                {
                    continue;
                }

                if (!TryGetEntitySizeTarget(child, out Vector2 size, out LayoutSizeTarget sizeTarget))
                {
                    continue;
                }

                ApplyPreferredSizeOverride(ref size, childTransform.LayoutChildPreferredSize);
                if (childTransform.LayoutConstraintEnabled)
                {
                    size = RectTransformMath.ClampSize(size, childTransform.LayoutConstraintMinSize, childTransform.LayoutConstraintMaxSize);
                }

                if (count >= infos.Length)
                {
                    break;
                }

                infos[count++] = new FlexChildInfo(
                    child,
                    childTransformHandle,
                    sizeTarget,
                    childTransform.Anchor,
                    Insets.FromVector4(childTransform.LayoutChildMargin),
                    alignSelf: 0,
                    flexGrow: 0f,
                    flexShrink: 0f,
                    baseSize: new Vector2(MathF.Max(0f, size.X), MathF.Max(0f, size.Y)));
            }

            if (count <= 0)
            {
                contentSize = Vector2.Zero;
                return true;
            }

            int rows = (count + columns - 1) / columns;
            if (rows <= 0)
            {
                rows = 1;
            }

            Span<float> colWidths = stackalloc float[64];
            Span<float> rowHeights = stackalloc float[256];

            for (int i = 0; i < columns; i++)
            {
                colWidths[i] = 0f;
            }
            for (int i = 0; i < rows; i++)
            {
                rowHeights[i] = 0f;
            }

            for (int i = 0; i < count; i++)
            {
                int col = i % columns;
                int row = i / columns;

                Vector2 size = infos[i].BaseSize;
                float w = size.X + infos[i].Margin.Left + infos[i].Margin.Right;
                float h = size.Y + infos[i].Margin.Top + infos[i].Margin.Bottom;

                if (w > colWidths[col]) colWidths[col] = w;
                if (h > rowHeights[row]) rowHeights[row] = h;
            }

            float contentWidth = 0f;
            for (int col = 0; col < columns; col++)
            {
                contentWidth += colWidths[col];
            }

            float contentHeight = 0f;
            for (int row = 0; row < rows; row++)
            {
                contentHeight += rowHeights[row];
            }

            if (columns > 1)
            {
                contentWidth += spacing * (columns - 1);
            }

            if (rows > 1)
            {
                contentHeight += spacing * (rows - 1);
            }

            contentSize = new Vector2(MathF.Max(0f, contentWidth), MathF.Max(0f, contentHeight));
            return true;
        }

        var axis = (LayoutAxis)Math.Clamp(parentTransform.LayoutContainerDirection, 0, 1);

        float usedMainNoSpacing = 0f;
        float maxCross = 0f;
        int includedCount = 0;

        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            EntityId child = children[childIndex];
            if (!TryGetTransform(child, out TransformComponentHandle childTransformHandle))
            {
                continue;
            }

            var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, childTransformHandle);
            if (!childTransform.IsAlive || childTransform.LayoutChildIgnoreLayout)
            {
                continue;
            }

            if (!TryGetEntitySizeTarget(child, out Vector2 size, out _))
            {
                continue;
            }

            ApplyPreferredSizeOverride(ref size, childTransform.LayoutChildPreferredSize);
            if (childTransform.LayoutConstraintEnabled)
            {
                size = RectTransformMath.ClampSize(size, childTransform.LayoutConstraintMinSize, childTransform.LayoutConstraintMaxSize);
            }

            size = new Vector2(MathF.Max(0f, size.X), MathF.Max(0f, size.Y));

            Insets margin = Insets.FromVector4(childTransform.LayoutChildMargin);

            float main = GetMainSize(axis, size) + GetMarginStart(axis, margin) + GetMarginEnd(axis, margin);
            float cross = GetCrossSize(axis, size) + GetMarginCrossStart(axis, margin) + GetMarginCrossEnd(axis, margin);

            usedMainNoSpacing += main;
            if (cross > maxCross)
            {
                maxCross = cross;
            }

            includedCount++;
        }

        float usedMain = usedMainNoSpacing;
        if (includedCount > 1)
        {
            usedMain += spacing * (includedCount - 1);
        }

        if (axis == LayoutAxis.Column)
        {
            contentSize = new Vector2(MathF.Max(0f, maxCross), MathF.Max(0f, usedMain));
        }
        else
        {
            contentSize = new Vector2(MathF.Max(0f, usedMain), MathF.Max(0f, maxCross));
        }

        return true;
    }

    private readonly struct FlexChildInfo
    {
        public readonly EntityId Entity;
        public readonly TransformComponentHandle TransformHandle;
        public readonly LayoutSizeTarget SizeTarget;
        public readonly Vector2 Pivot01;
        public readonly Insets Margin;
        public readonly byte AlignSelf;
        public readonly float FlexGrow;
        public readonly float FlexShrink;
        public readonly Vector2 BaseSize;

        public FlexChildInfo(
            EntityId entity,
            TransformComponentHandle transformHandle,
            LayoutSizeTarget sizeTarget,
            Vector2 pivot01,
            Insets margin,
            byte alignSelf,
            float flexGrow,
            float flexShrink,
            Vector2 baseSize)
        {
            Entity = entity;
            TransformHandle = transformHandle;
            SizeTarget = sizeTarget;
            Pivot01 = pivot01;
            Margin = margin;
            AlignSelf = alignSelf;
            FlexGrow = flexGrow;
            FlexShrink = flexShrink;
            BaseSize = baseSize;
        }
    }

    private static LayoutAlignment GetEffectiveAlignment(LayoutAlignment containerAlignment, byte alignSelf)
    {
        if (alignSelf == 0)
        {
            return containerAlignment;
        }

        int value = alignSelf - 1;
        if (value < 0)
        {
            value = 0;
        }
        else if (value > 3)
        {
            value = 3;
        }

        return (LayoutAlignment)value;
    }

    private static float GetMainSize(LayoutAxis axis, Vector2 size)
    {
        return axis == LayoutAxis.Column ? size.Y : size.X;
    }

    private static Vector2 SetMainSize(LayoutAxis axis, Vector2 size, float main)
    {
        if (axis == LayoutAxis.Column)
        {
            return new Vector2(size.X, main);
        }

        return new Vector2(main, size.Y);
    }

    private static float GetCrossSize(LayoutAxis axis, Vector2 size)
    {
        return axis == LayoutAxis.Column ? size.X : size.Y;
    }

    private static Vector2 SetCrossSize(LayoutAxis axis, Vector2 size, float cross)
    {
        if (axis == LayoutAxis.Column)
        {
            return new Vector2(cross, size.Y);
        }

        return new Vector2(size.X, cross);
    }

    private static float GetMarginStart(LayoutAxis axis, in Insets margin)
    {
        return axis == LayoutAxis.Column ? margin.Top : margin.Left;
    }

    private static float GetMarginEnd(LayoutAxis axis, in Insets margin)
    {
        return axis == LayoutAxis.Column ? margin.Bottom : margin.Right;
    }

    private static float GetMarginCrossStart(LayoutAxis axis, in Insets margin)
    {
        return axis == LayoutAxis.Column ? margin.Left : margin.Top;
    }

    private static float GetMarginCrossEnd(LayoutAxis axis, in Insets margin)
    {
        return axis == LayoutAxis.Column ? margin.Right : margin.Bottom;
    }

    private void ApplyFlexLayout(EntityId parentEntity, ImRect parentRectLocal, TransformComponentHandle parentTransformHandle)
    {
        var parentTransform = TransformComponent.Api.FromHandle(_propertyWorld, parentTransformHandle);
        if (!parentTransform.IsAlive)
        {
            ApplyConstraintsToChildren(parentEntity, parentRectLocal);
            return;
        }

        var axis = (LayoutAxis)Math.Clamp(parentTransform.LayoutContainerDirection, 0, 1);
        var alignment = (LayoutAlignment)Math.Clamp(parentTransform.LayoutContainerAlignItems, 0, 3);
        var justify = (LayoutJustify)Math.Clamp(parentTransform.LayoutContainerJustify, 0, 5);

        ImRect contentRect = RectTransformMath.Inset(parentRectLocal, Insets.FromVector4(parentTransform.LayoutContainerPadding));
        float spacing = MathF.Max(0f, parentTransform.LayoutContainerSpacing);

        ReadOnlySpan<EntityId> children = _world.GetChildren(parentEntity);

        Span<FlexChildInfo> infos = stackalloc FlexChildInfo[256];
        int infoCount = 0;

        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            EntityId child = children[childIndex];
            if (!TryGetTransform(child, out TransformComponentHandle childTransformHandle))
            {
                continue;
            }

            var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, childTransformHandle);
            if (!childTransform.IsAlive)
            {
                continue;
            }

            if (childTransform.LayoutChildIgnoreLayout)
            {
                ApplyConstraint(parentRectLocal, child, childTransformHandle);
                continue;
            }

            if (!TryGetEntityIntrinsicSize(child, out Vector2 size, out LayoutSizeTarget sizeTarget))
            {
                continue;
            }

            Vector2 preferredSize = childTransform.LayoutChildPreferredSize;
            ApplyPreferredSizeOverride(ref size, preferredSize);
            if (childTransform.LayoutConstraintEnabled)
            {
                size = RectTransformMath.ClampSize(size, childTransform.LayoutConstraintMinSize, childTransform.LayoutConstraintMaxSize);
            }

            if (infoCount >= infos.Length)
            {
                break;
            }

            float flexGrow = MathF.Max(0f, childTransform.LayoutChildFlexGrow);
            float flexShrink = MathF.Max(0f, childTransform.LayoutChildFlexShrink);

            // Flex behavior: if an item is "flexible" (Grow > 0) and no explicit preferred size is provided
            // on the main axis, treat its base size as 0 (i.e. flex-basis: 0) so weights behave predictably.
            float preferredMain = GetMainSize(axis, preferredSize);
            if (flexGrow > 0f && preferredMain <= 0f)
            {
                float minMainConstraint = 0f;
                if (childTransform.LayoutConstraintEnabled)
                {
                    minMainConstraint = GetMainSize(axis, childTransform.LayoutConstraintMinSize);
                }

                float baseMain = minMainConstraint > 0f ? minMainConstraint : 0f;
                size = SetMainSize(axis, size, baseMain);
            }

            infos[infoCount++] = new FlexChildInfo(
                child,
                childTransformHandle,
                sizeTarget,
                childTransform.Anchor,
                Insets.FromVector4(childTransform.LayoutChildMargin),
                alignSelf: (byte)Math.Clamp(childTransform.LayoutChildAlignSelf, 0, 4),
                flexGrow: flexGrow,
                flexShrink: flexShrink,
                baseSize: new Vector2(MathF.Max(0f, size.X), MathF.Max(0f, size.Y)));
        }

        if (infoCount <= 0)
        {
            return;
        }

        float availableMain = axis == LayoutAxis.Column ? contentRect.Height : contentRect.Width;

        Span<Vector2> finalSize = stackalloc Vector2[256];
        Span<float> minMain = stackalloc float[256];
        Span<float> maxMain = stackalloc float[256];

        float usedMainNoSpacing = 0f;
        float totalGrow = 0f;
        float totalShrinkWeight = 0f;

        for (int i = 0; i < infoCount; i++)
        {
            FlexChildInfo info = infos[i];
            var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, info.TransformHandle);
            Vector2 size = info.BaseSize;

            float min = 0f;
            float max = 0f;
            if (childTransform.IsAlive && childTransform.LayoutConstraintEnabled)
            {
                Vector2 clamped = RectTransformMath.ClampSize(size, childTransform.LayoutConstraintMinSize, childTransform.LayoutConstraintMaxSize);
                size = clamped;

                min = GetMainSize(axis, childTransform.LayoutConstraintMinSize);
                max = GetMainSize(axis, childTransform.LayoutConstraintMaxSize);
            }

            finalSize[i] = size;
            minMain[i] = min > 0f ? min : 0f;
            maxMain[i] = max > 0f ? max : 0f;

            float main = GetMainSize(axis, size);
            usedMainNoSpacing += main + GetMarginStart(axis, info.Margin) + GetMarginEnd(axis, info.Margin);

            totalGrow += info.FlexGrow;
            totalShrinkWeight += info.FlexShrink * MathF.Max(0f, main);
        }

        float usedMainWithSpacing = usedMainNoSpacing;
        if (infoCount > 1)
        {
            usedMainWithSpacing += spacing * (infoCount - 1);
        }

        float free = availableMain - usedMainWithSpacing;
        if (free > 0f && totalGrow > 0f)
        {
            // Grow, respecting max constraints.
            float remainingFree = free;
            Span<byte> isCapped = stackalloc byte[256];
            Span<float> addMain = stackalloc float[256];

            int iterations = infoCount;
            while (iterations-- > 0 && remainingFree > 0.001f)
            {
                float remainingGrow = 0f;
                for (int i = 0; i < infoCount; i++)
                {
                    addMain[i] = 0f;
                    if (isCapped[i] == 0 && infos[i].FlexGrow > 0f)
                    {
                        remainingGrow += infos[i].FlexGrow;
                    }
                }

                if (remainingGrow <= 0.0001f)
                {
                    break;
                }

                float totalApplied = 0f;
                bool anyProgress = false;

                for (int i = 0; i < infoCount; i++)
                {
                    if (isCapped[i] != 0 || infos[i].FlexGrow <= 0f)
                    {
                        continue;
                    }

                    Vector2 size = finalSize[i];
                    float main = GetMainSize(axis, size);

                    float add = remainingFree * (infos[i].FlexGrow / remainingGrow);
                    float max = maxMain[i];
                    if (max > 0f && main + add > max)
                    {
                        add = max - main;
                        if (add < 0f)
                        {
                            add = 0f;
                        }
                        isCapped[i] = 1;
                    }

                    if (add > 0.0001f)
                    {
                        addMain[i] = add;
                        totalApplied += add;
                        anyProgress = true;
                    }
                }

                if (!anyProgress || totalApplied <= 0.0001f)
                {
                    break;
                }

                for (int i = 0; i < infoCount; i++)
                {
                    float add = addMain[i];
                    if (add <= 0f)
                    {
                        continue;
                    }

                    Vector2 size = finalSize[i];
                    float main = GetMainSize(axis, size);
                    finalSize[i] = SetMainSize(axis, size, main + add);
                }

                remainingFree -= totalApplied;
            }
        }
        else if (free < 0f && totalShrinkWeight > 0f)
        {
            // Shrink, respecting min constraints.
            float remainingOverflow = -free;
            Span<byte> isAtMin = stackalloc byte[256];
            Span<float> subMain = stackalloc float[256];

            int iterations = infoCount;
            while (iterations-- > 0 && remainingOverflow > 0.001f)
            {
                float remainingShrinkWeight = 0f;
                for (int i = 0; i < infoCount; i++)
                {
                    subMain[i] = 0f;
                    if (isAtMin[i] != 0 || infos[i].FlexShrink <= 0f)
                    {
                        continue;
                    }

                    float main = GetMainSize(axis, finalSize[i]);
                    if (main <= 0f)
                    {
                        continue;
                    }

                    remainingShrinkWeight += infos[i].FlexShrink * main;
                }

                if (remainingShrinkWeight <= 0.0001f)
                {
                    break;
                }

                float totalApplied = 0f;
                bool anyProgress = false;

                for (int i = 0; i < infoCount; i++)
                {
                    if (isAtMin[i] != 0 || infos[i].FlexShrink <= 0f)
                    {
                        continue;
                    }

                    Vector2 size = finalSize[i];
                    float main = GetMainSize(axis, size);
                    if (main <= 0f)
                    {
                        isAtMin[i] = 1;
                        continue;
                    }

                    float weight = infos[i].FlexShrink * main;
                    if (weight <= 0f)
                    {
                        isAtMin[i] = 1;
                        continue;
                    }

                    float sub = remainingOverflow * (weight / remainingShrinkWeight);
                    float min = minMain[i];
                    if (min > 0f && main - sub < min)
                    {
                        sub = main - min;
                        if (sub < 0f)
                        {
                            sub = 0f;
                        }
                        isAtMin[i] = 1;
                    }

                    if (sub > 0.0001f)
                    {
                        subMain[i] = sub;
                        totalApplied += sub;
                        anyProgress = true;
                    }
                }

                if (!anyProgress || totalApplied <= 0.0001f)
                {
                    break;
                }

                for (int i = 0; i < infoCount; i++)
                {
                    float sub = subMain[i];
                    if (sub <= 0f)
                    {
                        continue;
                    }

                    Vector2 size = finalSize[i];
                    float main = GetMainSize(axis, size);
                    finalSize[i] = SetMainSize(axis, size, main - sub);
                }

                remainingOverflow -= totalApplied;
            }
        }

        float usedMainFinalNoSpacing = 0f;
        for (int i = 0; i < infoCount; i++)
        {
            usedMainFinalNoSpacing += GetMainSize(axis, finalSize[i]) + GetMarginStart(axis, infos[i].Margin) + GetMarginEnd(axis, infos[i].Margin);
        }

        float startOffset = 0f;
        float gapSpacing = spacing;
        ComputeJustifyOffsets(justify, spacing, infoCount, availableMain, usedMainFinalNoSpacing, out startOffset, out gapSpacing);

        float cursor = (axis == LayoutAxis.Column ? contentRect.Y : contentRect.X) + startOffset;

        for (int i = 0; i < infoCount; i++)
        {
            FlexChildInfo info = infos[i];

            var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, info.TransformHandle);
            if (!childTransform.IsAlive)
            {
                continue;
            }

            Vector2 size = finalSize[i];
            if (childTransform.LayoutConstraintEnabled)
            {
                size = RectTransformMath.ClampSize(size, childTransform.LayoutConstraintMinSize, childTransform.LayoutConstraintMaxSize);
            }

            float childWidth = MathF.Max(0f, size.X);
            float childHeight = MathF.Max(0f, size.Y);

            float childX;
            float childY;

            LayoutAlignment childAlignment = GetEffectiveAlignment(alignment, info.AlignSelf);

            if (axis == LayoutAxis.Column)
            {
                childY = cursor + info.Margin.Top;

                float availableWidth = MathF.Max(0f, contentRect.Width - info.Margin.Left - info.Margin.Right);
                if (childAlignment == LayoutAlignment.Stretch)
                {
                    childWidth = availableWidth;
                    childX = contentRect.X + info.Margin.Left;
                }
                else
                {
                    childX = GetAlignedPosition(contentRect.X, availableWidth, childWidth, info.Margin.Left, childAlignment);
                }

                cursor = childY + childHeight + info.Margin.Bottom + gapSpacing;
            }
            else
            {
                childX = cursor + info.Margin.Left;

                float availableHeight = MathF.Max(0f, contentRect.Height - info.Margin.Top - info.Margin.Bottom);
                if (childAlignment == LayoutAlignment.Stretch)
                {
                    childHeight = availableHeight;
                    childY = contentRect.Y + info.Margin.Top;
                }
                else
                {
                    childY = GetAlignedPosition(contentRect.Y, availableHeight, childHeight, info.Margin.Top, childAlignment);
                }

                cursor = childX + childWidth + info.Margin.Right + gapSpacing;
            }

            var childRect = new ImRect(childX, childY, childWidth, childHeight);
            Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(childRect, info.Pivot01);

            TrySetComputedPivotPosition(info.Entity, pivotPos);
            TrySetEntitySize(info.SizeTarget, new Vector2(childWidth, childHeight));
        }
    }

    private void ApplyGridLayout(EntityId parentEntity, ImRect parentRectLocal, TransformComponentHandle parentTransformHandle)
    {
        var parentTransform = TransformComponent.Api.FromHandle(_propertyWorld, parentTransformHandle);
        if (!parentTransform.IsAlive)
        {
            ApplyConstraintsToChildren(parentEntity, parentRectLocal);
            return;
        }

        ImRect contentRect = RectTransformMath.Inset(parentRectLocal, Insets.FromVector4(parentTransform.LayoutContainerPadding));
        float spacing = MathF.Max(0f, parentTransform.LayoutContainerSpacing);

        int columns = parentTransform.LayoutContainerGridColumns;
        if (columns <= 0)
        {
            columns = 1;
        }
        else if (columns > 64)
        {
            columns = 64;
        }

        var justify = (LayoutJustify)Math.Clamp(parentTransform.LayoutContainerJustify, 0, 5);
        LayoutAlignment alignX = justify <= LayoutJustify.End ? (LayoutAlignment)justify : LayoutAlignment.Start;
        var alignY = (LayoutAlignment)Math.Clamp(parentTransform.LayoutContainerAlignItems, 0, 3);

        ReadOnlySpan<EntityId> children = _world.GetChildren(parentEntity);

        Span<FlexChildInfo> infos = stackalloc FlexChildInfo[256];
        int count = 0;

        for (int childIndex = 0; childIndex < children.Length; childIndex++)
        {
            EntityId child = children[childIndex];
            if (!TryGetTransform(child, out TransformComponentHandle childTransformHandle))
            {
                continue;
            }

            var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, childTransformHandle);
            if (!childTransform.IsAlive)
            {
                continue;
            }

            if (childTransform.LayoutChildIgnoreLayout)
            {
                ApplyConstraint(parentRectLocal, child, childTransformHandle);
                continue;
            }

            if (!TryGetEntityIntrinsicSize(child, out Vector2 size, out LayoutSizeTarget sizeTarget))
            {
                continue;
            }

            ApplyPreferredSizeOverride(ref size, childTransform.LayoutChildPreferredSize);
            if (childTransform.LayoutConstraintEnabled)
            {
                size = RectTransformMath.ClampSize(size, childTransform.LayoutConstraintMinSize, childTransform.LayoutConstraintMaxSize);
            }

            if (count >= infos.Length)
            {
                break;
            }

            infos[count++] = new FlexChildInfo(
                child,
                childTransformHandle,
                sizeTarget,
                childTransform.Anchor,
                Insets.FromVector4(childTransform.LayoutChildMargin),
                alignSelf: (byte)Math.Clamp(childTransform.LayoutChildAlignSelf, 0, 4),
                flexGrow: 0f,
                flexShrink: 0f,
                baseSize: new Vector2(MathF.Max(0f, size.X), MathF.Max(0f, size.Y)));
        }

        if (count <= 0)
        {
            return;
        }

        if (columns > count)
        {
            columns = count;
        }

        int rows = (count + columns - 1) / columns;
        if (rows <= 0)
        {
            rows = 1;
        }

        Span<float> colWidths = stackalloc float[64];
        Span<float> rowHeights = stackalloc float[256];

        for (int i = 0; i < columns; i++)
        {
            colWidths[i] = 0f;
        }
        for (int i = 0; i < rows; i++)
        {
            rowHeights[i] = 0f;
        }

        for (int i = 0; i < count; i++)
        {
            int col = i % columns;
            int row = i / columns;

            Vector2 size = infos[i].BaseSize;
            float w = size.X + infos[i].Margin.Left + infos[i].Margin.Right;
            float h = size.Y + infos[i].Margin.Top + infos[i].Margin.Bottom;

            if (w > colWidths[col]) colWidths[col] = w;
            if (h > rowHeights[row]) rowHeights[row] = h;
        }

        float gridWidth = 0f;
        for (int col = 0; col < columns; col++)
        {
            gridWidth += colWidths[col];
        }
        if (columns > 1)
        {
            gridWidth += spacing * (columns - 1);
        }

        float gridHeight = 0f;
        for (int row = 0; row < rows; row++)
        {
            gridHeight += rowHeights[row];
        }
        if (rows > 1)
        {
            gridHeight += spacing * (rows - 1);
        }

        float gridStartX = contentRect.X;
        float gridStartY = contentRect.Y;

        if (alignX != LayoutAlignment.Start)
        {
            float offsetX = contentRect.Width - gridWidth;
            if (alignX == LayoutAlignment.Center)
            {
                gridStartX += offsetX * 0.5f;
            }
            else if (alignX == LayoutAlignment.End)
            {
                gridStartX += offsetX;
            }
        }

        if (alignY != LayoutAlignment.Start && alignY != LayoutAlignment.Stretch)
        {
            float offsetY = contentRect.Height - gridHeight;
            if (alignY == LayoutAlignment.Center)
            {
                gridStartY += offsetY * 0.5f;
            }
            else if (alignY == LayoutAlignment.End)
            {
                gridStartY += offsetY;
            }
        }

        float y = gridStartY;
        for (int row = 0; row < rows; row++)
        {
            float x = gridStartX;
            for (int col = 0; col < columns; col++)
            {
                int index = row * columns + col;
                if (index >= count)
                {
                    break;
                }

                FlexChildInfo info = infos[index];

                var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, info.TransformHandle);
                if (!childTransform.IsAlive)
                {
                    x += colWidths[col] + spacing;
                    continue;
                }

                Vector2 size = info.BaseSize;
                if (childTransform.LayoutConstraintEnabled)
                {
                    size = RectTransformMath.ClampSize(size, childTransform.LayoutConstraintMinSize, childTransform.LayoutConstraintMaxSize);
                }

                float cellW = colWidths[col];
                float cellH = rowHeights[row];

                float availableW = MathF.Max(0f, cellW - info.Margin.Left - info.Margin.Right);
                float availableH = MathF.Max(0f, cellH - info.Margin.Top - info.Margin.Bottom);

                float childW = MathF.Min(MathF.Max(0f, size.X), availableW);
                float childH = MathF.Min(MathF.Max(0f, size.Y), availableH);

                var childAlignX = alignX;
                var childAlignY = alignY;

                LayoutAlignment effectiveAlign = GetEffectiveAlignment(childAlignY, info.AlignSelf);
                childAlignY = effectiveAlign;

                float childX;
                float childY;
                if (childAlignX == LayoutAlignment.Stretch)
                {
                    childW = availableW;
                    childX = x + info.Margin.Left;
                }
                else
                {
                    childX = GetAlignedPosition(x, availableW, childW, info.Margin.Left, childAlignX);
                }

                if (childAlignY == LayoutAlignment.Stretch)
                {
                    childH = availableH;
                    childY = y + info.Margin.Top;
                }
                else
                {
                    childY = GetAlignedPosition(y, availableH, childH, info.Margin.Top, childAlignY);
                }

                var childRect = new ImRect(childX, childY, childW, childH);
                Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(childRect, info.Pivot01);

                TrySetComputedPivotPosition(info.Entity, pivotPos);
                TrySetEntitySize(info.SizeTarget, new Vector2(childW, childH));

                x += colWidths[col] + spacing;
            }

            y += rowHeights[row] + spacing;
        }
    }

    private static float GetAlignedPosition(float contentStart, float contentSize, float childSize, float marginStart, LayoutAlignment alignment)
    {
        if (alignment == LayoutAlignment.Center)
        {
            return contentStart + marginStart + (contentSize - childSize) * 0.5f;
        }

        if (alignment == LayoutAlignment.End)
        {
            return contentStart + marginStart + (contentSize - childSize);
        }

        return contentStart + marginStart;
    }

    private static void ComputeJustifyOffsets(
        LayoutJustify justify,
        float baseSpacing,
        int itemCount,
        float availableMain,
        float usedMainNoSpacing,
        out float startOffset,
        out float spacing)
    {
        startOffset = 0f;
        spacing = baseSpacing;

        if (itemCount <= 0)
        {
            return;
        }

        int gapCount = itemCount - 1;
        float usedWithBaseSpacing = usedMainNoSpacing + baseSpacing * MathF.Max(0, gapCount);
        float remaining = availableMain - usedWithBaseSpacing;
        if (remaining <= 0f)
        {
            return;
        }

        if (justify == LayoutJustify.Center)
        {
            startOffset = remaining * 0.5f;
            return;
        }

        if (justify == LayoutJustify.End)
        {
            startOffset = remaining;
            return;
        }

        if (justify == LayoutJustify.SpaceBetween)
        {
            if (gapCount <= 0)
            {
                startOffset = remaining * 0.5f;
                return;
            }

            float extra = remaining / gapCount;
            spacing = baseSpacing + extra;
            return;
        }

        if (justify == LayoutJustify.SpaceAround)
        {
            float extra = remaining / itemCount;
            spacing = baseSpacing + extra;
            startOffset = extra * 0.5f;
            return;
        }

        if (justify == LayoutJustify.SpaceEvenly)
        {
            float extra = remaining / (itemCount + 1);
            spacing = baseSpacing + extra;
            startOffset = extra;
        }
    }

    private void ApplyConstraint(ImRect parentRectLocal, EntityId child, TransformComponentHandle childTransformHandle)
    {
        var childTransform = TransformComponent.Api.FromHandle(_propertyWorld, childTransformHandle);
        if (!childTransform.IsAlive)
        {
            return;
        }

        bool constraintEnabled = childTransform.LayoutConstraintEnabled;

        bool fillEnabled = false;
        if (!constraintEnabled && childTransform.LayoutContainerEnabled)
        {
            var widthMode = (LayoutSizeMode)Math.Clamp(childTransform.LayoutContainerWidthMode, 0, 2);
            var heightMode = (LayoutSizeMode)Math.Clamp(childTransform.LayoutContainerHeightMode, 0, 2);
            fillEnabled = widthMode == LayoutSizeMode.Fill || heightMode == LayoutSizeMode.Fill;
        }

        if (!constraintEnabled && !fillEnabled)
        {
            return;
        }

        if (!TryGetEntitySizeTarget(child, out Vector2 intrinsicSize, out LayoutSizeTarget sizeTarget))
        {
            return;
        }

        Vector2 pivot01 = childTransform.Anchor;

        if (!constraintEnabled)
        {
            var widthMode = (LayoutSizeMode)Math.Clamp(childTransform.LayoutContainerWidthMode, 0, 2);
            var heightMode = (LayoutSizeMode)Math.Clamp(childTransform.LayoutContainerHeightMode, 0, 2);

            Vector2 intrinsic = intrinsicSize;
            Vector2 currentPivotPos = Vector2.Zero;
            if (TryGetComputedTransform(child, out ComputedTransformComponentHandle computedTransformHandle))
            {
                var computedTransform = ComputedTransformComponent.Api.FromHandle(_propertyWorld, computedTransformHandle);
                if (computedTransform.IsAlive)
                {
                    currentPivotPos = computedTransform.Position;
                }
            }
            var rect = RectTransformMath.GetRectFromPivot(currentPivotPos, pivot01, intrinsic);

            if (widthMode == LayoutSizeMode.Fill)
            {
                rect = new ImRect(parentRectLocal.X, rect.Y, parentRectLocal.Width, rect.Height);
            }

            if (heightMode == LayoutSizeMode.Fill)
            {
                rect = new ImRect(rect.X, parentRectLocal.Y, rect.Width, parentRectLocal.Height);
            }

            Vector2 computedSize = new Vector2(MathF.Max(0f, rect.Width), MathF.Max(0f, rect.Height));
            Vector2 computedPivotPos = RectTransformMath.GetPivotPosFromRect(rect, pivot01);

            TrySetEntitySize(sizeTarget, computedSize);
            TrySetComputedPivotPosition(child, computedPivotPos);
            return;
        }

        Vector2 parentMin = new Vector2(parentRectLocal.X, parentRectLocal.Y);
        Vector2 parentSize = new Vector2(parentRectLocal.Width, parentRectLocal.Height);

        Vector2 anchorMin = Clamp01(childTransform.LayoutConstraintAnchorMin);
        Vector2 anchorMax = Clamp01(childTransform.LayoutConstraintAnchorMax);

        bool isStretch =
            anchorMin.X != anchorMax.X ||
            anchorMin.Y != anchorMax.Y;

        Vector2 size = intrinsicSize;
        Vector2 pivotPos;

        if (!isStretch)
        {
            Vector2 anchorPoint = parentMin + new Vector2(parentSize.X * anchorMin.X, parentSize.Y * anchorMin.Y);
            pivotPos = anchorPoint + childTransform.LayoutConstraintOffsetMin;

            size = RectTransformMath.ClampSize(size, childTransform.LayoutConstraintMinSize, childTransform.LayoutConstraintMaxSize);
            TrySetEntitySize(sizeTarget, size);
        }
        else
        {
            Vector2 anchoredMin = parentMin + new Vector2(parentSize.X * anchorMin.X, parentSize.Y * anchorMin.Y);
            Vector2 anchoredMax = parentMin + new Vector2(parentSize.X * anchorMax.X, parentSize.Y * anchorMax.Y);

            Vector2 rectMin = anchoredMin + childTransform.LayoutConstraintOffsetMin;
            Vector2 rectMax = anchoredMax + childTransform.LayoutConstraintOffsetMax;

            Vector2 computedSize = new Vector2(rectMax.X - rectMin.X, rectMax.Y - rectMin.Y);
            computedSize = RectTransformMath.ClampSize(computedSize, childTransform.LayoutConstraintMinSize, childTransform.LayoutConstraintMaxSize);

            var rect = new ImRect(rectMin.X, rectMin.Y, computedSize.X, computedSize.Y);
            pivotPos = RectTransformMath.GetPivotPosFromRect(rect, pivot01);

            TrySetEntitySize(sizeTarget, computedSize);
        }

        TrySetComputedPivotPosition(child, pivotPos);
    }

    private static Vector2 Clamp01(Vector2 v)
    {
        return new Vector2(Math.Clamp(v.X, 0f, 1f), Math.Clamp(v.Y, 0f, 1f));
    }

    private bool TryGetTransform(EntityId entity, out TransformComponentHandle handle)
    {
        handle = default;
        if (!_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            return false;
        }

        handle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
        return true;
    }

    private bool TryGetComputedTransform(EntityId entity, out ComputedTransformComponentHandle handle)
    {
        handle = default;
        if (!_world.TryGetComponent(entity, ComputedTransformComponent.Api.PoolIdConst, out AnyComponentHandle computedAny) || !computedAny.IsValid)
        {
            return false;
        }

        handle = new ComputedTransformComponentHandle(computedAny.Index, computedAny.Generation);
        return true;
    }

    private bool TrySetComputedPivotPosition(EntityId entity, Vector2 pivotPos)
    {
        if (!TryGetComputedTransform(entity, out ComputedTransformComponentHandle computedHandle))
        {
            return false;
        }

        var computed = ComputedTransformComponent.Api.FromHandle(_propertyWorld, computedHandle);
        if (!computed.IsAlive)
        {
            return false;
        }

        computed.Position = pivotPos;
        return true;
    }

    private bool TrySetComputedSize(EntityId entity, Vector2 size)
    {
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

        computed.Size = size;
        return true;
    }

    private bool TryGetEntityIntrinsicSize(EntityId entity, out Vector2 size, out LayoutSizeTarget target)
    {
        size = Vector2.Zero;
        target = default;

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
        target = new LayoutSizeTarget(LayoutSizeTargetKind.ComputedSize, computedAny, entity);
        return size.X > 0f && size.Y > 0f;
    }

    private bool TryGetEntitySizeTarget(EntityId entity, out Vector2 size, out LayoutSizeTarget target)
    {
        size = Vector2.Zero;
        target = default;

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
        target = new LayoutSizeTarget(LayoutSizeTargetKind.ComputedSize, computedAny, entity);
        return true;
    }

    private bool TrySetEntitySize(LayoutSizeTarget target, Vector2 size)
    {
        if (target.Kind == LayoutSizeTargetKind.ComputedSize)
        {
            var handle = new ComputedSizeComponentHandle(target.Handle.Index, target.Handle.Generation);
            var computed = ComputedSizeComponent.Api.FromHandle(_propertyWorld, handle);
            if (!computed.IsAlive)
            {
                return false;
            }

            computed.Size = size;
            if (_world.GetNodeType(target.Entity) == UiNodeType.PrefabInstance)
            {
                TrySetPrefabCanvasSizeFromLayout(target.Entity, size);
                SyncExpandedRootProxySizeFromInstance(target.Entity, size);
            }
            return true;
        }

        return false;
    }

    private void TrySetPrefabCanvasSizeFromLayout(EntityId entity, Vector2 size)
    {
        if (entity.IsNull)
        {
            return;
        }

        if (!_world.TryGetComponent(entity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle canvasAny) || !canvasAny.IsValid)
        {
            return;
        }

        var canvasHandle = new PrefabCanvasComponentHandle(canvasAny.Index, canvasAny.Generation);
        var canvas = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, canvasHandle);
        if (!canvas.IsAlive)
        {
            return;
        }

        canvas.Size = size;
    }

    private void SyncExpandedRootProxySizeFromInstance(EntityId instanceEntity, Vector2 instanceCanvasSize)
    {
        if (instanceEntity.IsNull)
        {
            return;
        }

        // Only real instances (not proxies) have PrefabInstanceComponent.
        if (!_world.TryGetComponent(instanceEntity, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            return;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
        if (!instance.IsAlive || instance.SourcePrefabStableId == 0)
        {
            return;
        }

        if (!TryFindExpandedRootProxy(instanceEntity, instance.SourcePrefabStableId, out EntityId proxyEntity))
        {
            return;
        }

        if (_world.TryGetComponent(proxyEntity, PrefabCanvasComponent.Api.PoolIdConst, out AnyComponentHandle proxyCanvasAny) && proxyCanvasAny.IsValid)
        {
            var proxyCanvasHandle = new PrefabCanvasComponentHandle(proxyCanvasAny.Index, proxyCanvasAny.Generation);
            var proxyCanvas = PrefabCanvasComponent.Api.FromHandle(_propertyWorld, proxyCanvasHandle);
            if (proxyCanvas.IsAlive)
            {
                proxyCanvas.Size = instanceCanvasSize;
            }
        }

        // Layout uses ComputedSize as the intrinsic size when traversing the tree in the same pass.
        // Keep the expanded-root proxy computed size in sync with the instance computed size so
        // stretch/fill affects the expanded subtree immediately (without mutating authored sizes).
        TrySetComputedSize(proxyEntity, instanceCanvasSize);

        Vector2 instanceAnchor = Vector2.Zero;
        if (_world.TryGetComponent(instanceEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle instanceTransformAny) && instanceTransformAny.IsValid)
        {
            var handle = new TransformComponentHandle(instanceTransformAny.Index, instanceTransformAny.Generation);
            var view = TransformComponent.Api.FromHandle(_propertyWorld, handle);
            if (view.IsAlive)
            {
                instanceAnchor = view.Anchor;
            }
        }

        if (_world.TryGetComponent(proxyEntity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle proxyTransformAny) && proxyTransformAny.IsValid)
        {
            var proxyTransformHandle = new TransformComponentHandle(proxyTransformAny.Index, proxyTransformAny.Generation);
            var proxyTransform = TransformComponent.Api.FromHandle(_propertyWorld, proxyTransformHandle);
            if (proxyTransform.IsAlive)
            {
                Vector2 proxyAnchor = proxyTransform.Anchor;
                Vector2 proxyPos = (proxyAnchor - instanceAnchor) * instanceCanvasSize;
                proxyTransform.Position = proxyPos;
                TrySetComputedPivotPosition(proxyEntity, proxyPos);
            }
        }
    }

    private bool TryGetEntityRectLocalForLayout(EntityId entity, out ImRect rectLocal)
    {
        rectLocal = default;

        if (!TryGetEntityIntrinsicSize(entity, out Vector2 size, out _))
        {
            return false;
        }

        Vector2 pivot01 = Vector2.Zero;
        if (TryGetTransform(entity, out TransformComponentHandle transformHandle))
        {
            var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
            if (transform.IsAlive)
            {
                pivot01 = transform.Anchor;
            }
        }

        rectLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, pivot01, size);
        return rectLocal.Width > 0f && rectLocal.Height > 0f;
    }

    internal bool TryGetLayoutIntrinsicSize(EntityId entity, out Vector2 size, out Vector2 pivot01)
    {
        size = Vector2.Zero;
        pivot01 = Vector2.Zero;

        if (entity.IsNull || _world.GetNodeType(entity) == UiNodeType.None)
        {
            return false;
        }

        if (_world.TryGetComponent(entity, TransformComponent.Api.PoolIdConst, out AnyComponentHandle transformAny))
        {
            var transformHandle = new TransformComponentHandle(transformAny.Index, transformAny.Generation);
            var transform = TransformComponent.Api.FromHandle(_propertyWorld, transformHandle);
            if (transform.IsAlive)
            {
                pivot01 = transform.Anchor;
            }
        }

        if (!_world.TryGetComponent(entity, ComputedSizeComponent.Api.PoolIdConst, out AnyComponentHandle computedAny) || !computedAny.IsValid)
        {
            return false;
        }

        var computedHandle = new ComputedSizeComponentHandle(computedAny.Index, computedAny.Generation);
        var computed = ComputedSizeComponent.Api.FromHandle(_propertyWorld, computedHandle);
        if (!computed.IsAlive)
        {
            return false;
        }

        size = computed.Size;
        return size.X > 0f && size.Y > 0f;
    }

    private sealed class ConstraintExpandedLookup
    {
        public uint InstanceRootStableId;
        public uint SourcePrefabStableId;
        public uint SourcePrefabRevision;
        public uint[] SourceNodeStableIds = Array.Empty<uint>();
        public uint[] ExpandedNodeStableIds = Array.Empty<uint>();
        public int Count;
    }

    private ConstraintExpandedLookup[] _constraintExpandedLookups = Array.Empty<ConstraintExpandedLookup>();
    private int _constraintExpandedLookupCount;

    private void ApplyConstraintListPass()
    {
        int entityCount = _world.EntityCount;
        // Pass 1: position/size constraints (these can affect Scroll inputs).
        ApplyConstraintListPassCore(entityCount, applyScrollConstraints: false);
        // Pass 2: Scroll constraints (depend on computed sizes/positions).
        ApplyConstraintListPassCore(entityCount, applyScrollConstraints: true);
    }

    private void ApplyConstraintListPassCore(int entityCount, bool applyScrollConstraints)
    {
        for (int entityIndex = 1; entityIndex <= entityCount; entityIndex++)
        {
            var entity = new EntityId(entityIndex);
            if (_world.GetNodeType(entity) == UiNodeType.None)
            {
                continue;
            }

            if (!_world.TryGetComponent(entity, ConstraintListComponent.Api.PoolIdConst, out AnyComponentHandle constraintsAny) || !constraintsAny.IsValid)
            {
                continue;
            }

            var constraintsHandle = new ConstraintListComponentHandle(constraintsAny.Index, constraintsAny.Generation);
            var constraints = ConstraintListComponent.Api.FromHandle(_propertyWorld, constraintsHandle);
            if (!constraints.IsAlive)
            {
                continue;
            }

            ushort count = constraints.Count;
            if (count == 0)
            {
                continue;
            }
            if (count > ConstraintListComponent.MaxConstraints)
            {
                count = ConstraintListComponent.MaxConstraints;
            }

            uint instanceRootStableId = GetConstraintInstanceRootStableId(entity);

            ReadOnlySpan<byte> enabledValue = constraints.EnabledValueReadOnlySpan();
            ReadOnlySpan<byte> kindValue = constraints.KindValueReadOnlySpan();
            ReadOnlySpan<byte> flagsValue = constraints.FlagsValueReadOnlySpan();
            ReadOnlySpan<uint> targetSourceStableId = constraints.TargetSourceStableIdReadOnlySpan();

            for (int constraintIndex = 0; constraintIndex < count; constraintIndex++)
            {
                if (enabledValue[constraintIndex] == 0)
                {
                    continue;
                }

                var kind = (ConstraintListComponent.ConstraintKind)kindValue[constraintIndex];
                if (applyScrollConstraints)
                {
                    if (kind != ConstraintListComponent.ConstraintKind.Scroll)
                    {
                        continue;
                    }
                }
                else
                {
                    if (kind == ConstraintListComponent.ConstraintKind.Scroll)
                    {
                        continue;
                    }
                }

                uint targetStableId = targetSourceStableId[constraintIndex];
                if (targetStableId == 0)
                {
                    continue;
                }

                if (!TryResolveConstraintTargetEntity(instanceRootStableId, targetStableId, out EntityId targetEntity))
                {
                    continue;
                }

                if (kind == ConstraintListComponent.ConstraintKind.MatchTargetSize)
                {
                    if (!TryGetEntitySizeTarget(targetEntity, out Vector2 targetSize, out _))
                    {
                        continue;
                    }

                    TrySetComputedSize(entity, targetSize);
                    continue;
                }

                if (kind == ConstraintListComponent.ConstraintKind.MatchTargetPosition)
                {
                    if (!TryGetEntityWorldTransformEcs(targetEntity, out WorldTransform targetWorld))
                    {
                        continue;
                    }

                    EntityId parent = _world.GetParent(entity);
                    if (!TryGetParentLocalPointFromWorldEcs(parent, targetWorld.OriginWorld, out Vector2 localPoint))
                    {
                        continue;
                    }

                    TrySetComputedPivotPosition(entity, localPoint);
                    continue;
                }

                if (kind == ConstraintListComponent.ConstraintKind.Scroll)
                {
                    EnsureDraggableEnabledForScrollHandle(entity);
                    if (!TryApplyScrollConstraint(entity, targetEntity, flagsValue[constraintIndex]))
                    {
                        continue;
                    }
                }
            }
        }
    }

    private void EnsureDraggableEnabledForScrollHandle(EntityId entity)
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

    private bool TryApplyScrollConstraint(EntityId handleEntity, EntityId scrollObjectEntity, byte flags)
    {
        EntityId scrollObjectParent = _world.GetParent(scrollObjectEntity);
        if (scrollObjectParent.IsNull)
        {
            return false;
        }

        EntityId trackEntity = _world.GetParent(handleEntity);
        if (trackEntity.IsNull)
        {
            return false;
        }

        if (!TryGetEntitySizeTarget(scrollObjectEntity, out Vector2 scrollObjectSize, out _))
        {
            return false;
        }
        if (!TryGetEntitySizeTarget(scrollObjectParent, out Vector2 viewportSize, out _))
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
        Vector2 viewportPivot01 = GetTransformPivot01OrDefault(scrollObjectParent);
        Vector2 trackPivot01 = GetTransformPivot01OrDefault(trackEntity);
        Vector2 handlePivot01 = GetTransformPivot01OrDefault(handleEntity);

        ImRect viewportRectLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, viewportPivot01, viewportSize);
        ImRect scrollObjectRectLocal = RectTransformMath.GetRectFromPivot(scrollObjectPivotPos, scrollObjectPivot01, scrollObjectSize);
        ImRect trackRectLocal = RectTransformMath.GetRectFromPivot(Vector2.Zero, trackPivot01, trackSize);
        ImRect handleRectLocal = RectTransformMath.GetRectFromPivot(handlePivotPos, handlePivot01, handleSize);

        Insets viewportPadding = GetLayoutContainerPaddingOrZero(scrollObjectParent);
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
        bool resizeHandle = (flags & 1) != 0;
        if (!hasVertical && !hasHorizontal)
        {
            // No overflow: still keep the handle sized/placed deterministically (use track aspect to pick axis).
            bool useVerticalNoOverflow = trackContentRectLocal.Height >= trackContentRectLocal.Width;
            if (useVerticalNoOverflow)
            {
                float handleHeight = handleSize.Y;
                if (resizeHandle)
                {
                    const float minHandle = 10f;
                    handleHeight = Math.Clamp(trackContentRectLocal.Height, minHandle, MathF.Max(minHandle, trackContentRectLocal.Height));
                    if (MathF.Abs(handleHeight - handleSize.Y) > 0.0001f)
                    {
                        TrySetComputedSize(handleEntity, new Vector2(handleSize.X, handleHeight));
                        handleSize.Y = handleHeight;
                    }
                }

                float desiredY = trackContentRectLocal.Y;
                var desiredRect = new ImRect(handleRectLocal.X, desiredY, handleSize.X, handleSize.Y);
                Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(desiredRect, handlePivot01);
                return TrySetComputedPivotPosition(handleEntity, pivotPos);
            }
            else
            {
                float handleWidth = handleSize.X;
                if (resizeHandle)
                {
                    const float minHandle = 10f;
                    handleWidth = Math.Clamp(trackContentRectLocal.Width, minHandle, MathF.Max(minHandle, trackContentRectLocal.Width));
                    if (MathF.Abs(handleWidth - handleSize.X) > 0.0001f)
                    {
                        TrySetComputedSize(handleEntity, new Vector2(handleWidth, handleSize.Y));
                        handleSize.X = handleWidth;
                    }
                }

                float desiredX = trackContentRectLocal.X;
                var desiredRect = new ImRect(desiredX, handleRectLocal.Y, handleSize.X, handleSize.Y);
                Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(desiredRect, handlePivot01);
                return TrySetComputedPivotPosition(handleEntity, pivotPos);
            }
        }

        bool useVertical = hasVertical && (!hasHorizontal || overflowY >= overflowX);

        if (useVertical)
        {
            float availableScroll = overflowY;
            float t = Math.Clamp((viewportContentRectLocal.Y - scrollBoundsRectLocal.Y) / availableScroll, 0f, 1f);

            float handleHeight = handleSize.Y;
            if (resizeHandle)
            {
                float ratio = viewportContentRectLocal.Height <= 0.0001f || scrollBoundsRectLocal.Height <= 0.0001f
                    ? 1f
                    : viewportContentRectLocal.Height / scrollBoundsRectLocal.Height;

                ratio = Math.Clamp(ratio, 0f, 1f);
                float desired = trackContentRectLocal.Height * ratio;

                const float minHandle = 10f;
                handleHeight = Math.Clamp(desired, minHandle, MathF.Max(minHandle, trackContentRectLocal.Height));
            }

            float travel = trackContentRectLocal.Height - handleHeight;
            float desiredY = travel <= 0.0001f ? trackContentRectLocal.Y : (trackContentRectLocal.Y + t * travel);

            if (resizeHandle && MathF.Abs(handleHeight - handleSize.Y) > 0.0001f)
            {
                TrySetComputedSize(handleEntity, new Vector2(handleSize.X, handleHeight));
                handleSize.Y = handleHeight;
            }

            var desiredRect = new ImRect(handleRectLocal.X, desiredY, handleSize.X, handleSize.Y);
            Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(desiredRect, handlePivot01);
            return TrySetComputedPivotPosition(handleEntity, pivotPos);
        }
        else
        {
            float availableScroll = overflowX;
            float t = Math.Clamp((viewportContentRectLocal.X - scrollBoundsRectLocal.X) / availableScroll, 0f, 1f);

            float handleWidth = handleSize.X;
            if (resizeHandle)
            {
                float ratio = viewportContentRectLocal.Width <= 0.0001f || scrollBoundsRectLocal.Width <= 0.0001f
                    ? 1f
                    : viewportContentRectLocal.Width / scrollBoundsRectLocal.Width;

                ratio = Math.Clamp(ratio, 0f, 1f);
                float desired = trackContentRectLocal.Width * ratio;

                const float minHandle = 10f;
                handleWidth = Math.Clamp(desired, minHandle, MathF.Max(minHandle, trackContentRectLocal.Width));
            }

            float travel = trackContentRectLocal.Width - handleWidth;
            float desiredX = travel <= 0.0001f ? trackContentRectLocal.X : (trackContentRectLocal.X + t * travel);

            if (resizeHandle && MathF.Abs(handleWidth - handleSize.X) > 0.0001f)
            {
                TrySetComputedSize(handleEntity, new Vector2(handleWidth, handleSize.Y));
                handleSize.X = handleWidth;
            }

            var desiredRect = new ImRect(desiredX, handleRectLocal.Y, handleSize.X, handleSize.Y);
            Vector2 pivotPos = RectTransformMath.GetPivotPosFromRect(desiredRect, handlePivot01);
            return TrySetComputedPivotPosition(handleEntity, pivotPos);
        }
    }

    private bool TryGetComputedPivotPosition(EntityId entity, out Vector2 pivotPos)
    {
        pivotPos = Vector2.Zero;
        if (!TryGetComputedTransform(entity, out ComputedTransformComponentHandle handle))
        {
            return false;
        }

        var computed = ComputedTransformComponent.Api.FromHandle(_propertyWorld, handle);
        if (!computed.IsAlive)
        {
            return false;
        }

        pivotPos = computed.Position;
        return true;
    }

    private Vector2 GetTransformPivot01OrDefault(EntityId entity)
    {
        if (!TryGetTransform(entity, out TransformComponentHandle handle))
        {
            return Vector2.Zero;
        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, handle);
        if (!transform.IsAlive)
        {
            return Vector2.Zero;
        }

        return transform.Anchor;
    }

    private Insets GetLayoutContainerPaddingOrZero(EntityId entity)
    {
        if (!TryGetTransform(entity, out TransformComponentHandle handle))
        {
            return default;
        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, handle);
        if (!transform.IsAlive)
        {
            return default;
        }

        return Insets.FromVector4(transform.LayoutContainerPadding);
    }

    private Insets GetLayoutChildMarginOrZero(EntityId entity)
    {
        if (!TryGetTransform(entity, out TransformComponentHandle handle))
        {
            return default;
        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, handle);
        if (!transform.IsAlive)
        {
            return default;
        }

        return Insets.FromVector4(transform.LayoutChildMargin);
    }

    private static ImRect ExpandRect(ImRect rect, in Insets inset)
    {
        float x = rect.X - inset.Left;
        float y = rect.Y - inset.Top;
        float w = rect.Width + inset.Left + inset.Right;
        float h = rect.Height + inset.Top + inset.Bottom;
        return new ImRect(x, y, MathF.Max(0f, w), MathF.Max(0f, h));
    }

    private uint GetConstraintInstanceRootStableId(EntityId entity)
    {
        if (entity.IsNull)
        {
            return 0;
        }

        if (_world.TryGetComponent(entity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) && expandedAny.IsValid)
        {
            var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
            var expanded = PrefabExpandedComponent.Api.FromHandle(_propertyWorld, expandedHandle);
            if (expanded.IsAlive)
            {
                return expanded.InstanceRootStableId;
            }
        }

        if (_world.GetNodeType(entity) == UiNodeType.PrefabInstance)
        {
            return _world.GetStableId(entity);
        }

        return 0;
    }

    private bool TryResolveConstraintTargetEntity(uint instanceRootStableId, uint targetSourceStableId, out EntityId targetEntity)
    {
        targetEntity = EntityId.Null;

        if (targetSourceStableId == 0)
        {
            return false;
        }

        if (instanceRootStableId == 0)
        {
            targetEntity = _world.GetEntityByStableId(targetSourceStableId);
            return !targetEntity.IsNull;
        }

        if (!TryResolveExpandedStableId(instanceRootStableId, targetSourceStableId, out uint expandedStableId))
        {
            return false;
        }

        targetEntity = _world.GetEntityByStableId(expandedStableId);
        return !targetEntity.IsNull;
    }

    private bool TryResolveExpandedStableId(uint instanceRootStableId, uint sourceNodeStableId, out uint expandedStableId)
    {
        expandedStableId = 0;
        if (!TryGetConstraintExpandedLookup(instanceRootStableId, out ConstraintExpandedLookup lookup))
        {
            return false;
        }

        if (lookup.Count <= 0)
        {
            return false;
        }

        if (!TryBinarySearch(lookup.SourceNodeStableIds.AsSpan(0, lookup.Count), sourceNodeStableId, out int index))
        {
            return false;
        }

        expandedStableId = lookup.ExpandedNodeStableIds[index];
        return expandedStableId != 0;
    }

    private bool TryGetConstraintExpandedLookup(uint instanceRootStableId, out ConstraintExpandedLookup lookup)
    {
        lookup = null!;

        if (instanceRootStableId == 0)
        {
            return false;
        }

        for (int i = 0; i < _constraintExpandedLookupCount; i++)
        {
            ConstraintExpandedLookup existing = _constraintExpandedLookups[i];
            if (existing.InstanceRootStableId == instanceRootStableId)
            {
                lookup = existing;
                return EnsureConstraintExpandedLookupUpToDate(lookup);
            }
        }

        if (_constraintExpandedLookupCount >= _constraintExpandedLookups.Length)
        {
            int next = Math.Max(8, _constraintExpandedLookups.Length * 2);
            Array.Resize(ref _constraintExpandedLookups, next);
        }

        var created = new ConstraintExpandedLookup
        {
            InstanceRootStableId = instanceRootStableId
        };
        _constraintExpandedLookups[_constraintExpandedLookupCount++] = created;
        lookup = created;
        return EnsureConstraintExpandedLookupUpToDate(lookup);
    }

    private bool EnsureConstraintExpandedLookupUpToDate(ConstraintExpandedLookup lookup)
    {
        if (lookup.InstanceRootStableId == 0)
        {
            lookup.Count = 0;
            return false;
        }

        EntityId instanceRoot = _world.GetEntityByStableId(lookup.InstanceRootStableId);
        if (instanceRoot.IsNull)
        {
            lookup.Count = 0;
            lookup.SourcePrefabStableId = 0;
            lookup.SourcePrefabRevision = 0;
            return false;
        }

        if (!_world.TryGetComponent(instanceRoot, PrefabInstanceComponent.Api.PoolIdConst, out AnyComponentHandle instanceAny) || !instanceAny.IsValid)
        {
            lookup.Count = 0;
            lookup.SourcePrefabStableId = 0;
            lookup.SourcePrefabRevision = 0;
            return false;
        }

        var instanceHandle = new PrefabInstanceComponentHandle(instanceAny.Index, instanceAny.Generation);
        var instance = PrefabInstanceComponent.Api.FromHandle(_propertyWorld, instanceHandle);
        if (!instance.IsAlive || instance.SourcePrefabStableId == 0)
        {
            lookup.Count = 0;
            lookup.SourcePrefabStableId = 0;
            lookup.SourcePrefabRevision = 0;
            return false;
        }

        uint sourcePrefabStableId = instance.SourcePrefabStableId;
        EntityId sourcePrefabEntity = _world.GetEntityByStableId(sourcePrefabStableId);
        uint sourceRevision = GetPrefabRevision(sourcePrefabEntity);

        if (lookup.SourcePrefabStableId == sourcePrefabStableId && lookup.SourcePrefabRevision == sourceRevision && lookup.Count > 0)
        {
            return true;
        }

        lookup.SourcePrefabStableId = sourcePrefabStableId;
        lookup.SourcePrefabRevision = sourceRevision;
        RebuildConstraintExpandedLookup(lookup);
        return lookup.Count > 0;
    }

    private void RebuildConstraintExpandedLookup(ConstraintExpandedLookup lookup)
    {
        lookup.Count = 0;

        uint instanceRootStableId = lookup.InstanceRootStableId;
        if (instanceRootStableId == 0)
        {
            return;
        }

        int entityCount = _world.EntityCount;
        int count = 0;
        for (int entityIndex = 1; entityIndex <= entityCount; entityIndex++)
        {
            var entity = new EntityId(entityIndex);
            if (!_world.TryGetComponent(entity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) || !expandedAny.IsValid)
            {
                continue;
            }

            var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
            var expanded = PrefabExpandedComponent.Api.FromHandle(_propertyWorld, expandedHandle);
            if (!expanded.IsAlive || expanded.InstanceRootStableId != instanceRootStableId || expanded.SourceNodeStableId == 0)
            {
                continue;
            }

            count++;
        }

        if (count <= 0)
        {
            return;
        }

        if (lookup.SourceNodeStableIds.Length < count)
        {
            int next = Math.Max(count, lookup.SourceNodeStableIds.Length * 2);
            lookup.SourceNodeStableIds = new uint[next];
            lookup.ExpandedNodeStableIds = new uint[next];
        }

        int write = 0;
        for (int entityIndex = 1; entityIndex <= entityCount; entityIndex++)
        {
            var entity = new EntityId(entityIndex);
            if (!_world.TryGetComponent(entity, PrefabExpandedComponent.Api.PoolIdConst, out AnyComponentHandle expandedAny) || !expandedAny.IsValid)
            {
                continue;
            }

            var expandedHandle = new PrefabExpandedComponentHandle(expandedAny.Index, expandedAny.Generation);
            var expanded = PrefabExpandedComponent.Api.FromHandle(_propertyWorld, expandedHandle);
            if (!expanded.IsAlive || expanded.InstanceRootStableId != instanceRootStableId || expanded.SourceNodeStableId == 0)
            {
                continue;
            }

            if (write >= lookup.SourceNodeStableIds.Length)
            {
                break;
            }

            lookup.SourceNodeStableIds[write] = expanded.SourceNodeStableId;
            lookup.ExpandedNodeStableIds[write] = _world.GetStableId(entity);
            write++;

            if (write >= count)
            {
                break;
            }
        }

        if (write <= 0)
        {
            return;
        }

        Array.Sort(lookup.SourceNodeStableIds, lookup.ExpandedNodeStableIds, 0, write);
        lookup.Count = write;
    }

    private static bool TryBinarySearch(ReadOnlySpan<uint> sortedKeys, uint key, out int index)
    {
        int lo = 0;
        int hi = sortedKeys.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            uint v = sortedKeys[mid];
            if (v == key)
            {
                index = mid;
                return true;
            }
            if (v < key)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        index = -1;
        return false;
    }
}
