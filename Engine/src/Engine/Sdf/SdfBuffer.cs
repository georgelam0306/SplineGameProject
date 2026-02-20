using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Serilog;
using DerpLib.Memory;

namespace DerpLib.Sdf;

/// <summary>
/// Collects SDF commands during a frame and provides typed GPU storage.
/// Uses StorageBuffer&lt;SdfCommand&gt; for efficient upload.
/// Supports a warp stack for coordinate distortion effects.
/// Supports lattice deformers (4x4 FFD grids).
/// Uses segmented point buffer for polylines (arbitrary length, memory efficient).
/// </summary>
public sealed class SdfBuffer : IDisposable
{
    private const int MaxWarpStackDepth = 256;
    private const int MaxModifierStackDepth = 256;
    private const int MaxGroupStackDepth = 8;
    private const int MaxMorphStackDepth = 8;
    private const int MaxLattices = 64;
    private const int MaxPolylineHeaders = 2048;
    private const int MaxPolylinePoints = 16384; // 16K points total
    private const int MaxGradientStops = 65536; // Up to 8 stops per command at 8K commands.
    private const int TileSize = 8;
    private const int MaxTileIndices = 2097152; // 2M indices

    private readonly ILogger _log;
    private readonly MemoryAllocator _allocator;
    private readonly int _maxCommands;

    private readonly SdfCommand[] _commands;
    private int _count;
    private bool _hasGlyphCommands;
    private bool _hasSecondaryGlyphCommands;

    // Warp stack (persistent per-frame nodes; shapes capture a warp chain head index at submission time)
    // Warp node indices are 1-based, 0 means "no warp".
    private readonly SdfWarpNode[] _warpNodes;
    private readonly int _maxWarpNodes;
    private int _warpNodeCount;
    private uint _currentWarpHead;
    private int _warpStackDepth;

    // Modifier stack (persistent per-frame nodes; shapes capture a modifier chain head index at submission time)
    // Modifier node indices are 1-based, 0 means "no modifiers".
    private readonly SdfModifierNode[] _modifierNodes;
    private readonly int _maxModifierNodes;
    private int _modifierNodeCount;
    private uint _currentModifierHead;
    private int _modifierStackDepth;

    // Clip rect stack
    private const int MaxClipStackDepth = 64;
    private readonly Vector4[] _clipStack = new Vector4[MaxClipStackDepth];
    private int _clipStackDepth;

    // Boolean group stack
    private readonly GroupInfo[] _groupStack = new GroupInfo[MaxGroupStackDepth];
    private int _groupStackDepth;

    private readonly struct GroupInfo
    {
        public readonly int StartIndex;
        public readonly SdfBooleanOp Op;
        public readonly float Smoothness;

        public GroupInfo(int startIndex, SdfBooleanOp op, float smoothness)
        {
            StartIndex = startIndex;
            Op = op;
            Smoothness = smoothness;
        }
    }

    // Morph group stack
    private readonly MorphInfo[] _morphStack = new MorphInfo[MaxMorphStackDepth];
    private int _morphStackDepth;

    private readonly struct MorphInfo
    {
        public readonly int StartIndex;
        public readonly float MorphFactor;

        public MorphInfo(int startIndex, float morphFactor)
        {
            StartIndex = startIndex;
            MorphFactor = morphFactor;
        }
    }

    // Alpha mask stack
    private const int MaxMaskStackDepth = 8;
    private int _maskStackDepth;

    // Text group state (for grouping glyphs with shared warp/effects)
    private const int MaxTextGroupStackDepth = 8;
    private readonly TextGroupInfo[] _textGroupStack = new TextGroupInfo[MaxTextGroupStackDepth];
    private int _textGroupStackDepth;

    private readonly struct TextGroupInfo
    {
        public readonly int StartIndex;
        public readonly uint CapturedWarpHead;
        public readonly uint CapturedModifierHead;
        public readonly Vector4 Color;
        public readonly Vector4 ClipRect;

        public TextGroupInfo(int startIndex, uint capturedWarpHead, uint capturedModifierHead, Vector4 color, Vector4 clipRect = default)
        {
            StartIndex = startIndex;
            CapturedWarpHead = capturedWarpHead;
            CapturedModifierHead = capturedModifierHead;
            Color = color;
            ClipRect = clipRect;
        }
    }

    // Tracks group ranges and combined bounds for tile assignment (like DerpLib)
    private readonly List<(int startIdx, int endIdx, float minX, float minY, float maxX, float maxY)> _groupBounds = new(32);
    private readonly bool[] _isGroupedCommand;
    private readonly int[] _groupStartToBoundsIndex;

    // Lattice storage
    private readonly SdfLattice[] _lattices = new SdfLattice[MaxLattices];
    private int _latticeCount;

    // Segmented polyline storage
    private readonly SdfPolylineHeader[] _polylineHeaders = new SdfPolylineHeader[MaxPolylineHeaders];
    private readonly Vector2[] _polylinePoints = new Vector2[MaxPolylinePoints];
    private readonly float[] _polylineLengths = new float[MaxPolylinePoints];
    private int _polylineHeaderCount;
    private int _polylinePointCount;
    private bool _warnedPointBufferFullThisFrame;

    // Gradient stop storage
    private readonly SdfGradientStop[] _gradientStops = new SdfGradientStop[MaxGradientStops];
    private int _gradientStopCount;

    // Tile data (CPU-side building for GPU consumption)
    private readonly uint[] _tileOffsets;
    private readonly uint[] _tileIndices = new uint[MaxTileIndices];
    private List<ushort>[]? _tileLists; // Temporary per-tile lists
    private int[]? _visitedTileStamp; // For curve tile walking (avoids duplicates) - stamp-based to avoid per-command clears
    private int _visitedTileStampId;
    private int[]? _tileTouchedStamp; // For clearing only touched tiles each Build (sparse tile clear)
    private int _tileTouchedStampId;
    private int[]? _touchedTiles;
    private int _touchedTileCount;
    private int _tilesX;
    private int _tilesY;
    private int _tileCount;
    private int _tileIndexCount;

    private const int FramesInFlight = 2;

    private StorageBuffer<SdfCommand>[] _gpuBuffers;
    private StorageBuffer<SdfWarpNode>[] _warpNodeBuffers;
    private StorageBuffer<SdfModifierNode>[] _modifierNodeBuffers;
    private StorageBuffer<SdfLattice>[] _latticeBuffers;
    private StorageBuffer<SdfPolylineHeader>[] _headerBuffers;
    private StorageBuffer<Vector2>[] _pointBuffers;
    private StorageBuffer<float>[] _lengthBuffers;
    private StorageBuffer<SdfGradientStop>[] _gradientStopBuffers;
    private StorageBuffer<uint>[] _tileOffsetsBuffers;
    private StorageBuffer<uint>[] _tileIndicesBuffers;
    private bool _disposed;

    public int Count => _count;
    public int MaxCommands => _maxCommands;
    public int LatticeCount => _latticeCount;
    public int PolylineCount => _polylineHeaderCount;
    public int PointCount => _polylinePointCount;
    public int GradientStopCount => _gradientStopCount;
    public bool HasGlyphCommands => _hasGlyphCommands;
    public bool HasSecondaryGlyphCommands => _hasSecondaryGlyphCommands;

    /// <summary>
    /// Get the GPU storage buffer containing SDF commands for a specific frame.
    /// </summary>
    public ref StorageBuffer<SdfCommand> GetGpuBuffer(int frameIndex) => ref _gpuBuffers[frameIndex];

    /// <summary>
    /// Get the GPU storage buffer containing warp nodes for a specific frame.
    /// </summary>
    public ref StorageBuffer<SdfWarpNode> GetWarpNodeBuffer(int frameIndex) => ref _warpNodeBuffers[frameIndex];

    /// <summary>
    /// Get the GPU storage buffer containing modifier nodes for a specific frame.
    /// </summary>
    public ref StorageBuffer<SdfModifierNode> GetModifierNodeBuffer(int frameIndex) => ref _modifierNodeBuffers[frameIndex];

    /// <summary>
    /// Get the GPU storage buffer containing lattices for a specific frame.
    /// </summary>
    public ref StorageBuffer<SdfLattice> GetLatticeBuffer(int frameIndex) => ref _latticeBuffers[frameIndex];

    /// <summary>
    /// Get the GPU storage buffer containing polyline headers for a specific frame.
    /// </summary>
    public ref StorageBuffer<SdfPolylineHeader> GetHeaderBuffer(int frameIndex) => ref _headerBuffers[frameIndex];

    /// <summary>
    /// Get the GPU storage buffer containing polyline points for a specific frame.
    /// </summary>
    public ref StorageBuffer<Vector2> GetPointBuffer(int frameIndex) => ref _pointBuffers[frameIndex];

    /// <summary>
    /// Get the GPU storage buffer containing polyline prefix lengths (in pixels) for a specific frame.
    /// lengths[startIndex] == 0 and lengths increase monotonically per polyline.
    /// </summary>
    public ref StorageBuffer<float> GetLengthBuffer(int frameIndex) => ref _lengthBuffers[frameIndex];

    /// <summary>
    /// Get the GPU storage buffer containing gradient stops for a specific frame.
    /// </summary>
    public ref StorageBuffer<SdfGradientStop> GetGradientStopBuffer(int frameIndex) => ref _gradientStopBuffers[frameIndex];

    /// <summary>
    /// Get the GPU storage buffer containing tile offsets for a specific frame.
    /// </summary>
    public ref StorageBuffer<uint> GetTileOffsetsBuffer(int frameIndex) => ref _tileOffsetsBuffers[frameIndex];

    /// <summary>
    /// Get the GPU storage buffer containing tile command indices for a specific frame.
    /// </summary>
    public ref StorageBuffer<uint> GetTileIndicesBuffer(int frameIndex) => ref _tileIndicesBuffers[frameIndex];

    /// <summary>
    /// Number of tiles in X direction.
    /// </summary>
    public int TilesX => _tilesX;

    /// <summary>
    /// Number of tiles in Y direction.
    /// </summary>
    public int TilesY => _tilesY;

    public SdfBuffer(ILogger log, MemoryAllocator allocator, int maxCommands = 8192)
    {
        _log = log;
        _allocator = allocator;
        _maxCommands = maxCommands;

        _commands = new SdfCommand[maxCommands];
        _isGroupedCommand = new bool[maxCommands];
        _groupStartToBoundsIndex = new int[maxCommands];

        _maxWarpNodes = Math.Max(256, maxCommands * 2);
        _warpNodes = new SdfWarpNode[_maxWarpNodes + 1];

        _maxModifierNodes = Math.Max(256, maxCommands * 2);
        _modifierNodes = new SdfModifierNode[_maxModifierNodes + 1];

        // Allocate per-frame GPU buffers (double buffered for frames in flight)
        _gpuBuffers = new StorageBuffer<SdfCommand>[FramesInFlight];
        _warpNodeBuffers = new StorageBuffer<SdfWarpNode>[FramesInFlight];
        _modifierNodeBuffers = new StorageBuffer<SdfModifierNode>[FramesInFlight];
        _latticeBuffers = new StorageBuffer<SdfLattice>[FramesInFlight];
        _headerBuffers = new StorageBuffer<SdfPolylineHeader>[FramesInFlight];
        _pointBuffers = new StorageBuffer<Vector2>[FramesInFlight];
        _lengthBuffers = new StorageBuffer<float>[FramesInFlight];
        _gradientStopBuffers = new StorageBuffer<SdfGradientStop>[FramesInFlight];
        _tileOffsetsBuffers = new StorageBuffer<uint>[FramesInFlight];
        _tileIndicesBuffers = new StorageBuffer<uint>[FramesInFlight];

        const int maxTileOffsets = 262145; // max tiles = 4K x 4K screen / 8 = 512x512 = 262144 + 1 sentinel

        for (int i = 0; i < FramesInFlight; i++)
        {
            _gpuBuffers[i] = allocator.CreateStorageBuffer<SdfCommand>(maxCommands);
            _warpNodeBuffers[i] = allocator.CreateStorageBuffer<SdfWarpNode>(_maxWarpNodes + 1);
            _modifierNodeBuffers[i] = allocator.CreateStorageBuffer<SdfModifierNode>(_maxModifierNodes + 1);
            _latticeBuffers[i] = allocator.CreateStorageBuffer<SdfLattice>(MaxLattices);
            _headerBuffers[i] = allocator.CreateStorageBuffer<SdfPolylineHeader>(MaxPolylineHeaders);
            _pointBuffers[i] = allocator.CreateStorageBuffer<Vector2>(MaxPolylinePoints);
            _lengthBuffers[i] = allocator.CreateStorageBuffer<float>(MaxPolylinePoints);
            _gradientStopBuffers[i] = allocator.CreateStorageBuffer<SdfGradientStop>(MaxGradientStops);
            _tileOffsetsBuffers[i] = allocator.CreateStorageBuffer<uint>(maxTileOffsets);
            _tileIndicesBuffers[i] = allocator.CreateStorageBuffer<uint>(MaxTileIndices);
        }

        // CPU-side tile offsets array
        _tileOffsets = new uint[maxTileOffsets];

        _log.Debug("SdfBuffer created: {MaxCommands} commands, {MaxWarpNodes} warp nodes, {MaxModifierNodes} modifier nodes, {MaxLattices} lattices, {MaxHeaders} polylines, {MaxPoints} points",
            maxCommands, _maxWarpNodes, _maxModifierNodes, MaxLattices, MaxPolylineHeaders, MaxPolylinePoints);
    }

