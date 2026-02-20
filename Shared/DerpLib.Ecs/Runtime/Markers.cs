namespace DerpLib.Ecs;

/// <summary>
/// Marker for component types used by Derp.Ecs.
/// </summary>
public interface IEcsComponent;

/// <summary>
/// Authoritative simulation component (snapshotted).
/// </summary>
public interface ISimComponent : IEcsComponent;

/// <summary>
/// Derived simulation component (not snapshotted; recomputed after restore).
/// </summary>
public interface ISimDerivedComponent : IEcsComponent;

/// <summary>
/// View/presentation component (not part of rollback snapshots).
/// </summary>
public interface IViewComponent : IEcsComponent;

/// <summary>
/// Marker for view archetype tags used by Spawn&lt;TArchetypeTag&gt;.
/// </summary>
public interface IViewArchetypeTag;

