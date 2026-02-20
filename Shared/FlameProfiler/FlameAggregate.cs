using System;
using System.Collections.Generic;

namespace FlameProfiler;

/// <summary>
/// Represents an aggregated node in the flame graph, combining data from multiple frames.
/// </summary>
public sealed class FlameAggregateNode
{
    /// <summary>Scope ID for this node.</summary>
    public int ScopeId;

    /// <summary>Scope name.</summary>
    public string Name = string.Empty;

    /// <summary>Depth in the call stack (0 = root).</summary>
    public int Depth;

    /// <summary>Average total time in milliseconds.</summary>
    public double TotalMs;

    /// <summary>Average self time in milliseconds (excluding children).</summary>
    public double SelfMs;

    /// <summary>Minimum total time across frames.</summary>
    public double MinTotalMs;

    /// <summary>Maximum total time across frames.</summary>
    public double MaxTotalMs;

    /// <summary>Number of frames this node appeared in.</summary>
    public int SampleCount;

    /// <summary>Percentage of frame time this node takes.</summary>
    public double PercentOfFrame;

    /// <summary>Percentage of parent time this node takes.</summary>
    public double PercentOfParent;

    /// <summary>Parent node (null for roots).</summary>
    public FlameAggregateNode? Parent;

    /// <summary>Child nodes.</summary>
    public List<FlameAggregateNode> Children = new();

    /// <summary>
    /// Left position in normalized coordinates [0, 1] for rendering.
    /// Represents the start position within the parent's span.
    /// </summary>
    public double NormalizedLeft;

    /// <summary>
    /// Width in normalized coordinates [0, 1] for rendering.
    /// Represents the proportion of parent's span this node takes.
    /// </summary>
    public double NormalizedWidth;

    /// <summary>
    /// Stable hash of the call-path (root-to-this-node) for selection/zoom persistence across rebuilds.
    /// </summary>
    public ulong PathHash;

    /// <summary>
    /// Resets this node for reuse.
    /// </summary>
    public void Reset()
    {
        ScopeId = 0;
        Name = string.Empty;
        Depth = 0;
        TotalMs = 0;
        SelfMs = 0;
        MinTotalMs = double.MaxValue;
        MaxTotalMs = 0;
        SampleCount = 0;
        PercentOfFrame = 0;
        PercentOfParent = 0;
        Parent = null;
        Children.Clear();
        NormalizedLeft = 0;
        NormalizedWidth = 0;
        PathHash = 0;
    }
}

/// <summary>
/// Aggregates multiple frames into a single tree for flame graph visualization.
/// </summary>
public sealed class FlameAggregate
{
    /// <summary>Root nodes of the aggregated flame graph.</summary>
    public readonly List<FlameAggregateNode> Roots = new();

    /// <summary>All nodes in a flat list for iteration.</summary>
    public readonly List<FlameAggregateNode> AllNodes = new();

    /// <summary>Number of frames included in this aggregate.</summary>
    public int FrameCount;

    /// <summary>Average total frame time in milliseconds.</summary>
    public double AverageFrameMs;

    /// <summary>Maximum depth in the aggregate tree.</summary>
    public int MaxDepth;

    // Node pool for reuse
    private readonly List<FlameAggregateNode> _nodePool = new();
    private int _poolIndex;

