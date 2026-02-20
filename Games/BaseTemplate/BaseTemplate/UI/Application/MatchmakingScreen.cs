using System;
using Raylib_cs;
using BaseTemplate.GameApp.AppState;
using Networking;

namespace BaseTemplate.Presentation.UI;

/// <summary>
/// Matchmaking screen for browsing and creating online lobbies.
/// Publishes events to AppEventBus; MatchmakingCoordinator subscribes and handles actions.
/// Note: Poll() and heartbeat are handled by MatchmakingPoller service.
/// </summary>
public sealed class MatchmakingScreen
{
    private readonly RootStore _store;
    private readonly AppEventBus _eventBus;
    private int _selectedLobbyIndex = -1;
    private bool _showCreateDialog;
    private float _autoRefreshTimer;

    private const int LobbyListWidth = 600;
    private const int LobbyItemHeight = 50;
    private const int LobbyItemSpacing = 5;
    private const int MaxVisibleLobbies = 8;
    private const int ButtonWidth = 140;
    private const int ButtonHeight = 45;
    private const float AutoRefreshInterval = 5.0f;
    private const int PlayerListItemHeight = 40;
    private const int KickButtonWidth = 60;

    public MatchmakingScreen(RootStore store, AppEventBus eventBus)
    {
        _store = store;
        _eventBus = eventBus;
    }

    public void OnEnter()
    {
        _selectedLobbyIndex = -1;
        _showCreateDialog = false;
        _autoRefreshTimer = 0;
        _eventBus.PublishLobbiesRefreshRequested();
    }

    public void OnExit()
    {
        _showCreateDialog = false;
    }

    public void Update(float deltaTime)
    {
        var state = _store.Matchmaking.State.Value;

        // Auto-refresh lobby list periodically when idle
        _autoRefreshTimer += deltaTime;
        if (_autoRefreshTimer >= AutoRefreshInterval)
        {
            _autoRefreshTimer = 0;
            if (state == MatchmakingState.Idle && !_showCreateDialog)
            {
                _eventBus.PublishLobbiesRefreshRequested();
            }
        }
    }

    public void Draw(float deltaTime)
    {
        var state = _store.Matchmaking.State.Value;

        switch (state)
        {
            case MatchmakingState.Idle:
                if (_showCreateDialog)
                {
                    DrawCreateDialog();
                }
                else
                {
                    DrawLobbyBrowser();
                }
                break;

            case MatchmakingState.CreatingLobby:
                DrawLoadingState("Creating lobby...");
                break;

            case MatchmakingState.InLobby:
                DrawInLobbyState();
                break;

            case MatchmakingState.JoiningLobby:
                DrawLoadingState("Joining lobby...");
                break;

            case MatchmakingState.Error:
                DrawErrorState();
                break;
        }
    }

    private void DrawLobbyBrowser()
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Title
        ImmediateModeUI.DrawCenteredText("FIND MATCH", 50, 40, Color.White);

        // Subtitle with lobby count
        var lobbies = _store.Matchmaking.AvailableLobbies.Value;
        string subtitle = _store.Matchmaking.IsRefreshing.Value
            ? "Refreshing..."
            : $"{lobbies.Count} lobbies available";
        ImmediateModeUI.DrawCenteredText(subtitle, 100, 20, ImmediateModeUI.TextColor);

        // Lobby list panel
        int panelX = (screenWidth - LobbyListWidth - 40) / 2;
        int panelY = 140;
        int panelHeight = (LobbyItemHeight + LobbyItemSpacing) * MaxVisibleLobbies + 20;

        ImmediateModeUI.DrawPanel(panelX, panelY, LobbyListWidth + 40, panelHeight, null, ImmediateModeUI.PanelBorderColor);

        // Draw lobby items
        int itemX = panelX + 20;
        int itemY = panelY + 10;

        for (int i = 0; i < MaxVisibleLobbies; i++)
        {
            int y = itemY + i * (LobbyItemHeight + LobbyItemSpacing);

            if (i < lobbies.Count)
            {
                var lobby = lobbies[i];
                bool isSelected = i == _selectedLobbyIndex;
                bool isHovered = IsMouseInRect(itemX, y, LobbyListWidth, LobbyItemHeight);

                // Background
                Color bgColor = isSelected ? new Color(60, 80, 60, 255)
                    : isHovered ? new Color(50, 50, 60, 255)
                    : new Color(40, 40, 50, 255);
                Raylib.DrawRectangle(itemX, y, LobbyListWidth, LobbyItemHeight, bgColor);
                Raylib.DrawRectangleLines(itemX, y, LobbyListWidth, LobbyItemHeight, ImmediateModeUI.PanelBorderColor);

                // Host name
                Raylib.DrawText(lobby.HostName, itemX + 15, y + 8, 22, Color.White);

                // Player count
                string playerCount = $"{lobby.PlayerCount}/{lobby.MaxPlayers} players";
                int playerCountWidth = Raylib.MeasureText(playerCount, 18);
                Raylib.DrawText(playerCount, itemX + LobbyListWidth - playerCountWidth - 15, y + 15, 18, ImmediateModeUI.TextColor);

                // Click to select
                if (isHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    _selectedLobbyIndex = i;
                }

                // Double-click to join
                if (isSelected && isHovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    // Check if this is a "double-click" (selected then clicked again)
                    // For simplicity, just use the Join button
                }
            }
            else
            {
                // Empty slot
                Raylib.DrawRectangle(itemX, y, LobbyListWidth, LobbyItemHeight, new Color(30, 30, 35, 255));
                Raylib.DrawRectangleLines(itemX, y, LobbyListWidth, LobbyItemHeight, new Color(50, 50, 55, 255));
            }
        }

