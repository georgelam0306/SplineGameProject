using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Schedules and triggers wave events.
/// Manages horde warnings, horde spawns, and mini wave spawns.
/// </summary>
public sealed class WaveSchedulerSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;
    private readonly WaveConfigData _config;
    private bool _initialized;

    // Deterministic random salts
    private const int SaltHordeVariance = 0x48524456;   // "HRDV"
    private const int SaltMiniVariance = 0x4D494E56;    // "MINV"
    private const int SaltHordeDirection = 0x48524444;  // "HRDD"

    public WaveSchedulerSystem(
        SimWorld world,
        GameDataManager<GameDocDb> gameData) : base(world)
    {
        _gameData = gameData;
        _config = gameData.Db.WaveConfigData.FindById(0);
    }

    public override void Tick(in SimulationContext context)
    {
        // Only run while playing
        if (!World.IsPlaying()) return;

        if (!World.WaveStateRows.TryGetRow(0, out var state)) return;

        // Initialize wave schedule on first tick
        if (!_initialized)
        {
            // Get the frame when the match started
            int matchStartFrame = 0;
            if (World.GameRulesStateRows.TryGetRow(0, out var rulesRow))
            {
                matchStartFrame = rulesRow.FrameMatchStarted;
            }

            // Schedule first horde wave
            int firstHordeFrame = matchStartFrame + (_config.HordeDayInterval * _config.FramesPerDay);
            int hordeVariance = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, 0, 0, SaltHordeVariance,
                -_config.HordeVarianceFrames, _config.HordeVarianceFrames);
            state.NextHordeFrame = firstHordeFrame + hordeVariance;
            state.NextHordeWarningFrame = state.NextHordeFrame - _config.HordeWarningFrames;

            // Schedule first mini wave
            int firstMiniFrame = matchStartFrame + _config.MiniWaveIntervalFrames;
            int miniVariance = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, 0, 0, SaltMiniVariance,
                -_config.MiniWaveVarianceFrames, _config.MiniWaveVarianceFrames);
            state.NextMiniWaveFrame = firstMiniFrame + miniVariance;

            // Initialize wave counters
            state.CurrentHordeWave = 0;
            state.CurrentMiniWave = 0;
            state.MaxWaveZombieCount = 500;

            _initialized = true;
        }

        // Update warning countdown
        if (state.Flags.HasFlag(WaveStateFlags.WarningActive))
        {
            state.WarningCountdownFrames--;
            if (state.WarningCountdownFrames <= 0)
            {
                // Warning expired, start the horde
                state.Flags &= ~WaveStateFlags.WarningActive;
                state.Flags |= WaveStateFlags.HordeActive;
                state.HordeSpawnedCount = 0;
                state.HordeSpawnCooldown = 0;
                state.CurrentHordeWave++;

                // Schedule next horde (unless this was final)
                if (!state.Flags.HasFlag(WaveStateFlags.IsFinalWave))
                {
                    int nextHordeFrame = context.CurrentFrame + (_config.HordeDayInterval * _config.FramesPerDay);
                    int variance = DeterministicRandom.RangeWithSeed(
                        context.SessionSeed, context.CurrentFrame, state.CurrentHordeWave, SaltHordeVariance,
                        -_config.HordeVarianceFrames, _config.HordeVarianceFrames);
                    state.NextHordeFrame = nextHordeFrame + variance;
                    state.NextHordeWarningFrame = state.NextHordeFrame - _config.HordeWarningFrames;
                }
                else
                {
                    state.NextHordeFrame = int.MaxValue;
                    state.NextHordeWarningFrame = int.MaxValue;
                }
            }
        }

        // Check for horde warning trigger
        if (!state.Flags.HasFlag(WaveStateFlags.WarningActive) &&
            !state.Flags.HasFlag(WaveStateFlags.HordeActive) &&
            context.CurrentFrame >= state.NextHordeWarningFrame &&
            state.NextHordeWarningFrame > 0)
        {
            int waveNumber = state.CurrentHordeWave + 1;
            bool isFinalWave = _config.FinalWaveNumber > 0 && waveNumber >= _config.FinalWaveNumber;

            byte direction;
            if (isFinalWave)
            {
                direction = 4;
                state.Flags |= WaveStateFlags.IsFinalWave;
            }
            else
            {
                direction = (byte)DeterministicRandom.RangeWithSeed(
                    context.SessionSeed, context.CurrentFrame, waveNumber, SaltHordeDirection,
                    0, 4);
            }

            state.Flags |= WaveStateFlags.WarningActive;
            state.WarningCountdownFrames = _config.HordeWarningFrames;
            state.WarningDirection = direction;
            state.HordeDirection = direction;

            int baseCount = _config.HordeBaseZombieCount + (waveNumber * _config.HordeZombiesPerWave);
            if (isFinalWave)
            {
                baseCount = baseCount * _config.FinalWaveMultiplierPercent / 100;
            }
            state.HordeZombieCount = baseCount;
        }

        // Check for mini wave trigger
        if (!state.Flags.HasFlag(WaveStateFlags.MiniWaveActive) &&
            context.CurrentFrame >= state.NextMiniWaveFrame &&
            state.NextMiniWaveFrame > 0)
        {
            state.Flags |= WaveStateFlags.MiniWaveActive;

            int waveNumber = state.CurrentMiniWave + 1;
            int zombieCount = _config.MiniWaveBaseCount + (waveNumber * _config.MiniWaveCountPerWave);
            state.MiniWaveZombieCount = zombieCount;
            state.MiniWaveSpawnedCount = 0;
            state.MiniWaveSpawnCooldown = 0;
            state.CurrentMiniWave++;

            int variance = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, context.CurrentFrame, state.CurrentMiniWave, SaltMiniVariance,
                -_config.MiniWaveVarianceFrames, _config.MiniWaveVarianceFrames);
            state.NextMiniWaveFrame = context.CurrentFrame + _config.MiniWaveIntervalFrames + variance;
        }
    }
}
