using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Serilog;
using BaseTemplate.Infrastructure.Rollback;

namespace BaseTemplate.Headless;

/// <summary>
/// Headless replay runner for determinism testing.
/// Runs a replay at maximum speed without Raylib/graphics.
/// Outputs frame hashes for comparison across runs.
/// </summary>
public sealed class HeadlessReplayRunner
{
    private readonly ILogger _log;

    public HeadlessReplayRunner(ILogger logger)
    {
        _log = logger.ForContext<HeadlessReplayRunner>();
    }

    /// <summary>
    /// Result of running a headless replay.
    /// </summary>
    public readonly record struct ReplayResult(
        string ReplayFile,
        int TotalFrames,
        long ElapsedMs,
        Dictionary<int, ulong> FrameHashes,
        ulong FinalHash
    );

    /// <summary>
    /// Result of a determinism test.
    /// </summary>
    public readonly record struct DeterminismResult(
        string ReplayFile,
        int Iterations,
        bool IsDeterministic,
        int? DivergenceFrame,
        string Message
    );

    /// <summary>
    /// Run a replay file headlessly at max speed.
    /// Returns frame hashes for determinism comparison.
    /// </summary>
    public ReplayResult RunReplay(string replayFilePath, int hashInterval = 1)
    {
        _log.Information("Starting headless replay: {Path}", replayFilePath);

        if (!File.Exists(replayFilePath))
        {
            throw new FileNotFoundException($"Replay file not found: {replayFilePath}");
        }

        var frameHashes = new Dictionary<int, ulong>();
        ulong finalHash = 0;
        int totalFrames = 0;

        var sw = Stopwatch.StartNew();

        // Create headless composition for replay
        using var composition = new HeadlessComposition(replayFilePath);
        var gameSimulation = composition.GameSimulation;
        var rollbackManager = composition.RollbackManager;
        var replayManager = composition.ReplayManager;
        int playerCount = replayManager.PlayerCount;

        _log.Information("Replay loaded: {PlayerCount} players, seed={Seed}",
            playerCount, replayManager.Seed);

        // Restore session seed from replay file
        gameSimulation.SetSessionSeed((int)replayManager.Seed);

        // Reset simulation to initial state
        gameSimulation.Reset();
        rollbackManager.Clear();

        // Allocate input buffer
        Span<GameInput> replayInputs = stackalloc GameInput[playerCount];

        int currentFrame = 0;
        while (!replayManager.ReplayEnded)
        {
            // Get inputs from replay file
            if (!replayManager.TryGetReplayInputs(currentFrame, replayInputs))
            {
                break;
            }

            // Store replay inputs into buffer
            for (int i = 0; i < playerCount; i++)
            {
                rollbackManager.InputBuffer.StoreInput(currentFrame, i, replayInputs[i]);
            }

            // Tick simulation
            gameSimulation.SetFrame(currentFrame);
            gameSimulation.Tick();

            // Compute and store hash
            if (hashInterval == 1 || currentFrame % hashInterval == 0)
            {
                ulong hash = rollbackManager.GetStateHash();
                frameHashes[currentFrame] = hash;
                finalHash = hash;

                // Log progress every 1000 frames
                if (currentFrame > 0 && currentFrame % 1000 == 0)
                {
                    _log.Information("Frame {Frame}: hash={Hash:X16}", currentFrame, hash);
                }
            }

            currentFrame++;
            totalFrames = currentFrame;
        }

        sw.Stop();

        _log.Information("Replay complete: {Frames} frames in {Ms}ms ({Fps:F1} fps)",
            totalFrames, sw.ElapsedMilliseconds,
            totalFrames * 1000.0 / sw.ElapsedMilliseconds);

        return new ReplayResult(
            ReplayFile: replayFilePath,
            TotalFrames: totalFrames,
            ElapsedMs: sw.ElapsedMilliseconds,
            FrameHashes: frameHashes,
            FinalHash: finalHash
        );
    }

