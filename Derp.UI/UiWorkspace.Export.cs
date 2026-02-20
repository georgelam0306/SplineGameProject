namespace Derp.UI;

public sealed partial class UiWorkspace
{
    internal void PreparePrefabInstancesForExport()
    {
        // Ensure any authored prefab instances have their expansion state up-to-date so export
        // can safely reason about authored vs expanded nodes.
        UpdatePrefabInstances();
    }
}

