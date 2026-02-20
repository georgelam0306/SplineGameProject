using System;
using System.Collections.Generic;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Topological sort for binding definitions using Kahn's algorithm.
/// A binding B depends on binding A if any of B's source paths matches A's target path.
/// </summary>
public static class ExpressionBindingGraph
{
    /// <summary>
    /// Returns sorted binding indices, or null if a cycle is detected.
    /// </summary>
    public static int[]? Toposort(ReadOnlySpan<ExpressionBindingDefinition> bindings)
    {
        int n = bindings.Length;
        if (n == 0) return Array.Empty<int>();
        if (n == 1) return new[] { 0 };

        // Build adjacency: targetIndex → list of downstream binding indices
        // inDegree[i] = number of bindings that i depends on
        Span<int> inDegree = stackalloc int[n];
        var adjacency = new List<int>[n];
        for (int i = 0; i < n; i++)
            adjacency[i] = new List<int>();

        // Build target lookup: path → binding index
        var targetMap = new Dictionary<ExpressionBindingPath, int>(n);
        for (int i = 0; i < n; i++)
            targetMap[bindings[i].Target] = i;

        // For each binding, check if any source matches another binding's target
        for (int i = 0; i < n; i++)
        {
            var sources = bindings[i].Sources;
            for (int s = 0; s < sources.Length; s++)
            {
                if (targetMap.TryGetValue(sources[s].Path, out int depIndex) && depIndex != i)
                {
                    adjacency[depIndex].Add(i);
                    inDegree[i]++;
                }
            }
        }

        // Kahn's algorithm
        var queue = new Queue<int>(n);
        for (int i = 0; i < n; i++)
        {
            if (inDegree[i] == 0)
                queue.Enqueue(i);
        }

        var sorted = new int[n];
        int count = 0;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            sorted[count++] = current;

            foreach (int downstream in adjacency[current])
            {
                inDegree[downstream]--;
                if (inDegree[downstream] == 0)
                    queue.Enqueue(downstream);
            }
        }

        // If not all nodes were visited, there's a cycle
        return count == n ? sorted : null;
    }
}
