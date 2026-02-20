using System.Text.Json;
using Derp.Doc.Model;

namespace Derp.Doc.Preferences;

internal static class DocUserPreferencesFile
{
    private const string PreferencesDirectoryName = "DerpDoc";
    private const string PreferencesFileName = "preferences.json";

    public static string GetPath()
    {
        string appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appDataDirectory))
        {
            return Path.Combine(appDataDirectory, PreferencesDirectoryName, PreferencesFileName);
        }

        string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(homeDirectory))
        {
            return Path.Combine(homeDirectory, ".derpdoc", PreferencesFileName);
        }

        return Path.Combine(Directory.GetCurrentDirectory(), PreferencesFileName);
    }

    public static DocUserPreferences Read()
    {
        string path = GetPath();
        var preferences = new DocUserPreferences();
        if (!File.Exists(path))
        {
            return preferences;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return preferences;
            }

            if (root.TryGetProperty("uiFontSize", out JsonElement uiFontSizeElement) &&
                TryReadFiniteSingle(uiFontSizeElement, out float uiFontSize))
            {
                preferences.UiFontSize = uiFontSize;
            }

            if (root.TryGetProperty("chatMessageFontSize", out JsonElement chatMessageFontSizeElement) &&
                TryReadFiniteSingle(chatMessageFontSizeElement, out float chatMessageFontSize))
            {
                preferences.ChatMessageFontSize = chatMessageFontSize;
            }

            if (root.TryGetProperty("chatInputFontSize", out JsonElement chatInputFontSizeElement) &&
                TryReadFiniteSingle(chatInputFontSizeElement, out float chatInputFontSize))
            {
                preferences.ChatInputFontSize = chatInputFontSize;
            }

            if (root.TryGetProperty("subtablePreviewQuality", out JsonElement subtablePreviewQualityElement) &&
                subtablePreviewQualityElement.ValueKind == JsonValueKind.String)
            {
                string serializedQuality = subtablePreviewQualityElement.GetString() ?? "";
                if (Enum.TryParse<DocSubtablePreviewQuality>(
                        serializedQuality,
                        ignoreCase: true,
                        out var parsedQuality))
                {
                    preferences.SubtablePreviewQuality = parsedQuality;
                }
            }

            if (root.TryGetProperty("subtablePreviewFrameBudget", out JsonElement subtablePreviewFrameBudgetElement) &&
                TryReadInt32(subtablePreviewFrameBudgetElement, out int subtablePreviewFrameBudget))
            {
                preferences.SubtablePreviewFrameBudget = subtablePreviewFrameBudget;
            }

            if (root.TryGetProperty("recentProjects", out JsonElement recentProjectsElement) &&
                recentProjectsElement.ValueKind == JsonValueKind.Array)
            {
                int recentProjectCount = recentProjectsElement.GetArrayLength();
                for (int projectIndex = recentProjectCount - 1; projectIndex >= 0; projectIndex--)
                {
                    JsonElement pathElement = recentProjectsElement[projectIndex];
                    if (pathElement.ValueKind == JsonValueKind.String)
                    {
                        string recentPath = pathElement.GetString() ?? "";
                        preferences.AddRecentProjectPath(recentPath);
                    }
                }
            }

            if (root.TryGetProperty("pluginSettings", out JsonElement pluginSettingsElement) &&
                pluginSettingsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var settingProperty in pluginSettingsElement.EnumerateObject())
                {
                    if (settingProperty.Value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string settingValue = settingProperty.Value.GetString() ?? "";
                    preferences.SetPluginSetting(settingProperty.Name, settingValue);
                }
            }
        }
        catch
        {
        }

        preferences.ClampInPlace();
        return preferences;
    }

    public static void Write(DocUserPreferences preferences)
    {
        if (preferences == null)
        {
            return;
        }

        preferences.ClampInPlace();
        string path = GetPath();
        string directory = Path.GetDirectoryName(path) ?? ".";
        string tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        Directory.CreateDirectory(directory);

        var payload = new
        {
            uiFontSize = preferences.UiFontSize,
            chatMessageFontSize = preferences.ChatMessageFontSize,
            chatInputFontSize = preferences.ChatInputFontSize,
            subtablePreviewQuality = preferences.SubtablePreviewQuality.ToString(),
            subtablePreviewFrameBudget = preferences.SubtablePreviewFrameBudget,
            recentProjects = preferences.RecentProjectPaths,
            pluginSettings = preferences.PluginSettingsByKey.Count > 0 ? preferences.PluginSettingsByKey : null,
            updatedAtUtc = DateTime.UtcNow.ToString("O"),
        };

        try
        {
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(payload));
            File.Move(tmpPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }
        }
    }

    private static bool TryReadFiniteSingle(JsonElement element, out float value)
    {
        value = 0f;
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (!element.TryGetSingle(out float readValue))
        {
            return false;
        }

        if (float.IsNaN(readValue) || float.IsInfinity(readValue))
        {
            return false;
        }

        value = readValue;
        return true;
    }

    private static bool TryReadInt32(JsonElement element, out int value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return element.TryGetInt32(out value);
    }
}