    /// <summary>
    /// Run a replay multiple times and compare hashes for determinism.
    /// </summary>
    public DeterminismResult RunDeterminismTest(string replayFilePath, int iterations = 3)
    {
        _log.Information("Starting determinism test: {Path} x{Iterations}", replayFilePath, iterations);

        var allRuns = new List<ReplayResult>();

        for (int i = 0; i < iterations; i++)
        {
            _log.Information("Run {Run}/{Total}...", i + 1, iterations);
            var result = RunReplay(replayFilePath);
            allRuns.Add(result);
        }

        return CompareRuns(replayFilePath, allRuns);
    }

    /// <summary>
    /// Run a replay with periodic rollbacks using predictions, simulating real network play.
    /// Every rollbackInterval frames:
    /// 1. Save snapshot
    /// 2. Simulate rollbackDepth frames with PREDICTED (empty) inputs for player 1+
    /// 3. Restore snapshot
    /// 4. Re-simulate with CORRECT inputs
    /// This tests if prediction→rollback→resimulate produces same result as straight-through.
    /// </summary>
    public ReplayResult RunReplayWithRollbacks(string replayFilePath, int rollbackInterval = 10, int rollbackDepth = 5)
    {
        _log.Information("Starting headless replay WITH PREDICTION ROLLBACKS: {Path} (interval={Interval}, depth={Depth})",
            replayFilePath, rollbackInterval, rollbackDepth);

        if (!File.Exists(replayFilePath))
        {
            throw new FileNotFoundException($"Replay file not found: {replayFilePath}");
        }

        var frameHashes = new Dictionary<int, ulong>();
        ulong finalHash = 0;
        int totalFrames = 0;
        int rollbackCount = 0;

        var sw = Stopwatch.StartNew();

        using var composition = new HeadlessComposition(replayFilePath);
        var gameSimulation = composition.GameSimulation;
        var rollbackManager = composition.RollbackManager;
        var replayManager = composition.ReplayManager;
        var derivedRunner = composition.DerivedSystemRunner;
        int playerCount = replayManager.PlayerCount;

        _log.Information("Replay loaded: {PlayerCount} players, seed={Seed}",
            playerCount, replayManager.Seed);

        gameSimulation.SetSessionSeed((int)replayManager.Seed);
        gameSimulation.Reset();
        rollbackManager.Clear();

        // Pre-load all inputs so we can access them for predictions
        var allInputs = new Dictionary<int, GameInput[]>();
        {
            Span<GameInput> tempInputs = stackalloc GameInput[playerCount];
            int preloadFrame = 0;
            while (!replayManager.ReplayEnded)
            {
                if (!replayManager.TryGetReplayInputs(preloadFrame, tempInputs))
                    break;
                allInputs[preloadFrame] = tempInputs.ToArray();
                preloadFrame++;
            }
        }

        int maxFrame = allInputs.Count;
        _log.Information("Pre-loaded {Count} frames of inputs", maxFrame);

        int currentFrame = 0;
        while (currentFrame < maxFrame)
        {
            // Every rollbackInterval frames, simulate prediction scenario
            if (currentFrame > 0 && currentFrame % rollbackInterval == 0 && currentFrame + rollbackDepth < maxFrame)
            {
                // Save snapshot at current frame
                rollbackManager.SaveSnapshot(currentFrame);
                int snapshotFrame = currentFrame;

                // Simulate next rollbackDepth frames with PREDICTIONS
                // Player 0 gets correct input, players 1+ get empty (predicted)
                for (int f = currentFrame; f < currentFrame + rollbackDepth; f++)
                {
                    var correctInputs = allInputs[f];

                    // Store player 0's correct input
                    rollbackManager.InputBuffer.StoreInput(f, 0, correctInputs[0]);

                    // Store predictions (empty) for other players
                    for (int p = 1; p < playerCount; p++)
                    {
                        rollbackManager.InputBuffer.StorePredictedInput(f, p, GameInput.Empty);
                    }

                    rollbackManager.SaveSnapshot(f);
                    gameSimulation.SetFrame(f);
                    gameSimulation.Tick();
                }

                // Now "receive" the correct inputs and rollback
                rollbackManager.RestoreSnapshot(snapshotFrame, currentFrame + rollbackDepth - 1);
                derivedRunner.InvalidateAll();

                // Derived systems (PowerGrid, BuildingOccupancy) will be rebuilt in next tick's RebuildAll()

                // Re-simulate with CORRECT inputs
                for (int f = snapshotFrame; f < currentFrame + rollbackDepth; f++)
                {
                    var correctInputs = allInputs[f];
                    for (int p = 0; p < playerCount; p++)
                    {
                        rollbackManager.InputBuffer.StoreInput(f, p, correctInputs[p]);
                    }

                    rollbackManager.SaveSnapshot(f);
                    gameSimulation.SetFrame(f);
                    gameSimulation.Tick();
                }

                currentFrame += rollbackDepth;
                rollbackCount++;
            }
            else
            {
                // Normal frame - store correct inputs and tick
                var inputs = allInputs[currentFrame];
                for (int p = 0; p < playerCount; p++)
                {
                    rollbackManager.InputBuffer.StoreInput(currentFrame, p, inputs[p]);
                }

                rollbackManager.SaveSnapshot(currentFrame);
                gameSimulation.SetFrame(currentFrame);
                gameSimulation.Tick();
                currentFrame++;
            }

            // Record hash
            ulong hash = rollbackManager.GetStateHash();
            frameHashes[currentFrame - 1] = hash;
            finalHash = hash;

            // Log frames with any input
            int frame = currentFrame - 1;
            if (allInputs.TryGetValue(frame, out var frameInputs))
            {
                for (int p = 0; p < frameInputs.Length; p++)
                {
                    var inp = frameInputs[p];
                    if (!inp.IsEmpty)
                    {
                        _log.Information("Frame {Frame} P{Player}: HasInput=true", frame, p);
                    }
                }
            }

            if ((currentFrame - 1) > 0 && (currentFrame - 1) % 1000 == 0)
            {
                _log.Information("Frame {Frame}: hash={Hash:X16} (rollbacks={Rollbacks})",
                    currentFrame - 1, hash, rollbackCount);
            }

            totalFrames = currentFrame;
        }

        sw.Stop();

        _log.Information("Replay with prediction rollbacks complete: {Frames} frames, {Rollbacks} rollbacks in {Ms}ms ({Fps:F1} fps)",
            totalFrames, rollbackCount, sw.ElapsedMilliseconds,
            totalFrames * 1000.0 / sw.ElapsedMilliseconds);

        return new ReplayResult(
            ReplayFile: replayFilePath,
            TotalFrames: totalFrames,
            ElapsedMs: sw.ElapsedMilliseconds,
            FrameHashes: frameHashes,
            FinalHash: finalHash
        );
    }

