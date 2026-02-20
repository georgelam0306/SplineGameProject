using Derp.Doc.Model;
using DerpLib.ImGui.Core;

namespace Derp.Doc.Plugins;

public abstract class DerpDocTableViewRendererBase : IDerpDocTableViewRenderer
{
    public abstract string RendererId { get; }

    public abstract string DisplayName { get; }

    public virtual string? IconGlyph => null;

    public abstract void Draw(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect);

    public virtual bool DrawEmbedded(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        bool interactive,
        string stateKey)
    {
        return false;
    }

    public virtual float MeasureEmbeddedHeight(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        float blockWidth,
        float fallbackHeight)
    {
        return fallbackHeight;
    }

    public virtual float DrawInspector(
        IDerpDocEditorContext workspace,
        DocTable table,
        DocView view,
        ImRect contentRect,
        float y,
        ImStyle style)
    {
        return y;
    }
}
