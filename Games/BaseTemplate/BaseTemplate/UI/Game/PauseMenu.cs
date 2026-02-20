using Raylib_cs;

namespace BaseTemplate.Presentation.UI;

/// <summary>
/// Result of the pause menu interaction.
/// </summary>
public enum PauseMenuAction
{
    None,
    Resume,
    MainMenu,
    ExitToDesktop
}

/// <summary>
/// In-game pause menu with options to resume, return to main menu, or exit.
/// </summary>
public sealed class PauseMenu
{
    private bool _isOpen;

    private const int DialogWidth = 300;
    private const int DialogHeight = 250;
    private const int ButtonWidth = 200;
    private const int ButtonHeight = 45;
    private const int ButtonSpacing = 15;
    private const int Padding = 30;

    /// <summary>
    /// Whether the pause menu is currently open.
    /// </summary>
    public bool IsOpen => _isOpen;

    /// <summary>
    /// Open the pause menu.
    /// </summary>
    public void Open()
    {
        _isOpen = true;
    }

    /// <summary>
    /// Close the pause menu.
    /// </summary>
    public void Close()
    {
        _isOpen = false;
    }

    /// <summary>
    /// Toggle the pause menu open/closed state.
    /// </summary>
    public void Toggle()
    {
        _isOpen = !_isOpen;
    }

    /// <summary>
    /// Update and draw the pause menu. Returns the action taken by the user.
    /// </summary>
    public PauseMenuAction UpdateAndDraw()
    {
        if (!_isOpen)
            return PauseMenuAction.None;

        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();
        int dialogX = (screenWidth - DialogWidth) / 2;
        int dialogY = (screenHeight - DialogHeight) / 2;

        // Draw backdrop (semi-transparent)
        Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(0, 0, 0, 150));

        // Draw dialog panel
        ImmediateModeUI.DrawPanel(dialogX, dialogY, DialogWidth, DialogHeight,
            ImmediateModeUI.PanelColor, ImmediateModeUI.PanelBorderColor);

        // Title
        ImmediateModeUI.DrawCenteredText("PAUSED", dialogY + Padding, 28, Color.White);

        // Button layout - centered in dialog
        int buttonX = dialogX + (DialogWidth - ButtonWidth) / 2;
        int startY = dialogY + 80;

        // Resume button
        if (ImmediateModeUI.Button(buttonX, startY, ButtonWidth, ButtonHeight, "Resume"))
        {
            _isOpen = false;
            return PauseMenuAction.Resume;
        }

        // Main Menu button
        if (ImmediateModeUI.Button(buttonX, startY + ButtonHeight + ButtonSpacing, ButtonWidth, ButtonHeight, "Main Menu"))
        {
            _isOpen = false;
            return PauseMenuAction.MainMenu;
        }

        // Exit to Desktop button
        if (ImmediateModeUI.Button(buttonX, startY + (ButtonHeight + ButtonSpacing) * 2, ButtonWidth, ButtonHeight, "Exit to Desktop"))
        {
            return PauseMenuAction.ExitToDesktop;
        }

        return PauseMenuAction.None;
    }
}
