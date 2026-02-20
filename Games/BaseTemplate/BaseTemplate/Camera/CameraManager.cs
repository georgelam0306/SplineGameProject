using System;
using System.Numerics;
using BaseTemplate.GameApp.Config;
using Raylib_cs;

namespace BaseTemplate.Presentation.Camera;

public enum CameraMode
{
    Follow,      // Camera follows the player
    MouseControl // Camera controlled by mouse (edge pan, zoom)
}

public class CameraManager
{
    // Map bounds from GameConfig
    private const float MinBound = 0f;
    private static readonly float MaxBoundX = GameConfig.Map.WidthTiles * GameConfig.Map.TileSize;
    private static readonly float MaxBoundY = GameConfig.Map.HeightTiles * GameConfig.Map.TileSize;

    // Zoom limits
    private const float MinZoom = 0.25f;
    private const float MaxZoom = 4f;

    public float TargetX { get; private set; }
    public float TargetY { get; private set; }
    public float Zoom { get; private set; } = 1f;
    public CameraMode Mode { get; private set; } = CameraMode.Follow;

    /// <summary>
    /// The visible game width in world units at current zoom.
    /// </summary>
    public int GameWidth => (int)(Raylib.GetScreenWidth() / Zoom);

    /// <summary>
    /// The visible game height in world units at current zoom.
    /// </summary>
    public int GameHeight => (int)(Raylib.GetScreenHeight() / Zoom);

    public void SetZoom(float zoom)
    {
        if (zoom > 0f)
        {
            Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        }
    }

    public void SetMode(CameraMode mode)
    {
        Mode = mode;
    }

    public void ToggleMode()
    {
        Mode = Mode == CameraMode.Follow ? CameraMode.MouseControl : CameraMode.Follow;
    }

    public void SetTarget(float x, float y)
    {
        TargetX = Math.Clamp(x, MinBound, MaxBoundX);
        TargetY = Math.Clamp(y, MinBound, MaxBoundY);
    }

    /// <summary>
    /// Converts screen coordinates to world coordinates.
    /// </summary>
    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Screen center is the camera offset
        float offsetX = screenWidth * 0.5f;
        float offsetY = screenHeight * 0.5f;

        // Convert screen coords to world coords accounting for zoom
        float worldX = (screenPos.X - offsetX) / Zoom + TargetX;
        float worldY = (screenPos.Y - offsetY) / Zoom + TargetY;

        return new Vector2(worldX, worldY);
    }
}
