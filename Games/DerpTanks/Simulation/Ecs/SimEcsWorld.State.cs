using System;
using System.Collections.Generic;
using DerpLib.Ecs;
using FlowField;
using FixedMath;

namespace DerpTanks.Simulation.Ecs;

public sealed partial class SimEcsWorld
{
    public Fixed64 DeltaTime;

    public Fixed64Vec2 PlayerPosition;

    public Fixed64Vec2 PlayerForward;

    public bool FireRequested;

    public int CurrentFrame;

    public int SessionSeed;

    public int CurrentWave;

    public int NextWaveFrame;

    public int WaveSpawnRemaining;

    public IZoneFlowService FlowService = null!;

    public Fixed64 TileSizeFixed;

    public int CurrentSeedTileX;

    public int CurrentSeedTileY;

    public int NextFireFrame;

    public int FireCooldownFrames;

    public Fixed64 ProjectileSpeed;

    public Fixed64 ProjectileLifetime;

    public Fixed64 ProjectileContactRadius;

    public Fixed64 ProjectileExplosionRadius;

    public int ProjectileDamage;

    public int DebugLastShotFrame;

    public uint DebugLastShotRawId;

    public Fixed64Vec2 DebugLastShotPosition;

    public Fixed64Vec2 DebugLastShotDirection;

    private readonly List<(int tileX, int tileY, Fixed64 cost)> _seedBuffer = new(1);

    private EntityHandle[] _queryBuffer = Array.Empty<EntityHandle>();

    private int[] _separationDensity = Array.Empty<int>();

    private int[] _separationBlurBuffer = Array.Empty<int>();

    private long[] _separationGradientX = Array.Empty<long>();

    private long[] _separationGradientY = Array.Empty<long>();

    public List<(int tileX, int tileY, Fixed64 cost)> SeedBuffer => _seedBuffer;

    public Span<EntityHandle> QueryBuffer => _queryBuffer;

    public int[] SeparationDensity => _separationDensity;

    public int[] SeparationBlurBuffer => _separationBlurBuffer;

    public long[] SeparationGradientX => _separationGradientX;

    public long[] SeparationGradientY => _separationGradientY;

    public void Initialize(IZoneFlowService flowService, int sessionSeed, int tileSize, int queryBufferSize)
    {
        FlowService = flowService ?? throw new ArgumentNullException(nameof(flowService));
        SessionSeed = sessionSeed;
        TileSizeFixed = Fixed64.FromInt(tileSize);

        if (queryBufferSize < 1)
        {
            queryBufferSize = 1;
        }

        if (_queryBuffer.Length != queryBufferSize)
        {
            _queryBuffer = new EntityHandle[queryBufferSize];
        }

        if (_separationDensity.Length != Simulation.HordeSeparationGrid.TotalCells)
        {
            _separationDensity = new int[Simulation.HordeSeparationGrid.TotalCells];
            _separationBlurBuffer = new int[Simulation.HordeSeparationGrid.TotalCells];
            _separationGradientX = new long[Simulation.HordeSeparationGrid.TotalCells];
            _separationGradientY = new long[Simulation.HordeSeparationGrid.TotalCells];
        }

        _seedBuffer.Clear();
        _seedBuffer.Capacity = Math.Max(_seedBuffer.Capacity, 1);

        CurrentFrame = 0;
        CurrentWave = 0;
        NextWaveFrame = 0;
        WaveSpawnRemaining = 0;
        CurrentSeedTileX = int.MinValue;
        CurrentSeedTileY = int.MinValue;

        PlayerForward = new Fixed64Vec2(Fixed64.Zero, Fixed64.OneValue);
        FireRequested = false;
        NextFireFrame = 0;

        // Weapon tuning (simulation)
        FireCooldownFrames = 8; // ~7.5 shots/sec at 60fps
        ProjectileSpeed = Fixed64.FromInt(45);
        ProjectileLifetime = Fixed64.FromFloat(1.5f);
        ProjectileContactRadius = Fixed64.FromFloat(0.8f);
        ProjectileExplosionRadius = Fixed64.FromInt(8);
        ProjectileDamage = 1;

        DebugLastShotFrame = -1;
        DebugLastShotRawId = 0;
        DebugLastShotPosition = Fixed64Vec2.Zero;
        DebugLastShotDirection = Fixed64Vec2.Zero;
    }
}
