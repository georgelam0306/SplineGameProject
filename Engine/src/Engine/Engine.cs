using Serilog;
using Silk.NET.Vulkan;
using DerpLib.AssetPipeline;
using DerpLib.Assets;
using DerpLib.Core;
using DerpLib.Diagnostics;
using DerpLib.Input;
using System.Diagnostics;
using DerpLib.Memory;
using DerpLib.Presentation;
using DerpLib.Rendering;
using DerpLib.Shaders;
using DerpLib.Vfs;
using Profiling;
using FlameProfiler;
using PipelineCache = DerpLib.Shaders.PipelineCache;

namespace DerpLib;

/// <summary>
/// Tracks the current phase of the render loop.
/// </summary>
public enum RenderPhase
{
    /// <summary>Between frames, no rendering in progress.</summary>
    Idle,
    /// <summary>BeginFrame called, ready for camera or rendering.</summary>
    FrameStarted,
}

/// <summary>
/// Pending configuration for the next render pass.
/// Accumulated by setup calls, consumed by BeginCamera.
/// </summary>
public struct PendingPassConfig
{
    /// <summary>Clear color for the next pass. Null means no clear (load previous).</summary>
    public (float R, float G, float B)? ClearColor;

    // Future: depth test, attachments, etc.
}

/// <summary>
/// Bundles all state needed during an active render pass.
/// Created at BeginCamera, consumed at EndCamera.
/// </summary>
public struct RenderPassContext
{
    public CommandBuffer Cmd;
    public Silk.NET.Vulkan.RenderPass RenderPass;
    public Extent2D Extent;
    public int FrameIndex;
    public System.Numerics.Matrix4x4 ViewProjection;
}

