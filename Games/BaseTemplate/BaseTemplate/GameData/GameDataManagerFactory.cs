using System;
using System.IO;
using BaseTemplate.GameApp.Core;
using BaseTemplate.GameApp.Stores;
using BaseTemplate.GameData.Schemas;
using Serilog;

namespace BaseTemplate.GameData;

/// <summary>
/// Factory for creating GameDataManager with appropriate settings for debug/release.
/// </summary>
public static class GameDataManagerFactory
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(GameDataManagerFactory));

    public static GameDataManager<GameDocDb> Create()
    {
        var binPath = Path.Combine(AppContext.BaseDirectory, "GameData.bin");

        // Try to find source JSON directory for hot-reload
        var jsonPath = FindSourceDataPath();
        bool enableHotReload = jsonPath != null;

        if (enableHotReload)
        {
            Log.Debug("Hot-reload enabled - JSON source: {JsonPath}, Binary: {BinPath}", jsonPath, binPath);
            return new GameDataManager<GameDocDb>(
                jsonPath!,
                binPath,
                GameDataBinaryLoader.Load,
                GameDataBinaryBuilder.Build,
                enableHotReload: true);
        }
        else
        {
            // No source directory found - load from binary only
            if (!File.Exists(binPath))
            {
                throw new FileNotFoundException(
                    $"GameData.bin not found at {binPath}. " +
                    "The binary must be pre-built during build/publish.",
                    binPath);
            }

            Log.Debug("Hot-reload disabled (source not found) - Binary: {BinPath}", binPath);
            return new GameDataManager<GameDocDb>(
                binPath,
                binPath,
                GameDataBinaryLoader.Load,
                builder: null,
                enableHotReload: false);
        }
    }

    private static string? FindSourceDataPath()
    {
        // Walk up from the base directory until we find the workspace root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            // Check if this is the workspace root (has BaseTemplate.GameData/Data)
            var sourcePath = Path.Combine(dir.FullName, "BaseTemplate.GameData", "Data");
            if (Directory.Exists(sourcePath))
            {
                return sourcePath;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