    /// <summary>
    /// Test rollback determinism: run once normally, once with rollbacks, compare.
    /// On failure, re-runs with per-system hashing to identify the divergent system.
    /// </summary>
    public DeterminismResult RunRollbackDeterminismTest(string replayFilePath)
    {
        _log.Information("Starting ROLLBACK determinism test: {Path}", replayFilePath);

        _log.Information("Run 1: Normal (no rollbacks)...");
        var normalResult = RunReplay(replayFilePath);

        _log.Information("Run 2: With rollbacks...");
        var rollbackResult = RunReplayWithRollbacks(replayFilePath);

        // Compare hashes
        foreach (var (frame, normalHash) in normalResult.FrameHashes)
        {
            if (rollbackResult.FrameHashes.TryGetValue(frame, out var rollbackHash))
            {
                if (rollbackHash != normalHash)
                {
                    _log.Error("ROLLBACK DESYNC at frame {Frame}: normal={Hash0:X16}, rollback={Hash1:X16}",
                        frame, normalHash, rollbackHash);

                    // Re-run with per-system hashing to identify divergent system
                    _log.Information("Re-running with per-system hashing to identify divergent system...");
                    var divergentSystem = IdentifyDivergentSystem(replayFilePath, frame);

                    return new DeterminismResult(
                        ReplayFile: replayFilePath,
                        Iterations: 2,
                        IsDeterministic: false,
                        DivergenceFrame: frame,
                        Message: $"Rollback desync at frame {frame}. {divergentSystem}"
                    );
                }
            }
        }

        _log.Information("Rollback determinism test PASSED: {Frames} frames", normalResult.TotalFrames);

        return new DeterminismResult(
            ReplayFile: replayFilePath,
            Iterations: 2,
            IsDeterministic: true,
            DivergenceFrame: null,
            Message: $"Normal and rollback runs produced identical hashes for {normalResult.TotalFrames} frames"
        );
    }

