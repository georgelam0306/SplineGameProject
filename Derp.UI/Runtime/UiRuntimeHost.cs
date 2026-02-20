namespace Derp.UI;

internal sealed class UiRuntimeHost
{
    private readonly UiWorld _world;

    public UiRuntimeHost(UiWorld world)
    {
        _world = world;
    }

    // Placeholder for the runtime-only host. This will own the runtime UiWorld and tick runtime systems.
}
