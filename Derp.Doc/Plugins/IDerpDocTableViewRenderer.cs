using Derp.Doc.Model;
using DerpLib.ImGui.Core;

namespace Derp.Doc.Plugins;

public interface IDerpDocTableViewRenderer
{
    string RendererId { get; }

    string DisplayName { get; }

    string? IconGlyph { get; }

    void Draw(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect);

    bool DrawEmbedded(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        bool interactive,
        string stateKey);

    float MeasureEmbeddedHeight(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        float blockWidth,
        float fallbackHeight);

    float DrawInspector(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        float y,
        ImStyle style);
}
