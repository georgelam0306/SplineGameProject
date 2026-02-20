using System.Numerics;

namespace DerpLib.ImGui.Input;

/// <summary>
/// Public wrapper for cursor-lock drag operations used by number scrub widgets.
/// </summary>
public static class ImMouseDragLock
{
    public static bool Begin(int ownerId)
    {
        return ImMouseCursorLock.Begin(ownerId);
    }

    public static Vector2 ConsumeDelta(int ownerId)
    {
        return ImMouseCursorLock.ConsumeDelta(ownerId);
    }

    public static void End(int ownerId)
    {
        ImMouseCursorLock.End(ownerId);
    }
}
