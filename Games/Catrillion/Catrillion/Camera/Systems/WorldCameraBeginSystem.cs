using System;
using System.Numerics;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;

namespace Catrillion.Camera.Systems;

public sealed class WorldCameraBeginSystem : BaseSystem
{
    private readonly CameraManager _cameraManager;

    public WorldCameraBeginSystem(CameraManager cameraManager)
    {
        _cameraManager = cameraManager;
    }

    protected override void OnUpdateGroup()
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Snap camera position to pixel boundaries for crisp rendering
        float targetX = MathF.Floor(_cameraManager.TargetX);
        float targetY = MathF.Floor(_cameraManager.TargetY);

        var camera = new Camera2D
        {
            Target = new Vector2(targetX, targetY),
            Offset = new Vector2(screenWidth * 0.5f, screenHeight * 0.5f),
            Rotation = 0f,
            Zoom = _cameraManager.Zoom
        };
        Raylib.BeginMode2D(camera);
    }
}
