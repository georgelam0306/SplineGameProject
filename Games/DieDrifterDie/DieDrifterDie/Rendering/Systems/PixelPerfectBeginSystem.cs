using Friflo.Engine.ECS.Systems;
using Raylib_cs;

namespace DieDrifterDie.Presentation.Rendering.Systems;

/// <summary>
/// Begins rendering to the pixel-perfect render texture.
/// Must run before any world-space rendering systems.
/// Automatically updates render texture size when window is resized.
/// </summary>
public sealed class PixelPerfectBeginSystem : BaseSystem
{
    private readonly PixelPerfectRenderer _renderer;
    private int _lastScreenWidth;
    private int _lastScreenHeight;

    public PixelPerfectBeginSystem(PixelPerfectRenderer renderer)
    {
        _renderer = renderer;
    }

    protected override void OnUpdateGroup()
    {
        // Check for window resize and update render texture aspect ratio
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();
        if (screenWidth != _lastScreenWidth || screenHeight != _lastScreenHeight)
        {
            _lastScreenWidth = screenWidth;
            _lastScreenHeight = screenHeight;
            _renderer.UpdateScreenSize();
        }

        _renderer.BeginTextureMode();
    }
}
