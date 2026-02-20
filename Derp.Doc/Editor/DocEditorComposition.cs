using DerpLib.DI;
using static DerpLib.DI.DI;

namespace Derp.Doc.Editor;

[Composition]
internal partial class DocEditorComposition
{
    static void Setup() => DI.Setup()
        .Bind<DocWorkspace>().As(Singleton).To<DocWorkspace>()
        .Bind<DocEditorApp>().As(Singleton).To<DocEditorApp>()
        .Root<DocEditorApp>("Editor");
}
