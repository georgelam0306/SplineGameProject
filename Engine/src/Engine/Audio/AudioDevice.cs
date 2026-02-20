using Serilog;
using Silk.NET.SDL;

namespace DerpLib.Audio;

public sealed class AudioDevice : IDisposable
{
    private const int OutputFrequency = 44100;
    private const ushort OutputFormat = (ushort)Sdl.AudioS16Sys;
    private const byte OutputChannels = 2;

    private readonly ILogger _log;
    private readonly Sdl _sdl;

    private readonly List<SoundData> _sounds = new();
    private bool _deviceOpen;
    private uint _deviceId;

    private struct SoundData
    {
        public byte[]? Pcm;
        public int Frequency;
        public ushort Format;
        public byte Channels;
    }

    public AudioDevice(ILogger logger)
    {
        _log = logger.ForContext<AudioDevice>();
        _sdl = Sdl.GetApi();
    }

    public void Init()
    {
        if (_sdl.Init(Sdl.InitAudio) < 0)
        {
            throw new InvalidOperationException($"SDL audio init failed: {_sdl.GetErrorS()}");
        }
    }

    public Sound LoadSound(ReadOnlySpan<byte> fileBytes, string fileExtension)
    {
        EnsureDeviceOpen();

        byte[] pcm;
        if (fileExtension == ".wav")
        {
            var decoded = WavDecoder.Decode(fileBytes);
            pcm = ConvertToOutput(decoded.Pcm, decoded.Frequency, decoded.Channels, decoded.Format);
        }
        else if (fileExtension == ".mp3")
        {
            var decoded = Mp3Decoder.DecodeToFloat(fileBytes);
            pcm = ConvertToOutput(decoded.Samples, decoded.Frequency, decoded.Channels);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported audio file type: {fileExtension}");
        }

        _sounds.Add(new SoundData
        {
            Pcm = pcm,
            Frequency = OutputFrequency,
            Format = OutputFormat,
            Channels = OutputChannels
        });

        return new Sound(_sounds.Count);
    }

    public void UnloadSound(Sound sound)
    {
        if (!sound.IsValid)
        {
            return;
        }

        int index = sound.Index - 1;
        if ((uint)index >= (uint)_sounds.Count)
        {
            return;
        }

        _sounds[index] = default;
    }

    public unsafe void PlaySound(Sound sound)
    {
        if (!sound.IsValid)
        {
            return;
        }

        int index = sound.Index - 1;
        if ((uint)index >= (uint)_sounds.Count)
        {
            return;
        }

        if (!_deviceOpen || _deviceId == 0)
        {
            throw new InvalidOperationException("Audio device not initialized. Call InitAudioDevice() first.");
        }

        var data = _sounds[index].Pcm;
        if (data == null || data.Length == 0)
        {
            return;
        }

        _sdl.ClearQueuedAudio(_deviceId);
        fixed (byte* ptr = data)
        {
            int result = _sdl.QueueAudio(_deviceId, ptr, (uint)data.Length);
            if (result < 0)
            {
                _log.Warning("SDL_QueueAudio failed: {Error}", _sdl.GetErrorS());
            }
        }

        _sdl.PauseAudioDevice(_deviceId, 0);
    }

    public void StopAllSounds()
    {
        if (!_deviceOpen || _deviceId == 0)
        {
            return;
        }

        _sdl.ClearQueuedAudio(_deviceId);
    }

    private void EnsureDeviceOpen()
    {
        if (_deviceOpen)
        {
            return;
        }

        Init();

        var desired = new AudioSpec
        {
            Freq = OutputFrequency,
            Format = OutputFormat,
            Channels = OutputChannels,
            Samples = 4096,
            Callback = default,
            Userdata = null
        };

        AudioSpec obtained = default;
        unsafe
        {
            _deviceId = _sdl.OpenAudioDevice((byte*)null, 0, &desired, &obtained, 0);
        }
        if (_deviceId == 0)
        {
            throw new InvalidOperationException($"SDL_OpenAudioDevice failed: {_sdl.GetErrorS()}");
        }

        if (obtained.Freq != OutputFrequency || obtained.Format != OutputFormat || obtained.Channels != OutputChannels)
        {
            throw new InvalidOperationException(
                $"Audio device opened with unexpected spec. Expected {OutputFrequency}Hz fmt={OutputFormat} ch={OutputChannels}, " +
                $"got {obtained.Freq}Hz fmt={obtained.Format} ch={obtained.Channels}.");
        }

        _deviceOpen = true;
        _sdl.PauseAudioDevice(_deviceId, 1);

        _log.Information("Audio device opened: {Freq}Hz fmt={Format} ch={Channels}", obtained.Freq, obtained.Format, obtained.Channels);
    }

