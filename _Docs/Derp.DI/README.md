# Derp.DI - Source-Generated DI with Nested Compositions

A Pure.DI-inspired dependency injection framework with first-class support for nested/child compositions via auto-generated factories.

## Why Not Pure.DI?

Pure.DI is excellent for flat composition graphs but lacks nested composition support. When you have hierarchical scopes (App → Game → Match), you must manually plumb every shared dependency:

```csharp
// Current pain: GameStore must know about EVERY service GameComposition needs
_composition = new GameComposition(
    config, _networkService, replayConfig, sessionSeed,
    _eventBus, _logger, _inputStore, _gameplayStore, _networkStore  // 10+ manual args
);
```

Adding a new shared service requires changes in 3+ places.

## Derp.DI Solution

Declare a child scope, get an auto-generated factory:

```csharp
// AppComposition declares it can create GameComposition scopes
.Scope<GameComposition>()

// GameStore just injects the factory
public class GameStore
{
    private readonly GameComposition.Factory _gameFactory;

    public GameStore(GameComposition.Factory gameFactory)
    {
        _gameFactory = gameFactory;
    }

    public void CreateGame(PlayerConfig config, SessionSeed seed)
    {
        _gameScope = _gameFactory.Create(config, seed);  // Clean!
    }
}
```

---

## Core Design Principles

1. **Source-generated** - No runtime reflection, compile-time verification
2. **Hierarchical resolution** - Child scopes inherit parent bindings automatically
3. **Generated factories** - `TChild.Factory` class with typed `Create()` method
4. **Scoped disposal** - Child disposes its singletons, never touches parent
5. **Zero runtime allocation** - After initialization, resolution is just field access

---

## API Design

### Root Composition

```csharp
using Derp.DI;

[Composition]
partial class AppComposition
{
    static void Setup() => DI.Setup()
        // Runtime arguments (provided at construction)
        .Arg<NetworkConfig>("networkConfig")

        // Singleton bindings
        .Bind<ILogger>().As(Singleton).To(_ => Log.Logger)
        .Bind<NetworkService>().As(Singleton).To<NetworkService>()
        .Bind<AppStore>().As(Singleton).To<AppStore>()
        .Bind<GameStore>().As(Singleton).To<GameStore>()

        // Child scope declaration - generates GameComposition.Factory
        .Scope<GameComposition>()

        // Composition root
        .Root<IGameRunner>("GameRunner");
}
```

### Child Composition

```csharp
[Composition]
partial class GameComposition
{
    static void Setup() => DI.Setup()
        // Child-specific arguments (become Factory.Create parameters)
        .Arg<PlayerConfig>("playerConfig")
        .Arg<SessionSeed>("sessionSeed")

        // Child-specific bindings
        .Bind<SimWorld>().As(Singleton).To<SimWorld>()
        .Bind<EntityStore>().As(Singleton).To<EntityStore>()
        .Bind<Game>().As(Singleton).To<Game>()

        // ILogger, NetworkService, etc. - inherited from parent automatically

        .Root<Game>("Game");
}
```

### Generated Factory

The generator produces a nested `Factory` class:

```csharp
// Auto-generated: GameComposition.Factory.g.cs
partial class GameComposition
{
    public sealed class Factory
    {
        private readonly AppComposition _parent;

        public Factory(AppComposition parent) => _parent = parent;

        /// <summary>Creates a new GameComposition scope.</summary>
        /// <param name="playerConfig">Player configuration for this game session.</param>
        /// <param name="sessionSeed">Random seed for deterministic simulation.</param>
        public GameComposition Create(PlayerConfig playerConfig, SessionSeed sessionSeed)
            => new GameComposition(_parent, playerConfig, sessionSeed);
    }
}
```

The factory is auto-registered in the parent composition:

```csharp
// Auto-generated in AppComposition.g.cs
internal GameComposition.Factory Resolve_GameComposition_Factory()
{
    if (_singleton_GameComposition_Factory is { } cached) return cached;
    lock (_lock)
    {
        return _singleton_GameComposition_Factory ??= new GameComposition.Factory(this);
    }
}
```

### Usage

```csharp
public class GameStore
{
    private readonly GameComposition.Factory _gameFactory;
    private GameComposition? _gameScope;
    private Game? _currentGame;

    public GameStore(GameComposition.Factory gameFactory)
    {
        _gameFactory = gameFactory;
    }

    public Game? CurrentGame => _currentGame;

    public void CreateGame(PlayerConfig config, SessionSeed seed)
    {
        DisposeGame();
        _gameScope = _gameFactory.Create(config, seed);
        _currentGame = _gameScope.Game;
    }

    public void DisposeGame()
    {
        _currentGame = null;
        _gameScope?.Dispose();  // Disposes Game, SimWorld - NOT parent singletons
        _gameScope = null;
    }
}
```

---

## Resolution Rules

### Binding Lookup Order

When `GameComposition` resolves a type:

