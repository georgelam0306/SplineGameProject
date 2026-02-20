using Derp.Doc.Model;
using DerpLib.ImGui.Core;

namespace Derp.Doc.Plugins;

public interface IDerpDocNodeSubtableSectionRenderer
{
    string RendererId { get; }

    float MeasureSubtableSectionHeight(
        IDerpDocEditorContext workspace,
        DocTable parentTable,
        DocRow parentRow,
        DocColumn parentSubtableColumn,
        DocTable childTable,
        float contentWidth,
        float fallbackHeight);

    bool DrawSubtableSection(
        IDerpDocEditorContext workspace,
        DocTable parentTable,
        DocRow parentRow,
        DocColumn parentSubtableColumn,
        DocTable childTable,
        ImRect contentRect,
        bool interactive,
        string stateKey);
}
