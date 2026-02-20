using System.Runtime.CompilerServices;
using Pooled;
using Property;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 227)]
public partial struct StateMachineRuntimeComponent
{
    public const int MaxLayers = StateMachineDefinitionComponent.MaxLayers;

    [InlineArray(MaxLayers)]
    public struct UShortBufferLayers
    {
        private ushort _element0;
    }

    [InlineArray(MaxLayers)]
    public struct UIntBufferLayers
    {
        private uint _element0;
    }

    [Column]
    [Property(Name = "Active Machine", Group = "State Machine", Order = 0, Min = 0f, Step = 1f)]
    public int ActiveMachineId;

    [Column]
    [Property(Name = "Initialized", Group = "State Machine", Order = 1, Min = 0f, Max = 1f, Step = 1f)]
    public bool IsInitialized;

    // Debug conveniences for the property inspector (mirrors the first layer in the active machine, when present).
    [Column]
    [Property(Name = "Debug Active Layer", Group = "State Machine", Order = 2, Min = 0f, Step = 1f)]
    public int DebugActiveLayerId;

    [Column]
    [Property(Name = "Debug Active State", Group = "State Machine", Order = 3, Min = 0f, Step = 1f)]
    public int DebugActiveStateId;

    [Column]
    [Property(Name = "Debug Last Transition", Group = "State Machine", Order = 4, Min = 0f, Step = 1f)]
    public int DebugLastTransitionId;

    [Column]
    [Array(MaxLayers)]
    public UShortBufferLayers LayerCurrentStateId;

    [Column]
    [Array(MaxLayers)]
    public UShortBufferLayers LayerPreviousStateId;

    [Column]
    [Array(MaxLayers)]
    public UIntBufferLayers LayerStateTimeUs;

    // Editor/runtime visualization helpers (do not affect authoring data).
    [Column]
    [Array(MaxLayers)]
    public UShortBufferLayers LayerTransitionId;

    [Column]
    [Array(MaxLayers)]
    public UShortBufferLayers LayerTransitionFromStateId;

    [Column]
    [Array(MaxLayers)]
    public UShortBufferLayers LayerTransitionToStateId;

    [Column]
    [Array(MaxLayers)]
    public UIntBufferLayers LayerTransitionTimeUs;

    [Column]
    [Array(MaxLayers)]
    public UIntBufferLayers LayerTransitionDurationUs;
}
