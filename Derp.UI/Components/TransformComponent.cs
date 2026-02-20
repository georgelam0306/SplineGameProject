using System.Numerics;
using Core;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 200)]
public partial struct TransformComponent
{
    [Column]
    [Property(Name = "Position", Group = "Transform", Order = 0)]
    public Vector2 Position;

    [Column]
    [Property(Name = "Scale", Group = "Transform", Order = 1, Min = 0f, Step = 0.01f)]
    public Vector2 Scale;

    [Column]
    [Property(Name = "Anchor", Group = "Transform", Order = 2, Min = 0f, Max = 1f, Step = 0.01f)]
    public Vector2 Anchor;

    [Column]
    [Property(Name = "Rotation", Group = "Transform", Order = 3, Min = -180f, Max = 180f, Step = 1f)]
    public float Rotation;

    [Column]
    [Property(Name = "Depth", Group = "Transform", Order = 4)]
    public float Depth;

    [Column]
    [Property(Name = "Container Enabled", Group = "Layout Container", Order = -3)]
    public bool LayoutContainerEnabled;

    [Column]
    [Property(Name = "Layout", Group = "Layout Container", Order = -2, Min = 0f, Max = 2f, Step = 1f)]
    public int LayoutContainerLayout;

    [Column]
    [Property(Name = "Direction", Group = "Layout Container", Order = -1, Min = 0f, Max = 1f, Step = 1f)]
    public int LayoutContainerDirection;

	    [Column]
	    [PropertyDrawer(typeof(InsetsDrawer))]
	    [Property(Name = "Padding", Group = "Layout Container", Order = 0)]
	    // Order: L, T, R, B.
	    public Vector4 LayoutContainerPadding;

    [Column]
    [Property(Name = "Spacing", Group = "Layout Container", Order = 1, Min = 0f, Step = 1f)]
    public float LayoutContainerSpacing;

    [Column]
    [Property(Name = "Align Items", Group = "Layout Container", Order = 2, Min = 0f, Max = 3f, Step = 1f)]
    public int LayoutContainerAlignItems;

    [Column]
    [Property(Name = "Justify", Group = "Layout Container", Order = 3, Min = 0f, Max = 5f, Step = 1f)]
    public int LayoutContainerJustify;

    [Column]
    [Property(Name = "Width Mode", Group = "Layout Container", Order = 4, Min = 0f, Max = 2f, Step = 1f)]
    public int LayoutContainerWidthMode;

    [Column]
    [Property(Name = "Height Mode", Group = "Layout Container", Order = 5, Min = 0f, Max = 2f, Step = 1f)]
    public int LayoutContainerHeightMode;

    [Column]
    [Property(Name = "Grid Columns", Group = "Layout Container", Order = 6, Min = 1f, Max = 64f, Step = 1f)]
    public int LayoutContainerGridColumns;

    [Column]
    [Property(Name = "Ignore Layout", Group = "Layout Child", Order = -1)]
    public bool LayoutChildIgnoreLayout;

	    [Column]
	    [PropertyDrawer(typeof(InsetsDrawer))]
	    [Property(Name = "Margin", Group = "Layout Child", Order = 0)]
	    // Order: L, T, R, B.
	    public Vector4 LayoutChildMargin;

    [Column]
    [Property(Name = "Align Self", Group = "Layout Child", Order = 1, Min = 0f, Max = 4f, Step = 1f)]
    public int LayoutChildAlignSelf;

    [Column]
    [Property(Name = "Flex Grow", Group = "Layout Child", Order = 10, Min = 0f, Step = 0.1f)]
    public float LayoutChildFlexGrow;

    [Column]
    [Property(Name = "Flex Shrink", Group = "Layout Child", Order = 11, Min = 0f, Step = 0.1f)]
    public float LayoutChildFlexShrink;

    [Column]
    [Property(Name = "Preferred Size", Group = "Layout Child", Order = 12, Min = 0f, Step = 1f)]
    public Vector2 LayoutChildPreferredSize;

    [Column]
    [Property(Name = "Constraints Enabled", Group = "Layout Constraint", Order = -1)]
    public bool LayoutConstraintEnabled;

    [Column]
    [Property(Name = "Anchor Min", Group = "Layout Constraint", Order = 0, Min = 0f, Max = 1f, Step = 0.01f)]
    public Vector2 LayoutConstraintAnchorMin;

    [Column]
    [Property(Name = "Anchor Max", Group = "Layout Constraint", Order = 1, Min = 0f, Max = 1f, Step = 0.01f)]
    public Vector2 LayoutConstraintAnchorMax;

    [Column]
    [Property(Name = "Offset Min", Group = "Layout Constraint", Order = 2, Step = 1f)]
    public Vector2 LayoutConstraintOffsetMin;

    [Column]
    [Property(Name = "Offset Max", Group = "Layout Constraint", Order = 3, Step = 1f)]
    public Vector2 LayoutConstraintOffsetMax;

    [Column]
    [Property(Name = "Min Size", Group = "Layout Constraint", Order = 10, Min = 0f, Step = 1f)]
    public Vector2 LayoutConstraintMinSize;

    [Column]
    [Property(Name = "Max Size", Group = "Layout Constraint", Order = 11, Min = 0f, Step = 1f)]
    public Vector2 LayoutConstraintMaxSize;
}
