using System.Collections.Generic;
using System.Diagnostics;
using Pure.DI;
using DieDrifterDie.Presentation.Entities;
using DieDrifterDie.GameData;
using DieDrifterDie.Infrastructure.Rollback;
using DieDrifterDie.Simulation;
using DieDrifterDie.Simulation.Components;
using DieDrifterDie.Simulation.Systems;
using DieDrifterDie.Simulation.Systems.SimTable;
using Core;
using DerpTech.Rollback;
using Pooled.Runtime;
using static Pure.DI.Lifetime;

namespace DieDrifterDie;

partial class GameComposition
{
    [Conditional("DI")]
    static void SetupSimulation() => DI.Setup()
        // Simulation world and core components
        .Bind().As(Singleton).To<SimWorld>()
        .Bind().As(Singleton).To<EntitySpawner>()
        // DerivedSystemRunner - generated from Setup method in DerivedSystemRunner.cs
        // Constructor is generated, we just need to wire up dependencies
        .Bind().As(Singleton).To(ctx =>
        {
            ctx.Inject(out SimWorld simWorld);
            return new DerivedSystemRunner(simWorld);
        })
        .Bind<MultiPlayerInputBuffer<GameInput>>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out PlayerConfig config);
            return new MultiPlayerInputBuffer<GameInput>(InputBufferFrames, config.MaxPlayers);
        })
        .Bind<RollbackManager<GameInput>>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out SimWorld simWorld);
            ctx.Inject(out MultiPlayerInputBuffer<GameInput> inputBuffer);
            ctx.Inject(out PlayerConfig config);
            ctx.Inject(out Serilog.ILogger logger);

            var rm = new RollbackManager<GameInput>(simWorld, inputBuffer, logger);
            rm.SetPlayerCount(config.PlayerCount);
            rm.SetLocalPlayerId(config.LocalPlayerSlot);
            return rm;
        })
        // Game data (static data tables)
        .Bind().As(Singleton).To(ctx => GameDataManagerFactory.Create())
        // Simulation services
        .Bind<IPoolRegistry>().As(Singleton).To<World>()
        // SimTable systems
        .Bind().As(Singleton).To<CountdownSystem>()

        // System list (order matters for determinism)
        .Bind<IReadOnlyList<SimTableSystem>>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out CountdownSystem countdown);
            return new List<SimTableSystem>
            {
                countdown,
            };
        })
        .Bind().As(Singleton).To<GameSimulation>()
        .Bind<ReplayManager<GameInput>>().As(Singleton).To(ctx =>
        {
            ctx.Inject(out ReplayConfig replayConfig);
            ctx.Inject(out RollbackManager<GameInput> rollbackManager);
            ctx.Inject(out PlayerConfig playerConfig);

            var replayManager = new ReplayManager<GameInput>();
            if (!string.IsNullOrEmpty(replayConfig.ReplayFilePath))
            {
                int playerCount = replayManager.StartReplay(replayConfig.ReplayFilePath);
                rollbackManager.SetPlayerCount(playerCount);
            }
            else
            {
                // Always record - use explicit path or generate default with timestamp and player ID
                string recordPath = replayConfig.RecordFilePath;
                if (string.IsNullOrEmpty(recordPath))
                {
                    // Ensure Logs directory exists
                    System.IO.Directory.CreateDirectory("Logs");
                    string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    recordPath = $"Logs/replay_{timestamp}_p{playerConfig.LocalPlayerSlot}.bin";
                }
                // Defer recording start until after session seed is known (in Game.Restart)
                // Pass local player slot so RestartRecording can generate new paths
                replayManager.SetPendingRecordPath(recordPath, playerConfig.LocalPlayerSlot);
            }
            return replayManager;
        });
}
