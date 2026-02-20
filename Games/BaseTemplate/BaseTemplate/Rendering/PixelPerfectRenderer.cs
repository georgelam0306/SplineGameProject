using System;
using System.Numerics;
using Raylib_cs;

namespace BaseTemplate.Presentation.Rendering;

/// <summary>
/// Manages pixel-perfect rendering by drawing to a fixed-resolution render texture,
/// then scaling to the screen. This ensures all sprites are rendered at integer pixel
/// positions, eliminating aliasing artifacts from non-integer zoom levels.
/// </summary>
public sealed class PixelPerfectRenderer : IDisposable
{
    private RenderTexture2D _renderTexture;
    private int _gameWidth;
    private int _gameHeight;
    private readonly int _baseWidth;
    private readonly int _baseHeight;
    private float _currentZoom = 1f;
    private bool _isDisposed;

    // Zoom limits to prevent extremely large/small textures
    private const float MinZoom = 0.25f;  // Max texture size = 4x base
    private const float MaxZoom = 4f;     // Min texture size = 0.25x base
    private const int MinTextureSize = 64;
    private const int MaxTextureSize = 2048;

    /// <summary>
    /// The current game resolution width (render texture width, changes with zoom).
    /// </summary>
    public int GameWidth => _gameWidth;

    /// <summary>
    /// The current game resolution height (render texture height, changes with zoom).
    /// </summary>
    public int GameHeight => _gameHeight;

    /// <summary>
    /// The base resolution width at zoom 1.0.
    /// </summary>
    public int BaseWidth => _baseWidth;

    /// <summary>
    /// The base resolution height at zoom 1.0.
    /// </summary>
    public int BaseHeight => _baseHeight;

    public PixelPerfectRenderer(int baseWidth, int baseHeight)
    {
        _baseWidth = baseWidth;
        _baseHeight = baseHeight;
        _gameWidth = baseWidth;
        _gameHeight = baseHeight;
        CreateRenderTexture();
    }

    /// <summary>
    /// Update the render texture size based on zoom level and screen aspect ratio.
    /// Zooming in (zoom > 1) = smaller texture = see less of world.
    /// Zooming out (zoom < 1) = larger texture = see more of world.
    /// The render texture aspect ratio always matches the screen to eliminate letterboxing.
    /// </summary>
    public void UpdateZoom(float zoom)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _currentZoom = zoom;

        // Get screen dimensions to match aspect ratio
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();
        float screenAspect = (float)screenWidth / screenHeight;

        // Height based on zoom (determines vertical world visibility)
        // Width calculated from screen aspect ratio to fill screen completely
        int newHeight = Math.Clamp((int)(_baseHeight / zoom), MinTextureSize, MaxTextureSize);
        int newWidth = (int)(newHeight * screenAspect);

        // If width exceeds max, clamp it and recalculate height to maintain aspect ratio
        if (newWidth > MaxTextureSize)
        {
            newWidth = MaxTextureSize;
            newHeight = (int)(newWidth / screenAspect);
        }
        else if (newWidth < MinTextureSize)
        {
            newWidth = MinTextureSize;
            newHeight = (int)(newWidth / screenAspect);
        }

        newHeight = Math.Clamp(newHeight, MinTextureSize, MaxTextureSize);

        Resize(newWidth, newHeight);
    }

    /// <summary>
    /// Update render texture to match current screen aspect ratio.
    /// Call this when the window is resized.
    /// </summary>
    public void UpdateScreenSize()
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();
        Console.WriteLine($"[PixelPerfect] UpdateScreenSize: screen={screenWidth}x{screenHeight}, texture={_gameWidth}x{_gameHeight}, zoom={_currentZoom}");

        // Re-apply current zoom with new screen dimensions
        float zoom = _currentZoom;
        _currentZoom = 0; // Force recalculation
        UpdateZoom(zoom);

        Console.WriteLine($"[PixelPerfect] After resize: texture={_gameWidth}x{_gameHeight}");
    }

    private void CreateRenderTexture()
    {
        _renderTexture = Raylib.LoadRenderTexture(_gameWidth, _gameHeight);
        Raylib.SetTextureFilter(_renderTexture.Texture, TextureFilter.Point);
    }

    /// <summary>
    /// Resize the render texture to a new resolution.
    /// </summary>
    public void Resize(int gameWidth, int gameHeight)
    {
        if (_gameWidth == gameWidth && _gameHeight == gameHeight)
            return;

        Raylib.UnloadRenderTexture(_renderTexture);
        _gameWidth = gameWidth;
        _gameHeight = gameHeight;
        CreateRenderTexture();
    }

    /// <summary>
    /// Begin rendering to the pixel-perfect render texture.
    /// Call this before drawing world content.
    /// </summary>
    public void BeginTextureMode()
    {
        Raylib.BeginTextureMode(_renderTexture);
        Raylib.ClearBackground(Color.Black);
    }

    /// <summary>
    /// End rendering to the render texture.
    /// Call this after drawing all world content.
    /// </summary>
    public void EndTextureMode()
    {
        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Draw the render texture scaled to fill the entire screen.
    /// Since render texture aspect ratio matches screen, this fills completely with no distortion.
    /// </summary>
    public void DrawToScreen(int screenWidth, int screenHeight)
    {
        // Source rectangle (flip Y because render textures are upside down in OpenGL)
        var sourceRect = new Rectangle(0, 0, _gameWidth, -_gameHeight);
        // Destination fills entire screen - aspect ratios match so no distortion
        var destRect = new Rectangle(0, 0, screenWidth, screenHeight);

        Raylib.DrawTexturePro(_renderTexture.Texture, sourceRect, destRect, Vector2.Zero, 0f, Color.White);
    }

    /// <summary>
    /// Convert screen coordinates to render texture coordinates.
    /// Returns coordinates in the native game resolution space.
    /// </summary>
    public Vector2 ScreenToTexture(Vector2 screenPos)
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Direct scale conversion - render texture fills entire screen
        float textureX = screenPos.X * _gameWidth / screenWidth;
        float textureY = screenPos.Y * _gameHeight / screenHeight;

        return new Vector2(textureX, textureY);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Raylib.UnloadRenderTexture(_renderTexture);
    }
}