    /// <summary>
    /// Re-run simulation up to divergence frame with per-system hashing to identify which system diverges.
    /// Exports the actual divergent state inline (not re-running after the fact).
    /// </summary>
    private string IdentifyDivergentSystem(string replayFilePath, int divergenceFrame)
    {
        // Run normal up to divergence frame with per-system hashing and export
        var normalResult = RunWithPerSystemHashing(replayFilePath, divergenceFrame, useRollbacks: false, exportLabel: "normal");

        // Run with rollbacks up to divergence frame with per-system hashing and export
        // This captures the ACTUAL divergent state, not a re-run
        var rollbackResult = RunWithPerSystemHashing(replayFilePath, divergenceFrame, useRollbacks: true, exportLabel: "rollback");

        if (normalResult == null || rollbackResult == null)
        {
            return "Could not capture per-system hashes";
        }

        var normalHashes = normalResult.Value.hashes;
        var rollbackHashes = rollbackResult.Value.hashes;
        var normalTableHashes = normalResult.Value.tableHashes;
        var rollbackTableHashes = rollbackResult.Value.tableHashes;

        // Compare per-system hashes
        for (int i = 0; i < normalHashes.Length && i < rollbackHashes.Length; i++)
        {
            if (normalHashes[i] != rollbackHashes[i])
            {
                string systemName = i < normalResult.Value.systemNames.Length
                    ? normalResult.Value.systemNames[i]
                    : $"System[{i}]";

                _log.Error("First divergence after system '{System}': normal={Hash0:X16}, rollback={Hash1:X16}",
                    systemName, normalHashes[i], rollbackHashes[i]);

                // State already exported inline by RunWithPerSystemHashing

                // Show per-table hashes to identify which table(s) diverged
                if (normalTableHashes != null && rollbackTableHashes != null)
                {
                    _log.Information("Per-table hashes at frame {Frame}:", divergenceFrame);
                    foreach (var tableName in normalTableHashes.Keys)
                    {
                        var normalHash = normalTableHashes[tableName];
                        var rollbackHash = rollbackTableHashes.TryGetValue(tableName, out var rh) ? rh : 0;
                        if (normalHash != rollbackHash)
                        {
                            _log.Error("  TABLE '{Table}' DIVERGED: normal={Hash0:X16}, rollback={Hash1:X16}",
                                tableName, normalHash, rollbackHash);
                        }
                    }
                }

                // If divergence is at BeginFrame, trace back to find where it actually starts
                // Rollback interval is 10, depth is 5, so check the rollback point
                if (systemName == "BeginFrame" && divergenceFrame > 0)
                {
                    int rollbackInterval = 10;
                    int rollbackPoint = (divergenceFrame / rollbackInterval) * rollbackInterval;
                    _log.Information("Divergence at BeginFrame. Rollback point is frame {RollbackPoint}. Checking first frame after rollback...", rollbackPoint);

                    // Check the first frame of the rollback block
                    var firstNormalResult = RunWithPerSystemHashing(replayFilePath, rollbackPoint, useRollbacks: false);
                    var firstRollbackResult = RunWithPerSystemHashing(replayFilePath, rollbackPoint, useRollbacks: true);

                    if (firstNormalResult != null && firstRollbackResult != null)
                    {
                        var firstNormalHashes = firstNormalResult.Value.hashes;
                        var firstRollbackHashes = firstRollbackResult.Value.hashes;

                        _log.Information("Frame {Frame} per-system comparison:", rollbackPoint);
                        for (int j = 0; j < firstNormalHashes.Length && j < firstRollbackHashes.Length; j++)
                        {
                            string sysName = j < firstNormalResult.Value.systemNames.Length
                                ? firstNormalResult.Value.systemNames[j]
                                : $"System[{j}]";

                            if (firstNormalHashes[j] != firstRollbackHashes[j])
                            {
                                _log.Error("  DIVERGED after '{System}': normal={Hash0:X16}, rollback={Hash1:X16}",
                                    sysName, firstNormalHashes[j], firstRollbackHashes[j]);

                                // Show per-table hashes at this point
                                if (firstNormalResult.Value.tableHashes != null && firstRollbackResult.Value.tableHashes != null)
                                {
                                    _log.Information("Per-table hashes at divergence:");
                                    foreach (var tableName in firstNormalResult.Value.tableHashes.Keys)
                                    {
                                        var normalTableHash = firstNormalResult.Value.tableHashes[tableName];
                                        var rollbackTableHash = firstRollbackResult.Value.tableHashes.TryGetValue(tableName, out var rh) ? rh : 0;
                                        if (normalTableHash != rollbackTableHash)
                                        {
                                            _log.Error("    TABLE '{Table}': normal={Hash0:X16}, rollback={Hash1:X16}",
                                                tableName, normalTableHash, rollbackTableHash);
                                        }
                                    }
                                }

                                return $"Divergence at frame {rollbackPoint} after '{sysName}': normal={firstNormalHashes[j]:X16}, rollback={firstRollbackHashes[j]:X16}";
                            }
                        }
                        _log.Information("Frame {Frame} hashes match - checking subsequent frames in rollback block...", rollbackPoint);

                        // Check each frame 2101, 2102, 2103
                        for (int checkFrame = rollbackPoint + 1; checkFrame < divergenceFrame; checkFrame++)
                        {
                            var checkNormal = RunWithPerSystemHashing(replayFilePath, checkFrame, useRollbacks: false);
                            var checkRollback = RunWithPerSystemHashing(replayFilePath, checkFrame, useRollbacks: true);

                            if (checkNormal != null && checkRollback != null)
                            {
                                _log.Information("Frame {Frame} per-system comparison:", checkFrame);
                                for (int k = 0; k < checkNormal.Value.hashes.Length && k < checkRollback.Value.hashes.Length; k++)
                                {
                                    string sysNameK = k < checkNormal.Value.systemNames.Length
                                        ? checkNormal.Value.systemNames[k]
                                        : $"System[{k}]";

                                    if (checkNormal.Value.hashes[k] != checkRollback.Value.hashes[k])
                                    {
                                        _log.Error("  DIVERGED after '{System}': normal={Hash0:X16}, rollback={Hash1:X16}",
                                            sysNameK, checkNormal.Value.hashes[k], checkRollback.Value.hashes[k]);

                                        return $"Divergence at frame {checkFrame} after '{sysNameK}'";
                                    }
                                }
                                _log.Information("  All systems match on frame {Frame}", checkFrame);
                            }
                        }
                    }
                }

                return $"First divergence after '{systemName}': normal={normalHashes[i]:X16}, rollback={rollbackHashes[i]:X16}";
            }
        }

        return "Could not identify divergent system (all per-system hashes matched)";
    }

