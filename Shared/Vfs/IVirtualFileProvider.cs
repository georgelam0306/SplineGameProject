namespace DerpLib.Vfs;

public interface IVirtualFileProvider
{
    string RootPath { get; }
    Stream OpenStream(string url, VirtualFileMode mode, VirtualFileAccess access, VirtualFileShare share = VirtualFileShare.Read);
    string[] ListFiles(string url, string searchPattern, VirtualSearchOption searchOption);
    bool FileExists(string url);
    long FileSize(string url);
}
