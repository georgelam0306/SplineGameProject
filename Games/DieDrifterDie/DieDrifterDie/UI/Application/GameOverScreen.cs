using DieDrifterDie.GameApp.AppState;

namespace DieDrifterDie.Presentation.UI;

/// <summary>
/// Game over screen - placeholder for base template.
/// </summary>
public sealed class GameOverScreen
{
    private readonly RootStore _store;
    private readonly AppEventBus _eventBus;

    public GameOverScreen(RootStore store, AppEventBus eventBus)
    {
        _store = store;
        _eventBus = eventBus;
    }

    public void OnEnter() { }
    public void OnExit() { }
    public void Update(float deltaTime) { }
    public void Draw(float deltaTime) { }
}
