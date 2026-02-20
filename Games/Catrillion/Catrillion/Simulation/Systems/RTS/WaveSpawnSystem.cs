using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using Core;

namespace Catrillion.Simulation.Systems.RTS;

/// <summary>
/// Spawns zombies during active wave events.
/// Handles both horde waves (single direction) and mini waves (random edges).
/// </summary>
public sealed class WaveSpawnSystem : SimTableSystem
{
    private readonly ZombieSpawnerService _zombieSpawner;
    private readonly TerrainDataService _terrainData;
    private readonly WaveConfigData _config;

    // Map dimensions
    private readonly int _mapWidth;
    private readonly int _mapHeight;
    private readonly int _tileSize;

    // Spawn rate (zombies per frame)
    private readonly int _hordeSpawnRate;

    // Deterministic random salts
    private const int SaltSpawnX = 0x57535058;      // "WSPX"
    private const int SaltSpawnY = 0x57535059;      // "WSPY"
    private const int SaltMiniDir = 0x4D494E44;     // "MIND"

    public WaveSpawnSystem(
        SimWorld world,
        ZombieSpawnerService zombieSpawner,
        TerrainDataService terrainData,
        GameDataManager<GameDocDb> gameData) : base(world)
    {
        _zombieSpawner = zombieSpawner;
        _terrainData = terrainData;
        _config = gameData.Db.WaveConfigData.FindById(0);

        // Load map dimensions
        ref readonly var mapConfig = ref gameData.Db.MapConfigData.FindById(0);
        _tileSize = mapConfig.TileSize;
        _mapWidth = mapConfig.WidthTiles * _tileSize;
        _mapHeight = mapConfig.HeightTiles * _tileSize;
        _hordeSpawnRate = _config.HordeSpawnRate > 0 ? _config.HordeSpawnRate : 5;
    }

    public override void Tick(in SimulationContext context)
    {
        // Only run while playing
        if (!World.IsPlaying()) return;

        if (!World.WaveStateRows.TryGetRow(0, out var state)) return;

        // Process horde spawning
        if (state.Flags.HasFlag(WaveStateFlags.HordeActive))
        {
            // Convert all existing zombies to WaveChase on final wave
            if (state.Flags.HasFlag(WaveStateFlags.IsFinalWave))
            {
                ConvertAllZombiesToWaveChase(ref state);
            }

            if (state.HordeSpawnedCount >= state.HordeZombieCount)
            {
                state.Flags &= ~WaveStateFlags.HordeActive;
            }
            else
            {
                int toSpawn = Math.Min(_hordeSpawnRate, state.HordeZombieCount - state.HordeSpawnedCount);

                for (int i = 0; i < toSpawn; i++)
                {
                    int spawnIndex = state.HordeSpawnedCount + i;

                    byte direction = state.HordeDirection;
                    if (direction == 4)
                    {
                        direction = (byte)(spawnIndex % 4);
                    }

                    var position = GetEdgeSpawnPosition(context, direction, spawnIndex);
                    var typeId = _zombieSpawner.SelectWeightedZombieType(context, spawnIndex, 31);
                    var handle = _zombieSpawner.SpawnZombie(context, position, typeId, ZombieState.WaveChase, spawnIndex);

                    // Mark as wave zombie so it returns to WaveChase after attacking
                    var zombie = World.ZombieRows.GetRow(handle);
                    zombie.Flags |= MortalFlags.IsWaveZombie;
                }

                state.HordeSpawnedCount += toSpawn;
                state.ActiveWaveZombieCount += toSpawn;
            }
        }

        // Process mini wave spawning
        if (state.Flags.HasFlag(WaveStateFlags.MiniWaveActive))
        {
            if (state.MiniWaveSpawnedCount >= state.MiniWaveZombieCount)
            {
                state.Flags &= ~WaveStateFlags.MiniWaveActive;
                // Keep UI visible for 3 seconds after spawning completes
                state.MiniWaveDisplayEndFrame = context.CurrentFrame + 180;
            }
            else
            {
                int toSpawn = Math.Min(2, state.MiniWaveZombieCount - state.MiniWaveSpawnedCount);

                for (int i = 0; i < toSpawn; i++)
                {
                    int spawnIndex = state.MiniWaveSpawnedCount + i;

                    byte direction = (byte)DeterministicRandom.RangeWithSeed(
                        context.SessionSeed, context.CurrentFrame, spawnIndex, SaltMiniDir,
                        0, 4);

                    var position = GetEdgeSpawnPosition(context, direction, spawnIndex + 10000);
                    var typeId = _zombieSpawner.SelectWeightedZombieType(context, spawnIndex, 7);
                    var handle = _zombieSpawner.SpawnZombie(context, position, typeId, ZombieState.WaveChase, spawnIndex);

                    // Mark as wave zombie so it returns to WaveChase after attacking
                    var zombie = World.ZombieRows.GetRow(handle);
                    zombie.Flags |= MortalFlags.IsWaveZombie;
                }

                state.MiniWaveSpawnedCount += toSpawn;
                state.ActiveWaveZombieCount += toSpawn;
            }
        }
    }

