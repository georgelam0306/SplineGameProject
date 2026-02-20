using System;
using System.Collections.Generic;

namespace Derp.Doc.Model;

public sealed class DocProjectUiState
{
    public Dictionary<string, bool> TableFolderExpandedById { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, bool> DocumentFolderExpandedById { get; set; } = new(StringComparer.Ordinal);

    public bool TryGetFolderExpanded(DocFolderScope scope, string folderId, out bool expanded)
    {
        expanded = false;
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return false;
        }

        var map = scope == DocFolderScope.Tables
            ? TableFolderExpandedById
            : DocumentFolderExpandedById;
        return map.TryGetValue(folderId, out expanded);
    }

    public bool SetFolderExpanded(DocFolderScope scope, string folderId, bool expanded)
    {
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return false;
        }

        var map = scope == DocFolderScope.Tables
            ? TableFolderExpandedById
            : DocumentFolderExpandedById;
        if (map.TryGetValue(folderId, out bool currentExpanded) && currentExpanded == expanded)
        {
            return false;
        }

        map[folderId] = expanded;
        return true;
    }

}
