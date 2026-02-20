using Raylib_cs;

namespace BaseTemplate.Presentation.Rendering.Services;

/// <summary>
/// Centralized player color definitions for consistent visual identification.
/// Used for selection rings, highlights, and other player-specific rendering.
/// </summary>
public static class PlayerColorService
{
    /// <summary>
    /// Distinct colors for each player slot (0-7).
    /// </summary>
    public static readonly Color[] PlayerColors =
    [
        Color.Blue,                          // Player 0
        Color.Red,                           // Player 1
        Color.Green,                         // Player 2
        Color.Yellow,                        // Player 3
        new Color(255, 128, 0, 255),         // Player 4 (Orange)
        new Color(128, 0, 255, 255),         // Player 5 (Purple)
        new Color(0, 255, 255, 255),         // Player 6 (Cyan)
        new Color(255, 0, 255, 255),         // Player 7 (Magenta)
    ];

    /// <summary>
    /// Get the color associated with a player ID.
    /// </summary>
    /// <param name="playerId">Player slot (0-7)</param>
    /// <returns>The player's assigned color, or White for invalid IDs</returns>
    public static Color GetPlayerColor(int playerId)
        => playerId >= 0 && playerId < PlayerColors.Length
            ? PlayerColors[playerId]
            : Color.White;

    /// <summary>
    /// Get a semi-transparent version of a player's color for fills.
    /// </summary>
    /// <param name="playerId">Player slot (0-7)</param>
    /// <param name="alpha">Alpha value (0-255)</param>
    /// <returns>The player's color with adjusted alpha</returns>
    public static Color GetPlayerColorWithAlpha(int playerId, byte alpha)
    {
        var color = GetPlayerColor(playerId);
        return new Color(color.R, color.G, color.B, alpha);
    }
}
