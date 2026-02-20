using DieDrifterDie.GameApp.Config;
using DieDrifterDie.Simulation;
using DieDrifterDie.Simulation.Components;
using DieDrifterDie.Simulation.Systems.Pickup;
using DieDrifterDie.Simulation.Utilities;
using SimTable;

namespace DieDrifterDie.Tests.Simulation.Systems;

public class SuperPickupSystemDeterminismTests : IDisposable
{
    private readonly SimWorld _world;
    private readonly SuperPickupSystem _system;

    public SuperPickupSystemDeterminismTests()
    {
        _world = new SimWorld();
        _system = new SuperPickupSystem(_world);
    }

    public void Dispose() => _world.Dispose();

    #region Helper Methods

    private SimulationContext CreateContext(int frame) =>
        new(frame, playerCount: 4, sessionSeed: 12345, inputBuffer: null!);

    private void SetupWaveState(byte phase, int startFrame)
    {
        _world.WaveStateRows.Allocate();
        if (_world.WaveStateRows.TryGetRow(0, out var state))
        {
            state.SuperPickupPhase = phase;
            state.SuperPickupPhaseStartFrame = startFrame;
        }
    }

    private SimHandle CreatePlayer(int slot, Fixed64 x, Fixed64 y, bool isDead = false)
    {
        var handle = _world.PlayerRows.Allocate();
        var row = _world.PlayerRows.GetRow(handle);
        row.PlayerSlot = (byte)slot;
        row.Position = new Fixed64Vec2(x, y);
        row.IsActive = true;
        row.IsDead = isDead;
        return handle;
    }

    private SimHandle CreatePickup(Fixed64 x, Fixed64 y, PowerType power = PowerType.VelocityShooter)
    {
        var handle = _world.SuperPickupRows.Allocate();
        var row = _world.SuperPickupRows.GetRow(handle);
        row.Position = new Fixed64Vec2(x, y);
        row.IsActive = true;
        row.PowerType = power;
        return handle;
    }

    private SimHandle CreatePuck(Fixed64 x, Fixed64 y, byte ownerPlayerId = 0)
    {
        var handle = _world.PuckRows.Allocate();
        var row = _world.PuckRows.GetRow(handle);
        row.Position = new Fixed64Vec2(x, y);
        row.IsActive = true;
        row.OwnerPlayerId = ownerPlayerId;
        return handle;
    }

    private ulong CaptureStateHash()
    {
        return _world.SuperPickupRows.ComputeStateHash() ^
               (_world.PlayerRows.ComputeStateHash() << 1) ^
               (_world.WaveStateRows.ComputeStateHash() << 2) ^
               (_world.PuckRows.ComputeStateHash() << 3);
    }

    private static byte GetLevelForPower(PuckRowRowRef puck, PowerType power)
    {
        return power switch
        {
            PowerType.VelocityShooter => puck.ShooterLevel,
            PowerType.ChainLightning => puck.ChainLevel,
            PowerType.Explosion => puck.ExplosionLevel,
            PowerType.SizeUp => puck.SizeLevel,
            PowerType.SpeedBoost => puck.SpeedLevel,
            PowerType.Magnet => puck.MagnetLevel,
            PowerType.Split => puck.SplitLevel,
            PowerType.BounceBoost => puck.BounceLevel,
            PowerType.FreezeAura => puck.FreezeLevel,
            PowerType.GravityWell => puck.GravityLevel,
            _ => 0
        };
    }

    #endregion

    #region Test 1: Tick_WithSameState_ProducesSameResult

    [Fact]
    public void Tick_WithSameState_ProducesSameResult()
    {
        // Setup identical world state twice
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePlayer(0, Fixed64.Zero, Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(25), Fixed64.Zero);

        var ctx = CreateContext(100);

        // Run tick once
        _system.Tick(in ctx);
        var hash1 = CaptureStateHash();

        // Reset world and set up identical state
        _world.ResetAllTables();
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePlayer(0, Fixed64.Zero, Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(25), Fixed64.Zero);

        // Run tick again
        _system.Tick(in ctx);
        var hash2 = CaptureStateHash();

        Assert.Equal(hash1, hash2);
    }

    #endregion

    #region Test 2: PlayersOnPickupBitmask_IsDeterministic

    [Fact]
    public void PlayersOnPickupBitmask_IsDeterministic()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        // Create 3 players at different distances from pickup at (100, 100)
        Fixed64 pickupX = Fixed64.FromInt(100);
        Fixed64 pickupY = Fixed64.FromInt(100);

