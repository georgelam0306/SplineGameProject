using DerpLib.AssetPipeline;
using DerpLib.Assets;
using Serilog;
using Silk.NET.Assimp;
using StbImageSharp;

namespace DerpLib.Build;

[Compiler(typeof(ModelAsset))]
public sealed class MeshCompiler : IAssetCompiler
{
    private readonly ILogger _log;

    public MeshCompiler(ILogger log)
    {
        _log = log;
    }

    public IEnumerable<string> GetInputFiles(AssetItem item)
    {
        var asset = (ModelAsset)item.Asset;
        yield return asset.Source;
    }

    public unsafe ObjectId Compile(AssetItem item, IObjectDatabase db, IBlobSerializer serializer)
    {
        var asset = (ModelAsset)item.Asset;
        var sourceFile = asset.Source;

        if (!System.IO.File.Exists(sourceFile))
        {
            throw new FileNotFoundException($"Mesh source not found: {sourceFile}");
        }

        using var assimp = Assimp.GetApi();

        // Post-processing flags for Vulkan compatibility
        var flags = PostProcessSteps.Triangulate |
                    PostProcessSteps.GenerateNormals |
                    PostProcessSteps.FlipUVs |  // Vulkan uses Y-down UVs
                    PostProcessSteps.JoinIdenticalVertices;

        var scene = assimp.ImportFile(sourceFile, (uint)flags);
        if (scene == null || scene->MFlags == (uint)SceneFlags.Incomplete || scene->MRootNode == null)
        {
            var error = assimp.GetErrorStringS();
            throw new InvalidOperationException($"Assimp failed to load '{sourceFile}': {error}");
        }

        try
        {
            // === Extract embedded textures ===
            var embeddedTextures = ExtractEmbeddedTextures(scene);
            _log.Debug("Extracted {Count} embedded textures", embeddedTextures.Count);

            // === Build material -> texture index mapping ===
            // For each material, find its diffuse texture index
            var materialTextureMap = BuildMaterialTextureMap(assimp, scene);

            // === Process meshes ===
            // Count total vertices and indices
            int totalVertices = 0;
            int totalIndices = 0;

            for (uint m = 0; m < scene->MNumMeshes; m++)
            {
                var mesh = scene->MMeshes[m];
                totalVertices += (int)mesh->MNumVertices;

                for (uint f = 0; f < mesh->MNumFaces; f++)
                {
                    totalIndices += (int)mesh->MFaces[f].MNumIndices;
                }
            }

            // Allocate output arrays
            var vertices = new float[totalVertices * 8]; // pos(3) + normal(3) + uv(2)
            var indices = new uint[totalIndices];
            var submeshes = new List<Submesh>();

            int vertexOffset = 0;
            int indexOffset = 0;
            uint baseVertex = 0;

            for (uint m = 0; m < scene->MNumMeshes; m++)
            {
                var mesh = scene->MMeshes[m];
                var hasNormals = mesh->MNormals != null;
                var hasTexCoords = mesh->MTextureCoords[0] != null;

                int submeshIndexStart = indexOffset;

                // Copy vertices
                for (uint v = 0; v < mesh->MNumVertices; v++)
                {
                    int vi = vertexOffset + (int)v * 8;

                    // Position
                    vertices[vi + 0] = mesh->MVertices[v].X;
                    vertices[vi + 1] = mesh->MVertices[v].Y;
                    vertices[vi + 2] = mesh->MVertices[v].Z;

                    // Normal
                    if (hasNormals)
                    {
                        vertices[vi + 3] = mesh->MNormals[v].X;
                        vertices[vi + 4] = mesh->MNormals[v].Y;
                        vertices[vi + 5] = mesh->MNormals[v].Z;
                    }
                    else
                    {
                        vertices[vi + 3] = 0f;
                        vertices[vi + 4] = 1f;
                        vertices[vi + 5] = 0f;
                    }

                    // TexCoord
                    if (hasTexCoords)
                    {
                        vertices[vi + 6] = mesh->MTextureCoords[0][v].X;
                        vertices[vi + 7] = mesh->MTextureCoords[0][v].Y;
                    }
                    else
                    {
                        vertices[vi + 6] = 0f;
                        vertices[vi + 7] = 0f;
                    }
                }

                vertexOffset += (int)mesh->MNumVertices * 8;

                // Copy indices (with base vertex offset for multi-mesh)
                for (uint f = 0; f < mesh->MNumFaces; f++)
                {
                    var face = mesh->MFaces[f];
                    for (uint i = 0; i < face.MNumIndices; i++)
                    {
                        indices[indexOffset++] = face.MIndices[i] + baseVertex;
                    }
                }

                // Get texture index for this mesh's material
                int texIndex = -1;
                if (materialTextureMap.TryGetValue((int)mesh->MMaterialIndex, out var ti))
                {
                    texIndex = ti;
                }

                submeshes.Add(new Submesh
                {
                    IndexOffset = submeshIndexStart,
                    IndexCount = indexOffset - submeshIndexStart,
                    TextureIndex = texIndex
                });

                baseVertex += mesh->MNumVertices;
            }

            _log.Information("Compiled {Source} -> {Vertices} vertices, {Indices} indices, {Submeshes} submeshes, {Textures} textures",
                Path.GetFileName(sourceFile), totalVertices, totalIndices, submeshes.Count, embeddedTextures.Count);

            var compiled = new CompiledMesh
            {
                VertexCount = totalVertices,
                IndexCount = totalIndices,
                Vertices = vertices,
                Indices = indices,
                Submeshes = submeshes.ToArray(),
                EmbeddedTextures = embeddedTextures.ToArray()
            };

            return db.Put(serializer.Serialize(compiled));
        }
        finally
        {
            assimp.ReleaseImport(scene);
        }
    }

