using System.Runtime.InteropServices;

namespace DerpDoc.Runtime;

/// <summary>
/// Header for .derpdoc-live.bin (double-buffered live export).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LiveBinaryHeader
{
    public const uint MagicValue = 0x564C4444; // "DDLV" (Derp.Doc Live)

    public uint Magic;
    public uint Generation;
    public int ActiveSlot;
    public int TableCount;
    public long Slot0Offset;
    public long Slot1Offset;
    public long SlotSize;
}
