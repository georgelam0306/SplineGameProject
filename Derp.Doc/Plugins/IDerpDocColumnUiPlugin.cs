using Derp.Doc.Model;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;

namespace Derp.Doc.Plugins;

public interface IDerpDocColumnUiPlugin
{
    string ColumnTypeId { get; }

    bool IsTextWrappedByDefault { get; }

    float GetMinimumRowHeight(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        DocColumn column,
        DocCellValue cell,
        float fallbackHeight);

    bool DrawCell(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        DocColumn column,
        DocCellValue cell,
        ImRect cellRect,
        ImStyle style);

    bool OnCellActivated(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        int displayRowIndex,
        int displayColumnIndex,
        DocColumn column,
        DocCellValue cell);

    bool DrawCellEditor(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        int displayRowIndex,
        int displayColumnIndex,
        DocColumn column,
        DocCellValue cell,
        ImRect cellRect,
        ImInput input);

    bool DrawCellContextMenu(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        int displayRowIndex,
        int displayColumnIndex,
        DocColumn column,
        DocCellValue cell);

    bool DrawColumnContextMenu(
        IDerpDocEditorContext workspace,
        DocTable table,
        int displayColumnIndex,
        DocColumn column);

    float DrawInspector(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocColumn column,
        ImRect contentRect,
        float y,
        ImStyle style);
}
