using Friflo.Engine.ECS.Systems;
using Raylib_cs;

namespace BaseTemplate.Presentation.Rendering.Systems;

/// <summary>
/// Ends rendering to the pixel-perfect render texture and draws it scaled to screen.
/// Must run after all world-space rendering systems but before screen-space UI.
/// </summary>
public sealed class PixelPerfectEndSystem : BaseSystem
{
    private readonly PixelPerfectRenderer _renderer;

    public PixelPerfectEndSystem(PixelPerfectRenderer renderer)
    {
        _renderer = renderer;
    }

    protected override void OnUpdateGroup()
    {
        _renderer.EndTextureMode();
        _renderer.DrawToScreen(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
    }
}
