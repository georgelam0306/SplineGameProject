namespace DerpLib.Audio;

internal readonly struct WavPcm
{
    public readonly byte[] Pcm;
    public readonly int Frequency;
    public readonly ushort Format;
    public readonly byte Channels;

    public WavPcm(byte[] pcm, int frequency, ushort format, byte channels)
    {
        Pcm = pcm;
        Frequency = frequency;
        Format = format;
        Channels = channels;
    }
}

