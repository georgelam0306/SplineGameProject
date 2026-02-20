using Core;
using DerpTech.Rollback;
using SimTable;

namespace BaseTemplate.Infrastructure.Rollback;

public record struct GameInput : IGameInput<GameInput>
{

    public static GameInput Empty => default;

    public bool IsEmpty => true;
}
