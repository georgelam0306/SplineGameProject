namespace Derp.Doc.Storage;

public static class DocProjectPaths
{
    public const string GameRootMarkerFileName = "derpgame";
    public const string DatabaseDirectoryName = "Database";
    public const string ProjectJsonFileName = "project.json";

    public static string ResolveDbRootFromPath(string path, bool allowCreate, out string? gameRoot)
    {
        gameRoot = null;

        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        // Mode A: Game root discovery via 'derpgame' marker.
        var cursor = new DirectoryInfo(fullPath);
        while (cursor != null)
        {
            var marker = Path.Combine(cursor.FullName, GameRootMarkerFileName);
            if (File.Exists(marker))
            {
                gameRoot = cursor.FullName;
                return Path.Combine(gameRoot, DatabaseDirectoryName);
            }

            cursor = cursor.Parent;
        }

        // Mode B: Standalone DB root discovery via project.json.
        cursor = new DirectoryInfo(fullPath);
        while (cursor != null)
        {
            var projectJson = Path.Combine(cursor.FullName, ProjectJsonFileName);
            if (File.Exists(projectJson))
            {
                return cursor.FullName;
            }

            cursor = cursor.Parent;
        }

        if (allowCreate)
        {
            return fullPath;
        }

        throw new DirectoryNotFoundException($"No Derp.Doc DB root found at '{path}'. Expected {ProjectJsonFileName}, or a parent directory containing '{GameRootMarkerFileName}'.");
    }

    public static string ResolveDefaultBinaryPath(string dbRoot, string? gameRoot)
    {
        if (!string.IsNullOrWhiteSpace(gameRoot))
        {
            string gameName = new DirectoryInfo(gameRoot).Name;
            return Path.Combine(gameRoot, "Resources", "Database", gameName + ".derpdoc");
        }

        string projectName = new DirectoryInfo(dbRoot).Name;
        return Path.Combine(dbRoot, projectName + ".derpdoc");
    }

    public static string ResolveDefaultLiveBinaryPath(string dbRoot)
    {
        return Path.Combine(dbRoot, ".derpdoc-live.bin");
    }

    public static bool TryGetGameRootFromDbRoot(string dbRoot, out string gameRoot)
    {
        gameRoot = "";
        if (string.IsNullOrWhiteSpace(dbRoot))
        {
            return false;
        }

        var fullDbRoot = Path.GetFullPath(dbRoot).TrimEnd(Path.DirectorySeparatorChar);
        var dbDir = new DirectoryInfo(fullDbRoot);
        if (!string.Equals(dbDir.Name, DatabaseDirectoryName, StringComparison.Ordinal))
        {
            return false;
        }

        var parent = dbDir.Parent;
        if (parent == null)
        {
            return false;
        }

        var marker = Path.Combine(parent.FullName, GameRootMarkerFileName);
        if (!File.Exists(marker))
        {
            return false;
        }

        gameRoot = parent.FullName;
        return true;
    }
}
