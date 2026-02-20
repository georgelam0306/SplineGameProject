namespace DerpLib.Rendering;

/// <summary>
/// A loaded model consisting of multiple submeshes, each with its own texture.
/// </summary>
public readonly struct Model
{
    /// <summary>
    /// Array of submeshes, each registered as a separate mesh in the registry.
    /// </summary>
    public readonly SubmeshInfo[] Submeshes;

    /// <summary>
    /// Texture handles for this model's embedded textures.
    /// Submesh.TextureIndex indexes into this array.
    /// </summary>
    public readonly TextureHandle[] Textures;

    public Model(SubmeshInfo[] submeshes, TextureHandle[] textures)
    {
        Submeshes = submeshes;
        Textures = textures;
    }

    /// <summary>Total number of submeshes in this model.</summary>
    public int SubmeshCount => Submeshes?.Length ?? 0;

    /// <summary>Total number of embedded textures in this model.</summary>
    public int TextureCount => Textures?.Length ?? 0;
}

/// <summary>
/// Information about a single submesh within a model.
/// </summary>
public readonly struct SubmeshInfo
{
    /// <summary>Handle to the mesh in the registry.</summary>
    public readonly MeshHandle Mesh;

    /// <summary>
    /// Index into the Model.Textures array for this submesh's diffuse texture.
    /// -1 means no texture (use default white).
    /// </summary>
    public readonly int TextureIndex;

    public SubmeshInfo(MeshHandle mesh, int textureIndex)
    {
        Mesh = mesh;
        TextureIndex = textureIndex;
    }

    /// <summary>
    /// Gets the texture handle for this submesh, or Invalid if no texture.
    /// </summary>
    public TextureHandle GetTexture(Model model)
    {
        if (TextureIndex < 0 || TextureIndex >= model.TextureCount)
            return TextureHandle.Invalid;
        return model.Textures[TextureIndex];
    }
}
