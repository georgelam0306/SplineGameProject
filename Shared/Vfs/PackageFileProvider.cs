using System.Text.Json;
using K4os.Compression.LZ4;

namespace DerpLib.Vfs;

/// <summary>
/// File provider that reads from a .pak package file.
/// Package format is JSON with optional LZ4 compression per file.
/// </summary>
public sealed class PackageFileProvider : IVirtualFileProvider
{
    public string RootPath { get; }
    public string PackagePath { get; }

    private readonly Dictionary<string, PackageEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public PackageFileProvider(string mountPoint, string packagePath)
    {
        RootPath = mountPoint.EndsWith('/') ? mountPoint : mountPoint + "/";
        PackagePath = Path.GetFullPath(packagePath);
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        if (!File.Exists(PackagePath)) return;

        var json = File.ReadAllText(PackagePath);
        var package = JsonSerializer.Deserialize(json, VfsJsonContext.Default.PackageData);
        if (package == null) return;

        foreach (var kv in package.Files)
        {
            var entry = new PackageEntry
            {
                Path = kv.Key,
                Data = kv.Value.Data,
                Compressed = kv.Value.Compressed
            };
            _entries[NormalizePath(kv.Key)] = entry;
        }
    }

    private string ToAbsolute(string url)
    {
        var rel = url.Replace('\\', '/');
        if (rel.StartsWith('/')) rel = rel.Substring(1);
        return NormalizePath(rel);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').ToLowerInvariant();
    }

    public Stream OpenStream(string url, VirtualFileMode mode, VirtualFileAccess access, VirtualFileShare share = VirtualFileShare.Read)
    {
        if (access != VirtualFileAccess.Read || mode != VirtualFileMode.Open)
        {
            throw new NotSupportedException("PackageFileProvider only supports read-only access.");
        }

        EnsureLoaded();
        var key = ToAbsolute(url);

        if (!_entries.TryGetValue(key, out var entry))
        {
            throw new FileNotFoundException($"File not found in package: {url}");
        }

        var data = Convert.FromBase64String(entry.Data);
        if (entry.Compressed)
        {
            data = LZ4Pickler.Unpickle(data);
        }

        return new MemoryStream(data, writable: false);
    }

    public string[] ListFiles(string url, string searchPattern, VirtualSearchOption searchOption)
    {
        EnsureLoaded();
        var prefix = ToAbsolute(url);
        if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith('/'))
            prefix += "/";

        var results = new List<string>();
        foreach (var path in _entries.Keys)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var relativePath = path.Substring(prefix.Length);

            // For TopDirectoryOnly, skip files in subdirectories
            if (searchOption == VirtualSearchOption.TopDirectoryOnly && relativePath.Contains('/'))
                continue;

            // Simple pattern matching (supports * wildcard)
            if (MatchesPattern(Path.GetFileName(relativePath), searchPattern))
            {
                results.Add(relativePath);
            }
        }

        return results.ToArray();
    }

    public bool FileExists(string url)
    {
        EnsureLoaded();
        return _entries.ContainsKey(ToAbsolute(url));
    }

    public long FileSize(string url)
    {
        EnsureLoaded();
        if (!_entries.TryGetValue(ToAbsolute(url), out var entry))
            return 0;

        var data = Convert.FromBase64String(entry.Data);
        if (entry.Compressed)
        {
            // For compressed data, we need to decompress to get actual size
            // This is inefficient but accurate
            data = LZ4Pickler.Unpickle(data);
        }
        return data.Length;
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (pattern == "*" || pattern == "*.*")
            return true;

        if (pattern.StartsWith("*."))
        {
            var ext = pattern.Substring(1);
            return fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
        }

        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private class PackageEntry
    {
        public string Path { get; init; } = string.Empty;
        public string Data { get; init; } = string.Empty;
        public bool Compressed { get; init; }
    }

    /// <summary>
    /// Creates a package file from a directory.
    /// </summary>
    public static void CreatePackage(string outputPath, string sourceDirectory, bool compress = true)
    {
        var package = new PackageData();
        var baseDir = Path.GetFullPath(sourceDirectory);

        foreach (var file in Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
            var data = File.ReadAllBytes(file);

            byte[] encoded;
            bool isCompressed = false;

            if (compress)
            {
                encoded = LZ4Pickler.Pickle(data);
                // Only use compression if it actually saves space
                if (encoded.Length < data.Length)
                {
                    isCompressed = true;
                }
                else
                {
                    encoded = data;
                }
            }
            else
            {
                encoded = data;
            }

            package.Files[relativePath] = new PackageFileEntry
            {
                Data = Convert.ToBase64String(encoded),
                Compressed = isCompressed
            };
        }

        var json = JsonSerializer.Serialize(package, VfsJsonContext.Default.PackageData);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json);
    }
}
