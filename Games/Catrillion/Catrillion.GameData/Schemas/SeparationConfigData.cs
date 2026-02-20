using System.Runtime.InteropServices;
using GameDocDatabase;
using Core;

namespace Catrillion.GameData.Schemas;

/// <summary>
/// Configuration data for the density-based separation system.
/// Singleton table (single row with Id=0) controlling entity spacing behavior.
/// </summary>
[GameDocTable("SeparationConfig")]
[StructLayout(LayoutKind.Sequential)]
public struct SeparationConfigData
{
    [PrimaryKey]
    public int Id;

    /// <summary>Configuration name for identification (e.g., "Default").</summary>
    public GameDataId Name;

    /// <summary>Grid resolution (cells per axis). Default: 512 for 8192px map.</summary>
    public int GridSize;

    /// <summary>Size of each density cell in pixels. Default: 16.</summary>
    public int CellSize;

    /// <summary>Spread scale for subcell position variation (reduces grid alignment). Default: 0.4.</summary>
    public Fixed64 SpreadScale;

    /// <summary>Multiplier for gradient-based separation force. Default: 1.0.</summary>
    public Fixed64 GradientScale;

    /// <summary>EMA smoothing factor (0-1). Higher = more responsive, lower = smoother. Default: 0.2.</summary>
    public Fixed64 SmoothingAlpha;

    /// <summary>Squared threshold below which separation forces are ignored (dead zone). Default: 0.01.</summary>
    public Fixed64 DeadZoneThresholdSq;

    /// <summary>Minimum density in a cell before separation forces apply. Default: 1.</summary>
    public int MinDensityThreshold;
}
