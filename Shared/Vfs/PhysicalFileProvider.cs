namespace DerpLib.Vfs;

/// <summary>
/// File provider that maps virtual paths to a physical directory.
/// </summary>
public sealed class PhysicalFileProvider : IVirtualFileProvider
{
    public string RootPath { get; }
    public string AbsoluteRoot { get; }

    public PhysicalFileProvider(string mountPoint, string absolutePath)
    {
        RootPath = mountPoint.EndsWith('/') ? mountPoint : mountPoint + "/";
        AbsoluteRoot = Path.GetFullPath(absolutePath);
        Directory.CreateDirectory(AbsoluteRoot);
    }

    private string ToAbsolute(string url)
    {
        var rel = url.Replace('\\', '/');
        if (rel.StartsWith('/')) rel = rel.Substring(1);
        return Path.Combine(AbsoluteRoot, rel);
    }

    public Stream OpenStream(string url, VirtualFileMode mode, VirtualFileAccess access, VirtualFileShare share = VirtualFileShare.Read)
    {
        var path = ToAbsolute(url);
        var fm = mode switch
        {
            VirtualFileMode.Open => FileMode.Open,
            VirtualFileMode.Create => FileMode.Create,
            VirtualFileMode.CreateNew => FileMode.CreateNew,
            VirtualFileMode.OpenOrCreate => FileMode.OpenOrCreate,
            VirtualFileMode.Truncate => FileMode.Truncate,
            VirtualFileMode.Append => FileMode.Append,
            _ => FileMode.Open
        };
        var fa = access switch
        {
            VirtualFileAccess.Read => FileAccess.Read,
            VirtualFileAccess.Write => FileAccess.Write,
            VirtualFileAccess.ReadWrite => FileAccess.ReadWrite,
            _ => FileAccess.Read
        };
        var fs = share switch
        {
            VirtualFileShare.Read => FileShare.Read,
            VirtualFileShare.Write => FileShare.Write,
            VirtualFileShare.ReadWrite => FileShare.ReadWrite,
            _ => FileShare.None
        };

        if (access != VirtualFileAccess.Read)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }

        return new FileStream(path, fm, fa, fs);
    }

    public string[] ListFiles(string url, string searchPattern, VirtualSearchOption searchOption)
    {
        var path = ToAbsolute(url);
        var opt = searchOption == VirtualSearchOption.AllDirectories
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        if (!Directory.Exists(path)) return Array.Empty<string>();

        return Directory.GetFiles(path, searchPattern, opt)
            .Select(p => p.Substring(AbsoluteRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Select(p => p.Replace('\\', '/'))
            .ToArray();
    }

    public bool FileExists(string url) => File.Exists(ToAbsolute(url));

    public long FileSize(string url)
    {
        var fi = new FileInfo(ToAbsolute(url));
        return fi.Exists ? fi.Length : 0L;
    }
}
