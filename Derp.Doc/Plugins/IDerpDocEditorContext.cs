using Derp.Doc.Model;

namespace Derp.Doc.Plugins;

public interface IDerpDocEditorContext
{
    string WorkspaceRoot { get; }

    string? ProjectPath { get; }

    int ProjectRevision { get; }

    int LiveValueRevision { get; }

    int SelectedRowIndex { get; set; }

    DocProject Project { get; }

    DocTable? ActiveTable { get; }

    DocView? ActiveTableView { get; }

    DocDocument? ActiveDocument { get; }

    bool IsDirty { get; }

    int[]? ComputeViewRowIndices(DocTable table, DocView? view);

    string ResolveRelationDisplayLabel(DocColumn relationColumn, string relationRowId);

    string ResolveRelationDisplayLabel(string relationTableId, string relationRowId);

    bool TryGetGlobalPluginSetting(string key, out string value);

    bool SetGlobalPluginSetting(string key, string value);

    bool RemoveGlobalPluginSetting(string key);

    bool TryGetProjectPluginSetting(string key, out string value);

    bool SetProjectPluginSetting(string key, string value);

    bool RemoveProjectPluginSetting(string key);

    bool SetColumnPluginSettings(DocTable table, DocColumn column, string? pluginSettingsJson);

    void SetStatusMessage(string statusMessage);
}
