namespace Catrillion.Core;

/// <summary>
/// Encapsulates launch configuration parsed from CLI args and environment variables.
/// </summary>
public readonly record struct AppLaunch(
    bool SkipMenu,
    int ExpectedPlayerCount,
    int LocalPlayerSlot,
    string? RecordFilePath,
    string? ReplayFilePath,
    bool UseSteamMatchmaking = false);
