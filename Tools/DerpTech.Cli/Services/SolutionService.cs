using System.Diagnostics;

namespace DerpTech.Cli.Services;

public sealed class SolutionService
{
    private readonly string _solutionPath;
    private readonly string _repoRoot;

    public SolutionService(string repoRoot)
    {
        _repoRoot = repoRoot;
        _solutionPath = Path.Combine(repoRoot, "DerpTech2026.sln");
    }

    public bool SolutionExists() => File.Exists(_solutionPath);

    public async Task<bool> AddGameProjectsAsync(string gameName, Action<string>? onProgress = null)
    {
        var solutionFolder = $"Games\\{gameName}";

        // List of projects to add (in dependency order)
        var projects = new[]
        {
            $"Games/{gameName}/{gameName}.GameData/{gameName}.GameData.csproj",
            $"Games/{gameName}/{gameName}.GameData.Cli/{gameName}.GameData.Cli.csproj",
            $"Games/{gameName}/{gameName}/{gameName}.csproj",
            $"Games/{gameName}/{gameName}.Headless/{gameName}.Headless.csproj",
            $"Games/{gameName}/{gameName}.Tests/{gameName}.Tests.csproj",
            $"Games/{gameName}/{gameName}.Benchmarks/{gameName}.Benchmarks.csproj",
        };

        foreach (var project in projects)
        {
            var projectPath = Path.Combine(_repoRoot, project);

            if (!File.Exists(projectPath))
            {
                onProgress?.Invoke($"  Warning: Project not found: {project}");
                continue;
            }

            var success = await RunDotnetSlnAddAsync(project, solutionFolder);
            if (success)
            {
                onProgress?.Invoke($"  Added: {project}");
            }
            else
            {
                onProgress?.Invoke($"  Failed: {project}");
                return false;
            }
        }

        return true;
    }

    private async Task<bool> RunDotnetSlnAddAsync(string projectPath, string solutionFolder)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"sln \"{_solutionPath}\" add \"{Path.Combine(_repoRoot, projectPath)}\" --solution-folder \"{solutionFolder}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _repoRoot
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return false;
        }

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
}
