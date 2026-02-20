namespace DerpLib.Sdf;

/// <summary>
/// Modifier types for SDF evaluation and coverage shaping.
/// Stored in the per-command modifier node chain (see <see cref="SdfModifierNode"/>).
/// </summary>
public enum SdfModifierType : uint
{
    None = 0,
    Offset = 1,
    Feather = 2
}

