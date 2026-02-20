using Derp.Doc.Editor;

namespace Derp.Doc;

public static class Program
{
    public static void Main(string[] args)
    {
        var composition = new DocEditorComposition();
        composition.Editor.Run();
    }
}
