using Raylib_cs;

namespace Catrillion.UI;

/// <summary>
/// Static helper methods for immediate mode UI rendering with Raylib.
/// </summary>
public static class ImmediateModeUI
{
    /// <summary>Standard button color.</summary>
    public static readonly Color ButtonColor = new(60, 60, 80, 255);

    /// <summary>Button color when hovered.</summary>
    public static readonly Color ButtonHoverColor = new(80, 80, 110, 255);

    /// <summary>Button color when pressed.</summary>
    public static readonly Color ButtonPressColor = new(40, 40, 60, 255);

    /// <summary>Disabled button color.</summary>
    public static readonly Color ButtonDisabledColor = new(40, 40, 50, 255);

    /// <summary>Text color.</summary>
    public static readonly Color TextColor = new(220, 220, 220, 255);

    /// <summary>Disabled text color.</summary>
    public static readonly Color TextDisabledColor = new(100, 100, 100, 255);

    /// <summary>Panel background color.</summary>
    public static readonly Color PanelColor = new(30, 30, 40, 255);

    /// <summary>Panel border color.</summary>
    public static readonly Color PanelBorderColor = new(60, 60, 80, 255);

    /// <summary>Ready indicator color.</summary>
    public static readonly Color ReadyColor = new(80, 200, 80, 255);

    /// <summary>Not ready indicator color.</summary>
    public static readonly Color NotReadyColor = new(200, 80, 80, 255);

    /// <summary>Host indicator color.</summary>
    public static readonly Color HostColor = new(200, 180, 80, 255);

    /// <summary>
    /// Draws a button and returns true if clicked. Uses hardware mouse position.
    /// </summary>
    public static bool Button(int x, int y, int width, int height, string text, bool enabled = true)
    {
        return Button(x, y, width, height, text, Raylib.GetMousePosition(), enabled);
    }

    /// <summary>
    /// Draws a button and returns true if clicked. Uses provided mouse position (for software cursor support).
    /// </summary>
    public static bool Button(int x, int y, int width, int height, string text, System.Numerics.Vector2 mousePos, bool enabled = true)
    {
        var rect = new Rectangle(x, y, width, height);
        bool isHovered = Raylib.CheckCollisionPointRec(mousePos, rect);
        bool isPressed = isHovered && Raylib.IsMouseButtonDown(MouseButton.Left);
        bool isClicked = isHovered && Raylib.IsMouseButtonReleased(MouseButton.Left) && enabled;

        Color bgColor;
        Color textColor;

        if (!enabled)
        {
            bgColor = ButtonDisabledColor;
            textColor = TextDisabledColor;
        }
        else if (isPressed)
        {
            bgColor = ButtonPressColor;
            textColor = TextColor;
        }
        else if (isHovered)
        {
            bgColor = ButtonHoverColor;
            textColor = TextColor;
        }
        else
        {
            bgColor = ButtonColor;
            textColor = TextColor;
        }

        Raylib.DrawRectangle(x, y, width, height, bgColor);
        Raylib.DrawRectangleLines(x, y, width, height, PanelBorderColor);

        int fontSize = 20;
        int textWidth = Raylib.MeasureText(text, fontSize);
        int textX = x + (width - textWidth) / 2;
        int textY = y + (height - fontSize) / 2;
        Raylib.DrawText(text, textX, textY, fontSize, textColor);

        return isClicked;
    }

    /// <summary>
    /// Draws centered text at the given Y position.
    /// </summary>
    public static void DrawCenteredText(string text, int y, int fontSize, Color color)
    {
        int screenWidth = Raylib.GetScreenWidth();
        int textWidth = Raylib.MeasureText(text, fontSize);
        int x = (screenWidth - textWidth) / 2;
        Raylib.DrawText(text, x, y, fontSize, color);
    }

    /// <summary>
    /// Draws a panel background with optional border.
    /// </summary>
    public static void DrawPanel(int x, int y, int width, int height, Color? bgColor = null, Color? borderColor = null)
    {
        Raylib.DrawRectangle(x, y, width, height, bgColor ?? PanelColor);
        if (borderColor.HasValue)
        {
            Raylib.DrawRectangleLines(x, y, width, height, borderColor.Value);
        }
    }

    /// <summary>
    /// Draws a player slot row for the lobby screen.
    /// </summary>
    public static void DrawPlayerSlot(
        int x, int y, int width, int height,
        int slotIndex, string name, bool isReady, bool isHost, bool isLocal)
    {
        // Background
        var bgColor = isLocal ? new Color(40, 40, 60, 255) : PanelColor;
        Raylib.DrawRectangle(x, y, width, height, bgColor);
        Raylib.DrawRectangleLines(x, y, width, height, PanelBorderColor);

        // Slot number
        string slotText = $"#{slotIndex + 1}";
        Raylib.DrawText(slotText, x + 10, y + (height - 20) / 2, 20, TextColor);

        // Player name
        int nameX = x + 60;
        Raylib.DrawText(name, nameX, y + (height - 20) / 2, 20, TextColor);

        // Host indicator
        if (isHost)
        {
            int hostX = x + width - 160;
            Raylib.DrawText("[HOST]", hostX, y + (height - 16) / 2, 16, HostColor);
        }

        // Ready indicator
        int readyX = x + width - 80;
        string readyText = isReady ? "READY" : "---";
        Color readyColor = isReady ? ReadyColor : NotReadyColor;
        Raylib.DrawText(readyText, readyX, y + (height - 16) / 2, 16, readyColor);

        // Local indicator
        if (isLocal)
        {
            Raylib.DrawText("(You)", x + width - 240, y + (height - 14) / 2, 14, new Color(150, 150, 180, 255));
        }
    }

    /// <summary>
    /// Draws a large centered countdown number.
    /// </summary>
    public static void DrawCountdown(int seconds)
    {
        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        string text = seconds > 0 ? seconds.ToString() : "GO!";
        int fontSize = 120;
        int textWidth = Raylib.MeasureText(text, fontSize);

        int x = (screenWidth - textWidth) / 2;
        int y = (screenHeight - fontSize) / 2;

        // Shadow
        Raylib.DrawText(text, x + 4, y + 4, fontSize, new Color(0, 0, 0, 180));
        // Main text
        Raylib.DrawText(text, x, y, fontSize, Color.White);
    }

    /// <summary>
    /// Draws a loading progress bar.
    /// </summary>
    public static void DrawProgressBar(int x, int y, int width, int height, int current, int total)
    {
        // Background
        Raylib.DrawRectangle(x, y, width, height, PanelColor);
        Raylib.DrawRectangleLines(x, y, width, height, PanelBorderColor);

        // Progress
        if (total > 0)
        {
            float progress = (float)current / total;
            int fillWidth = (int)((width - 4) * progress);
            Raylib.DrawRectangle(x + 2, y + 2, fillWidth, height - 4, ReadyColor);
        }

        // Text
        string text = $"{current}/{total}";
        int fontSize = 16;
        int textWidth = Raylib.MeasureText(text, fontSize);
        int textX = x + (width - textWidth) / 2;
        int textY = y + (height - fontSize) / 2;
        Raylib.DrawText(text, textX, textY, fontSize, TextColor);
    }
}
