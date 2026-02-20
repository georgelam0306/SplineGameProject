namespace DieDrifterDie.GameApp.AppState;

/// <summary>
/// Represents the local client (this application instance).
/// Each running instance of the game has exactly one LocalClient.
/// </summary>
public sealed class LocalClient
{
    /// <summary>
    /// Unique identifier for this client, generated at application startup.
    /// Can be overridden when joining via matchmaking (server assigns PlayerId).
    /// </summary>
    public Guid ClientId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The player slot (0-7) assigned to this client in the lobby.
    /// -1 if not yet assigned.
    /// </summary>
    public int PlayerSlot { get; set; } = -1;

    /// <summary>
    /// Display name shown in the lobby.
    /// </summary>
    public string DisplayName { get; set; } = "Player";
}
