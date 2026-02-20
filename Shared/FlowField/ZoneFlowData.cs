using Core;
using Pooled;

namespace FlowField;

[Pooled(SoA = true, GenerateStableId = true, RefCounting = RefCountKind.None)]
public partial struct ZoneFlowData
{
    [Column] public FlowCell[] FlowCells;
    [Column] public Fixed64[] Distances;
    [Column] public int ZoneId;
    [Column] public int SectorX;
    [Column] public int SectorY;
    [Column] public bool IsComplete;
}
