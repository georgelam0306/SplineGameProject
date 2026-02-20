using System.Numerics;
using System.Runtime.CompilerServices;
using Core;
using Pooled;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 226)]
public partial struct StateMachineDefinitionComponent
{
    public const int MaxMachines = 8;
    public const int MaxLayers = 16;
    public const int MaxStates = 128;
    public const int MaxTransitions = 256;
    public const int MaxConditions = 512;
    public const int MaxBlendChildren = 512;

    public enum StateKind : byte
    {
        Timeline = 0,
        Blend1D = 1,
        BlendAdditive = 2
    }

    public enum TransitionFromKind : byte
    {
        State = 0,
        Entry = 1,
        AnyState = 2
    }

    public enum TransitionToKind : byte
    {
        State = 0,
        Exit = 1
    }

    public enum ConditionOp : byte
    {
        IsTrue = 0,
        IsFalse = 1,
        Equals = 2,
        NotEquals = 3,
        GreaterThan = 4,
        LessThan = 5,
        GreaterOrEqual = 6,
        LessOrEqual = 7
    }

    [InlineArray(MaxMachines)]
    public struct UShortBufferMachines
    {
        private ushort _element0;
    }

    [InlineArray(MaxMachines)]
    public struct StringHandleBufferMachines
    {
        private StringHandle _element0;
    }

    [InlineArray(MaxLayers)]
    public struct UShortBufferLayers
    {
        private ushort _element0;
    }

    [InlineArray(MaxLayers)]
    public struct StringHandleBufferLayers
    {
        private StringHandle _element0;
    }

    [InlineArray(MaxLayers)]
    public struct Vec2BufferLayers
    {
        private Vector2 _element0;
    }

    [InlineArray(MaxLayers)]
    public struct FloatBufferLayers
    {
        private float _element0;
    }

    [InlineArray(MaxStates)]
    public struct UShortBufferStates
    {
        private ushort _element0;
    }

    [InlineArray(MaxStates)]
    public struct StringHandleBufferStates
    {
        private StringHandle _element0;
    }

    [InlineArray(MaxStates)]
    public struct ByteBufferStates
    {
        private byte _element0;
    }

    [InlineArray(MaxStates)]
    public struct IntBufferStates
    {
        private int _element0;
    }

    [InlineArray(MaxStates)]
    public struct Vec2BufferStates
    {
        private Vector2 _element0;
    }

    [InlineArray(MaxStates)]
    public struct FloatBufferStates
    {
        private float _element0;
    }

    [InlineArray(MaxBlendChildren)]
    public struct UShortBufferBlendChildren
    {
        private ushort _element0;
    }

    [InlineArray(MaxBlendChildren)]
    public struct IntBufferBlendChildren
    {
        private int _element0;
    }

    [InlineArray(MaxBlendChildren)]
    public struct FloatBufferBlendChildren
    {
        private float _element0;
    }

    [InlineArray(MaxTransitions)]
    public struct UShortBufferTransitions
    {
        private ushort _element0;
    }

    [InlineArray(MaxTransitions)]
    public struct ByteBufferTransitions
    {
        private byte _element0;
    }

    [InlineArray(MaxTransitions)]
    public struct FloatBufferTransitions
    {
        private float _element0;
    }

    [InlineArray(MaxConditions)]
    public struct UShortBufferConditions
    {
        private ushort _element0;
    }

    [InlineArray(MaxConditions)]
    public struct ByteBufferConditions
    {
        private byte _element0;
    }

    [InlineArray(MaxConditions)]
    public struct ValueBufferConditions
    {
        private PropertyValue _element0;
    }

    [Column]
    public ushort NextMachineId;

    [Column]
    public ushort MachineCount;

    [Column]
    public UShortBufferMachines MachineId;

    [Column]
    public StringHandleBufferMachines MachineName;

    [Column(Storage = ColumnStorage.Sidecar)]
    public ushort SelectedMachineId;

    [Column]
    public ushort NextLayerId;

    [Column]
    public ushort LayerCount;

    [Column]
    public UShortBufferLayers LayerId;

    [Column]
    public UShortBufferLayers LayerMachineId;

