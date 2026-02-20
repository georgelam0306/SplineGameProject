using System;

namespace DieDrifterDie.GameApp.AppState;

/// <summary>
/// Root aggregator for all application datastores.
/// Single source of truth for all state, passed to ScreenManager and handlers.
/// </summary>
public sealed class RootStore : IDisposable
{
    /// <summary>
    /// App-level state: current screen, local client identity.
    /// </summary>
    public AppStore App { get; }

    /// <summary>
    /// Matchmaking-specific state: lobby discovery, available lobbies.
    /// </summary>
    public MatchmakingStore Matchmaking { get; }

    /// <summary>
    /// Lobby-specific state: player list, ready states, countdown.
    /// </summary>
    public LobbyStore Lobby { get; }

    /// <summary>
    /// Network connection state: connection status, host flag, peer count.
    /// </summary>
    public NetworkStore Network { get; }

    /// <summary>
    /// Game instance management: current Game, lifecycle control.
    /// </summary>
    public GameStore Game { get; }

    /// <summary>
    /// Gameplay UI state: build mode, selection preview (client-side, non-deterministic).
    /// </summary>
    public GameplayStore Gameplay { get; }

    public RootStore(
        AppStore appStore,
        MatchmakingStore matchmakingStore,
        NetworkStore networkStore,
        LobbyStore lobbyStore,
        GameStore gameStore,
        GameplayStore gameplayStore)
    {
        App = appStore;
        Matchmaking = matchmakingStore;
        Network = networkStore;
        Lobby = lobbyStore;
        Game = gameStore;
        Gameplay = gameplayStore;
    }

    public void Dispose()
    {
        Gameplay.Dispose();
        Game.Dispose();
        Network.Dispose();
        Lobby.Dispose();
        Matchmaking.Dispose();
        App.Dispose();
    }
}