    /// <summary>
    /// Builds an aggregate from the most recent N frames.
    /// </summary>
    /// <param name="service">The flame profiler service.</param>
    /// <param name="frameCount">Number of frames to aggregate.</param>
    public void Build(FlameProfilerService service, int frameCount)
    {
        // Reset
        Roots.Clear();
        AllNodes.Clear();
        _poolIndex = 0;
        FrameCount = 0;
        AverageFrameMs = 0;
        MaxDepth = 0;

        if (service.ValidFrameCount == 0)
        {
            return;
        }

        // Clamp to available frames
        frameCount = Math.Min(frameCount, service.ValidFrameCount);

        // Temporary structure to aggregate by call path
        // Key: path from root as scope IDs, Value: aggregated timing
        var pathToNode = new Dictionary<string, FlameAggregateNode>();

        double totalFrameMs = 0;

        // Process each frame
        for (int age = 0; age < frameCount; age++)
        {
            FlameFrame? frame = service.GetFrame(age);
            if (frame == null || !frame.IsValid)
            {
                continue;
            }

            FrameCount++;
            double frameMs = FlameProfilerService.TicksToMilliseconds(frame.FrameTotalTicks);
            totalFrameMs += frameMs;

            // Process each node in the frame
            for (int i = 0; i < frame.NodeCount; i++)
            {
                ref readonly FlameNode node = ref frame.Nodes[i];

                // Build path key
                string pathKey = BuildPathKey(frame, i);

                if (!pathToNode.TryGetValue(pathKey, out FlameAggregateNode? aggNode))
                {
                    aggNode = GetOrCreateNode();
                    aggNode.ScopeId = node.ScopeId;
                    aggNode.Name = service.GetScopeName(node.ScopeId);
                    aggNode.Depth = node.Depth;
                    pathToNode[pathKey] = aggNode;
                    AllNodes.Add(aggNode);

                    if (node.Depth > MaxDepth)
                    {
                        MaxDepth = node.Depth;
                    }
                }

                double totalMs = FlameProfilerService.TicksToMilliseconds(node.TotalTicks);
                double selfMs = FlameProfilerService.TicksToMilliseconds(node.SelfTicks);

                aggNode.TotalMs += totalMs;
                aggNode.SelfMs += selfMs;
                aggNode.SampleCount++;

                if (totalMs < aggNode.MinTotalMs) aggNode.MinTotalMs = totalMs;
                if (totalMs > aggNode.MaxTotalMs) aggNode.MaxTotalMs = totalMs;
            }
        }

        if (FrameCount == 0)
        {
            return;
        }

        AverageFrameMs = totalFrameMs / FrameCount;

        // Average the timings and compute percentages
        foreach (FlameAggregateNode node in AllNodes)
        {
            if (node.SampleCount > 0)
            {
                node.TotalMs /= node.SampleCount;
                node.SelfMs /= node.SampleCount;
            }

            if (AverageFrameMs > 0)
            {
                node.PercentOfFrame = (node.TotalMs / AverageFrameMs) * 100.0;
            }
        }

        // Rebuild the tree structure
        RebuildTree(pathToNode);

        // Compute normalized positions for rendering
        ComputeNormalizedPositions();
    }

    private string BuildPathKey(FlameFrame frame, int nodeIndex)
    {
        // Build a path from root to this node using scope IDs
        Span<int> path = stackalloc int[FlameFrame.MaxDepth];
        int depth = 0;

        int current = nodeIndex;
        while (current != -1 && depth < FlameFrame.MaxDepth)
        {
            path[depth++] = frame.Nodes[current].ScopeId;
            current = frame.Nodes[current].ParentIndex;
        }

        // Reverse to get root-to-leaf order
        // Build string key
        Span<char> buffer = stackalloc char[depth * 8];
        int pos = 0;

        for (int i = depth - 1; i >= 0; i--)
        {
            if (pos > 0)
            {
                buffer[pos++] = '/';
            }

            int scopeId = path[i];
            // Simple int to string in buffer
            if (scopeId == 0)
            {
                buffer[pos++] = '0';
            }
            else
            {
                int start = pos;
                int temp = scopeId;
                while (temp > 0)
                {
                    buffer[pos++] = (char)('0' + (temp % 10));
                    temp /= 10;
                }
                // Reverse the digits
                int end = pos - 1;
                while (start < end)
                {
                    (buffer[start], buffer[end]) = (buffer[end], buffer[start]);
                    start++;
                    end--;
                }
            }
        }

        return new string(buffer[..pos]);
    }

