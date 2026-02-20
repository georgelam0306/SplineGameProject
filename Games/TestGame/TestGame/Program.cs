namespace TestGame;

public static class Program
{
    public static void Main()
    {
        using var composition = new TestGameComposition();
        composition.App.Run();
    }
}
