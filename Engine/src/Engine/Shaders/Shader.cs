using DerpLib.AssetPipeline;
using DerpLib.Assets;

namespace DerpLib.Shaders;

/// <summary>
/// A loaded shader. Just holds the SPIR-V bytes.
/// Pipeline is created lazily at draw time based on current RenderState.
/// </summary>
public sealed class Shader
{
    private static int _nextId = 0;
    private static ContentManager? _content;
    private static PipelineCache? _defaultCache;

    /// <summary>Unique ID for cache keying.</summary>
    public int Id { get; }

    /// <summary>Vertex shader SPIR-V bytes.</summary>
    public byte[] VertexBytes { get; }

    /// <summary>Fragment shader SPIR-V bytes.</summary>
    public byte[] FragmentBytes { get; }

    private Shader(byte[] vertexBytes, byte[] fragmentBytes)
    {
        Id = Interlocked.Increment(ref _nextId);
        VertexBytes = vertexBytes;
        FragmentBytes = fragmentBytes;
    }

    /// <summary>
    /// Sets the ContentManager for shader loading. Called during engine composition.
    /// </summary>
    public static void SetContentManager(ContentManager content) => _content = content;

    /// <summary>
    /// Sets the default pipeline cache. Called by the engine during initialization.
    /// </summary>
    public static void SetDefaultCache(PipelineCache cache) => _defaultCache = cache;

    /// <summary>
    /// Gets the default pipeline cache.
    /// </summary>
    public static PipelineCache? DefaultCache => _defaultCache;

    /// <summary>
    /// Load a shader by name using ContentManager. Loads shaders/{name}.shader.
    /// </summary>
    public static Shader Load(string name)
    {
        if (_content is null)
            throw new InvalidOperationException("Shader.SetContentManager must be called before loading shaders.");

        var compiled = _content.Load<CompiledShader>($"shaders/{name}.shader");
        return new Shader(compiled.VertexSpirv, compiled.FragmentSpirv);
    }

    /// <summary>
    /// Load a shader from SPIR-V bytes.
    /// </summary>
    public static Shader FromBytes(byte[] vertBytes, byte[] fragBytes)
    {
        return new Shader(vertBytes, fragBytes);
    }
}
