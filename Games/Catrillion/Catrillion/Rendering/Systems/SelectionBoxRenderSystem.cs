using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.AppState;
using Catrillion.Input;
using Catrillion.Rendering.Services;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders the selection box while dragging (screen space).
/// Uses player color for visual identification in co-op.
/// Should run after WorldCameraEndSystem.
/// </summary>
public sealed class SelectionBoxRenderSystem : BaseSystem
{
    private readonly GameInputManager _inputManager;
    private readonly NetworkStore _networkStore;

    public SelectionBoxRenderSystem(GameInputManager inputManager, NetworkStore networkStore)
    {
        _inputManager = inputManager;
        _networkStore = networkStore;
    }

    protected override void OnUpdateGroup()
    {
        if (!_inputManager.IsSelecting) return;

        var start = _inputManager.SelectStartScreen;
        var end = _inputManager.SelectEndScreen;

        // Calculate rectangle bounds (handle any drag direction)
        int x = (int)MathF.Min(start.X, end.X);
        int y = (int)MathF.Min(start.Y, end.Y);
        int width = (int)MathF.Abs(end.X - start.X);
        int height = (int)MathF.Abs(end.Y - start.Y);

        // Get local player's color
        int localPlayerId = _networkStore.LocalSlot.Value;
        if (localPlayerId < 0) localPlayerId = 0;
        var playerColor = PlayerColorService.GetPlayerColor(localPlayerId);

        // Draw semi-transparent fill with player color
        Raylib.DrawRectangle(x, y, width, height, PlayerColorService.GetPlayerColorWithAlpha(localPlayerId, 30));
        // Draw outline in player color
        Raylib.DrawRectangleLines(x, y, width, height, playerColor);
    }
}
