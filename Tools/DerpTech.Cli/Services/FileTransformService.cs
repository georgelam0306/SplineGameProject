namespace DerpTech.Cli.Services;

public sealed class FileTransformService
{
    private const string TemplateName = "BaseTemplate";

    private static readonly string[] SourceExtensions = [".cs", ".csproj", ".props", ".targets", ".sh"];

    public int ReplaceNamespaces(string projectPath, string newNamespace, Action<string>? onProgress = null)
    {
        var count = 0;

        foreach (var ext in SourceExtensions)
        {
            foreach (var file in Directory.EnumerateFiles(projectPath, $"*{ext}", SearchOption.AllDirectories))
            {
                if (ReplaceInFile(file, TemplateName, newNamespace))
                {
                    count++;
                    onProgress?.Invoke(file);
                }
            }
        }

        return count;
    }

    private static bool ReplaceInFile(string filePath, string oldValue, string newValue)
    {
        var content = File.ReadAllText(filePath);
        var newContent = content.Replace(oldValue, newValue);

        if (content != newContent)
        {
            File.WriteAllText(filePath, newContent);
            return true;
        }

        return false;
    }
}
