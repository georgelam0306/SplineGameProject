using System.Diagnostics;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Pure.DI;
using BaseTemplate.Presentation.Camera;
using BaseTemplate.Presentation.Camera.Systems;
using BaseTemplate.Presentation.Input;
using BaseTemplate.Presentation.Rendering;
using BaseTemplate.Presentation.Rendering.Systems;
using BaseTemplate.Presentation.UI;
using static Pure.DI.Lifetime;

namespace BaseTemplate;

partial class GameComposition
{
    [Conditional("DI")]
    static void SetupSystems() => DI.Setup()
        // Input and camera managers
        .Bind().As(Singleton).To<CameraManager>()
        .Bind().As(Singleton).To<UIInputManager>()
        .Bind().As(Singleton).To<InputActionBuffer>()
        .Bind().As(Singleton).To<GameInputManager>()
        // Render interpolation state (for simulation-render decoupling)
        .Bind().As(Singleton).To<RenderInterpolation>()
        // Update systems
        .Bind().As(Singleton).To<CameraPanSystem>()
        // Render systems
        .Bind().As(Singleton).To<RenderInterpolationSystem>()
        .Bind().As(Singleton).To<WorldCameraBeginSystem>()
        .Bind().As(Singleton).To<SpriteRenderSystem>()
        .Bind().As(Singleton).To<WorldCameraEndSystem>()
        .Bind().As(Singleton).To<DebugMenuUI>()
        // SystemRoots
        .Bind<SystemRoot>("UpdateRoot").As(Singleton).To(ctx =>
        {
            ctx.Inject(out EntityStore store);
            ctx.Inject(out CameraPanSystem cameraPanSystem);

            var root = new SystemRoot(store)
            {
                cameraPanSystem,
            };
            root.SetMonitorPerf(true);
            return root;
        })
        .Bind<SystemRoot>("RenderRoot").As(Singleton).To(ctx =>
        {
            ctx.Inject(out EntityStore store);
            ctx.Inject(out RenderInterpolationSystem renderInterpolationSystem);
            ctx.Inject(out WorldCameraBeginSystem worldCameraBeginSystem);
            ctx.Inject(out SpriteRenderSystem spriteRenderSystem);
            ctx.Inject(out WorldCameraEndSystem worldCameraEndSystem);
            ctx.Inject(out DebugMenuUI debugMenuUI);

            var root = new SystemRoot(store)
            {
                renderInterpolationSystem, // FIRST - interpolate positions before any rendering
                worldCameraBeginSystem,
                spriteRenderSystem,
                worldCameraEndSystem,
                debugMenuUI,  // Screen space - Debug menu overlay
            };
            root.SetMonitorPerf(true);
            return root;
        });
}
