using System;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Core;
using FlowField;
using SimTable;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Handles move commands for selected units.
/// On RMB click, assigns a group ID to selected units and creates a MoveCommandRow.
/// </summary>
public sealed class MoveCommandSystem : SimTableSystem
{
    private readonly IZoneFlowService _flowService;

    // Reusable buffers for formation assignment (avoids stackalloc limit for large groups)
    private const int MaxFormationUnits = 1024;
    private readonly int[] _unitSlots = new int[MaxFormationUnits];
    private readonly Fixed64Vec2[] _unitOffsets = new Fixed64Vec2[MaxFormationUnits];
    private readonly Fixed64Vec2[] _slotPositions = new Fixed64Vec2[MaxFormationUnits];
    private readonly Fixed64Vec2[] _slotLocalOffsets = new Fixed64Vec2[MaxFormationUnits];
    private readonly Fixed64Vec2[] _unitLocalOffsets = new Fixed64Vec2[MaxFormationUnits];
    private readonly bool[] _slotAssigned = new bool[MaxFormationUnits];
    private readonly bool[] _unitAssigned = new bool[MaxFormationUnits];

    public MoveCommandSystem(SimWorld world, IZoneFlowService flowService) : base(world)
    {
        _flowService = flowService;
    }

    public override void Tick(in SimulationContext context)
    {
        // Commands not processed during countdown
        if (!World.IsPlaying()) return;

        for (int playerId = 0; playerId < context.PlayerCount; playerId++)
        {
            ref readonly var input = ref context.GetInput(playerId);

            if (input.HasMoveCommand)
            {
                IssueMoveCommand(playerId, context.CurrentFrame, input.MoveTarget, OrderType.Move);
            }
            else if (input.HasAttackMoveCommand)
            {
                IssueMoveCommand(playerId, context.CurrentFrame, input.AttackMoveTarget, OrderType.AttackMove);
            }
            else if (input.HasPatrolCommand)
            {
                IssuePatrolCommand(playerId, context.CurrentFrame, input.PatrolTarget);
            }
        }
    }

    private void IssueMoveCommand(int playerId, int currentFrame, Fixed64Vec2 destination, OrderType orderType)
    {
        var units = World.CombatUnitRows;
        var commands = World.MoveCommandRows;
        Fixed64Vec2 groupCenter = Fixed64Vec2.Zero;
        int unitCount = 0;

        for (int slot = 0; slot < units.Count && unitCount < MaxFormationUnits; slot++)
        {
            if (!units.TryGetRow(slot, out var unit)) continue;
            if (!unit.Flags.HasFlag(MortalFlags.IsActive)) continue;
            if (unit.SelectedByPlayerId != playerId) continue;

            _unitSlots[unitCount] = slot;
            groupCenter = groupCenter + unit.Position;
            unitCount++;
        }

        if (unitCount == 0) return;

        // Calculate center and unit offsets from center
        groupCenter = new Fixed64Vec2(
            groupCenter.X / Fixed64.FromInt(unitCount),
            groupCenter.Y / Fixed64.FromInt(unitCount)
        );

        for (int i = 0; i < unitCount; i++)
        {
            var unit = units.GetRowBySlot(_unitSlots[i]);
            _unitOffsets[i] = unit.Position - groupCenter;
        }

        // Allocate move command
        SimHandle cmdHandle = commands.Allocate();
        int groupId = cmdHandle.StableId;

        // Calculate formation aligned to movement direction
        // Columns spread along binormal (perpendicular to movement) = wide line
        // Rows spread along forward (movement direction) = depth
        const int UnitSpacing = 20;
        int columns = Math.Min(unitCount, 10); // Wide formation perpendicular to movement
        int rows = (unitCount + columns - 1) / columns;

        // Direction from group center to destination
        Fixed64Vec2 moveDir = destination - groupCenter;
        Fixed64 moveDirLenSq = moveDir.LengthSquared();

        Fixed64Vec2 forward;
        Fixed64Vec2 right; // binormal
        if (moveDirLenSq > Fixed64.FromFloat(0.001f))
        {
            Fixed64 moveDirLen = Fixed64.Sqrt(moveDirLenSq);
            forward = new Fixed64Vec2(moveDir.X / moveDirLen, moveDir.Y / moveDirLen);
            right = new Fixed64Vec2(-forward.Y, forward.X); // perpendicular
        }
        else
        {
            // No movement, default to axis-aligned
            forward = new Fixed64Vec2(Fixed64.Zero, Fixed64.OneValue);
            right = new Fixed64Vec2(Fixed64.OneValue, Fixed64.Zero);
        }

        Fixed64 halfWidth = Fixed64.FromInt((columns - 1) * UnitSpacing / 2);
        Fixed64 halfDepth = Fixed64.FromInt((rows - 1) * UnitSpacing / 2);

        for (int i = 0; i < unitCount; i++)
        {
            int col = i % columns;
            int row = i / columns;

            // Local offset in formation space (for matching)
            Fixed64 localX = Fixed64.FromInt(col * UnitSpacing) - halfWidth;
            Fixed64 localY = Fixed64.FromInt(row * UnitSpacing) - halfDepth;
            _slotLocalOffsets[i] = new Fixed64Vec2(localX, localY);

            // Transform to world space using formation basis vectors
            Fixed64Vec2 worldOffset = new Fixed64Vec2(
                right.X * localX + forward.X * localY,
                right.Y * localX + forward.Y * localY
            );

            // Validate slot position - find nearest passable if blocked
            Fixed64Vec2 candidatePos = destination + worldOffset;
            _slotPositions[i] = FindNearestPassablePosition(candidatePos);
        }

        // Project unit offsets into formation local space for matching
        // This ensures units on the "right" of the group go to "right" slots
        for (int i = 0; i < unitCount; i++)
        {
            Fixed64Vec2 worldOffset = _unitOffsets[i];
            // Project onto formation basis: localX = dot(offset, right), localY = dot(offset, forward)
            _unitLocalOffsets[i] = new Fixed64Vec2(
                worldOffset.X * right.X + worldOffset.Y * right.Y,
                worldOffset.X * forward.X + worldOffset.Y * forward.Y
            );
        }

        // Match units to slots by relative position similarity
        // Use best-pair-first matching: always pick the (slot, unit) pair with smallest distance
        // Clear assignment arrays
        Array.Clear(_slotAssigned, 0, unitCount);
        Array.Clear(_unitAssigned, 0, unitCount);

        for (int assigned = 0; assigned < unitCount; assigned++)
        {
            int bestSlot = -1;
            int bestUnit = -1;
            Fixed64 bestDistSq = Fixed64.MaxValue;

            // Find the globally best (slot, unit) pair
            for (int slotIdx = 0; slotIdx < unitCount; slotIdx++)
            {
                if (_slotAssigned[slotIdx]) continue;
                Fixed64Vec2 slotLocal = _slotLocalOffsets[slotIdx];

                for (int unitIdx = 0; unitIdx < unitCount; unitIdx++)
                {
                    if (_unitAssigned[unitIdx]) continue;

                    Fixed64Vec2 diff = _unitLocalOffsets[unitIdx] - slotLocal;
                    Fixed64 distSq = diff.X * diff.X + diff.Y * diff.Y;

                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestSlot = slotIdx;
                        bestUnit = unitIdx;
                    }
                }
            }

            if (bestSlot < 0) break;

            _slotAssigned[bestSlot] = true;
            _unitAssigned[bestUnit] = true;

            var unit = units.GetRowBySlot(_unitSlots[bestUnit]);
            unit.GroupId = groupId;
            unit.CurrentOrder = orderType;
            unit.OrderTarget = _slotPositions[bestSlot];
        }

