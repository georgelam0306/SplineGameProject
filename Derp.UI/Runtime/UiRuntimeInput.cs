using System.Numerics;

namespace Derp.UI;

public readonly struct UiPointerFrameInput
{
    /// <summary>
    /// Sentinel value: when passed for <see cref="HoveredStableId"/>, the runtime will compute hover via hit-test.
    /// </summary>
    public const uint ComputeHoveredStableId = 0xFFFF_FFFFu;

    public readonly bool PointerValid;
    public readonly Vector2 PointerWorld;
    public readonly bool PrimaryDown;
    public readonly float WheelDelta;
    public readonly uint HoveredStableId;

    public UiPointerFrameInput(bool pointerValid, Vector2 pointerWorld, bool primaryDown, float wheelDelta, uint hoveredStableId)
    {
        PointerValid = pointerValid;
        PointerWorld = pointerWorld;
        PrimaryDown = primaryDown;
        WheelDelta = wheelDelta;
        HoveredStableId = hoveredStableId;
    }
}

public sealed class UiRuntimeInput
{
    private UiPointerFrameInput _current;
    private bool _previousPrimaryDown;

    public bool PrimaryPressed { get; private set; }
    public bool PrimaryReleased { get; private set; }

    public ref readonly UiPointerFrameInput Current => ref _current;

    public void SetFrameInput(in UiPointerFrameInput input)
    {
        PrimaryPressed = input.PrimaryDown && !_previousPrimaryDown;
        PrimaryReleased = !input.PrimaryDown && _previousPrimaryDown;

        _previousPrimaryDown = input.PrimaryDown;
        _current = input;
    }

    public void SetComputedHoveredStableId(uint hoveredStableId)
    {
        _current = new UiPointerFrameInput(
            pointerValid: _current.PointerValid,
            pointerWorld: _current.PointerWorld,
            primaryDown: _current.PrimaryDown,
            wheelDelta: _current.WheelDelta,
            hoveredStableId: hoveredStableId);
    }
}
