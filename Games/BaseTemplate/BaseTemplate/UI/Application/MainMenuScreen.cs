using System;
using Raylib_cs;
using BaseTemplate.GameApp.AppState;

namespace BaseTemplate.Presentation.UI;

/// <summary>
/// Main menu screen with Local Play and Multiplayer options.
/// Publishes events to AppEventBus; coordinators subscribe and handle actions.
/// </summary>
public sealed class MainMenuScreen
{
    private readonly RootStore _store;
    private readonly AppEventBus _eventBus;
    private readonly BugReportDialog _bugReportDialog;
    private bool _showBugReportDialog;

    private const int ButtonWidth = 200;
    private const int ButtonHeight = 50;
    private const int ButtonSpacing = 20;

    public MainMenuScreen(RootStore store, AppEventBus eventBus)
    {
        _store = store;
        _eventBus = eventBus;
        _bugReportDialog = new BugReportDialog();
    }

    public void OnEnter()
    {
        _eventBus.PublishMainMenuEntered();
        _showBugReportDialog = false;
    }

    public void OnExit()
    {
        _showBugReportDialog = false;
    }

    public void Update(float deltaTime)
    {
        // No per-frame update logic needed
    }

    public void Draw(float deltaTime)
    {
        if (_showBugReportDialog)
        {
            DrawBugReportDialog();
        }
        else
        {
            DrawMainMenu();
        }
    }

    private void DrawMainMenu()
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Title
        ImmediateModeUI.DrawCenteredText("CATRILLION", 100, 60, Color.White);
        ImmediateModeUI.DrawCenteredText("Multiplayer Prototype", 170, 24, ImmediateModeUI.TextColor);

        // Button layout - centered
        int buttonX = (screenWidth - ButtonWidth) / 2;
        int startY = screenHeight / 2 - 80;

        // Local Play button
        if (ImmediateModeUI.Button(buttonX, startY, ButtonWidth, ButtonHeight, "Local Play"))
        {
            _eventBus.PublishLocalGameRequested();
        }

        // Multiplayer button (lobby browser)
        if (ImmediateModeUI.Button(buttonX, startY + ButtonHeight + ButtonSpacing, ButtonWidth, ButtonHeight, "Multiplayer"))
        {
            _eventBus.PublishLobbyBrowserRequested();
        }

        // Exit to Desktop button
        if (ImmediateModeUI.Button(buttonX, startY + (ButtonHeight + ButtonSpacing) * 2, ButtonWidth, ButtonHeight, "Exit to Desktop"))
        {
            Environment.Exit(0);
        }

        // Report Bug button (bottom left)
        if (ImmediateModeUI.Button(20, screenHeight - 60, 130, 40, "Report Bug"))
        {
            _showBugReportDialog = true;
            _bugReportDialog.Open();
        }
    }

    private void DrawBugReportDialog()
    {
        var result = _bugReportDialog.UpdateAndDraw();

        switch (result.Action)
        {
            case BugReportDialogAction.Submit:
                _eventBus.PublishBugReportRequested(result.Description);
                _showBugReportDialog = false;
                break;

            case BugReportDialogAction.Cancel:
                _showBugReportDialog = false;
                break;
        }
    }
}
