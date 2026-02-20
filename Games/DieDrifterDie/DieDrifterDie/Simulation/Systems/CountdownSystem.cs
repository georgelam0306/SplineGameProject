using DieDrifterDie.Simulation.Components;

namespace DieDrifterDie.Simulation.Systems.SimTable;

/// <summary>
/// Ticks the countdown timer during pre-game countdown phase.
/// Decrements FramesRemaining each tick until it reaches 0.
/// Transitions MatchState from Countdown to Playing when countdown finishes.
/// </summary>
public sealed class CountdownSystem : SimTableSystem
{
    public const int CountdownDuration = SimulationConfig.TickRate * 3; // 3 seconds

    public CountdownSystem(SimWorld world) : base(world)
    {
    }

    public override void Tick(in SimulationContext context)
    {
        if (!World.CountdownStateRows.TryGetRow(0, out var countdownRow)) return;

        if (countdownRow.FramesRemaining > 0)
        {
            countdownRow.FramesRemaining--;

            // When countdown reaches 0, transition to Playing
            if (countdownRow.FramesRemaining == 0)
            {
                if (World.GameRulesStateRows.TryGetRow(0, out var rulesRow))
                {
                    rulesRow.MatchState = MatchState.Playing;
                }
            }
        }
    }
}
