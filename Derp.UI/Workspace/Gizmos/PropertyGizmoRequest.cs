using Property.Runtime;

namespace Derp.UI;

internal readonly struct PropertyGizmoRequest
{
    public readonly PropertySlot Slot;
    public readonly EntityId TargetEntity;
    public readonly int WidgetId;
    public readonly PropertyGizmoPhase Phase;
    public readonly int Payload0;
    public readonly int Payload1;

    public PropertyGizmoRequest(PropertySlot slot, EntityId targetEntity, int widgetId, PropertyGizmoPhase phase, int payload0, int payload1)
    {
        Slot = slot;
        TargetEntity = targetEntity;
        WidgetId = widgetId;
        Phase = phase;
        Payload0 = payload0;
        Payload1 = payload1;
    }
}

