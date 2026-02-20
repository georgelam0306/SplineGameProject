using System;
using System.IO;
using System.Text.Json;

namespace Derp.UI;

internal static class UiEditorUserPreferencesFile
{
    private const string PreferencesDirectoryName = "DerpUI";
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
            return Path.Combine(homeDirectory, ".derpui", PreferencesFileName);
        }

        return Path.Combine(Directory.GetCurrentDirectory(), PreferencesFileName);
    }

    public static UiEditorUserPreferences Read()
    {
        string path = GetPath();
        var preferences = new UiEditorUserPreferences();
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

            if (root.TryGetProperty("recentDocuments", out JsonElement recentDocumentsElement) &&
                recentDocumentsElement.ValueKind == JsonValueKind.Array)
            {
                int recentDocumentCount = recentDocumentsElement.GetArrayLength();
                for (int documentIndex = recentDocumentCount - 1; documentIndex >= 0; documentIndex--)
                {
                    JsonElement pathElement = recentDocumentsElement[documentIndex];
                    if (pathElement.ValueKind == JsonValueKind.String)
                    {
                        string recentPath = pathElement.GetString() ?? "";
                        preferences.AddRecentDocumentPath(recentPath);
                    }
                }
            }
        }
        catch
        {
        }

        preferences.ClampInPlace();
        return preferences;
    }

    public static void Write(UiEditorUserPreferences preferences)
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
            recentDocuments = preferences.RecentDocumentPaths,
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
}
