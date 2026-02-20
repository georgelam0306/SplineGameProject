using System.Text.Json;
using DerpTech.Cli.Models;
using DerpTech.Cli.Services;
using DerpTech.Cli.Validation;

namespace DerpTech.Cli.Commands;

public static class NewGameCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<int> ExecuteAsync(
        string projectName,
        string description,
        string author,
        bool force)
    {
        // Find repository root (walk up until we find DerpTech2026.sln)
        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Console.Error.WriteLine("Error: Could not find repository root (DerpTech2026.sln)");
            Console.Error.WriteLine("Make sure you're running this command from within the DerpTech2026 repository.");
            return 1;
        }

        // Validate project name
        var (isValid, error) = ProjectNameValidator.Validate(projectName);
        if (!isValid)
        {
            Console.Error.WriteLine($"Error: {error}");
            return 1;
        }

        // Initialize services
        var templateService = new TemplateService(repoRoot);
        var transformService = new FileTransformService();
        var solutionService = new SolutionService(repoRoot);

        // Check template exists
        if (!templateService.TemplateExists())
        {
            Console.Error.WriteLine("Error: BaseTemplate not found at Games/BaseTemplate");
            return 1;
        }

        // Check if project already exists
        if (templateService.ProjectExists(projectName))
        {
            if (!force)
            {
                Console.Error.WriteLine($"Error: Project '{projectName}' already exists at Games/{projectName}");
                Console.Error.WriteLine("Use --force to overwrite the existing project.");
                return 1;
            }

            Console.WriteLine($"Removing existing project '{projectName}'...");
            templateService.DeleteProject(projectName);
        }

        Console.WriteLine($"Creating new game project: {projectName}");
        Console.WriteLine();

        // Step 1: Copy template
        Console.WriteLine("Step 1: Copying template files...");
        var fileCount = 0;
        templateService.CopyTemplate(projectName, _ => fileCount++);
        Console.WriteLine($"  Copied {fileCount} files");

        // Step 2: Transform namespaces
        Console.WriteLine("Step 2: Transforming namespaces...");
        var projectPath = Path.Combine(repoRoot, "Games", projectName);
        var transformedCount = transformService.ReplaceNamespaces(projectPath, projectName);
        Console.WriteLine($"  Updated {transformedCount} files");

        // Step 3: Generate ProjectConfig.json
        Console.WriteLine("Step 3: Generating ProjectConfig.json...");
        var config = new ProjectConfig
        {
            ProjectName = projectName,
            NamespacePrefix = projectName,
            Description = description,
            Author = author,
            CreatedDate = DateTime.UtcNow,
            TemplateVersion = "1.0.0"
        };

        var configPath = Path.Combine(projectPath, "ProjectConfig.json");
        var configJson = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, configJson);
        Console.WriteLine($"  Created {configPath}");

        // Copy schema to project
        var schemaSource = Path.Combine(repoRoot, "Tools", "DerpTech.Cli", "Schemas", "project-config.schema.json");
        var schemaDest = Path.Combine(projectPath, "project-config.schema.json");
        if (File.Exists(schemaSource))
        {
            File.Copy(schemaSource, schemaDest, overwrite: true);
            Console.WriteLine($"  Created {schemaDest}");
        }

        // Step 4: Add to solution
        Console.WriteLine("Step 4: Adding projects to solution...");
        if (solutionService.SolutionExists())
        {
            var success = await solutionService.AddGameProjectsAsync(projectName, Console.WriteLine);
            if (!success)
            {
                Console.Error.WriteLine("Warning: Some projects failed to add to solution.");
                Console.Error.WriteLine("You may need to add them manually with 'dotnet sln add'");
            }
        }
        else
        {
            Console.WriteLine("  Solution file not found, skipping...");
        }

        // Done!
        Console.WriteLine();
        Console.WriteLine($"Successfully created game project: {projectName}");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  1. cd Games/{projectName}");
        Console.WriteLine($"  2. dotnet build");
        Console.WriteLine($"  3. bash scripts/run-game.sh {projectName}");
        Console.WriteLine();

        return 0;
    }

    private static string? FindRepoRoot()
    {
        var current = Directory.GetCurrentDirectory();

        while (!string.IsNullOrEmpty(current))
        {
            var slnPath = Path.Combine(current, "DerpTech2026.sln");
            if (File.Exists(slnPath))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }
            current = parent.FullName;
        }

        return null;
    }
}