    /// <summary>
    /// Run simulation up to target frame with per-system hashing enabled.
    /// Optionally exports SimWorld state to a file for debugging.
    /// </summary>
    private (ulong[] hashes, string[] systemNames, Dictionary<string, ulong>? tableHashes, string? exportPath)? RunWithPerSystemHashing(
        string replayFilePath, int targetFrame, bool useRollbacks, string? exportLabel = null)
    {
        using var composition = new HeadlessComposition(replayFilePath);
        var gameSimulation = composition.GameSimulation;
        var rollbackManager = composition.RollbackManager;
        var replayManager = composition.ReplayManager;
        var derivedRunner = composition.DerivedSystemRunner;
        int playerCount = replayManager.PlayerCount;

        gameSimulation.SetSessionSeed((int)replayManager.Seed);
        gameSimulation.Reset();
        rollbackManager.Clear();

        // Pre-load all inputs
        var allInputs = new Dictionary<int, GameInput[]>();
        {
            Span<GameInput> tempInputs = stackalloc GameInput[playerCount];
            int preloadFrame = 0;
            while (!replayManager.ReplayEnded && preloadFrame <= targetFrame + 10)
            {
                if (!replayManager.TryGetReplayInputs(preloadFrame, tempInputs))
                    break;
                allInputs[preloadFrame] = tempInputs.ToArray();
                preloadFrame++;
            }
        }

        int currentFrame = 0;
        int rollbackInterval = 10;
        int rollbackDepth = 5;

        while (currentFrame <= targetFrame && allInputs.ContainsKey(currentFrame))
        {
            // Note: use (rollbackDepth - 1) to include case where targetFrame is the last frame of a rollback block
            if (useRollbacks && currentFrame > 0 && currentFrame % rollbackInterval == 0 &&
                currentFrame + rollbackDepth - 1 <= targetFrame && allInputs.ContainsKey(currentFrame + rollbackDepth - 1))
            {
                // Simulate with predictions then rollback (same logic as RunReplayWithRollbacks)
                rollbackManager.SaveSnapshot(currentFrame);
                int snapshotFrame = currentFrame;

                for (int f = currentFrame; f < currentFrame + rollbackDepth; f++)
                {
                    var correctInputs = allInputs[f];
                    rollbackManager.InputBuffer.StoreInput(f, 0, correctInputs[0]);
                    for (int p = 1; p < playerCount; p++)
                    {
                        rollbackManager.InputBuffer.StorePredictedInput(f, p, GameInput.Empty);
                    }
                    rollbackManager.SaveSnapshot(f);
                    gameSimulation.SetFrame(f);
                    gameSimulation.Tick();
                }

                rollbackManager.RestoreSnapshot(snapshotFrame, currentFrame + rollbackDepth - 1);
                derivedRunner.InvalidateAll();

                // Derived systems (PowerGrid, BuildingOccupancy) will be rebuilt in next tick's RebuildAll()

                for (int f = snapshotFrame; f < currentFrame + rollbackDepth; f++)
                {
                    var correctInputs = allInputs[f];
                    for (int p = 0; p < playerCount; p++)
                    {
                        rollbackManager.InputBuffer.StoreInput(f, p, correctInputs[p]);
                    }
                    rollbackManager.SaveSnapshot(f);

                    // Use per-system hashing for the target frame
                    if (f == targetFrame)
                    {
                        gameSimulation.SimulateTickWithHashing(f);
                    }
                    else
                    {
                        gameSimulation.SetFrame(f);
                        gameSimulation.Tick();
                    }
                }

                currentFrame += rollbackDepth;
            }
            else
            {
                var inputs = allInputs[currentFrame];
                for (int p = 0; p < playerCount; p++)
                {
                    rollbackManager.InputBuffer.StoreInput(currentFrame, p, inputs[p]);
                }
                rollbackManager.SaveSnapshot(currentFrame);

                // Use per-system hashing for the target frame
                if (currentFrame == targetFrame)
                {
                    gameSimulation.SimulateTickWithHashing(currentFrame);
                }
                else
                {
                    gameSimulation.SetFrame(currentFrame);
                    gameSimulation.Tick();
                }

                currentFrame++;
            }
        }

        // Get the per-system hashes for target frame
        if (gameSimulation.TryGetPerSystemHashesForFrame(targetFrame, out var hashes) && hashes != null)
        {
            var systemNames = gameSimulation.SystemNames.ToArray();
            // Also capture per-table hashes for BeginFrame divergence analysis
            var tableHashes = gameSimulation.GetPerTableHashes();

            // Export state if label provided - captures ACTUAL divergent state before disposal
            string? exportPath = null;
            if (exportLabel != null)
            {
                string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logsDir);
                string fileName = $"simworld_frame{targetFrame}_{exportLabel}.json";
                exportPath = Path.Combine(logsDir, fileName);

                var options = new System.Text.Json.JsonWriterOptions { Indented = true };
                using var stream = File.Create(exportPath);
                using var writer = new System.Text.Json.Utf8JsonWriter(stream, options);

                writer.WriteStartObject();
                writer.WriteNumber("frame", targetFrame);
                writer.WriteString("label", exportLabel);
                writer.WriteBoolean("useRollbacks", useRollbacks);
                writer.WritePropertyName("simWorld");
                gameSimulation.SimWorld.ExportDebugJson(writer);
                writer.WriteEndObject();
                writer.Flush();

                _log.Information("Exported SimWorld state to: {FilePath}", exportPath);
            }

            return (hashes, systemNames, tableHashes, exportPath);
        }

