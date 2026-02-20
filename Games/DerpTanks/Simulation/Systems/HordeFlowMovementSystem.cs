using DerpLib.Ecs;
using DerpTanks.Simulation.Ecs;
using FixedMath;

namespace DerpTanks.Simulation.Systems;

public sealed class HordeFlowMovementSystem : IEcsSystem<SimEcsWorld>
{
    private const int FrameSpread = 4;
    private static readonly Fixed64 NearChaseDistance = Fixed64.FromInt(16);
    private static readonly Fixed64 NearChaseDistanceSq = NearChaseDistance * NearChaseDistance;

    public void Update(SimEcsWorld world)
    {
        Fixed64Vec2 playerPos = world.PlayerPosition;
        int tileX = (world.PlayerPosition.X / world.TileSizeFixed).ToInt();
        int tileY = (world.PlayerPosition.Y / world.TileSizeFixed).ToInt();

        if (tileX != world.CurrentSeedTileX || tileY != world.CurrentSeedTileY)
        {
            var seeds = world.SeedBuffer;
            seeds.Clear();
            seeds.Add((tileX, tileY, Fixed64.Zero));
            world.FlowService.SetSeeds(seeds);
            world.CurrentSeedTileX = tileX;
            world.CurrentSeedTileY = tileY;
        }

        int phase = world.CurrentFrame & (FrameSpread - 1);

        for (int row = 0; row < world.Horde.Count; row++)
        {
            if ((row & (FrameSpread - 1)) != phase)
            {
                continue;
            }

            ref var transform = ref world.Horde.Transform(row);
            ref var combat = ref world.Horde.Combat(row);

            Fixed64Vec2 toPlayer = new Fixed64Vec2(playerPos.X - transform.Position.X, playerPos.Y - transform.Position.Y);
            Fixed64 distSq = toPlayer.LengthSquared();

            Fixed64Vec2 dir;
            int entityTileX = (transform.Position.X / world.TileSizeFixed).ToInt();
            int entityTileY = (transform.Position.Y / world.TileSizeFixed).ToInt();

            if (entityTileX == tileX && entityTileY == tileY)
            {
                // Flow fields are tile-based, so once an enemy reaches the seed tile the flow direction often becomes zero.
                // Chase the exact player position while in the same tile so enemies don't "stick" to the tile center.
                dir = toPlayer.Normalized();
            }
            else if (distSq <= NearChaseDistanceSq)
            {
                dir = toPlayer.Normalized();
            }
            else
            {
                // Flow field direction is tile-based, so it tends to converge to the seed tile center.
                // We switch to direct chase inside a small radius so enemies track the actual player position.
                dir = world.FlowService.GetFlowDirection(transform.Position);
            }

            transform.Velocity = dir * combat.MoveSpeed;
        }
    }
}