    [Column]
    public StringHandleBufferLayers LayerName;

    [Column]
    public UShortBufferLayers LayerEntryTargetStateId;

    [Column]
    public UShortBufferLayers LayerNextStateId;

    [Column]
    public UShortBufferLayers LayerNextTransitionId;

    [Column(Storage = ColumnStorage.Sidecar)]
    public Vec2BufferLayers LayerEntryPos;

    [Column(Storage = ColumnStorage.Sidecar)]
    public Vec2BufferLayers LayerAnyStatePos;

    [Column(Storage = ColumnStorage.Sidecar)]
    public Vec2BufferLayers LayerExitPos;

    [Column(Storage = ColumnStorage.Sidecar)]
    public Vec2BufferLayers LayerPan;

    [Column(Storage = ColumnStorage.Sidecar)]
    public FloatBufferLayers LayerZoom;

    [Column]
    public ushort StateCount;

    [Column]
    public UShortBufferStates StateId;

    [Column]
    public UShortBufferStates StateLayerId;

    [Column]
    public StringHandleBufferStates StateName;

    [Column]
    public ByteBufferStates StateKindValue;

    [Column]
    public IntBufferStates StateTimelineId;

    [Column]
    public FloatBufferStates StatePlaybackSpeed;

    // Blend1D state settings (ignored for other StateKind values).
    [Column]
    public UShortBufferStates StateBlendParameterVariableId;

    [Column]
    public UShortBufferStates StateBlendChildStart;

    [Column]
    public ByteBufferStates StateBlendChildCount;

    [Column(Storage = ColumnStorage.Sidecar)]
    public Vec2BufferStates StatePos;

    [Column]
    public ushort BlendChildCount;

    [Column]
    public IntBufferBlendChildren BlendChildTimelineId;

    [Column]
    public FloatBufferBlendChildren BlendChildThreshold;

    [Column]
    public ushort TransitionCount;

    [Column]
    public UShortBufferTransitions TransitionId;

    [Column]
    public UShortBufferTransitions TransitionLayerId;

    [Column]
    public ByteBufferTransitions TransitionFromKindValue;

    [Column]
    public UShortBufferTransitions TransitionFromStateId;

    [Column]
    public ByteBufferTransitions TransitionToKindValue;

    [Column]
    public UShortBufferTransitions TransitionToStateId;

    [Column]
    public FloatBufferTransitions TransitionDurationSeconds;

    [Column]
    public ByteBufferTransitions TransitionHasExitTimeValue;

    [Column]
    public FloatBufferTransitions TransitionExitTime01;

    [Column]
    public ushort ConditionCount;

    [Column]
    public UShortBufferTransitions TransitionConditionStart;

    [Column]
    public ByteBufferTransitions TransitionConditionCount;

    [Column]
    public UShortBufferConditions ConditionVariableId;

    [Column]
    public ByteBufferConditions ConditionOpValue;

    [Column]
    public ValueBufferConditions ConditionCompareValue;

    internal static StateMachineDefinitionComponent CreateDefault()
    {
        var def = new StateMachineDefinitionComponent
        {
            NextMachineId = 2,
            MachineCount = 1,
            SelectedMachineId = 1,

            NextLayerId = 2,
            LayerCount = 1,

            StateCount = 0,
            TransitionCount = 0,

            ConditionCount = 0,
            BlendChildCount = 0
        };

        def.MachineId[0] = 1;
        def.MachineName[0] = "State Machine 1";

        def.LayerId[0] = 1;
        def.LayerMachineId[0] = 1;
        def.LayerName[0] = "Layer 1";
        def.LayerEntryTargetStateId[0] = 0;
        def.LayerNextStateId[0] = 1;
        def.LayerNextTransitionId[0] = 1;

        def.LayerEntryPos[0] = new Vector2(0f, -140f);
        def.LayerAnyStatePos[0] = new Vector2(-180f, 160f);
        def.LayerExitPos[0] = new Vector2(180f, 160f);
        def.LayerPan[0] = Vector2.Zero;
        def.LayerZoom[0] = 1f;

        return def;
    }
}
