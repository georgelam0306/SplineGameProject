using Pure.DI;
using Serilog;
using DerpLib.AssetPipeline;
using DerpLib.Assets;
using DerpLib.Core;
using DerpLib.Memory;
using DerpLib.Presentation;
using DerpLib.Rendering;
using DerpLib.Shaders;
using DerpLib.Vfs;
using PipelineCache = DerpLib.Shaders.PipelineCache;
using RenderPassManager = DerpLib.Rendering.RenderPassManager;

namespace DerpLib;

/// <summary>
/// Configuration for engine initialization.
/// </summary>
public readonly record struct EngineConfig(
    int Width,
    int Height,
    string Title,
    int FramesInFlight = 2,
    int MaxTextures = 256,
    int MaxShadowTextures = 16,
    int MaxInstances = 10_000_000,
    int MaxMeshTypes = 256);

/// <summary>
/// Pure.DI composition for windowed engine.
/// </summary>
internal partial class EngineComposition
{
    static void Setup() => DI.Setup(nameof(EngineComposition))
        // Runtime configuration
        .Arg<EngineConfig>("config")

        // Logging
        .Bind<ILogger>().As(Lifetime.Singleton).To(_ =>
            new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger())

        // Virtual File System
        .Bind<VirtualFileSystem>().As(Lifetime.Singleton).To(ctx =>
        {
            ctx.Inject<ILogger>(out var log);
            var vfs = new VirtualFileSystem(log);
            var dataPath = Path.Combine(AppContext.BaseDirectory, "data");
            vfs.MountPhysical("/data", dataPath);
            return vfs;
        })

        // Content Manager
        .Bind<ContentManager>().As(Lifetime.Singleton).To(ctx =>
        {
            ctx.Inject<VirtualFileSystem>(out var vfs);
            ctx.Inject<ILogger>(out var log);
            var content = ContentModule.CreateContentManager(vfs, log);
            Shader.SetContentManager(content);
            return content;
        })

        // Core
        .Bind<VkInstance>().As(Lifetime.Singleton).To<VkInstance>()
        .Bind<DeviceCapabilities>().As(Lifetime.Singleton).To<DeviceCapabilities>()
        .Bind<VkDevice>().As(Lifetime.Singleton).To<VkDevice>()

        // Presentation
        .Bind<Window>().As(Lifetime.Singleton).To(ctx =>
        {
            ctx.Inject<ILogger>(out var log);
            ctx.Inject<EngineConfig>(out var config);
            return new Window(log, config.Width, config.Height, config.Title);
        })
        .Bind<VkSurface>().As(Lifetime.Singleton).To<VkSurface>()
        .Bind<Swapchain>().As(Lifetime.Singleton).To<Swapchain>()

        // Rendering
        .Bind<RenderPassManager>().As(Lifetime.Singleton).To<RenderPassManager>()
        .Bind<CommandRecorder>().As(Lifetime.Singleton).To(ctx =>
        {
            ctx.Inject<ILogger>(out var log);
            ctx.Inject<VkDevice>(out var device);
            ctx.Inject<EngineConfig>(out var config);
            return new CommandRecorder(log, device, config.FramesInFlight);
        })
        .Bind<SyncManager>().As(Lifetime.Singleton).To(ctx =>
        {
            ctx.Inject<ILogger>(out var log);
            ctx.Inject<VkDevice>(out var device);
            ctx.Inject<EngineConfig>(out var config);
            return new SyncManager(log, device, config.FramesInFlight);
        })

        // Memory
        .Bind<MemoryAllocator>().As(Lifetime.Singleton).To<MemoryAllocator>()
        .Bind<TransformBuffer>().As(Lifetime.Singleton).To(ctx =>
        {
            ctx.Inject<ILogger>(out var log);
            ctx.Inject<MemoryAllocator>(out var allocator);
            ctx.Inject<EngineConfig>(out var config);
            return new TransformBuffer(log, allocator, config.FramesInFlight, config.MaxInstances);
        })
        .Bind<IndirectBatcher>().As(Lifetime.Singleton).To(ctx =>
        {
            ctx.Inject<ILogger>(out var log);
            ctx.Inject<MemoryAllocator>(out var allocator);
            ctx.Inject<MeshRegistry>(out var meshRegistry);
            ctx.Inject<EngineConfig>(out var config);
            return new IndirectBatcher(log, allocator, meshRegistry, config.FramesInFlight, config.MaxInstances, config.MaxMeshTypes);
        })
        .Bind<TextureArray>().As(Lifetime.Singleton).To(ctx =>
        {
            ctx.Inject<ILogger>(out var log);
            ctx.Inject<VkDevice>(out var device);
            ctx.Inject<MemoryAllocator>(out var allocator);
            ctx.Inject<EngineConfig>(out var config);
            return new TextureArray(log, device, allocator, config.MaxTextures);
        })
        .Bind<MeshRegistry>().As(Lifetime.Singleton).To<MeshRegistry>()

        // Descriptors
        .Bind<DescriptorCache>().As(Lifetime.Singleton).To(ctx =>
        {
            ctx.Inject<ILogger>(out var log);
            ctx.Inject<VkDevice>(out var device);
            ctx.Inject<EngineConfig>(out var config);
            return new DescriptorCache(log, device, config.FramesInFlight);
        })

        // Shaders (PipelineCache no longer depends on DescriptorCache - Engine sets the layout)
        .Bind<PipelineCache>().As(Lifetime.Singleton).To<PipelineCache>()

        // Compute
        .Bind<ComputeDispatcher>().As(Lifetime.Singleton).To<ComputeDispatcher>()
        .Bind<ComputeTransforms>().As(Lifetime.Singleton).To(ctx =>
        {
            ctx.Inject<ILogger>(out var log);
            ctx.Inject<VkDevice>(out var device);
            ctx.Inject<MemoryAllocator>(out var allocator);
            ctx.Inject<PipelineCache>(out var pipelineCache);
            ctx.Inject<EngineConfig>(out var config);
            return new ComputeTransforms(log, device, allocator, pipelineCache, config.FramesInFlight, config.MaxInstances);
        })

        // Engine
        .Bind<EngineHost>().As(Lifetime.Singleton).To<EngineHost>()
        .Root<EngineHost>("Engine");
}