        // Player 0: Inside radius (distance = 30px)
        CreatePlayer(0, pickupX + Fixed64.FromInt(30), pickupY);
        // Player 1: Inside radius (distance = 40px)
        CreatePlayer(1, pickupX, pickupY + Fixed64.FromInt(40));
        // Player 2: Outside radius (distance = 60px)
        CreatePlayer(2, pickupX + Fixed64.FromInt(60), pickupY);

        CreatePickup(pickupX, pickupY);

        var ctx = CreateContext(100);

        // Run multiple times and verify bitmask is consistent
        byte[] results = new byte[10];
        for (int i = 0; i < 10; i++)
        {
            // Reset pickup state but keep players
            if (_world.SuperPickupRows.TryGetRow(0, out var pickup))
            {
                pickup.PlayersOnPickup = 0; // Reset before tick
            }

            _system.Tick(in ctx);

            if (_world.SuperPickupRows.TryGetRow(0, out var pickupAfter))
            {
                results[i] = pickupAfter.PlayersOnPickup;
            }
        }

        // All results should be identical
        for (int i = 1; i < 10; i++)
        {
            Assert.Equal(results[0], results[i]);
        }

        // Players 0 and 1 should be on pickup (bits 0 and 1 set)
        // Player 2 should NOT be on pickup (bit 2 not set)
        Assert.Equal(0b00000011, results[0]);
    }

    #endregion

    #region Test 3: CollisionBoundary_AtExactRadius_IsDeterministic

    [Fact]
    public void CollisionBoundary_AtExactRadius_IsDeterministic()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        // Collision radius is 50px
        Fixed64 pickupX = Fixed64.FromInt(100);
        Fixed64 pickupY = Fixed64.FromInt(100);

        // Place player at EXACTLY 50px (collision radius boundary)
        // Using exact integer to avoid Fixed64 rounding issues
        CreatePlayer(0, pickupX + Fixed64.FromInt(50), pickupY);
        CreatePickup(pickupX, pickupY);

        var ctx = CreateContext(100);

        // Run 100 times and verify consistent behavior
        bool? firstResult = null;
        for (int i = 0; i < 100; i++)
        {
            // Reset pickup bitmask
            if (_world.SuperPickupRows.TryGetRow(0, out var pickup))
            {
                pickup.PlayersOnPickup = 0;
            }

            _system.Tick(in ctx);

            if (_world.SuperPickupRows.TryGetRow(0, out var pickupAfter))
            {
                bool isOnPickup = (pickupAfter.PlayersOnPickup & 1) != 0;
                if (!firstResult.HasValue)
                {
                    firstResult = isOnPickup;
                }
                else
                {
                    Assert.Equal(firstResult.Value, isOnPickup);
                }
            }
        }
    }

    [Fact]
    public void CollisionBoundary_JustInsideRadius_IsDeterministic()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        Fixed64 pickupX = Fixed64.FromInt(100);
        Fixed64 pickupY = Fixed64.FromInt(100);

        // Place player at 49px (just inside 50px radius)
        CreatePlayer(0, pickupX + Fixed64.FromInt(49), pickupY);
        CreatePickup(pickupX, pickupY);

        var ctx = CreateContext(100);
        _system.Tick(in ctx);

        if (_world.SuperPickupRows.TryGetRow(0, out var pickup))
        {
            // Should definitely be inside
            Assert.Equal(0b00000001, pickup.PlayersOnPickup);
        }
    }

    [Fact]
    public void CollisionBoundary_JustOutsideRadius_IsDeterministic()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        Fixed64 pickupX = Fixed64.FromInt(100);
        Fixed64 pickupY = Fixed64.FromInt(100);

        // Place player at 51px (just outside 50px radius)
        CreatePlayer(0, pickupX + Fixed64.FromInt(51), pickupY);
        CreatePickup(pickupX, pickupY);

        var ctx = CreateContext(100);
        _system.Tick(in ctx);

        if (_world.SuperPickupRows.TryGetRow(0, out var pickup))
        {
            // Should definitely be outside
            Assert.Equal(0b00000000, pickup.PlayersOnPickup);
        }
    }

    #endregion

    #region Test 4: UnanimousVote_WithMultiplePlayers_IsDeterministic

    [Fact]
    public void UnanimousVote_TwoPlayers_CollectsWhenBothOnPickup()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        Fixed64 pickupX = Fixed64.FromInt(100);
        Fixed64 pickupY = Fixed64.FromInt(100);

        // Both players on pickup
        CreatePlayer(0, pickupX + Fixed64.FromInt(10), pickupY);
        CreatePlayer(1, pickupX - Fixed64.FromInt(10), pickupY);
        CreatePickup(pickupX, pickupY, PowerType.ChainLightning);

        // Create a puck to receive the power
        CreatePuck(Fixed64.Zero, Fixed64.Zero);

        var ctx = CreateContext(100);
        _system.Tick(in ctx);

        // Should have collected - phase should be PhaseCollected
        _world.WaveStateRows.TryGetRow(0, out var state);
        Assert.Equal(GameConfig.SuperPickup.PhaseCollected, state.SuperPickupPhase);
    }

    [Fact]
    public void UnanimousVote_TwoPlayers_DoesNotCollectWhenOnlyOne()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        Fixed64 pickupX = Fixed64.FromInt(100);
        Fixed64 pickupY = Fixed64.FromInt(100);

        // Only player 0 on pickup, player 1 far away
        CreatePlayer(0, pickupX + Fixed64.FromInt(10), pickupY);
        CreatePlayer(1, pickupX + Fixed64.FromInt(200), pickupY);
        CreatePickup(pickupX, pickupY);

        var ctx = CreateContext(100);
        _system.Tick(in ctx);

        // Should NOT have collected - phase should still be PhaseActive
        _world.WaveStateRows.TryGetRow(0, out var state);
        Assert.Equal(GameConfig.SuperPickup.PhaseActive, state.SuperPickupPhase);
    }

    [Fact]
    public void UnanimousVote_FourPlayers_RequiresAll()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        Fixed64 pickupX = Fixed64.FromInt(100);
        Fixed64 pickupY = Fixed64.FromInt(100);

        // 3 players on pickup, 1 off
        CreatePlayer(0, pickupX + Fixed64.FromInt(10), pickupY);
        CreatePlayer(1, pickupX - Fixed64.FromInt(10), pickupY);
        CreatePlayer(2, pickupX, pickupY + Fixed64.FromInt(10));
        CreatePlayer(3, pickupX + Fixed64.FromInt(200), pickupY); // Too far

        CreatePickup(pickupX, pickupY);

        var ctx = CreateContext(100);
        _system.Tick(in ctx);

        // Should NOT have collected - need all 4
        _world.WaveStateRows.TryGetRow(0, out var state);
        Assert.Equal(GameConfig.SuperPickup.PhaseActive, state.SuperPickupPhase);
    }

    #endregion

    #region Test 5: PowerUpgrade_AppliesSameLevels

    [Fact]
    public void PowerUpgrade_AppliesThreeLevelsToAllPucks()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        Fixed64 pickupX = Fixed64.FromInt(100);
        Fixed64 pickupY = Fixed64.FromInt(100);

        // Single player to allow collection
        CreatePlayer(0, pickupX, pickupY);
        CreatePickup(pickupX, pickupY, PowerType.ChainLightning);

        // Create 3 pucks
        var puck1 = CreatePuck(Fixed64.Zero, Fixed64.Zero);
        var puck2 = CreatePuck(Fixed64.FromInt(50), Fixed64.Zero);
        var puck3 = CreatePuck(Fixed64.FromInt(100), Fixed64.Zero);

        var ctx = CreateContext(100);
        _system.Tick(in ctx);

        // All pucks should have ChainLevel = 3
        var pucks = _world.PuckRows;
        Assert.True(pucks.TryGetRow(_world.PuckRows.GetSlot(puck1.StableId), out var p1));
        Assert.True(pucks.TryGetRow(_world.PuckRows.GetSlot(puck2.StableId), out var p2));
        Assert.True(pucks.TryGetRow(_world.PuckRows.GetSlot(puck3.StableId), out var p3));

        Assert.Equal(GameConfig.SuperPickup.LevelsGranted, p1.ChainLevel);
        Assert.Equal(GameConfig.SuperPickup.LevelsGranted, p2.ChainLevel);
        Assert.Equal(GameConfig.SuperPickup.LevelsGranted, p3.ChainLevel);

        // All should have the power flag set (bitmask check)
        Assert.True((p1.ActivePowers & (1 << (int)PowerType.ChainLightning)) != 0);
        Assert.True((p2.ActivePowers & (1 << (int)PowerType.ChainLightning)) != 0);
        Assert.True((p3.ActivePowers & (1 << (int)PowerType.ChainLightning)) != 0);
    }

    [Fact]
    public void PowerUpgrade_IsDeterministic_AcrossMultipleRuns()
    {
        // Run 1
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePlayer(0, Fixed64.FromInt(100), Fixed64.FromInt(100));
        CreatePickup(Fixed64.FromInt(100), Fixed64.FromInt(100), PowerType.Explosion);
        CreatePuck(Fixed64.Zero, Fixed64.Zero);

        var ctx = CreateContext(100);
        _system.Tick(in ctx);

        _world.PuckRows.TryGetRow(0, out var puck1);
        byte level1 = puck1.ExplosionLevel;
        ushort powers1 = puck1.ActivePowers;

        // Reset and Run 2
        _world.ResetAllTables();
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePlayer(0, Fixed64.FromInt(100), Fixed64.FromInt(100));
        CreatePickup(Fixed64.FromInt(100), Fixed64.FromInt(100), PowerType.Explosion);
        CreatePuck(Fixed64.Zero, Fixed64.Zero);

        _system.Tick(in ctx);

        _world.PuckRows.TryGetRow(0, out var puck2);
        byte level2 = puck2.ExplosionLevel;
        ushort powers2 = puck2.ActivePowers;

        Assert.Equal(level1, level2);
        Assert.Equal(powers1, powers2);
    }

    #endregion

    #region Test 6: PhaseTimeout_TriggersAtSameFrame

    [Fact]
    public void PhaseTimeout_TriggersAtExactFrame()
    {
        int startFrame = 0;
        int timeoutFrames = GameConfig.SuperPickup.TimeoutFrames;

        SetupWaveState(GameConfig.SuperPickup.PhaseActive, startFrame);
        CreatePickup(Fixed64.Zero, Fixed64.Zero);

        // Frame before timeout - should still be active
        var ctx1 = CreateContext(startFrame + timeoutFrames - 1);
        _system.Tick(in ctx1);

        _world.WaveStateRows.TryGetRow(0, out var state1);
        Assert.Equal(GameConfig.SuperPickup.PhaseActive, state1.SuperPickupPhase);

        // Frame at timeout - should deactivate
        var ctx2 = CreateContext(startFrame + timeoutFrames);
        _system.Tick(in ctx2);

        _world.WaveStateRows.TryGetRow(0, out var state2);
        Assert.Equal(GameConfig.SuperPickup.PhaseInactive, state2.SuperPickupPhase);
    }

    [Fact]
    public void PhaseTimeout_IsDeterministic_AcrossRuns()
    {
        int startFrame = 1000;
        int timeoutFrames = GameConfig.SuperPickup.TimeoutFrames;

        // Run 1
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, startFrame);
        CreatePickup(Fixed64.Zero, Fixed64.Zero);

        var ctx = CreateContext(startFrame + timeoutFrames);
        _system.Tick(in ctx);

        _world.WaveStateRows.TryGetRow(0, out var state1);
        byte phase1 = state1.SuperPickupPhase;

        // Run 2
        _world.ResetAllTables();
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, startFrame);
        CreatePickup(Fixed64.Zero, Fixed64.Zero);

        _system.Tick(in ctx);

        _world.WaveStateRows.TryGetRow(0, out var state2);
        byte phase2 = state2.SuperPickupPhase;

        Assert.Equal(phase1, phase2);
        Assert.Equal(GameConfig.SuperPickup.PhaseInactive, phase1);
    }

    [Fact]
    public void CelebrationPhase_EndsAtExactFrame()
    {
        int startFrame = 0;
        int celebrationFrames = GameConfig.SuperPickup.CelebrationFrames;

        SetupWaveState(GameConfig.SuperPickup.PhaseCollected, startFrame);

        // Frame before celebration ends - should stay in collected phase
        var ctx1 = CreateContext(startFrame + celebrationFrames - 1);
        _system.Tick(in ctx1);

        _world.WaveStateRows.TryGetRow(0, out var state1);
        Assert.Equal(GameConfig.SuperPickup.PhaseCollected, state1.SuperPickupPhase);

        // Frame at celebration end - should transition to inactive
        var ctx2 = CreateContext(startFrame + celebrationFrames);
        _system.Tick(in ctx2);

        _world.WaveStateRows.TryGetRow(0, out var state2);
        Assert.Equal(GameConfig.SuperPickup.PhaseInactive, state2.SuperPickupPhase);
    }

    #endregion

    #region Test 7: DeactivateAllSuperPickups_SameCountAfter

    [Fact]
    public void DeactivateAllSuperPickups_ClearsAllPickups()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        // Create 3 pickups
        CreatePickup(Fixed64.FromInt(100), Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(200), Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(300), Fixed64.Zero);

        Assert.Equal(3, _world.SuperPickupRows.Count);

        // Trigger timeout to deactivate all
        var ctx = CreateContext(GameConfig.SuperPickup.TimeoutFrames);
        _system.Tick(in ctx);

        // All pickups should be freed
        Assert.Equal(0, _world.SuperPickupRows.Count);
    }

    [Fact]
    public void DeactivateAllSuperPickups_IsDeterministic()
    {
        // Run 1
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePickup(Fixed64.FromInt(100), Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(200), Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(300), Fixed64.Zero);

        var ctx = CreateContext(GameConfig.SuperPickup.TimeoutFrames);
        _system.Tick(in ctx);

        int count1 = _world.SuperPickupRows.Count;
        ulong hash1 = _world.SuperPickupRows.ComputeStateHash();

        // Run 2
        _world.ResetAllTables();
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePickup(Fixed64.FromInt(100), Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(200), Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(300), Fixed64.Zero);

        _system.Tick(in ctx);

        int count2 = _world.SuperPickupRows.Count;
        ulong hash2 = _world.SuperPickupRows.ComputeStateHash();

        Assert.Equal(count1, count2);
        Assert.Equal(hash1, hash2);
    }

    #endregion

    #region Test 8: MultiplePickups_VotingOnEach_IsDeterministic

    [Fact]
    public void MultiplePickups_EachTracksSeparateBitmasks()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        // Create 3 pickups at different positions
        CreatePickup(Fixed64.Zero, Fixed64.Zero);                        // Pickup 0
        CreatePickup(Fixed64.FromInt(200), Fixed64.Zero);                // Pickup 1
        CreatePickup(Fixed64.FromInt(400), Fixed64.Zero);                // Pickup 2

        // Player 0 near pickup 0
        CreatePlayer(0, Fixed64.FromInt(10), Fixed64.Zero);
        // Player 1 near pickup 1
        CreatePlayer(1, Fixed64.FromInt(210), Fixed64.Zero);
        // Player 2 near pickup 2
        CreatePlayer(2, Fixed64.FromInt(410), Fixed64.Zero);

        var ctx = CreateContext(100);
        _system.Tick(in ctx);

        // Check each pickup has the correct player on it
        _world.SuperPickupRows.TryGetRow(0, out var p0);
        _world.SuperPickupRows.TryGetRow(1, out var p1);
        _world.SuperPickupRows.TryGetRow(2, out var p2);

        Assert.Equal(0b00000001, p0.PlayersOnPickup); // Player 0
        Assert.Equal(0b00000010, p1.PlayersOnPickup); // Player 1
        Assert.Equal(0b00000100, p2.PlayersOnPickup); // Player 2
    }

    [Fact]
    public void MultiplePickups_UnanimousVoteOnOne_CollectsThatOne()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        // Create 3 pickups
        CreatePickup(Fixed64.Zero, Fixed64.Zero, PowerType.VelocityShooter);
        CreatePickup(Fixed64.FromInt(200), Fixed64.Zero, PowerType.ChainLightning);
        CreatePickup(Fixed64.FromInt(400), Fixed64.Zero, PowerType.Explosion);

        // Both players on pickup 1 (ChainLightning)
        CreatePlayer(0, Fixed64.FromInt(200), Fixed64.Zero);
        CreatePlayer(1, Fixed64.FromInt(200), Fixed64.FromInt(10));

        CreatePuck(Fixed64.Zero, Fixed64.Zero);

        var ctx = CreateContext(100);
        _system.Tick(in ctx);

        // Should have collected ChainLightning pickup
        _world.WaveStateRows.TryGetRow(0, out var state);
        Assert.Equal(GameConfig.SuperPickup.PhaseCollected, state.SuperPickupPhase);
        Assert.Equal((byte)PowerType.ChainLightning, state.CollectedPowerType);

        // Puck should have ChainLightning
        _world.PuckRows.TryGetRow(0, out var puck);
        Assert.True((puck.ActivePowers & (1 << (int)PowerType.ChainLightning)) != 0);
        Assert.Equal(GameConfig.SuperPickup.LevelsGranted, puck.ChainLevel);
    }

    [Fact]
    public void MultiplePickups_IsDeterministic()
    {
        // Run twice with same setup and verify identical results

        // Run 1
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePickup(Fixed64.Zero, Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(200), Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(400), Fixed64.Zero);
        CreatePlayer(0, Fixed64.FromInt(10), Fixed64.Zero);
        CreatePlayer(1, Fixed64.FromInt(210), Fixed64.Zero);

        var ctx = CreateContext(100);
        _system.Tick(in ctx);

        byte[] bitmasks1 = new byte[3];
        for (int i = 0; i < 3; i++)
        {
            if (_world.SuperPickupRows.TryGetRow(i, out var p))
            {
                bitmasks1[i] = p.PlayersOnPickup;
            }
        }

        // Run 2
        _world.ResetAllTables();
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePickup(Fixed64.Zero, Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(200), Fixed64.Zero);
        CreatePickup(Fixed64.FromInt(400), Fixed64.Zero);
        CreatePlayer(0, Fixed64.FromInt(10), Fixed64.Zero);
        CreatePlayer(1, Fixed64.FromInt(210), Fixed64.Zero);

        _system.Tick(in ctx);

        byte[] bitmasks2 = new byte[3];
        for (int i = 0; i < 3; i++)
        {
            if (_world.SuperPickupRows.TryGetRow(i, out var p))
            {
                bitmasks2[i] = p.PlayersOnPickup;
            }
        }

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(bitmasks1[i], bitmasks2[i]);
        }
    }

    #endregion

    #region Additional Stress Tests

    [Fact]
    public void StressTest_ManyIterations_RemainsConsistent()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        // Use only 1 player on pickup - no unanimous vote possible with 2 players needed
        // Player 0 on pickup, Player 1 far away to prevent collection
        CreatePlayer(0, Fixed64.FromInt(100), Fixed64.FromInt(100));
        CreatePlayer(1, Fixed64.FromInt(500), Fixed64.FromInt(500)); // Far away
        CreatePickup(Fixed64.FromInt(100), Fixed64.FromInt(100));

        var ctx = CreateContext(100);

        // First run to get baseline
        _system.Tick(in ctx);
        Assert.True(_world.SuperPickupRows.TryGetRow(0, out var baseline));
        byte baselineBitmask = baseline.PlayersOnPickup;

        // Player 0 should be on pickup (bit 0 set), player 1 should not
        Assert.Equal(0b00000001, baselineBitmask);

        // Reset bitmask and run many times
        for (int i = 0; i < 1000; i++)
        {
            if (_world.SuperPickupRows.TryGetRow(0, out var pickup))
            {
                pickup.PlayersOnPickup = 0;
            }

            _system.Tick(in ctx);

            Assert.True(_world.SuperPickupRows.TryGetRow(0, out var after));
            Assert.Equal(baselineBitmask, after.PlayersOnPickup);
        }
    }

    [Fact]
    public void AllPowerTypes_UpgradeDeterministically()
    {
        var allPowers = new[]
        {
            PowerType.VelocityShooter,
            PowerType.ChainLightning,
            PowerType.Explosion,
            PowerType.SizeUp,
            PowerType.SpeedBoost,
            PowerType.Magnet,
            PowerType.Split,
            PowerType.BounceBoost,
            PowerType.FreezeAura,
            PowerType.GravityWell
        };

        foreach (var power in allPowers)
        {
            // Run 1
            _world.ResetAllTables();
            SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
            CreatePlayer(0, Fixed64.FromInt(100), Fixed64.FromInt(100));
            CreatePickup(Fixed64.FromInt(100), Fixed64.FromInt(100), power);
            CreatePuck(Fixed64.Zero, Fixed64.Zero);

            var ctx = CreateContext(100);
            _system.Tick(in ctx);

            _world.PuckRows.TryGetRow(0, out var puck1);
            byte level1 = GetLevelForPower(puck1, power);

            // Run 2
            _world.ResetAllTables();
            SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
            CreatePlayer(0, Fixed64.FromInt(100), Fixed64.FromInt(100));
            CreatePickup(Fixed64.FromInt(100), Fixed64.FromInt(100), power);
            CreatePuck(Fixed64.Zero, Fixed64.Zero);

            _system.Tick(in ctx);

            _world.PuckRows.TryGetRow(0, out var puck2);
            byte level2 = GetLevelForPower(puck2, power);

            Assert.Equal(level1, level2);
            Assert.Equal(GameConfig.SuperPickup.LevelsGranted, level1);
        }
    }

    #endregion

    #region WaveState Specific Tests

    [Fact]
    public void WaveState_AfterCollection_HasCorrectValues()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePlayer(0, Fixed64.FromInt(100), Fixed64.FromInt(100));
        CreatePickup(Fixed64.FromInt(100), Fixed64.FromInt(100), PowerType.Explosion);
        CreatePuck(Fixed64.Zero, Fixed64.Zero);

        int testFrame = 500;
        var ctx = CreateContext(testFrame);
        _system.Tick(in ctx);

        _world.WaveStateRows.TryGetRow(0, out var state);

        // Verify exact values
        Assert.Equal(GameConfig.SuperPickup.PhaseCollected, state.SuperPickupPhase);
        Assert.Equal(testFrame, state.SuperPickupPhaseStartFrame);
        Assert.Equal((byte)PowerType.Explosion, state.CollectedPowerType);
    }

    [Fact]
    public void WaveState_Hash_IsDeterministic_AfterCollection()
    {
        // Run 1
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePlayer(0, Fixed64.FromInt(100), Fixed64.FromInt(100));
        CreatePickup(Fixed64.FromInt(100), Fixed64.FromInt(100), PowerType.Magnet);
        CreatePuck(Fixed64.Zero, Fixed64.Zero);

        var ctx = CreateContext(250);
        _system.Tick(in ctx);

        ulong hash1 = _world.WaveStateRows.ComputeStateHash();
        _world.WaveStateRows.TryGetRow(0, out var state1);
        byte phase1 = state1.SuperPickupPhase;
        int startFrame1 = state1.SuperPickupPhaseStartFrame;
        byte powerType1 = state1.CollectedPowerType;

        // Run 2
        _world.ResetAllTables();
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePlayer(0, Fixed64.FromInt(100), Fixed64.FromInt(100));
        CreatePickup(Fixed64.FromInt(100), Fixed64.FromInt(100), PowerType.Magnet);
        CreatePuck(Fixed64.Zero, Fixed64.Zero);

        _system.Tick(in ctx);

        ulong hash2 = _world.WaveStateRows.ComputeStateHash();
        _world.WaveStateRows.TryGetRow(0, out var state2);
        byte phase2 = state2.SuperPickupPhase;
        int startFrame2 = state2.SuperPickupPhaseStartFrame;
        byte powerType2 = state2.CollectedPowerType;

        Assert.Equal(hash1, hash2);
        Assert.Equal(phase1, phase2);
        Assert.Equal(startFrame1, startFrame2);
        Assert.Equal(powerType1, powerType2);
    }

    [Fact]
    public void WaveState_Hash_IsDeterministic_AfterTimeout()
    {
        // Run 1
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePickup(Fixed64.Zero, Fixed64.Zero);

        var ctx = CreateContext(GameConfig.SuperPickup.TimeoutFrames);
        _system.Tick(in ctx);

        ulong hash1 = _world.WaveStateRows.ComputeStateHash();

        // Run 2
        _world.ResetAllTables();
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
        CreatePickup(Fixed64.Zero, Fixed64.Zero);

        _system.Tick(in ctx);

        ulong hash2 = _world.WaveStateRows.ComputeStateHash();

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void WaveState_Hash_IsDeterministic_AfterCelebrationEnd()
    {
        // Run 1
        SetupWaveState(GameConfig.SuperPickup.PhaseCollected, 0);

        var ctx = CreateContext(GameConfig.SuperPickup.CelebrationFrames);
        _system.Tick(in ctx);

        ulong hash1 = _world.WaveStateRows.ComputeStateHash();

        // Run 2
        _world.ResetAllTables();
        SetupWaveState(GameConfig.SuperPickup.PhaseCollected, 0);

        _system.Tick(in ctx);

        ulong hash2 = _world.WaveStateRows.ComputeStateHash();

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void WaveState_NoModification_WhenPhaseInactive()
    {
        SetupWaveState(GameConfig.SuperPickup.PhaseInactive, 0);
        CreatePickup(Fixed64.Zero, Fixed64.Zero);
        CreatePlayer(0, Fixed64.Zero, Fixed64.Zero);

        ulong hashBefore = _world.WaveStateRows.ComputeStateHash();

        var ctx = CreateContext(100);
        _system.Tick(in ctx);

        ulong hashAfter = _world.WaveStateRows.ComputeStateHash();

        // WaveState should be unchanged when phase is inactive
        Assert.Equal(hashBefore, hashAfter);
    }

    [Fact]
    public void WaveState_CollectedPowerType_MatchesPickupPowerType()
    {
        var allPowers = new[]
        {
            PowerType.VelocityShooter,
            PowerType.ChainLightning,
            PowerType.Explosion,
            PowerType.SizeUp,
            PowerType.SpeedBoost,
            PowerType.Magnet,
            PowerType.Split,
            PowerType.BounceBoost,
            PowerType.FreezeAura,
            PowerType.GravityWell
        };

        foreach (var power in allPowers)
        {
            _world.ResetAllTables();
            SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);
            CreatePlayer(0, Fixed64.FromInt(100), Fixed64.FromInt(100));
            CreatePickup(Fixed64.FromInt(100), Fixed64.FromInt(100), power);
            CreatePuck(Fixed64.Zero, Fixed64.Zero);

            var ctx = CreateContext(100);
            _system.Tick(in ctx);

            _world.WaveStateRows.TryGetRow(0, out var state);
            Assert.Equal((byte)power, state.CollectedPowerType);
        }
    }

    #endregion

    #region StableId Recycling Tests

    /// <summary>
    /// Verifies that stableIds are recycled (LIFO order) when entities are freed,
    /// keeping stableIds bounded within Capacity for long-running games.
    /// </summary>
    [Fact]
    public void StableIdRecycling_ReusesFreedIds()
    {
        // With stableId recycling, freed stableIds are reused instead of growing unboundedly
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        // Wave 1: allocate 3 pickups (stableIds 0, 1, 2)
        var h1 = CreatePickup(Fixed64.Zero, Fixed64.Zero);
        var h2 = CreatePickup(Fixed64.FromInt(100), Fixed64.Zero);
        var h3 = CreatePickup(Fixed64.FromInt(200), Fixed64.Zero);

        Assert.Equal(0, h1.StableId);
        Assert.Equal(1, h2.StableId);
        Assert.Equal(2, h3.StableId);

        // Free them (simulating wave end timeout)
        _world.SuperPickupRows.Free(h1);
        _world.SuperPickupRows.Free(h2);
        _world.SuperPickupRows.Free(h3);

        // Wave 2: allocate 3 more - should REUSE stableIds 2, 1, 0 (LIFO order)
        var h4 = CreatePickup(Fixed64.FromInt(300), Fixed64.Zero);
        var h5 = CreatePickup(Fixed64.FromInt(400), Fixed64.Zero);
        var h6 = CreatePickup(Fixed64.FromInt(500), Fixed64.Zero);

        // StableIds are recycled in LIFO order (last freed = first reused)
        Assert.Equal(2, h4.StableId); // Reused from h3
        Assert.Equal(1, h5.StableId); // Reused from h2
        Assert.Equal(0, h6.StableId); // Reused from h1

        _world.SuperPickupRows.Free(h4);
        _world.SuperPickupRows.Free(h5);
        _world.SuperPickupRows.Free(h6);

        // Wave 3: allocate 3 more - again reuses stableIds
        var h7 = CreatePickup(Fixed64.FromInt(600), Fixed64.Zero);
        var h8 = CreatePickup(Fixed64.FromInt(700), Fixed64.Zero);
        var h9 = CreatePickup(Fixed64.FromInt(800), Fixed64.Zero);

        // StableIds stay bounded, never exceed Capacity
        Assert.True(h7.StableId < 8);
        Assert.True(h8.StableId < 8);
        Assert.True(h9.StableId < 8);
    }

    [Fact]
    public void SnapshotRestore_WithRecycledStableIds_WorksCorrectly()
    {
        // Test that snapshot/restore works correctly when stableIds are recycled

        _world.ResetAllTables();
        SetupWaveState(GameConfig.SuperPickup.PhaseActive, 0);

        // Wave 1: allocate 3 pickups (stableIds 0, 1, 2), then free them
        var h1 = CreatePickup(Fixed64.FromInt(0), Fixed64.Zero);
        var h2 = CreatePickup(Fixed64.FromInt(100), Fixed64.Zero);
        var h3 = CreatePickup(Fixed64.FromInt(200), Fixed64.Zero);

        _world.SuperPickupRows.Free(h1);
        _world.SuperPickupRows.Free(h2);
        _world.SuperPickupRows.Free(h3);

        // Wave 2: allocate 3 pickups - with recycling, these reuse stableIds 2, 1, 0
        var h4 = CreatePickup(Fixed64.FromInt(300), Fixed64.Zero);
        var h5 = CreatePickup(Fixed64.FromInt(400), Fixed64.Zero);
        var h6 = CreatePickup(Fixed64.FromInt(500), Fixed64.Zero);

        // Verify recycling occurred (LIFO order)
        Assert.Equal(2, h4.StableId);
        Assert.Equal(1, h5.StableId);
        Assert.Equal(0, h6.StableId);

        // Take snapshot with 3 pickups (stableIds 2, 1, 0)
        var snapshot = new DieDrifterDie.Infrastructure.Rollback.WorldSnapshot(_world);
        byte[] buffer = new byte[_world.TotalSlabSize + _world.TotalMetaSize + 100];
        snapshot.SerializeWorld(100, buffer);

        int countBeforeModify = _world.SuperPickupRows.Count;
        Assert.Equal(3, countBeforeModify);

        // CONTINUE RUNNING: Free h5 (stableId 1), which should be undone by restore
        _world.SuperPickupRows.Free(h5);
        Assert.Equal(2, _world.SuperPickupRows.Count);

        // RESTORE: Roll back
        snapshot.DeserializeWorld(buffer);

        int countAfterRestore = _world.SuperPickupRows.Count;
        Assert.Equal(3, countAfterRestore); // Should be 3 again

        // Try to access h5 (was freed after snapshot, should be restored)
        var row5 = _world.SuperPickupRows.GetRow(h5);
        Assert.True(row5.IsValid, "StableId 1 should be valid after restore");
    }

    #endregion
}
