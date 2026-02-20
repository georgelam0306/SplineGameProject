using Derp.Doc.Model;

namespace Derp.Doc.Preferences;

internal sealed class DocUserPreferences
{
    public const float DefaultUiFontSize = 14f;
    public const float DefaultChatMessageFontSize = 14f;
    public const float DefaultChatInputFontSize = 14f;
    public const DocSubtablePreviewQuality DefaultSubtablePreviewQuality = DocSubtablePreviewQuality.Full;
    public const int DefaultSubtablePreviewFrameBudget = 48;
    public const int MaxRecentProjectCount = 12;

    public const float MinUiFontSize = 12f;
    public const float MaxUiFontSize = 20f;
    public const float MinChatFontSize = 12f;
    public const float MaxChatFontSize = 26f;
    public const int MinSubtablePreviewFrameBudget = 0;
    public const int MaxSubtablePreviewFrameBudget = 200;

    public float UiFontSize = DefaultUiFontSize;
    public float ChatMessageFontSize = DefaultChatMessageFontSize;
    public float ChatInputFontSize = DefaultChatInputFontSize;
    public DocSubtablePreviewQuality SubtablePreviewQuality = DefaultSubtablePreviewQuality;
    public int SubtablePreviewFrameBudget = DefaultSubtablePreviewFrameBudget;
    public readonly List<string> RecentProjectPaths = new();
    public readonly Dictionary<string, string> PluginSettingsByKey = new(StringComparer.Ordinal);

    public void ClampInPlace()
    {
        UiFontSize = Math.Clamp(UiFontSize, MinUiFontSize, MaxUiFontSize);
        ChatMessageFontSize = Math.Clamp(ChatMessageFontSize, MinChatFontSize, MaxChatFontSize);
        ChatInputFontSize = Math.Clamp(ChatInputFontSize, MinChatFontSize, MaxChatFontSize);
        if (!Enum.IsDefined(SubtablePreviewQuality))
        {
            SubtablePreviewQuality = DefaultSubtablePreviewQuality;
        }

        SubtablePreviewFrameBudget = Math.Clamp(
            SubtablePreviewFrameBudget,
            MinSubtablePreviewFrameBudget,
            MaxSubtablePreviewFrameBudget);
        NormalizeRecentProjectPathsInPlace();
        NormalizePluginSettingsInPlace();
    }

    public bool SetUiFontSize(float fontSize)
    {
        float clamped = Math.Clamp(fontSize, MinUiFontSize, MaxUiFontSize);
        if (MathF.Abs(UiFontSize - clamped) < 0.001f)
        {
            return false;
        }

        UiFontSize = clamped;
        return true;
    }

    public bool SetChatMessageFontSize(float fontSize)
    {
        float clamped = Math.Clamp(fontSize, MinChatFontSize, MaxChatFontSize);
        if (MathF.Abs(ChatMessageFontSize - clamped) < 0.001f)
        {
            return false;
        }

        ChatMessageFontSize = clamped;
        return true;
    }

    public bool SetChatInputFontSize(float fontSize)
    {
        float clamped = Math.Clamp(fontSize, MinChatFontSize, MaxChatFontSize);
        if (MathF.Abs(ChatInputFontSize - clamped) < 0.001f)
        {
            return false;
        }

        ChatInputFontSize = clamped;
        return true;
    }

    public bool SetSubtablePreviewQuality(DocSubtablePreviewQuality quality)
    {
        if (!Enum.IsDefined(quality))
        {
            quality = DefaultSubtablePreviewQuality;
        }

        if (SubtablePreviewQuality == quality)
        {
            return false;
        }

        SubtablePreviewQuality = quality;
        return true;
    }

    public bool SetSubtablePreviewFrameBudget(int frameBudget)
    {
        int clamped = Math.Clamp(
            frameBudget,
            MinSubtablePreviewFrameBudget,
            MaxSubtablePreviewFrameBudget);
        if (SubtablePreviewFrameBudget == clamped)
        {
            return false;
        }

        SubtablePreviewFrameBudget = clamped;
        return true;
    }

