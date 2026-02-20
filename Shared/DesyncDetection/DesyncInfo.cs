namespace DerpTech.DesyncDetection;

/// <summary>
/// Information about a detected desync between local and remote game state.
/// </summary>
public readonly struct DesyncInfo
{
    public readonly int Frame;
    public readonly ulong LocalHash;
    public readonly ulong RemoteHash;
    public readonly byte RemotePlayerId;

    public DesyncInfo(int frame, ulong localHash, ulong remoteHash, byte remotePlayerId)
    {
        Frame = frame;
        LocalHash = localHash;
        RemoteHash = remoteHash;
        RemotePlayerId = remotePlayerId;
    }
}

/// <summary>
/// A sync check result ready to be sent to peers.
/// </summary>
public readonly struct PendingSyncCheck
{
    public readonly int Frame;
    public readonly ulong Hash;

    public PendingSyncCheck(int frame, ulong hash)
    {
        Frame = frame;
        Hash = hash;
    }
}
