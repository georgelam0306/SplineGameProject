using DerpDocDatabase;

namespace SplineGame;

public static class Program
{
    public static void Main()
    {
        using var app = new SplineGameApp(new GameDatabase());
        app.Run();
    }
}
