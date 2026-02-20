using System;
using R3;

namespace DieDrifterDie.GameApp.AppState;

/// <summary>
/// Network connection states.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected
}

/// <summary>
/// Network-specific state store for P2P mesh networking.
/// Tracks connection status, coordinator role, and mesh state.
/// </summary>
public sealed class NetworkStore : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    /// <summary>
    /// Current connection state.
    /// </summary>
    public ReactiveProperty<ConnectionState> State { get; }

    /// <summary>
    /// The player slot of the current coordinator.
    /// Coordinator handles lobby state and can change via election.
    /// </summary>
    public ReactiveProperty<int> CoordinatorSlot { get; }

    /// <summary>
    /// Local player's assigned slot.
    /// </summary>
    public ReactiveProperty<int> LocalSlot { get; }

    /// <summary>
    /// Whether this is a single-player (local-only) session.
    /// </summary>
    public ReactiveProperty<bool> IsSinglePlayer { get; }

    /// <summary>
    /// Number of connected peers in the mesh (excluding self).
    /// </summary>
    public ReactiveProperty<int> ConnectedPeerCount { get; }

    /// <summary>
    /// Total expected peers in the mesh (including self).
    /// </summary>
    public ReactiveProperty<int> ExpectedPeerCount { get; }

    /// <summary>
    /// Whether the mesh is fully connected (all peers connected to all others).
    /// </summary>
    public ReactiveProperty<bool> IsMeshComplete { get; }

    /// <summary>
    /// Whether we are waiting for mesh to complete (after receiving PeerList, before all connections established).
    /// Used to keep UI on Matchmaking screen until peer-to-peer NAT punches finish.
    /// </summary>
    public ReactiveProperty<bool> IsWaitingForMesh { get; }

    /// <summary>
    /// Bootstrap coordinator address for initial connection (null when we are coordinator).
    /// </summary>
    public string? CoordinatorAddress { get; set; }

    /// <summary>
    /// Port for connections.
    /// </summary>
    public int Port { get; set; } = 7778;

    /// <summary>
    /// Whether the local player is currently the coordinator.
    /// </summary>
    public bool IsCoordinator => CoordinatorSlot.Value == LocalSlot.Value && LocalSlot.Value >= 0;

    public NetworkStore()
    {
        State = new ReactiveProperty<ConnectionState>(ConnectionState.Disconnected);
        CoordinatorSlot = new ReactiveProperty<int>(0); // Slot 0 starts as coordinator
        LocalSlot = new ReactiveProperty<int>(-1); // -1 = not assigned yet
        IsSinglePlayer = new ReactiveProperty<bool>(false);
        ConnectedPeerCount = new ReactiveProperty<int>(0);
        ExpectedPeerCount = new ReactiveProperty<int>(1);
        IsMeshComplete = new ReactiveProperty<bool>(false);
        IsWaitingForMesh = new ReactiveProperty<bool>(false);

        _disposables.Add(State);
        _disposables.Add(CoordinatorSlot);
        _disposables.Add(LocalSlot);
        _disposables.Add(IsSinglePlayer);
        _disposables.Add(ConnectedPeerCount);
        _disposables.Add(ExpectedPeerCount);
        _disposables.Add(IsMeshComplete);
        _disposables.Add(IsWaitingForMesh);
    }

    /// <summary>
    /// Reset to disconnected state.
    /// </summary>
    public void Reset()
    {
        State.Value = ConnectionState.Disconnected;
        CoordinatorSlot.Value = 0;
        LocalSlot.Value = -1;
        IsSinglePlayer.Value = false;
        ConnectedPeerCount.Value = 0;
        ExpectedPeerCount.Value = 1;
        IsMeshComplete.Value = false;
        IsWaitingForMesh.Value = false;
        CoordinatorAddress = null;
    }

    /// <summary>
    /// Set up as the bootstrap coordinator (first player to host).
    /// </summary>
    public void SetupAsCoordinator(int port = 7778)
    {
        State.Value = ConnectionState.Connected;
        CoordinatorSlot.Value = 0;
        LocalSlot.Value = 0;
        IsSinglePlayer.Value = false;
        ConnectedPeerCount.Value = 0;
        ExpectedPeerCount.Value = 1;
        IsMeshComplete.Value = true; // Solo coordinator is "complete"
        CoordinatorAddress = null;
        Port = port;
    }

    /// <summary>
    /// Set up for single-player (local coordinator, no network).
    /// </summary>
    public void SetupAsSinglePlayer()
    {
        State.Value = ConnectionState.Connected;
        CoordinatorSlot.Value = 0;
        LocalSlot.Value = 0;
        IsSinglePlayer.Value = true;
        ConnectedPeerCount.Value = 0;
        ExpectedPeerCount.Value = 1;
        IsMeshComplete.Value = true;
        CoordinatorAddress = null;
    }

    /// <summary>
    /// Set up as a joining peer connecting to bootstrap coordinator.
    /// </summary>
    public void SetupAsJoiningPeer(string coordinatorAddress, int port = 7778)
    {
        State.Value = ConnectionState.Connecting;
        CoordinatorSlot.Value = 0; // Assume slot 0 is coordinator until told otherwise
        LocalSlot.Value = -1; // Will be assigned by coordinator
        IsSinglePlayer.Value = false;
        ConnectedPeerCount.Value = 0;
        ExpectedPeerCount.Value = 1;
        IsMeshComplete.Value = false;
        CoordinatorAddress = coordinatorAddress;
        Port = port;
    }

    /// <summary>
    /// Mark connection as established.
    /// </summary>
    public void MarkConnected()
    {
        State.Value = ConnectionState.Connected;
    }

    /// <summary>
    /// Mark as disconnected.
    /// </summary>
    public void MarkDisconnected()
    {
        State.Value = ConnectionState.Disconnected;
        ConnectedPeerCount.Value = 0;
        IsMeshComplete.Value = false;
    }

    /// <summary>
    /// Update connected peer count.
    /// </summary>
    public void SetPeerCount(int count)
    {
        ConnectedPeerCount.Value = count;
    }

    /// <summary>
    /// Update the coordinator slot (after election).
    /// </summary>
    public void SetCoordinator(int slot)
    {
        CoordinatorSlot.Value = slot;
    }

    /// <summary>
    /// Set local player slot (assigned by coordinator).
    /// </summary>
    public void SetLocalSlot(int slot)
    {
        LocalSlot.Value = slot;
    }

    /// <summary>
    /// Mark mesh as complete (all peers connected to all others).
    /// </summary>
    public void SetMeshComplete(bool complete)
    {
        IsMeshComplete.Value = complete;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
