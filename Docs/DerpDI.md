# DerpLib.DI — Dependency Injection

Source-generated DI container with compile-time resolution, zero reflection, and automatic `IDisposable` tracking.

## Project References

Add to your `.csproj`:
```xml
<ProjectReference Include="..\..\Shared\DerpLib.DI\Annotations\Derp.DI.Annotations.csproj" />
<ProjectReference Include="..\..\Shared\DerpLib.DI\Generator\Derp.DI.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Quick Start

```csharp
using DerpLib.DI;
using static DerpLib.DI.DI;

[Composition]
partial class AppComposition
{
    static void Setup() => DI.Setup()
        .Bind<ILogger>().As(Singleton).To<ConsoleLogger>()
        .Bind<GameService>().As(Singleton).To<GameService>()
        .Root<GameService>("Service");
}

// Usage
using var app = new AppComposition();
var service = app.Service;
```

The generator creates a `partial class` implementing `IDisposable` with:
- Constructor accepting `Arg<T>` parameters
- Root properties for each `.Root<T>(name)` call
- Thread-safe singleton resolution (double-checked lock)
- Automatic disposal tracking (LIFO order)

## API Reference

### Bindings

```csharp
// Constructor injection — resolves dependencies from constructor parameters
.Bind<IService>().As(Singleton).To<ServiceImpl>()

// Self-binding shorthand — when service type IS the implementation type
.Bind<GameService>().As(Singleton)  // equivalent to .To<GameService>()

// Factory lambda (block) — only ctx.Inject*() + a single return expression
.Bind<SimWorld>().As(Singleton).To(ctx =>
{
    ctx.Inject<int>(out var seed);
    return SimWorldFactory.Create(seed);
})

// Factory lambda (expression) — single expression, no ctx needed
.Bind<Pipeline>().As(Singleton).To(_ => PipelineFactory.Create())

// Transient — new instance every resolution (avoid in hot paths)
.Bind<IHandler>().As(Transient).To<Handler>()
```

### Arguments (`Arg<T>`)

Runtime values passed through the constructor:

```csharp
[Composition]
partial class GameComposition
{
    static void Setup() => DI.Setup()
        .Arg<int>("sessionSeed")
        .Bind<SimWorld>().As(Singleton).To(ctx =>
        {
            ctx.Inject<int>(out var seed);  // Resolves the Arg<int>
            return new SimWorld(seed);
        })
        .Root<SimWorld>("World");
}

// Generated constructor: GameComposition(int sessionSeed)
```

### Roots

Named entry points into the composition:

```csharp
.Root<GameService>("Service")
.Root<SimWorld>("World")

// Generated properties:
// public GameService Service => ...
// public SimWorld World => ...
```

### Tagged Bindings

Multiple implementations of the same interface:

```csharp
.Bind<ILogger>("Console").As(Singleton).To<ConsoleLogger>()
.Bind<ILogger>("File").As(Singleton).To<FileLogger>()
.Bind<Consumer>().As(Singleton).To(ctx =>
{
    ctx.Inject<ILogger>("File", out var logger);
    return new Consumer(logger);
})
```

### Collection Bindings (`BindAll<T>`)

Multiple implementations of the same interface, resolved as `T[]` via constructor injection:

```csharp
.BindAll<IEcsSystem<SimEcsWorld>>().As(Singleton)
    .Add<PlayerMovementSystem>()
    .Add<EnemySpawnSystem>()
    .Add<CollisionSystem>()
.Bind<EcsSystemPipeline<SimEcsWorld>>().As(Singleton)  // Takes IEcsSystem<SimEcsWorld>[] in ctor
```

The generator creates:
- Individual singleton resolvers for each `.Add<T>()` item
- An array resolver that composes them all, called when a constructor takes `T[]`

Order is preserved — items appear in the array in the same order as `.Add<>()` calls.

## Child Scopes

Scopes create isolated DI containers with independent lifetimes. Disposing a child scope disposes only its singletons — the parent is unaffected.

### Declaring a Scope

```csharp
// Parent composition
[Composition]
partial class AppComposition
{
    static void Setup() => DI.Setup()
        .Bind<GameManager>().As(Singleton).To<GameManager>()
        .Scope<GameComposition>()            // Declares child scope
        .Root<GameManager>("GameManager");
}

// Child composition
[Composition]
partial class GameComposition
{
    static void Setup() => DI.Setup()
        .Arg<int>("sessionSeed")
        .Bind<SimWorld>().As(Singleton).To<SimWorld>()
        .Root<SimWorld>("World");
}
```

The generator creates `GameComposition.Factory` (nested class) and injects it into any parent service that depends on it.

### Using the Factory

```csharp
public sealed class GameManager
{
    private readonly GameComposition.Factory _gameFactory;
    private GameComposition? _scope;

    public GameManager(GameComposition.Factory gameFactory)
    {
        _gameFactory = gameFactory;
    }

    public SimWorld World => _scope!.World;

    public void StartGame(int seed)
    {
        _scope?.Dispose();                       // Clean up old scope
        _scope = _gameFactory.Create(seed);      // Fresh scope with new instances
    }
}
```

`Factory.Create(...)` accepts the child's `Arg<T>` parameters in declaration order.

### Parent Injection (Decorator Pattern)

Child scopes can access parent bindings:

```csharp
[Composition]
partial class ChildComposition
{
    static void Setup() => DI.Setup()
        .Bind<ILogger>().As(Singleton).To(ctx =>
        {
            ctx.InjectFromParent(out ILogger parentLogger);
            return new ScopedLogger(parentLogger, "Child");
        })
        .Root<ILogger>("Logger");
}
```

Non-overridden parent bindings are automatically forwarded — child code can resolve them without explicit `InjectFromParent`.

## Disposal

All compositions implement `IDisposable`. Singletons implementing `IDisposable` are tracked and disposed in reverse creation order when the composition is disposed.

```csharp
using var app = new AppComposition();
// ... use app ...
// app.Dispose() disposes all singletons in LIFO order
```

For error handling during disposal, implement the partial method:
```csharp
partial class AppComposition
{
    partial void OnDisposeException(IDisposable instance, Exception exception)
    {
        Log.Error(exception, "Failed to dispose {Type}", instance.GetType().Name);
    }
}
```

## Gotchas

**Factory lambdas are limited.** Block lambdas can only contain `ctx.Inject*()` statements followed by a single `return` expression. Multi-statement logic (e.g., calling `.Initialize()` after construction) must be moved to a static helper method:

```csharp
// BAD — generator error DERP010 (multi-statement not supported)
.Bind<SimWorld>().As(Singleton).To(ctx =>
{
    ctx.Inject<int>(out var seed);
    var world = new SimWorld();
    world.Initialize(seed);  // Not allowed — only ctx.Inject*() + return
    return world;
})

// GOOD — delegate to a static factory method
.Bind<SimWorld>().As(Singleton).To(ctx =>
{
    ctx.Inject<int>(out var seed);
    return MyFactory.CreateWorld(seed);  // Single return expression
})
```

**Factory return expressions are emitted verbatim** into the generated file. The generator copies `using` directives from your source file into the generated output, so types referenced in return expressions will resolve as long as you have the appropriate `using` at the top of your composition file.

## Reference

- **Annotations API:** `Shared/DerpLib.DI/Annotations/DI.cs`
- **Test compositions:** `Shared/DerpLib.DI/Tests/TestCompositions.cs`
- **Test services (Factory/Scope pattern):** `Shared/DerpLib.DI/Tests/TestServices.cs`
- **GyrussClone example:** `Games/GyrussClone/Core/`
