using Derp.Doc.Model;
using DerpLib.ImGui.Core;

namespace Derp.Doc.Plugins;

/// <summary>
/// Optional extension point for custom table renderers used in Subtable cell previews.
/// Implement this to provide renderer-specific subtable settings UI and preview drawing
/// that consumes per-column settings JSON.
/// </summary>
public interface IDerpDocSubtableDisplayRenderer
{
    bool DrawSubtableDisplayPreview(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        DocColumn parentSubtableColumn,
        string? pluginSettingsJson,
        ImRect contentRect,
        bool interactive,
        string stateKey);

    float MeasureSubtableDisplaySettingsHeight(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocColumn parentSubtableColumn,
        string? pluginSettingsJson,
        float contentWidth,
        ImStyle style);

    float DrawSubtableDisplaySettingsEditor(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocColumn parentSubtableColumn,
        ref string? pluginSettingsJson,
        ImRect contentRect,
        float y,
        ImStyle style);
}