/// <summary>
/// Core engine class managing Vulkan infrastructure.
/// Use Derp static API for simple usage, or EngineHost directly for more control.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private readonly ILogger _log;
    private readonly EngineConfig _config;
    private readonly VirtualFileSystem _vfs;
    private readonly ContentManager _content;
    private readonly VkInstance _vkInstance;
    private readonly VkDevice _vkDevice;
    private readonly Window _window;
    private readonly VkSurface _vkSurface;
    private readonly Swapchain _swapchain;
    private readonly RenderPassManager _renderPassManager;
    private readonly CommandRecorder _commandRecorder;
    private readonly SyncManager _syncManager;
    private readonly MemoryAllocator _memoryAllocator;
    private readonly DescriptorCache _descriptorCache;
    private readonly TransformBuffer _transformBuffer;
    private readonly IndirectBatcher _indirectBatcher;
    private readonly TextureArray _textureArray;
    private readonly MeshRegistry _meshRegistry;
    private readonly PipelineCache _pipelineCache;
    private readonly ComputeDispatcher _computeDispatcher;
    private readonly ComputeTransforms _computeTransforms;
    private InstancedMeshBuffer? _instancedMeshBuffer;

    // Input
    private SdlGamepadState? _gamepadState;

    // Profiling
    private GpuTimingService? _gpuTimingService;
    private int _frameNumber;
    private long _cpuFrameStartTicks;

    // Bindless rendering resources
    private DescriptorSetLayout _bindlessLayout;
    private DescriptorSet[] _bindlessSets = null!;

    // Default shader for automatic batched rendering
    private Shader? _defaultShader;
    private readonly RenderState _renderState3D = new();
    private readonly RenderState _renderState2D = new();

    // Active render pass context (null when not in a render pass)
    private RenderPassContext? _activePass;

    private System.Numerics.Matrix4x4 _lastViewProjection;
    private bool _hasLastViewProjection;

    // Pending configuration for the next render pass
    private PendingPassConfig _pendingConfig;

    private bool _initialized;
    private uint _currentImageIndex;
    private bool _frameSkipped;
    private RenderPhase _phase = RenderPhase.Idle;
    private Fence[] _swapchainImageFences = [];

    // Public accessors
    public VkInstance VkInstance => _vkInstance;
    public VkDevice Device => _vkDevice;
    public MemoryAllocator MemoryAllocator => _memoryAllocator;
    public DescriptorCache DescriptorCache => _descriptorCache;
    public TransformBuffer TransformBuffer => _transformBuffer;
    public IndirectBatcher IndirectBatcher => _indirectBatcher;
    public TextureArray TextureArray => _textureArray;
    public MeshRegistry MeshRegistry => _meshRegistry;
    public PipelineCache PipelineCache => _pipelineCache;
    public ComputeDispatcher ComputeDispatcher => _computeDispatcher;
    public ComputeTransforms ComputeTransforms => _computeTransforms;
    public InstancedMeshBuffer? InstancedMeshBuffer => _instancedMeshBuffer;
    internal SdlGamepadState GamepadState => _gamepadState ?? throw new InvalidOperationException("Gamepad not initialized");
    public RenderPassManager RenderPassManager => _renderPassManager;
    public Swapchain Swapchain => _swapchain;
    public Window Window => _window;
    public ContentManager Content => _content;
    public VirtualFileSystem Vfs => _vfs;
    public int FrameIndex => _syncManager.CurrentFrame;
    public bool IsInRenderPass => _activePass.HasValue;
    public uint CurrentImageIndex => _currentImageIndex;

    /// <summary>
    /// Gets the bindless descriptor set for the specified frame.
    /// </summary>
    public DescriptorSet GetBindlessSet(int frameIndex) => _bindlessSets[frameIndex];

    /// <summary>
    /// Gets the number of frames in flight.
    /// </summary>
    public int FramesInFlight => _config.FramesInFlight;

    /// <summary>
    /// Number of mesh instances queued this frame.
    /// </summary>
    public int MeshInstanceCount => _indirectBatcher.InstanceCount;

    /// <summary>
    /// Number of textures loaded.
    /// </summary>
    public int TextureCount => _textureArray.Count;

    /// <summary>
    /// Register a mesh for high-frequency instanced rendering.
    /// Must be called before InitializeInstancedBuffer.
    /// </summary>
    public MeshHandle RegisterInstancedMesh(float[] vertexData, int vertexCount, uint[] indices, int capacity)
    {
        _instancedMeshBuffer ??= new InstancedMeshBuffer(
            _log, _memoryAllocator, _meshRegistry, _config.FramesInFlight);

        // Register in mesh registry first (uploads geometry)
        var tempHandle = _meshRegistry.RegisterMesh(vertexData, vertexCount, indices);
        if (!tempHandle.IsValid) return tempHandle;

        // Register in instanced buffer to get region base
        int regionBase = _instancedMeshBuffer.RegisterMesh(tempHandle.Id, capacity);

        // Return handle with instanced info
        return new MeshHandle(
            tempHandle.Id, tempHandle.VertexCount, tempHandle.IndexCount,
            tempHandle.VertexOffset, tempHandle.FirstIndex,
            regionBase, capacity);
    }

    /// <summary>
    /// Initialize the instanced mesh buffer. Call after all instanced meshes are registered.
    /// </summary>
    public void InitializeInstancedBuffer()
    {
        _instancedMeshBuffer?.Initialize();
    }

    public EngineHost(
        ILogger log,
        EngineConfig config,
        VirtualFileSystem vfs,
        ContentManager content,
        VkInstance vkInstance,
        VkDevice vkDevice,
        Window window,
        VkSurface vkSurface,
        Swapchain swapchain,
        RenderPassManager renderPassManager,
        CommandRecorder commandRecorder,
        SyncManager syncManager,
        MemoryAllocator memoryAllocator,
        DescriptorCache descriptorCache,
        TransformBuffer transformBuffer,
        IndirectBatcher indirectBatcher,
        TextureArray textureArray,
        MeshRegistry meshRegistry,
        PipelineCache pipelineCache,
        ComputeDispatcher computeDispatcher,
        ComputeTransforms computeTransforms)
    {
        _log = log;
        _config = config;
        _vfs = vfs;
        _content = content;
        _vkInstance = vkInstance;
        _vkDevice = vkDevice;
        _window = window;
        _vkSurface = vkSurface;
        _swapchain = swapchain;
        _renderPassManager = renderPassManager;
        _commandRecorder = commandRecorder;
        _syncManager = syncManager;
        _memoryAllocator = memoryAllocator;
        _descriptorCache = descriptorCache;
        _transformBuffer = transformBuffer;
        _indirectBatcher = indirectBatcher;
        _textureArray = textureArray;
        _meshRegistry = meshRegistry;
        _pipelineCache = pipelineCache;
        _computeDispatcher = computeDispatcher;
        _computeTransforms = computeTransforms;
    }

    public void Initialize()
    {
        if (_initialized)
            throw new InvalidOperationException("Already initialized");

        _log.Information("Initializing engine...");

        // 1. Initialize window first (needed for surface extensions)
        _window.Initialize();

        // 2. Initialize Vulkan instance with surface extensions
        var surfaceExtensions = VkSurface.GetRequiredExtensions(_window.NativeWindow);
        _vkInstance.Initialize(surfaceExtensions);

        // 3. Create surface
        _vkSurface.Initialize(_window.NativeWindow);

        // 4. Initialize device with surface (for present support check)
        _vkDevice.Initialize(_vkSurface.KhrSurface, _vkSurface.Surface);

        // 5. Create swapchain
        _swapchain.Initialize(_vkInstance, _vkDevice, _vkSurface, (uint)_window.FramebufferWidth, (uint)_window.FramebufferHeight);
        _swapchainImageFences = new Fence[_swapchain.Images.Length];

        // 6. Create render passes, depth buffer, and framebuffers
        _renderPassManager.Initialize(_swapchain.ImageFormat, _swapchain.ImageViews, _swapchain.Extent);

        // 7. Create command pools and buffers
        _commandRecorder.Initialize();

        // 8. Create synchronization primitives
        _syncManager.Initialize();

        // 9. Initialize descriptor cache
        _descriptorCache.Initialize();

        // 10. Initialize transform buffer (creates GPU buffers)
        _transformBuffer.Initialize();

        // 11. Initialize indirect batcher (creates instance + indirect buffers)
        _indirectBatcher.Initialize();

        // 12. Initialize texture array (creates sampler)
        _textureArray.Initialize();

        // 13. Initialize mesh registry (creates global vertex/index buffers)
        _meshRegistry.Initialize();

        // 14. Create bindless descriptor set layout
        ReadOnlySpan<DescriptorBinding> bindlessBindings = stackalloc DescriptorBinding[]
        {
            new(0, DescriptorType.CombinedImageSampler, (uint)_config.MaxTextures, ShaderStageFlags.FragmentBit,
                DescriptorBindingFlags.PartiallyBoundBit),
            new(1, DescriptorType.StorageBuffer, 1, ShaderStageFlags.VertexBit),
            new(2, DescriptorType.CombinedImageSampler, (uint)_config.MaxShadowTextures, ShaderStageFlags.FragmentBit,
                DescriptorBindingFlags.PartiallyBoundBit)
        };
        _bindlessLayout = _descriptorCache.GetOrCreateLayout(bindlessBindings);

        // 15. Create default white texture (index 0)
        _textureArray.LoadSolidColor(255, 255, 255, 255);

        // 16. Initialize pipeline cache with our bindless layout
        _pipelineCache.Initialize();
        _pipelineCache.SetDescriptorSetLayout(_bindlessLayout);
        Shader.SetDefaultCache(_pipelineCache);

        // 17. Initialize compute dispatcher
        _computeDispatcher.Initialize();
        ComputeShader.SetContentManager(_content);

        // 18. Initialize GPU matrix generation
        var matrixGenShader = ComputeShader.Load("matrix_gen");
        _computeTransforms.Initialize(matrixGenShader);

        // 19. Allocate persistent descriptor sets (one per frame-in-flight)
        _bindlessSets = new DescriptorSet[_config.FramesInFlight];
        for (int i = 0; i < _config.FramesInFlight; i++)
        {
            _bindlessSets[i] = _descriptorCache.AllocatePersistent(_bindlessLayout);

            // Write GPU-generated transforms to binding 1
            _descriptorCache.WriteBuffer(
                _bindlessSets[i],
                binding: 1,
                DescriptorType.StorageBuffer,
                _computeTransforms.GetTransformBuffer(i));

            // Write textures to binding 0
            _textureArray.WriteToDescriptorSet(_descriptorCache, _bindlessSets[i], 0);
        }

        // 20. Load default instanced shader
        _defaultShader = Shader.Load("instanced");

        // 21. Configure render states
        _renderState3D.Reset3D();
        _renderState2D.Reset2D();

        // 22. Initialize profiling services
        _gpuTimingService = new GpuTimingService(_vkDevice, _config.FramesInFlight, GpuScopes.Names);

        // Initialize CPU profiling service with generated scope names (always available, even if empty)
        var cpuProfiler = new ProfilingService(ProfileScopes.Names);
        cpuProfiler.Enabled = true;
        ProfilingService.Instance = cpuProfiler;

        // Initialize flame graph profiler
        var flameProfiler = new FlameProfilerService(ProfileScopes.Names);
        flameProfiler.Enabled = true;
        FlameProfilerService.Instance = flameProfiler;

        // 23. Initialize SDL gamepad subsystem
        _gamepadState = new SdlGamepadState(_log);
        _gamepadState.Initialize();

        _initialized = true;
        _log.Information("Engine initialized: {Width}x{Height} (framebuffer: {FbWidth}x{FbHeight}, scale: {Scale:F1}x)",
            _window.Width, _window.Height, _window.FramebufferWidth, _window.FramebufferHeight, _window.ContentScaleX);
    }

    public bool ShouldClose => _window.ShouldClose;

    public int ScreenWidth => _window.Width;

    public int ScreenHeight => _window.Height;

    public void PollEvents()
    {
        _window.PollEvents();
        _gamepadState?.Update();

        if (_window.ConsumeResized())
        {
            HandleResize();
        }
    }

    /// <summary>
    /// Begin a new frame. Returns false if frame should be skipped (e.g., minimized).
    /// </summary>
    public unsafe bool BeginFrame()
    {
        if (_phase != RenderPhase.Idle)
            throw new InvalidOperationException($"BeginFrame requires Idle phase, but currently in {_phase}");

        _frameSkipped = false;

        var flameProfiler = FlameProfilerService.Instance;
        if (flameProfiler != null && flameProfiler.IsFrameInProgress)
        {
            flameProfiler.CancelFrame();
        }

        // Skip if minimized
        if (_window.Width == 0 || _window.Height == 0)
        {
            _frameSkipped = true;
            return false;
        }

        // Start flame profiler frame first (before any ProfileScope scopes).
        flameProfiler?.BeginFrame(_frameNumber);

        using var _scope = ProfileScope.Begin(ProfileScopes.BeginFrame);

        // Start frame wall-clock timing (covers the whole frame on the main thread)
        _cpuFrameStartTicks = Stopwatch.GetTimestamp();
        AllocationTracker.BeginFrame();

        // Wait for previous frame
        {
            using var _waitFenceScope = ProfileScope.Begin(ProfileScopes.BeginFrameWaitFence);
            _syncManager.WaitForCurrentFence();
        }

        // Read GPU timing results from the completed frame
        {
            using var _gpuReadbackScope = ProfileScope.Begin(ProfileScopes.BeginFrameGpuReadback);
            _gpuTimingService?.ReadResults(_syncManager.CurrentFrame);
        }

        // Acquire next image
        Result acquireResult;
        {
            using var _acquireScope = ProfileScope.Begin(ProfileScopes.BeginFrameAcquireImage);
            acquireResult = _swapchain.AcquireNextImage(
                _syncManager.CurrentImageAvailableSemaphore,
                out _currentImageIndex);
        }

        if (acquireResult == Result.ErrorOutOfDateKhr)
        {
            HandleResize();
            _frameSkipped = true;
            flameProfiler?.CancelFrame();
            return false;
        }

        // Frames-in-flight fencing is not enough when swapchain image count differs.
        // Ensure the acquired image is no longer in use by an older submission.
        var imageFence = _swapchainImageFences[_currentImageIndex];
        if (imageFence.Handle != 0)
        {
            _vkDevice.Vk.WaitForFences(_vkDevice.Device, 1, &imageFence, true, ulong.MaxValue);
        }
        _swapchainImageFences[_currentImageIndex] = _syncManager.CurrentFence;

        // Reset fence and begin recording
        {
            using var _recordScope = ProfileScope.Begin(ProfileScopes.BeginFrameCommandRecording);
            _syncManager.ResetCurrentFence();
            _commandRecorder.SetCurrentFrame(_syncManager.CurrentFrame);
            _commandRecorder.BeginRecording();
        }

        // Reset GPU timing queries for this frame
        {
            using var _gpuTimingScope = ProfileScope.Begin(ProfileScopes.BeginFrameGpuTimingBegin);
            _gpuTimingService?.BeginFrame(_commandRecorder.CurrentCommandBuffer, _syncManager.CurrentFrame);
        }

        // Reset transient resources for immediate-mode drawing
        {
            using var _resetScope = ProfileScope.Begin(ProfileScopes.BeginFrameResetTransient);
            _computeTransforms.Reset();
            _indirectBatcher.Reset();
            _instancedMeshBuffer?.Reset();
        }

        _phase = RenderPhase.FrameStarted;
        return true;
    }

    /// <summary>
    /// Begin a simple render pass with a clear color and default ortho projection.
    /// For more control, use BeginCamera3D or BeginCamera2D.
    /// </summary>
    public void BeginRenderPass(float r, float g, float b)
    {
        if (_frameSkipped) return;

        if (_phase != RenderPhase.FrameStarted)
            throw new InvalidOperationException($"BeginRenderPass requires FrameStarted phase, but currently in {_phase}");

        if (_activePass.HasValue)
            throw new InvalidOperationException("Already in a render pass");

        var cmd = _commandRecorder.CurrentCommandBuffer;
        var renderPass = _renderPassManager.BeginDepthPass(cmd, (int)_currentImageIndex, r, g, b);

        _activePass = new RenderPassContext
        {
            Cmd = cmd,
            RenderPass = renderPass,
            Extent = _swapchain.Extent,
            FrameIndex = _syncManager.CurrentFrame,
            ViewProjection = CreateOrtho(_window.Width, _window.Height)
        };
        _lastViewProjection = _activePass.Value.ViewProjection;
        _hasLastViewProjection = true;
    }

    /// <summary>
    /// Get the current command buffer for custom rendering.
    /// </summary>
    public CommandBuffer GetCommandBuffer()
    {
        return _commandRecorder.CurrentCommandBuffer;
    }

    /// <summary>
    /// End the render pass.
    /// </summary>
    public void EndRenderPass()
    {
        if (_frameSkipped) return;

        if (!_activePass.HasValue)
            throw new InvalidOperationException("Not in a render pass. Call BeginRenderPass first.");

        var ctx = _activePass.Value;

        // Draw any batched instances before ending the render pass
        DrawBatch(ctx.Cmd, ctx.RenderPass, ctx.Extent, ctx.FrameIndex, ctx.ViewProjection);

        _renderPassManager.EndPass(ctx.Cmd);
        _activePass = null;
    }

    /// <summary>
    /// Set the clear color for the next render pass.
    /// Must be called before BeginCamera3D.
    /// </summary>
    public void ClearBackground(float r, float g, float b)
    {
        _pendingConfig.ClearColor = (r, g, b);
    }

    /// <summary>
    /// Begin 3D rendering with a camera. Defers render pass start until EndCamera3D
    /// to allow GPU compute dispatch before rendering.
    /// Uses pending clear color if set via ClearBackground, otherwise defaults to dark gray.
    /// </summary>
    public void BeginCamera3D(Camera3D camera)
    {
        if (_frameSkipped) return;

        if (_phase != RenderPhase.FrameStarted)
            throw new InvalidOperationException($"BeginCamera3D requires FrameStarted phase, but currently in {_phase}");

        if (_activePass.HasValue)
            throw new InvalidOperationException("Already in a render pass");

        // Store pending clear color (consumed in EndCamera3D)
        var (clearR, clearG, clearB) = _pendingConfig.ClearColor ?? (0.1f, 0.1f, 0.1f);

        var cmd = _commandRecorder.CurrentCommandBuffer;
        float aspect = (float)_window.Width / _window.Height;

        // Store context for deferred render pass (compute dispatch before render)
        _activePass = new RenderPassContext
        {
            Cmd = cmd,
            RenderPass = default,  // Will be set when render pass actually starts
            Extent = _swapchain.Extent,
            FrameIndex = _syncManager.CurrentFrame,
            ViewProjection = camera.GetViewProjection(aspect)
        };
        _lastViewProjection = _activePass.Value.ViewProjection;
        _hasLastViewProjection = true;
        _pendingConfig.ClearColor = (clearR, clearG, clearB);
    }

    public bool TryGetLastViewProjection(out System.Numerics.Matrix4x4 viewProjection)
    {
        if (_hasLastViewProjection)
        {
            viewProjection = _lastViewProjection;
            return true;
        }

        viewProjection = default;
        return false;
    }

    /// <summary>
    /// End 3D camera mode. Dispatches GPU compute, then renders batched draws.
    /// </summary>
    public void EndCamera3D()
    {
        if (_frameSkipped) return;

        if (!_activePass.HasValue)
            throw new InvalidOperationException("Not in a render pass. Call BeginCamera3D first.");

        var ctx = _activePass.Value;
        var cmd = ctx.Cmd;

        // Dispatch GPU matrix generation before render pass
        {
            using var _computeScope = ProfileScope.Begin(ProfileScopes.ComputeTransforms);
            using var _gpuScope = GpuScope.Begin(cmd, ctx.FrameIndex, GpuScopes.ComputeTransforms);
            _computeTransforms.Dispatch(cmd, ctx.FrameIndex);
        }

        // Start the render pass
        var (clearR, clearG, clearB) = _pendingConfig.ClearColor ?? (0.1f, 0.1f, 0.1f);
        _pendingConfig = default;
        var renderPass = _renderPassManager.BeginDepthPass(cmd, (int)_currentImageIndex, clearR, clearG, clearB);

        // Update context with actual render pass
        ctx = ctx with { RenderPass = renderPass };

        {
            using var _renderPassScope = ProfileScope.Begin(ProfileScopes.RenderPass3D);
            using var _gpuScope = GpuScope.Begin(cmd, ctx.FrameIndex, GpuScopes.RenderPass3D);
            using var _drawScope = ProfileScope.Begin(ProfileScopes.DrawBatch);
            DrawBatch(ctx.Cmd, ctx.RenderPass, ctx.Extent, ctx.FrameIndex, ctx.ViewProjection);
        }
        _renderPassManager.EndPass(ctx.Cmd);
        _activePass = null;
    }

    /// <summary>
    /// Begin 2D rendering with a camera. Defers render pass start for GPU compute.
    /// </summary>
    public void BeginCamera2D(Camera2D camera)
    {
        if (_frameSkipped) return;

        if (_phase != RenderPhase.FrameStarted)
            throw new InvalidOperationException($"BeginCamera2D requires FrameStarted phase, but currently in {_phase}");

        if (_activePass.HasValue)
            throw new InvalidOperationException("Already in a render pass");

        var cmd = _commandRecorder.CurrentCommandBuffer;

        // Store context for deferred render pass (compute dispatch before render)
        _activePass = new RenderPassContext
        {
            Cmd = cmd,
            RenderPass = default,  // Will be set when render pass actually starts
            Extent = _swapchain.Extent,
            FrameIndex = _syncManager.CurrentFrame,
            ViewProjection = camera.GetProjection(_window.Width, _window.Height)
        };
        _lastViewProjection = _activePass.Value.ViewProjection;
        _hasLastViewProjection = true;
    }

    /// <summary>
    /// End 2D camera mode. Dispatches GPU compute, then renders batched draws.
    /// </summary>
    public void EndCamera2D()
    {
        if (_frameSkipped) return;

        if (!_activePass.HasValue)
            throw new InvalidOperationException("Not in a render pass. Call BeginCamera2D first.");

        var ctx = _activePass.Value;
        var cmd = ctx.Cmd;

        // Dispatch GPU matrix generation before render pass
        {
            using var _computeScope = ProfileScope.Begin(ProfileScopes.ComputeTransforms);
            using var _gpuScope = GpuScope.Begin(cmd, ctx.FrameIndex, GpuScopes.ComputeTransforms);
            _computeTransforms.Dispatch(cmd, ctx.FrameIndex);
        }

        // Start the color pass
        var renderPass = _renderPassManager.BeginColorPass(cmd, (int)_currentImageIndex);
        ctx = ctx with { RenderPass = renderPass };

        {
            using var _renderPassScope = ProfileScope.Begin(ProfileScopes.RenderPass2D);
            using var _gpuScope = GpuScope.Begin(cmd, ctx.FrameIndex, GpuScopes.RenderPass2D);
            using var _drawScope = ProfileScope.Begin(ProfileScopes.DrawBatch);
            DrawBatch(ctx.Cmd, ctx.RenderPass, ctx.Extent, ctx.FrameIndex, ctx.ViewProjection);
        }
        _renderPassManager.EndPass(ctx.Cmd);
        _activePass = null;
    }

    /// <summary>
    /// Draws all batched instances using the default instanced shader.
    /// Called automatically at the end of camera/render pass.
    /// </summary>
    /// <param name="cmd">Command buffer to record into.</param>
    /// <param name="renderPass">Current render pass for pipeline compatibility.</param>
    /// <param name="extent">Render extent for viewport/scissor.</param>
    /// <param name="frameIndex">Current frame index for synchronization.</param>
    /// <param name="viewProjection">View-projection matrix for the shader.</param>
    private unsafe void DrawBatch(
        CommandBuffer cmd,
        Silk.NET.Vulkan.RenderPass renderPass,
        Extent2D extent,
        int frameIndex,
        System.Numerics.Matrix4x4 viewProjection)
    {
        bool hasIndirect = _indirectBatcher.InstanceCount > 0;
        bool hasInstanced = _instancedMeshBuffer?.RegisteredMeshCount > 0;

        if ((!hasIndirect && !hasInstanced) || _defaultShader == null) return;

        // Flush instances to GPU
        if (hasIndirect)
            _indirectBatcher.Flush(frameIndex);
        if (hasInstanced)
            _instancedMeshBuffer!.Flush(frameIndex);

        // Select render state based on pass type (depth pass = 3D, color pass = 2D)
        var isDepthPass = renderPass.Handle == _renderPassManager.DepthPass.Handle;
        var renderState = isDepthPass ? _renderState3D : _renderState2D;

        // Get or create pipeline for the current render pass
        var pipeline = _pipelineCache.GetOrCreate(
            _defaultShader,
            renderState,
            renderPass,
            extent);

        // Set viewport
        var viewport = new Viewport
        {
            X = 0,
            Y = 0,
            Width = extent.Width,
            Height = extent.Height,
            MinDepth = 0,
            MaxDepth = 1
        };
        _vkDevice.Vk.CmdSetViewport(cmd, 0, 1, &viewport);

        // Set scissor
        var scissor = new Rect2D
        {
            Offset = new Offset2D { X = 0, Y = 0 },
            Extent = extent
        };
        _vkDevice.Vk.CmdSetScissor(cmd, 0, 1, &scissor);

        // Bind pipeline
        _vkDevice.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline);

        // Bind bindless descriptor set
        var descriptorSet = GetBindlessSet(frameIndex);
        var pipelineLayout = _pipelineCache.GetPipelineLayout(_defaultShader);
        _vkDevice.Vk.CmdBindDescriptorSets(
            cmd,
            PipelineBindPoint.Graphics,
            pipelineLayout,
            0, 1, &descriptorSet,
            0, null);

        // Push view-projection matrix
        _vkDevice.Vk.CmdPushConstants(
            cmd,
            pipelineLayout,
            ShaderStageFlags.VertexBit,
            0,
            64,
            &viewProjection);

        // Bind mesh vertex buffer (shared by both batchers)
        var vertexBuffer = _meshRegistry.VertexBuffer;
        ulong vertexOffset = 0;
        _vkDevice.Vk.CmdBindVertexBuffers(cmd, 0, 1, &vertexBuffer, &vertexOffset);

        // Bind index buffer
        _vkDevice.Vk.CmdBindIndexBuffer(cmd, _meshRegistry.IndexBuffer, 0, IndexType.Uint32);

        // Draw from indirect batcher (low-freq, sorted)
        if (hasIndirect)
            _indirectBatcher.Draw(_vkDevice.Vk, cmd, frameIndex);

        // Draw from instanced buffer (high-freq, per-mesh regions)
        if (hasInstanced && _instancedMeshBuffer!.CommandCount > 0)
        {
            _instancedMeshBuffer.Draw(_vkDevice.Vk, cmd, frameIndex);
        }

        // Instanced submissions are pass-local. Clear after drawing so later passes
        // in the same frame do not replay prior-pass instanced geometry.
        if (hasInstanced)
        {
            _instancedMeshBuffer.Reset();
        }
    }

    private static System.Numerics.Matrix4x4 CreateOrtho(float width, float height)
    {
        // Standard orthographic projection: (0,0)-(width,height) to (-1,-1)-(1,1)
        return new System.Numerics.Matrix4x4(
            2f / width, 0, 0, 0,
            0, 2f / height, 0, 0,
            0, 0, 1, 0,
            -1, -1, 0, 1
        );
    }

    /// <summary>
    /// End the current frame and present.
    /// </summary>
    public unsafe void EndFrame()
    {
        if (_frameSkipped)
        {
            _phase = RenderPhase.Idle;
            return;
        }

        if (_phase != RenderPhase.FrameStarted)
            throw new InvalidOperationException($"EndFrame requires FrameStarted phase, but currently in {_phase}. Did you forget to call EndRenderPass?");

        using var _scope = ProfileScope.Begin(ProfileScopes.EndFrame);

        // Record GPU frame end timestamp after all frame work was recorded.
        {
            using var _gpuTimingScope = ProfileScope.Begin(ProfileScopes.EndFrameGpuTimingEnd);
            _gpuTimingService?.EndFrame(_commandRecorder.CurrentCommandBuffer, _syncManager.CurrentFrame);
        }

        CommandBuffer commandBuffer;
        {
            using var _cmdEndScope = ProfileScope.Begin(ProfileScopes.EndFrameCommandEnd);
            commandBuffer = _commandRecorder.EndRecording();
        }

        // Submit
        var waitSemaphore = _syncManager.CurrentImageAvailableSemaphore;
        var signalSemaphore = _syncManager.CurrentRenderFinishedSemaphore;
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

        var fence = _syncManager.CurrentFence;
        {
            using var _submitScope = ProfileScope.Begin(ProfileScopes.EndFrameQueueSubmit);
            var result = _vkDevice.Vk.QueueSubmit(_vkDevice.GraphicsQueue, 1, &submitInfo, fence);
            if (result != Result.Success)
            {
                throw new Exception($"Failed to submit draw command buffer: {result}");
            }
        }

        // Present
        Result presentResult;
        {
            using var _presentScope = ProfileScope.Begin(ProfileScopes.EndFramePresent);
            presentResult = _swapchain.Present(
                _vkDevice.GraphicsQueue,
                _syncManager.CurrentRenderFinishedSemaphore,
                _currentImageIndex);
        }

        if (presentResult == Result.ErrorOutOfDateKhr || presentResult == Result.SuboptimalKhr)
        {
            HandleResize();
        }

        _syncManager.AdvanceFrame();
        _phase = RenderPhase.Idle;

        // End allocation tracking for the frame.
        AllocationTracker.EndFrame();

        // Update CPU profiling
        var cpuProfiler = ProfilingService.Instance;
        if (cpuProfiler != null)
        {
            long wallTicks = Stopwatch.GetTimestamp() - _cpuFrameStartTicks;
            cpuProfiler.SetCurrentFrameWallTicks(wallTicks);
            cpuProfiler.EndFrame(_frameNumber);
        }

        // End flame profiler frame
        FlameProfilerService.Instance?.EndFrame();

        _frameNumber++;
    }

    private void HandleResize()
    {
        if (_window.Width == 0 || _window.Height == 0)
            return;

        _vkDevice.Vk.DeviceWaitIdle(_vkDevice.Device);
        _swapchain.Recreate((uint)_window.FramebufferWidth, (uint)_window.FramebufferHeight);
        _renderPassManager.Resize(_swapchain.ImageViews, _swapchain.Extent);
        _swapchainImageFences = new Fence[_swapchain.Images.Length];
    }

    public void Dispose()
    {
        if (!_initialized) return;

        _log.Information("Shutting down engine...");
        _vkDevice.Vk.DeviceWaitIdle(_vkDevice.Device);

        _gamepadState?.Dispose();
        FlameProfilerService.Instance?.Dispose();
        _gpuTimingService?.Dispose();
        _computeTransforms.Dispose();
        _computeDispatcher.Dispose();
        _textureArray.Dispose();
        _meshRegistry.Dispose();
        _indirectBatcher.Dispose();
        _instancedMeshBuffer?.Dispose();
        _transformBuffer.Dispose();
        _pipelineCache.Dispose();
        _descriptorCache.Dispose();
        _memoryAllocator.Dispose();
        _syncManager.Dispose();
        _commandRecorder.Dispose();
        _renderPassManager.Dispose();
        _swapchain.Dispose();
        _vkSurface.Dispose();
        _vkDevice.Dispose();
        _vkInstance.Dispose();
        _window.Dispose();

        _initialized = false;
        _log.Information("Engine shut down");
    }
}
