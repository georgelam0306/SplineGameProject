using System.Collections.Generic;
using System.Numerics;

namespace Derp.UI;

internal sealed class StateMachineEditorState
{
    public int ActiveStateMachineId;
    public int ActiveLayerIndex;

    public bool SnappingEnabled = true;

    public float Zoom = 1f;
    public Vector2 Pan = Vector2.Zero;

    public readonly List<StateMachineGraphNodeRef> SelectedNodes = new(capacity: 32);
    public readonly List<Vector2> SelectedNodeDragStartPositions = new(capacity: 32);
    public int SelectedTransitionId;
    public int HoveredTransitionId;

    public bool IsDraggingNodes;
    public Vector2 NodeDragStartMouseGraph;

    public bool IsMarqueeActive;
    public Vector2 MarqueeStartGraph;
    public Vector2 MarqueeEndGraph;

    public bool IsPanning;
    public Vector2 PanStartMouseScreen;
    public Vector2 PanStartValue;

    public ConnectDragState ConnectDrag;

    public struct ConnectDragState
    {
        public bool Active;
        public StateMachineGraphNodeRef Source;
        public Vector2 StartGraph;
        public StateMachineGraphNodeRef SnapTarget;
        public Vector2 SnapTargetPortGraph;
        public bool HasSnapTarget;
    }
}
