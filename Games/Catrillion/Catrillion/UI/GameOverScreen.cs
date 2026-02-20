using Raylib_cs;
using Catrillion.AppState;
using Catrillion.Simulation;

namespace Catrillion.UI;

/// <summary>
/// Game over screen showing victory/defeat and match statistics.
/// Displays survival time, units killed, buildings constructed, and resources gathered.
/// </summary>
public sealed class GameOverScreen
{
    private readonly RootStore _store;
    private readonly AppEventBus _eventBus;

    // Cached stat strings to avoid per-frame allocations
    private int _cachedSurvivalSeconds = -1;
    private int _cachedUnitsKilled = -1;
    private int _cachedBuildingsConstructed = -1;
    private int _cachedResourcesGathered = -1;
    private MatchOutcome _cachedOutcome = MatchOutcome.None;

    private string _survivalTimeText = "";
    private string _unitsKilledText = "";
    private string _buildingsConstructedText = "";
    private string _resourcesGatheredText = "";
    private string _titleText = "";

    // Colors
    private static readonly Color VictoryColor = new(80, 200, 80, 255);   // Green
    private static readonly Color DefeatColor = new(200, 80, 80, 255);    // Red
    private static readonly Color OverlayColor = new(0, 0, 0, 191);       // 75% opacity

    public GameOverScreen(RootStore store, AppEventBus eventBus)
    {
        _store = store;
        _eventBus = eventBus;
    }

    public void OnEnter()
    {
        // Reset cached values to force refresh
        _cachedSurvivalSeconds = -1;
        _cachedUnitsKilled = -1;
        _cachedBuildingsConstructed = -1;
        _cachedResourcesGathered = -1;
        _cachedOutcome = MatchOutcome.None;
    }

    public void OnExit()
    {
    }

    public void Update(float deltaTime)
    {
        // Update cached strings if values changed
        var game = _store.Game.CurrentGame;
        if (game == null) return;

        var outcome = game.Outcome;
        var (unitsKilled, buildingsConstructed, resourcesGathered) = game.GetMatchStats();

        int survivalFrames = game.CurrentFrame - game.MatchStartFrame;
        int survivalSeconds = survivalFrames / 60;

        if (outcome != _cachedOutcome)
        {
            _cachedOutcome = outcome;
            _titleText = outcome == MatchOutcome.Victory ? "VICTORY" : "DEFEAT";
        }

        if (survivalSeconds != _cachedSurvivalSeconds)
        {
            _cachedSurvivalSeconds = survivalSeconds;
            int minutes = survivalSeconds / 60;
            int seconds = survivalSeconds % 60;
            _survivalTimeText = $"{minutes:D2}:{seconds:D2}";
        }

        if (unitsKilled != _cachedUnitsKilled)
        {
            _cachedUnitsKilled = unitsKilled;
            _unitsKilledText = unitsKilled.ToString("N0");
        }

        if (buildingsConstructed != _cachedBuildingsConstructed)
        {
            _cachedBuildingsConstructed = buildingsConstructed;
            _buildingsConstructedText = buildingsConstructed.ToString("N0");
        }

        if (resourcesGathered != _cachedResourcesGathered)
        {
            _cachedResourcesGathered = resourcesGathered;
            _resourcesGatheredText = resourcesGathered.ToString("N0");
        }
    }

    public void Draw(float deltaTime)
    {
        var game = _store.Game.CurrentGame;

        // Draw game frozen in background (optional - could skip for performance)
        game?.Draw(deltaTime);

        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Dark overlay
        Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, OverlayColor);

        // Title (VICTORY / DEFEAT)
        int titleFontSize = 72;
        Color titleColor = _cachedOutcome == MatchOutcome.Victory ? VictoryColor : DefeatColor;
        int titleWidth = Raylib.MeasureText(_titleText, titleFontSize);
        int titleX = (screenWidth - titleWidth) / 2;
        int titleY = screenHeight / 4;

        // Title shadow
        Raylib.DrawText(_titleText, titleX + 3, titleY + 3, titleFontSize, new Color(0, 0, 0, 180));
        Raylib.DrawText(_titleText, titleX, titleY, titleFontSize, titleColor);

        // Stats panel
        int panelWidth = 400;
        int panelHeight = 220;
        int panelX = (screenWidth - panelWidth) / 2;
        int panelY = screenHeight / 2 - 40;

        ImmediateModeUI.DrawPanel(panelX, panelY, panelWidth, panelHeight, ImmediateModeUI.PanelColor, ImmediateModeUI.PanelBorderColor);

        // Stats header
        int headerFontSize = 24;
        string headerText = "Your Statistics";
        int headerWidth = Raylib.MeasureText(headerText, headerFontSize);
        Raylib.DrawText(headerText, panelX + (panelWidth - headerWidth) / 2, panelY + 15, headerFontSize, ImmediateModeUI.TextColor);

        // Divider line
        Raylib.DrawLine(panelX + 20, panelY + 50, panelX + panelWidth - 20, panelY + 50, ImmediateModeUI.PanelBorderColor);

        // Stats rows
        int statFontSize = 20;
        int labelX = panelX + 30;
        int valueX = panelX + panelWidth - 120;
        int rowHeight = 35;
        int startY = panelY + 70;

        DrawStatRow("Survival Time:", _survivalTimeText, labelX, valueX, startY, statFontSize);
        DrawStatRow("Units Killed:", _unitsKilledText, labelX, valueX, startY + rowHeight, statFontSize);
        DrawStatRow("Buildings Built:", _buildingsConstructedText, labelX, valueX, startY + rowHeight * 2, statFontSize);
        DrawStatRow("Resources:", _resourcesGatheredText, labelX, valueX, startY + rowHeight * 3, statFontSize);

        // Return to Menu button
        int buttonWidth = 200;
        int buttonHeight = 50;
        int buttonX = (screenWidth - buttonWidth) / 2;
        int buttonY = panelY + panelHeight + 30;

        if (ImmediateModeUI.Button(buttonX, buttonY, buttonWidth, buttonHeight, "Return to Menu"))
        {
            _eventBus.PublishReturnToMainMenu();
        }
    }

    private static void DrawStatRow(string label, string value, int labelX, int valueX, int y, int fontSize)
    {
        Raylib.DrawText(label, labelX, y, fontSize, ImmediateModeUI.TextColor);
        int valueWidth = Raylib.MeasureText(value, fontSize);
        Raylib.DrawText(value, valueX + 80 - valueWidth, y, fontSize, ImmediateModeUI.TextColor);
    }
}
