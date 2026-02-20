using System;
using System.IO;

namespace Derp.Doc.Editor;

internal static class DocWorkspaceRootLocator
{
    public static string FindWorkspaceRoot()
    {
        string fromCurrent = TryFindRootFrom(Directory.GetCurrentDirectory());
        if (!string.IsNullOrWhiteSpace(fromCurrent))
        {
            return fromCurrent;
        }

        string fromBaseDir = TryFindRootFrom(AppContext.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(fromBaseDir))
        {
            return fromBaseDir;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string TryFindRootFrom(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return "";
        }

        string dir = Path.GetFullPath(startPath);

        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "DerpTech2026.sln")))
            {
                return dir;
            }

            if (File.Exists(Path.Combine(dir, "AGENTS.md")))
            {
                return dir;
            }

            if (Directory.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent == null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return "";
    }
}

