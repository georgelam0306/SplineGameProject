using System;
using Raylib_cs;

namespace DieDrifterDie.Presentation.UI;

/// <summary>
/// Result of the bug report dialog interaction.
/// </summary>
public enum BugReportDialogAction
{
    None,
    Submit,
    Cancel
}

/// <summary>
/// Result returned from the bug report dialog.
/// </summary>
public readonly record struct BugReportDialogResult(BugReportDialogAction Action, string? Description);

/// <summary>
/// Modal dialog for submitting a bug report with optional description.
/// </summary>
public sealed class BugReportDialog
{
    private readonly char[] _descriptionBuffer = new char[1000];
    private int _descriptionLength;
    private bool _isOpen;

    private const int DialogWidth = 500;
    private const int DialogHeight = 350;
    private const int TextAreaHeight = 150;
    private const int Padding = 20;

    /// <summary>
    /// Whether the dialog is currently open.
    /// </summary>
    public bool IsOpen => _isOpen;

    /// <summary>
    /// Open the dialog and reset state.
    /// </summary>
    public void Open()
    {
        _isOpen = true;
        _descriptionLength = 0;
        Array.Clear(_descriptionBuffer);
    }

    /// <summary>
    /// Close the dialog.
    /// </summary>
    public void Close()
    {
        _isOpen = false;
    }

    /// <summary>
    /// Update and draw the dialog. Returns the action taken by the user.
    /// </summary>
    public BugReportDialogResult UpdateAndDraw()
    {
        if (!_isOpen)
            return new BugReportDialogResult(BugReportDialogAction.None, null);

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
        Raylib.DrawText("Report a Bug", dialogX + Padding, dialogY + Padding, 24, ImmediateModeUI.TextColor);

        // Instructions
        Raylib.DrawText("Describe what happened (optional):",
            dialogX + Padding, dialogY + 60, 16, ImmediateModeUI.TextColor);

        // Text area background
        var textAreaX = dialogX + Padding;
        var textAreaY = dialogY + 85;
        var textAreaWidth = DialogWidth - (Padding * 2);
        Raylib.DrawRectangle(textAreaX, textAreaY, textAreaWidth, TextAreaHeight,
            new Color(20, 20, 30, 255));
        Raylib.DrawRectangleLines(textAreaX, textAreaY, textAreaWidth, TextAreaHeight,
            ImmediateModeUI.PanelBorderColor);

        // Handle text input
        int charPressed;
        while ((charPressed = Raylib.GetCharPressed()) != 0)
        {
            // Accept printable ASCII characters
            if (charPressed >= 32 && charPressed < 127 && _descriptionLength < _descriptionBuffer.Length - 1)
            {
                _descriptionBuffer[_descriptionLength++] = (char)charPressed;
            }
        }

        // Handle backspace
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _descriptionLength > 0)
        {
            _descriptionLength--;
            _descriptionBuffer[_descriptionLength] = '\0';
        }

        // Handle enter key for newline (if we wanted multiline - skip for simplicity)
        // Keeping single-line for now

        // Draw entered text with cursor
        var text = new string(_descriptionBuffer, 0, _descriptionLength);
        DrawWrappedText(text + "_", textAreaX + 5, textAreaY + 5, textAreaWidth - 10, 16, ImmediateModeUI.TextColor);

        // Info text
        Raylib.DrawText("Your replay file and system info will be included.",
            dialogX + Padding, dialogY + 250, 14, new Color(150, 150, 150, 255));

        // Buttons
        var buttonY = dialogY + DialogHeight - 60;

        if (ImmediateModeUI.Button(dialogX + Padding, buttonY, 100, 40, "Cancel"))
        {
            Close();
            return new BugReportDialogResult(BugReportDialogAction.Cancel, null);
        }

        if (ImmediateModeUI.Button(dialogX + DialogWidth - 120, buttonY, 100, 40, "Submit"))
        {
            var description = _descriptionLength > 0 ? new string(_descriptionBuffer, 0, _descriptionLength) : null;
            Close();
            return new BugReportDialogResult(BugReportDialogAction.Submit, description);
        }

        // ESC to cancel
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            Close();
            return new BugReportDialogResult(BugReportDialogAction.Cancel, null);
        }

        return new BugReportDialogResult(BugReportDialogAction.None, null);
    }

    /// <summary>
    /// Draw text with simple word wrapping.
    /// </summary>
    private static void DrawWrappedText(string text, int x, int y, int maxWidth, int fontSize, Color color)
    {
        int lineY = y;
        int lineHeight = fontSize + 4;
        int currentX = x;
        int spaceWidth = Raylib.MeasureText(" ", fontSize);

        string[] words = text.Split(' ');

        foreach (var word in words)
        {
            int wordWidth = Raylib.MeasureText(word, fontSize);

            // Check if word fits on current line
            if (currentX + wordWidth > x + maxWidth && currentX > x)
            {
                // Move to next line
                lineY += lineHeight;
                currentX = x;
            }

            Raylib.DrawText(word, currentX, lineY, fontSize, color);
            currentX += wordWidth + spaceWidth;
        }
    }
}
