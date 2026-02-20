using SimTable;

namespace Catrillion.Simulation.Components;

/// <summary>
/// Global wave state for zombie spawn management.
/// Tracks day/time, mini waves, and horde events.
/// </summary>
[SimDataTable]
public partial struct WaveStateRow
{
    // === Day/Time Tracking ===
    /// <summary>Current in-game day (starts at 1).</summary>
    public int CurrentDay;

    /// <summary>Frames elapsed in current day (0 to FramesPerDay-1).</summary>
    public int FramesSinceDayStart;

    // === Wave Tracking ===
    /// <summary>Current major horde wave number (starts at 0, increments after each horde).</summary>
    public int CurrentHordeWave;

    /// <summary>Current mini wave number (starts at 0).</summary>
    public int CurrentMiniWave;

    /// <summary>Currently alive zombies spawned by waves (not including map zombies).</summary>
    public int ActiveWaveZombieCount;

    // === Next Wave Scheduling ===
    /// <summary>Frame when next horde warning starts.</summary>
    public int NextHordeWarningFrame;

    /// <summary>Frame when next horde spawning begins.</summary>
    public int NextHordeFrame;

    /// <summary>Frame when next mini wave spawning begins.</summary>
    public int NextMiniWaveFrame;

    // === Horde State ===
    /// <summary>Total zombies to spawn in current horde.</summary>
    public int HordeZombieCount;

    /// <summary>Zombies spawned so far in current horde.</summary>
    public int HordeSpawnedCount;

    /// <summary>Frames until next batch spawn during horde.</summary>
    public int HordeSpawnCooldown;

    /// <summary>Direction: 0=North, 1=East, 2=South, 3=West, 4=All.</summary>
    public byte HordeDirection;

    // === Mini Wave State ===
    /// <summary>Total zombies to spawn in current mini wave.</summary>
    public int MiniWaveZombieCount;

    /// <summary>Zombies spawned so far in current mini wave.</summary>
    public int MiniWaveSpawnedCount;

    /// <summary>Frames until next spawn during mini wave.</summary>
    public int MiniWaveSpawnCooldown;

    /// <summary>Frame when mini wave UI display should end (for minimum visibility).</summary>
    public int MiniWaveDisplayEndFrame;

    // === Warning State ===
    /// <summary>Frames remaining in warning countdown (0 = no warning).</summary>
    public int WarningCountdownFrames;

    /// <summary>Direction indicator for UI display.</summary>
    public byte WarningDirection;

    // === Population Limits ===
    /// <summary>Maximum allowed active wave zombies.</summary>
    public int MaxWaveZombieCount;

    // === State Flags ===
    public WaveStateFlags Flags;
}
