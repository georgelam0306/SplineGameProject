using DerpLib.Ecs;
using GyrussClone.Simulation.Ecs;

namespace GyrussClone.Core;

sealed class GameManager
{
    private readonly GameComposition.Factory _gameFactory;
    private GameComposition? _scope;

    public GameManager(GameComposition.Factory gameFactory)
    {
        _gameFactory = gameFactory;
    }

    public SimEcsWorld World => _scope!.World;
    public EcsSystemPipeline<SimEcsWorld> Pipeline => _scope!.Pipeline;

    public void StartGame(int sessionSeed)
    {
        _scope?.Dispose();
        _scope = _gameFactory.Create(sessionSeed);
    }
}
