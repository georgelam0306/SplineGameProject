using System.Numerics;
using GpuStruct;

namespace DerpLib.Sdf;

/// <summary>
/// A gradient stop used by the SDF compute shader.
/// Stored in a dedicated storage buffer and referenced by commands via stopStart/stopCount.
/// </summary>
[GpuStruct]
public partial struct SdfGradientStop
{
    /// <summary>RGBA color (0-1 range).</summary>
    public partial Vector4 Color { get; set; }

    /// <summary>Stop parameters: x=t (0-1), y/z/w reserved.</summary>
    public partial Vector4 Params { get; set; }
}

