using System.Runtime.InteropServices;

namespace Derp.Doc.Export;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct KeyRowIndexPair
{
    public readonly int Key;
    public readonly int RowIndex;
}

