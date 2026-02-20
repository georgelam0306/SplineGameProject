using DerpLib.ImGui;
using DerpLib.ImGui.Layout;

namespace Derp.UI;

internal static class InspectorPanel
{
    public static void DrawInspectorPanel(UiWorkspace workspace)
    {
        var viewport = Im.CurrentViewport;
        bool restoreInputAfterPopoverCapture = false;
        bool savedMousePressed = false;
        bool savedMouseRightPressed = false;
        bool savedMouseMiddlePressed = false;
        bool savedIsDoubleClick = false;
        float savedScrollDelta = 0f;

        if (viewport != null && workspace.InspectorPopoversCaptureMouse(viewport))
        {
            restoreInputAfterPopoverCapture = true;
            savedMousePressed = viewport.Input.MousePressed;
            savedMouseRightPressed = viewport.Input.MouseRightPressed;
            savedMouseMiddlePressed = viewport.Input.MouseMiddlePressed;
            savedIsDoubleClick = viewport.Input.IsDoubleClick;
            savedScrollDelta = viewport.Input.ScrollDelta;

            viewport.Input.MousePressed = false;
            viewport.Input.MouseRightPressed = false;
            viewport.Input.MouseMiddlePressed = false;
            viewport.Input.IsDoubleClick = false;
            viewport.Input.ScrollDelta = 0f;
        }

        // Track inspector window rect in viewport-space for inspector popovers (place fully outside the inspector).
        var inspectorRectViewport = Im.WindowRect.Offset(Im.CurrentTranslation);
        workspace.SetInspectorContentRectViewport(inspectorRectViewport);

        InspectorCard.DrawInspectorBackground(Im.WindowContentRect);

        bool preferAnimationInspector = workspace.PreferAnimationInspector();
        if (preferAnimationInspector && workspace.TryGetStateMachineInspectorSelection(out var stateMachineSelection))
        {
            StateMachineSelectionInspector.Draw(workspace, stateMachineSelection);
        }
        else if (preferAnimationInspector && AnimationSelectionInspector.TryDraw(workspace))
        {
        }        
        else if (workspace._selectedEntities.Count == 1)
        {
            SelectionInspector.DrawEntityInspector(workspace, workspace._selectedEntities[0]);
        }
        else if (workspace._selectedEntities.Count >= 2)
        {
            SelectionInspector.DrawMultiSelectionInspector(workspace);
        }
        else if (!workspace._selectedPrefabEntity.IsNull)
        {
            SelectionInspector.DrawEntityInspector(workspace, workspace._selectedPrefabEntity);
        }
        else
        {
            InspectorCard.Begin("Selection");
            InspectorHint.Draw("No selection");
            InspectorCard.End();
        }

        ImLayout.Space(18f);

        if (restoreInputAfterPopoverCapture)
        {
            viewport!.Input.MousePressed = savedMousePressed;
            viewport.Input.MouseRightPressed = savedMouseRightPressed;
            viewport.Input.MouseMiddlePressed = savedMouseMiddlePressed;
            viewport.Input.IsDoubleClick = savedIsDoubleClick;
            viewport.Input.ScrollDelta = savedScrollDelta;
        }

        workspace.RenderInspectorPopovers();
    }
}
