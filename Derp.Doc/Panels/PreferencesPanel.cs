using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Preferences;
using Derp.Doc.Plugins;
using Derp.Doc.Tables;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;

namespace Derp.Doc.Panels;

internal static class PreferencesPanel
{
    private const float Padding = 10f;
    private const float HeaderHeight = 28f;
    private const float RowHeight = 36f;
    private const float CellPadding = 8f;
    private const float LabelColumnFraction = 0.42f;
    private const string NanobananaApiBaseUrlPreferenceKey = "nanobanana.apiBaseUrl";
    private const string NanobananaApiKeyPreferenceKey = "nanobanana.apiKey";
    private const string ElevenLabsApiBaseUrlPreferenceKey = "elevenlabs.apiBaseUrl";
    private const string ElevenLabsApiKeyPreferenceKey = "elevenlabs.apiKey";
    private static readonly string[] _subtablePreviewQualityLabels = ["Off", "Lite", "Full"];
    private static readonly List<DocLoadedPluginInfo> _loadedPluginInfosScratch = new();
    private static readonly List<IDerpDocPreferencesProvider> _pluginPreferencesProvidersScratch = new();
    private static readonly char[] _nanobananaApiBaseUrlBuffer = new char[512];
    private static readonly char[] _nanobananaApiKeyBuffer = new char[512];
    private static readonly char[] _elevenLabsApiBaseUrlBuffer = new char[512];
    private static readonly char[] _elevenLabsApiKeyBuffer = new char[512];
    private static int _nanobananaApiBaseUrlBufferLength;
    private static int _nanobananaApiKeyBufferLength;
    private static int _elevenLabsApiBaseUrlBufferLength;
    private static int _elevenLabsApiKeyBufferLength;
    private static string _nanobananaApiBaseUrlSyncedValue = "";
    private static string _nanobananaApiKeySyncedValue = "";
    private static string _elevenLabsApiBaseUrlSyncedValue = "";
    private static string _elevenLabsApiKeySyncedValue = "";
    private static bool _nanobananaApiBaseUrlWasFocused;
    private static bool _nanobananaApiKeyWasFocused;
    private static bool _elevenLabsApiBaseUrlWasFocused;
    private static bool _elevenLabsApiKeyWasFocused;

