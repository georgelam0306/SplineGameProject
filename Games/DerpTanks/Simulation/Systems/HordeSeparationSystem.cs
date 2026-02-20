using DerpLib.Ecs;
using DerpTanks.Simulation;
using DerpTanks.Simulation.Ecs;
using FixedMath;

namespace DerpTanks.Simulation.Systems;

public sealed class HordeSeparationSystem : IEcsSystem<SimEcsWorld>
{
    private const int MinDensityThreshold = 1;
    private const int FallbackDirectionSalt = 911;
    private const int BoidDensityThreshold = 4;
    private const int BoidQueryMaxResults = 32;
    private const int BoidMaxNeighbors = 24;
    private const int BoidNeighborSalt = 921;

    private static readonly Fixed64 SpreadScale = Fixed64.FromFloat(0.08f);
    private static readonly Fixed64 GradientScale = Fixed64.FromFloat(0.18f);
    private static readonly Fixed64 SmoothingAlpha = Fixed64.FromFloat(0.20f);
    private static readonly Fixed64 OneMinusAlpha = Fixed64.OneValue - SmoothingAlpha;
    private static readonly Fixed64 DeadZoneThresholdSq = Fixed64.FromFloat(0.01f);
    private static readonly Fixed64 SeparationMaxForceScale = Fixed64.FromFloat(0.50f);
    private static readonly Fixed64 BoidRadius = Fixed64.FromInt(3);
    private static readonly Fixed64 BoidSoftening = Fixed64.FromFloat(0.25f);
    private static readonly Fixed64 BoidMaxForceScale = Fixed64.FromFloat(0.40f);

    public void Update(SimEcsWorld world)
    {
        int[] density = world.SeparationDensity;
        int[] blurBuffer = world.SeparationBlurBuffer;
        long[] gradientX = world.SeparationGradientX;
        long[] gradientY = world.SeparationGradientY;

        BuildDensity(world, density);
        ApplyBoidSeparation(world, density);
        BlurDensity(density, blurBuffer);
        ComputeGradients(density, gradientX, gradientY);
        ApplyForces(world, density, gradientX, gradientY);
    }

    private static void BuildDensity(SimEcsWorld world, int[] density)
    {
        System.Array.Clear(density, 0, density.Length);

        for (int row = 0; row < world.Horde.Count; row++)
        {
            ref var transform = ref world.Horde.Transform(row);
            int cellIndex = HordeSeparationGrid.GetCellIndex(transform.Position);
            density[cellIndex]++;
        }
    }

    private static void BlurDensity(int[] density, int[] blurBuffer)
    {
        // 3x3 Gaussian blur: kernel [1,2,1; 2,4,2; 1,2,1] / 16
        for (int y = 1; y < HordeSeparationGrid.GridSize - 1; y++)
        {
            int rowStart = y * HordeSeparationGrid.GridSize;
            int rowAbove = (y - 1) * HordeSeparationGrid.GridSize;
            int rowBelow = (y + 1) * HordeSeparationGrid.GridSize;

            for (int x = 1; x < HordeSeparationGrid.GridSize - 1; x++)
            {
                int index = rowStart + x;

                int tl = density[rowAbove + x - 1];
                int t = density[rowAbove + x];
                int tr = density[rowAbove + x + 1];
                int l = density[index - 1];
                int c = density[index];
                int r = density[index + 1];
                int bl = density[rowBelow + x - 1];
                int b = density[rowBelow + x];
                int br = density[rowBelow + x + 1];

                blurBuffer[index] = (tl + (2 * t) + tr + (2 * l) + (4 * c) + (2 * r) + bl + (2 * b) + br) >> 4;
            }
        }

        for (int y = 1; y < HordeSeparationGrid.GridSize - 1; y++)
        {
            int rowStart = y * HordeSeparationGrid.GridSize;
            for (int x = 1; x < HordeSeparationGrid.GridSize - 1; x++)
            {
                density[rowStart + x] = blurBuffer[rowStart + x];
            }
        }
    }

