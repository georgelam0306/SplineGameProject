namespace Derp.Doc.Storage;

public static class DocExternalChangeSignalFile
{
    public const string DefaultFileName = ".derpdoc-external-change";

    public static string GetPath(string dbRoot, string fileName = DefaultFileName)
    {
        if (string.IsNullOrWhiteSpace(dbRoot))
        {
            dbRoot = Directory.GetCurrentDirectory();
        }
        return Path.Combine(Path.GetFullPath(dbRoot), fileName);
    }

    public static void Touch(string dbRoot, string fileName = DefaultFileName)
    {
        if (string.IsNullOrWhiteSpace(dbRoot))
        {
            return;
        }

        string path = GetPath(dbRoot, fileName);
        string tmpPath = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        File.WriteAllText(tmpPath, DateTime.UtcNow.ToString("O"));
        File.Move(tmpPath, path, overwrite: true);
    }

    public static bool TryGetLastWriteTimeUtc(string dbRoot, out DateTime lastWriteUtc, string fileName = DefaultFileName)
    {
        lastWriteUtc = default;
        string path = GetPath(dbRoot, fileName);
        if (!File.Exists(path))
        {
            return false;
        }

        lastWriteUtc = File.GetLastWriteTimeUtc(path);
        return lastWriteUtc != default;
    }
}

