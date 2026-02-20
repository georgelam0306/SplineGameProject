namespace BaseTemplate.GameApp.Core;

/// <summary>
/// Abstraction for the main game loop runner.
/// Allows DI to swap between Main (production) and DebugMain (skip-menu dev mode).
/// </summary>
public interface IGameRunner
{
    void Run();
}
