using System.Text;
using Raylib_cs;
using Catrillion.OnlineServices;
using Networking;

namespace Catrillion.UI;

/// <summary>
/// Action returned from the connection dialog.
/// </summary>
public enum ConnectionDialogAction
{
    None,
    Connect,
    Cancel
}

/// <summary>
/// Result from the connection dialog.
/// </summary>
public readonly struct ConnectionDialogResult
{
    public ConnectionDialogAction Action { get; init; }
    public string HostAddress { get; init; }
    public int Port { get; init; }
}

/// <summary>
/// Dialog for entering host IP address to join a game.
/// </summary>
public sealed class ConnectionDialog
{
    private const int DialogWidth = 400;
    private const int DialogHeight = 200;
    private const int InputWidth = 260;
    private const int InputHeight = 36;
    private const int PortInputWidth = 80;
    private const int ButtonWidth = 120;
    private const int ButtonHeight = 40;

    private readonly StringBuilder _addressBuffer = new("127.0.0.1");
    private readonly StringBuilder _portBuffer = new(NetworkService.DefaultPort.ToString());
    private bool _addressFocused = true; // Start with address focused

    /// <summary>
    /// Updates and draws the connection dialog. Returns the action taken.
    /// </summary>
    public ConnectionDialogResult UpdateAndDraw()
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Center the dialog
        int dialogX = (screenWidth - DialogWidth) / 2;
        int dialogY = (screenHeight - DialogHeight) / 2;

        // Dim background
        Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(0, 0, 0, 180));

        // Dialog panel
        ImmediateModeUI.DrawPanel(dialogX, dialogY, DialogWidth, DialogHeight, null, ImmediateModeUI.PanelBorderColor);

        // Title
        Raylib.DrawText("Join Game", dialogX + 20, dialogY + 15, 24, ImmediateModeUI.TextColor);

        // IP Address label and input
        int labelY = dialogY + 60;
        Raylib.DrawText("Host IP:", dialogX + 20, labelY, 18, ImmediateModeUI.TextColor);

        int inputY = labelY + 25;
        bool addressClicked = DrawTextInput(dialogX + 20, inputY, InputWidth, InputHeight, _addressBuffer, _addressFocused);
        if (addressClicked) _addressFocused = true;

        // Port label and input
        Raylib.DrawText("Port:", dialogX + 20 + InputWidth + 20, labelY, 18, ImmediateModeUI.TextColor);
        bool portClicked = DrawTextInput(dialogX + 20 + InputWidth + 20, inputY, PortInputWidth, InputHeight, _portBuffer, !_addressFocused);
        if (portClicked) _addressFocused = false;

        // Handle tab to switch focus
        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            _addressFocused = !_addressFocused;
        }

        // Handle keyboard input for focused field
        HandleTextInput(_addressFocused ? _addressBuffer : _portBuffer, _addressFocused ? 45 : 5);

        // Buttons
        int buttonY = dialogY + DialogHeight - ButtonHeight - 20;
        int connectX = dialogX + DialogWidth / 2 - ButtonWidth - 10;
        int cancelX = dialogX + DialogWidth / 2 + 10;

        var result = new ConnectionDialogResult
        {
            Action = ConnectionDialogAction.None,
            HostAddress = _addressBuffer.ToString(),
            Port = int.TryParse(_portBuffer.ToString(), out int port) ? port : NetworkService.DefaultPort
        };

        if (ImmediateModeUI.Button(connectX, buttonY, ButtonWidth, ButtonHeight, "Connect"))
        {
            result = result with { Action = ConnectionDialogAction.Connect };
        }

        if (ImmediateModeUI.Button(cancelX, buttonY, ButtonWidth, ButtonHeight, "Cancel"))
        {
            result = result with { Action = ConnectionDialogAction.Cancel };
        }

        // Handle Enter to connect
        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            result = result with { Action = ConnectionDialogAction.Connect };
        }

        // Handle Escape to cancel
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            result = result with { Action = ConnectionDialogAction.Cancel };
        }

        return result;
    }

    /// <summary>
    /// Draws a text input field. Returns true if clicked.
    /// </summary>
    private static bool DrawTextInput(int x, int y, int width, int height, StringBuilder buffer, bool focused)
    {
        var rect = new Rectangle(x, y, width, height);
        var mousePos = Raylib.GetMousePosition();
        bool isHovered = Raylib.CheckCollisionPointRec(mousePos, rect);
        bool isClicked = isHovered && Raylib.IsMouseButtonPressed(MouseButton.Left);

        // Background
        var bgColor = focused ? new Color(50, 50, 70, 255) : new Color(35, 35, 50, 255);
        Raylib.DrawRectangle(x, y, width, height, bgColor);

        // Border
        var borderColor = focused ? new Color(100, 100, 150, 255) : ImmediateModeUI.PanelBorderColor;
        Raylib.DrawRectangleLines(x, y, width, height, borderColor);

        // Text
        string text = buffer.ToString();
        int fontSize = 18;
        int textY = y + (height - fontSize) / 2;
        Raylib.DrawText(text, x + 8, textY, fontSize, ImmediateModeUI.TextColor);

        // Cursor (blinking)
        if (focused && ((int)(Raylib.GetTime() * 2) % 2 == 0))
        {
            int cursorX = x + 8 + Raylib.MeasureText(text, fontSize);
            Raylib.DrawRectangle(cursorX, y + 6, 2, height - 12, ImmediateModeUI.TextColor);
        }

        return isClicked;
    }

    /// <summary>
    /// Handles keyboard input for text field.
    /// </summary>
    private static void HandleTextInput(StringBuilder buffer, int maxLength)
    {
        // Handle backspace
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace))
        {
            if (buffer.Length > 0)
            {
                buffer.Length--;
            }
        }

        // Handle character input
        int key = Raylib.GetCharPressed();
        while (key > 0)
        {
            if (key >= 32 && key <= 126 && buffer.Length < maxLength)
            {
                buffer.Append((char)key);
            }
            key = Raylib.GetCharPressed();
        }
    }

    /// <summary>
    /// Resets the dialog to default values.
    /// </summary>
    public void Reset()
    {
        _addressBuffer.Clear();
        _addressBuffer.Append("127.0.0.1");
        _portBuffer.Clear();
        _portBuffer.Append(NetworkService.DefaultPort.ToString());
        _addressFocused = true;
    }
}
