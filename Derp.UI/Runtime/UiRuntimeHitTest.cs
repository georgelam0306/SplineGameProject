using System.Numerics;

namespace Derp.UI;

public static class UiRuntimeHitTest
{
    public static uint HitHoveredStableId(UiWorkspace workspace, uint activePrefabStableId, Vector2 pointerWorld)
    {
        if (workspace == null)
        {
            return 0;
        }

        EntityId activePrefabEntity = workspace.World.GetEntityByStableId(activePrefabStableId);
        return workspace.HitTestHoveredStableIdForRuntime(activePrefabEntity, pointerWorld);
    }
}
