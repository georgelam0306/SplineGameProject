using Pure.DI;
using static Pure.DI.Lifetime;

namespace Derp.UI;

internal partial class UiEditorComposition
{
    static void Setup() => DI.Setup(nameof(UiEditorComposition))
        .Bind().As(Singleton).To<UiWorld>()
        .Bind().As(Singleton).To<UiWorkspace>()
        .Bind().As(Singleton).To<CanvasSurface>()
        .Bind().As(Singleton).To<EditorApp>()
        .Root<EditorApp>("Editor");
}
