using Core;
using Pooled;
using Property;
using Property.Runtime;

namespace Derp.UI;

[Pooled(SoA = true, GenerateStableId = true, PoolId = 209)]
public partial struct TextComponent
{
    [Column]
    [Property(Name = "Text", Group = "Text", Order = 0, Kind = PropertyKind.StringHandle)]
    public StringHandle Text;

    [Column]
    [Property(Name = "Font", Group = "Text", Order = 1, Kind = PropertyKind.StringHandle)]
    public StringHandle Font;

    [Column]
    [Property(Name = "Size", Group = "Text", Order = 2, Min = 1f, Step = 0.5f)]
    public float FontSizePx;

    [Column]
    [Property(Name = "Line Height", Group = "Text", Order = 3, Min = 0.25f, Step = 0.05f)]
    public float LineHeightScale;

    [Column]
    [Property(Name = "Letter Spacing", Group = "Text", Order = 4, Step = 0.25f)]
    public float LetterSpacingPx;

    [Column]
    [Property(Name = "Multiline", Group = "Text", Order = 5)]
    public bool Multiline;

    [Column]
    [Property(Name = "Wrap", Group = "Text", Order = 6)]
    public bool Wrap;

    [Column]
    [Property(Name = "Overflow", Group = "Text", Order = 7, Kind = PropertyKind.Int, Min = 0f, Max = 4f, Step = 1f)]
    public int Overflow;

    [Column]
    [Property(Name = "Align X", Group = "Text", Order = 8, Kind = PropertyKind.Int, Min = 0f, Max = 2f, Step = 1f)]
    public int AlignX;

    [Column]
    [Property(Name = "Align Y", Group = "Text", Order = 9, Kind = PropertyKind.Int, Min = 0f, Max = 2f, Step = 1f)]
    public int AlignY;
}
