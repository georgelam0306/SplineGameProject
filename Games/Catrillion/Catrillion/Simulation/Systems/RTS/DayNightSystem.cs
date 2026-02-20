using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Tracks in-game day/time progression.
/// Increments day counter when FramesSinceDayStart reaches FramesPerDay.
/// </summary>
public sealed class DayNightSystem : SimTableSystem
{
    private readonly int _framesPerDay;

    public DayNightSystem(
        SimWorld world,
        GameDataManager<GameDocDb> gameData) : base(world)
    {
        // Load wave config for frames per day
        ref readonly var waveConfig = ref gameData.Db.WaveConfigData.FindById(0);
        _framesPerDay = waveConfig.FramesPerDay;
    }

    public override void Tick(in SimulationContext context)
    {
        // Only run while playing
        if (!World.IsPlaying()) return;

        if (!World.WaveStateRows.TryGetRow(0, out var state)) return;

        // Initialize on first frame
        if (state.CurrentDay == 0)
        {
            state.CurrentDay = 1;
            state.FramesSinceDayStart = 0;
            state.Flags |= WaveStateFlags.IsActive;
        }

        // Increment frame counter
        state.FramesSinceDayStart++;

        // Check for day transition
        if (state.FramesSinceDayStart >= _framesPerDay)
        {
            state.FramesSinceDayStart = 0;
            state.CurrentDay++;
        }
    }
}
