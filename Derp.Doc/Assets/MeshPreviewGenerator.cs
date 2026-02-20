using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Derp.Doc.Model;
using DerpLib.AssetPipeline;
using DerpLib.Assets;
using StbImageSharp;
using StbImageWriteSharp;

namespace Derp.Doc.Assets;

internal sealed class MeshPreviewGenerator
{
    private const int PreviewSize = 128;
    private const int PreviewCacheVersion = 2;

    public enum PreviewRenderStatus
    {
        None,
        Ready,
        Loading,
        Missing,
        InvalidPath,
        PreviewUnavailable,
        Failed,
    }

    private static readonly string[] SupportedMeshExtensions =
    [
        ".obj",
        ".glb",
        ".gltf",
        ".fbx",
        ".dae",
        ".3ds",
        ".mesh",
    ];

    private static readonly string[] SupportedTextureExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".tga",
    ];

    private readonly ImageWriter _imageWriter = new();
    private string _cachedCompiledMeshPath = "";
    private long _cachedCompiledMeshWriteTicks = long.MinValue;
    private CompiledMesh? _cachedCompiledMesh;
    private string? _cachedTextureOverridePath;
    private long _cachedTextureOverrideWriteTicks = long.MinValue;
    private byte[] _cachedTexturePixels = Array.Empty<byte>();
    private int _cachedTextureWidth;
    private int _cachedTextureHeight;
    private Vector3[] _rotatedPositionsScratch = Array.Empty<Vector3>();
    private Vector2[] _screenPositionsScratch = Array.Empty<Vector2>();
    private Vector2[] _textureCoordinatesScratch = Array.Empty<Vector2>();
    private float[] _depthValuesScratch = Array.Empty<float>();
    private float[] _depthBufferScratch = Array.Empty<float>();
    private byte[] _previewPixelsScratch = Array.Empty<byte>();

    private readonly struct MeshPreviewPaths
    {
        public MeshPreviewPaths(
            string cachePath,
            long sourceWriteTicks,
            string compiledMeshPath,
            long compiledWriteTicks,
            string cacheDirectory,
            DocModelPreviewSettings previewSettings,
            string? textureOverridePath,
            long textureOverrideWriteTicks)
        {
            CachePath = cachePath;
            SourceWriteTicks = sourceWriteTicks;
            CompiledMeshPath = compiledMeshPath;
            CompiledWriteTicks = compiledWriteTicks;
            CacheDirectory = cacheDirectory;
            PreviewSettings = previewSettings;
            TextureOverridePath = textureOverridePath;
            TextureOverrideWriteTicks = textureOverrideWriteTicks;
        }

        public string CachePath { get; }
        public long SourceWriteTicks { get; }
        public string CompiledMeshPath { get; }
        public long CompiledWriteTicks { get; }
        public string CacheDirectory { get; }
        public DocModelPreviewSettings PreviewSettings { get; }
        public string? TextureOverridePath { get; }
        public long TextureOverrideWriteTicks { get; }
    }

    public bool TryResolveCachedPreviewPath(
        string assetsRoot,
        string relativePath,
        DocModelPreviewSettings? previewSettings,
        out string previewPath)
    {
        previewPath = "";

        if (!TryResolvePreviewPaths(
                assetsRoot,
                relativePath,
                previewSettings,
                out MeshPreviewPaths previewPaths,
                out PreviewRenderStatus _))
        {
            return false;
        }

        string cacheKey = BuildCacheKey(
            previewPaths.CachePath,
            previewPaths.SourceWriteTicks,
            previewPaths.CompiledWriteTicks,
            previewPaths.PreviewSettings,
            previewPaths.TextureOverrideWriteTicks);
        previewPath = Path.Combine(previewPaths.CacheDirectory, cacheKey + ".png");
        if (File.Exists(previewPath))
        {
            return true;
        }

        if (!TryGetCompiledMesh(
                previewPaths.CompiledMeshPath,
                previewPaths.CompiledWriteTicks,
                out CompiledMesh compiledMesh))
        {
            previewPath = "";
            return false;
        }

        if (!TryGetTexturePixels(
                previewPaths.TextureOverridePath,
                previewPaths.TextureOverrideWriteTicks,
                out byte[] texturePixels,
                out int textureWidth,
                out int textureHeight))
        {
            previewPath = "";
            return false;
        }

        if (!TryRenderPreviewPixels(
                compiledMesh,
                PreviewSize,
                PreviewSize,
                previewPaths.PreviewSettings,
                texturePixels,
                textureWidth,
                textureHeight,
                out byte[] previewPixels))
        {
            previewPath = "";
            return false;
        }

        try
        {
            using var outputStream = File.Create(previewPath);
            _imageWriter.WritePng(
                previewPixels,
                PreviewSize,
                PreviewSize,
                StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha,
                outputStream);
            return true;
        }
        catch
        {
            previewPath = "";
            return false;
        }
    }

    public PreviewRenderStatus TryRenderPreviewPixels(
        string assetsRoot,
        string relativePath,
        DocModelPreviewSettings? previewSettings,
        int width,
        int height,
        out byte[] previewPixels)
    {
        previewPixels = Array.Empty<byte>();

        if (width <= 0 || height <= 0)
        {
            return PreviewRenderStatus.InvalidPath;
        }

        if (!TryResolvePreviewPaths(
                assetsRoot,
                relativePath,
                previewSettings,
                out MeshPreviewPaths previewPaths,
                out PreviewRenderStatus resolveStatus))
        {
            return resolveStatus;
        }

        if (!TryGetCompiledMesh(
                previewPaths.CompiledMeshPath,
                previewPaths.CompiledWriteTicks,
                out CompiledMesh compiledMesh))
        {
            return PreviewRenderStatus.Failed;
        }

        if (!TryGetTexturePixels(
                previewPaths.TextureOverridePath,
                previewPaths.TextureOverrideWriteTicks,
                out byte[] texturePixels,
                out int textureWidth,
                out int textureHeight))
        {
            return PreviewRenderStatus.Failed;
        }

        if (!TryRenderPreviewPixels(
                compiledMesh,
                width,
                height,
                previewPaths.PreviewSettings,
                texturePixels,
                textureWidth,
                textureHeight,
                out previewPixels))
        {
            return PreviewRenderStatus.PreviewUnavailable;
        }

        return PreviewRenderStatus.Ready;
    }

    private bool TryResolvePreviewPaths(
        string assetsRoot,
        string relativePath,
        DocModelPreviewSettings? previewSettings,
        out MeshPreviewPaths previewPaths,
        out PreviewRenderStatus status)
    {
        previewPaths = default;
        status = PreviewRenderStatus.Failed;

        if (string.IsNullOrWhiteSpace(assetsRoot) || string.IsNullOrWhiteSpace(relativePath))
        {
            status = PreviewRenderStatus.InvalidPath;
            return false;
        }

        if (!TryNormalizeRelativePath(relativePath, out string normalizedRelativePath))
        {
            status = PreviewRenderStatus.InvalidPath;
            return false;
        }

        string sourceExtension = Path.GetExtension(normalizedRelativePath);
        if (!IsSupportedMeshExtension(sourceExtension))
        {
            status = PreviewRenderStatus.PreviewUnavailable;
            return false;
        }

        string fullAssetsRoot = Path.GetFullPath(assetsRoot);
        string fullSourcePath = Path.GetFullPath(Path.Combine(fullAssetsRoot, normalizedRelativePath));
        if (!fullSourcePath.StartsWith(fullAssetsRoot, StringComparison.Ordinal))
        {
            status = PreviewRenderStatus.InvalidPath;
            return false;
        }

        bool isCompiledMeshReference = string.Equals(sourceExtension, ".mesh", StringComparison.OrdinalIgnoreCase);
        bool sourceExists = File.Exists(fullSourcePath);
        if (!isCompiledMeshReference && !sourceExists)
        {
            status = PreviewRenderStatus.Missing;
            return false;
        }

        long sourceWriteTicks = sourceExists ? File.GetLastWriteTimeUtc(fullSourcePath).Ticks : 0;

        if (!TryResolveGameRoot(fullAssetsRoot, out string gameRoot))
        {
            status = PreviewRenderStatus.InvalidPath;
            return false;
        }

        if (!TryResolveCompiledMeshOutputPath(
                gameRoot,
                normalizedRelativePath,
                out string compiledMeshPath,
                out string cachePath))
        {
            status = PreviewRenderStatus.InvalidPath;
            return false;
        }

        bool compiledExists = File.Exists(compiledMeshPath);
        if (!compiledExists)
        {
            if (!isCompiledMeshReference)
            {
                _ = DocAssetServices.LazyAssetCompiler.EnsureMeshCompileQueued(fullAssetsRoot, normalizedRelativePath);
                status = PreviewRenderStatus.Loading;
            }
            else
            {
                status = PreviewRenderStatus.Missing;
            }

            return false;
        }

        long compiledWriteTicks = File.GetLastWriteTimeUtc(compiledMeshPath).Ticks;
        if (!isCompiledMeshReference && sourceWriteTicks > compiledWriteTicks)
        {
            _ = DocAssetServices.LazyAssetCompiler.EnsureMeshCompileQueued(fullAssetsRoot, normalizedRelativePath);
        }

        var resolvedPreviewSettings = previewSettings?.Clone() ?? new DocModelPreviewSettings();
        resolvedPreviewSettings.ClampInPlace();
        TryResolveTextureOverridePath(
            fullAssetsRoot,
            resolvedPreviewSettings.TextureRelativePath,
            out string? textureOverridePath,
            out long textureOverrideWriteTicks);

        string cacheDirectory = Path.Combine(gameRoot, "Database", ".derpdoc-cache", "mesh-previews");
        Directory.CreateDirectory(cacheDirectory);

        previewPaths = new MeshPreviewPaths(
            cachePath,
            sourceWriteTicks,
            compiledMeshPath,
            compiledWriteTicks,
            cacheDirectory,
            resolvedPreviewSettings,
            textureOverridePath,
            textureOverrideWriteTicks);
        status = PreviewRenderStatus.Ready;
        return true;
    }

    private static bool TryResolveCompiledMeshOutputPath(
        string gameRoot,
        string normalizedRelativePath,
        out string compiledMeshPath,
        out string cachePath)
    {
        compiledMeshPath = "";
        cachePath = "";

        string compiledRelativePath = string.Equals(
                Path.GetExtension(normalizedRelativePath),
                ".mesh",
                StringComparison.OrdinalIgnoreCase)
            ? normalizedRelativePath
            : normalizedRelativePath + ".mesh";

        string compiledMeshesRoot = Path.GetFullPath(Path.Combine(gameRoot, "data", "db", "meshes"));
        string candidatePath = Path.GetFullPath(Path.Combine(compiledMeshesRoot, compiledRelativePath));
        if (!candidatePath.StartsWith(compiledMeshesRoot, StringComparison.Ordinal))
        {
            return false;
        }

        compiledMeshPath = candidatePath;
        cachePath = compiledRelativePath;
        return true;
    }

    private static bool TryNormalizeRelativePath(string relativePath, out string normalizedRelativePath)
    {
        normalizedRelativePath = "";
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        string slashNormalized = relativePath.Trim().Replace('\\', '/');
        while (slashNormalized.StartsWith("/", StringComparison.Ordinal))
        {
            slashNormalized = slashNormalized[1..];
        }

        if (slashNormalized.Length == 0)
        {
            return false;
        }

        string[] segments = slashNormalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            string segment = segments[segmentIndex];
            if (segment == "." || segment == "..")
            {
                return false;
            }
        }

        normalizedRelativePath = string.Join('/', segments);
        return true;
    }

    private static bool TryLoadCompiledMesh(string compiledMeshPath, out CompiledMesh compiledMesh)
    {
        compiledMesh = null!;

        try
        {
            byte[] meshBytes = File.ReadAllBytes(compiledMeshPath);
            if (meshBytes.Length == 0)
            {
                return false;
            }

            IBlobSerializer serializer = ContentModule.CreateSerializer();
            if (ChunkHeader.TryParse(meshBytes, out ChunkHeader chunkHeader))
            {
                if (chunkHeader.OffsetToObject < 0 || chunkHeader.OffsetToObject >= meshBytes.Length)
                {
                    return false;
                }

                ReadOnlySpan<byte> payload = meshBytes.AsSpan(chunkHeader.OffsetToObject);
                compiledMesh = serializer.Deserialize<CompiledMesh>(payload);
                return true;
            }

            compiledMesh = serializer.Deserialize<CompiledMesh>(meshBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadTexturePixels(
        string? texturePath,
        out byte[] texturePixels,
        out int textureWidth,
        out int textureHeight)
    {
        texturePixels = Array.Empty<byte>();
        textureWidth = 0;
        textureHeight = 0;

        if (string.IsNullOrWhiteSpace(texturePath))
        {
            return true;
        }

        try
        {
            using var stream = File.OpenRead(texturePath);
            var image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (image.Width <= 0 || image.Height <= 0 || image.Data == null || image.Data.Length < image.Width * image.Height * 4)
            {
                return false;
            }

            texturePixels = image.Data;
            textureWidth = image.Width;
            textureHeight = image.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetCompiledMesh(
        string compiledMeshPath,
        long compiledWriteTicks,
        out CompiledMesh compiledMesh)
    {
        if (_cachedCompiledMesh != null &&
            _cachedCompiledMeshWriteTicks == compiledWriteTicks &&
            string.Equals(_cachedCompiledMeshPath, compiledMeshPath, StringComparison.Ordinal))
        {
            compiledMesh = _cachedCompiledMesh;
            return true;
        }

        if (!TryLoadCompiledMesh(compiledMeshPath, out compiledMesh))
        {
            return false;
        }

        _cachedCompiledMeshPath = compiledMeshPath;
        _cachedCompiledMeshWriteTicks = compiledWriteTicks;
        _cachedCompiledMesh = compiledMesh;
        return true;
    }

    private bool TryGetTexturePixels(
        string? texturePath,
        long textureWriteTicks,
        out byte[] texturePixels,
        out int textureWidth,
        out int textureHeight)
    {
        texturePixels = Array.Empty<byte>();
        textureWidth = 0;
        textureHeight = 0;

        if (string.IsNullOrWhiteSpace(texturePath))
        {
            return true;
        }

        if (string.Equals(_cachedTextureOverridePath, texturePath, StringComparison.Ordinal) &&
            _cachedTextureOverrideWriteTicks == textureWriteTicks &&
            _cachedTexturePixels.Length >= _cachedTextureWidth * _cachedTextureHeight * 4 &&
            _cachedTextureWidth > 0 &&
            _cachedTextureHeight > 0)
        {
            texturePixels = _cachedTexturePixels;
            textureWidth = _cachedTextureWidth;
            textureHeight = _cachedTextureHeight;
            return true;
        }

        if (!TryLoadTexturePixels(texturePath, out texturePixels, out textureWidth, out textureHeight))
        {
            return false;
        }

        _cachedTextureOverridePath = texturePath;
        _cachedTextureOverrideWriteTicks = textureWriteTicks;
        _cachedTexturePixels = texturePixels;
        _cachedTextureWidth = textureWidth;
        _cachedTextureHeight = textureHeight;
        return true;
    }

    private bool TryRenderPreviewPixels(
        CompiledMesh compiledMesh,
        int width,
        int height,
        DocModelPreviewSettings previewSettings,
        byte[] texturePixels,
        int textureWidth,
        int textureHeight,
        out byte[] pixels)
    {
        pixels = Array.Empty<byte>();

        if (compiledMesh == null ||
            compiledMesh.VertexCount <= 0 ||
            compiledMesh.Vertices == null ||
            compiledMesh.Vertices.Length < compiledMesh.VertexCount * 8 ||
            compiledMesh.Indices == null ||
            compiledMesh.Indices.Length < 3)
        {
            return false;
        }

        int vertexCount = compiledMesh.VertexCount;
        EnsureCapacity(ref _rotatedPositionsScratch, vertexCount);
        EnsureCapacity(ref _screenPositionsScratch, vertexCount);
        EnsureCapacity(ref _textureCoordinatesScratch, vertexCount);
        EnsureCapacity(ref _depthValuesScratch, vertexCount);

        Span<Vector3> rotatedPositions = _rotatedPositionsScratch.AsSpan(0, vertexCount);
        Span<Vector2> screenPositions = _screenPositionsScratch.AsSpan(0, vertexCount);
        Span<Vector2> textureCoordinates = _textureCoordinatesScratch.AsSpan(0, vertexCount);
        Span<float> depthValues = _depthValuesScratch.AsSpan(0, vertexCount);

        float orbitYawDegrees = WrapDegrees(previewSettings.OrbitYawDegrees);
        float orbitPitchDegrees = Math.Clamp(
            previewSettings.OrbitPitchDegrees,
            DocModelPreviewSettings.MinOrbitPitchDegrees,
            DocModelPreviewSettings.MaxOrbitPitchDegrees);
        float panX = Math.Clamp(previewSettings.PanX, DocModelPreviewSettings.MinPan, DocModelPreviewSettings.MaxPan);
        float panY = Math.Clamp(previewSettings.PanY, DocModelPreviewSettings.MinPan, DocModelPreviewSettings.MaxPan);
        float zoom = Math.Clamp(previewSettings.Zoom, DocModelPreviewSettings.MinZoom, DocModelPreviewSettings.MaxZoom);

        float yawRadians = MathF.PI * (orbitYawDegrees / 180f);
        float pitchRadians = MathF.PI * (orbitPitchDegrees / 180f);
        Matrix4x4 previewRotation =
            Matrix4x4.CreateRotationY(yawRadians) *
            Matrix4x4.CreateRotationX(pitchRadians);

        Vector3 minBounds = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 maxBounds = new(float.MinValue, float.MinValue, float.MinValue);

        for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            int sourceOffset = vertexIndex * 8;
            var sourcePosition = new Vector3(
                compiledMesh.Vertices[sourceOffset],
                compiledMesh.Vertices[sourceOffset + 1],
                compiledMesh.Vertices[sourceOffset + 2]);
            Vector3 rotatedPosition = Vector3.Transform(sourcePosition, previewRotation);
            rotatedPositions[vertexIndex] = rotatedPosition;

            textureCoordinates[vertexIndex] = new Vector2(
                compiledMesh.Vertices[sourceOffset + 6],
                compiledMesh.Vertices[sourceOffset + 7]);

            minBounds = Vector3.Min(minBounds, rotatedPosition);
            maxBounds = Vector3.Max(maxBounds, rotatedPosition);
        }

        Vector3 boundsSize = maxBounds - minBounds;
        float maxBoundsSize = MathF.Max(boundsSize.X, boundsSize.Y);
        if (maxBoundsSize <= 0.0001f)
        {
            return false;
        }

        Vector3 boundsCenter = (minBounds + maxBounds) * 0.5f;
        float scale = (MathF.Min(width, height) * 0.76f * zoom) / maxBoundsSize;
        float centerX = width * (0.5f + panX);
        float centerY = height * (0.56f - panY);

        for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            Vector3 centeredPosition = rotatedPositions[vertexIndex] - boundsCenter;
            screenPositions[vertexIndex] = new Vector2(
                centeredPosition.X * scale + centerX,
                -(centeredPosition.Y * scale) + centerY);
            depthValues[vertexIndex] = -centeredPosition.Z;
        }

        bool hasTexture = texturePixels.Length >= textureWidth * textureHeight * 4 &&
                          textureWidth > 0 &&
                          textureHeight > 0;

        int depthBufferLength = width * height;
        int previewPixelLength = depthBufferLength * 4;
        if (_depthBufferScratch.Length != depthBufferLength)
        {
            _depthBufferScratch = new float[depthBufferLength];
        }

        if (_previewPixelsScratch.Length != previewPixelLength)
        {
            _previewPixelsScratch = new byte[previewPixelLength];
        }

        Span<float> depthBuffer = _depthBufferScratch.AsSpan(0, depthBufferLength);
        Span<byte> previewPixels = _previewPixelsScratch.AsSpan(0, previewPixelLength);
        depthBuffer.Fill(float.PositiveInfinity);
        previewPixels.Clear();

        Vector3 lightDirection = Vector3.Normalize(new Vector3(-0.35f, 0.45f, 0.82f));
        int shadedPixelCount = 0;

        for (int triangleIndex = 0; triangleIndex <= compiledMesh.Indices.Length - 3; triangleIndex += 3)
        {
            int index0 = (int)compiledMesh.Indices[triangleIndex];
            int index1 = (int)compiledMesh.Indices[triangleIndex + 1];
            int index2 = (int)compiledMesh.Indices[triangleIndex + 2];

            if ((uint)index0 >= (uint)vertexCount ||
                (uint)index1 >= (uint)vertexCount ||
                (uint)index2 >= (uint)vertexCount)
            {
                continue;
            }

            Vector3 rotated0 = rotatedPositions[index0];
            Vector3 rotated1 = rotatedPositions[index1];
            Vector3 rotated2 = rotatedPositions[index2];
            Vector3 triangleNormal = Vector3.Cross(rotated1 - rotated0, rotated2 - rotated0);
            if (triangleNormal.LengthSquared() <= 1e-8f)
            {
                continue;
            }

            triangleNormal = Vector3.Normalize(triangleNormal);
            float lightStrength = Math.Clamp(0.30f + MathF.Max(0f, Vector3.Dot(triangleNormal, lightDirection)) * 0.70f, 0f, 1f);

            Vector2 screen0 = screenPositions[index0];
            Vector2 screen1 = screenPositions[index1];
            Vector2 screen2 = screenPositions[index2];

            float minXFloat = MathF.Min(screen0.X, MathF.Min(screen1.X, screen2.X));
            float minYFloat = MathF.Min(screen0.Y, MathF.Min(screen1.Y, screen2.Y));
            float maxXFloat = MathF.Max(screen0.X, MathF.Max(screen1.X, screen2.X));
            float maxYFloat = MathF.Max(screen0.Y, MathF.Max(screen1.Y, screen2.Y));

            int minPixelX = Math.Clamp((int)MathF.Floor(minXFloat), 0, width - 1);
            int minPixelY = Math.Clamp((int)MathF.Floor(minYFloat), 0, height - 1);
            int maxPixelX = Math.Clamp((int)MathF.Ceiling(maxXFloat), 0, width - 1);
            int maxPixelY = Math.Clamp((int)MathF.Ceiling(maxYFloat), 0, height - 1);

            float area = EdgeFunction(screen0, screen1, screen2);
            if (MathF.Abs(area) <= 1e-6f)
            {
                continue;
            }

            bool areaIsPositive = area > 0f;
            for (int pixelY = minPixelY; pixelY <= maxPixelY; pixelY++)
            {
                for (int pixelX = minPixelX; pixelX <= maxPixelX; pixelX++)
                {
                    Vector2 samplePoint = new(pixelX + 0.5f, pixelY + 0.5f);
                    float weight0 = EdgeFunction(screen1, screen2, samplePoint);
                    float weight1 = EdgeFunction(screen2, screen0, samplePoint);
                    float weight2 = EdgeFunction(screen0, screen1, samplePoint);

                    bool isInsideTriangle = areaIsPositive
                        ? (weight0 >= 0f && weight1 >= 0f && weight2 >= 0f)
                        : (weight0 <= 0f && weight1 <= 0f && weight2 <= 0f);
                    if (!isInsideTriangle)
                    {
                        continue;
                    }

                    float inverseArea = 1f / area;
                    weight0 *= inverseArea;
                    weight1 *= inverseArea;
                    weight2 *= inverseArea;

                    float depth = depthValues[index0] * weight0 +
                                  depthValues[index1] * weight1 +
                                  depthValues[index2] * weight2;
                    int depthBufferIndex = pixelY * width + pixelX;
                    if (depth >= depthBuffer[depthBufferIndex])
                    {
                        continue;
                    }

                    depthBuffer[depthBufferIndex] = depth;

                    byte baseRed = 188;
                    byte baseGreen = 198;
                    byte baseBlue = 220;
                    if (hasTexture)
                    {
                        Vector2 uv0 = textureCoordinates[index0];
                        Vector2 uv1 = textureCoordinates[index1];
                        Vector2 uv2 = textureCoordinates[index2];
                        Vector2 sampledUv =
                            uv0 * weight0 +
                            uv1 * weight1 +
                            uv2 * weight2;
                        SampleTextureColor(texturePixels, textureWidth, textureHeight, sampledUv, out baseRed, out baseGreen, out baseBlue);
                    }

                    float litStrength = Math.Clamp(0.20f + lightStrength * 0.80f, 0f, 1f);
                    byte red = (byte)Math.Clamp((int)MathF.Round(baseRed * litStrength), 0, 255);
                    byte green = (byte)Math.Clamp((int)MathF.Round(baseGreen * litStrength), 0, 255);
                    byte blue = (byte)Math.Clamp((int)MathF.Round(baseBlue * litStrength), 0, 255);

                    int pixelByteOffset = depthBufferIndex * 4;
                    previewPixels[pixelByteOffset] = red;
                    previewPixels[pixelByteOffset + 1] = green;
                    previewPixels[pixelByteOffset + 2] = blue;
                    previewPixels[pixelByteOffset + 3] = 255;
                    shadedPixelCount++;
                }
            }
        }

        pixels = _previewPixelsScratch;
        return shadedPixelCount > 0;
    }

    private static void SampleTextureColor(
        byte[] texturePixels,
        int textureWidth,
        int textureHeight,
        Vector2 uv,
        out byte red,
        out byte green,
        out byte blue)
    {
        float wrappedU = uv.X - MathF.Floor(uv.X);
        float wrappedV = uv.Y - MathF.Floor(uv.Y);
        int sampleX = Math.Clamp((int)MathF.Round(wrappedU * (textureWidth - 1)), 0, textureWidth - 1);
        int sampleY = Math.Clamp((int)MathF.Round(wrappedV * (textureHeight - 1)), 0, textureHeight - 1);
        int sourceOffset = (sampleY * textureWidth + sampleX) * 4;

        red = texturePixels[sourceOffset];
        green = texturePixels[sourceOffset + 1];
        blue = texturePixels[sourceOffset + 2];
    }

    private static void EnsureCapacity<T>(ref T[] scratch, int requiredLength)
    {
        if (scratch.Length < requiredLength)
        {
            scratch = new T[requiredLength];
        }
    }

    private static float WrapDegrees(float degrees)
    {
        float wrapped = degrees % 360f;
        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        else if (wrapped < -180f)
        {
            wrapped += 360f;
        }

        return wrapped;
    }

    private static float EdgeFunction(in Vector2 point0, in Vector2 point1, in Vector2 samplePoint)
    {
        return (samplePoint.X - point0.X) * (point1.Y - point0.Y) -
               (samplePoint.Y - point0.Y) * (point1.X - point0.X);
    }

    private static bool TryResolveGameRoot(string fullAssetsRoot, out string gameRoot)
    {
        gameRoot = "";

        var assetsDirectoryInfo = new DirectoryInfo(fullAssetsRoot);
        if (!string.Equals(assetsDirectoryInfo.Name, "Assets", StringComparison.Ordinal))
        {
            return false;
        }

        if (assetsDirectoryInfo.Parent == null)
        {
            return false;
        }

        gameRoot = assetsDirectoryInfo.Parent.FullName;
        return true;
    }

    private static bool TryResolveTextureOverridePath(
        string fullAssetsRoot,
        string? textureRelativePath,
        out string? textureOverridePath,
        out long textureOverrideWriteTicks)
    {
        textureOverridePath = null;
        textureOverrideWriteTicks = 0;

        if (string.IsNullOrWhiteSpace(textureRelativePath))
        {
            return true;
        }

        if (!TryNormalizeRelativePath(textureRelativePath, out string normalizedTextureRelativePath))
        {
            return false;
        }

        string extension = Path.GetExtension(normalizedTextureRelativePath);
        if (!IsSupportedTextureExtension(extension))
        {
            return false;
        }

        string candidatePath = Path.GetFullPath(Path.Combine(fullAssetsRoot, normalizedTextureRelativePath));
        if (!candidatePath.StartsWith(fullAssetsRoot, StringComparison.Ordinal))
        {
            return false;
        }

        if (!File.Exists(candidatePath))
        {
            return false;
        }

        textureOverridePath = candidatePath;
        textureOverrideWriteTicks = File.GetLastWriteTimeUtc(candidatePath).Ticks;
        return true;
    }

    private static bool IsSupportedMeshExtension(string extension)
    {
        for (int extensionIndex = 0; extensionIndex < SupportedMeshExtensions.Length; extensionIndex++)
        {
            if (string.Equals(extension, SupportedMeshExtensions[extensionIndex], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSupportedTextureExtension(string extension)
    {
        for (int extensionIndex = 0; extensionIndex < SupportedTextureExtensions.Length; extensionIndex++)
        {
            if (string.Equals(extension, SupportedTextureExtensions[extensionIndex], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildCacheKey(
        string cachePath,
        long sourceWriteTicks,
        long compiledWriteTicks,
        DocModelPreviewSettings previewSettings,
        long textureOverrideWriteTicks)
    {
        string settingsSignature = DocModelPreviewSettings.BuildCacheSignature(previewSettings);
        string hashInput = PreviewCacheVersion + "|" + cachePath + "|" + sourceWriteTicks + "|" + compiledWriteTicks + "|" + settingsSignature + "|" + textureOverrideWriteTicks;
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
