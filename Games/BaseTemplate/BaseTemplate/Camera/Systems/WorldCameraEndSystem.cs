using Friflo.Engine.ECS.Systems;
using Raylib_cs;

namespace BaseTemplate.Presentation.Camera.Systems;

public sealed class WorldCameraEndSystem : BaseSystem
{
    protected override void OnUpdateGroup()
    {
        Raylib.EndMode2D();
    }
}

