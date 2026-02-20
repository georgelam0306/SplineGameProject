using System.Text.Json;
using Serilog;
using DerpTech.Rollback;

namespace DerpTech.DesyncDetection;

/// <summary>
/// Exports desync debug data to JSON files for analysis.
/// Generic over input type to support game-specific input fields.
/// </summary>
public static class DesyncDebugExporter<TInput>
    where TInput : unmanaged, IGameInput<TInput>, IEquatable<TInput>
{
    private static readonly ILogger _log = Log.ForContext(typeof(DesyncDebugExporter<TInput>));
    private const int RecentInputFrames = 20;

    /// <summary>
    /// Export desync debug data to a JSON file.
    /// </summary>
    /// <param name="simWorld">The simulation world (for state export).</param>
    /// <param name="rollbackManager">The rollback manager (for snapshots and input history).</param>
    /// <param name="gameSimulation">The game simulation (for per-system hashes).</param>
    /// <param name="derivedRunner">The derived system runner (for invalidation after restore).</param>
    /// <param name="inputExporter">Optional game-specific input exporter. If null, inputs are not exported in detail.</param>
    /// <param name="desyncFrame">The frame where desync was detected.</param>
    /// <param name="currentFrame">The current simulation frame.</param>
    /// <param name="localHash">The local state hash at desync frame.</param>
    /// <param name="remoteHash">The remote state hash at desync frame.</param>
    /// <param name="localPlayerId">The local player ID.</param>
    /// <returns>The path to the exported file, or null if export failed.</returns>
    public static string? ExportDesyncState(
        IDesyncExportable simWorld,
        RollbackManager<TInput> rollbackManager,
        IGameSimulation gameSimulation,
        IDerivedSystemRunner derivedRunner,
        IInputExporter<TInput>? inputExporter,
        int desyncFrame,
        int currentFrame,
        ulong localHash,
        ulong remoteHash,
        byte localPlayerId)
    {
        string? filePath = null;
        try
        {
            // Log immediately for debugging
            _log.Information("Player {LocalPlayerId}: desyncFrame={DesyncFrame}, currentFrame={CurrentFrame}",
                localPlayerId, desyncFrame, currentFrame);

            // Try to restore snapshot to get exact state at desync frame
            bool restoredSnapshot = false;
            int frameDiff = currentFrame - desyncFrame;
            if (frameDiff > 0 && frameDiff <= RollbackManager<TInput>.MaxRollbackFrames)
            {
                try
                {
                    rollbackManager.RestoreSnapshot(desyncFrame, currentFrame);
                    derivedRunner.InvalidateAll();
                    restoredSnapshot = true;
                    _log.Information("Restored snapshot for frame {DesyncFrame}", desyncFrame);
                }
                catch (Exception restoreEx)
                {
                    _log.Warning("Could not restore snapshot: {Message}", restoreEx.Message);
                }
            }

            string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logsDir);

            string fileName = $"desync_frame{desyncFrame}_player{localPlayerId}.json";
            filePath = Path.Combine(logsDir, fileName);

            var options = new JsonWriterOptions { Indented = true };
            using var stream = File.Create(filePath);
            using var writer = new Utf8JsonWriter(stream, options);

            writer.WriteStartObject();

            // Metadata
            writer.WriteNumber("desyncFrame", desyncFrame);
            writer.WriteNumber("currentFrame", currentFrame);
            writer.WriteBoolean("snapshotRestored", restoredSnapshot);
            writer.WriteString("localHash", $"{localHash:X16}");
            writer.WriteString("remoteHash", $"{remoteHash:X16}");
            writer.WriteNumber("localPlayerId", localPlayerId);
            writer.WriteNumber("sessionSeed", gameSimulation.SessionSeed);
            writer.WriteString("timestamp", DateTime.UtcNow.ToString("O"));

            // Export full SimWorld state (all tables with hashes and data)
            writer.WritePropertyName("simWorld");
            simWorld.ExportDebugJson(writer);

            // Per-system hashes for the actual desync frame (not current frame)
            var systemNames = gameSimulation.SystemNames;
            if (!systemNames.IsEmpty && gameSimulation.TryGetPerSystemHashesForFrame(desyncFrame, out var perSystemHashes) && perSystemHashes != null)
            {
                writer.WriteNumber("perSystemHashesFrame", desyncFrame);
                writer.WriteStartArray("perSystemHashes");
                int count = Math.Min(systemNames.Length, perSystemHashes.Length);
                for (int i = 0; i < count; i++)
                {
                    writer.WriteStartObject();
                    writer.WriteString("system", systemNames[i]);
                    writer.WriteString("hashAfter", $"{perSystemHashes[i]:X16}");
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            else if (!systemNames.IsEmpty)
            {
                // Fallback: no stored hashes for this frame
                writer.WriteString("perSystemHashesError", $"No per-system hashes stored for frame {desyncFrame}");
            }

            // Export per-system hashes for multiple recent frames to help identify divergence point
            if (!systemNames.IsEmpty)
            {
                writer.WriteStartArray("recentPerSystemHashes");
                // Export last 10 frames of per-system hashes
                for (int frame = desyncFrame - 9; frame <= desyncFrame; frame++)
                {
                    if (frame < 0) continue;
                    if (gameSimulation.TryGetPerSystemHashesForFrame(frame, out var frameHashes) && frameHashes != null)
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("frame", frame);
                        writer.WriteStartArray("systems");
                        int cnt = Math.Min(systemNames.Length, frameHashes.Length);
                        for (int i = 0; i < cnt; i++)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("system", systemNames[i]);
                            writer.WriteString("hash", $"{frameHashes[i]:X16}");
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    }
                }
                writer.WriteEndArray();
            }

            // Export recent inputs (if exporter provided)
            WriteRecentInputs(writer, rollbackManager, inputExporter, desyncFrame);

            // Export hash history to identify when divergence started
            WriteHashHistory(writer, rollbackManager, desyncFrame);

            writer.WriteEndObject();
            writer.Flush();

            _log.Information("Desync debug data exported to: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to export desync debug data");
        }

        return filePath;
    }

    private static void WriteRecentInputs(
        Utf8JsonWriter writer,
        RollbackManager<TInput> rollbackManager,
        IInputExporter<TInput>? inputExporter,
        int desyncFrame)
    {
        writer.WriteStartArray("recentInputs");

        int startFrame = Math.Max(0, desyncFrame - RecentInputFrames);
        var inputBuffer = rollbackManager.InputBuffer;
        int playerCount = rollbackManager.PlayerCount;

        for (int frame = startFrame; frame <= desyncFrame; frame++)
        {
            writer.WriteStartObject();
            writer.WriteNumber("frame", frame);
            writer.WriteStartArray("playerInputs");

            for (int playerId = 0; playerId < playerCount; playerId++)
            {
                ref readonly var input = ref inputBuffer.GetInput(frame, playerId);

                if (inputExporter != null)
                {
                    // Game-specific input export
                    writer.WriteStartObject();
                    inputExporter.WriteInputToJson(writer, playerId, in input);
                    writer.WriteEndObject();
                }
                else
                {
                    // Minimal export - just mark that input exists
                    writer.WriteStartObject();
                    writer.WriteNumber("playerId", playerId);
                    writer.WriteBoolean("isEmpty", input.IsEmpty);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteHashHistory(Utf8JsonWriter writer, RollbackManager<TInput> rollbackManager, int desyncFrame)
    {
        const int HashHistoryFrames = 60;
        Span<(int frame, ulong hash)> hashHistory = stackalloc (int, ulong)[HashHistoryFrames];
        rollbackManager.GetHashHistory(desyncFrame, HashHistoryFrames, hashHistory, out int written);

        writer.WriteStartArray("hashHistory");
        for (int i = 0; i < written; i++)
        {
            writer.WriteStartObject();
            writer.WriteNumber("frame", hashHistory[i].frame);
            writer.WriteString("hash", $"{hashHistory[i].hash:X16}");
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
