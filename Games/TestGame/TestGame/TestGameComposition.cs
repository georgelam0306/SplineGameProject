using DerpDocDatabase;
using DerpLib.DI;
using static DerpLib.DI.DI;

namespace TestGame;

[Composition]
internal partial class TestGameComposition
{
    static void Setup() => DI.Setup()
        .Bind<GameDatabase>().As(Singleton).To<GameDatabase>()
        .Bind<TestGameApp>().As(Singleton).To<TestGameApp>()
        .Root<TestGameApp>("App");
}
