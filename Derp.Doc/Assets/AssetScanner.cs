using Derp.Doc.Model;

namespace Derp.Doc.Assets;

internal sealed class AssetScanner
{
    private static readonly string[] TextureExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".tga",
    ];

    private static readonly string[] MeshExtensions =
    [
        ".obj",
        ".glb",
        ".gltf",
        ".fbx",
        ".dae",
        ".3ds",
        ".mesh",
    ];

    private static readonly string[] AudioExtensions =
    [
        ".mp3",
        ".wav",
        ".ogg",
        ".flac",
        ".m4a",
        ".aac",
        ".webm",
    ];

    private static readonly string[] UiExtensions =
    [
        ".bdui",
    ];

    private sealed class ScanCacheEntry
    {
        public DateTime ScannedAtUtc;
        public List<AssetEntry> Entries = new();
    }

    public readonly struct AssetEntry
    {
        public AssetEntry(string relativePath, string fileName, string extension, long fileSize)
        {
            RelativePath = relativePath;
            FileName = fileName;
            Extension = extension;
            FileSize = fileSize;
        }

        public string RelativePath { get; }
        public string FileName { get; }
        public string Extension { get; }
        public long FileSize { get; }
    }

    private readonly Dictionary<string, ScanCacheEntry> _scanCache = new(StringComparer.Ordinal);

    public IReadOnlyList<AssetEntry> ScanAssets(string assetsRoot, DocColumnKind columnKind, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(assetsRoot))
        {
            return Array.Empty<AssetEntry>();
        }

        string fullAssetsRoot = Path.GetFullPath(assetsRoot);
        if (!Directory.Exists(fullAssetsRoot))
        {
            return Array.Empty<AssetEntry>();
        }

        if (!TryGetAllowedExtensions(columnKind, out string[] allowedExtensions))
        {
            return Array.Empty<AssetEntry>();
        }

        string cacheKey = fullAssetsRoot + "|" + columnKind;
        if (!forceRefresh &&
            _scanCache.TryGetValue(cacheKey, out var cacheEntry) &&
            (DateTime.UtcNow - cacheEntry.ScannedAtUtc).TotalSeconds < 30)
        {
            return cacheEntry.Entries;
        }

        List<AssetEntry> entries = columnKind == DocColumnKind.UiAsset
            ? ScanUiDirectory(fullAssetsRoot)
            : ScanDirectory(fullAssetsRoot, allowedExtensions, relativePrefix: "");

        _scanCache[cacheKey] = new ScanCacheEntry
        {
            ScannedAtUtc = DateTime.UtcNow,
            Entries = entries,
        };

        return entries;
    }

    private static List<AssetEntry> ScanDirectory(string fullAssetsRoot, string[] allowedExtensions, string relativePrefix)
    {
        var entries = new List<AssetEntry>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(fullAssetsRoot, "*", SearchOption.AllDirectories);
        }
        catch
        {
            return entries;
        }

        foreach (string filePath in files)
        {
            string extension = Path.GetExtension(filePath);
            if (!IsAllowedExtension(extension, allowedExtensions))
            {
                continue;
            }

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(filePath);
            }
            catch
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(fullAssetsRoot, filePath).Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(relativePrefix))
            {
                relativePath = relativePrefix + relativePath;
            }

            entries.Add(new AssetEntry(
                relativePath,
                Path.GetFileName(filePath),
                extension,
                fileInfo.Length));
        }

        entries.Sort(static (left, right) =>
            string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    private static List<AssetEntry> ScanUiDirectory(string fullRootPath)
    {
        string resourcesRoot = Path.Combine(fullRootPath, "Resources");
        string assetsRoot = Path.Combine(fullRootPath, "Assets");
        bool hasResourcesRoot = Directory.Exists(resourcesRoot);
        bool hasAssetsRoot = Directory.Exists(assetsRoot);

        if (!hasResourcesRoot && !hasAssetsRoot)
        {
            return ScanDirectory(fullRootPath, UiExtensions, relativePrefix: "");
        }

        var entries = new List<AssetEntry>();
        if (hasResourcesRoot)
        {
            entries.AddRange(ScanDirectory(resourcesRoot, UiExtensions, "Resources/"));
        }

        if (hasAssetsRoot)
        {
            // Keep Assets-relative paths unprefixed, matching native asset column behavior.
            entries.AddRange(ScanDirectory(assetsRoot, UiExtensions, relativePrefix: ""));
        }

        entries.Sort(static (left, right) =>
            string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    private static bool TryGetAllowedExtensions(DocColumnKind columnKind, out string[] allowedExtensions)
    {
        if (columnKind == DocColumnKind.TextureAsset)
        {
            allowedExtensions = TextureExtensions;
            return true;
        }

        if (columnKind == DocColumnKind.MeshAsset)
        {
            allowedExtensions = MeshExtensions;
            return true;
        }

        if (columnKind == DocColumnKind.AudioAsset)
        {
            allowedExtensions = AudioExtensions;
            return true;
        }

        if (columnKind == DocColumnKind.UiAsset)
        {
            allowedExtensions = UiExtensions;
            return true;
        }

        allowedExtensions = Array.Empty<string>();
        return false;
    }

    private static bool IsAllowedExtension(string extension, string[] allowedExtensions)
    {
        for (int extensionIndex = 0; extensionIndex < allowedExtensions.Length; extensionIndex++)
        {
            if (string.Equals(extension, allowedExtensions[extensionIndex], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
