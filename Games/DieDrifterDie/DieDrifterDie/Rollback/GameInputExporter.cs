using System.Text.Json;
using Core;
using DerpTech.DesyncDetection;
using DieDrifterDie.Infrastructure.Rollback;
using SimTable;

namespace DieDrifterDie.Infrastructure.Rollback;

/// <summary>
/// Game-specific input exporter for desync debugging.
/// Exports all GameInput fields to JSON.
/// </summary>
public sealed class GameInputExporter : IInputExporter<GameInput>
{
    public void WriteInputToJson(Utf8JsonWriter writer, int playerId, in GameInput input)
    {
        writer.WriteNumber("playerId", playerId);
        // GameInput is empty in DieDrifterDie - no fields to export
    }
}
