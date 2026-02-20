using System.Runtime.InteropServices;
using Core;

namespace Core.Input;

/// <summary>
/// Compact snapshot of action state at a specific frame. 32 bytes, cache-aligned.
/// Used for input history buffering between render and simulation frames.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct ActionSnapshot
{
    /// <summary>Frame number when this snapshot was captured.</summary>
    public ulong FrameNumber;      // 8 bytes

    /// <summary>Action identifier (StringHandle.Id).</summary>
    public uint ActionId;          // 4 bytes

    /// <summary>Action phase at capture time.</summary>
    public ActionPhase Phase;      // 1 byte

    /// <summary>Action type for value interpretation.</summary>
    public ActionType Type;        // 1 byte

    private ushort _padding;       // 2 bytes alignment

    /// <summary>Action value at capture time.</summary>
    public InputValue Value;       // 16 bytes

    public ActionSnapshot(
        ulong frameNumber,
        StringHandle actionName,
        ActionPhase phase,
        ActionType type,
        InputValue value)
    {
        FrameNumber = frameNumber;
        ActionId = actionName.Id;
        Phase = phase;
        Type = type;
        _padding = 0;
        Value = value;
    }

    /// <summary>Get the action name as a StringHandle.</summary>
    public StringHandle ActionName => new StringHandle(ActionId);

    /// <summary>True if action was just started this frame.</summary>
    public bool WasStarted => Phase == ActionPhase.Started;

    /// <summary>True if action was just canceled this frame.</summary>
    public bool WasCanceled => Phase == ActionPhase.Canceled;

    /// <summary>True if action is currently active (Started or Performed).</summary>
    public bool IsActive => Phase == ActionPhase.Started || Phase == ActionPhase.Performed;
}
