namespace DerpTech.Cli.Services;

public sealed class TemplateService
{
    private const string TemplateName = "BaseTemplate";

    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Outputs",
        ".serena",
        "bin",
        "obj",
        ".git"
    };

    private readonly string _repoRoot;

    public TemplateService(string repoRoot)
    {
        _repoRoot = repoRoot;
    }

    public string TemplatePath => Path.Combine(_repoRoot, "Games", TemplateName);
    public string GamesPath => Path.Combine(_repoRoot, "Games");

    public bool TemplateExists() => Directory.Exists(TemplatePath);

    public bool ProjectExists(string projectName)
    {
        var targetPath = Path.Combine(GamesPath, projectName);
        return Directory.Exists(targetPath);
    }

    public void CopyTemplate(string targetName, Action<string>? onProgress = null)
    {
        var targetPath = Path.Combine(GamesPath, targetName);
        CopyDirectoryRecursive(TemplatePath, targetPath, TemplateName, targetName, onProgress);
    }

    private void CopyDirectoryRecursive(
        string source,
        string target,
        string oldName,
        string newName,
        Action<string>? onProgress)
    {
        Directory.CreateDirectory(target);

        // Copy files with renaming
        foreach (var sourceFile in Directory.GetFiles(source))
        {
            var fileName = Path.GetFileName(sourceFile);
            var newFileName = fileName.Replace(oldName, newName);
            var destFile = Path.Combine(target, newFileName);

            File.Copy(sourceFile, destFile, overwrite: false);
            onProgress?.Invoke(destFile);
        }

        // Recurse into subdirectories
        foreach (var sourceDir in Directory.GetDirectories(source))
        {
            var dirName = Path.GetFileName(sourceDir);

            // Skip build artifacts and tool directories
            if (SkipDirectories.Contains(dirName))
            {
                continue;
            }

            var newDirName = dirName.Replace(oldName, newName);
            var destDir = Path.Combine(target, newDirName);

            CopyDirectoryRecursive(sourceDir, destDir, oldName, newName, onProgress);
        }
    }

    public void DeleteProject(string projectName)
    {
        var targetPath = Path.Combine(GamesPath, projectName);
        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, recursive: true);
        }
    }
}
