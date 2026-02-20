using DerpLib.ImGui;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    private const int InspectorPopoversRenderedViewportCapacity = 16;
    private readonly DerpLib.ImGui.Viewport.ImViewport?[] _inspectorPopoversRenderedViewports = new DerpLib.ImGui.Viewport.ImViewport?[InspectorPopoversRenderedViewportCapacity];
    private int _inspectorPopoversRenderedViewportCount;
    private int _inspectorPopoversRenderedFrame = -1;

    public void DrawToolbarWindow()
    {
        Toolbar.DrawToolbarWindow(this);
    }

    public void DrawLayersPanel()
    {
        LayersPanel.DrawLayersPanel(this);
    }

    public void DrawInspectorPanel()
    {
        InspectorPanel.DrawInspectorPanel(this);
    }

    public void DrawVariablesPanel()
    {
        VariablesPanel.DrawVariablesPanel(this);
    }

    public void DrawAnimationEditorWindow()
    {
        AnimationEditorWindow.DrawAnimationEditorWindow(this);
    }

    internal bool ColorPopoverCapturesMouse(DerpLib.ImGui.Viewport.ImViewport viewport)
    {
        return _propertyInspector.ColorPopoverCapturesMouse(viewport);
    }

    internal void RenderInspectorColorPopover()
    {
        _propertyInspector.RenderInspectorColorPopover();
    }

    internal bool InspectorPopoversCaptureMouse(DerpLib.ImGui.Viewport.ImViewport viewport)
    {
        return _propertyInspector.InspectorPopoversCaptureMouse(viewport);
    }

    internal void RenderInspectorPopovers()
    {
        var viewport = Im.CurrentViewport;
        if (viewport == null)
        {
            return;
        }

        int frame = Im.Context.FrameCount;
        if (frame != _inspectorPopoversRenderedFrame)
        {
            _inspectorPopoversRenderedFrame = frame;
            _inspectorPopoversRenderedViewportCount = 0;
        }

        for (int index = 0; index < _inspectorPopoversRenderedViewportCount; index++)
        {
            if (ReferenceEquals(_inspectorPopoversRenderedViewports[index], viewport))
            {
                return;
            }
        }

        if (!_propertyInspector.HasOpenInspectorPopovers(viewport))
        {
            return;
        }

        _propertyInspector.RenderInspectorPopovers(this);

        if (_inspectorPopoversRenderedViewportCount < _inspectorPopoversRenderedViewports.Length)
        {
            _inspectorPopoversRenderedViewports[_inspectorPopoversRenderedViewportCount] = viewport;
            _inspectorPopoversRenderedViewportCount++;
        }
    }

    internal void SetInspectorContentRectViewport(DerpLib.ImGui.Core.ImRect rectViewport)
    {
        _propertyInspector.SetInspectorContentRectViewport(rectViewport);
    }
}