    /// <summary>
    /// Extracts all embedded textures from the scene.
    /// </summary>
    private unsafe List<CompiledTexture> ExtractEmbeddedTextures(Scene* scene)
    {
        var result = new List<CompiledTexture>();

        for (uint i = 0; i < scene->MNumTextures; i++)
        {
            var tex = scene->MTextures[i];
            if (tex == null) continue;

            byte[] rgba;
            int width, height;

            if (tex->MHeight == 0)
            {
                // Compressed format (PNG, JPG, etc.) - MWidth is byte length
                int len = (int)tex->MWidth;
                if (len <= 0) continue;

                var compressed = new byte[len];
                fixed (byte* dst = compressed)
                {
                    System.Buffer.MemoryCopy(tex->PcData, dst, len, len);
                }

                try
                {
                    var img = ImageResult.FromMemory(compressed, ColorComponents.RedGreenBlueAlpha);
                    width = img.Width;
                    height = img.Height;
                    rgba = img.Data;
                    _log.Debug("Decoded embedded texture {Index}: {Width}x{Height} (compressed {Bytes} bytes)",
                        i, width, height, len);
                }
                catch (Exception ex)
                {
                    _log.Warning("Failed to decode embedded texture {Index}: {Error}", i, ex.Message);
                    continue;
                }
            }
            else
            {
                // Raw RGBA format
                width = (int)tex->MWidth;
                height = (int)tex->MHeight;
                long byteCount = (long)width * height * 4;
                if (byteCount <= 0 || byteCount > int.MaxValue) continue;

                rgba = new byte[byteCount];
                fixed (byte* dst = rgba)
                {
                    System.Buffer.MemoryCopy(tex->PcData, dst, byteCount, byteCount);
                }
                _log.Debug("Extracted raw embedded texture {Index}: {Width}x{Height}", i, width, height);
            }

            result.Add(new CompiledTexture
            {
                Width = width,
                Height = height,
                Pixels = rgba
            });
        }

        return result;
    }

    /// <summary>
    /// Builds a map from material index to embedded texture index.
    /// </summary>
    private unsafe Dictionary<int, int> BuildMaterialTextureMap(Assimp assimp, Scene* scene)
    {
        var map = new Dictionary<int, int>();

        for (uint m = 0; m < scene->MNumMaterials; m++)
        {
            var mat = scene->MMaterials[m];

            // Get the diffuse texture path
            var texPath = new AssimpString();
            var result = assimp.GetMaterialTexture(mat, TextureType.Diffuse, 0, ref texPath,
                null, null, null, null, null, null);

            if (result != Return.Success) continue;

            var path = texPath.AsString;
            if (string.IsNullOrEmpty(path)) continue;

            // Embedded textures have paths like "*0", "*1", etc.
            if (path.StartsWith("*") && int.TryParse(path.AsSpan(1), out int texIndex))
            {
                map[(int)m] = texIndex;
                _log.Debug("Material {MatIndex} -> embedded texture {TexIndex}", m, texIndex);
            }
            else
            {
                // External texture path - not handled yet
                _log.Debug("Material {MatIndex} references external texture: {Path}", m, path);
            }
        }

        return map;
    }
}
