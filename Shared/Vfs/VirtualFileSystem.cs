using System.Collections.Concurrent;
using Serilog;

namespace DerpLib.Vfs;

/// <summary>
/// Virtual file system with mount points for abstracting file access.
/// Supports DI - inject IEnumerable&lt;IVirtualFileProvider&gt; to configure providers.
/// </summary>
public sealed class VirtualFileSystem
{
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, IVirtualFileProvider> _providers = new();

    /// <summary>
    /// Creates VFS with no initial providers. Use RegisterProvider to add them.
    /// </summary>
    public VirtualFileSystem(ILogger log)
    {
        _log = log;
    }

    /// <summary>
    /// Creates VFS with DI-injected providers. Providers are registered in order.
    /// </summary>
    public VirtualFileSystem(ILogger log, IEnumerable<IVirtualFileProvider> providers)
    {
        _log = log;
        foreach (var provider in providers)
        {
            RegisterProvider(provider);
        }
    }

    /// <summary>
    /// Registers a provider at its mount point. Later registrations override earlier ones.
    /// </summary>
    public void RegisterProvider(IVirtualFileProvider provider)
    {
        var key = NormalizeMountPoint(provider.RootPath);
        _providers[key] = provider;
        _log.Debug("VFS: Registered {ProviderType} at {MountPoint}", provider.GetType().Name, key);
    }

    /// <summary>
    /// Convenience method to mount a physical directory. For DI scenarios,
    /// prefer injecting IVirtualFileProvider implementations directly.
    /// </summary>
    public IVirtualFileProvider MountPhysical(string mountPoint, string absolutePath)
    {
        var provider = new PhysicalFileProvider(mountPoint, absolutePath);
        RegisterProvider(provider);
        _log.Information("VFS: Mounted physical {AbsolutePath} at {MountPoint}", absolutePath, mountPoint);
        return provider;
    }

    public Stream OpenStream(string path, VirtualFileMode mode, VirtualFileAccess access, VirtualFileShare share = VirtualFileShare.Read)
    {
        var (provider, subpath) = Resolve(path);
        return provider.OpenStream(subpath, mode, access, share);
    }

    /// <summary>
    /// Reads all bytes from a file. Convenience method for loading assets.
    /// </summary>
    public byte[] ReadAllBytes(string path)
    {
        using var stream = OpenStream(path, VirtualFileMode.Open, VirtualFileAccess.Read);
        var length = stream.Length;
        var buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = stream.Read(buffer, offset, (int)(length - offset));
            if (read == 0) break;
            offset += read;
        }
        return buffer;
    }

    public string[] ListFiles(string path, string searchPattern, VirtualSearchOption option)
    {
        var (provider, subpath) = Resolve(path);
        return provider.ListFiles(subpath, searchPattern, option);
    }

    public bool FileExists(string path)
    {
        try
        {
            var (provider, subpath) = Resolve(path);
            return provider.FileExists(subpath);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public long FileSize(string path)
    {
        var (provider, subpath) = Resolve(path);
        return provider.FileSize(subpath);
    }

    private (IVirtualFileProvider provider, string subpath) Resolve(string path)
    {
        var norm = Normalize(path);
        foreach (var kvp in _providers.OrderByDescending(k => k.Key.Length))
        {
            if (norm.StartsWith(kvp.Key, StringComparison.Ordinal))
            {
                var sub = norm.Substring(kvp.Key.Length);
                return (kvp.Value, sub);
            }
        }
        throw new InvalidOperationException($"No VFS provider registered for path '{path}'.");
    }

    private static string NormalizeMountPoint(string path)
    {
        var key = path.Replace('\\', '/');
        if (!key.StartsWith('/')) key = "/" + key;
        if (!key.EndsWith('/')) key += "/";
        return key;
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        path = path.Replace('\\', '/');
        if (!path.StartsWith('/')) path = "/" + path;
        return path;
    }
}
