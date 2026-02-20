namespace DerpLib.Examples;

public static class Program
{
    public static void Main(string[] args)
    {
        // If CLI argument provided, run that scene directly
        if (args.Length > 0)
        {
            if (SceneSelector.TryRunScene(args[0]))
                return;

            // Unknown scene - show available options
            Console.WriteLine($"Unknown scene: {args[0]}");
            Console.WriteLine("Available scenes: 3d, sdf, warp, trim, stress, imgui, font, mask, widgets, scroll");
            return;
        }

        // No args - show scene selector UI
        SceneSelector.Run();
    }
}