    private static void ComputeGradients(int[] density, long[] gradientX, long[] gradientY)
    {
        for (int y = 1; y < HordeSeparationGrid.GridSize - 1; y++)
        {
            int rowStart = y * HordeSeparationGrid.GridSize;

            for (int x = 1; x < HordeSeparationGrid.GridSize - 1; x++)
            {
                int index = rowStart + x;

                // Gradient points from high density toward low density.
                int gradXValue = density[index - 1] - density[index + 1];
                int gradYValue = density[index - HordeSeparationGrid.GridSize] - density[index + HordeSeparationGrid.GridSize];

                gradientX[index] = (long)gradXValue << Fixed64.FractionalBits;
                gradientY[index] = (long)gradYValue << Fixed64.FractionalBits;
            }
        }
    }

    private static void ApplyForces(SimEcsWorld world, int[] density, long[] gradientX, long[] gradientY)
    {
        for (int row = 0; row < world.Horde.Count; row++)
        {
            ref var transform = ref world.Horde.Transform(row);
            ref var combat = ref world.Horde.Combat(row);

            int cellIndex = HordeSeparationGrid.GetCellIndex(transform.Position);
            if (density[cellIndex] <= MinDensityThreshold)
            {
                transform.SmoothedSeparation = transform.SmoothedSeparation * OneMinusAlpha;
                continue;
            }

            Fixed64Vec2 grad = SampleGradient(transform.Position, gradientX, gradientY);
            Fixed64 gradMagSq = (grad.X * grad.X) + (grad.Y * grad.Y);

            if (gradMagSq < DeadZoneThresholdSq)
            {
                // At a symmetric peak density cell, finite-difference gradients can be zero.
                // Fall back to a deterministic "push out of the current cell" direction so dense piles actually disperse.
                Fixed64Vec2 fallback = ComputeFallbackDirection(world, row, transform.Position);
                ApplySmoothedForce(ref transform, ref combat, fallback.X, fallback.Y);
                continue;
            }

            int posX = transform.Position.X.ToInt();
            int posY = transform.Position.Y.ToInt();
            int localX = posX - HordeSeparationGrid.OriginX;
            int localY = posY - HordeSeparationGrid.OriginY;
            int subcellX = (localX >> 3) & 3;
            int subcellY = (localY >> 3) & 3;
            Fixed64 spread = Fixed64.FromInt(subcellX + subcellY - 3) * SpreadScale;

            Fixed64 rawForceX = grad.X - (grad.Y * spread);
            Fixed64 rawForceY = grad.Y + (grad.X * spread);

            ApplySmoothedForce(ref transform, ref combat, rawForceX, rawForceY);
        }
    }

    private static void ApplySmoothedForce(ref TransformComponent transform, ref CombatComponent combat, Fixed64 rawForceX, Fixed64 rawForceY)
    {
        Fixed64Vec2 prev = transform.SmoothedSeparation;
        Fixed64 smoothedX = (prev.X * OneMinusAlpha) + (rawForceX * SmoothingAlpha);
        Fixed64 smoothedY = (prev.Y * OneMinusAlpha) + (rawForceY * SmoothingAlpha);
        transform.SmoothedSeparation = new Fixed64Vec2(smoothedX, smoothedY);

        Fixed64 maxForce = combat.MoveSpeed * SeparationMaxForceScale;
        Fixed64 forceX = Fixed64.Clamp(smoothedX, -maxForce, maxForce);
        Fixed64 forceY = Fixed64.Clamp(smoothedY, -maxForce, maxForce);

        Fixed64Vec2 vel = transform.Velocity;
        Fixed64 newVelX = vel.X + forceX;
        Fixed64 newVelY = vel.Y + forceY;

        // Keep separation additive but prevent runaway acceleration and preserve flow intent.
        Fixed64 moveSpeed = combat.MoveSpeed;
        newVelX = Fixed64.Clamp(newVelX, -moveSpeed, moveSpeed);
        newVelY = Fixed64.Clamp(newVelY, -moveSpeed, moveSpeed);
        transform.Velocity = new Fixed64Vec2(newVelX, newVelY);
    }

