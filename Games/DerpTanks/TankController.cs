using System.Numerics;
using Core;
using Core.Input;

namespace DerpTanks;

/// <summary>
/// Tank movement controller using the action-based input system.
/// </summary>
public class TankController
{
    private readonly IInputManager _input;

    private const float MoveSpeed = 8f;    // Units per second
    private const float TurnSpeed = 3f;    // Radians per second

    private static readonly StringHandle MapName = "Tank";
    private static readonly StringHandle MoveAction = "Move";

    public TankController(IInputManager input)
    {
        _input = input;
    }

    public void Update(float dt, ref Vector3 position, ref float rotation)
    {
        // Read move input (X = turn left/right, Y = forward/back).
        var move = _input.ReadAction(MapName, MoveAction).Vector2;

        rotation -= move.X * TurnSpeed * dt;

        var forward = new Vector3(MathF.Sin(rotation), 0f, MathF.Cos(rotation));
        position += forward * move.Y * MoveSpeed * dt;

        // Fire is handled by simulation via Program.cs (to enforce fire rate + explosions deterministically).
    }
}
