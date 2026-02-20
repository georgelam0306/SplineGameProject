namespace DerpDoc.Runtime;

/// <summary>
/// Resolved paths for a game's database binary files.
/// </summary>
public readonly struct GameDatabasePaths
{
    public string? LiveBinaryPath { get; init; }
    public string? BakedBinaryPath { get; init; }
}

/// <summary>
/// Auto-discovers live and baked database binary paths using the derpgame convention.
/// </summary>
public static class GameRootDiscovery
{
    public const string GameRootMarker = "derpgame";

    /// <summary>
    /// Auto-discover paths. Checks CWD for game root, then AppContext.BaseDirectory.
    /// </summary>
    public static GameDatabasePaths Discover(string binaryFileName)
    {
        // 1. Walk up from CWD looking for derpgame marker
        string? gameRoot = FindGameRoot(Directory.GetCurrentDirectory());
        if (gameRoot != null)
        {
            string livePath = Path.Combine(gameRoot, "Database", ".derpdoc-live.bin");
            string bakedPath = Path.Combine(gameRoot, "Resources", "Database", binaryFileName);
            return new GameDatabasePaths
            {
                LiveBinaryPath = livePath,
                BakedBinaryPath = bakedPath,
            };
        }

        // 2. Check exe-relative (release/NativeAOT)
        string baseDir = AppContext.BaseDirectory;
        string exeBaked = Path.Combine(baseDir, "Resources", "Database", binaryFileName);
        if (File.Exists(exeBaked))
        {
            return new GameDatabasePaths
            {
                LiveBinaryPath = null,
                BakedBinaryPath = exeBaked,
            };
        }

        // 3. Neither found — reloader handles gracefully
        return new GameDatabasePaths
        {
            LiveBinaryPath = null,
            BakedBinaryPath = null,
        };
    }

    private static string? FindGameRoot(string startDir)
    {
        string? dir = startDir;
        while (dir != null)
        {
            string markerPath = Path.Combine(dir, GameRootMarker);
            if (File.Exists(markerPath))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
