using System.Numerics;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 202)]
public partial struct RectGeometryComponent
{
    [Column]
    [Property(Name = "Size", Group = "Rectangle Path", Order = 0)]
    public Vector2 Size;

	    [Column]
	    [PropertyDrawer(typeof(CornerRadiusDrawer))]
	    [Property(Name = "Corner Radius", Group = "Rectangle Path", Order = 1, Min = 0f, Step = 0.5f)]
	    // Corner order: TL, TR, BR, BL (X, Y, Z, W).
	    public Vector4 CornerRadius;
}
