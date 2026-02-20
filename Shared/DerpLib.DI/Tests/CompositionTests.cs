// Basic composition tests
#nullable enable
using System;
using Xunit;

namespace DerpLib.DI.Tests
{
    public class CompositionTests
    {
        [Fact]
        public void AppComposition_CanBeCreated()
        {
            var config = new NetworkConfig { Host = "test", Port = 1234 };
            using var app = new AppComposition(config);
            Assert.NotNull(app);
        }

        [Fact]
        public void AppComposition_ResolvesStore()
        {
            var config = new NetworkConfig { Host = "test", Port = 1234 };
            using var app = new AppComposition(config);
            var store = app.Store;
            Assert.NotNull(store);
        }

        [Fact]
        public void GameComposition_CanBeCreatedViaFactory()
        {
            var config = new NetworkConfig { Host = "test", Port = 1234 };
            using var app = new AppComposition(config);
            var store = app.Store;

            var gameConfig = new GameConfig { Name = "Test" };
            var seed = new SessionSeed { Value = 42 };
            var game = store.CreateGame(gameConfig, seed);

            Assert.NotNull(game);
            Assert.NotNull(game.SimWorld);
            Assert.NotNull(game.Network);
            Assert.Equal(config.Host, game.Network.Config.Host);
        }

        [Fact]
        public void GameComposition_InheritsDependencies()
        {
            var config = new NetworkConfig { Host = "test", Port = 1234 };
            using var app = new AppComposition(config);
            var store = app.Store;

            var parentNetwork = app.Resolve_NetworkService();
            var parentLogger = app.Resolve_ILogger();
            var parentDisposableService = app.Resolve_ParentDisposableService();

            var gameConfig = new GameConfig { Name = "Test" };
            var seed = new SessionSeed { Value = 42 };
            var game = store.CreateGame(gameConfig, seed);

            Assert.Same(parentNetwork, game.Network);
            Assert.Same(parentLogger, game.Logger);
            Assert.Same(parentDisposableService, game.ParentDisposableService);
        }

        [Fact]
        public void Disposing_GameComposition_DoesNotDisposeParent()
        {
            var config = new NetworkConfig { Host = "test", Port = 1234 };
            using var app = new AppComposition(config);
            var store = app.Store;
            var parentDisposableService = app.Resolve_ParentDisposableService();

            var gameConfig = new GameConfig { Name = "Test" };
            var seed = new SessionSeed { Value = 42 };
            var game1 = store.CreateGame(gameConfig, seed);
            store.DisposeGame();

            Assert.False(parentDisposableService.IsDisposed);

            // Can still create another game
            var game2 = store.CreateGame(gameConfig, seed);
            Assert.NotNull(game2);
            Assert.False(parentDisposableService.IsDisposed);
        }

        [Fact]
        public void TransientOnlyComposition_CanResolveAndDispose()
        {
            using var composition = new TransientOnlyComposition();
            var service1 = composition.Service;
            var service2 = composition.Service;

            Assert.NotNull(service1);
            Assert.NotNull(service2);
            Assert.NotSame(service1, service2);
        }

        [Fact]
        public void FactoryBlock_InjectFromParent_Works()
        {
            using var app = new DecoratorAppComposition();
            var store = app.Store;

            var logger = store.CreateLogger();
            var scoped = Assert.IsType<ScopedLogger>(logger);
            Assert.IsType<ConsoleLogger>(scoped.Parent);
            Assert.Equal("Game", scoped.ScopeName);
        }

        [Fact]
        public void FactoryBlock_TaggedInject_Works()
        {
            using var composition = new TaggedComposition();
            var consumer = composition.Consumer;

            Assert.NotNull(consumer);
            Assert.IsType<FileLogger>(consumer.Logger);
        }

        [Fact]
        public void FactoryExpression_StaticProperty_Works()
        {
            using var composition = new StaticPropertyFactoryComposition();
            Assert.Equal(Environment.NewLine, composition.NewLine);
        }

        [Fact]
        public void CollectionBinding_ResolvesAllPlugins()
        {
            using var composition = new CollectionComposition();
            var host = composition.Host;

            Assert.NotNull(host);
            Assert.Equal(3, host.Plugins.Length);
            Assert.Equal("A", host.Plugins[0].Name);
            Assert.Equal("B", host.Plugins[1].Name);
            Assert.Equal("C", host.Plugins[2].Name);
        }

        [Fact]
        public void SelfBinding_ResolvesWithoutTo()
        {
            using var composition = new SelfBindingComposition();
            var service = composition.Service;

            Assert.NotNull(service);
            Assert.IsType<ConsoleLogger>(service.Logger);
        }
    }
}