        // Initialize the command
        var cmd = commands.GetRow(cmdHandle);
        cmd.Destination = destination;
        cmd.IssuedFrame = currentFrame;
        cmd.IsActive = true;

        // Store all formation slot positions for multi-target flow field
        cmd.SlotCount = unitCount;
        for (int i = 0; i < unitCount; i++)
        {
            var tile = WorldTile.FromPixel(_slotPositions[i]);
            cmd.SlotTileXArray[i] = tile.X;
            cmd.SlotTileYArray[i] = tile.Y;
        }
    }

    /// <summary>
    /// Issues a patrol command to selected units. Units will move between their current position and the destination.
    /// </summary>
    private void IssuePatrolCommand(int playerId, int currentFrame, Fixed64Vec2 destination)
    {
        var units = World.CombatUnitRows;

        for (int slot = 0; slot < units.Count; slot++)
        {
            if (!units.TryGetRow(slot, out var unit)) continue;
            if (!unit.Flags.HasFlag(MortalFlags.IsActive)) continue;
            if (unit.SelectedByPlayerId != playerId) continue;

            // Store current position as patrol start, destination as patrol end
            unit.PatrolStart = unit.Position;
            unit.OrderTarget = FindNearestPassablePosition(destination);
            unit.CurrentOrder = OrderType.Patrol;
            unit.GroupId = currentFrame; // Use frame as group ID for patrol
        }
    }

    /// <summary>
    /// Finds the nearest passable position to the given world position.
    /// If the position is already passable, returns it unchanged.
    /// Uses SDF gradient ascent to find the nearest open space efficiently.
    /// </summary>
    private Fixed64Vec2 FindNearestPassablePosition(Fixed64Vec2 worldPos)
    {
        var zoneGraph = _flowService.GetZoneGraph();
        var destTile = WorldTile.FromPixel(worldPos);

        // Check if already passable
        if (zoneGraph.GetZoneIdAtTile(destTile.X, destTile.Y).HasValue)
        {
            return worldPos;
        }

        // Use SDF gradient ascent to find nearest passable tile
        // Follow the gradient toward higher wall distance (open space)
        const int MaxIterations = 10;
        int currentX = destTile.X;
        int currentY = destTile.Y;

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            // Sample SDF at 4 cardinal neighbors to compute gradient
            Fixed64 distUp = zoneGraph.GetWallDistance(currentX, currentY - 1);
            Fixed64 distDown = zoneGraph.GetWallDistance(currentX, currentY + 1);
            Fixed64 distLeft = zoneGraph.GetWallDistance(currentX - 1, currentY);
            Fixed64 distRight = zoneGraph.GetWallDistance(currentX + 1, currentY);

            // Find direction with highest SDF (toward open space)
            Fixed64 maxDist = distUp;
            int bestDx = 0, bestDy = -1;

            if (distDown > maxDist)
            {
                maxDist = distDown;
                bestDx = 0;
                bestDy = 1;
            }
            if (distLeft > maxDist)
            {
                maxDist = distLeft;
                bestDx = -1;
                bestDy = 0;
            }
            if (distRight > maxDist)
            {
                maxDist = distRight;
                bestDx = 1;
                bestDy = 0;
            }

            // No gradient found (stuck in obstacle) - fall back to original
            if (maxDist == Fixed64.Zero)
            {
                break;
            }

            // Move toward highest SDF
            currentX += bestDx;
            currentY += bestDy;

            // Check if we've reached a passable tile
            if (zoneGraph.GetZoneIdAtTile(currentX, currentY).HasValue)
            {
                return new Fixed64Vec2(
                    Fixed64.FromInt(currentX * 32 + 16),
                    Fixed64.FromInt(currentY * 32 + 16)
                );
            }
        }

        // Gradient ascent failed - return original position
        return worldPos;
    }
}
