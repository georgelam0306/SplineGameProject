namespace DieDrifterDie.Infrastructure.Networking;

/// <summary>
/// Network message types for DieDrifterDie P2P mesh multiplayer.
/// </summary>
public enum NetMessageType : byte
{
    // Lobby synchronization (coordinator-authoritative)
    LobbySync = 1,       // Coordinator broadcasts full lobby state
    ClientJoin = 2,      // Peer sends join request with DisplayName
    ClientLeave = 3,     // Peer leaving lobby
    Ready = 4,           // Player ready state change

    // Game flow
    MatchStart = 5,      // Coordinator signals all ready, begin loading
    LoadComplete = 6,    // Peer finished loading
    StartCountdown = 7,  // Coordinator signals all loaded, begin countdown

    // Gameplay (direct P2P input exchange)
    Input = 10,          // Player input for a frame (sent to all peers)
    InputAck = 11,       // Acknowledgment of received input
    SyncCheck = 12,      // State hash for desync detection
    DesyncNotify = 13,   // Notify all peers a desync was detected

    // P2P Mesh establishment
    PeerList = 20,           // Coordinator → All: list of peer endpoints to connect to
    PeerHello = 21,          // Peer → Peer: introduce self after connecting
    PeerAck = 22,            // Peer → Peer: acknowledge connection established
    MeshReady = 23,          // Peer → All: this peer is fully connected to all others

    // Coordinator election
    CoordinatorAnnounce = 30, // New coordinator announces takeover after election

    // Game restart
    RestartReady = 40,        // Player is ready for restart after game over

    // Dev-only: Hot-reload game data sync
    GameDataReload = 50,      // Coordinator broadcasts target frame for synchronized data reload
}
