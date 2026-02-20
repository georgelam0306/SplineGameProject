using System;
using R3;

namespace Catrillion.AppState;

/// <summary>
/// Lobby-specific reactive state store.
/// Manages lobby state and player ready/loaded status.
/// </summary>
public sealed class LobbyStore : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly AppStore _appStore;
    private readonly NetworkStore _networkStore;

    /// <summary>
    /// Current lobby state (immutable, replaced on changes).
    /// </summary>
    public ReactiveProperty<LobbyState> State { get; }

    /// <summary>
    /// Whether the game can be started (coordinator only, all clients ready).
    /// </summary>
    public ReadOnlyReactiveProperty<bool> CanStartGame { get; }

    public LobbyStore(AppStore appStore, NetworkStore networkStore)
    {
        _appStore = appStore;
        _networkStore = networkStore;
        State = new ReactiveProperty<LobbyState>(LobbyState.Empty);

        // Computed: Can start game when coordinator and all clients are ready
        CanStartGame = State
            .Select(lobby =>
                _networkStore.IsCoordinator &&
                lobby.PlayerCount > 0 &&
                lobby.AreAllClientsReady())
            .ToReadOnlyReactiveProperty();

        _disposables.Add(State);
        _disposables.Add(CanStartGame);
    }

    /// <summary>
    /// Creates a new lobby with the local client as coordinator.
    /// </summary>
    public void CreateAsHost()
    {
        _appStore.LocalClient.PlayerSlot = 0;
        State.Value = LobbyState.CreateWithHost(_appStore.LocalClient);
    }

    /// <summary>
    /// Joins a lobby as a peer (slot assigned by coordinator).
    /// </summary>
    public void JoinAsClient()
    {
        int slot = State.Value.GetNextAvailableSlot();
        if (slot < 0)
        {
            slot = State.Value.PlayerCount;
        }
        _appStore.LocalClient.PlayerSlot = slot;

        var clientInfo = new ConnectedClient(
            ClientId: _appStore.LocalClient.ClientId,
            PlayerSlot: slot,
            DisplayName: _appStore.LocalClient.DisplayName,
            IsReady: false,
            IsLoaded: false
        );

        State.Value = State.Value.WithClientAdded(clientInfo);
    }

    /// <summary>
    /// Toggle the local client's ready status.
    /// </summary>
    public void ToggleLocalReady()
    {
        var client = State.Value.GetClient(_appStore.LocalClient.ClientId);
        if (client.HasValue)
        {
            State.Value = State.Value.WithClientReady(_appStore.LocalClient.ClientId, !client.Value.IsReady);
        }
    }

    /// <summary>
    /// Set a client's ready status (used for remote clients).
    /// </summary>
    public void SetClientReady(Guid clientId, bool ready)
    {
        State.Value = State.Value.WithClientReady(clientId, ready);
    }

    /// <summary>
    /// Mark a client as loaded.
    /// </summary>
    public void MarkClientLoaded(Guid clientId)
    {
        State.Value = State.Value.WithClientLoaded(clientId, true);
    }

    /// <summary>
    /// Mark the local client as loaded.
    /// </summary>
    public void MarkLocalClientLoaded()
    {
        MarkClientLoaded(_appStore.LocalClient.ClientId);
    }

    /// <summary>
    /// Add a client to the lobby (used for remote clients joining).
    /// </summary>
    public void AddClient(ConnectedClient client)
    {
        State.Value = State.Value.WithClientAdded(client);
    }

    /// <summary>
    /// Remove a client from the lobby.
    /// </summary>
    public void RemoveClient(Guid clientId)
    {
        State.Value = State.Value.WithClientRemoved(clientId);
    }

    /// <summary>
    /// Replace the entire lobby state (used when receiving network sync).
    /// </summary>
    public void SetState(LobbyState newState)
    {
        State.Value = newState;
    }

    /// <summary>
    /// Reset to empty state.
    /// </summary>
    public void Clear()
    {
        State.Value = LobbyState.Empty;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