1. **Local binding** - If bound in `GameComposition`, use it
2. **Parent binding** - Delegate to `AppComposition`
3. **Grandparent** - Continue up the chain
4. **Compile error** - If not found anywhere

### Argument Inheritance

Child compositions inherit parent's `.Arg<>()` bindings:

```csharp
// AppComposition
.Arg<NetworkConfig>("networkConfig")

// GameComposition can inject NetworkConfig without re-declaring it
.Bind<Game>().As(Singleton).To(ctx => {
    ctx.Inject(out NetworkConfig config);  // Resolved from parent's arg
    return new Game(config);
})
```

### Override Semantics

When a child binds the same type as parent:

```csharp
// AppComposition
.Bind<ILogger>().As(Singleton).To(_ => Log.Logger)

// GameComposition - override with decorated version
.Bind<ILogger>().As(Singleton).To(ctx => {
    ctx.InjectFromParent(out ILogger parentLogger);  // Explicit parent access
    return new ScopedLogger(parentLogger, "Game");
})
```

- Child's binding wins for all resolution within child scope
- Use `ctx.InjectFromParent<T>()` to explicitly access parent's binding (decorator pattern)

---

## Multi-Level Nesting

Scopes can be nested arbitrarily deep:

```csharp
[Composition]
partial class AppComposition
{
    static void Setup() => DI.Setup()
        .Bind<ILogger>().As(Singleton).To(_ => Log.Logger)
        .Scope<GameComposition>()
        .Root<IGameRunner>("GameRunner");
}

[Composition]
partial class GameComposition
{
    static void Setup() => DI.Setup()
        .Arg<PlayerConfig>("playerConfig")
        .Bind<SimWorld>().As(Singleton).To<SimWorld>()
        .Scope<MatchComposition>()  // Game can create Match scopes
        .Root<Game>("Game");
}

[Composition]
partial class MatchComposition
{
    static void Setup() => DI.Setup()
        .Arg<MatchConfig>("matchConfig")
        .Bind<MatchState>().As(Singleton).To<MatchState>()
        .Root<Match>("Match");
}
```

**Usage:**
```csharp
public class Game
{
    private readonly MatchComposition.Factory _matchFactory;

    public Game(MatchComposition.Factory matchFactory)
    {
        _matchFactory = matchFactory;
    }

    public Match StartMatch(MatchConfig config)
    {
        var matchScope = _matchFactory.Create(config);
        return matchScope.Match;
    }
}
```

**Resolution chain:** Match → Game → App

---

## Advanced Features

### Tagged Bindings

```csharp
// Multiple bindings of same type
.Bind<ILogger>("Console").As(Singleton).To<ConsoleLogger>()
.Bind<ILogger>("File").As(Singleton).To<FileLogger>()

// Inject specific one
.Bind<Game>().As(Singleton).To(ctx => {
    ctx.Inject<ILogger>("File", out var logger);
    return new Game(logger);
})
```

### Transient Lifetime

```csharp
.Bind<ICommand>().As(Transient).To<MoveCommand>()
```

Creates new instance per resolution. No singleton caching.

### Conditional Bindings

```csharp
.Bind<IRenderer>().As(Singleton).When(ctx => ctx.IsDebug).To<DebugRenderer>()
.Bind<IRenderer>().As(Singleton).To<ProductionRenderer>()  // Default
```

### Lazy Resolution

```csharp
public class ExpensiveConsumer
{
    private readonly Lazy<ExpensiveService> _service;

    public ExpensiveConsumer(Lazy<ExpensiveService> service)
    {
        _service = service;  // Not created yet
    }

    public void DoWork()
    {
        _service.Value.Execute();  // Created on first access
    }
}
```

Generator auto-binds `Lazy<T>` for any bound `T`.

### Factory Delegates

```csharp
// Auto-generated for any binding with args
public class EnemySpawner
{
    private readonly Func<EnemyType, Vector2, Enemy> _createEnemy;

    public EnemySpawner(Func<EnemyType, Vector2, Enemy> createEnemy)
    {
        _createEnemy = createEnemy;
    }

    public Enemy Spawn(EnemyType type, Vector2 pos) => _createEnemy(type, pos);
}
```

---

## Disposal

### Scoped Disposal Model

```
AppComposition.Dispose()
├── Disposes: AppStore, NetworkService, GameStore
├── Disposes: GameComposition.Factory
└── Does NOT auto-dispose active GameComposition scopes (caller's responsibility)

GameComposition.Dispose()
├── Disposes: Game, SimWorld, EntityStore (child's singletons)
└── Does NOT dispose: ILogger, NetworkService (parent's singletons)
```

### Disposal Order

Singletons are disposed in reverse creation order (LIFO):

```csharp
// If created in order: A → B → C
// Disposed in order: C → B → A
```

### Exception Handling

```csharp
partial class GameComposition
{
    // Override to handle disposal exceptions
    partial void OnDisposeException(IDisposable instance, Exception exception)
    {
        Log.Error(exception, "Failed to dispose {Type}", instance.GetType().Name);
    }
}
```

