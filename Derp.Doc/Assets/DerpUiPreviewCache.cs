using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using Derp.UI;
using DerpLib.AssetPipeline;
using DerpLib.Rendering;
using DerpLib.Text;
using Serilog;
using DerpEngine = DerpLib.Derp;

namespace Derp.Doc.Assets;

internal sealed class DerpUiPreviewCache
{
    private const uint BduiMagic = 0x49554442;
    private const int PreviewWidth = 320;
    private const int PreviewHeight = 180;
    private const int MaxTrackedEntries = 96;
    private const float PrefabHalfScale = 0.5f;
    private const float PrefabQuarterScale = 0.25f;
    private static readonly long FailedRetryDelayTicks = TimeSpan.FromMilliseconds(250).Ticks;
    private static readonly long BudgetRetryDelayTicks = TimeSpan.FromMilliseconds(1500).Ticks;

    private static readonly MethodInfo? LoadFromPayloadMethod =
        typeof(CompiledUi).GetMethod("FromBduiPayload", BindingFlags.NonPublic | BindingFlags.Static);

    private readonly Dictionary<string, CachedPreview> _previewByKey =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _fontLoadAttempted;
    private bool _hasPreviewFont;
    private Font? _previewFont;
    private long _lastAccessSerial;

    public PreviewResult GetPreview(
        string? gameRoot,
        string? assetsRoot,
        string relativePath,
        PreviewRenderMode renderMode = PreviewRenderMode.Thumbnail)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new PreviewResult(PreviewStatus.InvalidPath, default, Vector2.Zero);
        }

        if (!TryResolveFullPath(gameRoot, assetsRoot, relativePath, out string fullPath))
        {
            return new PreviewResult(PreviewStatus.Missing, default, Vector2.Zero);
        }

        string extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, ".bdui", StringComparison.OrdinalIgnoreCase))
        {
            return new PreviewResult(PreviewStatus.PreviewUnavailable, default, Vector2.Zero);
        }

        long sourceWriteTicks;
        try
        {
            sourceWriteTicks = File.GetLastWriteTimeUtc(fullPath).Ticks;
        }
        catch
        {
            return new PreviewResult(PreviewStatus.Failed, default, Vector2.Zero);
        }

        string cacheKey = BuildCacheKey(fullPath, renderMode);
        if (_previewByKey.TryGetValue(cacheKey, out CachedPreview? cachedPreview))
        {
            cachedPreview.LastAccessSerial = ++_lastAccessSerial;
            bool sourceChanged = cachedPreview.SourceWriteTicks != sourceWriteTicks;
            bool hasReadySurface = cachedPreview.Status == PreviewStatus.Ready && cachedPreview.Surface != null;
            long utcNowTicks = DateTime.UtcNow.Ticks;

            if (!sourceChanged && hasReadySurface)
            {
                return new PreviewResult(PreviewStatus.Ready, cachedPreview.Surface!.Texture, cachedPreview.PrefabSize);
            }

            if (cachedPreview.NextRetryUtcTicks > utcNowTicks)
            {
                if (hasReadySurface)
                {
                    return new PreviewResult(PreviewStatus.Ready, cachedPreview.Surface!.Texture, cachedPreview.PrefabSize);
                }

                if (!sourceChanged)
                {
                    return new PreviewResult(cachedPreview.Status, default, cachedPreview.PrefabSize);
                }
            }

            CanvasSurface? previousSurface = cachedPreview.Surface;
            PreviewStatus previousStatus = cachedPreview.Status;
            Vector2 previousPrefabSize = cachedPreview.PrefabSize;
            long previousSourceWriteTicks = cachedPreview.SourceWriteTicks;
            bool builtPreview = TryBuildSurfaceWithEvictionRetry(
                fullPath,
                renderMode,
                previousSurface,
                cacheKey,
                out CanvasSurface? surface,
                out Vector2 prefabSize,
                out PreviewStatus buildStatus);
            if (!builtPreview || surface == null)
            {
                cachedPreview.NextRetryUtcTicks = utcNowTicks + GetRetryDelayTicks(buildStatus);
                if (previousStatus == PreviewStatus.Ready && previousSurface != null)
                {
                    cachedPreview.Status = PreviewStatus.Ready;
                    cachedPreview.Surface = previousSurface;
                    cachedPreview.PrefabSize = previousPrefabSize;
                    cachedPreview.SourceWriteTicks = previousSourceWriteTicks;
                    return new PreviewResult(PreviewStatus.Ready, previousSurface.Texture, previousPrefabSize);
                }

                cachedPreview.Status = buildStatus;
                cachedPreview.SourceWriteTicks = sourceWriteTicks;
                cachedPreview.DisposeSurface();
                cachedPreview.PrefabSize = Vector2.Zero;
                return new PreviewResult(cachedPreview.Status, default, cachedPreview.PrefabSize);
            }

            cachedPreview.Status = PreviewStatus.Ready;
            cachedPreview.SourceWriteTicks = sourceWriteTicks;
            cachedPreview.Surface = surface;
            cachedPreview.PrefabSize = prefabSize;
            cachedPreview.NextRetryUtcTicks = 0;
            return new PreviewResult(PreviewStatus.Ready, surface.Texture, prefabSize);
        }

        if (!EnsureCacheCapacityForInsert())
        {
            return new PreviewResult(PreviewStatus.PreviewUnavailable, default, Vector2.Zero);
        }

        cachedPreview = new CachedPreview
        {
            SourceWriteTicks = sourceWriteTicks,
            LastAccessSerial = ++_lastAccessSerial,
            Status = PreviewStatus.Failed,
        };
        _previewByKey[cacheKey] = cachedPreview;

        long buildUtcNowTicks = DateTime.UtcNow.Ticks;
        bool built = TryBuildSurfaceWithEvictionRetry(
            fullPath,
            renderMode,
            reusableSurface: null,
            cacheKey,
            out CanvasSurface? builtSurface,
            out Vector2 builtPrefabSize,
            out PreviewStatus builtStatus);
        if (!built || builtSurface == null)
        {
            cachedPreview.Status = builtStatus;
            cachedPreview.DisposeSurface();
            cachedPreview.PrefabSize = Vector2.Zero;
            cachedPreview.NextRetryUtcTicks = buildUtcNowTicks + GetRetryDelayTicks(builtStatus);
            return new PreviewResult(builtStatus, default, Vector2.Zero);
        }

        cachedPreview.Status = PreviewStatus.Ready;
        cachedPreview.Surface = builtSurface;
        cachedPreview.PrefabSize = builtPrefabSize;
        cachedPreview.NextRetryUtcTicks = 0;
        return new PreviewResult(PreviewStatus.Ready, builtSurface.Texture, builtPrefabSize);
    }

    private bool TryBuildSurfaceWithEvictionRetry(
        string fullPath,
        PreviewRenderMode renderMode,
        CanvasSurface? reusableSurface,
        string cacheKey,
        out CanvasSurface? surface,
        out Vector2 prefabSize,
        out PreviewStatus status)
    {
        if (TryBuildSurface(
                fullPath,
                renderMode,
                reusableSurface,
                out surface,
                out prefabSize,
                out status,
                out bool textureBudgetExceeded))
        {
            return true;
        }

        if (!textureBudgetExceeded)
        {
            return false;
        }

        for (int attemptIndex = 0; attemptIndex < MaxTrackedEntries; attemptIndex++)
        {
            bool evicted = TryEvictLeastRecentlyUsed(cacheKey);
            if (!evicted)
            {
                evicted = DocAssetServices.ThumbnailCache.TryEvictLeastRecentlyUsedReadyThumbnail();
            }

            if (!evicted)
            {
                break;
            }

            if (TryBuildSurface(
                    fullPath,
                    renderMode,
                    reusableSurface,
                    out surface,
                    out prefabSize,
                    out status,
                    out textureBudgetExceeded))
            {
                return true;
            }

            if (!textureBudgetExceeded)
            {
                return false;
            }
        }

        return false;
    }

    private bool TryBuildSurface(
        string fullPath,
        PreviewRenderMode renderMode,
        CanvasSurface? reusableSurface,
        out CanvasSurface? surface,
        out Vector2 prefabSize,
        out PreviewStatus status,
        out bool textureBudgetExceeded)
    {
        surface = null;
        prefabSize = Vector2.Zero;
        status = PreviewStatus.Failed;
        textureBudgetExceeded = false;

        if (!TryGetPreviewFont(out Font? loadedFont) || loadedFont == null)
        {
            status = PreviewStatus.PreviewUnavailable;
            return false;
        }

        Font font = loadedFont;

        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(fullPath);
        }
        catch
        {
            status = PreviewStatus.Missing;
            return false;
        }

        if (!TryExtractPayloadBytes(fileBytes, out byte[] payloadBytes))
        {
            status = PreviewStatus.PreviewUnavailable;
            return false;
        }

        if (!TryLoadCompiledUi(payloadBytes, out CompiledUi compiledUi))
        {
            status = PreviewStatus.PreviewUnavailable;
            return false;
        }

        CanvasSurface targetSurface = reusableSurface ?? new CanvasSurface();
        bool createdNewSurface = reusableSurface == null;
        try
        {
            var runtime = new UiRuntime();
            runtime.SetFont(font);
            runtime.Load(compiledUi);
            if (runtime.TryGetActivePrefabCanvasSize(out Vector2 activePrefabCanvasSize))
            {
                prefabSize = activePrefabCanvasSize;
            }

            int renderWidth = PreviewWidth;
            int renderHeight = PreviewHeight;
            if (renderMode == PreviewRenderMode.PrefabSize ||
                renderMode == PreviewRenderMode.PrefabHalf ||
                renderMode == PreviewRenderMode.PrefabQuarter)
            {
                if (prefabSize.X <= 0f || prefabSize.Y <= 0f)
                {
                    status = PreviewStatus.PreviewUnavailable;
                    return false;
                }

                float previewScale = renderMode switch
                {
                    PreviewRenderMode.PrefabHalf => PrefabHalfScale,
                    PreviewRenderMode.PrefabQuarter => PrefabQuarterScale,
                    _ => 1f,
                };

                renderWidth = Math.Max(1, (int)MathF.Ceiling(prefabSize.X * previewScale));
                renderHeight = Math.Max(1, (int)MathF.Ceiling(prefabSize.Y * previewScale));
                if (renderMode == PreviewRenderMode.PrefabSize)
                {
                    _ = runtime.TrySetActivePrefabCanvasSize(renderWidth, renderHeight, resolveLayout: true);
                    // Keep 1:1 scale for prefab-sized previews while aligning prefab canvas space to texture space.
                    _ = runtime.TryAutoFitActivePrefabToCanvas(
                        renderWidth,
                        renderHeight,
                        paddingFraction: 0f,
                        minZoom: 1f,
                        maxZoom: 1f);
                }
                else
                {
                    // For scaled prefab previews, keep layout space and fit into a smaller render target.
                    _ = runtime.TryAutoFitActivePrefabToCanvas(renderWidth, renderHeight, paddingFraction: 0f);
                }
            }

            runtime.Tick(
                0u,
                new UiPointerFrameInput(
                    pointerValid: false,
                    pointerWorld: default,
                    primaryDown: false,
                    wheelDelta: 0f,
                    hoveredStableId: 0));
            if (renderMode == PreviewRenderMode.Thumbnail)
            {
                _ = runtime.TryAutoFitActivePrefabToCanvas(PreviewWidth, PreviewHeight, paddingFraction: 0.08f);
            }

            targetSurface.SetFontAtlas(font.Atlas);
            runtime.BuildFrame(targetSurface, renderWidth, renderHeight);
            targetSurface.DispatchToTexture();
            surface = targetSurface;
            status = PreviewStatus.Ready;
            return true;
        }
        catch (Exception ex)
        {
            textureBudgetExceeded = IsTextureArrayFullException(ex);
            if (textureBudgetExceeded)
            {
                Log.Logger.Debug(
                    ex,
                    "DerpUiPreviewCache budget failure for {Path} ({RenderMode})",
                    fullPath,
                    renderMode);
            }
            else
            {
                Log.Logger.Debug(
                    ex,
                    "DerpUiPreviewCache build failure for {Path} ({RenderMode})",
                    fullPath,
                    renderMode);
            }
            if (createdNewSurface)
            {
                targetSurface.Dispose();
                surface = null;
            }
            else
            {
                surface = reusableSurface;
            }

            status = textureBudgetExceeded
                ? PreviewStatus.BudgetExceeded
                : PreviewStatus.Failed;
            return false;
        }
    }

    private static bool TryResolveFullPath(
        string? gameRoot,
        string? assetsRoot,
        string relativePath,
        out string fullPath)
    {
        string? resolvedGameRoot = string.IsNullOrWhiteSpace(gameRoot)
            ? null
            : Path.GetFullPath(gameRoot);
        string? resolvedAssetsRoot = string.IsNullOrWhiteSpace(assetsRoot)
            ? null
            : Path.GetFullPath(assetsRoot);

        if (!string.IsNullOrWhiteSpace(resolvedGameRoot))
        {
            string gameRootLeaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(resolvedGameRoot));
            if (string.Equals(gameRootLeaf, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                resolvedAssetsRoot ??= resolvedGameRoot;
                DirectoryInfo? parentDirectory = Directory.GetParent(resolvedGameRoot);
                if (parentDirectory != null)
                {
                    resolvedGameRoot = parentDirectory.FullName;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(resolvedAssetsRoot))
        {
            string assetsRootLeaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(resolvedAssetsRoot));
            if (string.Equals(assetsRootLeaf, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(resolvedGameRoot))
                {
                    DirectoryInfo? parentDirectory = Directory.GetParent(resolvedAssetsRoot);
                    if (parentDirectory != null)
                    {
                        resolvedGameRoot = parentDirectory.FullName;
                    }
                }
            }
            else if (string.IsNullOrWhiteSpace(resolvedGameRoot))
            {
                string candidateAssetsRoot = Path.Combine(resolvedAssetsRoot, "Assets");
                if (Directory.Exists(candidateAssetsRoot))
                {
                    resolvedGameRoot = resolvedAssetsRoot;
                    resolvedAssetsRoot = candidateAssetsRoot;
                }
            }
        }

        if (Path.IsPathRooted(relativePath))
        {
            fullPath = Path.GetFullPath(relativePath);
            return File.Exists(fullPath);
        }

        string normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalizedPath.Length == 0)
        {
            fullPath = "";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(resolvedGameRoot) &&
            normalizedPath.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
        {
            string resourcesRelativePath = normalizedPath["Resources/".Length..];
            fullPath = Path.Combine(resolvedGameRoot, "Resources", resourcesRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(resolvedGameRoot) &&
            normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            string assetsRelativePath = normalizedPath["Assets/".Length..];
            fullPath = Path.Combine(resolvedGameRoot, "Assets", assetsRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(resolvedAssetsRoot))
        {
            fullPath = Path.Combine(resolvedAssetsRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(resolvedGameRoot))
        {
            fullPath = Path.Combine(resolvedGameRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                return true;
            }

            fullPath = Path.Combine(resolvedGameRoot, "Assets", normalizedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                return true;
            }

            fullPath = Path.Combine(resolvedGameRoot, "Resources", normalizedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                return true;
            }
        }

        fullPath = "";
        return false;
    }

    private bool TryGetPreviewFont(out Font? font)
    {
        if (_hasPreviewFont)
        {
            font = _previewFont;
            return true;
        }

        if (_fontLoadAttempted)
        {
            font = null;
            return false;
        }

        _fontLoadAttempted = true;
        try
        {
            _previewFont = DerpEngine.LoadFont("arial");
            _hasPreviewFont = true;
            font = _previewFont;
            return true;
        }
        catch
        {
            font = null;
            return false;
        }
    }

    private static bool TryLoadCompiledUi(byte[] payloadBytes, out CompiledUi compiledUi)
    {
        compiledUi = null!;
        if (LoadFromPayloadMethod == null)
        {
            return false;
        }

        try
        {
            object? result = LoadFromPayloadMethod.Invoke(null, [payloadBytes]);
            if (result is CompiledUi parsedUi)
            {
                compiledUi = parsedUi;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static int FindPayloadOffset(byte[] fileBytes)
    {
        ReadOnlySpan<byte> span = fileBytes;
        for (int byteIndex = 0; byteIndex <= span.Length - 4; byteIndex++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(byteIndex, 4));
            if (value == BduiMagic)
            {
                return byteIndex;
            }
        }

        return -1;
    }

    private static string BuildCacheKey(string fullPath, PreviewRenderMode renderMode)
    {
        return renderMode switch
        {
            PreviewRenderMode.Thumbnail => fullPath + "|thumb",
            PreviewRenderMode.PrefabHalf => fullPath + "|prefab_half",
            PreviewRenderMode.PrefabQuarter => fullPath + "|prefab_quarter",
            _ => fullPath + "|prefab",
        };
    }

    private bool EnsureCacheCapacityForInsert()
    {
        while (_previewByKey.Count >= MaxTrackedEntries)
        {
            if (!TryEvictLeastRecentlyUsed())
            {
                return false;
            }
        }

        return true;
    }

    private bool TryEvictLeastRecentlyUsed(string? excludedKey = null)
    {
        string oldestKey = "";
        long oldestAccessSerial = long.MaxValue;
        foreach (KeyValuePair<string, CachedPreview> entry in _previewByKey)
        {
            if (!string.IsNullOrWhiteSpace(excludedKey) &&
                string.Equals(entry.Key, excludedKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Value.LastAccessSerial < oldestAccessSerial)
            {
                oldestAccessSerial = entry.Value.LastAccessSerial;
                oldestKey = entry.Key;
            }
        }

        if (string.IsNullOrWhiteSpace(oldestKey))
        {
            return false;
        }

        if (_previewByKey.TryGetValue(oldestKey, out CachedPreview? cachedPreview))
        {
            cachedPreview.DisposeSurface();
        }

        _previewByKey.Remove(oldestKey);
        return true;
    }

    private static bool IsTextureArrayFullException(Exception exception)
    {
        Exception? current = exception;
        while (current != null)
        {
            if (current is InvalidOperationException invalidOperationException &&
                invalidOperationException.Message.Contains("TextureArray full", StringComparison.Ordinal))
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    private static long GetRetryDelayTicks(PreviewStatus status)
    {
        return status == PreviewStatus.BudgetExceeded
            ? BudgetRetryDelayTicks
            : FailedRetryDelayTicks;
    }

    private static bool TryExtractPayloadBytes(byte[] fileBytes, out byte[] payloadBytes)
    {
        payloadBytes = Array.Empty<byte>();
        if (fileBytes.Length == 0)
        {
            return false;
        }

        if (ChunkHeader.TryParse(fileBytes, out ChunkHeader chunkHeader))
        {
            if (chunkHeader.OffsetToObject < 0 || chunkHeader.OffsetToObject >= fileBytes.Length)
            {
                return false;
            }

            int chunkPayloadLength = fileBytes.Length - chunkHeader.OffsetToObject;
            if (chunkPayloadLength <= 0)
            {
                return false;
            }

            payloadBytes = new byte[chunkPayloadLength];
            Array.Copy(fileBytes, chunkHeader.OffsetToObject, payloadBytes, 0, chunkPayloadLength);
            return true;
        }

        int payloadOffset = FindPayloadOffset(fileBytes);
        if (payloadOffset < 0)
        {
            return false;
        }

        int payloadLength = fileBytes.Length - payloadOffset;
        if (payloadLength <= 0)
        {
            return false;
        }

        payloadBytes = new byte[payloadLength];
        Array.Copy(fileBytes, payloadOffset, payloadBytes, 0, payloadLength);
        return true;
    }

    public enum PreviewStatus
    {
        Ready,
        InvalidPath,
        Missing,
        PreviewUnavailable,
        BudgetExceeded,
        Failed,
    }

    public enum PreviewRenderMode
    {
        Thumbnail,
        PrefabSize,
        PrefabHalf,
        PrefabQuarter,
    }

    public readonly struct PreviewResult
    {
        public PreviewResult(PreviewStatus status, Texture texture, Vector2 prefabSize)
        {
            Status = status;
            Texture = texture;
            PrefabSize = prefabSize;
        }

        public PreviewStatus Status { get; }
        public Texture Texture { get; }
        public Vector2 PrefabSize { get; }
    }

    private sealed class CachedPreview
    {
        public long SourceWriteTicks;
        public long LastAccessSerial;
        public PreviewStatus Status;
        public CanvasSurface? Surface;
        public Vector2 PrefabSize;
        public long NextRetryUtcTicks;

        public void DisposeSurface()
        {
            Surface?.Dispose();
            Surface = null;
        }
    }
}