    public static void Draw(DocWorkspace workspace, ImRect contentRect)
    {
        if (contentRect.Width <= 0f || contentRect.Height <= 0f)
        {
            return;
        }

        var style = Im.Style;
        Im.DrawRect(contentRect.X, contentRect.Y, contentRect.Width, contentRect.Height, style.Background);

        float x = contentRect.X + Padding;
        float y = contentRect.Y + Padding;
        float width = MathF.Max(180f, contentRect.Width - (Padding * 2f));

        Im.Text("Editor Preferences".AsSpan(), x, y, style.FontSize + 1f, style.TextPrimary);
        y += style.FontSize + 8f;

        Im.Text("Saved globally for this user (shared across projects).".AsSpan(), x, y, style.FontSize - 1f, style.TextSecondary);
        y += style.FontSize + 10f;

        float labelColumnWidth = MathF.Max(140f, width * LabelColumnFraction);
        DrawTableHeader(x, y, width, labelColumnWidth);
        y += HeaderHeight;

        float uiFontSize = workspace.UserPreferences.UiFontSize;
        if (DrawNumericPreferenceRow(
            rowIndex: 0,
            label: "UI text size",
            valueId: "pref_ui_text_size_value",
            x: x,
            y: y,
            width: width,
            labelColumnWidth: labelColumnWidth,
            ref uiFontSize,
            DocUserPreferences.MinUiFontSize,
            DocUserPreferences.MaxUiFontSize,
            format: "F1"))
        {
            workspace.SetUiFontSize(SnapFontSize(uiFontSize));
        }
        y += RowHeight;

        float chatMessageFontSize = workspace.UserPreferences.ChatMessageFontSize;
        if (DrawNumericPreferenceRow(
            rowIndex: 1,
            label: "Chat message text size",
            valueId: "pref_chat_message_text_size_value",
            x: x,
            y: y,
            width: width,
            labelColumnWidth: labelColumnWidth,
            ref chatMessageFontSize,
            DocUserPreferences.MinChatFontSize,
            DocUserPreferences.MaxChatFontSize,
            format: "F1"))
        {
            workspace.SetChatMessageFontSize(SnapFontSize(chatMessageFontSize));
        }
        y += RowHeight;

        float chatInputFontSize = workspace.UserPreferences.ChatInputFontSize;
        if (DrawNumericPreferenceRow(
            rowIndex: 2,
            label: "Chat input text size",
            valueId: "pref_chat_input_text_size_value",
            x: x,
            y: y,
            width: width,
            labelColumnWidth: labelColumnWidth,
            ref chatInputFontSize,
            DocUserPreferences.MinChatFontSize,
            DocUserPreferences.MaxChatFontSize,
            format: "F1"))
        {
            workspace.SetChatInputFontSize(SnapFontSize(chatInputFontSize));
        }
        y += RowHeight;

        int subtablePreviewQualityIndex = ResolveSubtablePreviewQualityIndex(workspace.UserPreferences.SubtablePreviewQuality);
        int previousSubtablePreviewQualityIndex = subtablePreviewQualityIndex;
        DrawDropdownPreferenceRow(
            rowIndex: 3,
            label: "Subtable preview quality",
            dropdownId: "pref_subtable_preview_quality",
            x: x,
            y: y,
            width: width,
            labelColumnWidth: labelColumnWidth,
            ref subtablePreviewQualityIndex,
            _subtablePreviewQualityLabels);
        if (subtablePreviewQualityIndex != previousSubtablePreviewQualityIndex)
        {
            workspace.SetSubtablePreviewQuality(ResolveSubtablePreviewQuality(subtablePreviewQualityIndex));
        }
        y += RowHeight;

        float subtablePreviewFrameBudget = workspace.UserPreferences.SubtablePreviewFrameBudget;
        if (DrawNumericPreferenceRow(
            rowIndex: 4,
            label: "Subtable preview frame budget",
            valueId: "pref_subtable_preview_budget_value",
            x: x,
            y: y,
            width: width,
            labelColumnWidth: labelColumnWidth,
            ref subtablePreviewFrameBudget,
            DocUserPreferences.MinSubtablePreviewFrameBudget,
            DocUserPreferences.MaxSubtablePreviewFrameBudget,
            format: "F0"))
        {
            workspace.SetSubtablePreviewFrameBudget((int)MathF.Round(subtablePreviewFrameBudget));
        }
        y += RowHeight;

        DrawTextPreferenceRow(
            workspace: workspace,
            rowIndex: 5,
            label: "Nanobanana API base URL (optional)",
            inputId: "pref_nanobanana_api_base_url",
            pluginSettingKey: NanobananaApiBaseUrlPreferenceKey,
            x: x,
            y: y,
            width: width,
            labelColumnWidth: labelColumnWidth,
            inputBuffer: _nanobananaApiBaseUrlBuffer,
            inputBufferLength: ref _nanobananaApiBaseUrlBufferLength,
            syncedValue: ref _nanobananaApiBaseUrlSyncedValue,
            wasFocused: ref _nanobananaApiBaseUrlWasFocused);
        y += RowHeight;

        DrawTextPreferenceRow(
            workspace: workspace,
            rowIndex: 6,
            label: "Nanobanana API key",
            inputId: "pref_nanobanana_api_key",
            pluginSettingKey: NanobananaApiKeyPreferenceKey,
            x: x,
            y: y,
            width: width,
            labelColumnWidth: labelColumnWidth,
            inputBuffer: _nanobananaApiKeyBuffer,
            inputBufferLength: ref _nanobananaApiKeyBufferLength,
            syncedValue: ref _nanobananaApiKeySyncedValue,
            wasFocused: ref _nanobananaApiKeyWasFocused);
        y += RowHeight + 4f;

        Im.Text(
            "Leave base URL empty to use the default Gemini 3 Pro image endpoint.".AsSpan(),
            x,
            y,
            style.FontSize - 1f,
            style.TextSecondary);
        y += style.FontSize + 4f;

        Im.Text(
            "Used by derpdoc.nanobanana.generate and derpdoc.nanobanana.edit MCP tools.".AsSpan(),
            x,
            y,
            style.FontSize - 1f,
            style.TextSecondary);
        y += style.FontSize + 8f;

        DrawTextPreferenceRow(
            workspace: workspace,
            rowIndex: 7,
            label: "ElevenLabs API base URL (optional)",
            inputId: "pref_elevenlabs_api_base_url",
            pluginSettingKey: ElevenLabsApiBaseUrlPreferenceKey,
            x: x,
            y: y,
            width: width,
            labelColumnWidth: labelColumnWidth,
            inputBuffer: _elevenLabsApiBaseUrlBuffer,
            inputBufferLength: ref _elevenLabsApiBaseUrlBufferLength,
            syncedValue: ref _elevenLabsApiBaseUrlSyncedValue,
            wasFocused: ref _elevenLabsApiBaseUrlWasFocused);
        y += RowHeight;

        DrawTextPreferenceRow(
            workspace: workspace,
            rowIndex: 8,
            label: "ElevenLabs API key",
            inputId: "pref_elevenlabs_api_key",
            pluginSettingKey: ElevenLabsApiKeyPreferenceKey,
            x: x,
            y: y,
            width: width,
            labelColumnWidth: labelColumnWidth,
            inputBuffer: _elevenLabsApiKeyBuffer,
            inputBufferLength: ref _elevenLabsApiKeyBufferLength,
            syncedValue: ref _elevenLabsApiKeySyncedValue,
            wasFocused: ref _elevenLabsApiKeyWasFocused);
        y += RowHeight + 4f;

        Im.Text(
            "Leave base URL empty to use the default ElevenLabs API endpoint.".AsSpan(),
            x,
            y,
            style.FontSize - 1f,
            style.TextSecondary);
        y += style.FontSize + 4f;

        Im.Text(
            "Used by derpdoc.elevenlabs.generate and derpdoc.elevenlabs.edit MCP tools.".AsSpan(),
            x,
            y,
            style.FontSize - 1f,
            style.TextSecondary);
        y += style.FontSize + 12f;

        if (Im.Button("Reset To Defaults", x, y, 170f, MathF.Max(style.MinButtonHeight, 28f)))
        {
            workspace.ResetUserPreferences();
        }

        y += MathF.Max(style.MinButtonHeight, 28f) + 16f;
        DrawPluginSection(workspace, contentRect, x, width, ref y);
    }

