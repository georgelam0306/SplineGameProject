using System.Numerics;

namespace DerpTech.Rollback;

/// <summary>
/// Validates that multiple replay files from the same session have consistent inputs.
/// Used to verify that all players received the same confirmed inputs.
/// </summary>
public sealed class ReplayInputValidator
{
    private const uint MagicNumber = 0x52504C59; // "RPLY"

    public record ReplayHeader(int Version, int PlayerCount, int InputSize, long Seed);

    public record ValidationResult(
        bool IsValid,
        int TotalFrames,
        int MismatchCount,
        List<InputMismatch> Mismatches,
        string Message);

    public record InputMismatch(
        int Frame,
        int Player,
        List<(string File, byte[] Input)> Inputs);

    /// <summary>
    /// Validates that all replay files have consistent inputs.
    /// Returns validation result with any mismatches found.
    /// </summary>
    public ValidationResult Validate(IReadOnlyList<string> replayPaths, int maxMismatchesToReport = 10)
    {
        if (replayPaths.Count < 2)
        {
            return new ValidationResult(true, 0, 0, [], "Need at least 2 replay files to compare");
        }

        // Read all replays
        var replays = new List<(string Path, ReplayHeader Header, Dictionary<int, Dictionary<int, byte[]>> Frames)>();

        foreach (var path in replayPaths)
        {
            try
            {
                var (header, frames) = ReadReplay(path);
                replays.Add((Path.GetFileName(path), header, frames));
            }
            catch (Exception ex)
            {
                return new ValidationResult(false, 0, 0, [],
                    $"Failed to read {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        // Validate headers match
        var firstHeader = replays[0].Header;
        for (int i = 1; i < replays.Count; i++)
        {
            var h = replays[i].Header;
            if (h.Seed != firstHeader.Seed)
            {
                return new ValidationResult(false, 0, 0, [],
                    $"Seed mismatch: {replays[0].Path} has {firstHeader.Seed}, {replays[i].Path} has {h.Seed}");
            }
            if (h.PlayerCount != firstHeader.PlayerCount)
            {
                return new ValidationResult(false, 0, 0, [],
                    $"PlayerCount mismatch: {replays[0].Path} has {firstHeader.PlayerCount}, {replays[i].Path} has {h.PlayerCount}");
            }
            if (h.InputSize != firstHeader.InputSize)
            {
                return new ValidationResult(false, 0, 0, [],
                    $"InputSize mismatch: {replays[0].Path} has {firstHeader.InputSize}, {replays[i].Path} has {h.InputSize}");
            }
        }

        // Collect all unique frames
        var allFrames = new HashSet<int>();
        foreach (var (_, _, frames) in replays)
        {
            foreach (var frame in frames.Keys)
            {
                allFrames.Add(frame);
            }
        }

        // Compare inputs for each frame/player combination
        var mismatches = new List<InputMismatch>();
        int totalFrames = allFrames.Count;

        foreach (var frame in allFrames.OrderBy(f => f))
        {
            for (int player = 0; player < firstHeader.PlayerCount; player++)
            {
                // Collect all inputs for this frame/player from files that have it
                var inputs = new List<(string File, byte[] Input)>();

                foreach (var (path, _, frames) in replays)
                {
                    if (frames.TryGetValue(frame, out var playerInputs) &&
                        playerInputs.TryGetValue(player, out var input))
                    {
                        inputs.Add((path, input));
                    }
                }

                // If multiple files have this input, verify they match
                if (inputs.Count > 1)
                {
                    var first = inputs[0].Input;
                    bool allMatch = true;

                    for (int i = 1; i < inputs.Count; i++)
                    {
                        if (!first.AsSpan().SequenceEqual(inputs[i].Input))
                        {
                            allMatch = false;
                            break;
                        }
                    }

                    if (!allMatch && mismatches.Count < maxMismatchesToReport)
                    {
                        mismatches.Add(new InputMismatch(frame, player, inputs));
                    }
                    else if (!allMatch)
                    {
                        // Just count, don't store details
                        mismatches.Add(new InputMismatch(frame, player, []));
                    }
                }
            }
        }

        if (mismatches.Count == 0)
        {
            return new ValidationResult(true, totalFrames, 0, [],
                $"All inputs match across {replays.Count} replay files ({totalFrames} frames)");
        }

        return new ValidationResult(false, totalFrames, mismatches.Count, mismatches.Take(maxMismatchesToReport).ToList(),
            $"Found {mismatches.Count} input mismatches across {replays.Count} replay files");
    }

    /// <summary>
    /// Reads a replay file and returns header + frames dictionary.
    /// Frames dictionary: frame number -> (player -> input bytes)
    /// </summary>
    private (ReplayHeader Header, Dictionary<int, Dictionary<int, byte[]>> Frames) ReadReplay(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));

        // Read header
        uint magic = reader.ReadUInt32();
        if (magic != MagicNumber)
        {
            throw new InvalidDataException($"Invalid magic number: {magic:X8}");
        }

        int version = reader.ReadInt32();
        if (version != 3)
        {
            throw new InvalidDataException($"Unsupported version: {version}");
        }

        int playerCount = reader.ReadInt32();
        int inputSize = reader.ReadInt32();
        long seed = reader.ReadInt64();

        var header = new ReplayHeader(version, playerCount, inputSize, seed);
        var frames = new Dictionary<int, Dictionary<int, byte[]>>();

        // Read frames
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            try
            {
                int frameNum = reader.ReadInt32();
                byte presenceMask = reader.ReadByte();

                var playerInputs = new Dictionary<int, byte[]>();

                for (int i = 0; i < playerCount; i++)
                {
                    if ((presenceMask & (1 << i)) != 0)
                    {
                        byte[] input = reader.ReadBytes(inputSize);
                        if (input.Length < inputSize)
                        {
                            break; // EOF
                        }
                        playerInputs[i] = input;
                    }
                }

                if (playerInputs.Count > 0)
                {
                    frames[frameNum] = playerInputs;
                }
            }
            catch (EndOfStreamException)
            {
                break;
            }
        }

        return (header, frames);
    }