        // Buttons below the list
        int buttonY = panelY + panelHeight + 20;
        int totalButtonWidth = ButtonWidth * 4 + 60;
        int buttonStartX = (screenWidth - totalButtonWidth) / 2;

        // Back button
        if (ImmediateModeUI.Button(buttonStartX, buttonY, ButtonWidth, ButtonHeight, "Back"))
        {
            _eventBus.PublishMatchmakingLeftRequested();
        }

        // Refresh button
        bool canRefresh = !_store.Matchmaking.IsRefreshing.Value;
        if (ImmediateModeUI.Button(buttonStartX + ButtonWidth + 20, buttonY, ButtonWidth, ButtonHeight, "Refresh", canRefresh))
        {
            _eventBus.PublishLobbiesRefreshRequested();
        }

        // Create Lobby button
        if (ImmediateModeUI.Button(buttonStartX + (ButtonWidth + 20) * 2, buttonY, ButtonWidth, ButtonHeight, "Create Lobby"))
        {
            _showCreateDialog = true;
        }

        // Join button (enabled when lobby selected)
        bool canJoin = _selectedLobbyIndex >= 0 && _selectedLobbyIndex < lobbies.Count;
        if (ImmediateModeUI.Button(buttonStartX + (ButtonWidth + 20) * 3, buttonY, ButtonWidth, ButtonHeight, "Join", canJoin))
        {
            var selectedLobby = lobbies[_selectedLobbyIndex];
            _eventBus.PublishLobbyJoinRequested(selectedLobby.LobbyId, _store.App.LocalClient.DisplayName);
        }

