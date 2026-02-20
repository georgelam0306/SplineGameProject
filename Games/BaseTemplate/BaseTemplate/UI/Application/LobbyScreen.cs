using System;
using Raylib_cs;
using BaseTemplate.GameApp.AppState;
using Networking;

namespace BaseTemplate.Presentation.UI;

/// <summary>
/// Lobby screen showing connected players, ready states, and controls.
/// Uses P2P LobbyStore for state - Orleans is only used for discovery/NAT punch.
/// Publishes events to AppEventBus; LobbyCoordinator subscribes and handles actions.
/// </summary>
public sealed class LobbyScreen
{
    private readonly RootStore _store;
    private readonly AppEventBus _eventBus;
    private bool _autoReadyTriggered;

    private const int SlotWidth = 500;
    private const int SlotHeight = 50;
    private const int SlotSpacing = 5;
    private const int ButtonWidth = 150;
    private const int ButtonHeight = 45;

    public LobbyScreen(RootStore store, AppEventBus eventBus)
    {
        _store = store;
        _eventBus = eventBus;
    }

    public void OnEnter()
    {
        _autoReadyTriggered = false;
    }

    public void OnExit()
    {
    }

    public void Update(float deltaTime)
    {
        // Auto-ready for skip-menu mode (clients)
        if (!_autoReadyTriggered && IsSkipMenuMode())
        {
            var lobby = _store.Lobby.State.Value;
            bool isLocalReady = lobby.GetClient(_store.App.LocalClient.ClientId)?.IsReady ?? false;

            if (!isLocalReady)
            {
                _eventBus.PublishReadyToggled();
            }
            _autoReadyTriggered = true;
        }
    }

    public void Draw(float deltaTime)
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        var localClient = _store.App.LocalClient;
        bool isCoordinator = _store.Network.IsCoordinator;
        int coordinatorSlot = _store.Network.CoordinatorSlot.Value;
        var lobby = _store.Lobby.State.Value;

        // Title
        string title = isCoordinator ? "LOBBY (HOST)" : "LOBBY";
        ImmediateModeUI.DrawCenteredText(title, 50, 40, Color.White);

        // Player count
        string playerCountText = $"Players: {lobby.PlayerCount}/{LobbyState.MaxPlayers}";
        ImmediateModeUI.DrawCenteredText(playerCountText, 100, 20, ImmediateModeUI.TextColor);

        // Ready count
        string readyCountText = $"Ready: {lobby.GetReadyCount()}/{lobby.PlayerCount}";
        ImmediateModeUI.DrawCenteredText(readyCountText, 125, 20, ImmediateModeUI.TextColor);

        // Player slots panel
        int panelX = (screenWidth - SlotWidth - 40) / 2;
        int panelY = 160;
        int panelHeight = (SlotHeight + SlotSpacing) * LobbyState.MaxPlayers + 20;

        ImmediateModeUI.DrawPanel(panelX, panelY, SlotWidth + 40, panelHeight, null, ImmediateModeUI.PanelBorderColor);

        // Draw player slots
        int slotX = panelX + 20;
        int slotY = panelY + 10;

        for (int i = 0; i < LobbyState.MaxPlayers; i++)
        {
            // Find client in this slot
            ConnectedClient? clientInSlot = null;
            for (int j = 0; j < lobby.Clients.Length; j++)
            {
                if (lobby.Clients[j].PlayerSlot == i)
                {
                    clientInSlot = lobby.Clients[j];
                    break;
                }
            }

            if (clientInSlot.HasValue)
            {
                var client = clientInSlot.Value;
                bool isLocal = client.ClientId == localClient.ClientId;
                bool isClientCoordinator = client.PlayerSlot == coordinatorSlot;
                ImmediateModeUI.DrawPlayerSlot(
                    slotX, slotY + i * (SlotHeight + SlotSpacing),
                    SlotWidth, SlotHeight,
                    i, client.DisplayName, client.IsReady, isClientCoordinator, isLocal
                );
            }
            else
            {
                // Empty slot
                int y = slotY + i * (SlotHeight + SlotSpacing);
                ImmediateModeUI.DrawPanel(slotX, y, SlotWidth, SlotHeight, null, ImmediateModeUI.PanelBorderColor);
                string emptyText = $"#{i + 1} - Empty";
                Raylib.DrawText(emptyText, slotX + 10, y + (SlotHeight - 20) / 2, 20, new Color(80, 80, 80, 255));
            }
        }

        // Buttons at the bottom
        int buttonY = panelY + panelHeight + 30;
        int buttonAreaWidth = ButtonWidth * 3 + 40;
        int buttonStartX = (screenWidth - buttonAreaWidth) / 2;

        // Leave button
        if (ImmediateModeUI.Button(buttonStartX, buttonY, ButtonWidth, ButtonHeight, "Leave"))
        {
            _eventBus.PublishLobbyLeftRequested();
        }

        // Ready toggle button
        var localClientInfo = lobby.GetClient(localClient.ClientId);
        string readyButtonText = localClientInfo?.IsReady == true ? "Not Ready" : "Ready";
        if (ImmediateModeUI.Button(buttonStartX + ButtonWidth + 20, buttonY, ButtonWidth, ButtonHeight, readyButtonText))
        {
            _eventBus.PublishReadyToggled();
        }

        // Start Game button (coordinator only, when all ready)
        bool canStart = _store.Lobby.CanStartGame.CurrentValue;
        if (ImmediateModeUI.Button(buttonStartX + (ButtonWidth + 20) * 2, buttonY, ButtonWidth, ButtonHeight, "Start Game", canStart))
        {
            _eventBus.PublishMatchStartRequested();
        }

        // Instructions
        int instructionsY = buttonY + ButtonHeight + 30;
        if (isCoordinator)
        {
            ImmediateModeUI.DrawCenteredText("All players must be ready before starting", instructionsY, 16, new Color(150, 150, 150, 255));
        }
        else
        {
            ImmediateModeUI.DrawCenteredText("Waiting for coordinator to start the game...", instructionsY, 16, new Color(150, 150, 150, 255));
        }
    }

    private static bool IsSkipMenuMode()
    {
        string? netPlayers = Environment.GetEnvironmentVariable("NET_PLAYERS");
        return int.TryParse(netPlayers, out int count) && count > 1;
    }
}
