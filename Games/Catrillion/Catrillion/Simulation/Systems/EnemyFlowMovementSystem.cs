using System.Collections.Generic;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Grids;
using Catrillion.Simulation.Services;
using Catrillion.Simulation.Systems.SimTable;
using Core;
using FlowField;

namespace Catrillion.Simulation.Systems;

/// <summary>
/// Applies flow-based movement to zombies.
/// Blends three flow sources: noise attraction, player flow, and center flow (fallback).
/// </summary>
public sealed class EnemyFlowMovementSystem : SimTableSystem
{
    private readonly IZoneFlowService _zoneFlowService;
    private readonly CenterFlowService _centerFlowService;
    private readonly IWorldProvider _worldProvider;

    private static readonly Fixed64 EnemySpeed = Fixed64.FromFloat(1.5f);
    private static readonly Fixed64 NoiseBlendWeight = Fixed64.FromFloat(0.6f);
    private static readonly Fixed64 PlayerBlendWeight = Fixed64.FromFloat(0.4f);
    private static readonly Fixed64 MinMagnitude = Fixed64.FromFloat(0.001f);

    private const int MaxPlayerSeeds = 8;
    private readonly List<(int tileX, int tileY, Fixed64 cost)> _seedList;

    public EnemyFlowMovementSystem(
        SimWorld world,
        IZoneFlowService zoneFlowService,
        CenterFlowService centerFlowService,
        IWorldProvider worldProvider) : base(world)
    {
        _zoneFlowService = zoneFlowService;
        _centerFlowService = centerFlowService;
        _worldProvider = worldProvider;
        _seedList = new List<(int, int, Fixed64)>(MaxPlayerSeeds);
    }

    public override void Tick(in SimulationContext context)
    {
        UpdateFlowField();
        ApplyEnemyFlowMovement();
    }

    private void UpdateFlowField()
    {
        _seedList.Clear();

        // Seed flow field from noise sources (cells with high noise)
        var noiseTable = World.NoiseGridStateRows;
        if (noiseTable.Count == 0)
        {
            _zoneFlowService.ClearSeeds();
            return;
        }

        Fixed64 noiseThreshold = NoiseGridService.NoiseAttractionThreshold;

        // Scan noise grid for high-noise cells and use them as seeds
        for (int cellY = 0; cellY < NoiseCell.GridHeight && _seedList.Count < MaxPlayerSeeds; cellY++)
        {
            for (int cellX = 0; cellX < NoiseCell.GridWidth && _seedList.Count < MaxPlayerSeeds; cellX++)
            {
                var noiseCell = new NoiseCell(cellX, cellY);
                Fixed64 noise = NoiseGridService.GetNoiseLevel(noiseTable, noiseCell);
                if (noise < noiseThreshold) continue;

                // Convert cell center to tile coordinates using generated API
                var tile = noiseCell.ToTileCenter();

                // Higher noise = lower cost (more attractive)
                Fixed64 cost = NoiseGridService.MaxNoise - noise;
                _seedList.Add((tile.X, tile.Y, cost));
            }
        }

        if (_seedList.Count > 0)
        {
            _zoneFlowService.SetSeeds(_seedList);
        }
        else
        {
            _zoneFlowService.ClearSeeds();
        }
    }

    private void ApplyEnemyFlowMovement()
    {
        var zombies = World.ZombieRows;
        var noiseTable = World.NoiseGridStateRows;
        bool hasNoiseGrid = noiseTable.Count > 0;

        for (int slot = 0; slot < zombies.Count; slot++)
        {
            var zombie = zombies.GetRowBySlot(slot);
            if (zombie.Flags.IsDead()) continue;

            Fixed64Vec2 flow;

            // Get player flow
            var playerFlow = _zoneFlowService.GetFlowDirection(zombie.Position);
            bool hasPlayerFlow = playerFlow != Fixed64Vec2.Zero;

            // Check for noise attraction
            if (hasNoiseGrid && zombie.NoiseAttraction > 0)
            {
                // Get direction toward noise source (radius from data)
                var (_, _, noiseDirection) = NoiseGridService.FindHighestNoiseNearby(
                    noiseTable,
                    zombie.Position,
                    zombie.NoiseSearchRadius);

                // Blend noise and player flows
                flow = noiseDirection * NoiseBlendWeight + playerFlow * PlayerBlendWeight;
            }
            else if (hasPlayerFlow)
            {
                // Use player flow when no noise attraction
                flow = playerFlow;
            }
            else
            {
                // Fallback to center flow
                flow = _centerFlowService.GetFlowDirection(zombie.Position);
            }

            // Normalize the blended flow
            Fixed64 magSq = flow.X * flow.X + flow.Y * flow.Y;
            if (magSq > MinMagnitude)
            {
                Fixed64 mag = Fixed64.Sqrt(magSq);
                flow = new Fixed64Vec2(flow.X / mag, flow.Y / mag);
            }

            // Store flow for debugging/visualization
            zombie.Flow = flow;

            // Apply flow to velocity
            zombie.Velocity += flow * EnemySpeed;
        }
    }
}