    private static byte[] ConvertToOutput(byte[] pcm, int frequency, byte channels, ushort format)
    {
        if (pcm.Length == 0)
        {
            return Array.Empty<byte>();
        }

        float[] samples;
        if (format == (ushort)Sdl.AudioS16Lsb || format == (ushort)Sdl.AudioS16Sys)
        {
            int sampleCount = pcm.Length / 2;
            samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short v = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                samples[i] = v / 32768f;
            }
        }
        else if (format == (ushort)Sdl.AudioF32Lsb || format == (ushort)Sdl.AudioF32Sys)
        {
            int sampleCount = pcm.Length / 4;
            samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = BitConverter.ToSingle(pcm, i * 4);
            }
        }
        else
        {
            throw new InvalidOperationException($"Unsupported decoded format: {format}");
        }

        return ConvertToOutput(samples, frequency, channels);
    }

    private static byte[] ConvertToOutput(float[] samples, int frequency, int channels)
    {
        if (samples.Length == 0)
        {
            return Array.Empty<byte>();
        }

        if (channels != 1 && channels != 2)
        {
            throw new InvalidOperationException($"Unsupported channel count: {channels}");
        }

        float[] stereoSamples = channels == 2 ? samples : ConvertMonoToStereo(samples);

        float[] resampled = frequency == OutputFrequency
            ? stereoSamples
            : ResampleLinear(stereoSamples, frequency, OutputFrequency, OutputChannels);

        int sampleCount = resampled.Length;
        var bytes = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            float s = resampled[i];
            if (s < -1f) s = -1f;
            if (s > 1f) s = 1f;
            short v = (short)MathF.Round(s * 32767f);
            bytes[i * 2] = (byte)(v & 0xFF);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }

        return bytes;
    }

    private static float[] ConvertMonoToStereo(float[] mono)
    {
        int frames = mono.Length;
        var stereo = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            float v = mono[i];
            int j = i * 2;
            stereo[j] = v;
            stereo[j + 1] = v;
        }
        return stereo;
    }

    private static float[] ResampleLinear(float[] input, int inputRate, int outputRate, int channels)
    {
        int inputFrames = input.Length / channels;
        if (inputFrames <= 1)
        {
            return input;
        }

        int outputFrames = (int)MathF.Round(inputFrames * (outputRate / (float)inputRate));
        if (outputFrames <= 1)
        {
            outputFrames = 1;
        }

        var output = new float[outputFrames * channels];
        float step = inputRate / (float)outputRate;

        for (int outFrame = 0; outFrame < outputFrames; outFrame++)
        {
            float srcPos = outFrame * step;
            int srcFrame = (int)srcPos;
            if (srcFrame >= inputFrames - 1)
            {
                srcFrame = inputFrames - 2;
                srcPos = srcFrame;
            }
            float frac = srcPos - srcFrame;

            int srcIndex0 = srcFrame * channels;
            int srcIndex1 = (srcFrame + 1) * channels;
            int dstIndex = outFrame * channels;

            for (int channelIndex = 0; channelIndex < channels; channelIndex++)
            {
                float a = input[srcIndex0 + channelIndex];
                float b = input[srcIndex1 + channelIndex];
                output[dstIndex + channelIndex] = a + (b - a) * frac;
            }
        }

        return output;
    }

    public void Dispose()
    {
        if (_deviceId != 0)
        {
            StopAllSounds();
            _sdl.CloseAudioDevice(_deviceId);
            _deviceId = 0;
        }

        _deviceOpen = false;
        _sounds.Clear();
    }
}