    private void RebuildTree(Dictionary<string, FlameAggregateNode> pathToNode)
    {
        // Clear parent/child relationships
        foreach (FlameAggregateNode node in AllNodes)
        {
            node.Parent = null;
            node.Children.Clear();
        }
        Roots.Clear();

        // Build parent-child relationships based on path keys
        foreach (var kvp in pathToNode)
        {
            string path = kvp.Key;
            FlameAggregateNode node = kvp.Value;
            node.PathHash = ComputePathHash(path);

            // Find parent path (everything before last '/')
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash > 0)
            {
                string parentPath = path.Substring(0, lastSlash);
                if (pathToNode.TryGetValue(parentPath, out FlameAggregateNode? parent))
                {
                    node.Parent = parent;
                    parent.Children.Add(node);

                    // Compute percent of parent
                    if (parent.TotalMs > 0)
                    {
                        node.PercentOfParent = (node.TotalMs / parent.TotalMs) * 100.0;
                    }
                }
            }
            else
            {
                // No parent = root
                Roots.Add(node);
            }
        }

        // Sort children by total time (descending) for consistent rendering
        foreach (FlameAggregateNode node in AllNodes)
        {
            node.Children.Sort((a, b) => b.TotalMs.CompareTo(a.TotalMs));
        }
        Roots.Sort((a, b) => b.TotalMs.CompareTo(a.TotalMs));
    }

    private static ulong ComputePathHash(string path)
    {
        const ulong fnvOffset = 14695981039346656037ul;
        const ulong fnvPrime = 1099511628211ul;

        ulong hash = fnvOffset;
        for (int i = 0; i < path.Length; i++)
        {
            hash ^= path[i];
            hash *= fnvPrime;
        }
        return hash;
    }

    private void ComputeNormalizedPositions()
    {
        // Assign positions to roots
        double currentLeft = 0;
        foreach (FlameAggregateNode root in Roots)
        {
            root.NormalizedWidth = AverageFrameMs > 0 ? root.TotalMs / AverageFrameMs : 0;
            root.NormalizedLeft = currentLeft;
            currentLeft += root.NormalizedWidth;

            // Recursively assign to children
            ComputeChildPositions(root);
        }
    }

    private void ComputeChildPositions(FlameAggregateNode parent)
    {
        if (parent.Children.Count == 0 || parent.TotalMs <= 0)
        {
            return;
        }

        double childLeft = parent.NormalizedLeft;

        foreach (FlameAggregateNode child in parent.Children)
        {
            child.NormalizedWidth = (child.TotalMs / parent.TotalMs) * parent.NormalizedWidth;
            child.NormalizedLeft = childLeft;
            childLeft += child.NormalizedWidth;

            ComputeChildPositions(child);
        }
    }

    private FlameAggregateNode GetOrCreateNode()
    {
        if (_poolIndex < _nodePool.Count)
        {
            FlameAggregateNode node = _nodePool[_poolIndex++];
            node.Reset();
            return node;
        }

        FlameAggregateNode newNode = new();
        _nodePool.Add(newNode);
        _poolIndex++;
        return newNode;
    }

    /// <summary>
    /// Finds a node at the given screen position.
    /// </summary>
    /// <param name="normalizedX">X position in [0, 1] range.</param>
    /// <param name="depth">Depth level (0 = roots).</param>
    /// <returns>The node at that position, or null.</returns>
    public FlameAggregateNode? FindNodeAt(double normalizedX, int depth)
    {
        foreach (FlameAggregateNode node in AllNodes)
        {
            if (node.Depth == depth &&
                normalizedX >= node.NormalizedLeft &&
                normalizedX < node.NormalizedLeft + node.NormalizedWidth)
            {
                return node;
            }
        }
        return null;
    }

    public FlameAggregateNode? FindByPathHash(ulong pathHash)
    {
        for (int i = 0; i < AllNodes.Count; i++)
        {
            FlameAggregateNode node = AllNodes[i];
            if (node.PathHash == pathHash)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// Iterates all nodes at a given depth.
    /// </summary>
    public IEnumerable<FlameAggregateNode> GetNodesAtDepth(int depth)
    {
        foreach (FlameAggregateNode node in AllNodes)
        {
            if (node.Depth == depth)
            {
                yield return node;
            }
        }
    }
}
