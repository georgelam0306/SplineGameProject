using DieDrifterDie.GameData.Schemas;
using GameDocDatabase.Runtime;

namespace DieDrifterDie.GameData.Cli;

/// <summary>
/// CLI tool for building binary GameDocDb files from JSON.
/// Usage: dotnet run -- build &lt;jsonDir&gt; &lt;outputPath&gt;
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            PrintUsage();
            return 1;
        }

        return args[0].ToLower() switch
        {
            "build" => Build(args[1..]),
            "info" => Info(args[1..]),
            _ => PrintUsage()
        };
    }

    private static int PrintUsage()
    {
        Console.WriteLine("DieDrifterDie.GameData.Cli - Binary file builder");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  build <jsonDir> <outputPath>  Build binary from JSON files");
        Console.WriteLine("  info <binFile>                Show info about binary file");
        return 1;
    }

    private static int Build(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: build <jsonDir> <outputPath>");
            return 1;
        }

        var jsonDir = args[0];
        var outputPath = args[1];

        if (!Directory.Exists(jsonDir))
        {
            Console.WriteLine($"Error: Directory not found: {jsonDir}");
            return 1;
        }

        Console.WriteLine($"Building binary from: {jsonDir}");
        Console.WriteLine($"Output: {outputPath}");

        try
        {
            // Use the generated builder (emitted by GameDocDatabase.Generator into DieDrifterDie.GameData.Schemas)
            GameDataBinaryBuilder.Build(jsonDir, outputPath);

            // Verify
            using var loader = BinaryLoader.Load(outputPath);
            Console.WriteLine($"Verification: {loader.TableNames.Count()} tables loaded");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static int Info(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: info <binFile>");
            return 1;
        }

        var binFile = args[0];

        try
        {
            using var loader = BinaryLoader.Load(binFile);

            Console.WriteLine($"File: {binFile}");
            Console.WriteLine($"Tables: {loader.TableNames.Count()}");
            Console.WriteLine();

            foreach (var tableName in loader.TableNames)
            {
                Console.WriteLine($"  {tableName}: {loader.GetRecordCount(tableName)} records");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
