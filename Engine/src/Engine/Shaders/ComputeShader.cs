using DerpLib.AssetPipeline;
using DerpLib.Assets;

namespace DerpLib.Shaders;

/// <summary>
/// A loaded compute shader. Holds the SPIR-V bytes.
/// </summary>
public sealed class ComputeShader
{
    private static int _nextId;
    private static ContentManager? _content;

    /// <summary>Unique ID for cache keying.</summary>
    public int Id { get; }

    /// <summary>Compute shader SPIR-V bytes.</summary>
    public byte[] Bytes { get; }

    private ComputeShader(byte[] bytes)
    {
        Id = Interlocked.Increment(ref _nextId);
        Bytes = bytes;
    }

    /// <summary>
    /// Sets the ContentManager for shader loading. Called during engine composition.
    /// </summary>
    public static void SetContentManager(ContentManager content) => _content = content;

    /// <summary>
    /// Load a compute shader by name. Loads shaders/{name}.compute.
    /// </summary>
    public static ComputeShader Load(string name)
    {
        if (_content is null)
            throw new InvalidOperationException("ComputeShader.SetContentManager must be called before loading shaders.");

        var compiled = _content.Load<CompiledComputeShader>($"shaders/{name}.compute");
        return new ComputeShader(compiled.Spirv);
    }

    /// <summary>
    /// Load a compute shader from SPIR-V bytes.
    /// </summary>
    public static ComputeShader FromBytes(byte[] bytes)
    {
        return new ComputeShader(bytes);
    }
}
