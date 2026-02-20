using System;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation.Components;
using ConfigRefresh;
using Core;

namespace Catrillion.Simulation.Systems.SimTable;

/// <summary>
/// Unified density-based separation system for all separable entities.
/// Uses multi-table query with chunked iteration for optimal performance (~0% overhead).
/// Applies Gaussian-blurred density gradients with EMA smoothing to reduce jitter.
/// Configuration loaded from GameDocDatabase (SeparationConfig table).
/// </summary>
[ConfigSource(typeof(SeparationConfigData), (int)SeparationConfigId.Default)]
public sealed partial class SeparationSystem : SimTableSystem
{
    private readonly GameDataManager<GameDocDb> _gameData;

    // Config values loaded from GameDocDatabase (refreshed automatically via generator)
    [CachedConfig] private int _gridSize;
    [CachedConfig] private int _cellSize;
    [CachedConfig] private Fixed64 _spreadScale;
    [CachedConfig] private Fixed64 _gradientScale;
    [CachedConfig] private Fixed64 _smoothingAlpha;
    [CachedConfig] private Fixed64 _deadZoneThresholdSq;
    [CachedConfig] private int _minDensityThreshold;

    // Computed values (handled in OnConfigRefreshed)
    private int _totalCells;
    private Fixed64 _oneMinusAlpha;

    // Public properties for debug visualization
    public int GridSize => _gridSize;
    public int CellSize => _cellSize;

    // Buffers (mutable for reallocation on grid size change)
    private int[] _density;
    private int[] _blurBuffer;
    private long[] _gradientX;
    private long[] _gradientY;

    public SeparationSystem(SimWorld world, GameDataManager<GameDocDb> gameData) : base(world)
    {
        _gameData = gameData;

        // Load initial config from GameDocDatabase
        ref readonly var config = ref gameData.Db.SeparationConfigData.FindById((int)SeparationConfigId.Default);

        _gridSize = config.GridSize;
        _cellSize = config.CellSize;
        _spreadScale = config.SpreadScale;
        _gradientScale = config.GradientScale;
        _smoothingAlpha = config.SmoothingAlpha;
        _deadZoneThresholdSq = config.DeadZoneThresholdSq;
        _minDensityThreshold = config.MinDensityThreshold;

        // Initialize computed values and allocate buffers
        _totalCells = _gridSize * _gridSize;
        _oneMinusAlpha = Fixed64.OneValue - _smoothingAlpha;
        _density = new int[_totalCells];
        _blurBuffer = new int[_totalCells];
        _gradientX = new long[_totalCells];
        _gradientY = new long[_totalCells];
    }

#if HOT_RELOAD
    /// <summary>Called by generated RefreshConfigIfStale after config values are refreshed.</summary>
    partial void OnConfigRefreshed()
    {
        int oldTotalCells = _totalCells;

        // Update computed values
        _totalCells = _gridSize * _gridSize;
        _oneMinusAlpha = Fixed64.OneValue - _smoothingAlpha;

        // Reallocate buffers if grid size changed
        if (_totalCells != oldTotalCells)
        {
            _density = new int[_totalCells];
            _blurBuffer = new int[_totalCells];
            _gradientX = new long[_totalCells];
            _gradientY = new long[_totalCells];
        }
    }
#endif

    /// <summary>Gets cell density for debug visualization.</summary>
    public int GetCellDensity(int cellIndex)
    {
        if (cellIndex < 0 || cellIndex >= _totalCells) return 0;
        return _density[cellIndex];
    }

    /// <summary>Gets cell index for a position.</summary>
    public int GetCellIndexForPos(Fixed64 posX, Fixed64 posY)
    {
        int cellX = posX.ToInt() / _cellSize;
        int cellY = posY.ToInt() / _cellSize;
        cellX = Math.Clamp(cellX, 0, _gridSize - 1);
        cellY = Math.Clamp(cellY, 0, _gridSize - 1);
        return cellX + cellY * _gridSize;
    }

    public override void Tick(in SimulationContext context)
    {
        // Check for hot-reload config changes (no-op in production builds)
        RefreshConfigIfStale(_gameData.Generation, _gameData.Db);

        BuildDensity();
        BlurDensity();
        ComputeGradients();
        ApplyForces();
    }

    private void BuildDensity()
    {
        Array.Clear(_density, 0, _totalCells);

        // Only process zombies - combat units use RVO instead
        var zombies = World.ZombieRows;
        int count = zombies.Count;

        for (int i = 0; i < count; i++)
        {
            var zombie = zombies.GetRowBySlot(i);

            // Skip dead entities
            if (zombie.Flags.IsDead()) continue;

            var pos = zombie.Position;

            // Write to center cell only - blur will spread it
            int cellX = Math.Clamp(pos.X.ToInt() / _cellSize, 0, _gridSize - 1);
            int cellY = Math.Clamp(pos.Y.ToInt() / _cellSize, 0, _gridSize - 1);
            _density[cellX + cellY * _gridSize]++;
        }
    }