        return null;
    }

    private DeterminismResult CompareRuns(string replayFile, List<ReplayResult> runs)
    {
        if (runs.Count < 2)
        {
            return new DeterminismResult(
                ReplayFile: replayFile,
                Iterations: runs.Count,
                IsDeterministic: true,
                DivergenceFrame: null,
                Message: "Need at least 2 runs to compare"
            );
        }

        var baseline = runs[0];

        // Compare all runs against baseline
        for (int runIndex = 1; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];

            // Check all frames in baseline
            foreach (var (frame, baselineHash) in baseline.FrameHashes)
            {
                if (run.FrameHashes.TryGetValue(frame, out var runHash))
                {
                    if (runHash != baselineHash)
                    {
                        _log.Error("DESYNC at frame {Frame}: run 0 hash={Hash0:X16}, run {Run} hash={Hash1:X16}",
                            frame, baselineHash, runIndex, runHash);

                        return new DeterminismResult(
                            ReplayFile: replayFile,
                            Iterations: runs.Count,
                            IsDeterministic: false,
                            DivergenceFrame: frame,
                            Message: $"Hash mismatch at frame {frame}: run 0={baselineHash:X16}, run {runIndex}={runHash:X16}"
                        );
                    }
                }
            }
        }

        _log.Information("Determinism test PASSED: {Iterations} runs, {Frames} frames each",
            runs.Count, baseline.TotalFrames);

        return new DeterminismResult(
            ReplayFile: replayFile,
            Iterations: runs.Count,
            IsDeterministic: true,
            DivergenceFrame: null,
            Message: $"All {runs.Count} runs produced identical hashes for {baseline.TotalFrames} frames"
        );
    }

    /// <summary>
    /// Export SimWorld state to JSON file for debugging.
    /// </summary>
    private void ExportSimWorldState(string replayFilePath, int targetFrame, bool useRollbacks, string label)
    {
        using var composition = new HeadlessComposition(replayFilePath);
        var gameSimulation = composition.GameSimulation;
        var rollbackManager = composition.RollbackManager;
        var replayManager = composition.ReplayManager;
        var derivedRunner = composition.DerivedSystemRunner;
        int playerCount = replayManager.PlayerCount;

        gameSimulation.SetSessionSeed((int)replayManager.Seed);
        gameSimulation.Reset();
        rollbackManager.Clear();

        // Pre-load all inputs
        var allInputs = new Dictionary<int, GameInput[]>();
        {
            Span<GameInput> tempInputs = stackalloc GameInput[playerCount];
            int preloadFrame = 0;
            while (!replayManager.ReplayEnded && preloadFrame <= targetFrame + 10)
            {
                if (!replayManager.TryGetReplayInputs(preloadFrame, tempInputs))
                    break;
                allInputs[preloadFrame] = tempInputs.ToArray();
                preloadFrame++;
            }
        }

        int currentFrame = 0;
        int rollbackInterval = 10;
        int rollbackDepth = 5;

        while (currentFrame <= targetFrame && allInputs.ContainsKey(currentFrame))
        {
            if (useRollbacks && currentFrame > 0 && currentFrame % rollbackInterval == 0 &&
                currentFrame + rollbackDepth - 1 <= targetFrame && allInputs.ContainsKey(currentFrame + rollbackDepth - 1))
            {
                rollbackManager.SaveSnapshot(currentFrame);
                int snapshotFrame = currentFrame;

                for (int f = currentFrame; f < currentFrame + rollbackDepth; f++)
                {
                    var correctInputs = allInputs[f];
                    rollbackManager.InputBuffer.StoreInput(f, 0, correctInputs[0]);
                    for (int p = 1; p < playerCount; p++)
                    {
                        rollbackManager.InputBuffer.StorePredictedInput(f, p, GameInput.Empty);
                    }
                    rollbackManager.SaveSnapshot(f);
                    gameSimulation.SetFrame(f);
                    gameSimulation.Tick();
                }

                rollbackManager.RestoreSnapshot(snapshotFrame, currentFrame + rollbackDepth - 1);
                derivedRunner.InvalidateAll();

                // Derived systems (PowerGrid, BuildingOccupancy) will be rebuilt in next tick's RebuildAll()

                for (int f = snapshotFrame; f < currentFrame + rollbackDepth; f++)
                {
                    var correctedInputs = allInputs[f];
                    for (int p = 0; p < playerCount; p++)
                    {
                        rollbackManager.InputBuffer.StoreInput(f, p, correctedInputs[p]);
                    }
                    gameSimulation.SetFrame(f);
                    gameSimulation.Tick();
                }

                currentFrame += rollbackDepth;
            }
            else
            {
                var inputs = allInputs[currentFrame];
                for (int p = 0; p < playerCount; p++)
                {
                    rollbackManager.InputBuffer.StoreInput(currentFrame, p, inputs[p]);
                }
                rollbackManager.SaveSnapshot(currentFrame);
                gameSimulation.SetFrame(currentFrame);
                gameSimulation.Tick();
                currentFrame++;
            }
        }

        // Export to JSON
        string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        Directory.CreateDirectory(logsDir);
        string fileName = $"simworld_frame{targetFrame}_{label}.json";
        string filePath = Path.Combine(logsDir, fileName);

        var options = new System.Text.Json.JsonWriterOptions { Indented = true };
        using var stream = File.Create(filePath);
        using var writer = new System.Text.Json.Utf8JsonWriter(stream, options);

        writer.WriteStartObject();
        writer.WriteNumber("frame", targetFrame);
        writer.WriteString("label", label);
        writer.WriteBoolean("useRollbacks", useRollbacks);
        writer.WritePropertyName("simWorld");
        gameSimulation.SimWorld.ExportDebugJson(writer);
        writer.WriteEndObject();
        writer.Flush();

        _log.Information("Exported SimWorld state to: {FilePath}", filePath);
    }
}
