using DerpEngine = DerpLib.Derp;
using Derp.Doc.Model;
using DerpLib.Rendering;
using StbImageSharp;

namespace Derp.Doc.Assets;

internal sealed class AssetThumbnailCache
{
    private const int MaxThumbnailDimension = 128;
    private const int MaxGpuThumbnails = 48;
    private const int ReservedTextureSlotsForUiPreviews = 24;
    private const int MaxTrackedEntries = 256;
    private const int RetryDelayFrames = 120;

    private static readonly string[] SupportedTextureExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".tga",
    ];

    private sealed class CachedThumbnail
    {
        public ThumbnailStatus Status;
        public Texture Texture;
        public bool HasGpuTexture;
        public string SourcePath = "";
        public long SourceWriteTicks;
        public int LastAccessFrame;
        public int NextRetryFrame;
    }

    private readonly struct LoadRequest
    {
        public LoadRequest(
            string key,
            string assetsRoot,
            string relativePath,
            DocColumnKind kind,
            DocModelPreviewSettings? modelPreviewSettings)
        {
            Key = key;
            AssetsRoot = assetsRoot;
            RelativePath = relativePath;
            Kind = kind;
            ModelPreviewSettings = modelPreviewSettings;
        }

        public string Key { get; }
        public string AssetsRoot { get; }
        public string RelativePath { get; }
        public DocColumnKind Kind { get; }
        public DocModelPreviewSettings? ModelPreviewSettings { get; }
    }

    public enum ThumbnailStatus
    {
        None,
        Loading,
        Ready,
        Missing,
        InvalidPath,
        PreviewUnavailable,
        Failed,
        BudgetExceeded,
    }

    public readonly struct ThumbnailResult
    {
        public ThumbnailResult(ThumbnailStatus status, Texture texture)
        {
            Status = status;
            Texture = texture;
        }

        public ThumbnailStatus Status { get; }
        public Texture Texture { get; }
    }

    private readonly Dictionary<string, CachedThumbnail> _thumbnailsByKey = new(StringComparer.Ordinal);
    private readonly Queue<LoadRequest> _loadQueue = new();
    private readonly HashSet<string> _queuedKeys = new(StringComparer.Ordinal);
    private readonly MeshPreviewGenerator _meshPreviewGenerator = new();

    private int _frameIndex;
    private int _loadedGpuThumbnailCount;

    public ThumbnailResult GetThumbnail(
        string? assetsRoot,
        DocColumnKind kind,
        string? relativePath,
        DocModelPreviewSettings? modelPreviewSettings = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new ThumbnailResult(ThumbnailStatus.None, default);
        }

        if (!TryNormalizeInputs(
                assetsRoot,
                relativePath,
                kind,
                modelPreviewSettings,
                out string fullAssetsRoot,
                out string normalizedRelativePath,
                out DocModelPreviewSettings? normalizedModelPreviewSettings,
                out string cacheKey))
        {
            return new ThumbnailResult(ThumbnailStatus.InvalidPath, default);
        }

        if (kind == DocColumnKind.AudioAsset)
        {
            StringComparison pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            string fullAssetsRootPrefix = fullAssetsRoot.EndsWith(Path.DirectorySeparatorChar)
                ? fullAssetsRoot
                : fullAssetsRoot + Path.DirectorySeparatorChar;
            string absoluteAssetPath = Path.GetFullPath(Path.Combine(fullAssetsRoot, normalizedRelativePath));
            if (!absoluteAssetPath.StartsWith(fullAssetsRootPrefix, pathComparison))
            {
                return new ThumbnailResult(ThumbnailStatus.InvalidPath, default);
            }

            if (!File.Exists(absoluteAssetPath))
            {
                return new ThumbnailResult(ThumbnailStatus.Missing, default);
            }

            return new ThumbnailResult(ThumbnailStatus.PreviewUnavailable, default);
        }

        if (kind == DocColumnKind.UiAsset)
        {
            var previewResult = DocAssetServices.DerpUiPreviewCache.GetPreview(
                gameRoot: fullAssetsRoot,
                assetsRoot: fullAssetsRoot,
                normalizedRelativePath);

            return previewResult.Status switch
            {
                DerpUiPreviewCache.PreviewStatus.Ready => new ThumbnailResult(ThumbnailStatus.Ready, previewResult.Texture),
                DerpUiPreviewCache.PreviewStatus.InvalidPath => new ThumbnailResult(ThumbnailStatus.InvalidPath, default),
                DerpUiPreviewCache.PreviewStatus.Missing => new ThumbnailResult(ThumbnailStatus.Missing, default),
                DerpUiPreviewCache.PreviewStatus.PreviewUnavailable => new ThumbnailResult(ThumbnailStatus.PreviewUnavailable, default),
                DerpUiPreviewCache.PreviewStatus.BudgetExceeded => new ThumbnailResult(ThumbnailStatus.BudgetExceeded, default),
                _ => new ThumbnailResult(ThumbnailStatus.Failed, default),
            };
        }

        if (!_thumbnailsByKey.TryGetValue(cacheKey, out var cachedThumbnail))
        {
            cachedThumbnail = new CachedThumbnail
            {
                Status = ThumbnailStatus.Loading,
                LastAccessFrame = _frameIndex,
            };
            _thumbnailsByKey[cacheKey] = cachedThumbnail;
            QueueLoad(cacheKey, fullAssetsRoot, normalizedRelativePath, kind, normalizedModelPreviewSettings);
            return new ThumbnailResult(ThumbnailStatus.Loading, default);
        }

        cachedThumbnail.LastAccessFrame = _frameIndex;

        if (cachedThumbnail.Status == ThumbnailStatus.Ready)
        {
            if (IsCachedSourceModified(cachedThumbnail))
            {
                ReleaseGpuTexture(cachedThumbnail);
                cachedThumbnail.Status = ThumbnailStatus.Loading;
                cachedThumbnail.NextRetryFrame = _frameIndex;
                QueueLoad(cacheKey, fullAssetsRoot, normalizedRelativePath, kind, normalizedModelPreviewSettings);
                return new ThumbnailResult(ThumbnailStatus.Loading, default);
            }

            return new ThumbnailResult(ThumbnailStatus.Ready, cachedThumbnail.Texture);
        }

        bool isActivelyQueued = _queuedKeys.Contains(cacheKey);
        if ((!isActivelyQueued || cachedThumbnail.Status != ThumbnailStatus.Loading) &&
            _frameIndex >= cachedThumbnail.NextRetryFrame)
        {
            cachedThumbnail.Status = ThumbnailStatus.Loading;
            QueueLoad(cacheKey, fullAssetsRoot, normalizedRelativePath, kind, normalizedModelPreviewSettings);
        }

        return new ThumbnailResult(cachedThumbnail.Status, default);
    }

    public void ProcessLoadQueue(int maxPerFrame)
    {
        _frameIndex++;

        if (maxPerFrame <= 0)
        {
            return;
        }

        int processedCount = 0;
        while (processedCount < maxPerFrame && _loadQueue.Count > 0)
        {
            LoadRequest request = _loadQueue.Dequeue();
            _queuedKeys.Remove(request.Key);
            processedCount++;

            if (!_thumbnailsByKey.TryGetValue(request.Key, out var cachedThumbnail))
            {
                continue;
            }

            if (cachedThumbnail.Status != ThumbnailStatus.Loading)
            {
                continue;
            }

            cachedThumbnail.LastAccessFrame = _frameIndex;

            if (!EnsureGpuBudgetForLoad(request.Key))
            {
                cachedThumbnail.Status = ThumbnailStatus.BudgetExceeded;
                cachedThumbnail.NextRetryFrame = _frameIndex + RetryDelayFrames;
                continue;
            }

            if (!TryLoadThumbnailTexture(request, out Texture loadedTexture, out string sourcePath, out long sourceWriteTicks, out ThumbnailStatus failureStatus))
            {
                cachedThumbnail.Status = failureStatus;
                cachedThumbnail.NextRetryFrame = _frameIndex + RetryDelayFrames;
                continue;
            }

            if (cachedThumbnail.HasGpuTexture)
            {
                ReleaseGpuTexture(cachedThumbnail);
            }

            cachedThumbnail.Texture = loadedTexture;
            cachedThumbnail.HasGpuTexture = true;
            cachedThumbnail.SourcePath = sourcePath;
            cachedThumbnail.SourceWriteTicks = sourceWriteTicks;
            cachedThumbnail.Status = ThumbnailStatus.Ready;
            _loadedGpuThumbnailCount++;
        }

        TrimTrackingCache();
    }

    private static bool TryNormalizeInputs(
        string? assetsRoot,
        string relativePath,
        DocColumnKind kind,
        DocModelPreviewSettings? modelPreviewSettings,
        out string fullAssetsRoot,
        out string normalizedRelativePath,
        out DocModelPreviewSettings? normalizedModelPreviewSettings,
        out string cacheKey)
    {
        fullAssetsRoot = "";
        normalizedRelativePath = "";
        normalizedModelPreviewSettings = null;
        cacheKey = "";

        if (string.IsNullOrWhiteSpace(assetsRoot))
        {
            return false;
        }

        if (kind != DocColumnKind.TextureAsset &&
            kind != DocColumnKind.MeshAsset &&
            kind != DocColumnKind.AudioAsset &&
            kind != DocColumnKind.UiAsset)
        {
            return false;
        }

        if (!TryNormalizeRelativePath(relativePath, out normalizedRelativePath))
        {
            return false;
        }

        fullAssetsRoot = Path.GetFullPath(assetsRoot);
        if (kind == DocColumnKind.MeshAsset)
        {
            normalizedModelPreviewSettings = modelPreviewSettings?.Clone() ?? new DocModelPreviewSettings();
            normalizedModelPreviewSettings.ClampInPlace();
            string previewSignature = DocModelPreviewSettings.BuildCacheSignature(normalizedModelPreviewSettings);
            cacheKey = fullAssetsRoot + "|" + kind + "|" + normalizedRelativePath + "|" + previewSignature;
        }
        else
        {
            cacheKey = fullAssetsRoot + "|" + kind + "|" + normalizedRelativePath;
        }

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

    private void QueueLoad(
        string cacheKey,
        string assetsRoot,
        string relativePath,
        DocColumnKind kind,
        DocModelPreviewSettings? modelPreviewSettings)
    {
        if (_queuedKeys.Contains(cacheKey))
        {
            return;
        }

        _loadQueue.Enqueue(new LoadRequest(cacheKey, assetsRoot, relativePath, kind, modelPreviewSettings?.Clone()));
        _queuedKeys.Add(cacheKey);
    }

    private bool TryLoadThumbnailTexture(
        LoadRequest request,
        out Texture texture,
        out string sourcePath,
        out long sourceWriteTicks,
        out ThumbnailStatus failureStatus)
    {
        texture = default;
        sourcePath = "";
        sourceWriteTicks = 0;

        if (!TryResolveSourcePath(request, out sourcePath, out failureStatus))
        {
            return false;
        }

        if (!TryLoadImagePixels(sourcePath, out byte[] pixels, out int width, out int height))
        {
            failureStatus = ThumbnailStatus.Failed;
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            failureStatus = ThumbnailStatus.Failed;
            return false;
        }

        byte[] thumbnailPixels = ResizePixelsToThumbnail(pixels, width, height, out int thumbnailWidth, out int thumbnailHeight);

        try
        {
            texture = DerpEngine.LoadTexture(thumbnailPixels, thumbnailWidth, thumbnailHeight);
            sourceWriteTicks = File.GetLastWriteTimeUtc(sourcePath).Ticks;
            failureStatus = ThumbnailStatus.Ready;
            return true;
        }
        catch
        {
            failureStatus = ThumbnailStatus.Failed;
            return false;
        }
    }

    private bool TryResolveSourcePath(LoadRequest request, out string sourcePath, out ThumbnailStatus failureStatus)
    {
        sourcePath = "";

        string absoluteAssetPath = Path.GetFullPath(Path.Combine(request.AssetsRoot, request.RelativePath));
        if (!absoluteAssetPath.StartsWith(request.AssetsRoot, StringComparison.Ordinal))
        {
            failureStatus = ThumbnailStatus.InvalidPath;
            return false;
        }

        if (request.Kind == DocColumnKind.TextureAsset)
        {
            if (!File.Exists(absoluteAssetPath))
            {
                failureStatus = ThumbnailStatus.Missing;
                return false;
            }

            string extension = Path.GetExtension(absoluteAssetPath);
            if (!IsSupportedTextureExtension(extension))
            {
                failureStatus = ThumbnailStatus.Failed;
                return false;
            }

            sourcePath = absoluteAssetPath;
            failureStatus = ThumbnailStatus.Ready;
            return true;
        }

        if (request.Kind == DocColumnKind.MeshAsset)
        {
            if (_meshPreviewGenerator.TryResolveCachedPreviewPath(
                    request.AssetsRoot,
                    request.RelativePath,
                    request.ModelPreviewSettings,
                    out string previewPath))
            {
                sourcePath = previewPath;
                failureStatus = ThumbnailStatus.Ready;
                return true;
            }

            if (DocAssetServices.LazyAssetCompiler.IsMeshCompilePending(request.AssetsRoot, request.RelativePath))
            {
                failureStatus = ThumbnailStatus.Loading;
                return false;
            }

            failureStatus = ThumbnailStatus.PreviewUnavailable;
            return false;
        }

        failureStatus = ThumbnailStatus.Failed;
        return false;
    }

    private static bool TryLoadImagePixels(string sourcePath, out byte[] pixels, out int width, out int height)
    {
        pixels = Array.Empty<byte>();
        width = 0;
        height = 0;

        try
        {
            using var fileStream = File.OpenRead(sourcePath);
            var image = ImageResult.FromStream(fileStream, ColorComponents.RedGreenBlueAlpha);
            pixels = image.Data;
            width = image.Width;
            height = image.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ResizePixelsToThumbnail(byte[] sourcePixels, int sourceWidth, int sourceHeight, out int targetWidth, out int targetHeight)
    {
        float scale = MathF.Min(
            MaxThumbnailDimension / (float)sourceWidth,
            MaxThumbnailDimension / (float)sourceHeight);
        if (scale >= 1f)
        {
            targetWidth = sourceWidth;
            targetHeight = sourceHeight;
            return sourcePixels;
        }

        targetWidth = Math.Max(1, (int)MathF.Round(sourceWidth * scale));
        targetHeight = Math.Max(1, (int)MathF.Round(sourceHeight * scale));

        var resizedPixels = new byte[targetWidth * targetHeight * 4];
        for (int targetY = 0; targetY < targetHeight; targetY++)
        {
            int sourceY = (int)((targetY / (float)targetHeight) * sourceHeight);
            sourceY = Math.Clamp(sourceY, 0, sourceHeight - 1);

            for (int targetX = 0; targetX < targetWidth; targetX++)
            {
                int sourceX = (int)((targetX / (float)targetWidth) * sourceWidth);
                sourceX = Math.Clamp(sourceX, 0, sourceWidth - 1);

                int sourcePixelOffset = (sourceY * sourceWidth + sourceX) * 4;
                int targetPixelOffset = (targetY * targetWidth + targetX) * 4;
                resizedPixels[targetPixelOffset] = sourcePixels[sourcePixelOffset];
                resizedPixels[targetPixelOffset + 1] = sourcePixels[sourcePixelOffset + 1];
                resizedPixels[targetPixelOffset + 2] = sourcePixels[sourcePixelOffset + 2];
                resizedPixels[targetPixelOffset + 3] = sourcePixels[sourcePixelOffset + 3];
            }
        }

        return resizedPixels;
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

    private static bool IsCachedSourceModified(CachedThumbnail cachedThumbnail)
    {
        if (string.IsNullOrWhiteSpace(cachedThumbnail.SourcePath) || !File.Exists(cachedThumbnail.SourcePath))
        {
            return true;
        }

        long currentWriteTicks = File.GetLastWriteTimeUtc(cachedThumbnail.SourcePath).Ticks;
        return currentWriteTicks != cachedThumbnail.SourceWriteTicks;
    }

    private void TrimTrackingCache()
    {
        while (_thumbnailsByKey.Count > MaxTrackedEntries)
        {
            string keyToRemove = FindOldestCacheKey(includeReadyEntries: false);
            if (keyToRemove.Length == 0)
            {
                keyToRemove = FindOldestCacheKey(includeReadyEntries: true);
            }

            if (keyToRemove.Length == 0)
            {
                break;
            }

            RemoveCachedThumbnailEntry(keyToRemove);
        }
    }

    public bool TryEvictLeastRecentlyUsedReadyThumbnail()
    {
        string keyToEvict = FindOldestReadyCacheKey(excludedKey: null);
        if (keyToEvict.Length == 0)
        {
            return false;
        }

        RemoveCachedThumbnailEntry(keyToEvict);
        return true;
    }

    private bool EnsureGpuBudgetForLoad(string requestingKey)
    {
        while (_loadedGpuThumbnailCount >= MaxGpuThumbnails || IsGlobalTextureBudgetConstrained())
        {
            string keyToEvict = FindOldestReadyCacheKey(requestingKey);
            if (keyToEvict.Length == 0)
            {
                return false;
            }

            RemoveCachedThumbnailEntry(keyToEvict);
        }

        return true;
    }

    private static bool IsGlobalTextureBudgetConstrained()
    {
        int maxTextureCount = DerpEngine.MaxTextureCount;
        int activeTextureCount = DerpEngine.ActiveTextureCount;
        return activeTextureCount >= maxTextureCount - ReservedTextureSlotsForUiPreviews;
    }

    private string FindOldestReadyCacheKey(string? excludedKey)
    {
        string oldestKey = "";
        int oldestFrame = int.MaxValue;
        foreach (KeyValuePair<string, CachedThumbnail> pair in _thumbnailsByKey)
        {
            if (!string.IsNullOrWhiteSpace(excludedKey) &&
                string.Equals(pair.Key, excludedKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (pair.Value.Status != ThumbnailStatus.Ready || !pair.Value.HasGpuTexture)
            {
                continue;
            }

            if (_queuedKeys.Contains(pair.Key))
            {
                continue;
            }

            if (pair.Value.LastAccessFrame < oldestFrame)
            {
                oldestFrame = pair.Value.LastAccessFrame;
                oldestKey = pair.Key;
            }
        }

        return oldestKey;
    }

    private void RemoveCachedThumbnailEntry(string cacheKey)
    {
        if (_thumbnailsByKey.TryGetValue(cacheKey, out CachedThumbnail? cachedThumbnail))
        {
            ReleaseGpuTexture(cachedThumbnail);
        }

        _thumbnailsByKey.Remove(cacheKey);
        _queuedKeys.Remove(cacheKey);
    }

    private void ReleaseGpuTexture(CachedThumbnail cachedThumbnail)
    {
        if (!cachedThumbnail.HasGpuTexture)
        {
            return;
        }

        DerpEngine.UnloadTexture(cachedThumbnail.Texture);
        cachedThumbnail.Texture = default;
        cachedThumbnail.HasGpuTexture = false;
        if (_loadedGpuThumbnailCount > 0)
        {
            _loadedGpuThumbnailCount--;
        }
    }

    private string FindOldestCacheKey(bool includeReadyEntries)
    {
        string oldestKey = "";
        int oldestFrame = int.MaxValue;

        foreach (var pair in _thumbnailsByKey)
        {
            if (_queuedKeys.Contains(pair.Key))
            {
                continue;
            }

            if (!includeReadyEntries && pair.Value.Status == ThumbnailStatus.Ready)
            {
                continue;
            }

            if (pair.Value.LastAccessFrame < oldestFrame)
            {
                oldestFrame = pair.Value.LastAccessFrame;
                oldestKey = pair.Key;
            }
        }

        return oldestKey;
    }
}