    // ========================================================================
    // Modifier Stack Management
    // ========================================================================

    public void PushModifierOffset(float offsetX, float offsetY)
    {
        PushModifier(SdfModifierType.Offset, new Vector4(offsetX, offsetY, 0f, 0f));
    }

    public void PushModifierFeather(float radiusPx, SdfFeatherDirection direction = SdfFeatherDirection.Both)
    {
        if (radiusPx <= 0f)
        {
            return;
        }

        float directionValue = (float)direction;
        PushModifier(SdfModifierType.Feather, new Vector4(radiusPx, directionValue, 0f, 0f));
    }

    private void PushModifier(SdfModifierType type, Vector4 @params)
    {
        if (_modifierStackDepth >= MaxModifierStackDepth)
        {
            _log.Warning("SdfBuffer modifier stack full (max {Max}), ignoring push", MaxModifierStackDepth);
            return;
        }

        if (_modifierNodeCount >= _maxModifierNodes)
        {
            _log.Warning("SdfBuffer modifier node buffer full (max {Max}), ignoring push", _maxModifierNodes);
            return;
        }

        _modifierNodeCount++;
        _modifierNodes[_modifierNodeCount] = new SdfModifierNode(_currentModifierHead, type, @params);
        _currentModifierHead = (uint)_modifierNodeCount;
        _modifierStackDepth++;
    }

    public void PopModifier()
    {
        if (_modifierStackDepth <= 0)
        {
            _log.Warning("SdfBuffer modifier stack empty, ignoring pop");
            return;
        }

        _modifierStackDepth--;
        if (_currentModifierHead != 0)
        {
            _currentModifierHead = _modifierNodes[_currentModifierHead].Prev;
        }
    }

    public int ModifierStackDepth => _modifierStackDepth;

    /// <summary>
    /// Add gradient stops to the internal stop buffer and return the starting index.
    /// Stops are stored per-frame and must be uploaded via Flush().
    /// </summary>
    public int AddGradientStops(ReadOnlySpan<SdfGradientStop> stops)
    {
        if (stops.Length <= 0)
        {
            return 0;
        }

        if (_gradientStopCount + stops.Length > MaxGradientStops)
        {
            _log.Warning("SdfBuffer gradient stop buffer full, dropping stops");
            return 0;
        }

        int startIndex = _gradientStopCount;
        stops.CopyTo(_gradientStops.AsSpan(startIndex, stops.Length));
        _gradientStopCount += stops.Length;
        return startIndex;
    }

    /// <summary>
    /// Push a warp onto the stack. All subsequent shapes will have this warp applied.
    /// </summary>
    public void PushWarp(SdfWarp warp)
    {
        if (_warpStackDepth >= MaxWarpStackDepth)
        {
            _log.Warning("SdfBuffer warp stack full (max {Max}), ignoring push", MaxWarpStackDepth);
            return;
        }

        if (_warpNodeCount >= _maxWarpNodes)
        {
            _log.Warning("SdfBuffer warp node buffer full (max {Max}), ignoring push", _maxWarpNodes);
            return;
        }

        _warpNodeCount++;
        _warpNodes[_warpNodeCount] = new SdfWarpNode(_currentWarpHead, warp);
        _currentWarpHead = (uint)_warpNodeCount;
        _warpStackDepth++;
    }

    /// <summary>
    /// Pop the top warp from the stack.
    /// </summary>
    public void PopWarp()
    {
        if (_warpStackDepth <= 0)
        {
            _log.Warning("SdfBuffer warp stack empty, ignoring pop");
            return;
        }
        _warpStackDepth--;

        if (_currentWarpHead != 0)
        {
            _currentWarpHead = _warpNodes[_currentWarpHead].Prev;
        }
    }

