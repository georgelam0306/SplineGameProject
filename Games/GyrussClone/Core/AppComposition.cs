using DerpLib.DI;
using static DerpLib.DI.DI;

namespace GyrussClone.Core;

[Composition]
partial class AppComposition
{
    static void Setup() => DI.Setup()
        .Bind<GameManager>().As(Singleton).To<GameManager>()
        .Scope<GameComposition>()
        .Root<GameManager>("GameManager");
}