---

## Migration from Pure.DI

### Before (Pure.DI)

```csharp
// GameStore.cs - manual plumbing nightmare
public class GameStore
{
    private readonly ILogger _logger;
    private readonly NetworkService _networkService;
    private readonly AppEventBus _eventBus;
    private readonly InputStore _inputStore;
    private readonly GameplayStore _gameplayStore;
    private readonly NetworkStore _networkStore;
    // ... 10+ fields

    public GameStore(
        ILogger logger,
        NetworkService networkService,
        AppEventBus eventBus,
        InputStore inputStore,
        GameplayStore gameplayStore,
        NetworkStore networkStore
        // ... 10+ constructor params just to forward to GameComposition
    ) { ... }

    public void CreateGame(PlayerConfig config, SessionSeed seed)
    {
        _composition = new GameComposition(
            config, _networkService, seed, _eventBus,
            _logger, _inputStore, _gameplayStore, _networkStore
        );
    }
}
```

### After (Derp.DI)

```csharp
// GameStore.cs - clean!
public class GameStore
{
    private readonly GameComposition.Factory _gameFactory;

    public GameStore(GameComposition.Factory gameFactory)
    {
        _gameFactory = gameFactory;
    }

    public void CreateGame(PlayerConfig config, SessionSeed seed)
    {
        _gameScope = _gameFactory.Create(config, seed);  // That's it!
    }
}
```

**Adding a new shared service:**

| Pure.DI | Derp.DI |
|---------|---------|
| 1. Add to GameStore constructor | (nothing) |
| 2. Add field to GameStore | (nothing) |
| 3. Pass to GameComposition | (nothing) |
| 4. Add .Arg<> in GameComposition | (nothing - inherited) |

With Derp.DI, you just add the binding in `AppComposition` and it's automatically available to all child scopes.

---

## Comparison

| Feature | Pure.DI | Derp.DI |
|---------|---------|---------|
| Source-generated | ✅ | ✅ |
| Compile-time verification | ✅ | ✅ |
| Zero reflection | ✅ | ✅ |
| Singleton lifetime | ✅ | ✅ |
| Transient lifetime | ✅ | ✅ |
| Runtime args | ✅ | ✅ |
| Tagged bindings | ✅ | ✅ |
| **Nested scopes** | ❌ | ✅ |
| **Generated factories** | ❌ | ✅ |
| **Inherited bindings** | ❌ | ✅ |
| **Scoped disposal** | ❌ | ✅ |
| **Override parent bindings** | ❌ | ✅ |

---

## Project Structure

```
Derp.DI/
├── src/
│   ├── Derp.DI/                      # Runtime library (minimal)
│   │   ├── Attributes.cs             # [Composition], etc.
│   │   ├── Lifetime.cs               # Singleton, Transient enum
│   │   └── DI.cs                     # Fluent API (compile-time only)
│   │
│   └── Derp.DI.Generator/            # Source generator
│       ├── CompositionGenerator.cs   # Main incremental generator
│       ├── Parser/
│       │   ├── CompositionParser.cs  # Parses [Composition] classes
│       │   ├── BindingParser.cs      # Parses .Bind() chains
│       │   └── ScopeParser.cs        # Parses .Scope<>() declarations
│       ├── Model/
│       │   ├── CompositionModel.cs   # Intermediate representation
│       │   ├── BindingModel.cs
│       │   └── ScopeModel.cs
│       ├── Resolution/
│       │   ├── DependencyResolver.cs # Constructor analysis
│       │   └── ScopeChainBuilder.cs  # Parent-child linking
│       ├── Emit/
│       │   ├── CompositionEmitter.cs # Generates composition class
│       │   ├── FactoryEmitter.cs     # Generates .Factory nested class
│       │   └── ResolverEmitter.cs    # Generates Resolve_T methods
│       └── Diagnostics/
│           └── DiagnosticDescriptors.cs
│
└── tests/
    ├── Derp.DI.Tests/                # Unit tests
    └── Derp.DI.Generator.Tests/      # Generator snapshot tests
```

---

## Implementation Phases

### Phase 1: Core Generator
- `[Composition]` attribute parsing
- `.Bind<T>().As(Singleton).To<TImpl>()` support
- `.Arg<T>()` support
- `.Root<T>()` support
- Constructor dependency analysis
- Code emission for flat compositions
- Disposal tracking

### Phase 2: Nested Scopes
- `.Scope<TChild>()` parsing
- `TChild.Factory` generation
- Auto-registration of factory in parent
- Hierarchical resolution (child → parent delegation)
- Scoped disposal (child only disposes its own)

### Phase 3: Advanced Features
- Tagged bindings
- Transient lifetime
- `ctx.InjectFromParent<T>()` for decorators
- `Lazy<T>` auto-binding
- `Func<TArgs, T>` factory delegates

### Phase 4: Diagnostics & Polish
- Compile-time error messages with fix suggestions
- Circular dependency detection
- Unused binding warnings
- IDE completion hints
