using Catrillion.Rollback;
using DerpTech.Rollback;

namespace Catrillion.Simulation;

public readonly ref struct SimulationContext
{
    public readonly int CurrentFrame;
    public readonly int PlayerCount;
    public readonly int SessionSeed;
    private readonly MultiPlayerInputBuffer<GameInput> _inputBuffer;

    public SimulationContext(int currentFrame, int playerCount, int sessionSeed, MultiPlayerInputBuffer<GameInput> inputBuffer)
    {
        CurrentFrame = currentFrame;
        PlayerCount = playerCount;
        SessionSeed = sessionSeed;
        _inputBuffer = inputBuffer;
    }

    public ref readonly GameInput GetInput(int playerId)
    {
        return ref _inputBuffer.GetInput(CurrentFrame, playerId);
    }
}
