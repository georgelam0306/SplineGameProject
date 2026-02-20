namespace Networking;

/// <summary>
/// Infrastructure message types for P2P mesh networking.
/// These are handled by NetworkService for connection management,
/// peer discovery, and coordinator election.
///
/// Game-specific message types (e.g., Input, LobbySync) should be
/// defined in each game's NetMessageType enum with values that don't
/// conflict with these infrastructure types.
/// </summary>
public enum InfraMessageType : byte
{
    // Join/Ready flow
    ClientJoin = 2,      // Peer sends join request
    Ready = 4,           // Player ready state change

    // Game flow (managed by NetworkService)
    MatchStart = 5,      // Coordinator signals all ready, begin loading
    LoadComplete = 6,    // Peer finished loading
    StartCountdown = 7,  // Coordinator signals all loaded, begin countdown

    // Sync checking (handled by NetworkService)
    SyncCheck = 12,      // State hash for desync detection
    DesyncNotify = 13,   // Notify all peers a desync was detected

    // P2P Mesh establishment
    PeerList = 20,       // Coordinator → All: list of peer endpoints
    PeerHello = 21,      // Peer → Peer: introduce self after connecting
    PeerAck = 22,        // Peer → Peer: acknowledge connection established
    MeshReady = 23,      // Peer → All: this peer is fully connected to all others

    // Coordinator election
    CoordinatorAnnounce = 30, // New coordinator announces takeover

    // Game restart
    RestartReady = 40,   // Player is ready for restart after game over
}