    /// <summary>
    /// Current warp stack depth.
    /// </summary>
    public int WarpStackDepth => _warpStackDepth;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetWarpHead(in SdfCommand cmd)
    {
        return BitConverter.SingleToUInt32Bits(cmd.Rotation.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SdfCommand WithWarpHead(in SdfCommand cmd, uint warpHead)
    {
        var result = cmd;
        result.Rotation = new Vector2(cmd.Rotation.X, BitConverter.UInt32BitsToSingle(warpHead));
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetModifierHead(in SdfCommand cmd)
    {
        return BitConverter.SingleToUInt32Bits(cmd.WarpParams.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SdfCommand WithModifierHead(in SdfCommand cmd, uint modifierHead)
    {
        var result = cmd;
        result.WarpParams = new Vector4(cmd.WarpParams.X, BitConverter.UInt32BitsToSingle(modifierHead), cmd.WarpParams.Z, cmd.WarpParams.W);
        return result;
    }

    // ========================================================================
    // Clip Rect Stack Management
    // ========================================================================

    /// <summary>
    /// Push a clip rect onto the stack. All commands will be clipped to this rect.
    /// </summary>
    /// <param name="clipRect">Clip rect as (x, y, width, height). Use SdfCommand.NoClip for no clipping.</param>
    public void PushClipRect(Vector4 clipRect)
    {
        if (_clipStackDepth >= MaxClipStackDepth)
        {
            _log.Warning("SdfBuffer clip stack full (max {Max}), ignoring push", MaxClipStackDepth);
            return;
        }
        _clipStack[_clipStackDepth++] = clipRect;
    }

    /// <summary>
    /// Pop the top clip rect from the stack.
    /// </summary>
    public void PopClipRect()
    {
        if (_clipStackDepth <= 0)
        {
            _log.Warning("SdfBuffer clip stack empty, ignoring pop");
            return;
        }
        _clipStackDepth--;
    }

    /// <summary>
    /// Get the current clip rect (top of stack), or NoClip if stack is empty.
    /// </summary>
    public Vector4 CurrentClipRect => _clipStackDepth > 0 ? _clipStack[_clipStackDepth - 1] : SdfCommand.NoClip;

    /// <summary>
    /// Current clip stack depth.
    /// </summary>
    public int ClipStackDepth => _clipStackDepth;

    // ========================================================================
    // Alpha Mask Stack Management
    // ========================================================================

    /// <summary>
    /// Current mask stack depth.
    /// </summary>
    public int MaskStackDepth => _maskStackDepth;

    /// <summary>
    /// Push a single mask shape onto the stack.
    /// All shapes rendered while mask is active get their alpha multiplied by mask coverage.
    /// </summary>
    /// <param name="maskCmd">Mask command created by SdfMaskShape factory methods.</param>
    public void PushMask(SdfCommand maskCmd)
    {
        if (_maskStackDepth >= MaxMaskStackDepth)
        {
            _log.Warning("SdfBuffer mask stack full (max {Max}), ignoring PushMask", MaxMaskStackDepth);
            return;
        }

        // Add the mask command to the buffer (captures current warp + modifiers).
        Add(maskCmd);
        _maskStackDepth++;
    }

    public void PushMask(ReadOnlySpan<SdfCommand> maskCommands)
    {
        if (_maskStackDepth >= MaxMaskStackDepth)
        {
            _log.Warning("SdfBuffer mask stack full (max {Max}), ignoring PushMask", MaxMaskStackDepth);
            return;
        }

        if (maskCommands.IsEmpty)
        {
            _log.Warning("SdfBuffer PushMask called with empty span, ignoring");
            return;
        }

        for (int i = 0; i < maskCommands.Length; i++)
        {
            Add(maskCommands[i]);
        }
        _maskStackDepth++;
    }

    /// <summary>
    /// Push a union of mask shapes onto the stack.
    /// The resulting mask is visible where ANY of the input shapes are visible.
    /// </summary>
    /// <param name="unionMasks">Array of mask commands to union (from SdfMaskShape.Union).</param>
    public void PushMask(SdfCommand[] unionMasks)
    {
        PushMask(unionMasks.AsSpan());
    }

    /// <summary>
    /// Pop the top mask from the stack.
    /// </summary>
    public void PopMask()
    {
        if (_maskStackDepth <= 0)
        {
            _log.Warning("SdfBuffer mask stack empty, ignoring PopMask");
            return;
        }

        // Add a MaskPop command
        AddRaw(SdfCommand.MaskPop());
        _maskStackDepth--;
    }

    // ========================================================================
    // Boolean Group Management
    // ========================================================================

    /// <summary>
    /// Current group stack depth.
    /// </summary>
    public int GroupStackDepth => _groupStackDepth;

    /// <summary>
    /// Begin a boolean group. All commands until EndGroup are combined using the specified operation.
    /// Groups can be nested up to 8 levels deep.
    /// </summary>
    /// <param name="op">Boolean operation (Union, Intersect, Subtract, or smooth variants).</param>
    /// <param name="smoothness">Smoothness factor for SmoothUnion/Intersect/Subtract (default 10).</param>
    public void BeginGroup(SdfBooleanOp op, float smoothness = 10f)
    {
        if (_groupStackDepth >= MaxGroupStackDepth)
        {
            _log.Warning("SdfBuffer group stack full (max {Max}), ignoring BeginGroup", MaxGroupStackDepth);
            return;
        }

        // Record the group start index BEFORE adding the GroupBegin command
        int startIndex = _count;

        // Push a GroupBegin command
        AddRaw(SdfCommand.GroupBegin(op, smoothness));

        // Record the group info for when we end it (startIndex points to GroupBegin command)
        _groupStack[_groupStackDepth++] = new GroupInfo(startIndex, op, smoothness);
    }

    /// <summary>
    /// End the current boolean group. The combined result is rendered with the specified styling.
    /// </summary>
    /// <param name="color">Fill color for the combined shape.</param>
    /// <param name="strokeColor">Optional stroke color.</param>
    /// <param name="strokeWidth">Stroke width in pixels.</param>
    /// <param name="glowRadius">Glow effect radius.</param>
    /// <param name="softEdge">Anti-aliasing edge width.</param>
    public void EndGroup(Vector4 color, Vector4 strokeColor = default, float strokeWidth = 0f,
        float glowRadius = 0f, float softEdge = SdfCommand.DefaultSoftEdge)
    {
        EndGroupInternal(SdfCommand.GroupEnd(color, strokeColor, strokeWidth, glowRadius, softEdge));
    }

    /// <summary>
    /// End the current boolean group with trim + dash options for the group's stroke.
    /// </summary>
    public void EndGroup(
        Vector4 color,
        Vector4 strokeColor,
        float strokeWidth,
        float glowRadius,
        float softEdge,
        in SdfStrokeTrim strokeTrim,
        in SdfStrokeDash strokeDash)
    {
        var endCmd = SdfCommand.GroupEnd(color, strokeColor, strokeWidth, glowRadius, softEdge);
        if (strokeTrim.Enabled)
        {
            endCmd = endCmd.WithStrokeTrim(in strokeTrim);
        }
        if (strokeDash.Enabled)
        {
            endCmd = endCmd.WithStrokeDash(in strokeDash);
        }
        EndGroupInternal(endCmd);
    }

    private void EndGroupInternal(in SdfCommand groupEndCommand)
    {
        if (_groupStackDepth <= 0)
        {
            _log.Warning("SdfBuffer group stack empty, ignoring EndGroup");
            return;
        }

        _groupStackDepth--;
        var group = _groupStack[_groupStackDepth];
        int endIndex = _count; // Current index (before adding GroupEnd)

        // Compute combined bounds of all shapes in this group (excluding GroupBegin itself)
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = group.StartIndex + 1; i < endIndex; i++)
        {
            ref readonly var cmd = ref _commands[i];
            var shapeType = (SdfShapeType)cmd.Type;

            // Skip nested group markers (their bounds are included via their shapes)
            // Skip mask stack commands (they don't contribute geometry bounds).
            if (shapeType == SdfShapeType.GroupBegin || shapeType == SdfShapeType.GroupEnd ||
                shapeType == SdfShapeType.MaskPush || shapeType == SdfShapeType.MaskPop)
                continue;

            var (cMinX, cMinY, cMaxX, cMaxY) = GetCommandBounds(cmd);
            minX = Math.Min(minX, cMinX);
            minY = Math.Min(minY, cMinY);
            maxX = Math.Max(maxX, cMaxX);
            maxY = Math.Max(maxY, cMaxY);
        }

        float strokeWidth = groupEndCommand.Effects.X;
        float glowRadius = groupEndCommand.Effects.Y;
        float softEdge = groupEndCommand.Effects.Z;

        // Expand bounds by smoothness for smooth booleans + effects.
        // Glow uses a 2x radius falloff in the compute shader (d < glowRadius * 2),
        // so we must include the full glow extent here to avoid tile clipping.
        // Stroke expands by half the stroke width (plus soft edge).
        float expansion = group.Smoothness + Math.Max(glowRadius * 2f, strokeWidth * 0.5f) + softEdge;
        minX -= expansion;
        minY -= expansion;
        maxX += expansion;
        maxY += expansion;

        float modifierExpansion = GetModifierExpansion(in groupEndCommand);
        minX -= modifierExpansion;
        minY -= modifierExpansion;
        maxX += modifierExpansion;
        maxY += modifierExpansion;

        // For clipped groups, ensure tile assignment also covers the clip rect.
        // The group result is rendered through the clip, so missing tiles inside the clip produces
        // tile-aligned boolean artifacts (most visible with filled polygons).
        if (groupEndCommand.ClipRect.W >= 0)
        {
            float clipMinX = groupEndCommand.ClipRect.X;
            float clipMinY = groupEndCommand.ClipRect.Y;
            float clipMaxX = groupEndCommand.ClipRect.X + groupEndCommand.ClipRect.Z;
            float clipMaxY = groupEndCommand.ClipRect.Y + groupEndCommand.ClipRect.W;

            if (clipMinX < minX) minX = clipMinX;
            if (clipMinY < minY) minY = clipMinY;
            if (clipMaxX > maxX) maxX = clipMaxX;
            if (clipMaxY > maxY) maxY = clipMaxY;
        }

        // Add GroupEnd command.
        AddRaw(groupEndCommand);

        // Record this group's range and bounds for tile assignment
        _groupBounds.Add((group.StartIndex, _count, minX, minY, maxX, maxY));
    }

    /// <summary>
    /// End the current boolean group and apply a clip rect to the rendered group result.
    /// This is required when boolean groups are used in UI contexts that rely on per-command clipping.
    /// </summary>
    public void EndGroupClipped(Vector4 color, Vector4 clipRect, Vector4 strokeColor = default, float strokeWidth = 0f,
        float glowRadius = 0f, float softEdge = SdfCommand.DefaultSoftEdge)
    {
        EndGroupClippedInternal(
            color,
            clipRect,
            strokeColor,
            strokeWidth,
            glowRadius,
            blendMode: 0u,
            softEdge);
    }

    public void EndGroupClipped(Vector4 color, Vector4 clipRect, uint blendMode, Vector4 strokeColor = default, float strokeWidth = 0f,
        float glowRadius = 0f, float softEdge = SdfCommand.DefaultSoftEdge)
    {
        EndGroupClippedInternal(
            color,
            clipRect,
            strokeColor,
            strokeWidth,
            glowRadius,
            blendMode,
            softEdge);
    }

    private void EndGroupClippedInternal(Vector4 color, Vector4 clipRect, Vector4 strokeColor, float strokeWidth,
        float glowRadius, uint blendMode, float softEdge)
    {
        if (_groupStackDepth <= 0)
        {
            _log.Warning("SdfBuffer group stack empty, ignoring EndGroupClipped");
            return;
        }

        _groupStackDepth--;
        var group = _groupStack[_groupStackDepth];
        int endIndex = _count; // Current index (before adding GroupEnd)

        // Compute combined bounds of all shapes in this group (excluding GroupBegin itself)
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = group.StartIndex + 1; i < endIndex; i++)
        {
            ref readonly var cmd = ref _commands[i];
            var shapeType = (SdfShapeType)cmd.Type;

            // Skip nested group markers (their bounds are included via their shapes)
            // Skip mask stack commands (they don't contribute geometry bounds).
            if (shapeType == SdfShapeType.GroupBegin || shapeType == SdfShapeType.GroupEnd ||
                shapeType == SdfShapeType.MaskPush || shapeType == SdfShapeType.MaskPop)
            {
                continue;
            }

            var (cMinX, cMinY, cMaxX, cMaxY) = GetCommandBounds(cmd);
            minX = Math.Min(minX, cMinX);
            minY = Math.Min(minY, cMinY);
            maxX = Math.Max(maxX, cMaxX);
            maxY = Math.Max(maxY, cMaxY);
        }

        // Expand bounds by smoothness for smooth booleans + effects.
        // Glow uses a 2x radius falloff in the compute shader (d < glowRadius * 2),
        // so we must include the full glow extent here to avoid tile clipping.
        // Stroke expands by half the stroke width (plus soft edge).
        float expansion = group.Smoothness + Math.Max(glowRadius * 2f, strokeWidth * 0.5f) + softEdge;
        minX -= expansion;
        minY -= expansion;
        maxX += expansion;
        maxY += expansion;

        // For clipped groups, ensure tile assignment also covers the clip rect.
        // The group result is rendered through the clip, so missing tiles inside the clip produces
        // tile-aligned boolean artifacts (most visible with filled polygons).
        if (clipRect.W >= 0)
        {
            float clipMinX = clipRect.X;
            float clipMinY = clipRect.Y;
            float clipMaxX = clipRect.X + clipRect.Z;
            float clipMaxY = clipRect.Y + clipRect.W;

            if (clipMinX < minX) minX = clipMinX;
            if (clipMinY < minY) minY = clipMinY;
            if (clipMaxX > maxX) maxX = clipMaxX;
            if (clipMaxY > maxY) maxY = clipMaxY;
        }

        // Add GroupEnd command with clip rect
        var endCmd = SdfCommand.GroupEnd(color, strokeColor, strokeWidth, glowRadius, softEdge);
        if (blendMode != 0u)
        {
            endCmd = endCmd.WithBlendMode(blendMode);
        }
        if (clipRect.W >= 0)
        {
            endCmd = endCmd.WithClip(clipRect);
        }
        AddRaw(endCmd);

        // Record this group's range and bounds for tile assignment
        _groupBounds.Add((group.StartIndex, _count, minX, minY, maxX, maxY));
    }

    /// <summary>
    /// End the current boolean group with RGB color values.
    /// </summary>
    public void EndGroup(float r, float g, float b, float a = 1f)
    {
        EndGroup(new Vector4(r, g, b, a));
    }

    // ========================================================================
    // Morph Group Management
    // ========================================================================

    /// <summary>
    /// Begin a morph group. The next two shapes will be blended using the morph factor.
    /// </summary>
    /// <param name="morphFactor">Blend factor: 0 = first shape only, 1 = second shape only, 0.5 = equal blend.</param>
    public void BeginMorph(float morphFactor)
    {
        if (_morphStackDepth >= MaxMorphStackDepth)
        {
            _log.Warning("SdfBuffer morph stack full (max {Max}), ignoring BeginMorph", MaxMorphStackDepth);
            return;
        }

        // Record the morph start index BEFORE adding the MorphBegin command
        int startIndex = _count;

        // Push a MorphBegin command
        AddRaw(SdfCommand.MorphBegin(morphFactor));

        // Record the morph info for when we end it
        _morphStack[_morphStackDepth++] = new MorphInfo(startIndex, morphFactor);
    }

    /// <summary>
    /// End the current morph group. The blended result is rendered with the specified styling.
    /// </summary>
    /// <param name="color">Fill color for the blended shape.</param>
    /// <param name="strokeColor">Optional stroke color.</param>
    /// <param name="strokeWidth">Stroke width in pixels.</param>
    /// <param name="glowRadius">Glow effect radius.</param>
    /// <param name="softEdge">Anti-aliasing edge width.</param>
    public void EndMorph(Vector4 color, Vector4 strokeColor = default, float strokeWidth = 0f,
        float glowRadius = 0f, float softEdge = SdfCommand.DefaultSoftEdge)
    {
        if (_morphStackDepth <= 0)
        {
            _log.Warning("SdfBuffer morph stack empty, ignoring EndMorph");
            return;
        }

        _morphStackDepth--;
        var morph = _morphStack[_morphStackDepth];
        int endIndex = _count; // Current index (before adding MorphEnd)

        // Compute combined bounds of both shapes in this morph group
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = morph.StartIndex + 1; i < endIndex; i++)
        {
            ref readonly var cmd = ref _commands[i];
            var shapeType = (SdfShapeType)cmd.Type;

            // Skip markers
            if (shapeType == SdfShapeType.MorphBegin || shapeType == SdfShapeType.MorphEnd)
                continue;

            var (cMinX, cMinY, cMaxX, cMaxY) = GetCommandBounds(cmd);
            minX = Math.Min(minX, cMinX);
            minY = Math.Min(minY, cMinY);
            maxX = Math.Max(maxX, cMaxX);
            maxY = Math.Max(maxY, cMaxY);
        }

        // Expand bounds by effects
        float expansion = glowRadius + softEdge;
        minX -= expansion;
        minY -= expansion;
        maxX += expansion;
        maxY += expansion;

        // Add MorphEnd command
        AddRaw(SdfCommand.MorphEnd(color, strokeColor, strokeWidth, glowRadius, softEdge));

        // Record this morph's range and bounds for tile assignment (reuse groupBounds list)
        _groupBounds.Add((morph.StartIndex, _count, minX, minY, maxX, maxY));
    }

    /// <summary>
    /// End the current morph group with RGB color values.
    /// </summary>
    public void EndMorph(float r, float g, float b, float a = 1f)
    {
        EndMorph(new Vector4(r, g, b, a));
    }

    // ========================================================================
    // Text Group Management (for grouped text with shared warp/effects)
    // ========================================================================

    /// <summary>
    /// Whether we're currently in a text group (glyphs should be added without individual warp).
    /// </summary>
    public bool InTextGroup => _textGroupStackDepth > 0;

    /// <summary>
    /// Begin a text group. Glyphs added after this will be grouped together and
    /// treated as a single SDF shape for warp and effects.
    /// Call EndTextGroup() after adding all glyphs.
    /// </summary>
    /// <param name="color">Fill color for the combined text.</param>
    /// <param name="clipRect">Optional clip rect (x, y, width, height). Default = no clip.</param>
    public void BeginTextGroup(Vector4 color, Vector4 clipRect = default)
    {
        if (_textGroupStackDepth >= MaxTextGroupStackDepth)
        {
            _log.Warning("SdfBuffer text group stack full (max {Max}), ignoring BeginTextGroup", MaxTextGroupStackDepth);
            return;
        }

        // Capture current warp chain - it will be applied to the TextGroup, not individual glyphs
        uint capturedWarpHead = _currentWarpHead;
        uint capturedModifierHead = _currentModifierHead;

        // Push text group info
        _textGroupStack[_textGroupStackDepth++] = new TextGroupInfo(_count, capturedWarpHead, capturedModifierHead, color, clipRect);
    }

    /// <summary>
    /// Begin a text group with RGB color values.
    /// </summary>
    public void BeginTextGroup(float r, float g, float b, float a = 1f, Vector4 clipRect = default)
    {
        BeginTextGroup(new Vector4(r, g, b, a), clipRect);
    }

    /// <summary>
    /// End the current text group. Emits a TextGroup command that wraps all glyphs
    /// added since BeginTextGroup(), with combined bounds and shared warp/effects.
    /// </summary>
    /// <param name="strokeColor">Optional stroke color.</param>
    /// <param name="strokeWidth">Stroke width in pixels.</param>
    /// <param name="glowRadius">Glow effect radius.</param>
    /// <param name="softEdge">Anti-aliasing edge width.</param>
    public void EndTextGroup(Vector4 strokeColor = default, float strokeWidth = 0f,
        float glowRadius = 0f, float softEdge = SdfCommand.DefaultFontSoftEdge)
    {
        if (_textGroupStackDepth <= 0)
        {
            _log.Warning("SdfBuffer text group stack empty, ignoring EndTextGroup");
            return;
        }

        _textGroupStackDepth--;
        var group = _textGroupStack[_textGroupStackDepth];
        int endIndex = _count;
        int glyphCount = endIndex - group.StartIndex;

        if (glyphCount == 0)
        {
            // No glyphs were added, nothing to do
            return;
        }

        // Compute combined bounds of all glyphs in this group
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = group.StartIndex; i < endIndex; i++)
        {
            ref readonly var cmd = ref _commands[i];
            var (cMinX, cMinY, cMaxX, cMaxY) = GetCommandBounds(cmd);
            minX = Math.Min(minX, cMinX);
            minY = Math.Min(minY, cMinY);
            maxX = Math.Max(maxX, cMaxX);
            maxY = Math.Max(maxY, cMaxY);
        }

        // Expand bounds to include effect radii (glow, stroke)
        // This ensures TextGroup command bounds cover the full effect extent
        float effectPadding = MathF.Max(
            strokeWidth,
            MathF.Max(
                glowRadius,
                0f
            )
        );
        effectPadding += softEdge + 2f; // Extra for anti-aliasing and safety

        minX -= effectPadding;
        minY -= effectPadding;
        maxX += effectPadding;
        maxY += effectPadding;

        float modifierExpansion = GetModifierExpansion(group.CapturedModifierHead);
        minX -= modifierExpansion;
        minY -= modifierExpansion;
        maxX += modifierExpansion;
        maxY += modifierExpansion;

        // Calculate center and half-size for TextGroup (with expanded bounds)
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;
        float halfW = (maxX - minX) * 0.5f;
        float halfH = (maxY - minY) * 0.5f;

        // Create TextGroup command
        var textGroup = SdfCommand.TextGroup(
            new Vector2(centerX, centerY),
            new Vector2(halfW, halfH),
            group.StartIndex,
            glyphCount,
            group.Color);

        // Apply captured warp chain head (Rotation.Y stores the head index bitwise).
        textGroup = WithWarpHead(textGroup, group.CapturedWarpHead);
        textGroup = WithModifierHead(textGroup, group.CapturedModifierHead);

        // Apply effects
        if (strokeWidth > 0f || glowRadius > 0f || softEdge != SdfCommand.DefaultSoftEdge)
        {
            textGroup = textGroup.WithEffects(strokeColor, strokeWidth, glowRadius, softEdge);
        }
        else if (strokeColor.W > 0f)
        {
            textGroup = textGroup.WithStroke(strokeColor, strokeWidth);
        }

        // Apply clip rect if specified (W > 0 means valid clip rect)
        if (group.ClipRect.W > 0)
        {
            textGroup = textGroup.WithClip(group.ClipRect);
        }

        // Add TextGroup command (bypassing warp stack - warp already captured)
        AddRaw(textGroup);

        // Add warp displacement to bounds for tile assignment
        float warpExpansion = GetWarpDisplacement(textGroup);
        float finalMinX = minX - warpExpansion;
        float finalMinY = minY - warpExpansion;
        float finalMaxX = maxX + warpExpansion;
        float finalMaxY = maxY + warpExpansion;

        if (group.CapturedWarpHead != 0 && WarpChainContains(group.CapturedWarpHead, SdfWarpType.Repeat))
        {
            // Repeat can contribute anywhere inside the clip rect; ensure tile assignment covers it.
            if (group.ClipRect.W > 0)
            {
                float clipMinX = group.ClipRect.X;
                float clipMinY = group.ClipRect.Y;
                float clipMaxX = group.ClipRect.X + group.ClipRect.Z;
                float clipMaxY = group.ClipRect.Y + group.ClipRect.W;
                finalMinX = Math.Min(finalMinX, clipMinX);
                finalMinY = Math.Min(finalMinY, clipMinY);
                finalMaxX = Math.Max(finalMaxX, clipMaxX);
                finalMaxY = Math.Max(finalMaxY, clipMaxY);
            }
            else
            {
                finalMinX = float.NegativeInfinity;
                finalMinY = float.NegativeInfinity;
                finalMaxX = float.PositiveInfinity;
                finalMaxY = float.PositiveInfinity;
            }
        }

        // Record this text group's range and bounds for tile assignment
        // (includes the glyph commands AND the TextGroup command)
        _groupBounds.Add((group.StartIndex, _count, finalMinX, finalMinY, finalMaxX, finalMaxY));
    }

    // Flag bit to mark glyphs that are internal to a TextGroup (should not be rendered directly)
    // Stored in Flags bits 16+, which don't conflict with warp type (bits 8-15)
    private const uint FlagInternalGlyph = 1u << 16;

    /// <summary>
    /// Add a glyph for text group (bypasses warp stack since TextGroup handles warp).
    /// Sets the internal flag so shader skips direct rendering.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddGlyphForTextGroup(float centerX, float centerY, float halfWidth, float halfHeight,
        float u0, float v0, float u1, float v1, float r, float g, float b, float a)
    {
        AddGlyphForTextGroup(centerX, centerY, halfWidth, halfHeight, u0, v0, u1, v1, r, g, b, a, secondaryFontAtlas: false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddGlyphForTextGroup(float centerX, float centerY, float halfWidth, float halfHeight,
        float u0, float v0, float u1, float v1, float r, float g, float b, float a,
        bool secondaryFontAtlas)
    {
        if (_count >= _maxCommands)
        {
            _log.Warning("SdfBuffer full, dropping glyph command");
            return;
        }

        _hasGlyphCommands = true;
        if (secondaryFontAtlas)
        {
            _hasSecondaryGlyphCommands = true;
        }
        // Add glyph WITHOUT applying warp - the TextGroup will handle warp
        // Mark as internal so shader skips direct rendering
        var cmd = SdfCommand.Glyph(
            new Vector2(centerX, centerY),
            new Vector2(halfWidth, halfHeight),
            new Vector4(u0, v0, u1, v1),
            new Vector4(r, g, b, a));
        cmd.Flags |= FlagInternalGlyph;
        if (secondaryFontAtlas)
        {
            cmd.Flags |= SdfCommand.FlagSecondaryFontAtlas;
        }
        _commands[_count++] = cmd;
    }

    /// <summary>
    /// Add a command directly without applying the warp stack (used for group markers).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddRaw(SdfCommand cmd)
    {
        if (_count >= _maxCommands)
        {
            _log.Warning("SdfBuffer full, dropping command");
            return;
        }
        cmd = WithModifierHead(cmd, _currentModifierHead);
        _commands[_count++] = cmd;
    }

    // ========================================================================
    // Lattice Management
    // ========================================================================

    /// <summary>
    /// Add a lattice and return its index. Use this index with PushLattice().
    /// </summary>
    public int AddLattice(in SdfLattice lattice)
    {
        if (_latticeCount >= MaxLattices)
        {
            _log.Warning("SdfBuffer lattice buffer full (max {Max}), returning 0", MaxLattices);
            return 0;
        }
        int index = _latticeCount++;
        _lattices[index] = lattice;
        return index;
    }

    /// <summary>
    /// Push a lattice warp onto the stack using a pre-registered lattice index.
    /// </summary>
    /// <param name="latticeIndex">Index from AddLattice().</param>
    /// <param name="scaleX">Width of the lattice effect area in pixels.</param>
    /// <param name="scaleY">Height of the lattice effect area in pixels.</param>
    public void PushLattice(int latticeIndex, float scaleX = 200f, float scaleY = 200f)
    {
        PushWarp(SdfWarp.Lattice(latticeIndex, scaleX, scaleY));
    }

    /// <summary>
    /// Convenience method: Add a lattice and immediately push it onto the warp stack.
    /// </summary>
    public int PushNewLattice(in SdfLattice lattice, float scaleX = 200f, float scaleY = 200f)
    {
        int index = AddLattice(lattice);
        PushLattice(index, scaleX, scaleY);
        return index;
    }

    // ========================================================================
    // Polyline Management (Segmented)
    // ========================================================================

    /// <summary>
    /// Allocate space for points and return the start index.
    /// Use this for building polylines manually.
    /// </summary>
    public int AllocatePoints(int count)
    {
        if (_polylinePointCount + count > MaxPolylinePoints)
        {
            if (!_warnedPointBufferFullThisFrame)
            {
                _warnedPointBufferFullThisFrame = true;
                _log.Warning("SdfBuffer point buffer full (need {Need}, have {Have})",
                    count, MaxPolylinePoints - _polylinePointCount);
            }
            return -1;
        }
        int start = _polylinePointCount;
        _polylinePointCount += count;
        return start;
    }

    /// <summary>
    /// Get a span to write points into at the given start index.
    /// </summary>
    public Span<Vector2> GetPointSpan(int startIndex, int count)
    {
        return _polylinePoints.AsSpan(startIndex, count);
    }

    /// <summary>
    /// Add points to the buffer and return their start index.
    /// </summary>
    public int AddPoints(ReadOnlySpan<Vector2> points)
    {
        int start = AllocatePoints(points.Length);
        if (start < 0) return 0;
        points.CopyTo(_polylinePoints.AsSpan(start, points.Length));
        return start;
    }

    /// <summary>
    /// Register a polyline header (bounds + indices) and return its index.
    /// </summary>
    public int AddPolylineHeader(int startIndex, int pointCount, Vector2 boundsMin, Vector2 boundsMax)
    {
        if (_polylineHeaderCount >= MaxPolylineHeaders)
        {
            _log.Warning("SdfBuffer header buffer full (max {Max}), returning 0", MaxPolylineHeaders);
            return 0;
        }

        if (pointCount > 0)
        {
            // Compute per-point prefix lengths for constant-speed trimming/dashing.
            // Note: lengths are stored per point index (shared buffer), but reset to 0 at the start of each polyline.
            int end = startIndex + pointCount;
            _polylineLengths[startIndex] = 0f;
            float cumulative = 0f;
            for (int i = startIndex + 1; i < end; i++)
            {
                var a = _polylinePoints[i - 1];
                var b = _polylinePoints[i];
                float dx = b.X - a.X;
                float dy = b.Y - a.Y;
                cumulative += MathF.Sqrt(dx * dx + dy * dy);
                _polylineLengths[i] = cumulative;
            }
        }

        int index = _polylineHeaderCount++;
        _polylineHeaders[index] = new SdfPolylineHeader((uint)startIndex, (uint)pointCount, boundsMin, boundsMax);
        return index;
    }

    /// <summary>
    /// Add a polyline from points and return its header index.
    /// </summary>
    public int AddPolyline(ReadOnlySpan<Vector2> points)
    {
        int start = AddPoints(points);
        var (min, max) = SdfPolylineBuilder.ComputeBounds(points);
        return AddPolylineHeader(start, points.Length, min, max);
    }

    /// <summary>
    /// Add a polyline command using a header index.
    /// </summary>
    public void AddPolylineCommand(int headerIndex, float thickness, float r, float g, float b, float a)
    {
        Add(SdfCommand.Polyline(
            headerIndex,
            thickness,
            new Vector4(r, g, b, a)));
    }

    /// <summary>
    /// Add a polyline from points and immediately draw it.
    /// </summary>
    public int AddPolyline(ReadOnlySpan<Vector2> points, float thickness, float r, float g, float b, float a)
    {
        int headerIdx = AddPolyline(points);
        AddPolylineCommand(headerIdx, thickness, r, g, b, a);
        return headerIdx;
    }

    /// <summary>
    /// Draw a regular polygon.
    /// </summary>
    public void AddPolygon(float centerX, float centerY, float radius, int sides, float thickness, float r, float g, float b, float a, float rotation = 0f)
    {
        int needed = sides + 1; // +1 to close
        int start = AllocatePoints(needed);
        if (start < 0) return;

        var span = GetPointSpan(start, needed);
        int count = SdfPolylineBuilder.WritePolygon(span, new Vector2(centerX, centerY), radius, sides, rotation, closed: true);

        var (min, max) = SdfPolylineBuilder.ComputeBounds(span.Slice(0, count));
        int headerIdx = AddPolylineHeader(start, count, min, max);
        AddPolylineCommand(headerIdx, thickness, r, g, b, a);
    }

    /// <summary>
    /// Draw a star shape.
    /// </summary>
    public void AddStar(float centerX, float centerY, float outerRadius, float innerRadius, int points, float thickness, float r, float g, float b, float a, float rotation = 0f)
    {
        int needed = points * 2 + 1; // vertices + close
        int start = AllocatePoints(needed);
        if (start < 0) return;

        var span = GetPointSpan(start, needed);
        int count = SdfPolylineBuilder.WriteStar(span, new Vector2(centerX, centerY), outerRadius, innerRadius, points, rotation);

        var (min, max) = SdfPolylineBuilder.ComputeBounds(span.Slice(0, count));
        int headerIdx = AddPolylineHeader(start, count, min, max);
        AddPolylineCommand(headerIdx, thickness, r, g, b, a);
    }

    /// <summary>
    /// Draw a waveform/graph from sample data.
    /// </summary>
    public void AddWaveform(ReadOnlySpan<float> samples, float startX, float width, float baseY, float amplitude, float thickness, float r, float g, float b, float a)
    {
        int start = AllocatePoints(samples.Length);
        if (start < 0) return;

        var span = GetPointSpan(start, samples.Length);
        int count = SdfPolylineBuilder.WriteWaveform(span, samples, startX, width, baseY, amplitude);

        var (min, max) = SdfPolylineBuilder.ComputeBounds(span.Slice(0, count));
        int headerIdx = AddPolylineHeader(start, count, min, max);
        AddPolylineCommand(headerIdx, thickness, r, g, b, a);
    }

    /// <summary>
    /// Draw a profiler-style graph.
    /// </summary>
    public void AddGraph(ReadOnlySpan<float> values, float x, float y, float width, float height, float minValue, float maxValue, float thickness, float r, float g, float b, float a)
    {
        int start = AllocatePoints(values.Length);
        if (start < 0) return;

        var span = GetPointSpan(start, values.Length);
        int count = SdfPolylineBuilder.WriteGraph(span, values, x, y, width, height, minValue, maxValue);

        var (min, max) = SdfPolylineBuilder.ComputeBounds(span.Slice(0, count));
        int headerIdx = AddPolylineHeader(start, count, min, max);
        AddPolylineCommand(headerIdx, thickness, r, g, b, a);
    }

    // ========================================================================
    // Filled Polygons
    // ========================================================================

    /// <summary>
    /// Add a filled polygon command using a header index.
    /// </summary>
    public void AddFilledPolygonCommand(int headerIndex, float r, float g, float b, float a)
    {
        Add(SdfCommand.FilledPolygon(
            headerIndex,
            new Vector4(r, g, b, a)));
    }

    /// <summary>
    /// Draw a filled regular polygon.
    /// </summary>
    public void AddFilledPolygon(float centerX, float centerY, float radius, int sides, float r, float g, float b, float a, float rotation = 0f)
    {
        int start = AllocatePoints(sides);
        if (start < 0) return;

        var span = GetPointSpan(start, sides);
        int count = SdfPolylineBuilder.WritePolygon(span, new Vector2(centerX, centerY), radius, sides, rotation, closed: false);

        var (min, max) = SdfPolylineBuilder.ComputeBounds(span.Slice(0, count));
        int headerIdx = AddPolylineHeader(start, count, min, max);
        AddFilledPolygonCommand(headerIdx, r, g, b, a);
    }

    /// <summary>
    /// Draw a filled star shape.
    /// </summary>
    public void AddFilledStar(float centerX, float centerY, float outerRadius, float innerRadius, int points, float r, float g, float b, float a, float rotation = 0f)
    {
        int vertices = points * 2;
        int start = AllocatePoints(vertices);
        if (start < 0) return;

        var span = GetPointSpan(start, vertices);
        var center = new Vector2(centerX, centerY);

        for (int i = 0; i < vertices; i++)
        {
            float angle = rotation + i * MathF.PI / points;
            float rad = (i % 2 == 0) ? outerRadius : innerRadius;
            span[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * rad;
        }

        var (min, max) = SdfPolylineBuilder.ComputeBounds(span);
        int headerIdx = AddPolylineHeader(start, vertices, min, max);
        AddFilledPolygonCommand(headerIdx, r, g, b, a);
    }

    /// <summary>
    /// Draw a filled polygon from custom points.
    /// </summary>
    public int AddFilledPolygon(ReadOnlySpan<Vector2> points, float r, float g, float b, float a)
    {
        int headerIdx = AddPolyline(points);
        AddFilledPolygonCommand(headerIdx, r, g, b, a);
        return headerIdx;
    }

    // ========================================================================

    /// <summary>
    /// Add a command to the buffer. Captures the current warp chain (if any).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(SdfCommand cmd)
    {
        if (_count >= _maxCommands)
        {
            _log.Warning("SdfBuffer full, dropping command");
            return;
        }

        if (cmd.Type == (uint)SdfShapeType.Glyph)
        {
            _hasGlyphCommands = true;
            if ((cmd.Flags & SdfCommand.FlagSecondaryFontAtlas) != 0)
            {
                _hasSecondaryGlyphCommands = true;
            }
        }

        // Capture current warp chain head into the command (Rotation.Y stores the head index bitwise).
        cmd = WithWarpHead(cmd, _currentWarpHead);
        cmd = WithModifierHead(cmd, _currentModifierHead);

        // Apply current clip rect from stack only if the command does not already carry one.
        if (_clipStackDepth > 0 && cmd.ClipRect.W < 0)
        {
            cmd = cmd.WithClip(_clipStack[_clipStackDepth - 1]);
        }

        // If the command is fully outside its clip rect, skip it to avoid wasting SDF buffer slots.
        // (Clipping happens in the shader, but the command still consumes a buffer entry.)
        if (cmd.ClipRect.W >= 0 && IsFullyOutsideClipRect(in cmd))
        {
            return;
        }

        _commands[_count++] = cmd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsFullyOutsideClipRect(in SdfCommand cmd)
    {
        Vector4 clip = cmd.ClipRect;
        float clipMinX = clip.X;
        float clipMinY = clip.Y;
        float clipMaxX = clip.X + clip.Z;
        float clipMaxY = clip.Y + clip.W;

        // Never cull control/marker commands: their presence affects later rendering.
        if (cmd.Type >= (uint)SdfShapeType.GroupBegin)
        {
            return false;
        }

        uint warpHead = GetWarpHead(cmd);
        if (warpHead != 0 && WarpChainContains(warpHead, SdfWarpType.Repeat))
        {
            // Repeat can cause the shape to contribute throughout the clip rect.
            return false;
        }

        float minX;
        float minY;
        float maxX;
        float maxY;

        float shapePad = 0f;

        switch ((SdfShapeType)cmd.Type)
        {
            case SdfShapeType.Circle:
                {
                    float radius = cmd.Size.X;
                    minX = cmd.Position.X - radius;
                    minY = cmd.Position.Y - radius;
                    maxX = cmd.Position.X + radius;
                    maxY = cmd.Position.Y + radius;
                    break;
                }

            case SdfShapeType.Rect:
            case SdfShapeType.RoundedRect:
            case SdfShapeType.RoundedRectPerCorner:
            case SdfShapeType.Glyph:
            case SdfShapeType.Image:
            case SdfShapeType.TextGroup:
                {
                    Vector2 halfSize = cmd.Size;
                    float rotation = cmd.Rotation.X;
                    if (rotation != 0f)
                    {
                        float cos = MathF.Abs(MathF.Cos(rotation));
                        float sin = MathF.Abs(MathF.Sin(rotation));
                        halfSize = new Vector2(
                            cos * halfSize.X + sin * halfSize.Y,
                            sin * halfSize.X + cos * halfSize.Y);
                    }
                    minX = cmd.Position.X - halfSize.X;
                    minY = cmd.Position.Y - halfSize.Y;
                    maxX = cmd.Position.X + halfSize.X;
                    maxY = cmd.Position.Y + halfSize.Y;
                    break;
                }

            case SdfShapeType.Line:
                {
                    Vector2 a = cmd.Position;
                    Vector2 b = cmd.Size;
                    float halfThickness = cmd.Params.X * 0.5f;
                    minX = MathF.Min(a.X, b.X) - halfThickness;
                    minY = MathF.Min(a.Y, b.Y) - halfThickness;
                    maxX = MathF.Max(a.X, b.X) + halfThickness;
                    maxY = MathF.Max(a.Y, b.Y) + halfThickness;
                    break;
                }

            case SdfShapeType.Bezier:
                {
                    Vector2 p0 = cmd.Position;
                    Vector2 p1 = new(cmd.Params.Y, cmd.Params.Z);
                    Vector2 p2 = cmd.Size;
                    float halfThickness = cmd.Params.X * 0.5f;
                    minX = MathF.Min(p0.X, MathF.Min(p1.X, p2.X)) - halfThickness;
                    minY = MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)) - halfThickness;
                    maxX = MathF.Max(p0.X, MathF.Max(p1.X, p2.X)) + halfThickness;
                    maxY = MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)) + halfThickness;
                    break;
                }

            case SdfShapeType.Polyline:
            case SdfShapeType.FilledPolygon:
                {
                    int headerIndex = (int)cmd.Params.Y;
                    if ((uint)headerIndex >= (uint)_polylineHeaderCount)
                    {
                        return false;
                    }

                    Vector2 boundsMin = _polylineHeaders[headerIndex].BoundsMin;
                    Vector2 boundsMax = _polylineHeaders[headerIndex].BoundsMax;
                    minX = boundsMin.X;
                    minY = boundsMin.Y;
                    maxX = boundsMax.X;
                    maxY = boundsMax.Y;

                    if (cmd.Type == (uint)SdfShapeType.Polyline)
                    {
                        shapePad = cmd.Params.X * 0.5f;
                    }
                    break;
                }

            default:
                return false;
        }

        float strokeWidth = cmd.Effects.X;
        float glowExtent = cmd.Effects.Y * 2f;
        float softEdge = cmd.Effects.Z;

        float expansion = shapePad;
        if (strokeWidth > expansion) expansion = strokeWidth;
        if (glowExtent > expansion) expansion = glowExtent;
        if (softEdge > expansion) expansion = softEdge;
        expansion += GetModifierExpansion(in cmd);

        minX -= expansion;
        minY -= expansion;
        maxX += expansion;
        maxY += expansion;

        return maxX <= clipMinX || minX >= clipMaxX || maxY <= clipMinY || minY >= clipMaxY;
    }

