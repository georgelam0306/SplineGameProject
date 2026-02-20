using Property.Runtime;

namespace Derp.UI;

internal static class AnimationSelectionInspector
{
    public static bool TryDraw(UiWorkspace workspace)
    {
        if (workspace == null)
        {
            return false;
        }

        if (!workspace.TryGetActivePrefabEntity(out EntityId prefabEntity) || prefabEntity.IsNull)
        {
            return false;
        }

        if (!AnimationSelectionSync.TrySyncSelection(workspace, prefabEntity, out var kind, out AnyComponentHandle selectionAny) || !selectionAny.IsValid)
        {
            return false;
        }

        string title = kind == AnimationSelectionComponentKind.Keyframe ? "Keyframe" : "Timeline";

        InspectorCard.Begin(title);
        workspace.PropertyInspector.DrawComponentPropertiesContent(prefabEntity, title, selectionAny);
        InspectorCard.End();
        return true;
    }
}

