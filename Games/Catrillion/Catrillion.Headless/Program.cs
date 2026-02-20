using DerpTech.Rollback;
using Serilog;

namespace Catrillion.Headless;

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
            PrintUsage();
            Environment.ExitCode = 1;
            return;
        }

        // Check for --validate-inputs mode (requires multiple files)
        if (args[0] == "--validate-inputs")
        {
            var replayFiles = args.Skip(1).ToList();
            if (replayFiles.Count < 2)
            {
                Console.WriteLine("Error: --validate-inputs requires at least 2 replay files");
                Environment.ExitCode = 1;
                return;
            }
            RunValidateInputs(replayFiles);
            Log.CloseAndFlush();
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

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: Catrillion.Headless <replay-file> [iterations|--rollback]");
        Console.WriteLine("       Catrillion.Headless --validate-inputs <replay1> <replay2> [...]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  replay-file       Path to the replay file (.bin)");
        Console.WriteLine("  iterations        Number of iterations for determinism test (default: 3)");
        Console.WriteLine("  --rollback        Run rollback determinism test (compare normal vs rollback run)");
        Console.WriteLine("  --validate-inputs Validate inputs match across multiple replay files");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  Catrillion.Headless Logs/replay.bin              # 3 iterations");
        Console.WriteLine("  Catrillion.Headless Logs/replay.bin 10           # 10 iterations");
        Console.WriteLine("  Catrillion.Headless Logs/replay.bin --rollback   # rollback test");
        Console.WriteLine("  Catrillion.Headless --validate-inputs p0.bin p1.bin p2.bin");
    }

    private static void RunValidateInputs(List<string> replayPaths)
    {
        Log.Information("Validating inputs across {Count} replay files", replayPaths.Count);

        var validator = new ReplayInputValidator();
        var result = validator.Validate(replayPaths);

        ReplayInputValidator.PrintResult(result);

        Environment.ExitCode = result.IsValid ? 0 : 1;
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
