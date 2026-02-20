using System;
using System.Collections.Generic;
using Catrillion.GameData.Schemas;
using R3;
using SimTable;

namespace Catrillion.AppState;

/// <summary>
/// Unit command modes for cursor state (A+click attack-move, P+click patrol).
/// </summary>
public enum UnitCommandMode
{
    None,
    AttackMove,
    Patrol
}

/// <summary>
/// Client-side gameplay state (non-deterministic).
/// Tracks build mode, placement preview, and other UI state.
/// </summary>
public sealed class GameplayStore : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    /// <summary>
    /// Currently selected building type to place, or null if not in build mode.
    /// </summary>
    public ReactiveProperty<BuildingTypeId?> BuildModeType { get; }

    /// <summary>
    /// Current unit command mode (attack-move or patrol cursor). None = normal mode.
    /// </summary>
    public ReactiveProperty<UnitCommandMode> UnitCommandMode { get; }

    /// <summary>
    /// Current placement preview position (tile coords), or null if not previewing.
    /// </summary>
    public ReactiveProperty<(int tileX, int tileY)?> PlacementPreview { get; }

    /// <summary>
    /// Currently open building category (folder) ID, or null if showing category list.
    /// </summary>
    public ReactiveProperty<int?> OpenCategoryId { get; }

    /// <summary>
    /// Whether currently in build mode.
    /// </summary>
    public ReadOnlyReactiveProperty<bool> IsInBuildMode { get; }

    /// <summary>
    /// Flag to trigger garrison eject command. Reset after being read by input manager.
    /// </summary>
    public ReactiveProperty<bool> ShouldEjectGarrison { get; }

    /// <summary>
    /// Unit handle to eject from garrison. Reset after being read by input manager.
    /// </summary>
    public ReactiveProperty<SimHandle> SingleEjectUnitHandle { get; }

    /// <summary>
    /// Pending train unit command (building handle, unit type ID).
    /// Rollback-safe: GameplayStore is client-side only. PollGameInput() reads once per
    /// real frame, writes to GameInput, then clears. GameInput is what gets synchronized.
    /// </summary>
    public ReactiveProperty<(SimHandle building, byte unitType)?> PendingTrainCommand { get; }

    /// <summary>
    /// Pending cancel training command (building handle, slot index).
    /// Rollback-safe: same pattern as PendingTrainCommand.
    /// </summary>
    public ReactiveProperty<(SimHandle building, byte slotIndex)?> PendingCancelTrainingCommand { get; }

    /// <summary>
    /// Currently hovered building slot, or -1 if not hovering any building.
    /// Used for production progress bar rendering.
    /// </summary>
    public ReactiveProperty<int> HoveredBuildingSlot { get; }

    /// <summary>
    /// Pending research command. 0 = cancel, 1-255 = research item ID + 1.
    /// Reset after being read by input manager.
    /// </summary>
    public ReactiveProperty<int> PendingResearchCommand { get; }

    /// <summary>
    /// Pending upgrade command. Building handle to upgrade.
    /// Reset after being read by input manager.
    /// </summary>
    public ReactiveProperty<SimHandle> PendingUpgradeCommand { get; }

    /// <summary>
    /// Queue of building handles pending destruction. Input manager polls one per frame.
    /// </summary>
    public Queue<SimHandle> PendingDestroyQueue { get; } = new Queue<SimHandle>();

    /// <summary>
    /// Pending repair command. Building handle to start repairing.
    /// Reset after being read by input manager.
    /// </summary>
    public ReactiveProperty<SimHandle> PendingRepairCommand { get; }

    /// <summary>
    /// Pending cancel repair command. Building handle to stop repairing.
    /// Reset after being read by input manager.
    /// </summary>
    public ReactiveProperty<SimHandle> PendingCancelRepairCommand { get; }

    /// <summary>
    /// Destroy confirmation modal state: list of buildings to destroy with total refunds.
    /// Null if modal is not shown.
    /// </summary>
    public ReactiveProperty<(List<SimHandle> buildings, int totalRefundGold, int totalRefundWood, int totalRefundStone, int totalRefundIron, int totalRefundOil, int buildingCount)?> DestroyConfirmation { get; }

    public GameplayStore()
    {
        BuildModeType = new ReactiveProperty<BuildingTypeId?>(null);
        UnitCommandMode = new ReactiveProperty<UnitCommandMode>(AppState.UnitCommandMode.None);
        PlacementPreview = new ReactiveProperty<(int tileX, int tileY)?>(null);
        OpenCategoryId = new ReactiveProperty<int?>(null);
        ShouldEjectGarrison = new ReactiveProperty<bool>(false);
        SingleEjectUnitHandle = new ReactiveProperty<SimHandle>(SimHandle.Invalid);
        PendingTrainCommand = new ReactiveProperty<(SimHandle building, byte unitType)?>(null);
        PendingCancelTrainingCommand = new ReactiveProperty<(SimHandle building, byte slotIndex)?>(null);
        HoveredBuildingSlot = new ReactiveProperty<int>(-1);
        PendingResearchCommand = new ReactiveProperty<int>(-1);
        PendingUpgradeCommand = new ReactiveProperty<SimHandle>(SimHandle.Invalid);
        PendingRepairCommand = new ReactiveProperty<SimHandle>(SimHandle.Invalid);
        PendingCancelRepairCommand = new ReactiveProperty<SimHandle>(SimHandle.Invalid);
        DestroyConfirmation = new ReactiveProperty<(List<SimHandle> buildings, int totalRefundGold, int totalRefundWood, int totalRefundStone, int totalRefundIron, int totalRefundOil, int buildingCount)?>(null);

        IsInBuildMode = BuildModeType
            .Select(type => type.HasValue)
            .ToReadOnlyReactiveProperty();

        _disposables.Add(BuildModeType);
        _disposables.Add(UnitCommandMode);
        _disposables.Add(PlacementPreview);
        _disposables.Add(OpenCategoryId);
        _disposables.Add(ShouldEjectGarrison);
        _disposables.Add(SingleEjectUnitHandle);
        _disposables.Add(PendingTrainCommand);
        _disposables.Add(PendingCancelTrainingCommand);
        _disposables.Add(HoveredBuildingSlot);
        _disposables.Add(PendingResearchCommand);
        _disposables.Add(PendingUpgradeCommand);
        _disposables.Add(PendingRepairCommand);
        _disposables.Add(PendingCancelRepairCommand);
        _disposables.Add(DestroyConfirmation);
        _disposables.Add(IsInBuildMode);
    }

    /// <summary>
    /// Enter build mode for the specified building type.
    /// </summary>
    public void EnterBuildMode(BuildingTypeId type)
    {
        BuildModeType.Value = type;
    }

    /// <summary>
    /// Exit build mode and clear preview.
    /// </summary>
    public void CancelBuildMode()
    {
        BuildModeType.Value = null;
        PlacementPreview.Value = null;
    }

    /// <summary>
    /// Update the placement preview position.
    /// </summary>
    public void UpdatePreview(int tileX, int tileY)
    {
        PlacementPreview.Value = (tileX, tileY);
    }

    /// <summary>
    /// Clear preview (e.g., mouse moved off map).
    /// </summary>
    public void ClearPreview()
    {
        PlacementPreview.Value = null;
    }

    /// <summary>
    /// Open a building category folder in the build UI.
    /// </summary>
    public void OpenCategory(int categoryId)
    {
        OpenCategoryId.Value = categoryId;
    }

    /// <summary>
    /// Close the current category and return to category list.
    /// </summary>
    public void CloseCategory()
    {
        OpenCategoryId.Value = null;
    }

    /// <summary>
    /// Request ejection of all units from selected garrison buildings.
    /// </summary>
    public void EjectGarrison()
    {
        ShouldEjectGarrison.Value = true;
    }

    /// <summary>
    /// Request ejection of a specific unit from its garrison.
    /// </summary>
    public void EjectSingleUnit(SimHandle unitHandle)
    {
        SingleEjectUnitHandle.Value = unitHandle;
    }

    /// <summary>
    /// Queue a unit training command. Will be read by GameInputManager on next poll.
    /// </summary>
    public void QueueTrainUnit(SimHandle buildingHandle, byte unitTypeId)
    {
        PendingTrainCommand.Value = (buildingHandle, unitTypeId);
    }

    /// <summary>
    /// Cancel training at a specific queue slot. Will be read by GameInputManager on next poll.
    /// </summary>
    public void CancelTraining(SimHandle buildingHandle, byte slotIndex)
    {
        PendingCancelTrainingCommand.Value = (buildingHandle, slotIndex);
    }

    /// <summary>
    /// Start or cancel research on selected building.
    /// 0 = cancel current research, 1-255 = start research item ID + 1.
    /// </summary>
    public void StartResearch(int researchCommand)
    {
        PendingResearchCommand.Value = researchCommand;
    }

    /// <summary>
    /// Request upgrade of the specified building.
    /// </summary>
    public void UpgradeBuilding(SimHandle buildingHandle)
    {
        PendingUpgradeCommand.Value = buildingHandle;
    }

    /// <summary>
    /// Show destroy confirmation modal with refund information for multiple buildings.
    /// </summary>
    public void RequestDestroyBuildings(List<SimHandle> buildings, int totalRefundGold, int totalRefundWood, int totalRefundStone, int totalRefundIron, int totalRefundOil)
    {
        DestroyConfirmation.Value = (buildings, totalRefundGold, totalRefundWood, totalRefundStone, totalRefundIron, totalRefundOil, buildings.Count);
    }

    /// <summary>
    /// Confirm destruction and queue all buildings for deletion.
    /// </summary>
    public void ConfirmDestroy()
    {
        if (DestroyConfirmation.Value.HasValue)
        {
            var buildings = DestroyConfirmation.Value.Value.buildings;
            foreach (var handle in buildings)
            {
                PendingDestroyQueue.Enqueue(handle);
            }
            DestroyConfirmation.Value = null;
        }
    }

    /// <summary>
    /// Cancel the destroy confirmation modal without destroying.
    /// </summary>
    public void CancelDestroy()
    {
        DestroyConfirmation.Value = null;
    }

    /// <summary>
    /// Request repair of the specified building.
    /// </summary>
    public void RepairBuilding(SimHandle buildingHandle)
    {
        PendingRepairCommand.Value = buildingHandle;
    }

    /// <summary>
    /// Request cancellation of ongoing repair.
    /// </summary>
    public void CancelRepair(SimHandle buildingHandle)
    {
        PendingCancelRepairCommand.Value = buildingHandle;
    }

    /// <summary>
    /// Enter attack-move command mode (next click issues attack-move).
    /// </summary>
    public void EnterAttackMoveMode()
    {
        CancelBuildMode();
        UnitCommandMode.Value = AppState.UnitCommandMode.AttackMove;
    }

    /// <summary>
    /// Enter patrol command mode (next click issues patrol to that location).
    /// </summary>
    public void EnterPatrolMode()
    {
        CancelBuildMode();
        UnitCommandMode.Value = AppState.UnitCommandMode.Patrol;
    }

    /// <summary>
    /// Cancel any active unit command mode.
    /// </summary>
    public void CancelUnitCommandMode()
    {
        UnitCommandMode.Value = AppState.UnitCommandMode.None;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
