using System.Numerics;
using Core;

namespace Catrillion.Input;

/// <summary>
/// Buffers discrete input actions between simulation ticks.
/// When rendering at 60 FPS but simulating at 30 FPS, inputs that occur
/// on non-simulation frames would be lost. This buffer queues them.
///
/// Uses "last wins" strategy - if multiple actions of the same type occur,
/// the last one's data is used.
/// </summary>
public sealed class InputActionBuffer
{
    // Movement commands (RMB click)
    private bool _hasMoveCommand;
    private Fixed64Vec2 _moveTarget;

    // Attack-move command (A + LMB click)
    private bool _hasAttackMoveCommand;
    private Fixed64Vec2 _attackMoveTarget;

    // Patrol command (P + LMB click)
    private bool _hasPatrolCommand;
    private Fixed64Vec2 _patrolTarget;

    // Mode entry keys
    private bool _enterAttackMoveMode;
    private bool _enterPatrolMode;

    // Selection tracking (needs special handling for drag state)
    private bool _selectionStarted;
    private Fixed64Vec2 _selectStart;
    private bool _selectionCompleted;
    private Fixed64Vec2 _selectEnd;

    /// <summary>
    /// Queue a move command (RMB click).
    /// </summary>
    public void QueueMoveCommand(Fixed64Vec2 target)
    {
        _hasMoveCommand = true;
        _moveTarget = target;
    }

    /// <summary>
    /// Queue an attack-move command.
    /// </summary>
    public void QueueAttackMoveCommand(Fixed64Vec2 target)
    {
        _hasAttackMoveCommand = true;
        _attackMoveTarget = target;
    }

    /// <summary>
    /// Queue a patrol command.
    /// </summary>
    public void QueuePatrolCommand(Fixed64Vec2 target)
    {
        _hasPatrolCommand = true;
        _patrolTarget = target;
    }

    /// <summary>
    /// Queue entering attack-move mode (A key).
    /// </summary>
    public void QueueEnterAttackMoveMode()
    {
        _enterAttackMoveMode = true;
    }

    /// <summary>
    /// Queue entering patrol mode (P key).
    /// </summary>
    public void QueueEnterPatrolMode()
    {
        _enterPatrolMode = true;
    }

    /// <summary>
    /// Queue selection start (LMB down).
    /// </summary>
    public void QueueSelectionStart(Fixed64Vec2 worldPos)
    {
        _selectionStarted = true;
        _selectStart = worldPos;
    }

    /// <summary>
    /// Queue selection complete (LMB release).
    /// </summary>
    public void QueueSelectionComplete(Fixed64Vec2 endPos)
    {
        _selectionCompleted = true;
        _selectEnd = endPos;
    }

    /// <summary>
    /// Check if attack-move mode should be entered and consume the flag.
    /// </summary>
    public bool ConsumeEnterAttackMoveMode()
    {
        bool result = _enterAttackMoveMode;
        _enterAttackMoveMode = false;
        return result;
    }

    /// <summary>
    /// Check if patrol mode should be entered and consume the flag.
    /// </summary>
    public bool ConsumeEnterPatrolMode()
    {
        bool result = _enterPatrolMode;
        _enterPatrolMode = false;
        return result;
    }

    /// <summary>
    /// Check if a move command was queued.
    /// </summary>
    public bool HasMoveCommand => _hasMoveCommand;
    public Fixed64Vec2 MoveTarget => _moveTarget;

    /// <summary>
    /// Check if an attack-move command was queued.
    /// </summary>
    public bool HasAttackMoveCommand => _hasAttackMoveCommand;
    public Fixed64Vec2 AttackMoveTarget => _attackMoveTarget;

    /// <summary>
    /// Check if a patrol command was queued.
    /// </summary>
    public bool HasPatrolCommand => _hasPatrolCommand;
    public Fixed64Vec2 PatrolTarget => _patrolTarget;

    /// <summary>
    /// Check if selection was started this buffer period.
    /// </summary>
    public bool SelectionStarted => _selectionStarted;
    public Fixed64Vec2 SelectStart => _selectStart;

    /// <summary>
    /// Check if selection was completed this buffer period.
    /// </summary>
    public bool SelectionCompleted => _selectionCompleted;
    public Fixed64Vec2 SelectEnd => _selectEnd;

    /// <summary>
    /// Clear all buffered actions. Call after draining into GameInput.
    /// </summary>
    public void Clear()
    {
        _hasMoveCommand = false;
        _hasAttackMoveCommand = false;
        _hasPatrolCommand = false;
        _enterAttackMoveMode = false;
        _enterPatrolMode = false;
        _selectionStarted = false;
        _selectionCompleted = false;
    }
}
