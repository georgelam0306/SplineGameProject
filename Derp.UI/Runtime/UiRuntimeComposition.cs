using Pure.DI;
using static Pure.DI.Lifetime;

namespace Derp.UI;

internal partial class UiRuntimeComposition
{
    static void Setup() => DI.Setup(nameof(UiRuntimeComposition))
        .Bind().As(Singleton).To<UiWorld>()
        .Bind().As(Singleton).To<UiRuntimeHost>()
        .Root<UiRuntimeHost>("Runtime");
}
