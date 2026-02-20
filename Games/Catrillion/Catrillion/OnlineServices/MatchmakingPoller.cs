using System;
using Catrillion.AppState;
using Networking;

namespace Catrillion.OnlineServices;

/// <summary>
/// Handles infrastructure polling for matchmaking services.
/// Polls the matchmaking coordinator for network events and sends heartbeats.
/// Called from Application update loop when on Matchmaking screen.
/// </summary>
public sealed class MatchmakingPoller : IDisposable
{
    private readonly MatchmakingCoordinator _coordinator;
    private readonly RootStore _store;
    private float _heartbeatTimer;

    private const float HeartbeatInterval = 5.0f;

    public MatchmakingPoller(MatchmakingCoordinator coordinator, RootStore store)
    {
        _coordinator = coordinator;
        _store = store;
    }

    /// <summary>
    /// Update polling and heartbeat. Called every frame from Application.
    /// Polls on Matchmaking screen, and sends heartbeats on Lobby screen if Orleans session is active.
    /// </summary>
    public void Update(float deltaTime)
    {
        var currentScreen = _store.App.CurrentScreen.Value;
        var matchmakingState = _store.Matchmaking.State.Value;

        // On Matchmaking screen - full polling
        if (currentScreen == ApplicationState.Matchmaking)
        {
            _coordinator.Poll();

            // Send heartbeat while in lobby waiting for Start Hosting
            if (matchmakingState == MatchmakingState.InLobby)
            {
                _heartbeatTimer += deltaTime;
                if (_heartbeatTimer >= HeartbeatInterval)
                {
                    _heartbeatTimer = 0;
                    _coordinator.SendHeartbeat();
                }
            }
            else
            {
                _heartbeatTimer = 0;
            }
        }
        // On Lobby screen with active Orleans session - heartbeats only
        else if (currentScreen == ApplicationState.Lobby && matchmakingState == MatchmakingState.InLobby)
        {
            _coordinator.Poll();
            _heartbeatTimer += deltaTime;
            if (_heartbeatTimer >= HeartbeatInterval)
            {
                _heartbeatTimer = 0;
                _coordinator.SendHeartbeat();
            }
        }
        else
        {
            _heartbeatTimer = 0;
        }
    }

    public void Dispose()
    {
        // Nothing to dispose - coordinator is owned by DI container
    }
}