    /// <summary>
    /// Add a circle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddCircle(float x, float y, float radius, float r, float g, float b, float a)
    {
        Add(SdfCommand.Circle(
            new Vector2(x, y),
            radius,
            new Vector4(r, g, b, a)));
    }

    /// <summary>
    /// Add a rectangle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddRect(float x, float y, float halfWidth, float halfHeight, float r, float g, float b, float a)
    {
        Add(SdfCommand.Rect(
            new Vector2(x, y),
            new Vector2(halfWidth, halfHeight),
            new Vector4(r, g, b, a)));
    }

    /// <summary>
    /// Add a glyph quad.
    /// If in a text group, the glyph is added without warp (TextGroup handles warp).
    /// Params: u0,v0,u1,v1 in normalized atlas UV space.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddGlyph(float centerX, float centerY, float halfWidth, float halfHeight, float u0, float v0, float u1, float v1, float r, float g, float b, float a)
    {
        AddGlyph(centerX, centerY, halfWidth, halfHeight, u0, v0, u1, v1, r, g, b, a, secondaryFontAtlas: false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddGlyph(float centerX, float centerY, float halfWidth, float halfHeight,
        float u0, float v0, float u1, float v1,
        float r, float g, float b, float a,
        bool secondaryFontAtlas)
    {
        _hasGlyphCommands = true;
        if (secondaryFontAtlas)
        {
            _hasSecondaryGlyphCommands = true;
        }

        // If in text group, add without warp (TextGroup will handle warp for the whole string)
        if (_textGroupStackDepth > 0)
        {
            AddGlyphForTextGroup(centerX, centerY, halfWidth, halfHeight, u0, v0, u1, v1, r, g, b, a, secondaryFontAtlas);
            return;
        }

        // Otherwise add with normal warp handling
        var cmd = SdfCommand.Glyph(
            new Vector2(centerX, centerY),
            new Vector2(halfWidth, halfHeight),
            new Vector4(u0, v0, u1, v1),
            new Vector4(r, g, b, a));
        if (secondaryFontAtlas)
        {
            cmd.Flags |= SdfCommand.FlagSecondaryFontAtlas;
        }
        Add(cmd);
    }

    /// <summary>
    /// Add a textured image quad. Samples color from the bindless texture array.
    /// UVs are normalized (0-1), textureIndex is the bindless slot index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddImage(float centerX, float centerY, float halfWidth, float halfHeight,
        float u0, float v0, float u1, float v1,
        uint textureIndex,
        float r, float g, float b, float a)
    {
        var cmd = SdfCommand.Image(
            new Vector2(centerX, centerY),
            new Vector2(halfWidth, halfHeight),
            new Vector4(u0, v0, u1, v1),
            new Vector4(r, g, b, a),
            textureIndex);
        Add(cmd);
    }

    /// <summary>
    /// Add a rounded rectangle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddRoundedRect(float x, float y, float halfWidth, float halfHeight, float cornerRadius, float r, float g, float b, float a)
    {
        Add(SdfCommand.RoundedRect(
            new Vector2(x, y),
            new Vector2(halfWidth, halfHeight),
            cornerRadius,
            new Vector4(r, g, b, a)));
    }

    /// <summary>
    /// Add a line (capsule) from point A to point B.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddLine(float x1, float y1, float x2, float y2, float thickness, float r, float g, float b, float a)
    {
        Add(SdfCommand.Line(
            new Vector2(x1, y1),
            new Vector2(x2, y2),
            thickness,
            new Vector4(r, g, b, a)));
    }

    /// <summary>
    /// Add a quadratic bezier curve.
    /// Converts to polyline for consistent tile assignment and rendering (like DerpLib).
    /// </summary>
    public void AddBezier(float x0, float y0, float cx, float cy, float x1, float y1,
        float thickness, float r, float g, float b, float a,
        float glowRadius = 0f)
    {
        // Calculate approximate curve length
        float chordLength = MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        float controlDist = MathF.Sqrt((cx - x0) * (cx - x0) + (cy - y0) * (cy - y0))
                          + MathF.Sqrt((x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy));
        float approxLength = (chordLength + controlDist) * 0.5f;

        // ~3 pixels per segment (like DerpLib)
        const float PixelsPerSegment = 3f;
        int subdivisions = Math.Max(1, (int)MathF.Ceiling(approxLength / PixelsPerSegment));

        // Allocate points for the tessellated curve
        int pointCount = subdivisions + 1;
        int startIdx = AllocatePoints(pointCount);
        if (startIdx < 0) return;

        var span = GetPointSpan(startIdx, pointCount);

        // Tessellate the bezier curve
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = 0; i <= subdivisions; i++)
        {
            float t = (float)i / subdivisions;
            float mt = 1f - t;
            float mt2 = mt * mt;
            float t2 = t * t;
            float tmt2 = 2f * mt * t;

            float px = mt2 * x0 + tmt2 * cx + t2 * x1;
            float py = mt2 * y0 + tmt2 * cy + t2 * y1;

            span[i] = new Vector2(px, py);

            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px);
            maxY = Math.Max(maxY, py);
        }

