// Test compositions for DI testing
#nullable enable
using System;
using DerpLib.DI;
using static DerpLib.DI.DI;

namespace DerpLib.DI.Tests
{
    [Composition]
    public partial class AppComposition
    {
        static void Setup() => DI.Setup()
            .Arg<NetworkConfig>("networkConfig")
            .Bind<ILogger>().As(Singleton).To<ConsoleLogger>()
            .Bind<NetworkService>().As(Singleton).To<NetworkService>()
            .Bind<ParentDisposableService>().As(Singleton).To<ParentDisposableService>()
            .Bind<GameStore>().As(Singleton).To<GameStore>()
            .Scope<GameComposition>()
            .Root<GameStore>("Store");
    }

    [Composition]
    public partial class GameComposition
    {
        static void Setup() => DI.Setup()
            .Arg<GameConfig>("gameConfig")
            .Arg<SessionSeed>("sessionSeed")
            .Bind<SimWorld>().As(Singleton).To<SimWorld>()
            .Bind<Game>().As(Singleton).To<Game>()
            .Root<Game>("Game");
    }

    [Composition]
    public partial class TransientOnlyComposition
    {
        static void Setup() => DI.Setup()
            .Bind<TransientDisposableService>().As(Transient).To<TransientDisposableService>()
            .Root<TransientDisposableService>("Service");
    }

    [Composition]
    public partial class DecoratorAppComposition
    {
        static void Setup() => DI.Setup()
            .Bind<ILogger>().As(Singleton).To<ConsoleLogger>()
            .Bind<DecoratorGameStore>().As(Singleton).To<DecoratorGameStore>()
            .Scope<DecoratorGameComposition>()
            .Root<DecoratorGameStore>("Store");
    }

    [Composition]
    public partial class DecoratorGameComposition
    {
        static void Setup() => DI.Setup()
            .Bind<ILogger>().As(Singleton).To(ctx =>
            {
                ctx.InjectFromParent(out ILogger parentLogger);
                return new ScopedLogger(parentLogger, "Game");
            })
            .Root<ILogger>("Logger");
    }

    [Composition]
    public partial class TaggedComposition
    {
        static void Setup() => DI.Setup()
            .Bind<ILogger>("Console").As(Singleton).To<ConsoleLogger>()
            .Bind<ILogger>("File").As(Singleton).To<FileLogger>()
            .Bind<TaggedConsumer>().As(Singleton).To(ctx =>
            {
                ctx.Inject<ILogger>("File", out var logger);
                return new TaggedConsumer(logger);
            })
            .Root<TaggedConsumer>("Consumer");
    }

    [Composition]
    public partial class StaticPropertyFactoryComposition
    {
        static void Setup() => DI.Setup()
            .Bind<string>().As(Singleton).To(_ => Environment.NewLine)
            .Root<string>("NewLine");
    }

    [Composition]
    public partial class CollectionComposition
    {
        static void Setup() => DI.Setup()
            .BindAll<IPlugin>().As(Singleton)
                .Add<PluginA>()
                .Add<PluginB>()
                .Add<PluginC>()
            .Bind<PluginHost>().As(Singleton)
            .Root<PluginHost>("Host");
    }

    [Composition]
    public partial class SelfBindingComposition
    {
        static void Setup() => DI.Setup()
            .Bind<ILogger>().As(Singleton).To<ConsoleLogger>()
            .Bind<SelfBoundService>().As(Singleton)
            .Root<SelfBoundService>("Service");
    }
}
