using Derp.Doc.Editor;

namespace Derp.Doc.Panels;

internal static class DocumentPanel
{
    public static void Draw(DocWorkspace workspace)
    {
        DocumentRenderer.Draw(workspace);
    }
}