    private static float SnapFontSize(float value)
    {
        return MathF.Round(value * 2f) * 0.5f;
    }

    private static void DrawTableHeader(float x, float y, float width, float labelColumnWidth)
    {
        var style = Im.Style;
        Im.DrawRect(x, y, width, HeaderHeight, style.Surface);
        Im.DrawLine(x, y + HeaderHeight, x + width, y + HeaderHeight, 1f, style.Border);
        Im.DrawLine(x + labelColumnWidth, y, x + labelColumnWidth, y + HeaderHeight, 1f, style.Border);

        float textY = y + (HeaderHeight - style.FontSize) * 0.5f;
        Im.Text("Setting".AsSpan(), x + CellPadding, textY, style.FontSize, style.TextSecondary);
        Im.Text("Value".AsSpan(), x + labelColumnWidth + CellPadding, textY, style.FontSize, style.TextSecondary);
    }

    private static bool DrawNumericPreferenceRow(
        int rowIndex,
        string label,
        string valueId,
        float x,
        float y,
        float width,
        float labelColumnWidth,
        ref float value,
        float minValue,
        float maxValue,
        string format)
    {
        var style = Im.Style;
        uint backgroundColor = rowIndex % 2 == 0
            ? ImStyle.WithAlpha(style.Surface, 96)
            : ImStyle.WithAlpha(style.Surface, 80);
        Im.DrawRect(x, y, width, RowHeight, backgroundColor);
        Im.DrawLine(x, y + RowHeight, x + width, y + RowHeight, 1f, style.Border);
        Im.DrawLine(x + labelColumnWidth, y, x + labelColumnWidth, y + RowHeight, 1f, style.Border);

        float labelTextY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), x + CellPadding, labelTextY, style.FontSize, style.TextPrimary);

        float inputX = x + labelColumnWidth + CellPadding;
        float inputWidth = MathF.Max(80f, width - labelColumnWidth - (CellPadding * 2f));
        float inputY = y + (RowHeight - style.MinButtonHeight) * 0.5f;
        return ImScalarInput.DrawAt(
            valueId,
            inputX,
            inputY,
            inputWidth,
            ref value,
            minValue,
            maxValue,
            format: format);
    }

    private static void DrawDropdownPreferenceRow(
        int rowIndex,
        string label,
        string dropdownId,
        float x,
        float y,
        float width,
        float labelColumnWidth,
        ref int selectedIndex,
        string[] options)
    {
        var style = Im.Style;
        uint backgroundColor = rowIndex % 2 == 0
            ? ImStyle.WithAlpha(style.Surface, 96)
            : ImStyle.WithAlpha(style.Surface, 80);
        Im.DrawRect(x, y, width, RowHeight, backgroundColor);
        Im.DrawLine(x, y + RowHeight, x + width, y + RowHeight, 1f, style.Border);
        Im.DrawLine(x + labelColumnWidth, y, x + labelColumnWidth, y + RowHeight, 1f, style.Border);

        float labelTextY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), x + CellPadding, labelTextY, style.FontSize, style.TextPrimary);

        float inputX = x + labelColumnWidth + CellPadding;
        float inputWidth = MathF.Max(80f, width - labelColumnWidth - (CellPadding * 2f));
        float inputY = y + (RowHeight - style.MinButtonHeight) * 0.5f;
        Im.Dropdown(
            dropdownId,
            options.AsSpan(),
            ref selectedIndex,
            inputX,
            inputY,
            inputWidth);
    }

    private static void DrawTextPreferenceRow(
        DocWorkspace workspace,
        int rowIndex,
        string label,
        string inputId,
        string pluginSettingKey,
        float x,
        float y,
        float width,
        float labelColumnWidth,
        char[] inputBuffer,
        ref int inputBufferLength,
        ref string syncedValue,
        ref bool wasFocused)
    {
        var style = Im.Style;
        uint backgroundColor = rowIndex % 2 == 0
            ? ImStyle.WithAlpha(style.Surface, 96)
            : ImStyle.WithAlpha(style.Surface, 80);
        Im.DrawRect(x, y, width, RowHeight, backgroundColor);
        Im.DrawLine(x, y + RowHeight, x + width, y + RowHeight, 1f, style.Border);
        Im.DrawLine(x + labelColumnWidth, y, x + labelColumnWidth, y + RowHeight, 1f, style.Border);

        float labelTextY = y + (RowHeight - style.FontSize) * 0.5f;
        Im.Text(label.AsSpan(), x + CellPadding, labelTextY, style.FontSize, style.TextPrimary);

        string currentValue = "";
        if (workspace.TryGetGlobalPluginSetting(pluginSettingKey, out string persistedValue))
        {
            currentValue = persistedValue;
        }

        if (!wasFocused && !string.Equals(currentValue, syncedValue, StringComparison.Ordinal))
        {
            CopyStringToBuffer(currentValue, inputBuffer, out inputBufferLength);
            syncedValue = currentValue;
        }

        float inputX = x + labelColumnWidth + CellPadding;
        float inputWidth = MathF.Max(80f, width - labelColumnWidth - (CellPadding * 2f));
        float inputY = y + (RowHeight - style.MinButtonHeight) * 0.5f;
        _ = Im.TextInput(
            inputId,
            inputBuffer,
            ref inputBufferLength,
            inputBuffer.Length,
            inputX,
            inputY,
            inputWidth);

        if (!ShouldCommitTextInput(inputId, ref wasFocused))
        {
            return;
        }

        string newValue = new string(inputBuffer, 0, inputBufferLength).Trim();
        if (string.IsNullOrWhiteSpace(newValue))
        {
            workspace.RemoveGlobalPluginSetting(pluginSettingKey);
            syncedValue = "";
            return;
        }

        workspace.SetGlobalPluginSetting(pluginSettingKey, newValue);
        syncedValue = newValue;
    }

    private static void CopyStringToBuffer(string value, char[] destinationBuffer, out int destinationLength)
    {
        destinationLength = 0;
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        destinationLength = Math.Min(value.Length, destinationBuffer.Length);
        value.AsSpan(0, destinationLength).CopyTo(destinationBuffer);
    }

    private static bool ShouldCommitTextInput(string inputId, ref bool wasFocused)
    {
        int widgetId = Im.Context.GetId(inputId);
        bool isFocused = Im.Context.IsFocused(widgetId);
        bool shouldCommit = false;
        var input = Im.Context.Input;
        if (isFocused && (input.KeyEnter || input.KeyTab))
        {
            shouldCommit = true;
        }
        else if (!isFocused && wasFocused)
        {
            shouldCommit = true;
        }

        wasFocused = isFocused;
        return shouldCommit;
    }

    private static int ResolveSubtablePreviewQualityIndex(DocSubtablePreviewQuality quality)
    {
        return quality switch
        {
            DocSubtablePreviewQuality.Off => 0,
            DocSubtablePreviewQuality.Lite => 1,
            DocSubtablePreviewQuality.Full => 2,
            _ => 2,
        };
    }

    private static DocSubtablePreviewQuality ResolveSubtablePreviewQuality(int index)
    {
        return index switch
        {
            0 => DocSubtablePreviewQuality.Off,
            1 => DocSubtablePreviewQuality.Lite,
            2 => DocSubtablePreviewQuality.Full,
            _ => DocSubtablePreviewQuality.Full,
        };
    }

    private static void DrawPluginSection(
        DocWorkspace workspace,
        ImRect contentRect,
        float x,
        float width,
        ref float y)
    {
        var style = Im.Style;
        Im.Text("Plugins".AsSpan(), x, y, style.FontSize + 1f, style.TextPrimary);
        y += style.FontSize + 6f;

        if (Im.Button("Reload Project Plugins", x, y, 190f, MathF.Max(style.MinButtonHeight, 26f)))
        {
            workspace.ReloadPluginsForActiveProject();
        }

        y += MathF.Max(style.MinButtonHeight, 26f) + 8f;

        string loadMessage = workspace.GetPluginLoadMessage();
        if (!string.IsNullOrWhiteSpace(loadMessage))
        {
            Im.Text(loadMessage.AsSpan(), x, y, style.FontSize - 1f, style.TextSecondary);
            y += style.FontSize + 4f;
        }

        string countsLine =
            "Loaded: " + workspace.GetLoadedPluginCount().ToString() +
            " | Column types: " + ColumnTypeDefinitionRegistry.Count.ToString() +
            " | Cell codecs: " + ColumnCellCodecProviderRegistry.Count.ToString() +
            " | Defaults: " + ColumnDefaultValueProviderRegistry.Count.ToString() +
            " | Formula funcs: " + FormulaFunctionRegistry.Count.ToString() +
            " | View renderers: " + TableViewRendererRegistry.Count.ToString() +
            " | UI plugins: " + ColumnUiPluginRegistry.Count.ToString() +
            " | Pref providers: " + PluginPreferencesProviderRegistry.Count.ToString() +
            " | Automation: " + PluginAutomationProviderRegistry.Count.ToString();
        Im.Text(countsLine.AsSpan(), x, y, style.FontSize - 1f, style.TextSecondary);
        y += style.FontSize + 8f;

        workspace.CopyLoadedPluginInfos(_loadedPluginInfosScratch);
        if (_loadedPluginInfosScratch.Count <= 0)
        {
            Im.Text("(no loaded plugins)".AsSpan(), x, y, style.FontSize - 1f, style.TextSecondary);
            y += style.FontSize + 8f;
        }
        else
        {
            for (int pluginIndex = 0; pluginIndex < _loadedPluginInfosScratch.Count; pluginIndex++)
            {
                var pluginInfo = _loadedPluginInfosScratch[pluginIndex];
                Im.Text(pluginInfo.Id.AsSpan(), x, y, style.FontSize, style.TextPrimary);
                y += style.FontSize + 2f;
                Im.Text(pluginInfo.AssemblyPath.AsSpan(), x + 10f, y, style.FontSize - 1f, style.TextSecondary);
                y += style.FontSize + 6f;
            }
        }

        PluginPreferencesProviderRegistry.CopyProviders(_pluginPreferencesProvidersScratch);
        if (_pluginPreferencesProvidersScratch.Count <= 0)
        {
            return;
        }

        y += 4f;
        Im.Text("Plugin Preferences".AsSpan(), x, y, style.FontSize, style.TextPrimary);
        y += style.FontSize + 6f;

        for (int providerIndex = 0; providerIndex < _pluginPreferencesProvidersScratch.Count; providerIndex++)
        {
            var provider = _pluginPreferencesProvidersScratch[providerIndex];
            Im.Text(provider.DisplayName.AsSpan(), x, y, style.FontSize, style.TextSecondary);
            y += style.FontSize + 4f;

            float providerStartY = y;
            float nextY = provider.DrawPreferences(workspace, contentRect, y, style);
            if (nextY <= providerStartY)
            {
                nextY = providerStartY + 4f;
            }

            y = nextY + 8f;
        }
    }
}
