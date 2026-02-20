using Raylib_cs;
using Catrillion.AppState;

namespace Catrillion.UI;

/// <summary>
/// Loading screen shown while Game is being created and players confirm loaded.
/// Displays loading progress only - LoadingManager handles game creation.
/// </summary>
public sealed class LoadingScreen
{
    private readonly RootStore _store;

    public LoadingScreen(RootStore store)
    {
        _store = store;
    }

    public void OnEnter()
    {
    }

    public void OnExit()
    {
    }

    public void Update(float deltaTime)
    {
    }

    public void Draw(float deltaTime)
    {
        int screenHeight = Raylib.GetScreenHeight();

        var lobby = _store.Lobby.State.Value;

        // Title
        ImmediateModeUI.DrawCenteredText("LOADING", screenHeight / 2 - 80, 48, Color.White);

        // Loading status
        int loadedCount = lobby.GetLoadedCount();
        int totalCount = lobby.PlayerCount;
        string statusText = $"Players loaded: {loadedCount}/{totalCount}";
        ImmediateModeUI.DrawCenteredText(statusText, screenHeight / 2 - 20, 24, ImmediateModeUI.TextColor);

        // Progress bar
        int screenWidth = Raylib.GetScreenWidth();
        int barWidth = 400;
        int barHeight = 30;
        int barX = (screenWidth - barWidth) / 2;
        int barY = screenHeight / 2 + 20;
        ImmediateModeUI.DrawProgressBar(barX, barY, barWidth, barHeight, loadedCount, totalCount);

        // Waiting message
        if (loadedCount < totalCount)
        {
            ImmediateModeUI.DrawCenteredText("Waiting for all players to load...", screenHeight / 2 + 80, 16, new Color(150, 150, 150, 255));
        }
        else
        {
            ImmediateModeUI.DrawCenteredText("All players loaded! Starting countdown...", screenHeight / 2 + 80, 16, ImmediateModeUI.ReadyColor);
        }
    }
}
