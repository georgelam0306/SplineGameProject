using DerpLib.ImGui;
using DerpLib.ImGui.Layout;

namespace Derp.UI;

internal static class VariablesPanel
{
    public static void DrawVariablesPanel(UiWorkspace workspace)
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

        // Track window rect in viewport-space for popover placement.
        var rectViewport = Im.WindowRect.Offset(Im.CurrentTranslation);
        workspace.SetInspectorContentRectViewport(rectViewport);

        InspectorCard.DrawInspectorBackground(Im.WindowContentRect);

        EntityId selected = EntityId.Null;
        if (workspace._selectedEntities.Count == 1)
        {
            selected = workspace._selectedEntities[0];
        }
        else if (workspace._selectedEntities.Count == 0 && !workspace._selectedPrefabEntity.IsNull)
        {
            selected = workspace._selectedPrefabEntity;
        }

        if (selected.IsNull)
        {
            InspectorCard.Begin("Variables");
            InspectorHint.Draw("Select a prefab or prefab instance.");
            InspectorCard.End();
        }
        else
        {
            UiNodeType type = workspace.World.GetNodeType(selected);
            if (type == UiNodeType.Prefab)
            {
                PrefabVariablesPanel.DrawForPrefab(workspace, selected);
            }
            else if (type == UiNodeType.PrefabInstance)
            {
                PrefabVariablesPanel.DrawForInstance(workspace, selected);
            }
            else
            {
                InspectorCard.Begin("Variables");
                InspectorHint.Draw("Select a prefab or prefab instance.");
                InspectorCard.End();
            }
        }

        ImLayout.Space(18f);
        RuntimeDebugPanel.Draw(workspace);
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
