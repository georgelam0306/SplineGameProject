using NLayer;

namespace DerpLib.Audio;

internal static class Mp3Decoder
{
    public static (float[] Samples, int Frequency, int Channels) DecodeToFloat(ReadOnlySpan<byte> mp3Bytes)
    {
        using var stream = new MemoryStream(mp3Bytes.ToArray(), writable: false);
        using var mpeg = new MpegFile(stream);

        int sampleRate = mpeg.SampleRate;
        int channels = mpeg.Channels;
        if (channels != 1 && channels != 2)
        {
            throw new InvalidOperationException($"Unsupported MP3 channel count: {channels}");
        }

        long length = mpeg.Length;
        if (length <= 0)
        {
            return (Array.Empty<float>(), sampleRate, channels);
        }

        int maxSamples = length > int.MaxValue ? int.MaxValue : (int)length;
        var samples = new float[maxSamples];

        int offset = 0;
        while (offset < samples.Length)
        {
            int read = mpeg.ReadSamples(samples, offset, samples.Length - offset);
            if (read <= 0)
            {
                break;
            }
            offset += read;
        }

        if (offset == samples.Length)
        {
            return (samples, sampleRate, channels);
        }

        var trimmed = new float[offset];
        Array.Copy(samples, 0, trimmed, 0, offset);
        return (trimmed, sampleRate, channels);
    }
}

