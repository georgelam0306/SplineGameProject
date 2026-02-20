using System;
using System.Numerics;
using Core;
using DerpLib.ImGui.Core;
using Property.Runtime;
using DerpLib.Sdf;
using static Derp.UI.UiColor32;
using static Derp.UI.UiFillGradient;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private int AddFillGradientStops(
        CanvasSdfDrawList draw,
        in FillComponent.ViewProxy fillView,
        float opacity,
        out uint startArgb,
        out uint endArgb,
        out int stopCount)
    {
        startArgb = 0u;
        endArgb = 0u;
        stopCount = GetFillGradientStopCount(fillView);
        if (stopCount <= 0)
        {
            return -1;
        }

        Span<SdfGradientStop> stops = stackalloc SdfGradientStop[MaxStops];
        Span<uint> stopColorsArgb = stackalloc uint[MaxStops];
        int visibleStopCount = 0;
        for (int stopIndex = 0; stopIndex < stopCount; stopIndex++)
        {
            float t = GetFillGradientStopT(fillView, stopIndex);
            Color32 stopColor = GetFillGradientStopColor(fillView, stopIndex);
            stopColor = ApplyTintAndOpacity(stopColor, tint: Color32.White, opacity);
            uint argb = stopColor.A == 0 ? 0u : ToArgb(stopColor);
            stopColorsArgb[stopIndex] = argb;

            var stop = new SdfGradientStop
            {
                Color = ImStyle.ToVector4(argb),
                Params = new Vector4(Math.Clamp(t, 0f, 1f), 0f, 0f, 0f)
            };
            stops[stopIndex] = stop;

            if (argb != 0u)
            {
                visibleStopCount++;
            }
        }

        if (visibleStopCount == 0)
        {
            return -1;
        }

        startArgb = stopColorsArgb[0];
        endArgb = stopColorsArgb[stopCount - 1];
        int stopStartIndex = draw.AddGradientStops(stops.Slice(0, stopCount));
        return stopStartIndex;
    }

    private static Vector2 GetShadowOffset(Vector2 direction, float length)
    {
        if (length <= 0f)
        {
            return Vector2.Zero;
        }

        float magnitude = direction.Length();
        if (magnitude <= 0.0001f)
        {
            return Vector2.Zero;
        }

        float scale = length / magnitude;
        return new Vector2(direction.X * scale, direction.Y * scale);
    }

    private static Vector2 RotateVector(Vector2 value, float radians)
    {
        if (radians == 0f)
        {
            return value;
        }

        float sin = MathF.Sin(radians);
        float cos = MathF.Cos(radians);
        return new Vector2(
            value.X * cos - value.Y * sin,
            value.X * sin + value.Y * cos);
    }

    private static float ComputeGradientAngleRadians(Vector2 direction)
    {
        if (direction.LengthSquared() <= 0.0001f)
        {
            return 0f;
        }

        return MathF.Atan2(direction.Y, direction.X);
    }

    private static bool IsPointInsideOrientedRect(Vector2 centerWorld, Vector2 halfSizeWorld, float rotationRadians, Vector2 pointWorld)
    {
        Vector2 local = RotateVector(pointWorld - centerWorld, -rotationRadians);
        return MathF.Abs(local.X) <= halfSizeWorld.X && MathF.Abs(local.Y) <= halfSizeWorld.Y;
    }

    private readonly struct WorldTransform
    {
        public readonly Vector2 OriginWorld;
        public readonly Vector2 Scale;
        public readonly float RotationRadians;
        public readonly bool IsVisible;

        public WorldTransform(Vector2 originWorld, Vector2 scale, float rotationRadians, bool isVisible)
        {
            OriginWorld = originWorld;
            Scale = scale;
            RotationRadians = rotationRadians;
            IsVisible = isVisible;
        }
    }

    private readonly struct ShapeWorldTransform
    {
        public readonly Vector2 PositionWorld;
        public readonly Vector2 ScaleWorld;
        public readonly float RotationRadians;
        public readonly Vector2 Anchor;
        public readonly float Opacity;
        public readonly PaintBlendMode BlendMode;
        public readonly bool IsVisible;

        public ShapeWorldTransform(
            Vector2 positionWorld,
            Vector2 scaleWorld,
            float rotationRadians,
            Vector2 anchor,
            float opacity,
            PaintBlendMode blendMode,
            bool isVisible)
        {
            PositionWorld = positionWorld;
            ScaleWorld = scaleWorld;
            RotationRadians = rotationRadians;
            Anchor = anchor;
            Opacity = opacity;
            BlendMode = blendMode;
            IsVisible = isVisible;
        }
    }

    private static WorldTransform IdentityWorldTransform => new(Vector2.Zero, Vector2.One, 0f, isVisible: true);

    private static Vector2 NormalizeScale(Vector2 scale)
    {
        float scaleX = scale.X == 0f ? 1f : scale.X;
        float scaleY = scale.Y == 0f ? 1f : scale.Y;
        return new Vector2(scaleX, scaleY);
    }

    private static Vector2 TransformPoint(in WorldTransform transform, Vector2 localPoint)
    {
        Vector2 scaled = new Vector2(localPoint.X * transform.Scale.X, localPoint.Y * transform.Scale.Y);
        Vector2 rotated = RotateVector(scaled, transform.RotationRadians);
        return transform.OriginWorld + rotated;
    }

    private static Vector2 InverseTransformPoint(in WorldTransform transform, Vector2 worldPoint)
    {
        Vector2 relativeWorld = worldPoint - transform.OriginWorld;
        Vector2 unrotated = RotateVector(relativeWorld, -transform.RotationRadians);
        float scaleX = transform.Scale.X == 0f ? 1f : transform.Scale.X;
        float scaleY = transform.Scale.Y == 0f ? 1f : transform.Scale.Y;
        return new Vector2(unrotated.X / scaleX, unrotated.Y / scaleY);
    }

    private static Vector2 InverseTransformVector(in WorldTransform transform, Vector2 worldVector)
    {
        Vector2 unrotated = RotateVector(worldVector, -transform.RotationRadians);
        float scaleX = transform.Scale.X == 0f ? 1f : transform.Scale.X;
        float scaleY = transform.Scale.Y == 0f ? 1f : transform.Scale.Y;
        return new Vector2(unrotated.X / scaleX, unrotated.Y / scaleY);
    }

    private static WorldTransform ComposeTransform(in WorldTransform parentWorld, Vector2 localPosition, Vector2 localScale, float localRotationRadians, bool localIsVisible)
    {
        Vector2 originWorld = TransformPoint(parentWorld, localPosition);
        Vector2 scaleWorld = new Vector2(parentWorld.Scale.X * localScale.X, parentWorld.Scale.Y * localScale.Y);
        float rotationWorld = parentWorld.RotationRadians + localRotationRadians;
        bool isVisible = parentWorld.IsVisible && localIsVisible;
        return new WorldTransform(originWorld, scaleWorld, rotationWorld, isVisible);
    }

    private bool TryGetBlendState(BlendComponentHandle blendHandle, out bool isVisible, out float opacity, out PaintBlendMode blendMode)
    {
        isVisible = true;
        opacity = 1f;
        blendMode = PaintBlendMode.Normal;

        if (blendHandle.IsNull)
        {
            return true;
        }

        var blend = BlendComponent.Api.FromHandle(_propertyWorld, blendHandle);
        if (!blend.IsAlive)
        {
            return false;
        }

        isVisible = blend.IsVisible;
        opacity = Math.Clamp(blend.Opacity, 0f, 1f);

        int mode = blend.BlendMode;
        if (mode < 0)
        {
            mode = 0;
        }
        else if (mode > (int)PaintBlendMode.Luminosity)
        {
            mode = (int)PaintBlendMode.Luminosity;
        }
        blendMode = (PaintBlendMode)mode;
        return true;
    }

    private bool TryGetGroupLocalTransform(BooleanGroup group, out WorldTransform localTransform)
    {
        localTransform = IdentityWorldTransform;

        if (!TryGetBlendState(group.Blend, out bool isVisible, out _, out _))
        {
            return false;
        }

        if (group.Transform.IsNull)
        {
            localTransform = new WorldTransform(Vector2.Zero, Vector2.One, 0f, isVisible);
            return true;
        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, group.Transform);
        if (!transform.IsAlive)
        {
            return false;
        }

        Vector2 scale = NormalizeScale(transform.Scale);
        float rotationRadians = transform.Rotation * (MathF.PI / 180f);
        localTransform = new WorldTransform(transform.Position, scale, rotationRadians, isVisible);
        return true;
    }

    private bool TryGetGroupWorldTransformById(int groupId, out WorldTransform worldTransform)
    {
        worldTransform = IdentityWorldTransform;

        if (groupId == 0)
        {
            return true;
        }

        Span<int> groupIndexStack = stackalloc int[64];
        int stackCount = 0;
        int currentGroupId = groupId;
        int rootParentShapeId = 0;

        while (currentGroupId != 0)
        {
            if (!TryGetGroupIndexById(currentGroupId, out int groupIndex))
            {
                return false;
            }

            if (stackCount >= groupIndexStack.Length)
            {
                return false;
            }

            groupIndexStack[stackCount++] = groupIndex;
            var group = _groups[groupIndex];
            rootParentShapeId = group.ParentShapeId;
            currentGroupId = group.ParentGroupId;
        }

        WorldTransform currentWorld = IdentityWorldTransform;
        if (rootParentShapeId != 0)
        {
            if (!TryGetShapeWorldTransformById(rootParentShapeId, out currentWorld))
            {
                return false;
            }
        }
        for (int i = stackCount - 1; i >= 0; i--)
        {
            var group = _groups[groupIndexStack[i]];
            if (!TryGetGroupLocalTransform(group, out WorldTransform localTransform))
            {
                localTransform = IdentityWorldTransform;
            }

            currentWorld = ComposeTransform(currentWorld, localTransform.OriginWorld, localTransform.Scale, localTransform.RotationRadians, localTransform.IsVisible);
        }

        worldTransform = currentWorld;
        return true;
    }

    private bool TryGetShapeWorldTransformById(int shapeId, out WorldTransform worldTransform)
    {
        worldTransform = IdentityWorldTransform;

        if (shapeId == 0)
        {
            return true;
        }

        if (!TryGetShapeIndexById(shapeId, out int shapeIndex))
        {
            return false;
        }

        var shape = _shapes[shapeIndex];

        // Get the parent's world transform (could be group or shape)
        if (!TryGetNodeParentWorldTransform(shape.ParentGroupId, shape.ParentShapeId, out WorldTransform parentWorld))
        {
            return false;
        }

        // Now apply this shape's local transform
        var transform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
        if (!transform.IsAlive)
        {
            worldTransform = new WorldTransform(parentWorld.OriginWorld, parentWorld.Scale, parentWorld.RotationRadians, isVisible: false);
            return false;
        }

        if (!TryGetBlendState(shape.Blend, out bool isVisible, out _, out _))
        {
            worldTransform = new WorldTransform(parentWorld.OriginWorld, parentWorld.Scale, parentWorld.RotationRadians, isVisible: false);
            return false;
        }

        if (!isVisible)
        {
            worldTransform = new WorldTransform(parentWorld.OriginWorld, parentWorld.Scale, parentWorld.RotationRadians, isVisible: false);
            return false;
        }

        Vector2 localScale = NormalizeScale(transform.Scale);
        Vector2 scaleWorld = new Vector2(parentWorld.Scale.X * localScale.X, parentWorld.Scale.Y * localScale.Y);
        float rotationRadians = parentWorld.RotationRadians + transform.Rotation * (MathF.PI / 180f);
        Vector2 positionWorld = TransformPoint(parentWorld, transform.Position);

        worldTransform = new WorldTransform(positionWorld, scaleWorld, rotationRadians, isVisible: true);
        return true;
    }

    private bool TryGetNodeParentWorldTransform(int parentGroupId, int parentShapeId, out WorldTransform parentWorldTransform)
    {
        // If shape parent, get shape's world transform first
        if (parentShapeId != 0)
        {
            return TryGetShapeWorldTransformById(parentShapeId, out parentWorldTransform);
        }
        // Otherwise, group parent (or identity if 0)
        return TryGetGroupWorldTransformById(parentGroupId, out parentWorldTransform);
    }

    // Backward-compatible overload for group-only parents
    private bool TryGetNodeParentWorldTransform(int parentGroupId, out WorldTransform parentWorldTransform)
    {
        return TryGetNodeParentWorldTransform(parentGroupId, 0, out parentWorldTransform);
    }

    private bool TryGetShapeWorldTransform(in Shape shape, in WorldTransform parentWorldTransform, out ShapeWorldTransform worldTransform)
    {
        worldTransform = default;

        if (!parentWorldTransform.IsVisible)
        {
            return false;
        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, shape.Transform);
        if (!transform.IsAlive)
        {
            return false;
        }

        if (!TryGetBlendState(shape.Blend, out bool isVisible, out float opacity, out PaintBlendMode blendMode))
        {
            return false;
        }

        if (!isVisible)
        {
            return false;
        }

        Vector2 localScale = NormalizeScale(transform.Scale);
        Vector2 scaleWorld = new Vector2(parentWorldTransform.Scale.X * localScale.X, parentWorldTransform.Scale.Y * localScale.Y);
        float rotationRadians = parentWorldTransform.RotationRadians + transform.Rotation * (MathF.PI / 180f);
        Vector2 positionWorld = TransformPoint(parentWorldTransform, transform.Position);

        worldTransform = new ShapeWorldTransform(
            positionWorld,
            scaleWorld,
            rotationRadians,
            transform.Anchor,
            opacity,
            blendMode,
            isVisible: true);
        return true;
    }

	    private bool TryGetGroupWorldTransform(int groupIndex, out WorldTransform parentWorldTransform, out WorldTransform groupWorldTransform)
	    {
	        parentWorldTransform = IdentityWorldTransform;
	        groupWorldTransform = IdentityWorldTransform;

        if (groupIndex < 0 || groupIndex >= _groups.Count)
        {
            return false;
        }

        var group = _groups[groupIndex];
        EnsureGroupTransformInitialized(ref group);
        _groups[groupIndex] = group;

	        if (!TryGetNodeParentWorldTransform(group.ParentGroupId, group.ParentShapeId, out parentWorldTransform))
	        {
	            parentWorldTransform = IdentityWorldTransform;
	        }

	        if (!TryGetBlendState(group.Blend, out bool groupIsVisible, out _, out _))
	        {
	            return false;
	        }

	        if (group.Transform.IsNull)
	        {
	            groupWorldTransform = new WorldTransform(
	                parentWorldTransform.OriginWorld,
	                parentWorldTransform.Scale,
	                parentWorldTransform.RotationRadians,
	                parentWorldTransform.IsVisible && groupIsVisible);
	            return true;
	        }

        var transform = TransformComponent.Api.FromHandle(_propertyWorld, group.Transform);
        if (!transform.IsAlive)
        {
            return false;
        }

	        Vector2 localScale = NormalizeScale(transform.Scale);
	        float localRotationRadians = transform.Rotation * (MathF.PI / 180f);

	        groupWorldTransform = ComposeTransform(parentWorldTransform, transform.Position, localScale, localRotationRadians, groupIsVisible);
	        return true;
	    }

}
