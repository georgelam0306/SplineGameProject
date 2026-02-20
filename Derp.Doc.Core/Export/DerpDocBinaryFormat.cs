namespace Derp.Doc.Export;

internal static class DerpDocBinaryFormat
{
    public const uint Magic = 0x42444447; // "GDDB"
    public const uint Version = 2;
    public const int DataAlignment = 16;
}

