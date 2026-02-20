using DerpEngine = DerpLib.Derp;
using DerpLib.Audio;

namespace Derp.Doc.Assets;

internal sealed class AudioPreviewPlayer
{
    private const string PreviewMountPoint = "/derpdoc_assets_preview";

    private sealed class CachedSoundEntry
    {
        public Sound Sound;
        public long SourceWriteTicks;
    }

    private readonly Dictionary<string, CachedSoundEntry> _cachedSoundsByRelativePath = new(StringComparer.Ordinal);
    private string _mountedAssetsRoot = "";

    public bool TryPlay(string? assetsRoot, string? relativePath, out string errorMessage)
    {
        errorMessage = "";
        if (!TryResolveSoundPath(
                assetsRoot,
                relativePath,
                out string fullAssetsRoot,
                out string normalizedRelativePath,
                out string absoluteSoundPath,
                out errorMessage))
        {
            return false;
        }

        if (!TryEnsureAssetsMounted(fullAssetsRoot, out errorMessage))
        {
            return false;
        }

        long sourceWriteTicks;
        try
        {
            sourceWriteTicks = File.GetLastWriteTimeUtc(absoluteSoundPath).Ticks;
        }
        catch (Exception ex)
        {
            errorMessage = "Unable to read sound file metadata: " + ex.Message;
            return false;
        }

        if (!_cachedSoundsByRelativePath.TryGetValue(normalizedRelativePath, out var cachedEntry) ||
            !cachedEntry.Sound.IsValid ||
            cachedEntry.SourceWriteTicks != sourceWriteTicks)
        {
            if (cachedEntry != null && cachedEntry.Sound.IsValid)
            {
                DerpEngine.UnloadSound(cachedEntry.Sound);
            }

            string vfsPath = PreviewMountPoint + "/" + normalizedRelativePath;
            Sound loadedSound;
            try
            {
                loadedSound = DerpEngine.LoadSound(vfsPath);
            }
            catch (Exception ex)
            {
                errorMessage = "Unable to load sound: " + ex.Message;
                return false;
            }

            cachedEntry = new CachedSoundEntry
            {
                Sound = loadedSound,
                SourceWriteTicks = sourceWriteTicks,
            };
            _cachedSoundsByRelativePath[normalizedRelativePath] = cachedEntry;
        }

        DerpEngine.PlaySound(cachedEntry.Sound);
        return true;
    }

    public void Clear()
    {
        foreach (var entry in _cachedSoundsByRelativePath.Values)
        {
            if (entry.Sound.IsValid)
            {
                DerpEngine.UnloadSound(entry.Sound);
            }
        }

        _cachedSoundsByRelativePath.Clear();
        _mountedAssetsRoot = "";
    }

    private bool TryEnsureAssetsMounted(string fullAssetsRoot, out string errorMessage)
    {
        errorMessage = "";
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(_mountedAssetsRoot, fullAssetsRoot, pathComparison))
        {
            return true;
        }

        Clear();
        try
        {
            DerpEngine.Vfs.MountPhysical(PreviewMountPoint, fullAssetsRoot);
            _mountedAssetsRoot = fullAssetsRoot;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = "Unable to mount assets for audio preview: " + ex.Message;
            return false;
        }
    }

    private static bool TryResolveSoundPath(
        string? assetsRoot,
        string? relativePath,
        out string fullAssetsRoot,
        out string normalizedRelativePath,
        out string absoluteSoundPath,
        out string errorMessage)
    {
        fullAssetsRoot = "";
        normalizedRelativePath = "";
        absoluteSoundPath = "";
        errorMessage = "";

        if (string.IsNullOrWhiteSpace(assetsRoot))
        {
            errorMessage = "Assets root is not available.";
            return false;
        }

        if (!Directory.Exists(assetsRoot))
        {
            errorMessage = "Assets directory does not exist.";
            return false;
        }

        if (!TryNormalizeRelativePath(relativePath, out normalizedRelativePath))
        {
            errorMessage = "Audio path must be a relative path under Assets.";
            return false;
        }

        fullAssetsRoot = Path.GetFullPath(assetsRoot);
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string fullAssetsRootPrefix = fullAssetsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullAssetsRoot
            : fullAssetsRoot + Path.DirectorySeparatorChar;

        absoluteSoundPath = Path.GetFullPath(Path.Combine(fullAssetsRoot, normalizedRelativePath));
        if (!absoluteSoundPath.StartsWith(fullAssetsRootPrefix, pathComparison))
        {
            errorMessage = "Audio path resolved outside Assets.";
            return false;
        }

        if (!File.Exists(absoluteSoundPath))
        {
            errorMessage = "Audio file does not exist.";
            return false;
        }

        return true;
    }

    private static bool TryNormalizeRelativePath(string? relativePath, out string normalizedRelativePath)
    {
        normalizedRelativePath = "";
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        string slashNormalized = relativePath.Trim().Replace('\\', '/');
        while (slashNormalized.StartsWith("/", StringComparison.Ordinal))
        {
            slashNormalized = slashNormalized[1..];
        }

        if (slashNormalized.Length <= 0)
        {
            return false;
        }

        string[] segments = slashNormalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 0)
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
}
