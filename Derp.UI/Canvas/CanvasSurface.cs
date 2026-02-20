using DerpLib.Rendering;
using DerpLib.Sdf;
using Serilog;
using DerpEngine = DerpLib.Derp;

namespace Derp.UI;

public sealed class CanvasSurface : IDisposable
{
    private SdfRenderer? _renderer;
    private Texture _outputTexture;
    private bool _hasOutputTexture;

    private Texture _fontAtlas;
    private bool _hasFontAtlas;

    public bool HasFrame { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public SdfBuffer Buffer
    {
        get
        {
            if (_renderer == null)
            {
                throw new InvalidOperationException("CanvasSurface is not initialized.");
            }
            return _renderer.Buffer;
        }
    }

    public Texture Texture
    {
        get
        {
            if (!_hasOutputTexture)
            {
                throw new InvalidOperationException("CanvasSurface output texture not initialized.");
            }
            return _outputTexture;
        }
    }

    public void SetFontAtlas(Texture atlas)
    {
        _fontAtlas = atlas;
        _hasFontAtlas = true;

        if (_renderer != null)
        {
            DerpEngine.SetSdfFontAtlas(_renderer, atlas);
        }
    }

    public void BeginFrame(int width, int height)
    {
        EnsureInitialized(width, height);
        HasFrame = true;
        _renderer!.Reset();
    }

    public void DispatchToTexture()
    {
        if (!HasFrame || _renderer == null)
        {
            return;
        }

        if (_renderer.Buffer.Count == 0)
        {
            HasFrame = false;
            return;
        }

        var cmd = DerpEngine.GetCommandBuffer();
        int frameIndex = DerpEngine.FrameIndex;

        _renderer.Resize((uint)Width, (uint)Height);
        SyncOutputTexture();

        _renderer.Buffer.Build(Width, Height);
        _renderer.Buffer.Flush(frameIndex);
        _renderer.Dispatch(cmd, frameIndex);
        _renderer.TransitionOutputForSampling(cmd);
        _renderer.Reset();

        HasFrame = false;
    }

    private void EnsureInitialized(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            width = 1;
            height = 1;
        }

        Width = width;
        Height = height;

        if (_renderer == null)
        {
            _renderer = new SdfRenderer(
                Log.Logger,
                DerpEngine.Device,
                DerpEngine.MemoryAllocator,
                DerpEngine.DescriptorCache,
                DerpEngine.PipelineCache,
                (uint)width,
                (uint)height,
                maxCommands: 16384);

            _renderer.SetShader(DerpEngine.SdfShader);

            if (_hasFontAtlas)
            {
                DerpEngine.SetSdfFontAtlas(_renderer, _fontAtlas);
            }

            _outputTexture = DerpEngine.RegisterExternalTexture(_renderer.StorageImage.ImageView, width, height);
            _hasOutputTexture = true;
            return;
        }

        _renderer.Resize((uint)width, (uint)height);
        SyncOutputTexture();
    }

    private void SyncOutputTexture()
    {
        if (!_hasOutputTexture || _renderer == null)
        {
            return;
        }

        int width = (int)_renderer.StorageImage.Width;
        int height = (int)_renderer.StorageImage.Height;

        if (_outputTexture.Width == width && _outputTexture.Height == height)
        {
            return;
        }

        _outputTexture = DerpEngine.UpdateExternalTexture(_outputTexture, _renderer.StorageImage.ImageView, width, height);
    }

    public void Dispose()
    {
        if (_hasOutputTexture)
        {
            DerpEngine.UnregisterExternalTexture(_outputTexture);
            _outputTexture = default;
            _hasOutputTexture = false;
        }

        _renderer?.Dispose();
        _renderer = null;
        HasFrame = false;
    }
}
