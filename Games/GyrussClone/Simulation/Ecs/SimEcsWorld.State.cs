using FixedMath;

namespace GyrussClone.Simulation.Ecs;

public sealed partial class SimEcsWorld
{
    // Frame state
    public Fixed64 DeltaTime;
    public int CurrentFrame;
    public int SessionSeed;

    // Player state (player is world state, not an entity)
    public Fixed64 PlayerAngle;
    public Fixed64 PlayerAngularInput; // -1, 0, or 1
    public bool FireRequested;
    public int NextFireFrame;
    public int Lives;
    public int Score;
    public bool PlayerAlive;

    // Wave state
    public int CurrentWave;
    public int WaveSpawnRemaining;
    public int NextWaveFrame;

    // Tuning constants
    public Fixed64 PlayerRadius;
    public Fixed64 PlayerAngularSpeed;
    public Fixed64 BulletRadialSpeed;
    public Fixed64 BulletLifetime;
    public int FireCooldownFrames;
    public Fixed64 EnemyBaseRadialSpeed;
    public Fixed64 CollisionRadius;
    public Fixed64 PlayerCollisionRadius;

    public static SimEcsWorld Create(int sessionSeed)
    {
        var world = new SimEcsWorld();
        world.Initialize(sessionSeed);
        return world;
    }

    public void Initialize(int sessionSeed)
    {
        SessionSeed = sessionSeed;
        CurrentFrame = 0;

        // Player
        PlayerAngle = Fixed64.Zero;
        PlayerAngularInput = Fixed64.Zero;
        FireRequested = false;
        NextFireFrame = 0;
        Lives = 3;
        Score = 0;
        PlayerAlive = true;

        // Waves
        CurrentWave = 0;
        WaveSpawnRemaining = 0;
        NextWaveFrame = 60; // Start first wave after 1 second

        // Tuning
        PlayerRadius = Fixed64.FromInt(280);
        PlayerAngularSpeed = Fixed64.FromInt(3);
        BulletRadialSpeed = Fixed64.FromInt(400);
        BulletLifetime = Fixed64.FromFloat(1.5f);
        FireCooldownFrames = 8;
        EnemyBaseRadialSpeed = Fixed64.FromInt(40);
        CollisionRadius = Fixed64.FromInt(14);
        PlayerCollisionRadius = Fixed64.FromInt(18);
    }
}
