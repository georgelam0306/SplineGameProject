using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Core;
using DerpLib.Memory;
using DerpLib.Rendering;
using DerpLib.Sdf;
using DerpLib.Shaders;

namespace DerpLib.Presentation;

/// <summary>
/// Holds per-viewport Vulkan resources: window, surface, swapchain, command recording, sync.
/// For multi-viewport rendering, each native OS window has its own ViewportResources.
/// </summary>
public sealed class ViewportResources : IDisposable
{
    private readonly ILogger _log;
    private readonly VkInstance _vkInstance;
    private readonly VkDevice _vkDevice;
    private readonly MemoryAllocator _memoryAllocator;
    private readonly DescriptorCache _descriptorCache;
    private readonly Shaders.PipelineCache _pipelineCache;
    private readonly int _framesInFlight;

    // Per-viewport resources
    public Window Window { get; private set; }
    public VkSurface Surface { get; private set; }
    public Swapchain Swapchain { get; private set; }
    public RenderPassManager RenderPassManager { get; private set; }
    public CommandRecorder CommandRecorder { get; private set; }
    public SyncManager SyncManager { get; private set; }

    // SDF rendering (optional, per-viewport)
    public SdfRenderer? SdfRenderer { get; private set; }
    public SdfBuffer? SdfBuffer => SdfRenderer?.Buffer;

    // State
    public bool IsInitialized { get; private set; }
    public bool IsPrimary { get; }
    public int Id { get; }
    private bool _ownsWindow;

    private static int _nextId;

    /// <summary>
    /// Create viewport resources for a primary viewport with an existing window.
    /// </summary>
    public ViewportResources(
        ILogger log,
        VkInstance vkInstance,
        VkDevice vkDevice,
        MemoryAllocator memoryAllocator,
        DescriptorCache descriptorCache,
        Shaders.PipelineCache pipelineCache,
        int framesInFlight,
        Window existingWindow)
    {
        _log = log;
        _vkInstance = vkInstance;
        _vkDevice = vkDevice;
        _memoryAllocator = memoryAllocator;
        _descriptorCache = descriptorCache;
        _pipelineCache = pipelineCache;
        _framesInFlight = framesInFlight;
        IsPrimary = true;
        Id = _nextId++;
        _ownsWindow = false;

        Window = existingWindow;
        Surface = new VkSurface(log, vkInstance);
        Swapchain = new Swapchain(log);
        RenderPassManager = new RenderPassManager(log, vkDevice);
        CommandRecorder = new CommandRecorder(log, vkDevice, framesInFlight);
        SyncManager = new SyncManager(log, vkDevice, framesInFlight);
    }

    /// <summary>
    /// Create viewport resources for a secondary viewport (new window will be created).
    /// </summary>
    public ViewportResources(
        ILogger log,
        VkInstance vkInstance,
        VkDevice vkDevice,
        MemoryAllocator memoryAllocator,
        DescriptorCache descriptorCache,
        Shaders.PipelineCache pipelineCache,
        int framesInFlight,
        int width,
        int height,
        string title)
    {
        _log = log;
        _vkInstance = vkInstance;
        _vkDevice = vkDevice;
        _memoryAllocator = memoryAllocator;
        _descriptorCache = descriptorCache;
        _pipelineCache = pipelineCache;
        _framesInFlight = framesInFlight;
        IsPrimary = false;
        Id = _nextId++;
        _ownsWindow = true;

        // Create new window for this viewport (undecorated - no OS title bar)
        Window = new Window(log, width, height, title, decorated: false);
        Surface = new VkSurface(log, vkInstance);
        Swapchain = new Swapchain(log);
        RenderPassManager = new RenderPassManager(log, vkDevice);
        CommandRecorder = new CommandRecorder(log, vkDevice, framesInFlight);
        SyncManager = new SyncManager(log, vkDevice, framesInFlight);
    }

    /// <summary>
    /// Initialize all Vulkan resources for this viewport.
    /// For primary viewport, window should already be initialized.
    /// For secondary viewport, window will be initialized here.
    /// </summary>
    public void Initialize(int? x = null, int? y = null)
    {
        if (IsInitialized)
            throw new InvalidOperationException("Already initialized");

        // Initialize window if we own it (secondary viewport)
        if (_ownsWindow)
        {
            Window.Initialize();
            if (x.HasValue && y.HasValue)
                Window.SetPosition(x.Value, y.Value);
        }

        // Create surface from window
        Surface.Initialize(Window.NativeWindow);

        // Create swapchain
        Swapchain.Initialize(_vkInstance, _vkDevice, Surface, (uint)Window.FramebufferWidth, (uint)Window.FramebufferHeight);

        // Create render passes, depth buffer, framebuffers
        RenderPassManager.Initialize(Swapchain.ImageFormat, Swapchain.ImageViews, Swapchain.Extent);

        // Create command pools and buffers
        CommandRecorder.Initialize();

        // Create synchronization primitives
        SyncManager.Initialize();

        IsInitialized = true;
        _log.Debug("ViewportResources {Id} initialized: {Width}x{Height} (primary={Primary})",
            Id, Window.Width, Window.Height, IsPrimary);
    }

    /// <summary>
    /// Initialize SDF rendering for this viewport.
    /// </summary>
    public void InitializeSdf(ComputeShader sdfShader, int maxCommands = 8192)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Viewport not initialized");

