using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Input;
using Property;
using Property.Runtime;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private bool _hasActivePropertyGizmo;
    private PropertyGizmoRequest _activePropertyGizmo;
    private bool _hasHoveredPropertyGizmo;
    private PropertyGizmoRequest _hoveredPropertyGizmo;

    internal void ReportPropertyGizmo(in PropertyGizmoRequest request)
    {
        if (request.Phase == PropertyGizmoPhase.Active)
        {
            _hasActivePropertyGizmo = true;
            _activePropertyGizmo = request;
            return;
        }

        _hasHoveredPropertyGizmo = true;
        _hoveredPropertyGizmo = request;
    }

    private void ClearPropertyGizmosForFrame()
    {
        _hasActivePropertyGizmo = false;
        _activePropertyGizmo = default;
        _hasHoveredPropertyGizmo = false;
        _hoveredPropertyGizmo = default;
    }

    private bool HandlePropertyGizmosInput(in ImInput input, Vector2 canvasOrigin, Vector2 mouseCanvas)
    {
        if (!_hasActivePropertyGizmo)
        {
            return false;
        }

        return HandleGeneratedPropertyGizmoInput(in input, canvasOrigin, mouseCanvas, _activePropertyGizmo);
    }

    private void DrawPropertyGizmosForFrame(int frameId, Vector2 canvasOrigin)
    {
        if (_hasActivePropertyGizmo)
        {
            DrawGeneratedPropertyGizmo(frameId, canvasOrigin, _activePropertyGizmo);
            return;
        }

        if (_hasHoveredPropertyGizmo)
        {
            DrawGeneratedPropertyGizmo(frameId, canvasOrigin, _hoveredPropertyGizmo);
        }
    }

    private void UpdatePropertyGizmosFromInspectorState()
    {
        if (_hasActivePropertyGizmo)
        {
            return;
        }

        if (!PropertyInspector.TryGetActivePaintFillGradientEditor(out EntityId targetEntity, out PaintComponentHandle paintHandle, out int layerIndex))
        {
            return;
        }

        AnyComponentHandle paintAny = PaintComponentProperties.ToAnyHandle(paintHandle);
        PropertySlot slot = PaintComponentPropertySlot.ArrayElement(paintAny, "FillGradientDirection", layerIndex, PropertyKind.Vec2);
        int widgetId = Im.Context.GetId("paint_gradient_gizmo");
        ReportPropertyGizmo(new PropertyGizmoRequest(slot, targetEntity, widgetId, PropertyGizmoPhase.Active, payload0: layerIndex, payload1: 0));
    }
}
