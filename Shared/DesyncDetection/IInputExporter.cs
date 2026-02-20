using System.Text.Json;
using DerpTech.Rollback;

namespace DerpTech.DesyncDetection;

/// <summary>
/// Interface for exporting game-specific input fields to JSON.
/// Each game implements this to export its custom input fields (building placement, garrison, etc.).
/// TInput must be blittable/memcopy-safe (matches IGameInput constraint).
/// </summary>
public interface IInputExporter<TInput>
    where TInput : unmanaged, IGameInput<TInput>, IEquatable<TInput>
{
    /// <summary>
    /// Writes a single player's input to JSON.
    /// </summary>
    void WriteInputToJson(Utf8JsonWriter writer, int playerId, in TInput input);
}
