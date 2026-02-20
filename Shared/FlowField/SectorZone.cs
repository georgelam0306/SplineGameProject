using System.Collections.Generic;
using Core;

namespace FlowField;

public struct SectorZone
{
    public int ZoneId;
    public int SectorX;
    public int SectorY;
    public int ZoneIndex;
}

public struct ZonePortal
{
    public int PortalId;
    public int StartTileX;
    public int StartTileY;
    public int EndTileX;
    public int EndTileY;
    public int FromZoneId;
    public int ToZoneId;

    public int CenterTileX => (StartTileX + EndTileX) / 2;
    public int CenterTileY => (StartTileY + EndTileY) / 2;
}

public struct ZoneSector
{
    public int SectorX;
    public int SectorY;
    public int MinTileX;
    public int MinTileY;
    public int MaxTileX;
    public int MaxTileY;
    public List<int> ZoneIds;
    public int[] TileZoneIndices;
    public Fixed64[] WallDistances;
}
