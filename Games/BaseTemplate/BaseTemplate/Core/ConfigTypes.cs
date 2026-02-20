namespace BaseTemplate;

/// <summary>
/// Player configuration passed when creating a game.
/// </summary>
public readonly record struct PlayerConfig(int MaxPlayers, int PlayerCount, int LocalPlayerSlot);

/// <summary>
/// Configuration for replay/recording mode.
/// </summary>
public readonly record struct ReplayConfig(string? ReplayFilePath, string? RecordFilePath);

/// <summary>
/// Session seed for deterministic random in simulation.
/// </summary>
public readonly record struct SessionSeed(int Value);

/// <summary>
/// Initial coordinator status for the game session.
/// </summary>
public readonly record struct InitialCoordinator(bool Value);
