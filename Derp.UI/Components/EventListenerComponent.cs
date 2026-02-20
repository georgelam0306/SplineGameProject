using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 239)]
public partial struct EventListenerComponent
{
    [Column]
    [Property(Name = "Hover", Group = "Events", Order = 0, Min = 0f, Step = 1f)]
    public int HoverVarId;

    [Column]
    [Property(Name = "Hover Enter", Group = "Events", Order = 1, Min = 0f, Step = 1f)]
    public int HoverEnterTriggerId;

    [Column]
    [Property(Name = "Hover Exit", Group = "Events", Order = 2, Min = 0f, Step = 1f)]
    public int HoverExitTriggerId;

    [Column]
    [Property(Name = "Press", Group = "Events", Order = 3, Min = 0f, Step = 1f)]
    public int PressTriggerId;

    [Column]
    [Property(Name = "Release", Group = "Events", Order = 4, Min = 0f, Step = 1f)]
    public int ReleaseTriggerId;

    [Column]
    [Property(Name = "Click", Group = "Events", Order = 5, Min = 0f, Step = 1f)]
    public int ClickTriggerId;

    [Column]
    [Property(Name = "Child Hover", Group = "Events", Order = 6, Min = 0f, Step = 1f)]
    public int ChildHoverVarId;

    [Column]
    [Property(Name = "Child Hover Enter", Group = "Events", Order = 7, Min = 0f, Step = 1f)]
    public int ChildHoverEnterTriggerId;

    [Column]
    [Property(Name = "Child Hover Exit", Group = "Events", Order = 8, Min = 0f, Step = 1f)]
    public int ChildHoverExitTriggerId;
}
