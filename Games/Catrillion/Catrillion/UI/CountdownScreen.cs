using Raylib_cs;
using Catrillion.AppState;
using Catrillion.Simulation;

namespace Catrillion.UI;

/// <summary>
/// Countdown screen shown before game starts (3-2-1-GO!).
/// Publishes CountdownComplete event when countdown finishes.
/// </summary>
public sealed class CountdownScreen
{
    private readonly RootStore _store;
    private readonly AppEventBus _eventBus;

    public CountdownScreen(RootStore store, AppEventBus eventBus)
    {
        _store = store;
        _eventBus = eventBus;
    }

    public void OnEnter()
    {
    }

    public void OnExit()
    {
    }

    public void Update(float deltaTime)
    {
        var game = _store.Game.CurrentGame;
        if (game == null) return;

        // Run the full game loop with proper rollback support
        // This ensures countdown state is synchronized across clients
        game.Update(deltaTime);

        // Check for completion
        if (game.GameSimulation.GetCountdownFrames() <= 0)
        {
            _eventBus.PublishCountdownComplete();
        }
    }

    public void Draw(float deltaTime)
    {
        // Draw the game frozen in background
        _store.Game.CurrentGame?.Draw(deltaTime);

        int screenWidth = Raylib.GetScreenWidth();
        int screenHeight = Raylib.GetScreenHeight();

        // Semi-transparent background overlay
        Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(0, 0, 0, 180));

        // Countdown number - read from simulation
        int frames = _store.Game.CurrentGame?.GameSimulation.GetCountdownFrames() ?? 0;
        int seconds = (frames + SimulationConfig.TickRate - 1) / SimulationConfig.TickRate; // Ceiling division
        ImmediateModeUI.DrawCountdown(seconds);

        // "Get Ready" text
        ImmediateModeUI.DrawCenteredText("GET READY!", screenHeight / 2 + 100, 32, ImmediateModeUI.TextColor);
    }
}
