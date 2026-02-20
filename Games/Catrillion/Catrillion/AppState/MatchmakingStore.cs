using System;
using System.Collections.Generic;
using R3;
using Catrillion.OnlineServices;
using Networking;

namespace Catrillion.AppState;

/// <summary>
/// Matchmaking-specific state store.
/// Tracks lobby discovery state, available lobbies, and current lobby info.
/// </summary>
public sealed class MatchmakingStore : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    /// <summary>
    /// Current matchmaking state.
    /// </summary>
    public ReactiveProperty<MatchmakingState> State { get; }

    /// <summary>
    /// Error message when State == Error.
    /// </summary>
    public ReactiveProperty<string> ErrorMessage { get; }

    /// <summary>
    /// Available lobbies from the last refresh.
    /// </summary>
    public ReactiveProperty<IReadOnlyList<LobbyListItem>> AvailableLobbies { get; }

    /// <summary>
    /// Current lobby info (when in InLobby state).
    /// </summary>
    public ReactiveProperty<LobbyInfo?> CurrentLobby { get; }

    /// <summary>
    /// Whether a lobby list refresh is in progress.
    /// </summary>
    public ReactiveProperty<bool> IsRefreshing { get; }

    /// <summary>
    /// Players in the current lobby (updated via heartbeat).
    /// </summary>
    public ReactiveProperty<IReadOnlyList<LobbyPlayerInfo>> LobbyPlayers { get; }

    /// <summary>
    /// Whether local player is the host of the current lobby.
    /// </summary>
    public ReactiveProperty<bool> IsHost { get; }

    /// <summary>
    /// Whether local player was kicked from lobby.
    /// </summary>
    public ReactiveProperty<bool> WasKicked { get; }

    /// <summary>
    /// Whether local player is ready.
    /// </summary>
    public ReactiveProperty<bool> LocalPlayerReady { get; }

    /// <summary>
    /// Whether all players in the lobby are ready.
    /// </summary>
    public ReactiveProperty<bool> AllPlayersReady { get; }

    /// <summary>
    /// Whether the match has been started by the host.
    /// </summary>
    public ReactiveProperty<bool> MatchStarted { get; }

    /// <summary>
    /// Whether all players have established P2P mesh connectivity.
    /// </summary>
    public ReactiveProperty<bool> AllPlayersMeshConnected { get; }

    public MatchmakingStore()
    {
        State = new ReactiveProperty<MatchmakingState>(MatchmakingState.Idle);
        ErrorMessage = new ReactiveProperty<string>(string.Empty);
        AvailableLobbies = new ReactiveProperty<IReadOnlyList<LobbyListItem>>(Array.Empty<LobbyListItem>());
        CurrentLobby = new ReactiveProperty<LobbyInfo?>(null);
        IsRefreshing = new ReactiveProperty<bool>(false);
        LobbyPlayers = new ReactiveProperty<IReadOnlyList<LobbyPlayerInfo>>(Array.Empty<LobbyPlayerInfo>());
        IsHost = new ReactiveProperty<bool>(false);
        WasKicked = new ReactiveProperty<bool>(false);
        LocalPlayerReady = new ReactiveProperty<bool>(false);
        AllPlayersReady = new ReactiveProperty<bool>(false);
        MatchStarted = new ReactiveProperty<bool>(false);
        AllPlayersMeshConnected = new ReactiveProperty<bool>(false);

        _disposables.Add(State);
        _disposables.Add(ErrorMessage);
        _disposables.Add(AvailableLobbies);
        _disposables.Add(CurrentLobby);
        _disposables.Add(IsRefreshing);
        _disposables.Add(LobbyPlayers);
        _disposables.Add(IsHost);
        _disposables.Add(WasKicked);
        _disposables.Add(LocalPlayerReady);
        _disposables.Add(AllPlayersReady);
        _disposables.Add(MatchStarted);
        _disposables.Add(AllPlayersMeshConnected);
    }

    /// <summary>
    /// Update state from provider.
    /// </summary>
    public void SetState(MatchmakingState state)
    {
        State.Value = state;
    }

    /// <summary>
    /// Set error message.
    /// </summary>
    public void SetError(string message)
    {
        ErrorMessage.Value = message;
        State.Value = MatchmakingState.Error;
    }

    /// <summary>
    /// Clear error and return to idle.
    /// </summary>
    public void ClearError()
    {
        ErrorMessage.Value = string.Empty;
        State.Value = MatchmakingState.Idle;
    }

    /// <summary>
    /// Update available lobbies list.
    /// </summary>
    public void SetAvailableLobbies(IReadOnlyList<LobbyListItem> lobbies)
    {
        AvailableLobbies.Value = lobbies;
    }

    /// <summary>
    /// Set current lobby info.
    /// </summary>
    public void SetCurrentLobby(LobbyInfo? lobby)
    {
        CurrentLobby.Value = lobby;
    }

    /// <summary>
    /// Set refreshing state.
    /// </summary>
    public void SetRefreshing(bool refreshing)
    {
        IsRefreshing.Value = refreshing;
    }

    /// <summary>
    /// Set lobby players list (from heartbeat).
    /// </summary>
    public void SetLobbyPlayers(IReadOnlyList<LobbyPlayerInfo> players)
    {
        LobbyPlayers.Value = players;
    }

    /// <summary>
    /// Set whether local player is host.
    /// </summary>
    public void SetIsHost(bool isHost)
    {
        IsHost.Value = isHost;
    }

    /// <summary>
    /// Set whether local player was kicked.
    /// </summary>
    public void SetWasKicked(bool wasKicked)
    {
        WasKicked.Value = wasKicked;
    }

    /// <summary>
    /// Set local player's ready state.
    /// </summary>
    public void SetLocalPlayerReady(bool isReady)
    {
        LocalPlayerReady.Value = isReady;
    }

    /// <summary>
    /// Set whether all players are ready.
    /// </summary>
    public void SetAllPlayersReady(bool allReady)
    {
        AllPlayersReady.Value = allReady;
    }

    /// <summary>
    /// Set whether match has started.
    /// </summary>
    public void SetMatchStarted(bool started)
    {
        MatchStarted.Value = started;
    }

    /// <summary>
    /// Set whether all players have P2P mesh connectivity.
    /// </summary>
    public void SetAllPlayersMeshConnected(bool allConnected)
    {
        AllPlayersMeshConnected.Value = allConnected;
    }

    /// <summary>
    /// Reset to initial state.
    /// </summary>
    public void Reset()
    {
        State.Value = MatchmakingState.Idle;
        ErrorMessage.Value = string.Empty;
        AvailableLobbies.Value = Array.Empty<LobbyListItem>();
        CurrentLobby.Value = null;
        IsRefreshing.Value = false;
        LobbyPlayers.Value = Array.Empty<LobbyPlayerInfo>();
        IsHost.Value = false;
        WasKicked.Value = false;
        LocalPlayerReady.Value = false;
        AllPlayersReady.Value = false;
        MatchStarted.Value = false;
        AllPlayersMeshConnected.Value = false;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