    private static void ApplyBoidSeparation(SimEcsWorld world, int[] rawDensity)
    {
        Span<EntityHandle> queryBuffer = world.QueryBuffer;
        if (queryBuffer.Length <= 0)
        {
            return;
        }

        Span<EntityHandle> results = queryBuffer;
        if (results.Length > BoidQueryMaxResults)
        {
            results = results.Slice(0, BoidQueryMaxResults);
        }

        Fixed64 radius = BoidRadius;
        Fixed64 radiusSq = radius * radius;

        for (int row = 0; row < world.Horde.Count; row++)
        {
            ref var transform = ref world.Horde.Transform(row);
            int cellIndex = HordeSeparationGrid.GetCellIndex(transform.Position);
            if (rawDensity[cellIndex] <= BoidDensityThreshold)
            {
                continue;
            }

            int hitCount = world.Horde.QueryRadius(transform.Position, radius, results);
            if (hitCount <= 1)
            {
                continue;
            }

            var selfEntity = world.Horde.Entity(row);
            Fixed64Vec2 accum = Fixed64Vec2.Zero;
            int neighborCount = 0;

            for (int i = 0; i < hitCount; i++)
            {
                EntityHandle neighborEntity = results[i];
                if (neighborEntity.RawId == selfEntity.RawId)
                {
                    continue;
                }

                if (!world.Horde.TryGetRow(neighborEntity, out int neighborRow))
                {
                    continue;
                }

                ref var neighborTransform = ref world.Horde.Transform(neighborRow);
                Fixed64Vec2 diff = new Fixed64Vec2(
                    transform.Position.X - neighborTransform.Position.X,
                    transform.Position.Y - neighborTransform.Position.Y);

                Fixed64 distSq = (diff.X * diff.X) + (diff.Y * diff.Y);
                if (distSq.Raw == 0)
                {
                    diff = DeterministicRandom.UnitVector2DWithSeed(world.SessionSeed, world.CurrentFrame, row, BoidNeighborSalt + neighborRow);
                    distSq = Fixed64.OneValue;
                }

                if (distSq > radiusSq)
                {
                    continue;
                }

                Fixed64 inv = Fixed64.OneValue / (distSq + BoidSoftening);
                accum = new Fixed64Vec2(accum.X + diff.X * inv, accum.Y + diff.Y * inv);

                neighborCount++;
                if (neighborCount >= BoidMaxNeighbors)
                {
                    break;
                }
            }

            if (neighborCount <= 0)
            {
                continue;
            }

            Fixed64Vec2 dir = accum.Normalized();
            if (dir == Fixed64Vec2.Zero)
            {
                continue;
            }

            ref var combat = ref world.Horde.Combat(row);
            Fixed64 maxForce = combat.MoveSpeed * BoidMaxForceScale;
            Fixed64 forceX = dir.X * maxForce;
            Fixed64 forceY = dir.Y * maxForce;

            Fixed64Vec2 vel = transform.Velocity;
            Fixed64 newVelX = vel.X + forceX;
            Fixed64 newVelY = vel.Y + forceY;

            Fixed64 moveSpeed = combat.MoveSpeed;
            newVelX = Fixed64.Clamp(newVelX, -moveSpeed, moveSpeed);
            newVelY = Fixed64.Clamp(newVelY, -moveSpeed, moveSpeed);
            transform.Velocity = new Fixed64Vec2(newVelX, newVelY);
        }
    }

    private static Fixed64Vec2 ComputeFallbackDirection(SimEcsWorld world, int row, Fixed64Vec2 position)
    {
        int cellX = HordeSeparationGrid.GetCellX(position);
        int cellY = HordeSeparationGrid.GetCellY(position);

        int centerXInt = HordeSeparationGrid.GetCellWorldX(cellX) + HordeSeparationGrid.CellSize / 2;
        int centerYInt = HordeSeparationGrid.GetCellWorldY(cellY) + HordeSeparationGrid.CellSize / 2;

        Fixed64Vec2 delta = new Fixed64Vec2(position.X - Fixed64.FromInt(centerXInt), position.Y - Fixed64.FromInt(centerYInt));
        Fixed64 lenSq = delta.LengthSquared();

        if (lenSq > Fixed64.FromFloat(0.001f))
        {
            return delta.Normalized();
        }

        // If we're essentially at the cell center, use a deterministic per-entity direction so we still break symmetry.
        return DeterministicRandom.UnitVector2DWithSeed(world.SessionSeed, world.CurrentFrame, row, FallbackDirectionSalt);
    }

    private static Fixed64Vec2 SampleGradient(Fixed64Vec2 worldPos, long[] gradientX, long[] gradientY)
    {
        int index = HordeSeparationGrid.GetInnerCellIndex(worldPos);

        Fixed64 gx = Fixed64.FromRaw(gradientX[index]) * GradientScale;
        Fixed64 gy = Fixed64.FromRaw(gradientY[index]) * GradientScale;

        return new Fixed64Vec2(gx, gy);
    }
}
