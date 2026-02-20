using Derp.Doc.Model;
using Derp.Doc.Tables;

namespace Derp.Doc.Storage;

public static class DocProjectScaffolder
{
    public static string EnsureDbRoot(string dbRoot, string projectName)
    {
        if (string.IsNullOrWhiteSpace(dbRoot))
        {
            throw new ArgumentException("dbRoot is required.", nameof(dbRoot));
        }

        string fullDbRoot = Path.GetFullPath(dbRoot);
        string projectJson = Path.Combine(fullDbRoot, DocProjectPaths.ProjectJsonFileName);
        if (File.Exists(projectJson))
        {
            return fullDbRoot;
        }

        Directory.CreateDirectory(fullDbRoot);
        Directory.CreateDirectory(Path.Combine(fullDbRoot, "tables"));
        Directory.CreateDirectory(Path.Combine(fullDbRoot, "docs"));

        var project = new DocProject { Name = string.IsNullOrWhiteSpace(projectName) ? "DerpDocProject" : projectName };
        DocSystemTableSynchronizer.Synchronize(project, fullDbRoot);
        ProjectSerializer.Save(project, fullDbRoot);
        return fullDbRoot;
    }

    public static string EnsureGameRoot(string gameRoot, string projectName)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            throw new ArgumentException("gameRoot is required.", nameof(gameRoot));
        }

        string fullGameRoot = Path.GetFullPath(gameRoot);
        Directory.CreateDirectory(fullGameRoot);

        string markerPath = Path.Combine(fullGameRoot, DocProjectPaths.GameRootMarkerFileName);
        if (!File.Exists(markerPath))
        {
            File.WriteAllText(markerPath, "");
        }

        Directory.CreateDirectory(Path.Combine(fullGameRoot, "Assets"));
        Directory.CreateDirectory(Path.Combine(fullGameRoot, "Resources", "Database"));

        string dbRoot = Path.Combine(fullGameRoot, DocProjectPaths.DatabaseDirectoryName);
        EnsureDbRoot(dbRoot, string.IsNullOrWhiteSpace(projectName) ? new DirectoryInfo(fullGameRoot).Name : projectName);
        return fullGameRoot;
    }
}
