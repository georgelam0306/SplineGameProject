using System;
using System.Collections.Generic;
using System.IO;

namespace Derp.UI;

internal sealed class UiEditorUserPreferences
{
    public const int MaxRecentDocumentCount = 12;

    public readonly List<string> RecentDocumentPaths = new();

    public void ClampInPlace()
    {
        NormalizeRecentDocumentPathsInPlace();
    }

    public bool AddRecentDocumentPath(string path)
    {
        string normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        bool changed = false;
        for (int pathIndex = RecentDocumentPaths.Count - 1; pathIndex >= 0; pathIndex--)
        {
            if (string.Equals(RecentDocumentPaths[pathIndex], normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                RecentDocumentPaths.RemoveAt(pathIndex);
                changed = true;
            }
        }

        RecentDocumentPaths.Insert(0, normalizedPath);
        changed = true;
        if (RecentDocumentPaths.Count > MaxRecentDocumentCount)
        {
            RecentDocumentPaths.RemoveRange(MaxRecentDocumentCount, RecentDocumentPaths.Count - MaxRecentDocumentCount);
        }

        return changed;
    }

    public bool RemoveRecentDocumentPath(string path)
    {
        string normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        bool removed = false;
        for (int pathIndex = RecentDocumentPaths.Count - 1; pathIndex >= 0; pathIndex--)
        {
            if (string.Equals(RecentDocumentPaths[pathIndex], normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                RecentDocumentPaths.RemoveAt(pathIndex);
                removed = true;
            }
        }

        return removed;
    }

    public bool ClearRecentDocumentPaths()
    {
        if (RecentDocumentPaths.Count <= 0)
        {
            return false;
        }

        RecentDocumentPaths.Clear();
        return true;
    }

    private void NormalizeRecentDocumentPathsInPlace()
    {
        for (int pathIndex = RecentDocumentPaths.Count - 1; pathIndex >= 0; pathIndex--)
        {
            string normalizedPath = NormalizePath(RecentDocumentPaths[pathIndex]);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                RecentDocumentPaths.RemoveAt(pathIndex);
                continue;
            }

            RecentDocumentPaths[pathIndex] = normalizedPath;
        }

        for (int outerIndex = 0; outerIndex < RecentDocumentPaths.Count; outerIndex++)
        {
            string candidatePath = RecentDocumentPaths[outerIndex];
            for (int innerIndex = RecentDocumentPaths.Count - 1; innerIndex > outerIndex; innerIndex--)
            {
                if (string.Equals(candidatePath, RecentDocumentPaths[innerIndex], StringComparison.OrdinalIgnoreCase))
                {
                    RecentDocumentPaths.RemoveAt(innerIndex);
                }
            }
        }

        if (RecentDocumentPaths.Count > MaxRecentDocumentCount)
        {
            RecentDocumentPaths.RemoveRange(MaxRecentDocumentCount, RecentDocumentPaths.Count - MaxRecentDocumentCount);
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        try
        {
            string normalizedPath = Path.GetFullPath(path.Trim());
            return normalizedPath;
        }
        catch
        {
            return "";
        }
    }
}
