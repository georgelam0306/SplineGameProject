using Core;
using DerpTech.Rollback;
using SimTable;

namespace Catrillion.Rollback;

public record struct GameInput : IGameInput<GameInput>
{
    // Selection box (world coordinates)
    public Fixed64Vec2 SelectStart;
    public Fixed64Vec2 SelectEnd;
    public bool IsSelecting;
    public bool HasSelectionComplete;  // True on the frame when selection drag ends (LMB release)

    // Move command (world coordinates)
    public Fixed64Vec2 MoveTarget;
    public bool HasMoveCommand;

    // Attack-move command (world coordinates)
    public Fixed64Vec2 AttackMoveTarget;
    public bool HasAttackMoveCommand;

    // Patrol command (world coordinates)
    public Fixed64Vec2 PatrolTarget;
    public bool HasPatrolCommand;

    // Building placement (tile coordinates, supports negative)
    public IntVec2 BuildingPlacementTile;
    public byte BuildingTypeToBuild;  // Cast to/from BuildingTypeId
    public bool HasBuildingPlacement;

    // Garrison commands
    public SimHandle GarrisonTargetHandle;  // Target building for garrison entry
    public bool HasEnterGarrisonCommand;
    public bool HasExitGarrisonCommand;
    public SimHandle SingleEjectUnitHandle;  // Specific unit to eject from garrison

    // Unit training commands
    public SimHandle TrainUnitBuildingHandle;  // Building to train from
    public byte TrainUnitTypeId;               // UnitTypeId to train
    public bool HasTrainUnitCommand;

    // Cancel training command
    public SimHandle CancelTrainingBuildingHandle;
    public byte CancelTrainingSlotIndex;  // Which queue slot to cancel (0-4)
    public bool HasCancelTrainingCommand;

    // Research commands
    /// <summary>Research item to start (1-255 = ResearchItemData.Id + 1, 0 = cancel current research).</summary>
    public byte ResearchCommand;
    /// <summary>True when a research command should be processed.</summary>
    public bool HasResearchCommand;

    // Upgrade commands
    /// <summary>Building handle to upgrade.</summary>
    public SimHandle UpgradeBuildingHandle;
    /// <summary>True when an upgrade command should be processed.</summary>
    public bool HasUpgradeCommand;

    // Destroy building command
    /// <summary>Building handle to destroy (self-destruct for refund).</summary>
    public SimHandle DestroyBuildingHandle;
    /// <summary>True when a destroy command should be processed.</summary>
    public bool HasDestroyCommand;

    // Repair building commands
    /// <summary>Building handle to start repairing.</summary>
    public SimHandle RepairBuildingHandle;
    /// <summary>True when a repair command should be processed.</summary>
    public bool HasRepairCommand;
    /// <summary>Building handle to cancel repair on.</summary>
    public SimHandle CancelRepairBuildingHandle;
    /// <summary>True when a cancel repair command should be processed.</summary>
    public bool HasCancelRepairCommand;

    public static GameInput Empty => default;

    public bool IsEmpty =>
        !IsSelecting &&
        !HasSelectionComplete &&
        !HasMoveCommand &&
        !HasAttackMoveCommand &&
        !HasPatrolCommand &&
        !HasBuildingPlacement &&
        !HasEnterGarrisonCommand &&
        !HasExitGarrisonCommand &&
        !HasTrainUnitCommand &&
        !HasCancelTrainingCommand &&
        !HasResearchCommand &&
        !HasUpgradeCommand &&
        !HasDestroyCommand &&
        !HasRepairCommand &&
        !HasCancelRepairCommand;
}
