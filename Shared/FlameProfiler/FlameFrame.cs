using System;
using System.Runtime.CompilerServices;

namespace FlameProfiler;

/// <summary>
/// Contains all profiling data for a single frame.
/// Manages a pool of FlameNodes and tracks the current call stack.
/// </summary>
public sealed class FlameFrame
{
    /// <summary>Maximum call stack depth supported.</summary>
    public const int MaxDepth = 32;

    /// <summary>Maximum number of scope entries per frame.</summary>
    public const int MaxNodes = 1024;

    /// <summary>Pool of nodes for this frame.</summary>
    public readonly FlameNode[] Nodes;

    /// <summary>Number of active nodes in the Nodes array.</summary>
    public int NodeCount;

    /// <summary>Current depth in the call stack (0 when no scopes are active).</summary>
    public int CurrentDepth;

    /// <summary>
    /// Stack path from root to current node. Each entry is an index into Nodes.
    /// StackPath[0..CurrentDepth-1] contains the active path.
    /// </summary>
    public readonly int[] StackPath;

    /// <summary>Timestamp when this frame started (Stopwatch ticks).</summary>
    public long FrameStartTicks;

    /// <summary>Timestamp when this frame ended (Stopwatch ticks).</summary>
    public long FrameEndTicks;

    /// <summary>Total frame time in ticks.</summary>
    public long FrameTotalTicks => FrameEndTicks - FrameStartTicks;

    /// <summary>Frame number when this was recorded.</summary>
    public int FrameNumber;

    /// <summary>Whether this frame has valid data.</summary>
    public bool IsValid;

    /// <summary>
    /// Creates a new FlameFrame with pre-allocated node pool.
    /// </summary>
    public FlameFrame()
    {
        Nodes = new FlameNode[MaxNodes];
        StackPath = new int[MaxDepth];

        // Initialize all nodes
        for (int i = 0; i < MaxNodes; i++)
        {
            Nodes[i].Reset();
        }
    }

    /// <summary>
    /// Resets this frame for reuse. Called at the start of a new frame.
    /// </summary>
    public void Reset()
    {
        NodeCount = 0;
        CurrentDepth = 0;
        FrameStartTicks = 0;
        FrameEndTicks = 0;
        FrameNumber = 0;
        IsValid = false;

        // Clear stack path
        Array.Clear(StackPath, 0, MaxDepth);
    }

    /// <summary>
    /// Begins a new scope. Returns the node index for use with EndScope.
    /// </summary>
    /// <param name="scopeId">The scope identifier.</param>
    /// <param name="startTicks">Current timestamp.</param>
    /// <returns>Node index, or -1 if capacity exceeded.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int BeginScope(int scopeId, long startTicks)
    {
        if (NodeCount >= MaxNodes || CurrentDepth >= MaxDepth)
        {
            return -1; // Capacity exceeded
        }

        int nodeIndex = NodeCount++;
        ref FlameNode node = ref Nodes[nodeIndex];

        node.ScopeId = scopeId;
        node.StartTicks = startTicks;
        node.EndTicks = 0;
        node.SelfTicks = 0;
        node.Depth = CurrentDepth;
        node.FirstChildIndex = -1;
        node.NextSiblingIndex = -1;

        // Link to parent
        if (CurrentDepth > 0)
        {
            int parentIndex = StackPath[CurrentDepth - 1];
            node.ParentIndex = parentIndex;

            // Add as child of parent
            ref FlameNode parent = ref Nodes[parentIndex];
            if (parent.FirstChildIndex == -1)
            {
                parent.FirstChildIndex = nodeIndex;
            }
            else
            {
                // Find last sibling and link
                int siblingIndex = parent.FirstChildIndex;
                while (Nodes[siblingIndex].NextSiblingIndex != -1)
                {
                    siblingIndex = Nodes[siblingIndex].NextSiblingIndex;
                }
                Nodes[siblingIndex].NextSiblingIndex = nodeIndex;
            }
        }
        else
        {
            node.ParentIndex = -1;
        }

        // Push onto stack
        StackPath[CurrentDepth] = nodeIndex;
        CurrentDepth++;

        return nodeIndex;
    }

    /// <summary>
    /// Ends a scope started by BeginScope.
    /// </summary>
    /// <param name="nodeIndex">The node index returned by BeginScope.</param>
    /// <param name="endTicks">Current timestamp.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EndScope(int nodeIndex, long endTicks)
    {
        if (nodeIndex < 0 || nodeIndex >= NodeCount)
        {
            return;
        }

        ref FlameNode node = ref Nodes[nodeIndex];
        node.EndTicks = endTicks;

        // Compute self time: total time minus children's time
        long childrenTicks = 0;
        int childIndex = node.FirstChildIndex;
        while (childIndex != -1)
        {
            childrenTicks += Nodes[childIndex].TotalTicks;
            childIndex = Nodes[childIndex].NextSiblingIndex;
        }
        node.SelfTicks = node.TotalTicks - childrenTicks;

        // Pop from stack
        if (CurrentDepth > 0)
        {
            CurrentDepth--;
        }
    }

    /// <summary>
    /// Iterates all root nodes (nodes at depth 0).
    /// </summary>
    public RootEnumerator GetRoots() => new RootEnumerator(this);

    /// <summary>
    /// Iterates children of a given node.
    /// </summary>
    public ChildEnumerator GetChildren(int nodeIndex) => new ChildEnumerator(this, nodeIndex);

    /// <summary>
    /// Enumerator for root nodes.
    /// </summary>
    public ref struct RootEnumerator
    {
        private readonly FlameFrame _frame;
        private int _index;

        internal RootEnumerator(FlameFrame frame)
        {
            _frame = frame;
            _index = -1;
        }

        public bool MoveNext()
        {
            while (++_index < _frame.NodeCount)
            {
                if (_frame.Nodes[_index].ParentIndex == -1)
                {
                    return true;
                }
            }
            return false;
        }

        public ref readonly FlameNode Current => ref _frame.Nodes[_index];
        public int CurrentIndex => _index;
        public RootEnumerator GetEnumerator() => this;
    }

    /// <summary>
    /// Enumerator for child nodes.
    /// </summary>
    public ref struct ChildEnumerator
    {
        private readonly FlameFrame _frame;
        private int _index;
        private readonly int _parentIndex;
        private bool _started;

        internal ChildEnumerator(FlameFrame frame, int parentIndex)
        {
            _frame = frame;
            _parentIndex = parentIndex;
            _index = -1;
            _started = false;
        }

        public bool MoveNext()
        {
            if (!_started)
            {
                _started = true;
                if (_parentIndex >= 0 && _parentIndex < _frame.NodeCount)
                {
                    _index = _frame.Nodes[_parentIndex].FirstChildIndex;
                }
                return _index != -1;
            }

            if (_index != -1)
            {
                _index = _frame.Nodes[_index].NextSiblingIndex;
            }
            return _index != -1;
        }

        public ref readonly FlameNode Current => ref _frame.Nodes[_index];
        public int CurrentIndex => _index;
        public ChildEnumerator GetEnumerator() => this;
    }
}
