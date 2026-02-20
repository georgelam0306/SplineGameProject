using Derp.Doc.Model;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Input;

namespace Derp.Doc.Plugins;

public abstract class DerpDocColumnUiPluginBase : IDerpDocColumnUiPlugin
{
    public abstract string ColumnTypeId { get; }

    public virtual bool IsTextWrappedByDefault => false;

    public virtual float GetMinimumRowHeight(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        DocColumn column,
        DocCellValue cell,
        float fallbackHeight)
    {
        return fallbackHeight;
    }

    public virtual bool DrawCell(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        DocColumn column,
        DocCellValue cell,
        ImRect cellRect,
        ImStyle style)
    {
        return false;
    }

    public virtual bool OnCellActivated(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        int displayRowIndex,
        int displayColumnIndex,
        DocColumn column,
        DocCellValue cell)
    {
        return false;
    }

    public virtual bool DrawCellEditor(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        int displayRowIndex,
        int displayColumnIndex,
        DocColumn column,
        DocCellValue cell,
        ImRect cellRect,
        ImInput input)
    {
        return false;
    }

    public virtual bool DrawCellContextMenu(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocRow row,
        int sourceRowIndex,
        int displayRowIndex,
        int displayColumnIndex,
        DocColumn column,
        DocCellValue cell)
    {
        return false;
    }

    public virtual bool DrawColumnContextMenu(
        IDerpDocEditorContext workspace,
        DocTable table,
        int displayColumnIndex,
        DocColumn column)
    {
        return false;
    }

    public virtual float DrawInspector(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocColumn column,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        return y;
    }
}
