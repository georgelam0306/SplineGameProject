using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 210)]
public partial struct BlendComponent
{
    [Column]
    [Property(Name = "Visible", Group = "Blend", Order = 0)]
    public bool IsVisible;

    [Column]
    [Property(Name = "Opacity", Group = "Blend", Order = 1, Min = 0f, Max = 1f, Step = 0.01f)]
    public float Opacity;

    [Column]
    [Property(Name = "Blend Mode", Group = "Blend", Order = 2, Min = 0f, Max = 15f, Step = 1f)]
    public int BlendMode;
}
