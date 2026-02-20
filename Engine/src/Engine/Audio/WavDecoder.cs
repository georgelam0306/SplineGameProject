using System.Buffers.Binary;
using Silk.NET.SDL;

namespace DerpLib.Audio;

internal static class WavDecoder
{
    public static WavPcm Decode(ReadOnlySpan<byte> wavBytes)
    {
        if (wavBytes.Length < 44)
        {
            throw new InvalidOperationException("WAV too small.");
        }

        if (!IsFourCc(wavBytes, 0, 'R', 'I', 'F', 'F') || !IsFourCc(wavBytes, 8, 'W', 'A', 'V', 'E'))
        {
            throw new InvalidOperationException("Not a RIFF/WAVE file.");
        }

        int offset = 12;

        bool hasFmt = false;
        bool hasData = false;

        ushort audioFormat = 0;
        ushort channels = 0;
        uint sampleRate = 0;
        ushort bitsPerSample = 0;

        int dataOffset = 0;
        int dataSize = 0;

        while (offset + 8 <= wavBytes.Length)
        {
            uint chunkId = BinaryPrimitives.ReadUInt32LittleEndian(wavBytes.Slice(offset, 4));
            int chunkSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(wavBytes.Slice(offset + 4, 4));
            int chunkDataOffset = offset + 8;

            if (chunkDataOffset + chunkSize > wavBytes.Length)
            {
                break;
            }

            if (chunkId == FourCc('f', 'm', 't', ' '))
            {
                if (chunkSize < 16)
                {
                    throw new InvalidOperationException("Invalid fmt chunk.");
                }

                audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(wavBytes.Slice(chunkDataOffset + 0, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(wavBytes.Slice(chunkDataOffset + 2, 2));
                sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(wavBytes.Slice(chunkDataOffset + 4, 4));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(wavBytes.Slice(chunkDataOffset + 14, 2));
                hasFmt = true;
            }
            else if (chunkId == FourCc('d', 'a', 't', 'a'))
            {
                dataOffset = chunkDataOffset;
                dataSize = chunkSize;
                hasData = true;
            }

            offset = chunkDataOffset + chunkSize;
            if ((offset & 1) != 0)
            {
                offset++;
            }

            if (hasFmt && hasData)
            {
                break;
            }
        }

        if (!hasFmt || !hasData)
        {
            throw new InvalidOperationException("WAV missing fmt or data chunk.");
        }

        if (channels == 0 || sampleRate == 0)
        {
            throw new InvalidOperationException("Invalid WAV fmt.");
        }

        ushort sdlFormat;
        if (audioFormat == 1 && bitsPerSample == 16)
        {
            sdlFormat = (ushort)Sdl.AudioS16Lsb;
        }
        else if (audioFormat == 3 && bitsPerSample == 32)
        {
            sdlFormat = (ushort)Sdl.AudioF32Lsb;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported WAV format: fmt={audioFormat}, bits={bitsPerSample}.");
        }

        if (dataSize <= 0)
        {
            throw new InvalidOperationException("WAV data chunk empty.");
        }

        var pcm = wavBytes.Slice(dataOffset, dataSize).ToArray();
        return new WavPcm(pcm, (int)sampleRate, sdlFormat, (byte)channels);
    }

    private static bool IsFourCc(ReadOnlySpan<byte> bytes, int offset, char a, char b, char c, char d)
    {
        return bytes.Length >= offset + 4
               && bytes[offset + 0] == (byte)a
               && bytes[offset + 1] == (byte)b
               && bytes[offset + 2] == (byte)c
               && bytes[offset + 3] == (byte)d;
    }

    private static uint FourCc(char a, char b, char c, char d)
    {
        return (uint)((byte)a | ((byte)b << 8) | ((byte)c << 16) | ((byte)d << 24));
    }
}

