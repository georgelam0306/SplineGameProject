using Serilog;

namespace BaseTemplate.Headless;

public static class Program
{
    private static int _determinismIterations = 3;
    private static bool _rollbackTest = false;

    public static void Main(string[] args)
    {
        // Initialize logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        if (args.Length < 1)
        {
            Console.WriteLine("Usage: BaseTemplate.Headless <replay-file> [iterations|--rollback]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  replay-file   Path to the replay file (.bin)");
            Console.WriteLine("  iterations    Number of iterations for determinism test (default: 3)");
            Console.WriteLine("  --rollback    Run rollback determinism test (compare normal vs rollback run)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  BaseTemplate.Headless Logs/replay.bin           # 3 iterations");
            Console.WriteLine("  BaseTemplate.Headless Logs/replay.bin 10        # 10 iterations");
            Console.WriteLine("  BaseTemplate.Headless Logs/replay.bin --rollback  # rollback test");
            Environment.ExitCode = 1;
            return;
        }

        string replayPath = args[0];

        if (args.Length >= 2)
        {
            if (args[1] == "--rollback")
            {
                _rollbackTest = true;
            }
            else if (int.TryParse(args[1], out int iterations) && iterations > 0)
            {
                _determinismIterations = iterations;
            }
        }

        RunHeadless(replayPath);

        Log.CloseAndFlush();
    }

    private static void RunHeadless(string replayPath)
    {
        var runner = new HeadlessReplayRunner(Log.Logger);

        if (_rollbackTest)
        {
            // Rollback determinism test - compare normal vs rollback run
            Log.Information("Running ROLLBACK determinism test: {ReplayPath}", replayPath);

            var result = runner.RunRollbackDeterminismTest(replayPath);

            Console.WriteLine();
            Console.WriteLine("=== Rollback Determinism Test Results ===");
            Console.WriteLine($"Replay: {result.ReplayFile}");
            Console.WriteLine($"Result: {(result.IsDeterministic ? "PASS - Rollback deterministic" : "FAIL - Rollback non-deterministic")}");
            if (result.DivergenceFrame.HasValue)
            {
                Console.WriteLine($"Divergence Frame: {result.DivergenceFrame}");
            }
            Console.WriteLine($"Details: {result.Message}");
            Console.WriteLine();

            Environment.ExitCode = result.IsDeterministic ? 0 : 1;
        }
        else if (_determinismIterations > 1)
        {
            // Determinism test mode - run multiple iterations
            Log.Information("Running headless determinism test: {ReplayPath}", replayPath);

            var result = runner.RunDeterminismTest(replayPath, _determinismIterations);

            Console.WriteLine();
            Console.WriteLine("=== Determinism Test Results ===");
            Console.WriteLine($"Replay: {result.ReplayFile}");
            Console.WriteLine($"Iterations: {result.Iterations}");
            Console.WriteLine($"Result: {(result.IsDeterministic ? "PASS - Deterministic" : "FAIL - Non-deterministic")}");
            if (result.DivergenceFrame.HasValue)
            {
                Console.WriteLine($"Divergence Frame: {result.DivergenceFrame}");
            }
            Console.WriteLine($"Details: {result.Message}");
            Console.WriteLine();

            Environment.ExitCode = result.IsDeterministic ? 0 : 1;
        }
        else
        {
            // Single run mode - just replay and output final hash
            Log.Information("Running single headless replay: {ReplayPath}", replayPath);

            var result = runner.RunReplay(replayPath);

            Console.WriteLine();
            Console.WriteLine("=== Headless Replay Results ===");
            Console.WriteLine($"Replay: {result.ReplayFile}");
            Console.WriteLine($"Frames: {result.TotalFrames}");
            Console.WriteLine($"Time: {result.ElapsedMs}ms ({result.TotalFrames * 1000.0 / result.ElapsedMs:F1} fps)");
            Console.WriteLine($"Final Hash: {result.FinalHash:X16}");
            Console.WriteLine();
        }
    }
}
