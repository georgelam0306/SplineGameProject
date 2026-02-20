using System;
using Property;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Reads properties, evaluates expressions, writes results.
/// Decoupled from ECS via <see cref="IBindingPropertyReader"/> / <see cref="IBindingPropertyWriter"/>.
/// </summary>
public interface IBindingPropertyReader
{
    PropertyValue ReadProperty(in ExpressionBindingPath path);
}

public interface IBindingPropertyWriter
{
    void WriteProperty(in ExpressionBindingPath path, in PropertyValue value);
}

/// <summary>
/// Holds pre-sorted binding definitions and evaluates all bindings in dependency order.
/// Called during Flush() to recompute derived values.
/// </summary>
public sealed class ExpressionBindingEvaluator
{
    private ExpressionBindingDefinition[] _sorted = Array.Empty<ExpressionBindingDefinition>();

    /// <summary>
    /// Sets the binding definitions. Must be called whenever bindings change.
    /// Compiles all definitions and topologically sorts them.
    /// Returns false if a cycle is detected.
    /// </summary>
    public bool SetBindings(ExpressionBindingDefinition[] definitions)
    {
        if (definitions.Length == 0)
        {
            _sorted = Array.Empty<ExpressionBindingDefinition>();
            return true;
        }

        // Compile all
        for (int i = 0; i < definitions.Length; i++)
            definitions[i].Compile();

        // Toposort
        int[]? order = ExpressionBindingGraph.Toposort(definitions);
        if (order == null)
        {
            _sorted = Array.Empty<ExpressionBindingDefinition>();
            return false; // cycle detected
        }

        // Reorder into sorted array
        _sorted = new ExpressionBindingDefinition[definitions.Length];
        for (int i = 0; i < order.Length; i++)
            _sorted[i] = definitions[order[i]];

        return true;
    }

    /// <summary>
    /// Evaluates all bindings in topological order, reading sources and writing targets.
    /// No commands are emitted — targets are written directly via <see cref="IBindingPropertyWriter"/>.
    /// </summary>
    public void EvaluateAll(IBindingPropertyReader reader, IBindingPropertyWriter writer)
    {
        // Pre-allocate source buffer outside the loop (max 8 on stack, else heap)
        Span<PropertyValue> stackBuf = stackalloc PropertyValue[8];

        for (int i = 0; i < _sorted.Length; i++)
        {
            var binding = _sorted[i];
            if (!binding.IsValid) continue;

            // Read source values
            int sourceCount = binding.Sources.Length;
            Span<PropertyValue> sourceValues = sourceCount <= 8
                ? stackBuf.Slice(0, sourceCount)
                : new PropertyValue[sourceCount];

            for (int s = 0; s < sourceCount; s++)
                sourceValues[s] = reader.ReadProperty(binding.Sources[s].Path);

            // Evaluate
            var result = ExpressionEvaluator.Evaluate(binding.FlatExpression, sourceValues);

            // Write target directly (no commands)
            writer.WriteProperty(binding.Target, result);
        }
    }

    public int BindingCount => _sorted.Length;
}
