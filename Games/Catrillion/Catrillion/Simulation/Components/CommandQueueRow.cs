using Core;
using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Command queue for units with multiple orders.
/// Linked to owning entity via SimHandle.
/// </summary>
[SimTable(Capacity = 10000)]
public partial struct CommandQueueRow
{
    public SimHandle OwnerHandle;

    // Order slot 0
    public OrderType Order0Type;
    public Fixed64Vec2 Order0Target;
    public SimHandle Order0TargetHandle;

    // Order slot 1
    public OrderType Order1Type;
    public Fixed64Vec2 Order1Target;
    public SimHandle Order1TargetHandle;

    // Order slot 2
    public OrderType Order2Type;
    public Fixed64Vec2 Order2Target;
    public SimHandle Order2TargetHandle;

    // Order slot 3
    public OrderType Order3Type;
    public Fixed64Vec2 Order3Target;
    public SimHandle Order3TargetHandle;

    // Queue state
    public byte QueueLength;
    public byte CurrentIndex;

    // State
    public CommandQueueFlags Flags;
}
