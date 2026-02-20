namespace Derp.UI;

public readonly struct UiRuntimeStateMachineDebug
{
    public readonly int ActiveMachineId;
    public readonly bool IsInitialized;
    public readonly int DebugActiveLayerId;
    public readonly int DebugActiveStateId;
    public readonly int DebugLastTransitionId;

    public readonly ushort Layer0CurrentStateId;
    public readonly ushort Layer0PreviousStateId;
    public readonly uint Layer0StateTimeUs;

    public readonly ushort Layer0TransitionId;
    public readonly ushort Layer0TransitionFromStateId;
    public readonly ushort Layer0TransitionToStateId;
    public readonly uint Layer0TransitionTimeUs;
    public readonly uint Layer0TransitionDurationUs;

    public UiRuntimeStateMachineDebug(
        int activeMachineId,
        bool isInitialized,
        int debugActiveLayerId,
        int debugActiveStateId,
        int debugLastTransitionId,
        ushort layer0CurrentStateId,
        ushort layer0PreviousStateId,
        uint layer0StateTimeUs,
        ushort layer0TransitionId,
        ushort layer0TransitionFromStateId,
        ushort layer0TransitionToStateId,
        uint layer0TransitionTimeUs,
        uint layer0TransitionDurationUs)
    {
        ActiveMachineId = activeMachineId;
        IsInitialized = isInitialized;
        DebugActiveLayerId = debugActiveLayerId;
        DebugActiveStateId = debugActiveStateId;
        DebugLastTransitionId = debugLastTransitionId;
        Layer0CurrentStateId = layer0CurrentStateId;
        Layer0PreviousStateId = layer0PreviousStateId;
        Layer0StateTimeUs = layer0StateTimeUs;
        Layer0TransitionId = layer0TransitionId;
        Layer0TransitionFromStateId = layer0TransitionFromStateId;
        Layer0TransitionToStateId = layer0TransitionToStateId;
        Layer0TransitionTimeUs = layer0TransitionTimeUs;
        Layer0TransitionDurationUs = layer0TransitionDurationUs;
    }
}
