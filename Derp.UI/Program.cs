namespace Derp.UI;

public static class Program
{
    public static void Main(string[] args)
    {
        var composition = new UiEditorComposition();
        composition.Editor.Run();
    }
}
