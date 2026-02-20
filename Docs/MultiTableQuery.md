# Multi-Table Query API

Unified iteration over multiple SimTable row types that share common fields.

## Defining a Query

```csharp
[MultiTableQuery]
public interface IDamageable
{
    ref Fixed64Vec2 Position { get; }
    ref int Health { get; }
}
```

The generator auto-discovers all `[SimTable]` types with matching field names/types.

## Iteration Approaches

### 1. Chunked (Recommended for Hot Paths)

```csharp
foreach (var chunk in world.Query<IDamageable>().ByTable())
{
    var healths = chunk.Healths;
    int count = chunk.Count;
    for (int i = 0; i < count; i++)
        healths[i] -= damage;
}
```

**Performance: ~0% overhead vs optimal manual span iteration.**

Use for: Systems that run every frame, SIMD-friendly operations.

### 2. Per-Entity (Convenience API)

```csharp
foreach (var entity in world.Query<IDamageable>())
{
    entity.Health -= damage;
    if (entity.Is<ZombieRow>()) { /* type-specific logic */ }
}
```

**Performance: ~55% overhead vs chunked.**

Use for: Non-hot paths, code clarity, type discrimination.

## Quick Reference

| Approach | Overhead | Use Case |
|----------|----------|----------|
| `Query<T>().ByTable()` | ~0% | Hot paths, per-frame systems |
| `Query<T>()` per-entity | ~55% | Convenience, type checks |