    private void BlurDensity()
    {
        // 3x3 Gaussian blur: kernel [1,2,1; 2,4,2; 1,2,1] / 16
        for (int y = 1; y < _gridSize - 1; y++)
        {
            int rowStart = y * _gridSize;
            int rowAbove = (y - 1) * _gridSize;
            int rowBelow = (y + 1) * _gridSize;

            for (int x = 1; x < _gridSize - 1; x++)
            {
                int idx = rowStart + x;

                int tl = _density[rowAbove + x - 1];
                int t  = _density[rowAbove + x];
                int tr = _density[rowAbove + x + 1];
                int l  = _density[idx - 1];
                int c  = _density[idx];
                int r  = _density[idx + 1];
                int bl = _density[rowBelow + x - 1];
                int b  = _density[rowBelow + x];
                int br = _density[rowBelow + x + 1];

                _blurBuffer[idx] = (tl + 2*t + tr + 2*l + 4*c + 2*r + bl + 2*b + br) >> 4;
            }
        }

        // Copy blurred values back
        for (int y = 1; y < _gridSize - 1; y++)
        {
            int rowStart = y * _gridSize;
            for (int x = 1; x < _gridSize - 1; x++)
            {
                _density[rowStart + x] = _blurBuffer[rowStart + x];
            }
        }
    }

    private void ComputeGradients()
    {
        // Simple 2-point finite difference (blur already smoothed the field)
        for (int y = 1; y < _gridSize - 1; y++)
        {
            int rowStart = y * _gridSize;

            for (int x = 1; x < _gridSize - 1; x++)
            {
                int idx = rowStart + x;

                // Gradient points from high density toward low density
                int gradXVal = _density[idx - 1] - _density[idx + 1];
                int gradYVal = _density[idx - _gridSize] - _density[idx + _gridSize];

                _gradientX[idx] = (long)gradXVal << Fixed64.FractionalBits;
                _gradientY[idx] = (long)gradYVal << Fixed64.FractionalBits;
            }
        }
    }

    private void ApplyForces()
    {
        // Only process zombies - combat units use RVO instead
        var zombies = World.ZombieRows;
        int count = zombies.Count;

        for (int i = 0; i < count; i++)
        {
            var zombie = zombies.GetRowBySlot(i);

            // Skip dead entities
            if (zombie.Flags.IsDead()) continue;

            var pos = zombie.Position;

            // Skip if not crowded
            int cellIdx = GetCellIndex(pos.X, pos.Y);
            if (_density[cellIdx] <= _minDensityThreshold) continue;

            var (gradX, gradY) = SampleGradient(pos.X, pos.Y);

            // Skip if gradient too small (dead zone)
            Fixed64 gradMagSq = gradX * gradX + gradY * gradY;
            if (gradMagSq < _deadZoneThresholdSq) continue;

            // Add spread based on subcell position
            int subcellX = (pos.X.ToInt() >> 3) & 3;
            int subcellY = (pos.Y.ToInt() >> 3) & 3;
            Fixed64 spread = Fixed64.FromInt(subcellX + subcellY - 3) * _spreadScale;

            Fixed64 rawForceX = gradX - gradY * spread;
            Fixed64 rawForceY = gradY + gradX * spread;

            // Apply EMA smoothing
            var prevSmoothed = zombie.SmoothedSeparation;
            Fixed64 smoothedX = prevSmoothed.X * _oneMinusAlpha + rawForceX * _smoothingAlpha;
            Fixed64 smoothedY = prevSmoothed.Y * _oneMinusAlpha + rawForceY * _smoothingAlpha;
            zombie.SmoothedSeparation = new Fixed64Vec2(smoothedX, smoothedY);

            // Cap force by entity's MoveSpeed
            Fixed64 maxForce = zombie.MoveSpeed;
            Fixed64 forceX = Fixed64.Clamp(smoothedX, -maxForce, maxForce);
            Fixed64 forceY = Fixed64.Clamp(smoothedY, -maxForce, maxForce);

            // Apply to velocity
            var vel = zombie.Velocity;
            zombie.Velocity = new Fixed64Vec2(vel.X + forceX, vel.Y + forceY);
        }
    }

    private (Fixed64 gradX, Fixed64 gradY) SampleGradient(Fixed64 posX, Fixed64 posY)
    {
        // Simple nearest-cell lookup (blur already smoothed the field)
        int cellX = Math.Clamp(posX.ToInt() / _cellSize, 1, _gridSize - 2);
        int cellY = Math.Clamp(posY.ToInt() / _cellSize, 1, _gridSize - 2);
        int idx = cellX + cellY * _gridSize;

        Fixed64 gradX = Fixed64.FromRaw(_gradientX[idx]) * _gradientScale;
        Fixed64 gradY = Fixed64.FromRaw(_gradientY[idx]) * _gradientScale;

        return (gradX, gradY);
    }

    private int GetCellIndex(Fixed64 posX, Fixed64 posY)
    {
        int cellX = posX.ToInt() / _cellSize;
        int cellY = posY.ToInt() / _cellSize;

        cellX = Math.Clamp(cellX, 0, _gridSize - 1);
        cellY = Math.Clamp(cellY, 0, _gridSize - 1);

        return cellX + cellY * _gridSize;
    }
}
