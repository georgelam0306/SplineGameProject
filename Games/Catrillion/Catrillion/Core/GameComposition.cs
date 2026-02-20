using System.Diagnostics;
using Friflo.Engine.ECS;
using Pure.DI;
using Serilog;
using Catrillion.AppState;
using Catrillion.Core;
using Catrillion.OnlineServices;
using Catrillion.Rollback;
using Catrillion.Simulation;
using Catrillion.Simulation.Components;
using Catrillion.Stores;
using Catrillion.GameData.Schemas;
using DerpTech.Rollback;
using Networking;
using static Pure.DI.Lifetime;

namespace Catrillion;

partial class GameComposition
{
    internal const int InputBufferFrames = 64;

    [Conditional("DI")]
    static void Setup() => DI.Setup()
        // Runtime arguments
        .Arg<PlayerConfig>("playerConfig")
        .Arg<NetworkService>("networkService")
        .Arg<ReplayConfig>("replayConfig")
        .Arg<SessionSeed>("sessionSeed")
        .Arg<InitialCoordinator>("isInitialCoordinator")
        .Arg<AppEventBus>("appEventBus")
        .Arg<ILogger>("logger")
        .Arg<InputStore>("inputStore")
        .Arg<GameDataManager<GameDocDb>>("gameDataManager")
        .Arg<GameplayStore>("gameplayStore")
        .Arg<NetworkStore>("networkStore")
        .Bind().As(Singleton).To<NetworkCoordinator>()
        .Bind<NetworkCoordinator>("Configured").As(Singleton).To(ctx =>
        {
            ctx.Inject(out NetworkCoordinator coordinator);
            ctx.Inject(out PlayerConfig config);
            ctx.Inject(out NetworkStore networkStore);
            coordinator.SetPlayerInfo(config.PlayerCount, config.LocalPlayerSlot);
            networkStore.SetLocalSlot(config.LocalPlayerSlot);
            return coordinator;
        })
        // Core infrastructure
        .Bind().As(Singleton).To<EntityStore>()
        // Game root
        .Bind<Game>().As(Singleton).To<Game>()
        .Root<Game>("Game");
}