    private Fixed64Vec2 GetEdgeSpawnPosition(in SimulationContext context, byte direction, int spawnIndex)
    {
        int offset = _config.EdgeSpawnOffset;
        int spread = _config.EdgeSpawnSpread;
        const int maxAttempts = 10;

        // Try multiple positions to find passable terrain
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int edgeOffset = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, context.CurrentFrame, spawnIndex + attempt * 1000, SaltSpawnX,
                -spread, spread);

            int depthOffset = DeterministicRandom.RangeWithSeed(
                context.SessionSeed, context.CurrentFrame, spawnIndex + attempt * 1000, SaltSpawnY,
                0, offset);

            int x, y;
            switch (direction)
            {
                case 0: // North
                    x = _mapWidth / 2 + edgeOffset;
                    y = offset + depthOffset;
                    break;
                case 1: // East
                    x = _mapWidth - offset - depthOffset;
                    y = _mapHeight / 2 + edgeOffset;
                    break;
                case 2: // South
                    x = _mapWidth / 2 + edgeOffset;
                    y = _mapHeight - offset - depthOffset;
                    break;
                case 3: // West
                    x = offset + depthOffset;
                    y = _mapHeight / 2 + edgeOffset;
                    break;
                default:
                    x = _mapWidth / 2;
                    y = _mapHeight / 2;
                    break;
            }

            x = Math.Clamp(x, _tileSize, _mapWidth - _tileSize);
            y = Math.Clamp(y, _tileSize, _mapHeight - _tileSize);

            // Check if terrain is passable
            int tileX = x / _tileSize;
            int tileY = y / _tileSize;
            if (_terrainData.IsPassable(tileX, tileY))
            {
                return Fixed64Vec2.FromInt(x, y);
            }
        }

        // Fallback: spawn at edge center even if not passable (rare case)
        int fallbackX, fallbackY;
        switch (direction)
        {
            case 0: fallbackX = _mapWidth / 2; fallbackY = offset * 2; break;
            case 1: fallbackX = _mapWidth - offset * 2; fallbackY = _mapHeight / 2; break;
            case 2: fallbackX = _mapWidth / 2; fallbackY = _mapHeight - offset * 2; break;
            case 3: fallbackX = offset * 2; fallbackY = _mapHeight / 2; break;
            default: fallbackX = _mapWidth / 2; fallbackY = _mapHeight / 2; break;
        }
        return Fixed64Vec2.FromInt(fallbackX, fallbackY);
    }

    private void ConvertAllZombiesToWaveChase(ref WaveStateRowRowRef waveState)
    {
        // Only run once when final wave starts
        if (waveState.Flags.HasFlag(WaveStateFlags.FinalWaveActivated))
            return;

        waveState.Flags |= WaveStateFlags.FinalWaveActivated;

        var zombies = World.ZombieRows;
        for (int slot = 0; slot < zombies.Count; slot++)
        {
            if (!zombies.TryGetRow(slot, out var zombie)) continue;

            // Skip if already in WaveChase or mid-attack
            if (zombie.State == ZombieState.WaveChase || zombie.State == ZombieState.Attack)
                continue;

            zombie.State = ZombieState.WaveChase;
            zombie.Flags |= MortalFlags.IsWaveZombie;
            zombie.StateTimer = 0;
        }
    }
}
