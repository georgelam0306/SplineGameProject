using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Global wave system configuration.
/// Singleton table (Id=0) containing all wave timing and spawn parameters.
/// </summary>
[GameDocTable("WaveConfig")]
[StructLayout(LayoutKind.Sequential)]
public struct WaveConfigData
{
    [PrimaryKey]
    public int Id;

    /// <summary>Name for debugging.</summary>
    public GameDataId Name;

    // === Day/Time Settings ===
    /// <summary>Frames per in-game day (default: 3600 = 60 seconds at 60fps).</summary>
    public int FramesPerDay;

    // === Horde Wave Settings ===
    /// <summary>Days between major horde waves (e.g., 7 = weekly horde).</summary>
    public int HordeDayInterval;

    /// <summary>Random variance in frames for horde timing (e.g., 600 = +/- 10 seconds).</summary>
    public int HordeVarianceFrames;

    /// <summary>Warning countdown frames before horde arrival (e.g., 600 = 10 seconds).</summary>
    public int HordeWarningFrames;

    /// <summary>Base zombie count for first horde wave.</summary>
    public int HordeBaseZombieCount;

    /// <summary>Additional zombies per wave (linear scaling).</summary>
    public int HordeZombiesPerWave;

    /// <summary>Zombies spawned per frame during horde spawn.</summary>
    public int HordeSpawnRate;

    // === Mini Wave Settings ===
    /// <summary>Frames between mini waves (e.g., 1800 = 30 seconds).</summary>
    public int MiniWaveIntervalFrames;

    /// <summary>Random variance for mini wave timing.</summary>
    public int MiniWaveVarianceFrames;

    /// <summary>Base zombie count for mini waves.</summary>
    public int MiniWaveBaseCount;

    /// <summary>Additional zombies per wave number for mini waves.</summary>
    public int MiniWaveCountPerWave;

    // === Final Wave ===
    /// <summary>Wave number that triggers final wave (0 = disabled).</summary>
    public int FinalWaveNumber;

    /// <summary>Zombie multiplier for final wave (e.g., 300 = 3x zombies).</summary>
    public int FinalWaveMultiplierPercent;

    // === Spawn Geometry ===
    /// <summary>Distance from map edge (in pixels) where waves spawn.</summary>
    public int EdgeSpawnOffset;

    /// <summary>Spread along the edge (in pixels) for spawn distribution.</summary>
    public int EdgeSpawnSpread;
}
