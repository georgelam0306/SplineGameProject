using LiteNetLib;

namespace Networking;

/// <summary>
/// Status of a peer connection in the mesh.
/// </summary>
public enum PeerStatus
{
    /// <summary>Peer is known but no connection attempt made yet.</summary>
    Unknown,

    /// <summary>Connection attempt in progress.</summary>
    Connecting,

    /// <summary>Successfully connected and handshake complete.</summary>
    Connected,

    /// <summary>Was connected but now disconnected.</summary>
    Disconnected
}

/// <summary>
/// Represents a peer in the P2P mesh network.
/// Tracks connection state and endpoint information for each player.
/// </summary>
public struct PeerInfo
{
    /// <summary>Unique identifier for this peer.</summary>
    public Guid ClientId;

    /// <summary>Assigned player slot (0 = coordinator, 1-7 = other players).</summary>
    public int PlayerSlot;

    /// <summary>LiteNetLib connection handle. Null if self or not connected.</summary>
    public NetPeer? Connection;

    /// <summary>IP address for connection/reconnection.</summary>
    public string EndpointAddress;

    /// <summary>Port for connection/reconnection.</summary>
    public int EndpointPort;

    /// <summary>Current connection status.</summary>
    public PeerStatus Status;

    /// <summary>Display name for UI.</summary>
    public string DisplayName;

    /// <summary>Whether this peer is ready to start the game.</summary>
    public bool IsReady;

    /// <summary>Whether this peer has finished loading.</summary>
    public bool IsLoaded;

    /// <summary>Whether this peer has reported mesh connectivity complete.</summary>
    public bool IsMeshReady;

    /// <summary>NAT punch token for peer-to-peer NAT traversal. Format: "{matchId}:{clientId}"</summary>
    public string NatToken;

    /// <summary>
    /// Returns true if this slot is occupied by a valid peer.
    /// </summary>
    public readonly bool IsValid => ClientId != Guid.Empty;

    /// <summary>
    /// Returns true if this peer is connected and ready for communication.
    /// </summary>
    public readonly bool IsConnected => Status == PeerStatus.Connected && Connection != null;

    /// <summary>
    /// Creates an empty/invalid peer info.
    /// </summary>
    public static PeerInfo Empty => new()
    {
        ClientId = Guid.Empty,
        PlayerSlot = -1,
        Connection = null,
        EndpointAddress = string.Empty,
        EndpointPort = 0,
        Status = PeerStatus.Unknown,
        DisplayName = string.Empty,
        IsReady = false,
        IsLoaded = false,
        IsMeshReady = false,
        NatToken = string.Empty
    };

    /// <summary>
    /// Creates a peer info for the local player (no connection needed).
    /// </summary>
    public static PeerInfo CreateLocal(Guid clientId, int playerSlot, string displayName, string natToken = "")
    {
        return new PeerInfo
        {
            ClientId = clientId,
            PlayerSlot = playerSlot,
            Connection = null, // Self - no connection
            EndpointAddress = string.Empty,
            EndpointPort = 0,
            Status = PeerStatus.Connected, // Local is always "connected"
            DisplayName = displayName,
            IsReady = false,
            IsLoaded = false,
            IsMeshReady = false,
            NatToken = natToken
        };
    }

    /// <summary>
    /// Creates a peer info for a remote peer we know about but haven't connected to yet.
    /// </summary>
    public static PeerInfo CreateRemote(Guid clientId, int playerSlot, string displayName, string address, int port, string natToken = "")
    {
        return new PeerInfo
        {
            ClientId = clientId,
            PlayerSlot = playerSlot,
            Connection = null,
            EndpointAddress = address,
            EndpointPort = port,
            Status = PeerStatus.Unknown,
            DisplayName = displayName,
            IsReady = false,
            IsLoaded = false,
            IsMeshReady = false,
            NatToken = natToken
        };
    }

    public override readonly string ToString()
    {
        return $"Peer[Slot={PlayerSlot}, Name={DisplayName}, Status={Status}]";
    }
}