        // Instructions
        int instructionsY = buttonY + ButtonHeight + 20;
        ImmediateModeUI.DrawCenteredText("Select a lobby and click Join, or create your own", instructionsY, 16, new Color(120, 120, 120, 255));
    }

    private void DrawCreateDialog()
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Title
        ImmediateModeUI.DrawCenteredText("CREATE LOBBY", 50, 40, Color.White);

        // Panel
        int panelWidth = 400;
        int panelHeight = 200;
        int panelX = (screenWidth - panelWidth) / 2;
        int panelY = (screenHeight - panelHeight) / 2 - 50;

        ImmediateModeUI.DrawPanel(panelX, panelY, panelWidth, panelHeight, null, ImmediateModeUI.PanelBorderColor);

        // Panel title
        Raylib.DrawText("Create New Lobby", panelX + 20, panelY + 15, 22, Color.White);

        // Display name info
        string displayName = _store.App.LocalClient.DisplayName;
        Raylib.DrawText("Host Name:", panelX + 20, panelY + 50, 20, ImmediateModeUI.TextColor);
        Raylib.DrawText(displayName, panelX + 130, panelY + 50, 20, Color.White);

        // Max players info
        Raylib.DrawText("Max Players:", panelX + 20, panelY + 85, 20, ImmediateModeUI.TextColor);
        Raylib.DrawText($"{MatchmakingConfig.MaxPlayers}", panelX + 150, panelY + 85, 20, Color.White);

        // Port info
        Raylib.DrawText("Port:", panelX + 20, panelY + 120, 20, ImmediateModeUI.TextColor);
        Raylib.DrawText($"{MatchmakingConfig.DefaultPort}", panelX + 80, panelY + 120, 20, Color.White);

        // Buttons
        int buttonY = panelY + panelHeight - 60;
        int buttonSpacing = 20;
        int buttonX = panelX + (panelWidth - ButtonWidth * 2 - buttonSpacing) / 2;

        if (ImmediateModeUI.Button(buttonX, buttonY, ButtonWidth, ButtonHeight, "Cancel"))
        {
            _showCreateDialog = false;
        }

        if (ImmediateModeUI.Button(buttonX + ButtonWidth + buttonSpacing, buttonY, ButtonWidth, ButtonHeight, "Create"))
        {
            _showCreateDialog = false;
            _eventBus.PublishLobbyCreateRequested(displayName);
        }
    }

    private void DrawInLobbyState()
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Title
        ImmediateModeUI.DrawCenteredText("LOBBY", 50, 40, Color.White);

        var lobby = _store.Matchmaking.CurrentLobby.Value;
        bool isHost = _store.Matchmaking.IsHost.Value;
        var players = _store.Matchmaking.LobbyPlayers.Value;

        if (lobby.HasValue)
        {
            // Invite code prominently displayed
            string inviteCode = lobby.Value.LobbyId;
            ImmediateModeUI.DrawCenteredText($"INVITE CODE: {inviteCode}", 100, 32, new Color(100, 255, 100, 255));
            ImmediateModeUI.DrawCenteredText("Share this code with friends to join", 140, 16, ImmediateModeUI.TextColor);

            // Player list panel
            int panelWidth = 500;
            int maxPlayers = Math.Max(4, players.Count);
            int panelHeight = 60 + maxPlayers * (PlayerListItemHeight + 5);
            int panelX = (screenWidth - panelWidth) / 2;
            int panelY = 180;

            ImmediateModeUI.DrawPanel(panelX, panelY, panelWidth, panelHeight, null, ImmediateModeUI.PanelBorderColor);

            // Panel title
            Raylib.DrawText($"Players ({players.Count}/8)", panelX + 20, panelY + 15, 22, Color.White);

            // Draw player list
            int playerY = panelY + 50;
            foreach (var player in players)
            {
                // Player row background
                Color rowBg = player.IsHost ? new Color(50, 70, 50, 255) : new Color(40, 40, 50, 255);
                Raylib.DrawRectangle(panelX + 15, playerY, panelWidth - 30, PlayerListItemHeight, rowBg);

                // Player name
                string displayName = player.DisplayName;
                if (player.IsHost)
                {
                    displayName += " (Host)";
                }
                Raylib.DrawText(displayName, panelX + 25, playerY + 10, 20, Color.White);

                // Kick button (host only, can't kick self)
                if (isHost && !player.IsHost)
                {
                    int kickX = panelX + panelWidth - KickButtonWidth - 25;
                    int kickY = playerY + 5;
                    if (ImmediateModeUI.Button(kickX, kickY, KickButtonWidth, PlayerListItemHeight - 10, "Kick"))
                    {
                        _eventBus.PublishPlayerKickRequested(player.PlayerId);
                    }
                }

                playerY += PlayerListItemHeight + 5;
            }

            // Buttons below player list
            int buttonY = panelY + panelHeight + 20;
            int buttonX = (screenWidth - ButtonWidth * 2 - 20) / 2;

            if (ImmediateModeUI.Button(buttonX, buttonY, ButtonWidth, ButtonHeight, "Leave"))
            {
                _eventBus.PublishMatchmakingLobbyLeftRequested();
            }

            if (isHost)
            {
                if (ImmediateModeUI.Button(buttonX + ButtonWidth + 20, buttonY, ButtonWidth, ButtonHeight, "Start Hosting"))
                {
                    _eventBus.PublishHostingStartRequested();
                }
            }

            // Instructions
            int instructionsY = buttonY + ButtonHeight + 20;
            if (isHost)
            {
                ImmediateModeUI.DrawCenteredText("Click 'Start Hosting' when all players have joined", instructionsY, 16, new Color(120, 120, 120, 255));
            }
            else
            {
                ImmediateModeUI.DrawCenteredText("Waiting for host to start the game...", instructionsY, 16, new Color(120, 120, 120, 255));
            }
        }
    }

    private void DrawLoadingState(string message)
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        ImmediateModeUI.DrawCenteredText(message, screenHeight / 2 - 20, 30, Color.White);

        // Simple animated dots
        int dots = (int)(Raylib.GetTime() * 2) % 4;
        string dotStr = new string('.', dots);
        ImmediateModeUI.DrawCenteredText(dotStr, screenHeight / 2 + 20, 30, ImmediateModeUI.TextColor);
    }

    private void DrawErrorState()
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Title
        ImmediateModeUI.DrawCenteredText("ERROR", 50, 40, new Color(255, 100, 100, 255));

        // Error message panel
        int panelWidth = 500;
        int panelHeight = 150;
        int panelX = (screenWidth - panelWidth) / 2;
        int panelY = screenHeight / 2 - 100;

        ImmediateModeUI.DrawPanel(panelX, panelY, panelWidth, panelHeight, null, new Color(150, 50, 50, 255));

        // Error message
        string error = _store.Matchmaking.ErrorMessage.Value;
        if (string.IsNullOrEmpty(error))
        {
            error = "An unknown error occurred";
        }

        // Word wrap (simple)
        int textY = panelY + 30;
        Raylib.DrawText(error, panelX + 20, textY, 18, Color.White);

        // OK button
        int buttonX = (screenWidth - ButtonWidth) / 2;
        int buttonY = panelY + panelHeight + 20;

        if (ImmediateModeUI.Button(buttonX, buttonY, ButtonWidth, ButtonHeight, "OK"))
        {
            _eventBus.PublishMatchmakingLeftRequested();
        }
    }

    private static bool IsMouseInRect(int x, int y, int width, int height)
    {
        var mousePos = Raylib.GetMousePosition();
        return mousePos.X >= x && mousePos.X < x + width &&
               mousePos.Y >= y && mousePos.Y < y + height;
    }
}
