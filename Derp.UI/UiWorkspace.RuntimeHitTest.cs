using System.Numerics;

namespace Derp.UI;

public sealed partial class UiWorkspace
{
    internal uint HitTestHoveredStableIdForRuntime(EntityId prefabEntity, Vector2 pointerWorld)
    {
        if (prefabEntity.IsNull || _world.GetNodeType(prefabEntity) != UiNodeType.Prefab)
        {
            return 0;
        }

        // Runtime input should hit the actual expanded prefab-instance clone nodes so nested event listeners work.
        // Editor selection uses "opaque prefab instance selection"; runtime does not.
        EntityId hovered = FindTopmostShapeEntityInPrefab(
            prefabEntity,
            pointerWorld,
            includeGroupedShapes: true,
            resolveOpaquePrefabInstanceSelection: false);
        if (hovered.IsNull)
        {
            return 0;
        }

        return _world.GetStableId(hovered);
    }
}
