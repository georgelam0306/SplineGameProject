using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace BaseTemplate.GameData.Schemas;

/// <summary>
/// Network and rollback configuration.
/// Singleton table (Id=0) containing input delay and sync settings.
/// </summary>
[GameDocTable("NetworkConfig")]
[StructLayout(LayoutKind.Sequential)]
public struct NetworkConfigData
{
    [PrimaryKey]
    public int Id;

    /// <summary>Name for debugging.</summary>
    public GameDataId Name;

    /// <summary>
    /// Input delay in frames. Local inputs captured at frame N are executed at frame N + delay.
    /// Reduces rollback frequency by giving remote inputs more time to arrive.
    /// Default: 3 (50ms at 60fps).
    /// </summary>
    public int InputDelayFrames;

    /// <summary>
    /// Maximum frames the simulation can run ahead of confirmed inputs before pausing.
    /// Higher values allow smoother gameplay but increase rollback depth.
    /// Default: 4.
    /// </summary>
    public int MaxFramesAheadOfConfirmed;
}
