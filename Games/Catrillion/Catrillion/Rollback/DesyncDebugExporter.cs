using System;
using System.IO;
using System.Text.Json;
using Serilog;
using Catrillion.Simulation;
using Catrillion.Simulation.Components;
using Core;
using DerpTech.Rollback;

namespace Catrillion.Rollback;

public static class DesyncDebugExporter
{
    private static readonly ILogger _log = Log.ForContext(typeof(DesyncDebugExporter));
    private const int RecentInputFrames = 20;

    /// <summary>
    /// Export desync debug data to a JSON file.
    /// </summary>
    /// <returns>The path to the exported file, or null if export failed.</returns>
    public static string? ExportDesyncState(
        SimWorld simWorld,
        RollbackManager<GameInput> rollbackManager,
        GameSimulation gameSimulation,
        DerivedSystemRunner derivedRunner,
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
            if (frameDiff > 0 && frameDiff <= RollbackManager<GameInput>.MaxRollbackFrames)
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

            // Diagnostic: Export raw values of key Fixed64 constants to detect JIT differences
            WriteConstantDiagnostics(writer);

            // Export recent inputs
            WriteRecentInputs(writer, rollbackManager, desyncFrame);

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

    private static void WriteFixed64(Utf8JsonWriter writer, string name, Fixed64 value)
    {
        writer.WriteString(name, value.ToDouble().ToString("F6"));
    }

    private static void WriteFixed64Vec2(Utf8JsonWriter writer, string name, Fixed64Vec2 value)
    {
        writer.WriteStartObject(name);
        writer.WriteString("x", value.X.ToDouble().ToString("F6"));
        writer.WriteString("y", value.Y.ToDouble().ToString("F6"));
        writer.WriteEndObject();
    }

    private static void WriteCombatUnitsTable(Utf8JsonWriter writer, CombatUnitRowTable table)
    {
        writer.WriteStartArray("combatUnits");
        int count = table.Count;
        for (int slot = 0; slot < count; slot++)
        {
            if (!table.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(MortalFlags.IsActive)) continue;

            writer.WriteStartObject();
            writer.WriteNumber("slot", slot);
            WriteFixed64Vec2(writer, "position", row.Position);
            WriteFixed64Vec2(writer, "velocity", row.Velocity);
            writer.WriteNumber("ownerPlayerId", row.OwnerPlayerId);
            writer.WriteNumber("typeId", (int)row.TypeId);
            writer.WriteNumber("groupId", row.GroupId);
            writer.WriteNumber("selectedByPlayerId", row.SelectedByPlayerId);
            writer.WriteNumber("currentOrder", (int)row.CurrentOrder);
            WriteFixed64Vec2(writer, "orderTarget", row.OrderTarget);
            writer.WriteNumber("flags", (int)row.Flags);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteZombiesTable(Utf8JsonWriter writer, ZombieRowTable table)
    {
        writer.WriteStartArray("zombies");
        int count = table.Count;
        for (int slot = 0; slot < count; slot++)
        {
            if (!table.TryGetRow(slot, out var row)) continue;
            if (row.Flags.IsDead()) continue;

            writer.WriteStartObject();
            writer.WriteNumber("slot", slot);
            WriteFixed64Vec2(writer, "position", row.Position);
            WriteFixed64Vec2(writer, "velocity", row.Velocity);
            WriteFixed64(writer, "facingAngle", row.FacingAngle);
            writer.WriteNumber("typeId", (int)row.TypeId);
            writer.WriteNumber("health", row.Health);
            writer.WriteNumber("maxHealth", row.MaxHealth);
            writer.WriteNumber("state", (int)row.State);
            writer.WriteNumber("zoneId", row.ZoneId);
            WriteFixed64Vec2(writer, "flow", row.Flow);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteProjectilesTable(Utf8JsonWriter writer, ProjectileRowTable table)
    {
        writer.WriteStartArray("projectiles");
        int count = table.Count;
        for (int slot = 0; slot < count; slot++)
        {
            if (!table.TryGetRow(slot, out var row)) continue;
            if (!row.Flags.HasFlag(ProjectileFlags.IsActive)) continue;

            writer.WriteStartObject();
            writer.WriteNumber("slot", slot);
            WriteFixed64Vec2(writer, "position", row.Position);
            WriteFixed64Vec2(writer, "velocity", row.Velocity);
            writer.WriteNumber("type", (int)row.Type);
            writer.WriteNumber("ownerPlayerId", row.OwnerPlayerId);
            writer.WriteNumber("damage", row.Damage);
            writer.WriteNumber("lifetimeFrames", row.LifetimeFrames);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteSimStateTable(Utf8JsonWriter writer, SimStateRowTable table)
    {
        writer.WriteStartArray("simState");
        int count = table.Count;
        for (int slot = 0; slot < count; slot++)
        {
            if (!table.TryGetRow(slot, out var row)) continue;

            writer.WriteStartObject();
            writer.WriteNumber("slot", slot);
            writer.WriteNumber("matchState", row.MatchState);
            writer.WriteNumber("currentFrame", row.CurrentFrame);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteGameRulesStateTable(Utf8JsonWriter writer, GameRulesStateRowTable table)
    {
        writer.WriteStartArray("gameRulesState");
        int count = table.Count;
        for (int slot = 0; slot < count; slot++)
        {
            if (!table.TryGetRow(slot, out var row)) continue;

            writer.WriteStartObject();
            writer.WriteNumber("slot", slot);
            writer.WriteNumber("matchState", (int)row.MatchState);
            writer.WriteNumber("spawnedUnitCount", row.SpawnedUnitCount);
            writer.WriteNumber("winningPlayerId", row.WinningPlayerId);
            writer.WriteNumber("frameMatchStarted", row.FrameMatchStarted);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteWaveStateTable(Utf8JsonWriter writer, WaveStateRowTable table)
    {
        writer.WriteStartArray("waveState");
        int count = table.Count;
        for (int slot = 0; slot < count; slot++)
        {
            if (!table.TryGetRow(slot, out var row)) continue;

            writer.WriteStartObject();
            writer.WriteNumber("slot", slot);
            writer.WriteNumber("currentDay", row.CurrentDay);
            writer.WriteNumber("currentHordeWave", row.CurrentHordeWave);
            writer.WriteNumber("currentMiniWave", row.CurrentMiniWave);
            writer.WriteNumber("activeWaveZombieCount", row.ActiveWaveZombieCount);
            writer.WriteBoolean("hordeActive", (row.Flags & WaveStateFlags.HordeActive) != 0);
            writer.WriteBoolean("miniWaveActive", (row.Flags & WaveStateFlags.MiniWaveActive) != 0);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteRecentInputs(Utf8JsonWriter writer, RollbackManager<GameInput> rollbackManager, int desyncFrame)
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

                writer.WriteStartObject();
                writer.WriteNumber("playerId", playerId);
                WriteFixed64Vec2(writer, "selectStart", input.SelectStart);
                WriteFixed64Vec2(writer, "selectEnd", input.SelectEnd);
                writer.WriteBoolean("isSelecting", input.IsSelecting);
                WriteFixed64Vec2(writer, "moveTarget", input.MoveTarget);
                writer.WriteBoolean("hasMoveCommand", input.HasMoveCommand);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteHashHistory(Utf8JsonWriter writer, RollbackManager<GameInput> rollbackManager, int desyncFrame)
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

    private static void WriteConstantDiagnostics(Utf8JsonWriter writer)
    {
        // Export raw values of key Fixed64 constants used in simulation
        // This helps detect if JIT compilation produces different values across processes
        writer.WriteStartObject("constantDiagnostics");

        // GridDensitySeparation constants (used for unit separation)
        writer.WriteString("SpreadScale_0.4f", $"{Fixed64.FromFloat(0.4f).Raw:X16}");
        writer.WriteString("MaxForce_3.0f", $"{Fixed64.FromFloat(3.0f).Raw:X16}");
        writer.WriteString("GradientScale_1.0f", $"{Fixed64.FromFloat(1.0f).Raw:X16}");

        // FlowFieldService constants
        writer.WriteString("DiagonalCost_1.414f", $"{Fixed64.FromFloat(1.414f).Raw:X16}");

        // EnemyFlowMovementSystem constants
        writer.WriteString("EnemySpeed_1.5f", $"{Fixed64.FromFloat(1.5f).Raw:X16}");

        // Fixed64 core values
        writer.WriteString("Fixed64_One", $"{Fixed64.OneValue.Raw:X16}");
        writer.WriteString("Fixed64_Zero", $"{Fixed64.Zero.Raw:X16}");

        // SIMD diagnostic - Vector256 count for detecting platform differences
        writer.WriteNumber("Vector256_IntCount", System.Runtime.Intrinsics.Vector256<int>.Count);

        writer.WriteEndObject();
    }
}