    /// <summary>
    /// Prints validation result to console.
    /// </summary>
    public static void PrintResult(ValidationResult result)
    {
        Console.WriteLine();
        Console.WriteLine("=== Replay Input Validation ===");
        Console.WriteLine($"Result: {(result.IsValid ? "PASS" : "FAIL")}");
        Console.WriteLine($"Total frames: {result.TotalFrames}");
        Console.WriteLine($"Mismatches: {result.MismatchCount}");
        Console.WriteLine($"Message: {result.Message}");

        if (result.Mismatches.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("First mismatches:");
            foreach (var mismatch in result.Mismatches.Where(m => m.Inputs.Count > 0))
            {
                Console.WriteLine($"  Frame {mismatch.Frame}, Player {mismatch.Player}:");

                // Find first byte that differs
                var firstInput = mismatch.Inputs[0].Input;
                int diffOffset = -1;
                for (int i = 1; i < mismatch.Inputs.Count && diffOffset < 0; i++)
                {
                    var other = mismatch.Inputs[i].Input;
                    for (int b = 0; b < Math.Min(firstInput.Length, other.Length); b++)
                    {
                        if (firstInput[b] != other[b])
                        {
                            diffOffset = b;
                            break;
                        }
                    }
                }

                foreach (var (file, input) in mismatch.Inputs)
                {
                    // Show bytes around the diff location
                    int start = diffOffset >= 0 ? Math.Max(0, diffOffset - 4) : 0;
                    int end = Math.Min(input.Length, start + 16);
                    string hex = Convert.ToHexString(input.AsSpan(start, end - start));
                    Console.WriteLine($"    {file}: @{start}: {hex}");
                }
            }
        }
        Console.WriteLine();
    }
}