        if (SdfRenderer != null)
            throw new InvalidOperationException("SDF already initialized for this viewport");

        SdfRenderer = new SdfRenderer(
            _log,
            _vkDevice,
            _memoryAllocator,
            _descriptorCache,
            _pipelineCache,
            (uint)Window.FramebufferWidth,
            (uint)Window.FramebufferHeight,
            maxCommands);

        SdfRenderer.SetShader(sdfShader);

        _log.Debug("SDF initialized for viewport {Id}: {Width}x{Height}, max {MaxCommands} commands",
            Id, Window.Width, Window.Height, maxCommands);
    }

    /// <summary>
    /// Handle window resize.
    /// </summary>
    public void HandleResize()
    {
        if (!IsInitialized || Window.Width == 0 || Window.Height == 0)
            return;

        _vkDevice.Vk.DeviceWaitIdle(_vkDevice.Device);
        Swapchain.Recreate((uint)Window.FramebufferWidth, (uint)Window.FramebufferHeight);
        RenderPassManager.Resize(Swapchain.ImageViews, Swapchain.Extent);

        // Resize SDF if enabled
        SdfRenderer?.Resize((uint)Window.FramebufferWidth, (uint)Window.FramebufferHeight);
    }

    /// <summary>
    /// Update the OS window position and size. Intended for multi-viewport synchronization.
    /// </summary>
    public void UpdatePositionAndSize(int x, int y, int width, int height)
    {
        if (!IsInitialized)
        {
            return;
        }

        var currentPos = Window.ScreenPosition;
        if ((int)currentPos.X != x || (int)currentPos.Y != y)
        {
            Window.SetPosition(x, y);
        }

        if (Window.Width != width || Window.Height != height)
        {
            Window.SetSize(width, height);
        }
    }

    /// <summary>
    /// Begin a new frame for this viewport.
    /// Returns false if frame should be skipped.
    /// </summary>
    public bool BeginFrame(out uint imageIndex)
    {
        imageIndex = 0;

        // Skip if minimized
        if (Window.Width == 0 || Window.Height == 0)
            return false;

        // Wait for previous frame
        SyncManager.WaitForCurrentFence();

        // Acquire next image
        var acquireResult = Swapchain.AcquireNextImage(
            SyncManager.CurrentImageAvailableSemaphore,
            out imageIndex);

        if (acquireResult == Result.ErrorOutOfDateKhr)
        {
            HandleResize();
            return false;
        }

        // Reset fence and begin recording
        SyncManager.ResetCurrentFence();
        CommandRecorder.SetCurrentFrame(SyncManager.CurrentFrame);
        CommandRecorder.BeginRecording();

        return true;
    }

    /// <summary>
    /// End frame and present for this viewport.
    /// </summary>
    public unsafe void EndFrame(uint imageIndex)
    {
        var commandBuffer = CommandRecorder.EndRecording();

        // Submit
        var waitSemaphore = SyncManager.CurrentImageAvailableSemaphore;
        var signalSemaphore = SyncManager.CurrentRenderFinishedSemaphore;
        var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore
        };

        var fence = SyncManager.CurrentFence;
        var result = _vkDevice.Vk.QueueSubmit(_vkDevice.GraphicsQueue, 1, &submitInfo, fence);
        if (result != Result.Success)
        {
            throw new Exception($"Failed to submit draw command buffer: {result}");
        }

        // Present
        var presentResult = Swapchain.Present(
            _vkDevice.GraphicsQueue,
            SyncManager.CurrentRenderFinishedSemaphore,
            imageIndex);

        if (presentResult == Result.ErrorOutOfDateKhr || presentResult == Result.SuboptimalKhr)
        {
            HandleResize();
        }

        SyncManager.AdvanceFrame();
    }

    /// <summary>
    /// Render SDF and blit to swapchain.
    /// </summary>
    public void RenderSdf(uint imageIndex)
    {
        if (SdfRenderer == null || SdfBuffer == null || SdfBuffer.Count == 0)
            return;

        // Handle resize
        int framebufferWidth = Window.FramebufferWidth;
        int framebufferHeight = Window.FramebufferHeight;
        SdfRenderer.Resize((uint)framebufferWidth, (uint)framebufferHeight);

        // Build tile data
        SdfBuffer.Build(framebufferWidth, framebufferHeight);

        // Flush to GPU
        SdfBuffer.Flush(SyncManager.CurrentFrame);

        // Dispatch compute shader
        var cmd = CommandRecorder.CurrentCommandBuffer;
        SdfRenderer.Dispatch(cmd, SyncManager.CurrentFrame);

        // Blit to swapchain
        SdfRenderer.BlitToSwapchain(cmd,
            Swapchain.Images[imageIndex],
            Swapchain.Extent.Width,
            Swapchain.Extent.Height);

        // Reset for next frame
        SdfRenderer.Reset();
    }

    public void Dispose()
    {
        if (!IsInitialized) return;

        _log.Debug("Disposing ViewportResources {Id}", Id);
        _vkDevice.Vk.DeviceWaitIdle(_vkDevice.Device);

        SdfRenderer?.Dispose();
        SyncManager.Dispose();
        CommandRecorder.Dispose();
        RenderPassManager.Dispose();
        Swapchain.Dispose();
        Surface.Dispose();

        // Only dispose window if we own it (secondary viewport)
        if (_ownsWindow)
            Window.Dispose();

        IsInitialized = false;
    }
}