    public bool ResetToDefaults()
    {
        bool changed = false;
        changed |= SetUiFontSize(DefaultUiFontSize);
        changed |= SetChatMessageFontSize(DefaultChatMessageFontSize);
        changed |= SetChatInputFontSize(DefaultChatInputFontSize);
        changed |= SetSubtablePreviewQuality(DefaultSubtablePreviewQuality);
        changed |= SetSubtablePreviewFrameBudget(DefaultSubtablePreviewFrameBudget);
        return changed;
    }

    public bool AddRecentProjectPath(string path)
    {
        string normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        bool changed = false;
        for (int pathIndex = RecentProjectPaths.Count - 1; pathIndex >= 0; pathIndex--)
        {
            if (string.Equals(RecentProjectPaths[pathIndex], normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                RecentProjectPaths.RemoveAt(pathIndex);
                changed = true;
            }
        }

        RecentProjectPaths.Insert(0, normalizedPath);
        changed = true;
        if (RecentProjectPaths.Count > MaxRecentProjectCount)
        {
            RecentProjectPaths.RemoveRange(MaxRecentProjectCount, RecentProjectPaths.Count - MaxRecentProjectCount);
        }

        return changed;
    }

    public bool RemoveRecentProjectPath(string path)
    {
        string normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        bool removed = false;
        for (int pathIndex = RecentProjectPaths.Count - 1; pathIndex >= 0; pathIndex--)
        {
            if (string.Equals(RecentProjectPaths[pathIndex], normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                RecentProjectPaths.RemoveAt(pathIndex);
                removed = true;
            }
        }

        return removed;
    }

    public bool ClearRecentProjectPaths()
    {
        if (RecentProjectPaths.Count <= 0)
        {
            return false;
        }

        RecentProjectPaths.Clear();
        return true;
    }

    public bool TryGetPluginSetting(string key, out string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = "";
            return false;
        }

        return PluginSettingsByKey.TryGetValue(key.Trim(), out value!);
    }

    public bool SetPluginSetting(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        string normalizedKey = key.Trim();
        string normalizedValue = value ?? "";
        if (PluginSettingsByKey.TryGetValue(normalizedKey, out string? existingValue) &&
            string.Equals(existingValue, normalizedValue, StringComparison.Ordinal))
        {
            return false;
        }

        PluginSettingsByKey[normalizedKey] = normalizedValue;
        return true;
    }

    public bool RemovePluginSetting(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return PluginSettingsByKey.Remove(key.Trim());
    }

    private void NormalizeRecentProjectPathsInPlace()
    {
        for (int pathIndex = RecentProjectPaths.Count - 1; pathIndex >= 0; pathIndex--)
        {
            string normalizedPath = NormalizePath(RecentProjectPaths[pathIndex]);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                RecentProjectPaths.RemoveAt(pathIndex);
                continue;
            }

            RecentProjectPaths[pathIndex] = normalizedPath;
        }

        for (int outerIndex = 0; outerIndex < RecentProjectPaths.Count; outerIndex++)
        {
            string candidatePath = RecentProjectPaths[outerIndex];
            for (int innerIndex = RecentProjectPaths.Count - 1; innerIndex > outerIndex; innerIndex--)
            {
                if (string.Equals(candidatePath, RecentProjectPaths[innerIndex], StringComparison.OrdinalIgnoreCase))
                {
                    RecentProjectPaths.RemoveAt(innerIndex);
                }
            }
        }

        if (RecentProjectPaths.Count > MaxRecentProjectCount)
        {
            RecentProjectPaths.RemoveRange(MaxRecentProjectCount, RecentProjectPaths.Count - MaxRecentProjectCount);
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
            return Path.GetFullPath(path);
        }
        catch
        {
            return path.Trim();
        }
    }

    private void NormalizePluginSettingsInPlace()
    {
        if (PluginSettingsByKey.Count <= 0)
        {
            return;
        }

        var keysToRemove = new List<string>();
        foreach (var pair in PluginSettingsByKey)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                keysToRemove.Add(pair.Key);
            }
        }

        for (int keyIndex = 0; keyIndex < keysToRemove.Count; keyIndex++)
        {
            PluginSettingsByKey.Remove(keysToRemove[keyIndex]);
        }
    }
}

