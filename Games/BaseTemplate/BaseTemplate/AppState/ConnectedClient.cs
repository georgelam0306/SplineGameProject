namespace BaseTemplate.GameApp.AppState;

/// <summary>
/// Represents a connected client in the lobby.
/// Immutable record for use in LobbyState.
/// </summary>
public readonly record struct ConnectedClient(
    /// <summary>Unique identifier for the client.</summary>
    Guid ClientId,

    /// <summary>Player slot (0-7) assigned in the lobby.</summary>
    int PlayerSlot,

    /// <summary>Display name shown in the lobby.</summary>
    string DisplayName,

    /// <summary>Whether this client has marked themselves as ready.</summary>
    bool IsReady,

    /// <summary>Whether this client has confirmed their Game is loaded.</summary>
    bool IsLoaded
);
