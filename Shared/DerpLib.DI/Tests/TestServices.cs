// Test services for DI testing
#nullable enable
using System;

namespace DerpLib.DI.Tests
{
    public interface ILogger
    {
        void Log(string message);
    }

    public class ConsoleLogger : ILogger
    {
        public void Log(string message) => Console.WriteLine(message);
    }

    public class NetworkConfig
    {
        public string Host { get; init; } = "localhost";
        public int Port { get; init; } = 8080;
    }

    public class NetworkService : IDisposable
    {
        public NetworkConfig Config { get; }
        public ILogger Logger { get; }

        public NetworkService(NetworkConfig config, ILogger logger)
        {
            Config = config;
            Logger = logger;
        }

        public void Dispose() { }
    }

    public sealed class ParentDisposableService : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    public class GameConfig
    {
        public string Name { get; init; } = "Test Game";
    }

    public class SessionSeed
    {
        public int Value { get; init; }
    }

    public class SimWorld : IDisposable
    {
        public void Dispose() { }
    }

    public class Game : IDisposable
    {
        public SimWorld SimWorld { get; }
        public NetworkService Network { get; }
        public ILogger Logger { get; }
        public ParentDisposableService ParentDisposableService { get; }
        public GameConfig Config { get; }
        public SessionSeed Seed { get; }

        public Game(
            SimWorld simWorld,
            NetworkService network,
            ILogger logger,
            ParentDisposableService parentDisposableService,
            GameConfig config,
            SessionSeed seed)
        {
            SimWorld = simWorld;
            Network = network;
            Logger = logger;
            ParentDisposableService = parentDisposableService;
            Config = config;
            Seed = seed;
        }

        public void Dispose() { }
    }

    public class GameStore
    {
        private GameComposition.Factory _gameFactory;
        private GameComposition? _gameScope;

        public GameStore(GameComposition.Factory gameFactory)
        {
            _gameFactory = gameFactory;
        }

        public Game? CurrentGame => _gameScope?.Game;

        public Game CreateGame(GameConfig config, SessionSeed seed)
        {
            _gameScope?.Dispose();
            _gameScope = _gameFactory.Create(config, seed);
            return _gameScope.Game;
        }

        public void DisposeGame()
        {
            _gameScope?.Dispose();
            _gameScope = null;
        }
    }

    public sealed class TransientDisposableService : IDisposable
    {
        public void Dispose() { }
    }

    public sealed class ScopedLogger : ILogger
    {
        public ILogger Parent { get; }
        public string ScopeName { get; }

        public ScopedLogger(ILogger parent, string scopeName)
        {
            Parent = parent;
            ScopeName = scopeName;
        }

        public void Log(string message)
        {
            Parent.Log($"[{ScopeName}] {message}");
        }
    }

    public sealed class DecoratorGameStore
    {
        private readonly DecoratorGameComposition.Factory _gameFactory;
        private DecoratorGameComposition? _gameScope;

        public DecoratorGameStore(DecoratorGameComposition.Factory gameFactory)
        {
            _gameFactory = gameFactory;
        }

        public ILogger CreateLogger()
        {
            _gameScope?.Dispose();
            _gameScope = _gameFactory.Create();
            return _gameScope.Logger;
        }

        public void DisposeGame()
        {
            _gameScope?.Dispose();
            _gameScope = null;
        }
    }

    public sealed class FileLogger : ILogger
    {
        public void Log(string message) => Console.WriteLine($"FILE:{message}");
    }

    public sealed class TaggedConsumer
    {
        public ILogger Logger { get; }

        public TaggedConsumer(ILogger logger)
        {
            Logger = logger;
        }
    }

    public interface IPlugin
    {
        string Name { get; }
    }

    public class PluginA : IPlugin
    {
        public string Name => "A";
    }

    public class PluginB : IPlugin
    {
        public string Name => "B";
    }

    public class PluginC : IPlugin
    {
        public string Name => "C";
    }

    public class PluginHost
    {
        public IPlugin[] Plugins { get; }

        public PluginHost(IPlugin[] plugins)
        {
            Plugins = plugins;
        }
    }

    public class SelfBoundService
    {
        public ILogger Logger { get; }

        public SelfBoundService(ILogger logger)
        {
            Logger = logger;
        }
    }
}