        // Add as polyline with effects
        int headerIdx = AddPolylineHeader(startIdx, pointCount, new Vector2(minX, minY), new Vector2(maxX, maxY));
        var cmd = SdfCommand.Polyline(headerIdx, thickness, new Vector4(r, g, b, a));
        if (glowRadius > 0f)
            cmd = cmd.WithGlow(glowRadius);
        // Note: softEdge is already set to DefaultSoftEdge in Polyline factory
        // Custom softEdge would require modifying Effects.Z directly
        Add(cmd);
    }

    // ========================================================================
    // Tile Building (CPU-side for GPU consumption)
    // ========================================================================

    /// <summary>
    /// Build tile data for the current frame. Call after adding all commands.
    /// Assigns each command to the tiles it overlaps based on bounding boxes.
    /// Groups are assigned based on their combined bounds.
    /// </summary>
    public void Build(int screenWidth, int screenHeight)
    {
        ClearTouchedTiles();

        if (_count == 0)
        {
            _tilesX = 0;
            _tilesY = 0;
            _tileCount = 0;
            _tileIndexCount = 0;
            return;
        }

        // Calculate tile grid dimensions
        _tilesX = (screenWidth + TileSize - 1) / TileSize;
        _tilesY = (screenHeight + TileSize - 1) / TileSize;
        _tileCount = _tilesX * _tilesY;

        // Initialize or resize tile lists
        if (_tileLists == null || _tileLists.Length < _tileCount)
        {
            _tileLists = new List<ushort>[_tileCount];
            for (int i = 0; i < _tileCount; i++)
            {
                _tileLists[i] = new List<ushort>(64);
            }
        }
        else
        {
            for (int i = 0; i < _tileCount; i++)
            {
                if (_tileLists[i] == null)
                    _tileLists[i] = new List<ushort>(64);
            }
        }

        EnsureTileTouchedTracking();
        _tileTouchedStampId = NextTileTouchedStampId();

        // Mark which commands belong to groups and build a lookup table for group starts.
        // Tile assignment must preserve command-index order for correct z-order; groups are assigned
        // once at their start index, then skipped.
        Array.Clear(_isGroupedCommand, 0, _count);
        Array.Fill(_groupStartToBoundsIndex, -1, 0, _count);
        for (int groupIndex = 0; groupIndex < _groupBounds.Count; groupIndex++)
        {
            var group = _groupBounds[groupIndex];
            for (int cmdIdx = group.startIdx; cmdIdx < group.endIdx; cmdIdx++)
            {
                _isGroupedCommand[cmdIdx] = true;
            }

            int existingGroupIndex = _groupStartToBoundsIndex[group.startIdx];
            if (existingGroupIndex < 0)
            {
                _groupStartToBoundsIndex[group.startIdx] = groupIndex;
                continue;
            }

            // If multiple groups start at the same index (rare), prefer the outermost group.
            if (group.endIdx > _groupBounds[existingGroupIndex].endIdx)
            {
                _groupStartToBoundsIndex[group.startIdx] = groupIndex;
            }
        }

        // Ensure visited tiles stamp array is large enough (stamp-based to avoid per-command Array.Clear on big tile grids)
        if (_visitedTileStamp == null || _visitedTileStamp.Length < _tileCount)
        {
            _visitedTileStamp = new int[Math.Max(_tileCount, 4096)];
            _visitedTileStampId = 0;
        }

        // Assign commands to tiles in command-index order to preserve z-order.
        // Use curve walkers for lines and beziers for precise tile assignment.
        int activeGroupEnd = -1;
        for (int cmdIdx = 0; cmdIdx < _count; cmdIdx++)
        {
            if (cmdIdx < activeGroupEnd)
            {
                continue;
            }

            int groupIndex = _groupStartToBoundsIndex[cmdIdx];
            if (groupIndex >= 0)
            {
                var group = _groupBounds[groupIndex];
                AssignGroupToTiles(group.startIdx, group.endIdx, group.minX, group.minY, group.maxX, group.maxY);
                activeGroupEnd = group.endIdx;
                cmdIdx = group.endIdx - 1;
                continue;
            }

            ref readonly var cmd = ref _commands[cmdIdx];
            if ((cmd.Flags & SdfCommand.FlagInternalNoRender) != 0)
            {
                continue;
            }
            var shapeType = (SdfShapeType)cmd.Type;

            switch (shapeType)
            {
                case SdfShapeType.Line:
                    AssignLineToTiles(cmdIdx, in cmd);
                    break;

                case SdfShapeType.Bezier:
                    AssignBezierToTiles(cmdIdx, in cmd);
                    break;

                case SdfShapeType.Polyline:
                    AssignPolylineToTiles(cmdIdx, in cmd);
                    break;

                default:
                    // Use bounding box for filled shapes
                    var (minX, minY, maxX, maxY) = GetCommandBounds(cmd);
                    AssignToTiles(cmdIdx, minX, minY, maxX, maxY);
                    break;
            }
        }

        // Pack tile lists into flat arrays
        int offset = 0;
        for (int i = 0; i < _tileCount; i++)
        {
            _tileOffsets[i] = (uint)offset;
            var list = _tileLists[i];
            for (int j = 0; j < list.Count; j++)
            {
                if (offset >= MaxTileIndices)
                {
                    _log.Warning("SdfBuffer tile index buffer full, some commands may not render");
                    break;
                }
                _tileIndices[offset++] = list[j];
            }
        }
        // Sentinel for last tile's end
        _tileOffsets[_tileCount] = (uint)offset;
        _tileIndexCount = offset;
    }

    private void AssignToTiles(int cmdIdx, float minX, float minY, float maxX, float maxY)
    {
        // Convert to tile coordinates (clamped to screen).
        // Use a small pad so shapes that land exactly on a tile boundary still get assigned to both tiles.
        const float tilePad = 1f;

        int minTileX;
        int minTileY;
        int maxTileX;
        int maxTileY;

        if (!float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(maxX) || !float.IsFinite(maxY))
        {
            minTileX = 0;
            minTileY = 0;
            maxTileX = _tilesX - 1;
            maxTileY = _tilesY - 1;
        }
        else
        {
            minTileX = (int)MathF.Floor((minX - tilePad) / TileSize);
            minTileY = (int)MathF.Floor((minY - tilePad) / TileSize);
            maxTileX = (int)MathF.Floor((maxX + tilePad) / TileSize);
            maxTileY = (int)MathF.Floor((maxY + tilePad) / TileSize);

            if (minTileX < 0) minTileX = 0;
            if (minTileY < 0) minTileY = 0;
            if (maxTileX > _tilesX - 1) maxTileX = _tilesX - 1;
            if (maxTileY > _tilesY - 1) maxTileY = _tilesY - 1;
        }

        // Add command to all overlapping tiles
        for (int ty = minTileY; ty <= maxTileY; ty++)
        {
            for (int tx = minTileX; tx <= maxTileX; tx++)
            {
                int tileIdx = ty * _tilesX + tx;
                AddTileCommand(tileIdx, cmdIdx);
            }
        }
    }

    private void AssignGroupToTiles(int startIdx, int endIdx, float minX, float minY, float maxX, float maxY)
    {
        // Calculate tile range for the group's combined bounds
        const float tilePad = 1f;

        int minTileX;
        int minTileY;
        int maxTileX;
        int maxTileY;

        if (!float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(maxX) || !float.IsFinite(maxY))
        {
            minTileX = 0;
            minTileY = 0;
            maxTileX = _tilesX - 1;
            maxTileY = _tilesY - 1;
        }
        else
        {
            minTileX = (int)MathF.Floor((minX - tilePad) / TileSize);
            minTileY = (int)MathF.Floor((minY - tilePad) / TileSize);
            maxTileX = (int)MathF.Floor((maxX + tilePad) / TileSize);
            maxTileY = (int)MathF.Floor((maxY + tilePad) / TileSize);

            if (minTileX < 0) minTileX = 0;
            if (minTileY < 0) minTileY = 0;
            if (maxTileX > _tilesX - 1) maxTileX = _tilesX - 1;
            if (maxTileY > _tilesY - 1) maxTileY = _tilesY - 1;
        }

        var tileLists = _tileLists!;

        // Assign ALL commands in the group to ALL tiles in the combined bounds
        for (int tileY = minTileY; tileY <= maxTileY; tileY++)
        {
            for (int tileX = minTileX; tileX <= maxTileX; tileX++)
            {
                int tileIdx = tileY * _tilesX + tileX;
                for (int cmdIdx = startIdx; cmdIdx < endIdx; cmdIdx++)
                {
                    if ((_commands[cmdIdx].Flags & SdfCommand.FlagInternalNoRender) != 0)
                    {
                        continue;
                    }
                    AddTileCommand(tileIdx, cmdIdx);
                }
            }
        }
    }

    private void AssignLineToTiles(int cmdIdx, in SdfCommand cmd)
    {
        // Line: Position = start, Size = end, Params.X = thickness
        float x1 = cmd.Position.X;
        float y1 = cmd.Position.Y;
        float x2 = cmd.Size.X;
        float y2 = cmd.Size.Y;
        float thickness = cmd.Params.X * 0.5f;
        float glowRadius = cmd.Effects.Y;
        float glowExtent = glowRadius * 2f;
        float softEdge = cmd.Effects.Z;
        float warpExpand = GetWarpDisplacement(in cmd);
        float modifierExpand = GetModifierExpansion(in cmd);

        int visitId = NextVisitedTileStampId();
        float extraRadiusForTiles = glowExtent + modifierExpand + warpExpand;
        CurveTileWalker.WalkLine(x1, y1, x2, y2, thickness, extraRadiusForTiles, softEdge,
            TileSize, _tilesX, _tilesY,
            _tileLists!, _visitedTileStamp!, visitId,
            _tileTouchedStamp!, _tileTouchedStampId, _touchedTiles!, ref _touchedTileCount,
            cmdIdx);
    }

    private void AssignBezierToTiles(int cmdIdx, in SdfCommand cmd)
    {
        // Bezier: Position = P0, Size = P2, Params.X = thickness, Params.YZ = P1
        float x0 = cmd.Position.X;
        float y0 = cmd.Position.Y;
        float x2 = cmd.Size.X;
        float y2 = cmd.Size.Y;
        float x1 = cmd.Params.Y;
        float y1 = cmd.Params.Z;
        float thickness = cmd.Params.X * 0.5f;
        float glowRadius = cmd.Effects.Y;
        float glowExtent = glowRadius * 2f;
        float softEdge = cmd.Effects.Z;
        float warpExpand = GetWarpDisplacement(in cmd);
        float modifierExpand = GetModifierExpansion(in cmd);

        int visitId = NextVisitedTileStampId();
        float extraRadiusForTiles = glowExtent + modifierExpand + warpExpand;
        CurveTileWalker.WalkBezier(x0, y0, x1, y1, x2, y2, thickness, extraRadiusForTiles, softEdge,
            TileSize, _tilesX, _tilesY,
            _tileLists!, _visitedTileStamp!, visitId,
            _tileTouchedStamp!, _tileTouchedStampId, _touchedTiles!, ref _touchedTileCount,
            cmdIdx);
    }

    private void AssignPolylineToTiles(int cmdIdx, in SdfCommand cmd)
    {
        // Polyline: Params.X = thickness, Params.Y = headerIndex
        int headerIdx = (int)cmd.Params.Y;
        if (headerIdx < 0 || headerIdx >= _polylineHeaderCount)
        {
            // Fallback to bounding box
            var (minX, minY, maxX, maxY) = GetCommandBounds(cmd);
            AssignToTiles(cmdIdx, minX, minY, maxX, maxY);
            return;
        }

        ref readonly var header = ref _polylineHeaders[headerIdx];
        int startIdx = (int)header.StartIndex;
        int pointCount = (int)header.PointCount;

        if (pointCount < 2)
        {
            return;
        }

        float halfThickness = cmd.Params.X * 0.5f;
        float glowRadius = cmd.Effects.Y;
        float glowExtent = glowRadius * 2f;
        float softEdge = cmd.Effects.Z;
        float warpExpand = GetWarpDisplacement(in cmd);
        float modifierExpand = GetModifierExpansion(in cmd);
        float expand = halfThickness + glowExtent + softEdge + modifierExpand + warpExpand;
        const float tilePad = 1f;

        int visitId = NextVisitedTileStampId();

        // Use per-segment expanded AABB tile coverage instead of stepped tile walking.
        // This avoids micro-holes at joins on very small segments while remaining
        // significantly tighter than assigning the full polyline bounds.
        for (int i = 0; i < pointCount - 1; i++)
        {
            var p1 = _polylinePoints[startIdx + i];
            var p2 = _polylinePoints[startIdx + i + 1];

            float minX = MathF.Min(p1.X, p2.X) - expand;
            float minY = MathF.Min(p1.Y, p2.Y) - expand;
            float maxX = MathF.Max(p1.X, p2.X) + expand;
            float maxY = MathF.Max(p1.Y, p2.Y) + expand;

            int minTileX;
            int minTileY;
            int maxTileX;
            int maxTileY;

            if (!float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(maxX) || !float.IsFinite(maxY))
            {
                minTileX = 0;
                minTileY = 0;
                maxTileX = _tilesX - 1;
                maxTileY = _tilesY - 1;
            }
            else
            {
                minTileX = (int)MathF.Floor((minX - tilePad) / TileSize);
                minTileY = (int)MathF.Floor((minY - tilePad) / TileSize);
                maxTileX = (int)MathF.Floor((maxX + tilePad) / TileSize);
                maxTileY = (int)MathF.Floor((maxY + tilePad) / TileSize);

                if (minTileX < 0) minTileX = 0;
                if (minTileY < 0) minTileY = 0;
                if (maxTileX > _tilesX - 1) maxTileX = _tilesX - 1;
                if (maxTileY > _tilesY - 1) maxTileY = _tilesY - 1;
            }

            for (int tileY = minTileY; tileY <= maxTileY; tileY++)
            {
                for (int tileX = minTileX; tileX <= maxTileX; tileX++)
                {
                    int tileIdx = tileY * _tilesX + tileX;
                    if (_visitedTileStamp![tileIdx] == visitId)
                    {
                        continue;
                    }

                    _visitedTileStamp[tileIdx] = visitId;
                    AddTileCommand(tileIdx, cmdIdx);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddTileCommand(int tileIdx, int cmdIdx)
    {
        if (_tileTouchedStamp![tileIdx] != _tileTouchedStampId)
        {
            _tileTouchedStamp[tileIdx] = _tileTouchedStampId;
            _touchedTiles![_touchedTileCount++] = tileIdx;
        }

        _tileLists![tileIdx].Add((ushort)cmdIdx);
    }

    private int NextVisitedTileStampId()
    {
        int next = _visitedTileStampId + 1;
        if (next == int.MaxValue)
        {
            Array.Clear(_visitedTileStamp!, 0, _tileCount);
            next = 1;
        }

        _visitedTileStampId = next;
        return next;
    }

    private void EnsureTileTouchedTracking()
    {
        if (_tileTouchedStamp == null || _tileTouchedStamp.Length < _tileCount)
        {
            _tileTouchedStamp = new int[Math.Max(_tileCount, 4096)];
            _tileTouchedStampId = 0;
        }

        if (_touchedTiles == null || _touchedTiles.Length < _tileCount)
        {
            _touchedTiles = new int[Math.Max(_tileCount, 4096)];
            _touchedTileCount = 0;
        }
    }

    private int NextTileTouchedStampId()
    {
        int next = _tileTouchedStampId + 1;
        if (next == int.MaxValue)
        {
            Array.Clear(_tileTouchedStamp!, 0, _tileCount);
            next = 1;
        }

        _tileTouchedStampId = next;
        return next;
    }

    private void ClearTouchedTiles()
    {
        if (_touchedTileCount == 0 || _tileLists == null || _touchedTiles == null)
        {
            return;
        }

        for (int i = 0; i < _touchedTileCount; i++)
        {
            _tileLists[_touchedTiles[i]].Clear();
        }

        _touchedTileCount = 0;
    }

    /// <summary>
    /// Get bounding box for a command. Returns (minX, minY, maxX, maxY).
    /// </summary>
    private (float, float, float, float) GetCommandBounds(in SdfCommand cmd)
    {
        uint warpHead = GetWarpHead(cmd);
        if (warpHead != 0 && WarpChainContains(warpHead, SdfWarpType.Repeat))
        {
            Vector4 clip = cmd.ClipRect;
            if (clip.W >= 0f)
            {
                float minX = clip.X;
                float minY = clip.Y;
                float maxX = clip.X + clip.Z;
                float maxY = clip.Y + clip.W;
                return (minX, minY, maxX, maxY);
            }

            // Non-finite bounds force conservative full-screen tile assignment.
            return (float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);
        }

        // Effects that expand the bounding box
        // Effects: x=strokeWidth, y=glowRadius, z=softEdge, w=gradientType
        float strokeWidth = cmd.Effects.X;
        float glowRadius = cmd.Effects.Y;
        float glowExtent = glowRadius * 2f;
        float modifierExpand = GetModifierExpansion(in cmd);

        // Calculate warp displacement
        float warpExpand = GetWarpDisplacement(in cmd);

        // Total expansion needed (including warp and modifiers)
        float expand = strokeWidth + glowExtent + modifierExpand + warpExpand + 2f;

        var shapeType = (SdfShapeType)cmd.Type;

        switch (shapeType)
        {
            case SdfShapeType.Circle:
                {
                    float cx = cmd.Position.X;
                    float cy = cmd.Position.Y;
                    float radius = cmd.Size.X + expand;
                    return (cx - radius, cy - radius, cx + radius, cy + radius);
                }

            case SdfShapeType.Rect:
            case SdfShapeType.RoundedRect:
            case SdfShapeType.RoundedRectPerCorner:
            case SdfShapeType.Glyph:
                {
                    float cx = cmd.Position.X;
                    float cy = cmd.Position.Y;
                    float hw = cmd.Size.X;
                    float hh = cmd.Size.Y;

                    float rotation = cmd.Rotation.X;
                    if (rotation != 0f)
                    {
                        float cos = MathF.Abs(MathF.Cos(rotation));
                        float sin = MathF.Abs(MathF.Sin(rotation));
                        float newHw = cos * hw + sin * hh;
                        float newHh = sin * hw + cos * hh;
                        hw = newHw;
                        hh = newHh;
                    }

                    hw += expand;
                    hh += expand;
                    return (cx - hw, cy - hh, cx + hw, cy + hh);
                }

            case SdfShapeType.Line:
                {
                    // Line: Position = start, Size = end
                    float x1 = cmd.Position.X;
                    float y1 = cmd.Position.Y;
                    float x2 = cmd.Size.X;
                    float y2 = cmd.Size.Y;
                    float thickness = cmd.Params.X * 0.5f;
                    float totalExpand = expand + thickness;
                    return (
                        Math.Min(x1, x2) - totalExpand,
                        Math.Min(y1, y2) - totalExpand,
                        Math.Max(x1, x2) + totalExpand,
                        Math.Max(y1, y2) + totalExpand
                    );
                }

            case SdfShapeType.Bezier:
                {
                    // Bezier: Position = P0, Size = P2, Params.yz = P1
                    float x0 = cmd.Position.X;
                    float y0 = cmd.Position.Y;
                    float x2 = cmd.Size.X;
                    float y2 = cmd.Size.Y;
                    float x1 = cmd.Params.Y;
                    float y1 = cmd.Params.Z;
                    float thickness = cmd.Params.X * 0.5f;
                    float totalExpand = expand + thickness;
                    return (
                        Math.Min(Math.Min(x0, x1), x2) - totalExpand,
                        Math.Min(Math.Min(y0, y1), y2) - totalExpand,
                        Math.Max(Math.Max(x0, x1), x2) + totalExpand,
                        Math.Max(Math.Max(y0, y1), y2) + totalExpand
                    );
                }

            case SdfShapeType.Polyline:
            case SdfShapeType.FilledPolygon:
                {
                    // Get bounds from header
                    int headerIdx = (int)cmd.Params.Y;
                    if (headerIdx >= 0 && headerIdx < _polylineHeaderCount)
                    {
                        ref readonly var header = ref _polylineHeaders[headerIdx];
                        float thickness = cmd.Params.X * 0.5f;
                        float totalExpand = expand + thickness;
                        return (
                            header.BoundsMin.X - totalExpand,
                            header.BoundsMin.Y - totalExpand,
                            header.BoundsMax.X + totalExpand,
                            header.BoundsMax.Y + totalExpand
                        );
                    }
                    // Fallback: full screen
                    return (0, 0, float.MaxValue, float.MaxValue);
                }

            case SdfShapeType.GroupBegin:
            case SdfShapeType.GroupEnd:
            case SdfShapeType.MorphBegin:
            case SdfShapeType.MorphEnd:
                {
                    // Group/Morph markers don't have direct bounds - they're assigned to tiles
                    // based on their child shapes during Build(). For now, return full screen.
                    return (0, 0, float.MaxValue, float.MaxValue);
                }

            default:
                // Unknown shape: full screen fallback
                return (0, 0, float.MaxValue, float.MaxValue);
        }
    }

    /// <summary>
    /// Calculate a conservative displacement bound from the command's warp chain.
    /// Used for CPU-side bounds expansion and tile assignment.
    /// </summary>
    private float GetWarpDisplacement(in SdfCommand cmd)
    {
        uint warpHead = GetWarpHead(cmd);
        if (warpHead == 0)
        {
            return 0f;
        }

        float totalDisplacement = 0f;
        uint nodeIdx = warpHead;

        // Safety cap: also bounds worst-case CPU work if someone pushes thousands of warps.
        for (int depth = 0; depth < MaxWarpStackDepth && nodeIdx != 0; depth++)
        {
            ref readonly var node = ref _warpNodes[nodeIdx];
            var warpType = (SdfWarpType)node.Type;

            float param1 = node.Params.Y;
            float param2 = node.Params.Z;
            float param3 = node.Params.W;

            totalDisplacement += GetSingleWarpDisplacement(warpType, param1, param2, param3);
            nodeIdx = node.Prev;
        }

        return totalDisplacement;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool WarpChainContains(uint warpHead, SdfWarpType type)
    {
        uint nodeIdx = warpHead;
        for (int depth = 0; depth < MaxWarpStackDepth && nodeIdx != 0; depth++)
        {
            if ((SdfWarpType)_warpNodes[nodeIdx].Type == type)
            {
                return true;
            }
            nodeIdx = _warpNodes[nodeIdx].Prev;
        }
        return false;
    }

    private float GetModifierExpansion(in SdfCommand cmd)
    {
        uint modifierHead = GetModifierHead(cmd);
        return GetModifierExpansion(modifierHead);
    }

    private float GetModifierExpansion(uint modifierHead)
    {
        if (modifierHead == 0)
        {
            return 0f;
        }

        // Feather is a Gaussian-like falloff (erf/CDF) so it has a long tail, and future curvature-aware
        // variants may further increase the effective feather width near corners. Expand bounds
        // conservatively to avoid tile-aligned clipping artifacts.
        // Note: Feather radius in the UI represents a visual blur extent; we expand more than 1x to ensure
        // low-alpha tails don’t get tile-clipped (especially noticeable on complex pen/polygon shapes).
        const float FeatherExpansionMultiplier = 4f;

        float expansion = 0f;
        uint nodeIdx = modifierHead;

        // Safety cap: also bounds worst-case CPU work if someone pushes thousands of modifiers.
        for (int depth = 0; depth < MaxModifierStackDepth && nodeIdx != 0; depth++)
        {
            ref readonly var node = ref _modifierNodes[nodeIdx];
            var modifierType = (SdfModifierType)node.Type;

            switch (modifierType)
            {
                case SdfModifierType.Offset:
                    expansion += MathF.Max(MathF.Abs(node.Params.X), MathF.Abs(node.Params.Y));
                    break;
                case SdfModifierType.Feather:
                    expansion += MathF.Max(node.Params.X, 0f) * FeatherExpansionMultiplier;
                    break;
            }

            nodeIdx = node.Prev;
        }

        return expansion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float GetSingleWarpDisplacement(SdfWarpType warpType, float param1, float param2, float param3)
    {
        switch (warpType)
        {
            case SdfWarpType.None:
                return 0f;

            case SdfWarpType.Wave:
                // Wave displacement ~= amplitude (param2)
                return MathF.Abs(param2);

            case SdfWarpType.Twist:
                // Conservative estimate for ~100px shapes.
                return MathF.Abs(param1) * 50f;

            case SdfWarpType.Bulge:
                {
                    float bulgeStrength = MathF.Abs(param1);
                    float bulgeRadius = param2 > 0f ? param2 : 100f;
                    return bulgeStrength * bulgeRadius * 0.5f;
                }

            case SdfWarpType.Lattice:
                {
                    int latticeIdx = (int)param1;
                    if (latticeIdx >= 0 && latticeIdx < _latticeCount)
                    {
                        ref readonly var lattice = ref _lattices[latticeIdx];
                        return GetLatticeMaxDisplacement(in lattice, param2, param3);
                    }
                    return MathF.Max(param2, param3) * 0.25f;
                }

            case SdfWarpType.Repeat:
                // Repeat can conceptually extend infinitely; bounds are handled via clip/tile assignment.
                return 0f;

            default:
                return 0f;
        }
    }

    /// <summary>
    /// Calculate maximum displacement from a lattice by checking control point offsets.
    /// Lattice stores offsets from rest positions, so displacement = offset magnitude * scale.
    /// </summary>
    private static float GetLatticeMaxDisplacement(in SdfLattice lattice, float scaleX, float scaleY)
    {
        float maxDisplacement = 0f;

        // Lattice stores offsets from rest positions
        // Check each control point's offset magnitude
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                // Get offset (lattice stores offsets, not absolute positions)
                var offset = lattice[row, col];

                // Calculate displacement in pixels
                float dx = offset.X * scaleX;
                float dy = offset.Y * scaleY;
                float displacement = MathF.Sqrt(dx * dx + dy * dy);

                maxDisplacement = Math.Max(maxDisplacement, displacement);
            }
        }

        return maxDisplacement;
    }

    /// <summary>
    /// Upload commands, lattices, headers, points, and tile data to GPU for the specified frame.
    /// Call after Build().
    /// </summary>
    /// <param name="frameIndex">Current frame index (0 to FramesInFlight-1)</param>
    public void Flush(int frameIndex)
    {
        if (_count == 0) return;
        _gpuBuffers[frameIndex].Upload(_commands.AsSpan(0, _count));

        if (_warpNodeCount > 0)
        {
            _warpNodeBuffers[frameIndex].Upload(_warpNodes.AsSpan(0, _warpNodeCount + 1));
        }

        if (_modifierNodeCount > 0)
        {
            _modifierNodeBuffers[frameIndex].Upload(_modifierNodes.AsSpan(0, _modifierNodeCount + 1));
        }

        if (_latticeCount > 0)
        {
            _latticeBuffers[frameIndex].Upload(_lattices.AsSpan(0, _latticeCount));
        }

        if (_polylineHeaderCount > 0)
        {
            _headerBuffers[frameIndex].Upload(_polylineHeaders.AsSpan(0, _polylineHeaderCount));
        }

        if (_polylinePointCount > 0)
        {
            _pointBuffers[frameIndex].Upload(_polylinePoints.AsSpan(0, _polylinePointCount));
            _lengthBuffers[frameIndex].Upload(_polylineLengths.AsSpan(0, _polylinePointCount));
        }

        if (_gradientStopCount > 0)
        {
            _gradientStopBuffers[frameIndex].Upload(_gradientStops.AsSpan(0, _gradientStopCount));
        }

        // Upload tile data
        if (_tileCount > 0)
        {
            _tileOffsetsBuffers[frameIndex].Upload(_tileOffsets.AsSpan(0, _tileCount + 1));
        }
        if (_tileIndexCount > 0)
        {
            _tileIndicesBuffers[frameIndex].Upload(_tileIndices.AsSpan(0, _tileIndexCount));
        }
    }

    /// <summary>
    /// Reset for next frame. Clears commands, warp stack, group stack, morph stack, lattices, polylines, and tiles.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        ClearTouchedTiles();
        _count = 0;
        _hasGlyphCommands = false;
        _hasSecondaryGlyphCommands = false;
        _warpStackDepth = 0;
        _warpNodeCount = 0;
        _currentWarpHead = 0;
        _modifierStackDepth = 0;
        _modifierNodeCount = 0;
        _currentModifierHead = 0;
        _groupStackDepth = 0;
        _morphStackDepth = 0;
        _textGroupStackDepth = 0;
        _maskStackDepth = 0;
        _groupBounds.Clear();
        _latticeCount = 0;
        _polylineHeaderCount = 0;
        _polylinePointCount = 0;
        _warnedPointBufferFullThisFrame = false;
        _gradientStopCount = 0;
        _tilesX = 0;
        _tilesY = 0;
        _tileCount = 0;
        _tileIndexCount = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = 0; i < FramesInFlight; i++)
        {
            _allocator.FreeStorageBuffer(_gpuBuffers[i]);
            _allocator.FreeStorageBuffer(_warpNodeBuffers[i]);
            _allocator.FreeStorageBuffer(_modifierNodeBuffers[i]);
            _allocator.FreeStorageBuffer(_latticeBuffers[i]);
            _allocator.FreeStorageBuffer(_headerBuffers[i]);
            _allocator.FreeStorageBuffer(_pointBuffers[i]);
            _allocator.FreeStorageBuffer(_lengthBuffers[i]);
            _allocator.FreeStorageBuffer(_gradientStopBuffers[i]);
            _allocator.FreeStorageBuffer(_tileOffsetsBuffers[i]);
            _allocator.FreeStorageBuffer(_tileIndicesBuffers[i]);
        }
        _log.Debug("SdfBuffer disposed");
    }
}
