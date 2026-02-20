using System.Numerics;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Serilog;
using Silk.NET.Vulkan;
using DerpLib.Assets;
using DerpLib.Core;
using DerpLib.Diagnostics;
using DerpLib.Input;
using DerpLib.Memory;
using DerpLib.Presentation;
using DerpLib.Audio;
using DerpLib.Rendering;
using DerpLib.Sdf;
using DerpLib.Shaders;
using DerpLib.Text;
using DerpLib.Vfs;
using Profiling;
using PipelineCache = DerpLib.Shaders.PipelineCache;
using RenderPassManager = DerpLib.Rendering.RenderPassManager;

namespace DerpLib;

/// <summary>
/// Raylib-style static API for the engine.
/// </summary>
public static class Derp
{
    private static EngineHost? _engine;
    private static AudioDevice? _audioDevice;

    // Public accessors for advanced usage
    public static EngineHost Engine => _engine ?? throw new InvalidOperationException("Not initialized");
    public static VirtualFileSystem Vfs => Engine.Vfs;
    public static VkInstance VkInstance => Engine.VkInstance;
    public static VkDevice Device => Engine.Device;
    public static MemoryAllocator MemoryAllocator => Engine.MemoryAllocator;
    public static DescriptorCache DescriptorCache => Engine.DescriptorCache;
    public static PipelineCache PipelineCache => Engine.PipelineCache;
    public static RenderPassManager RenderPassManager => Engine.RenderPassManager;
    public static Swapchain Swapchain => Engine.Swapchain;
    public static TransformBuffer TransformBuffer => Engine.TransformBuffer;
    public static IndirectBatcher IndirectBatcher => Engine.IndirectBatcher;
    public static TextureArray TextureArray => Engine.TextureArray;
    public static MeshRegistry MeshRegistry => Engine.MeshRegistry;
    public static int ActiveTextureCount => Engine.TextureArray.ActiveCount;
    public static int MaxTextureCount => Engine.TextureArray.MaxTextures;
    public static int FrameIndex => Engine.FrameIndex;
    public static int FramesInFlight => Engine.FramesInFlight;
    public static ComputeShader SdfShader => _sdfShader ?? throw new InvalidOperationException("SDF not initialized. Call InitSdf() first.");

    /// <summary>
    /// Gets the bindless descriptor set for the current frame.
    /// </summary>
    public static DescriptorSet GetBindlessSet(int frameIndex) => Engine.GetBindlessSet(frameIndex);

    /// <summary>
    /// Initialize window and graphics context.
    /// </summary>
    public static void InitWindow(int width, int height, string title)
    {
        if (_engine != null)
            throw new InvalidOperationException("Already initialized. Call CloseWindow() first.");

        var config = new EngineConfig(width, height, title);
        var composition = new EngineComposition(config);
        _engine = composition.Engine;
        _engine.Initialize();
    }

    /// <summary>
    /// Check if window should close.
    /// </summary>
    public static bool WindowShouldClose()
    {
        EnsureInitialized();
        return _engine!.ShouldClose;
    }

    /// <summary>
    /// Poll window events. Call once per frame.
    /// </summary>
    public static void PollEvents()
    {
        EnsureInitialized();
        _engine!.PollEvents();
    }

    /// <summary>
    /// Begin a new frame. Returns false if frame should be skipped (e.g., minimized).
    /// </summary>
    public static bool BeginDrawing()
    {
        EnsureInitialized();
        return _engine!.BeginFrame();
    }

    /// <summary>
    /// Set the clear color for the next render pass.
    /// Call before BeginCamera3D.
    /// </summary>
    public static void ClearBackground(float r, float g, float b)
    {
        EnsureInitialized();
        _engine!.ClearBackground(r, g, b);
    }

    /// <summary>
    /// End the current frame and present.
    /// </summary>
    public static void EndDrawing()
    {
        EnsureInitialized();

        // Only end render pass if one is active (ClearBackground started one)
        // Camera methods (BeginCamera3D/EndCamera3D) handle their own passes
        if (_engine!.IsInRenderPass)
            _engine.EndRenderPass();

        _engine.EndFrame();
    }

    /// <summary>
    /// Get the current command buffer for custom rendering.
    /// </summary>
    public static CommandBuffer GetCommandBuffer()
    {
        EnsureInitialized();
        return _engine!.GetCommandBuffer();
    }

    /// <summary>
    /// Get current window width.
    /// </summary>
    public static int GetScreenWidth()
    {
        EnsureInitialized();
        return _engine!.ScreenWidth;
    }

    /// <summary>
    /// Get current window height.
    /// </summary>
    public static int GetScreenHeight()
    {
        EnsureInitialized();
        return _engine!.ScreenHeight;
    }

    /// <summary>
    /// Get DPI content scale (e.g., 2.0 on Retina displays).
    /// Multiply window coordinates by this to get framebuffer coordinates.
    /// </summary>
    public static float GetContentScale()
    {
        EnsureInitialized();
        return _engine!.Window.ContentScaleX;
    }

    /// <summary>
    /// Get framebuffer width (window width * content scale).
    /// </summary>
    public static int GetFramebufferWidth()
    {
        EnsureInitialized();
        return _engine!.Window.FramebufferWidth;
    }

    /// <summary>
    /// Get framebuffer height (window height * content scale).
    /// </summary>
    public static int GetFramebufferHeight()
    {
        EnsureInitialized();
        return _engine!.Window.FramebufferHeight;
    }

    /// <summary>
    /// Get window position in screen coordinates.
    /// </summary>
    public static Vector2 GetWindowPosition()
    {
        EnsureInitialized();
        return _engine!.Window.ScreenPosition;
    }

    #region Input

    /// <summary>Get mouse position in screen coordinates.</summary>
    public static Vector2 GetMousePosition()
    {
        EnsureInitialized();
        return _engine!.Window.MousePosition;
    }

    /// <summary>Get mouse X position.</summary>
    public static float GetMouseX() => GetMousePosition().X;

    /// <summary>Get mouse Y position.</summary>
    public static float GetMouseY() => GetMousePosition().Y;

    /// <summary>Get mouse movement since last frame.</summary>
    public static Vector2 GetMouseDelta()
    {
        EnsureInitialized();
        return _engine!.Window.MouseDelta;
    }

    /// <summary>Get scroll wheel delta since last frame.</summary>
    public static float GetScrollDelta()
    {
        EnsureInitialized();
        return _engine!.Window.ScrollDelta;
    }

    /// <summary>Get horizontal scroll wheel delta since last frame.</summary>
    public static float GetScrollDeltaX()
    {
        EnsureInitialized();
        return _engine!.Window.ScrollDeltaX;
    }

    /// <summary>Get scroll wheel delta since last frame (alias).</summary>
    public static float GetMouseScrollDelta() => GetScrollDelta();
    
    /// <summary>Get horizontal scroll wheel delta since last frame (alias).</summary>
    public static float GetMouseScrollDeltaX() => GetScrollDeltaX();

    public static string? GetClipboardText()
    {
        EnsureInitialized();
        return _engine!.Window.GetClipboardText();
    }

    public static void SetClipboardText(string text)
    {
        EnsureInitialized();
        _engine!.Window.SetClipboardText(text);
    }

    /// <summary>Check if a key is currently held down.</summary>
    public static bool IsKeyDown(Silk.NET.Input.Key key)
    {
        EnsureInitialized();
        return _engine!.Window.IsKeyDown(key);
    }

    /// <summary>Check if a key was pressed this frame (edge detection - only true on first frame).</summary>
    public static bool IsKeyPressed(Silk.NET.Input.Key key)
    {
        EnsureInitialized();
        return _engine!.Window.IsKeyPressed(key);
    }

    /// <summary>Check if a key was released this frame (edge detection - only true on release frame).</summary>
    public static bool IsKeyReleased(Silk.NET.Input.Key key)
    {
        EnsureInitialized();
        return _engine!.Window.IsKeyReleased(key);
    }

    /// <summary>Get number of characters typed this frame.</summary>
    public static int GetInputCharCount()
    {
        EnsureInitialized();
        return _engine!.Window.InputCharCount;
    }

    /// <summary>Get a character from the input buffer.</summary>
    public static char GetInputChar(int index)
    {
        EnsureInitialized();
        return _engine!.Window.GetInputChar(index);
    }

    /// <summary>Check if a mouse button is currently held down.</summary>
    public static bool IsMouseButtonDown(Silk.NET.Input.MouseButton button)
    {
        EnsureInitialized();
        return _engine!.Window.IsMouseButtonDown(button);
    }

    /// <summary>Check if a mouse button is currently held down (0=left, 1=right, 2=middle).</summary>
    public static bool IsMouseButtonDown(int button) =>
        IsMouseButtonDown((Silk.NET.Input.MouseButton)button);

    /// <summary>Check if a mouse button was pressed this frame.</summary>
    public static bool IsMouseButtonPressed(Silk.NET.Input.MouseButton button)
    {
        EnsureInitialized();
        return _engine!.Window.IsMouseButtonPressed(button);
    }

    /// <summary>Check if a mouse button was pressed this frame (0=left, 1=right, 2=middle).</summary>
    public static bool IsMouseButtonPressed(int button) =>
        IsMouseButtonPressed((Silk.NET.Input.MouseButton)button);

    /// <summary>Check if a mouse button was released this frame.</summary>
    public static bool IsMouseButtonReleased(Silk.NET.Input.MouseButton button)
    {
        EnsureInitialized();
        return _engine!.Window.IsMouseButtonReleased(button);
    }

    /// <summary>Check if a mouse button was released this frame (0=left, 1=right, 2=middle).</summary>
    public static bool IsMouseButtonReleased(int button) =>
        IsMouseButtonReleased((Silk.NET.Input.MouseButton)button);

    /// <summary>Set the window title.</summary>
    public static void SetWindowTitle(string title)
    {
        EnsureInitialized();
        _engine!.Window.SetTitle(title);
    }

    /// <summary>Set the mouse cursor shape.</summary>
    public static void SetCursor(Silk.NET.Input.StandardCursor cursor)
    {
        EnsureInitialized();
        _engine!.Window.SetCursor(cursor);
    }

    internal static DerpLib.Presentation.Window GetPrimaryWindowForImGui()
    {
        EnsureInitialized();
        return _engine!.Window;
    }

    #endregion

    #region Gamepad

    /// <summary>Check if a gamepad is available at the specified index (0-3).</summary>
    public static bool IsGamepadAvailable(int gamepad = 0)
    {
        EnsureInitialized();
        return _engine!.GamepadState.IsGamepadAvailable(gamepad);
    }

    /// <summary>Get the name of the gamepad at the specified index.</summary>
    public static string? GetGamepadName(int gamepad = 0)
    {
        EnsureInitialized();
        return _engine!.GamepadState.GetGamepadName(gamepad);
    }

    /// <summary>Check if a gamepad button is currently held down.</summary>
    public static bool IsGamepadButtonDown(int gamepad, GamepadButton button)
    {
        EnsureInitialized();
        return _engine!.GamepadState.IsGamepadButtonDown(gamepad, button);
    }

    /// <summary>Check if a gamepad button is currently held down (gamepad 0).</summary>
    public static bool IsGamepadButtonDown(GamepadButton button) => IsGamepadButtonDown(0, button);

    /// <summary>Check if a gamepad button was pressed this frame (edge detection).</summary>
    public static bool IsGamepadButtonPressed(int gamepad, GamepadButton button)
    {
        EnsureInitialized();
        return _engine!.GamepadState.IsGamepadButtonPressed(gamepad, button);
    }

    /// <summary>Check if a gamepad button was pressed this frame (gamepad 0).</summary>
    public static bool IsGamepadButtonPressed(GamepadButton button) => IsGamepadButtonPressed(0, button);

    /// <summary>Check if a gamepad button was released this frame (edge detection).</summary>
    public static bool IsGamepadButtonReleased(int gamepad, GamepadButton button)
    {
        EnsureInitialized();
        return _engine!.GamepadState.IsGamepadButtonReleased(gamepad, button);
    }

    /// <summary>Check if a gamepad button was released this frame (gamepad 0).</summary>
    public static bool IsGamepadButtonReleased(GamepadButton button) => IsGamepadButtonReleased(0, button);

    /// <summary>Get the current axis value (-1.0 to 1.0 for sticks, 0.0 to 1.0 for triggers).</summary>
    public static float GetGamepadAxisMovement(int gamepad, GamepadAxis axis)
    {
        EnsureInitialized();
        return _engine!.GamepadState.GetGamepadAxisMovement(gamepad, axis);
    }

    /// <summary>Get the current axis value (gamepad 0).</summary>
    public static float GetGamepadAxisMovement(GamepadAxis axis) => GetGamepadAxisMovement(0, axis);

    /// <summary>Get the number of axes on the gamepad.</summary>
    public static int GetGamepadAxisCount(int gamepad = 0)
    {
        EnsureInitialized();
        return _engine!.GamepadState.GetGamepadAxisCount(gamepad);
    }

    /// <summary>Get the number of buttons on the gamepad.</summary>
    public static int GetGamepadButtonCount(int gamepad = 0)
    {
        EnsureInitialized();
        return _engine!.GamepadState.GetGamepadButtonCount(gamepad);
    }

    #endregion

    /// <summary>
    /// Close window and cleanup resources.
    /// </summary>
    public static void CloseWindow()
    {
        if (_engine == null) return;

        CloseAudioDevice();

        // Reset SDF state
        _sdfRenderer?.Dispose();
        _sdfRenderer = null;
        _sdfShader = null;
        _sdfOutputTexture = default;
        _hasSdfOutputTexture = false;
        _debugAsciiFontAtlas = default;
        _hasDebugAsciiFontAtlas = false;

        // Reset render state
        _activeRenderTexture = null;
        _activeDepthTexture = null;
        _activeDepthArray = null;
        _activeDepthArrayLayer = 0;
        _activeShader = null;

        _engine.Dispose();
        _engine = null;
    }

    #region Audio

    public static void InitAudioDevice()
    {
        if (_audioDevice != null)
        {
            return;
        }

        _audioDevice = new AudioDevice(Log.Logger);
        _audioDevice.Init();
    }

    public static void CloseAudioDevice()
    {
        if (_audioDevice == null)
        {
            return;
        }

        _audioDevice.Dispose();
        _audioDevice = null;
    }

    public static Sound LoadSound(string vfsPath)
    {
        InitAudioDevice();

        var bytes = Vfs.ReadAllBytes(vfsPath);
        var extension = Path.GetExtension(vfsPath).ToLowerInvariant();
        return _audioDevice!.LoadSound(bytes, extension);
    }

    public static void UnloadSound(Sound sound)
    {
        _audioDevice?.UnloadSound(sound);
    }

    public static void PlaySound(Sound sound)
    {
        _audioDevice?.PlaySound(sound);
    }

    public static void StopSound(Sound sound)
    {
        _audioDevice?.StopAllSounds();
    }

    #endregion

    #region Texture Loading

    /// <summary>
    /// Registers an externally-owned image view as a bindless texture slot and returns a Texture wrapper.
    /// Intended for engine subsystems that render into storage images (e.g., SDF output) and want to sample them later.
    /// </summary>
    public static Texture RegisterExternalTexture(Silk.NET.Vulkan.ImageView imageView, int width, int height)
    {
        EnsureInitialized();
        var handle = _engine!.TextureArray.RegisterExternal(imageView);
        UpdateTextureDescriptors();
        return new Texture(handle.Index, width, height);
    }

    /// <summary>
    /// Releases a previously-registered external texture slot.
    /// </summary>
    public static void UnregisterExternalTexture(Texture texture)
    {
        EnsureInitialized();
        int textureIndex = texture.GetIndex();
        bool released = _engine!.TextureArray.UnregisterExternal(textureIndex);
        if (released)
        {
            UpdateTextureDescriptors();
        }
    }

    /// <summary>
    /// Updates an existing external texture slot after the underlying image is recreated (e.g., resize).
    /// </summary>
    public static Texture UpdateExternalTexture(Texture texture, Silk.NET.Vulkan.ImageView imageView, int width, int height)
    {
        EnsureInitialized();
        _engine!.TextureArray.UpdateExternal(texture.GetIndex(), imageView);
        UpdateTextureDescriptors();
        return new Texture(texture.GetIndex(), width, height);
    }

    /// <summary>
    /// Releases an owned texture slot previously created by LoadTexture.
    /// </summary>
    public static void UnloadTexture(Texture texture)
    {
        EnsureInitialized();
        int textureIndex = texture.GetIndex();
        bool released = _engine!.TextureArray.UnloadOwned(textureIndex);
        if (released)
        {
            UpdateTextureDescriptors();
        }
    }

    /// <summary>
    /// Load a texture from RGBA pixel data.
    /// </summary>
    public static Texture LoadTexture(ReadOnlySpan<byte> pixels, int width, int height)
    {
        EnsureInitialized();
        var handle = _engine!.TextureArray.Load(pixels, (uint)width, (uint)height);
        UpdateTextureDescriptors();
        return new Texture(handle.Index, width, height);
    }

    /// <summary>
    /// Update pixel data for an existing texture slot.
    /// Width and height must match the existing texture dimensions.
    /// </summary>
    public static Texture UpdateTexture(Texture texture, ReadOnlySpan<byte> pixels, int width, int height)
    {
        EnsureInitialized();
        _engine!.TextureArray.UpdateOwnedTexturePixels(texture.GetIndex(), pixels, (uint)width, (uint)height);
        return new Texture(texture.GetIndex(), width, height);
    }

    /// <summary>
    /// Load a solid color 1x1 texture.
    /// </summary>
    public static Texture LoadSolidColor(byte r, byte g, byte b, byte a = 255)
    {
        EnsureInitialized();
        var handle = _engine!.TextureArray.LoadSolidColor(r, g, b, a);
        UpdateTextureDescriptors();
        return new Texture(handle.Index, 1, 1);
    }

    /// <summary>
    /// Load a texture from the content pipeline by name.
    /// Loads from textures/{name}.texture in the data directory.
    /// </summary>
    public static Texture LoadTexture(string name)
    {
        EnsureInitialized();
        var compiled = _engine!.Content.Load<CompiledTexture>($"textures/{name}.texture");
        var handle = _engine.TextureArray.Load(compiled.Pixels, (uint)compiled.Width, (uint)compiled.Height);
        UpdateTextureDescriptors();
        return new Texture(handle.Index, compiled.Width, compiled.Height);
    }

    /// <summary>
    /// Updates bindless texture descriptors after loading new textures.
    /// </summary>
    private static void UpdateTextureDescriptors()
    {
        var textureArray = _engine!.TextureArray;
        var descriptorCache = _engine.DescriptorCache;
        for (int i = 0; i < _engine.FramesInFlight; i++)
        {
            textureArray.WriteToDescriptorSet(descriptorCache, _engine.GetBindlessSet(i), 0);
        }

        if (_sdfRenderer != null)
        {
            _sdfRenderer.SetFontTextureArray(textureArray);
        }
    }

    #endregion


    #region Font Loading

    /// <summary>
    /// Load a font from the content pipeline by name.
    /// Loads from fonts/{name}.font in the data directory.
    /// </summary>
    public static Font LoadFont(string name)
    {
        EnsureInitialized();
        var compiled = _engine!.Content.Load<CompiledFont>($"fonts/{name}.font");
        var font = FontLoader.Load(compiled, _engine.TextureArray);
        UpdateTextureDescriptors();
        return font;
    }

    #endregion

    #region Mesh Loading

    /// <summary>
    /// Load a mesh from the content pipeline by name.
    /// Loads from meshes/{name}.mesh in the data directory.
    /// </summary>
    public static MeshHandle LoadMesh(string name)
    {
        EnsureInitialized();
        var compiled = _engine!.Content.Load<CompiledMesh>($"meshes/{name}.mesh");
        return _engine.MeshRegistry.RegisterMesh(compiled.Vertices, compiled.VertexCount, compiled.Indices);
    }

    /// <summary>
    /// Load a mesh for high-frequency instanced rendering.
    /// Uses per-mesh region buffer - no sorting needed.
    /// </summary>
    /// <param name="name">Mesh name (loads from meshes/{name}.mesh)</param>
    /// <param name="instanced">Must be true</param>
    /// <param name="capacity">Maximum instances per frame</param>
    public static MeshHandle LoadMesh(string name, bool instanced, int capacity)
    {
        EnsureInitialized();
        if (!instanced)
            return LoadMesh(name);

        var compiled = _engine!.Content.Load<CompiledMesh>($"meshes/{name}.mesh");
        return _engine.RegisterInstancedMesh(compiled.Vertices, compiled.VertexCount, compiled.Indices, capacity);
    }

    /// <summary>
    /// Initialize the instanced mesh buffer after all instanced meshes are registered.
    /// </summary>
    public static void InitializeInstancedMeshes()
    {
        EnsureInitialized();
        _engine!.InitializeInstancedBuffer();
    }

    /// <summary>
    /// Register a mesh from raw vertex and index data.
    /// </summary>
    public static MeshHandle RegisterMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<uint> indices)
    {
        EnsureInitialized();
        return _engine!.MeshRegistry.RegisterMesh(vertices, indices);
    }

    /// <summary>
    /// Load a model with multiple submeshes and embedded textures.
    /// Loads from meshes/{name}.mesh in the data directory.
    /// </summary>
    public static Model LoadModel(string name)
    {
        EnsureInitialized();
        var compiled = _engine!.Content.Load<CompiledMesh>($"meshes/{name}.mesh");

        // Upload embedded textures to TextureArray
        var textures = new TextureHandle[compiled.EmbeddedTextures.Length];
        for (int i = 0; i < compiled.EmbeddedTextures.Length; i++)
        {
            var tex = compiled.EmbeddedTextures[i];
            textures[i] = _engine.TextureArray.Load(tex.Pixels, (uint)tex.Width, (uint)tex.Height);
        }

        // Update bindless descriptors with newly loaded textures
        if (textures.Length > 0)
        {
            UpdateTextureDescriptors();
        }

        // Register mesh with submeshes
        var meshHandles = _engine.MeshRegistry.RegisterMeshWithSubmeshes(
            compiled.Vertices, compiled.VertexCount, compiled.Indices, compiled.Submeshes);

        // Build submesh info array
        var submeshInfos = new SubmeshInfo[meshHandles.Length];
        for (int i = 0; i < meshHandles.Length; i++)
        {
            int texIndex = i < compiled.Submeshes.Length ? compiled.Submeshes[i].TextureIndex : -1;
            submeshInfos[i] = new SubmeshInfo(meshHandles[i], texIndex);
        }

        return new Model(submeshInfos, textures);
    }

    #endregion

    #region Drawing

    /// <summary>
    /// Draw a texture at a position.
    /// </summary>
    public static void DrawTexture(Texture texture, float x, float y)
    {
        DrawTexture(texture, x, y, 255, 255, 255, 255);
    }

    /// <summary>
    /// Draw a texture at a position with a color tint.
    /// </summary>
    public static void DrawTexture(Texture texture, float x, float y, byte r, byte g, byte b, byte a)
    {
        EnsureInitialized();
        var batcher = _engine!.IndirectBatcher;
        var computeTransforms = _engine.ComputeTransforms;
        var quadMesh = _engine.MeshRegistry.QuadMesh;

        var position = new Vector3(x + texture.Width * 0.5f, y + texture.Height * 0.5f, 0);
        var scale = new Vector3(texture.Width, texture.Height, 1);
        uint packedColor = (uint)(r | (g << 8) | (b << 16) | (a << 24));
        int transformIndex = computeTransforms.Add(position, 0f, scale, (uint)quadMesh.Id, (uint)texture.Index, packedColor);
        if (transformIndex < 0) return;

        batcher.Add(quadMesh, (uint)transformIndex, (uint)texture.Index, r, g, b, a);
    }

    /// <summary>
    /// Draw a texture at a position with uniform scale and color tint.
    /// Position is the center of the quad.
    /// </summary>
    public static void DrawTextureEx(Texture texture, float x, float y, float scale, byte r, byte g, byte b, byte a)
    {
        var transform = Matrix4x4.CreateScale(scale) *
                       Matrix4x4.CreateTranslation(x, y, 0);
        DrawTextureTransform(texture, transform, r, g, b, a);
    }

    /// <summary>
    /// Draw a texture with an explicit world transform matrix.
    /// Use for hierarchy (pre-multiply parent * child) or custom transforms.
    /// </summary>
    public static void DrawTextureTransform(Texture texture, Matrix4x4 transform, byte r, byte g, byte b, byte a)
    {
        EnsureInitialized();
        var computeTransforms = _engine!.ComputeTransforms;
        var quadMesh = _engine.MeshRegistry.QuadMesh;

        uint packedColor = (uint)(r | (g << 8) | (b << 16) | (a << 24));
        int transformIndex = computeTransforms.AddMatrix(transform, (uint)quadMesh.Id, (uint)texture.Index, packedColor);
        if (transformIndex < 0) return;

        _engine.IndirectBatcher.Add(quadMesh, (uint)transformIndex, (uint)texture.Index, r, g, b, a);
    }

    /// <summary>
    /// Draw a texture region at a position.
    /// </summary>
    public static void DrawTextureRec(Texture texture, Rectangle source, float x, float y, byte r, byte g, byte b, byte a)
    {
        EnsureInitialized();
        var computeTransforms = _engine!.ComputeTransforms;
        var quadMesh = _engine.MeshRegistry.QuadMesh;

        var position = new Vector3(x + source.Width * 0.5f, y + source.Height * 0.5f, 0);
        var scale = new Vector3(source.Width, source.Height, 1);
        uint packedColor = (uint)(r | (g << 8) | (b << 16) | (a << 24));
        int transformIndex = computeTransforms.Add(position, 0f, scale, (uint)quadMesh.Id, (uint)texture.Index, packedColor);
        if (transformIndex < 0) return;

        // Calculate UV offset (normalized)
        float uvOffsetX = source.X / texture.Width;
        float uvOffsetY = source.Y / texture.Height;

        _engine.IndirectBatcher.Add(quadMesh, (uint)transformIndex, (uint)texture.Index, r, g, b, a, uvOffsetX, uvOffsetY);
    }

    /// <summary>
    /// Draw a texture with full control over source, destination, origin, and rotation.
    /// </summary>
    public static void DrawTexturePro(Texture texture, Rectangle source, Rectangle dest, float originX, float originY, float rotation, byte r, byte g, byte b, byte a)
    {
        EnsureInitialized();
        var computeTransforms = _engine!.ComputeTransforms;
        var quadMesh = _engine.MeshRegistry.QuadMesh;

        // Build transform: scale to dest size, rotate around origin, translate to position
        var transform =
            Matrix4x4.CreateScale(dest.Width, dest.Height, 1) *
            Matrix4x4.CreateTranslation(-originX, -originY, 0) *
            Matrix4x4.CreateRotationZ(rotation) *
            Matrix4x4.CreateTranslation(dest.X + originX, dest.Y + originY, 0);

        uint packedColor = (uint)(r | (g << 8) | (b << 16) | (a << 24));
        int transformIndex = computeTransforms.AddMatrix(transform, (uint)quadMesh.Id, (uint)texture.Index, packedColor);
        if (transformIndex < 0) return;

        // Calculate UV offset
        float uvOffsetX = source.X / texture.Width;
        float uvOffsetY = source.Y / texture.Height;

        _engine.IndirectBatcher.Add(quadMesh, (uint)transformIndex, (uint)texture.Index, r, g, b, a, uvOffsetX, uvOffsetY);
    }

    /// <summary>
    /// Draw a 3D mesh with a transform matrix.
    /// Use within BeginCamera3D/EndCamera3D for proper 3D rendering.
    /// </summary>
    public static void DrawMesh(MeshHandle mesh, Matrix4x4 transform, Texture texture, byte r = 255, byte g = 255, byte b = 255, byte a = 255)
    {
        EnsureInitialized();
        var computeTransforms = _engine!.ComputeTransforms;

        uint packedColor = (uint)(r | (g << 8) | (b << 16) | (a << 24));
        int transformIndex = computeTransforms.AddMatrix(transform, (uint)mesh.Id, (uint)texture.Index, packedColor);
        if (transformIndex < 0) return;

        _engine.IndirectBatcher.Add(mesh, (uint)transformIndex, (uint)texture.Index, r, g, b, a);
    }

    /// <summary>
    /// Draw a 3D mesh at a position with scale (no CPU matrix).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawMesh(MeshHandle mesh, Vector3 position, Vector3 scale, Texture texture, byte r = 255, byte g = 255, byte b = 255, byte a = 255)
    {
        var computeTransforms = _engine!.ComputeTransforms;

        uint packedColor = (uint)(r | (g << 8) | (b << 16) | (a << 24));
        int transformIndex = computeTransforms.Add(position, 0f, scale, (uint)mesh.Id, (uint)texture.Index, packedColor);
        if (transformIndex < 0) return;

        _engine.IndirectBatcher.Add(mesh, (uint)transformIndex, (uint)texture.Index, r, g, b, a);
    }

    /// <summary>
    /// Draw a 3D mesh at a position with Y-rotation and scale (no CPU matrix).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawMesh(MeshHandle mesh, Vector3 position, float rotationY, Vector3 scale, Texture texture, byte r = 255, byte g = 255, byte b = 255, byte a = 255)
    {
        var computeTransforms = _engine!.ComputeTransforms;

        uint packedColor = (uint)(r | (g << 8) | (b << 16) | (a << 24));
        int transformIndex = computeTransforms.Add(position, rotationY, scale, (uint)mesh.Id, (uint)texture.Index, packedColor);
        if (transformIndex < 0) return;

        _engine.IndirectBatcher.Add(mesh, (uint)transformIndex, (uint)texture.Index, r, g, b, a);
    }

    /// <summary>
    /// Draw a 3D mesh at a position with Y-rotation and uniform scale (no CPU matrix).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawMesh(MeshHandle mesh, Vector3 position, float rotationY, float scale, byte r = 255, byte g = 255, byte b = 255, byte a = 255)
    {
        var computeTransforms = _engine!.ComputeTransforms;

        uint packedColor = (uint)(r | (g << 8) | (b << 16) | (a << 24));
        int transformIndex = computeTransforms.Add(position, rotationY, scale, (uint)mesh.Id, 0, packedColor);
        if (transformIndex < 0) return;

        // Route to instanced buffer or indirect batcher based on mesh type
        if (mesh.IsInstanced)
        {
            _engine.InstancedMeshBuffer!.Add(mesh, (uint)transformIndex, 0, packedColor);
        }
        else
        {
            _engine.IndirectBatcher.Add(mesh, (uint)transformIndex, 0, r, g, b, a);
        }
    }

    /// <summary>
    /// Draw a 3D mesh with a white texture (solid color).
    /// </summary>
    public static void DrawMesh(MeshHandle mesh, Matrix4x4 transform, byte r = 255, byte g = 255, byte b = 255, byte a = 255)
    {
        // Texture index 0 is the default white texture
        EnsureInitialized();
        var computeTransforms = _engine!.ComputeTransforms;

        uint packedColor = (uint)(r | (g << 8) | (b << 16) | (a << 24));
        int transformIndex = computeTransforms.AddMatrix(transform, (uint)mesh.Id, 0, packedColor);
        if (transformIndex < 0) return;

        _engine.IndirectBatcher.Add(mesh, (uint)transformIndex, 0, r, g, b, a);
    }

    /// <summary>
    /// Draw a model (multiple submeshes with textures) at a transform.
    /// Each submesh is drawn with its associated texture.
    /// </summary>
    public static void DrawModel(Model model, Matrix4x4 transform)
    {
        EnsureInitialized();
        var computeTransforms = _engine!.ComputeTransforms;

        // Allocate one transform for the whole model (shared by all submeshes)
        const uint white = 0xFFFFFFFF;
        int transformIndex = computeTransforms.AddMatrix(transform, 0, 0, white);
        if (transformIndex < 0) return;

        // Draw each submesh with its texture
        var batcher = _engine.IndirectBatcher;
        foreach (var submesh in model.Submeshes)
        {
            var texture = submesh.GetTexture(model);
            uint texIndex = texture.IsValid ? (uint)texture.Index : 0; // Default white if invalid

            batcher.Add(submesh.Mesh, (uint)transformIndex, texIndex, 255, 255, 255, 255);
        }
    }

    /// <summary>
    /// Draw a model at a position with scale (no CPU matrix).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawModel(Model model, Vector3 position, Vector3 scale)
    {
        var computeTransforms = _engine!.ComputeTransforms;

        const uint white = 0xFFFFFFFF;
        int transformIndex = computeTransforms.Add(position, 0f, scale, 0, 0, white);
        if (transformIndex < 0) return;

        var batcher = _engine.IndirectBatcher;
        foreach (var submesh in model.Submeshes)
        {
            var texture = submesh.GetTexture(model);
            uint texIndex = texture.IsValid ? (uint)texture.Index : 0;
            batcher.Add(submesh.Mesh, (uint)transformIndex, texIndex, 255, 255, 255, 255);
        }
    }

    /// <summary>
    /// Draw a model at a position with uniform scale (no CPU matrix).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DrawModel(Model model, Vector3 position, float scale = 1f)
    {
        DrawModel(model, position, new Vector3(scale));
    }

    #endregion

    #region Render Textures

    /// <summary>
    /// Create a render texture for offscreen 3D rendering.
    /// </summary>
    public static RenderTexture CreateRenderTexture(int width, int height)
    {
        EnsureInitialized();
        return new RenderTexture(
            _engine!.Device,
            _engine.MemoryAllocator,
            (uint)width,
            (uint)height,
            _engine.Swapchain.ImageFormat,
            _engine.RenderPassManager.DepthFormat);
    }

    /// <summary>
    /// Create a depth-only texture for shadow mapping.
    /// </summary>
    public static DepthTexture CreateDepthTexture(int width, int height)
    {
        EnsureInitialized();
        return new DepthTexture(
            _engine!.Device,
            (uint)width,
            (uint)height,
            _engine.RenderPassManager.DepthFormat);
    }

    /// <summary>
    /// Create a depth texture array for shadow cascades or virtual shadow maps.
    /// </summary>
    public static DepthTextureArray CreateDepthTextureArray(int width, int height, int layers)
    {
        EnsureInitialized();
        return new DepthTextureArray(
            _engine!.Device,
            (uint)width,
            (uint)height,
            (uint)layers,
            _engine.RenderPassManager.DepthFormat);
    }

    private static RenderTexture? _activeRenderTexture;
    private static DepthTexture? _activeDepthTexture;
    private static DepthTextureArray? _activeDepthArray;
    private static int _activeDepthArrayLayer;

    /// <summary>
    /// Begin rendering to a render texture.
    /// </summary>
    public static void BeginTextureMode(RenderTexture target, float clearR = 0, float clearG = 0, float clearB = 0)
    {
        EnsureInitialized();

        // End current pass if active
        if (_engine!.GetCommandBuffer().Handle != 0)
        {
            _engine.EndRenderPass();
        }

        _activeRenderTexture = target;
        target.BeginRenderPass(_engine.GetCommandBuffer(), clearR, clearG, clearB);
    }

    /// <summary>
    /// Begin rendering to a depth texture (for shadow maps).
    /// </summary>
    public static void BeginTextureMode(DepthTexture target)
    {
        EnsureInitialized();

        if (_engine!.GetCommandBuffer().Handle != 0)
        {
            _engine.EndRenderPass();
        }

        _activeDepthTexture = target;
        target.BeginRenderPass(_engine.GetCommandBuffer());
    }

    /// <summary>
    /// Begin rendering to a specific layer of a depth texture array.
    /// </summary>
    public static void BeginTextureMode(DepthTextureArray target, int layer)
    {
        EnsureInitialized();

        if (_engine!.GetCommandBuffer().Handle != 0)
        {
            _engine.EndRenderPass();
        }

        _activeDepthArray = target;
        _activeDepthArrayLayer = layer;
        target.BeginRenderPass(_engine.GetCommandBuffer(), layer);
    }

    /// <summary>
    /// End rendering to the current texture target.
    /// </summary>
    public static void EndTextureMode()
    {
        EnsureInitialized();

        var cmd = _engine!.GetCommandBuffer();

        if (_activeRenderTexture != null)
        {
            _activeRenderTexture.EndRenderPass(cmd);
            _activeRenderTexture = null;
        }
        else if (_activeDepthTexture != null)
        {
            _activeDepthTexture.EndRenderPass(cmd);
            _activeDepthTexture = null;
        }
        else if (_activeDepthArray != null)
        {
            _activeDepthArray.EndRenderPass(cmd);
            _activeDepthArray = null;
        }
    }

    #endregion

    #region Camera

    /// <summary>
    /// Begin 3D rendering with a camera. Clears color and depth.
    /// Use ClearBackground before this to set clear color, otherwise defaults to dark gray.
    /// </summary>
    public static void BeginCamera3D(Camera3D camera)
    {
        EnsureInitialized();
        _engine!.BeginCamera3D(camera);
    }

    /// <summary>
    /// End 3D camera mode.
    /// </summary>
    public static void EndCamera3D()
    {
        EnsureInitialized();
        _engine!.EndCamera3D();
    }

    /// <summary>
    /// Begin 2D rendering with a camera. Preserves previous content.
    /// </summary>
    public static void BeginCamera2D(Camera2D camera)
    {
        EnsureInitialized();
        _engine!.BeginCamera2D(camera);
    }

    /// <summary>
    /// End 2D camera mode.
    /// </summary>
    public static void EndCamera2D()
    {
        EnsureInitialized();
        _engine!.EndCamera2D();
    }

    public static bool TryWorldToScreen(Vector3 worldPos, out Vector2 screenPos)
    {
        EnsureInitialized();

        if (!_engine!.TryGetLastViewProjection(out Matrix4x4 viewProjection))
        {
            screenPos = default;
            return false;
        }

        int targetWidth = _engine.Window.FramebufferWidth;
        int targetHeight = _engine.Window.FramebufferHeight;
        return Projection3D.TryWorldToTarget(viewProjection, targetWidth, targetHeight, worldPos, out screenPos);
    }

    public static bool TryWorldToSdf(Vector3 worldPos, out Vector2 sdfPos)
    {
        EnsureSdfInitialized();

        if (!_engine!.TryGetLastViewProjection(out Matrix4x4 viewProjection))
        {
            sdfPos = default;
            return false;
        }

        var tex = SdfOutputTexture;
        return Projection3D.TryWorldToTarget(viewProjection, tex.Width, tex.Height, worldPos, out sdfPos);
    }

    public static bool TryProjectWorldAabbToScreenRect(BoundingBox aabb, out Rectangle rect)
    {
        EnsureInitialized();

        if (!_engine!.TryGetLastViewProjection(out Matrix4x4 viewProjection))
        {
            rect = default;
            return false;
        }

        int targetWidth = _engine.Window.FramebufferWidth;
        int targetHeight = _engine.Window.FramebufferHeight;
        return Projection3D.TryProjectAabbToTargetRect(viewProjection, targetWidth, targetHeight, aabb, out rect);
    }

    public static bool TryProjectWorldAabbToSdfRect(BoundingBox aabb, out Rectangle rect)
    {
        EnsureSdfInitialized();

        if (!_engine!.TryGetLastViewProjection(out Matrix4x4 viewProjection))
        {
            rect = default;
            return false;
        }

        var tex = SdfOutputTexture;
        return Projection3D.TryProjectAabbToTargetRect(viewProjection, tex.Width, tex.Height, aabb, out rect);
    }

    public static bool TryScreenToWorldRay(Vector2 screenPos, out Ray3D ray)
    {
        EnsureInitialized();

        if (!_engine!.TryGetLastViewProjection(out Matrix4x4 viewProjection))
        {
            ray = default;
            return false;
        }

        int targetWidth = _engine.Window.FramebufferWidth;
        int targetHeight = _engine.Window.FramebufferHeight;
        return Projection3D.TryScreenToWorldRay(viewProjection, targetWidth, targetHeight, screenPos, out ray);
    }

    #endregion

    #region Shaders

    private static Shader? _activeShader;

    /// <summary>
    /// Begin using a custom shader for subsequent draw calls.
    /// </summary>
    public static void BeginShaderMode(Shader shader)
    {
        _activeShader = shader;
    }

    /// <summary>
    /// End using a custom shader, revert to default.
    /// </summary>
    public static void EndShaderMode()
    {
        _activeShader = null;
    }

    /// <summary>
    /// Get the currently active shader, or null for default.
    /// </summary>
    public static Shader? ActiveShader => _activeShader;

    /// <summary>
    /// Set push constants for the current shader.
    /// Requires BeginShaderMode to be called first.
    /// </summary>
    public static unsafe void SetPushConstants<T>(T data) where T : unmanaged
    {
        EnsureInitialized();
        if (_activeShader == null)
            throw new InvalidOperationException("SetPushConstants requires BeginShaderMode to be called first");

        var cmd = _engine!.GetCommandBuffer();
        var layout = _engine.PipelineCache.GetPipelineLayout(_activeShader);
        var size = (uint)Marshal.SizeOf<T>();

        _engine.Device.Vk.CmdPushConstants(cmd, layout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, size, &data);
    }

    #endregion

    #region Compute Shaders

    /// <summary>
    /// Load a compute shader by name.
    /// Loads from shaders/{name}.compute in the data directory.
    /// </summary>
    public static ComputeShader LoadComputeShader(string name)
    {
        EnsureInitialized();
        return ComputeShader.Load(name);
    }

    /// <summary>
    /// Begin compute dispatch mode.
    /// Must be called before BindStorageBuffer or DispatchCompute.
    /// </summary>
    public static void BeginCompute()
    {
        EnsureInitialized();
        _engine!.ComputeDispatcher.Begin(_engine.FrameIndex);
    }

    /// <summary>
    /// Bind a storage buffer to a binding slot for compute shaders.
    /// </summary>
    public static void BindStorageBuffer<T>(uint binding, StorageBuffer<T> buffer) where T : unmanaged
    {
        EnsureInitialized();
        _engine!.ComputeDispatcher.BindStorageBuffer(binding, buffer);
    }

    /// <summary>
    /// Set push constants for a compute shader.
    /// </summary>
    public static void SetComputePushConstants<T>(ComputeShader shader, T data) where T : unmanaged
    {
        EnsureInitialized();
        var size = (uint)Marshal.SizeOf<T>();
        _engine!.ComputeDispatcher.SetPushConstants(_engine.GetCommandBuffer(), shader, data, size);
    }

    /// <summary>
    /// Dispatch a compute shader.
    /// </summary>
    public static void DispatchCompute(ComputeShader shader, uint groupsX, uint groupsY = 1, uint groupsZ = 1, uint pushConstantSize = 0)
    {
        EnsureInitialized();
        _engine!.ComputeDispatcher.Dispatch(_engine.GetCommandBuffer(), shader, groupsX, groupsY, groupsZ, pushConstantSize);
    }

    /// <summary>
    /// End compute dispatch mode.
    /// Inserts a memory barrier so compute writes are visible to subsequent render passes.
    /// </summary>
    public static void EndCompute()
    {
        EnsureInitialized();
        _engine!.ComputeDispatcher.End(_engine.GetCommandBuffer());
    }

    #endregion

    #region GPU Matrix Generation (Phase 2)

    /// <summary>
    /// Get the GPU matrix generator for testing.
    /// </summary>
    public static ComputeTransforms ComputeTransforms => _engine!.ComputeTransforms;

    #endregion

    #region Buffers and Descriptors

    /// <summary>
    /// Create a typed storage buffer for shader access.
    /// </summary>
    /// <param name="capacity">Maximum number of elements.</param>
    /// <param name="readable">If true, enables CPU readback (allocates staging buffers).</param>
    public static StorageBuffer<T> CreateBuffer<T>(int capacity, bool readable = false) where T : unmanaged
    {
        EnsureInitialized();
        var size = (ulong)(capacity * Marshal.SizeOf<T>());
        var allocation = _engine!.MemoryAllocator.CreateStorageBuffer(size);

        ReadbackState? readback = null;
        if (readable)
        {
            readback = new ReadbackState(_engine.Device, _engine.MemoryAllocator, size);
        }

        return new StorageBuffer<T>(allocation, _engine.MemoryAllocator, capacity, readback);
    }

    /// <summary>
    /// Create a user descriptor set with storage buffers (binds to set 1).
    /// </summary>
    public static UserDescriptorSet CreateDescriptorSet(params object[] buffers)
    {
        EnsureInitialized();
        // TODO: Implement descriptor set creation for user buffers
        // This requires creating a layout and allocating a set
        throw new NotImplementedException("User descriptor sets not yet implemented");
    }

    /// <summary>
    /// Bind a user descriptor set to set 1.
    /// Requires BeginShaderMode to be called first.
    /// </summary>
    public static unsafe void BindDescriptorSet(int set, UserDescriptorSet descriptorSet)
    {
        EnsureInitialized();
        if (set != 1)
            throw new ArgumentException("User descriptor sets must be bound to set 1");
        if (_activeShader == null)
            throw new InvalidOperationException("BindDescriptorSet requires BeginShaderMode to be called first");

        var cmd = _engine!.GetCommandBuffer();
        var layout = _engine.PipelineCache.GetPipelineLayout(_activeShader);
        var handle = descriptorSet.Handle;

        _engine.Device.Vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, layout, 1, 1, &handle, 0, null);
    }

    #endregion

    #region SDF Rendering

    private static SdfRenderer? _sdfRenderer;
    private static ComputeShader? _sdfShader;
    private static Texture _sdfOutputTexture;
    private static bool _hasSdfOutputTexture;
    private static Texture _debugAsciiFontAtlas;
    private static bool _hasDebugAsciiFontAtlas;

    /// <summary>
    /// Gets the SDF buffer for advanced command creation.
    /// Use this to add commands with effects (glow, stroke, etc).
    /// </summary>
    public static Sdf.SdfBuffer SdfBuffer
    {
        get
        {
            EnsureSdfInitialized();
            return _sdfRenderer!.Buffer;
        }
    }

    /// <summary>
    /// Initialize SDF rendering system.
    /// Call after InitWindow() and before using any SDF drawing functions.
    /// </summary>
    public static void InitSdf(int maxCommands = 8192)
    {
        EnsureInitialized();

        if (_sdfRenderer != null)
            throw new InvalidOperationException("SDF already initialized");

        // Load SDF compute shader
        _sdfShader = ComputeShader.Load("sdf_compute");

        // Create SDF renderer
        _sdfRenderer = new SdfRenderer(
            Log.Logger,
            _engine!.Device,
            _engine.MemoryAllocator,
            _engine.DescriptorCache,
            _engine.PipelineCache,
            (uint)_engine.Window.FramebufferWidth,
            (uint)_engine.Window.FramebufferHeight,
            maxCommands);

        _sdfRenderer.SetShader(_sdfShader);
        _sdfRenderer.SetFontTextureArray(_engine.TextureArray);

        // Register the SDF output image as an external bindless texture for later compositing.
        var handle = _engine.TextureArray.RegisterExternal(_sdfRenderer.StorageImage.ImageView);
        UpdateTextureDescriptors();
        _sdfOutputTexture = new Texture(handle.Index, (int)_sdfRenderer.StorageImage.Width, (int)_sdfRenderer.StorageImage.Height);
        _hasSdfOutputTexture = true;

        Log.Logger.Information("SDF initialized: {Width}x{Height}, max {MaxCommands} commands",
            _engine.ScreenWidth, _engine.ScreenHeight, maxCommands);
    }

    /// <summary>
    /// Bind a font atlas texture for SDF glyph rendering.
    /// The SDF compute shader will sample the atlas when glyph commands are present.
    /// </summary>
    public static void SetSdfFontAtlas(Texture fontAtlas)
    {
        EnsureSdfInitialized();
        var atlasIndex = fontAtlas.GetIndex();
        var imageView = _engine!.TextureArray.GetImageView(atlasIndex);
        var sampler = _engine.TextureArray.Sampler;
        _sdfRenderer!.SetFontAtlas(imageView, sampler);
    }

    /// <summary>
    /// Bind a font atlas texture to a specific SdfRenderer (for secondary viewports).
    /// </summary>
    public static void SetSdfFontAtlas(Sdf.SdfRenderer renderer, Texture fontAtlas)
    {
        EnsureSdfInitialized();
        var atlasIndex = fontAtlas.GetIndex();
        var imageView = _engine!.TextureArray.GetImageView(atlasIndex);
        var sampler = _engine.TextureArray.Sampler;
        renderer.SetFontAtlas(imageView, sampler);
    }

    public static void SetSdfSecondaryFontAtlas(Texture fontAtlas)
    {
        EnsureSdfInitialized();
        var atlasIndex = fontAtlas.GetIndex();
        var imageView = _engine!.TextureArray.GetImageView(atlasIndex);
        var sampler = _engine.TextureArray.Sampler;
        _sdfRenderer!.SetSecondaryFontAtlas(imageView, sampler);
    }

    public static void SetSdfSecondaryFontAtlas(Sdf.SdfRenderer renderer, Texture fontAtlas)
    {
        EnsureSdfInitialized();
        var atlasIndex = fontAtlas.GetIndex();
        var imageView = _engine!.TextureArray.GetImageView(atlasIndex);
        var sampler = _engine.TextureArray.Sampler;
        renderer.SetSecondaryFontAtlas(imageView, sampler);
    }

    public static void SetSdfFontTextureArray(Sdf.SdfRenderer renderer)
    {
        EnsureSdfInitialized();
        renderer.SetFontTextureArray(_engine!.TextureArray);
    }

    /// <summary>
    /// Draws debug ASCII text using a tiny built-in bitmap atlas.
    /// This is meant for engine bring-up (no asset pipeline / no font loader yet).
    /// </summary>
    public static void DrawDebugText(string text, float x, float y, float pixelSize = 16f, float r = 1f, float g = 1f, float b = 1f, float a = 1f)
    {
        EnsureSdfInitialized();
        if (string.IsNullOrEmpty(text))
            return;

        DrawDebugText(text.AsSpan(), x, y, pixelSize, r, g, b, a);
    }

    /// <summary>
    /// Draws debug ASCII text using a tiny built-in bitmap atlas.
    /// </summary>
    public static void DrawDebugText(ReadOnlySpan<char> text, float x, float y, float pixelSize = 16f, float r = 1f, float g = 1f, float b = 1f, float a = 1f)
    {
        EnsureSdfInitialized();
        if (text.Length == 0)
            return;

        EnsureDebugAsciiFontAtlas();

        float scale = pixelSize / DebugAsciiFontAtlas.CellSize;
        float glyphWidth = DebugAsciiFontAtlas.CellSize * scale;
        float glyphHeight = DebugAsciiFontAtlas.CellSize * scale;

        float penX = x;
        float penY = y;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\n')
            {
                penX = x;
                penY += glyphHeight;
                continue;
            }

            if (ch >= 'a' && ch <= 'z')
            {
                ch = (char)(ch - 32);
            }

            byte codepoint = (byte)'?';
            if (ch <= 127)
            {
                codepoint = (byte)ch;
            }

            DebugAsciiFontAtlas.GetUvRect(codepoint, out float u0, out float v0, out float u1, out float v1);

            float halfW = glyphWidth * 0.5f;
            float halfH = glyphHeight * 0.5f;
            float centerX = penX + halfW;
            float centerY = penY + halfH;

            DrawSdfGlyph(centerX, centerY, halfW, halfH, u0, v0, u1, v1, r, g, b, a);
            penX += glyphWidth;
        }
    }

    /// <summary>
    /// Draw text using a compiled font atlas. Coordinates are top-left of the text block.
    /// Text is rendered as a group, so warps and effects apply to the entire string.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void DrawText(Font font, DerpFormatHandler handler, float x, float y, float fontSizePixels = 16f, float spacingPixels = 0f, float r = 1f, float g = 1f, float b = 1f, float a = 1f)
    {
        DrawText(font, handler.Text, x, y, fontSizePixels, spacingPixels, r, g, b, a);
    }

    /// <summary>
    /// Draw text using a compiled font atlas. Coordinates are top-left of the text block.
    /// Text is rendered as a group, so warps and effects apply to the entire string.
    /// </summary>
    public static void DrawText(Font font, string text, float x, float y, float fontSizePixels = 16f, float spacingPixels = 0f, float r = 1f, float g = 1f, float b = 1f, float a = 1f)
    {
        EnsureSdfInitialized();
        if (string.IsNullOrEmpty(text))
            return;

        DrawText(font, text.AsSpan(), x, y, fontSizePixels, spacingPixels, r, g, b, a);
    }

    /// <summary>
    /// Draw text using a compiled font atlas. Coordinates are top-left of the text block.
    /// Text is rendered as a group, so warps and effects apply to the entire string.
    /// </summary>
    public static void DrawText(Font font, ReadOnlySpan<char> text, float x, float y, float fontSizePixels = 16f, float spacingPixels = 0f, float r = 1f, float g = 1f, float b = 1f, float a = 1f)
    {
        EnsureSdfInitialized();
        if (text.Length == 0)
            return;

        SetSdfFontAtlas(font.Atlas);

        // Use TextRenderer which handles text groups automatically
        Text.TextRenderer.DrawText(_sdfRenderer!.Buffer, font, text, x, y, fontSizePixels, r, g, b, a, spacingPixels);
    }

    /// <summary>
    /// Draw text with effects (stroke, glow, shadow). Effects apply to the entire string as a unit.
    /// </summary>
    public static void DrawTextWithEffects(
        Font font, ReadOnlySpan<char> text, float x, float y, float fontSizePixels,
        float r, float g, float b, float a = 1f,
        float strokeWidth = 0f, Vector4 strokeColor = default,
        float glowRadius = 0f,
        float shadowOffsetX = 0f, float shadowOffsetY = 0f, float shadowBlur = 0f, Vector4 shadowColor = default)
    {
        EnsureSdfInitialized();
        if (text.Length == 0)
            return;

        SetSdfFontAtlas(font.Atlas);

        Text.TextRenderer.DrawText(_sdfRenderer!.Buffer, font, text, x, y, fontSizePixels,
            r, g, b, a, spacing: 0f,
            strokeWidth, strokeColor, glowRadius,
            shadowOffsetX, shadowOffsetY, shadowBlur, shadowColor);
    }

    /// <summary>
    /// Draw text with effects using Vector4 color.
    /// </summary>
    public static void DrawTextWithEffects(
        Font font, ReadOnlySpan<char> text, float x, float y, float fontSizePixels, Vector4 color,
        float strokeWidth = 0f, Vector4 strokeColor = default,
        float glowRadius = 0f,
        float shadowOffsetX = 0f, float shadowOffsetY = 0f, float shadowBlur = 0f, Vector4 shadowColor = default)
    {
        DrawTextWithEffects(font, text, x, y, fontSizePixels,
            color.X, color.Y, color.Z, color.W,
            strokeWidth, strokeColor, glowRadius,
            shadowOffsetX, shadowOffsetY, shadowBlur, shadowColor);
    }

    /// <summary>
    /// Draw an SDF circle.
    /// </summary>
    public static void DrawSdfCircle(float x, float y, float radius, float r, float g, float b, float a = 1f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddCircle(x, y, radius, r, g, b, a);
    }

    /// <summary>
    /// Draw an SDF rectangle.
    /// </summary>
    public static void DrawSdfRect(float x, float y, float halfWidth, float halfHeight, float r, float g, float b, float a = 1f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddRect(x, y, halfWidth, halfHeight, r, g, b, a);
    }

    /// <summary>
    /// Draw an SDF rounded rectangle.
    /// </summary>
    public static void DrawSdfRoundedRect(float x, float y, float halfWidth, float halfHeight, float cornerRadius, float r, float g, float b, float a = 1f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddRoundedRect(x, y, halfWidth, halfHeight, cornerRadius, r, g, b, a);
    }

    /// <summary>
    /// Draw a glyph quad that samples from the bound SDF font atlas.
    /// UVs are normalized (0-1) into the atlas texture.
    /// </summary>
    public static void DrawSdfGlyph(float x, float y, float halfWidth, float halfHeight, float u0, float v0, float u1, float v1, float r, float g, float b, float a = 1f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddGlyph(x, y, halfWidth, halfHeight, u0, v0, u1, v1, r, g, b, a);
    }

    /// <summary>
    /// Draw an SDF line (capsule) from point A to point B.
    /// </summary>
    /// <param name="x1">Start X coordinate.</param>
    /// <param name="y1">Start Y coordinate.</param>
    /// <param name="x2">End X coordinate.</param>
    /// <param name="y2">End Y coordinate.</param>
    /// <param name="thickness">Line thickness (diameter).</param>
    public static void DrawSdfLine(float x1, float y1, float x2, float y2, float thickness, float r, float g, float b, float a = 1f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddLine(x1, y1, x2, y2, thickness, r, g, b, a);
    }

    /// <summary>
    /// Draw an SDF quadratic bezier curve.
    /// </summary>
    /// <param name="x0">Start point X.</param>
    /// <param name="y0">Start point Y.</param>
    /// <param name="cx">Control point X.</param>
    /// <param name="cy">Control point Y.</param>
    /// <param name="x1">End point X.</param>
    /// <param name="y1">End point Y.</param>
    /// <param name="thickness">Curve thickness.</param>
    public static void DrawSdfBezier(float x0, float y0, float cx, float cy, float x1, float y1, float thickness, float r, float g, float b, float a = 1f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddBezier(x0, y0, cx, cy, x1, y1, thickness, r, g, b, a);
    }

    /// <summary>
    /// Draw an SDF polyline from an array of points.
    /// </summary>
    public static void DrawSdfPolyline(ReadOnlySpan<System.Numerics.Vector2> points, float thickness, float r, float g, float b, float a = 1f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddPolyline(points, thickness, r, g, b, a);
    }

    /// <summary>
    /// Draw an SDF regular polygon (triangle, hexagon, etc).
    /// </summary>
    /// <param name="x">Center X.</param>
    /// <param name="y">Center Y.</param>
    /// <param name="radius">Radius from center to vertices.</param>
    /// <param name="sides">Number of sides (3 = triangle, 6 = hexagon, etc).</param>
    /// <param name="thickness">Stroke thickness.</param>
    /// <param name="rotation">Rotation in radians.</param>
    public static void DrawSdfPolygon(float x, float y, float radius, int sides, float thickness, float r, float g, float b, float a = 1f, float rotation = 0f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddPolygon(x, y, radius, sides, thickness, r, g, b, a, rotation);
    }

    /// <summary>
    /// Draw an SDF star shape.
    /// </summary>
    /// <param name="x">Center X.</param>
    /// <param name="y">Center Y.</param>
    /// <param name="outerRadius">Distance to outer points.</param>
    /// <param name="innerRadius">Distance to inner points.</param>
    /// <param name="points">Number of star points (5 = classic star).</param>
    /// <param name="thickness">Stroke thickness.</param>
    /// <param name="rotation">Rotation in radians.</param>
    public static void DrawSdfStar(float x, float y, float outerRadius, float innerRadius, int points, float thickness, float r, float g, float b, float a = 1f, float rotation = 0f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddStar(x, y, outerRadius, innerRadius, points, thickness, r, g, b, a, rotation);
    }

    /// <summary>
    /// Draw a filled SDF polygon (solid, no stroke).
    /// </summary>
    public static void DrawSdfFilledPolygon(float x, float y, float radius, int sides, float r, float g, float b, float a = 1f, float rotation = 0f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddFilledPolygon(x, y, radius, sides, r, g, b, a, rotation);
    }

    /// <summary>
    /// Draw a filled SDF star (solid, no stroke).
    /// </summary>
    public static void DrawSdfFilledStar(float x, float y, float outerRadius, float innerRadius, int points, float r, float g, float b, float a = 1f, float rotation = 0f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddFilledStar(x, y, outerRadius, innerRadius, points, r, g, b, a, rotation);
    }

    /// <summary>
    /// Draw a filled polygon from custom points.
    /// </summary>
    public static void DrawSdfFilledPolygon(ReadOnlySpan<System.Numerics.Vector2> points, float r, float g, float b, float a = 1f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddFilledPolygon(points, r, g, b, a);
    }

    /// <summary>
    /// Draw a waveform/oscilloscope-style line from sample data.
    /// </summary>
    /// <param name="samples">Sample values (Y offsets from baseline).</param>
    /// <param name="startX">Starting X position.</param>
    /// <param name="width">Total width of the waveform.</param>
    /// <param name="baseY">Baseline Y position.</param>
    /// <param name="amplitude">Scale factor for sample values.</param>
    /// <param name="thickness">Line thickness.</param>
    public static void DrawSdfWaveform(ReadOnlySpan<float> samples, float startX, float width, float baseY, float amplitude, float thickness, float r, float g, float b, float a = 1f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddWaveform(samples, startX, width, baseY, amplitude, thickness, r, g, b, a);
    }

    /// <summary>
    /// Draw a profiler-style graph from values.
    /// </summary>
    /// <param name="values">Graph values.</param>
    /// <param name="x">Left edge X position.</param>
    /// <param name="y">Top edge Y position.</param>
    /// <param name="width">Graph width.</param>
    /// <param name="height">Graph height.</param>
    /// <param name="minValue">Minimum value for scaling.</param>
    /// <param name="maxValue">Maximum value for scaling.</param>
    /// <param name="thickness">Line thickness.</param>
    public static void DrawSdfGraph(ReadOnlySpan<float> values, float x, float y, float width, float height, float minValue, float maxValue, float thickness, float r, float g, float b, float a = 1f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.AddGraph(values, x, y, width, height, minValue, maxValue, thickness, r, g, b, a);
    }

    // ========================================================================
    // SDF Warps
    // ========================================================================

    /// <summary>
    /// Push a warp onto the stack. All subsequent SDF shapes will have this warp applied.
    /// </summary>
    public static void PushSdfWarp(Sdf.SdfWarp warp)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.PushWarp(warp);
    }

    /// <summary>
    /// Pop the top warp from the stack.
    /// </summary>
    public static void PopSdfWarp()
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.PopWarp();
    }

    // ========================================================================
    // SDF MASK API
    // ========================================================================

    /// <summary>
    /// Push a mask onto the stack. All subsequent SDF shapes will have their alpha
    /// multiplied by the mask's coverage. Use with SdfMaskShape factory methods.
    /// </summary>
    /// <param name="mask">Mask command created by SdfMaskShape factory methods.</param>
    public static void PushSdfMask(Sdf.SdfCommand mask)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.PushMask(mask);
    }

    /// <summary>
    /// Push a union of masks onto the stack. The resulting mask is visible where
    /// ANY of the input shapes are visible.
    /// </summary>
    /// <param name="unionMasks">Array of mask commands to union (from SdfMaskShape.Union).</param>
    public static void PushSdfMask(Sdf.SdfCommand[] unionMasks)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.PushMask(unionMasks);
    }

    /// <summary>
    /// Pop the top mask from the stack.
    /// </summary>
    public static void PopSdfMask()
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.PopMask();
    }

    // ========================================================================
    // SDF WARP CONVENIENCE METHODS
    // ========================================================================

    /// <summary>
    /// Push a wave warp. Sinusoidal distortion along one axis.
    /// </summary>
    /// <param name="frequency">How many waves per 100 pixels.</param>
    /// <param name="amplitude">How far the wave displaces (in pixels).</param>
    /// <param name="phase">Phase offset (use time for animation).</param>
    public static void PushSdfWave(float frequency, float amplitude, float phase = 0f)
    {
        PushSdfWarp(Sdf.SdfWarp.Wave(frequency, amplitude, phase));
    }

    /// <summary>
    /// Push a twist warp. Rotates coordinates based on distance from shape center.
    /// </summary>
    /// <param name="strength">Radians of rotation per 100 pixels from center.</param>
    public static void PushSdfTwist(float strength)
    {
        PushSdfWarp(Sdf.SdfWarp.Twist(strength));
    }

    /// <summary>
    /// Push a bulge warp. Fisheye/pinch effect.
    /// </summary>
    /// <param name="strength">Positive = bulge out, negative = pinch in.</param>
    /// <param name="radius">Radius of effect (0 = use default falloff).</param>
    public static void PushSdfBulge(float strength, float radius = 0f)
    {
        PushSdfWarp(Sdf.SdfWarp.Bulge(strength, radius));
    }

    /// <summary>
    /// Register a lattice for FFD (Free-Form Deformation) and return its index.
    /// Use the index with PushSdfLattice().
    /// </summary>
    public static int AddSdfLattice(in Sdf.SdfLattice lattice)
    {
        EnsureSdfInitialized();
        return _sdfRenderer!.Buffer.AddLattice(lattice);
    }

    /// <summary>
    /// Push a pre-registered lattice warp onto the stack.
    /// </summary>
    /// <param name="latticeIndex">Index returned from AddSdfLattice().</param>
    /// <param name="scaleX">Width of the lattice effect area in pixels.</param>
    /// <param name="scaleY">Height of the lattice effect area in pixels.</param>
    public static void PushSdfLattice(int latticeIndex, float scaleX = 200f, float scaleY = 200f)
    {
        EnsureSdfInitialized();
        _sdfRenderer!.Buffer.PushLattice(latticeIndex, scaleX, scaleY);
    }

    /// <summary>
    /// Add a lattice and immediately push it onto the warp stack.
    /// Convenience method combining AddSdfLattice + PushSdfLattice.
    /// </summary>
    public static int PushNewSdfLattice(in Sdf.SdfLattice lattice, float scaleX = 200f, float scaleY = 200f)
    {
        EnsureSdfInitialized();
        return _sdfRenderer!.Buffer.PushNewLattice(lattice, scaleX, scaleY);
    }

    /// <summary>
    /// Render all SDF commands and blit to screen.
    /// Call this at the end of your frame, before EndDrawing().
    /// </summary>
    public static void RenderSdf()
    {
        EnsureSdfInitialized();

        var renderer = _sdfRenderer!;
        if (renderer.Buffer.Count == 0)
            return;

        // Handle resize
        int framebufferWidth = _engine!.Window.FramebufferWidth;
        int framebufferHeight = _engine.Window.FramebufferHeight;
        renderer.Resize((uint)framebufferWidth, (uint)framebufferHeight);
        SyncSdfOutputTexture();

        // Build tile data (CPU-side for GPU consumption)
        renderer.Buffer.Build(framebufferWidth, framebufferHeight);

        // Flush commands and tile data to GPU (per-frame buffer for double buffering)
        renderer.Buffer.Flush(_engine.FrameIndex);

        // Dispatch compute shader
        var cmd = _engine.GetCommandBuffer();
        renderer.Dispatch(cmd, _engine.FrameIndex);

        // Blit to swapchain
        var swapchain = _engine.Swapchain;
        renderer.BlitToSwapchain(cmd,
            swapchain.Images[_engine.CurrentImageIndex],
            swapchain.Extent.Width,
            swapchain.Extent.Height);

        // Reset for next frame
        renderer.Reset();
    }

    /// <summary>
    /// Dispatch SDF compute into its output texture and transition it for sampling.
    /// This does NOT present or blit; use DrawTexture(SdfOutputTexture, ...) to composite it in a render pass.
    /// </summary>
    public static void DispatchSdfToTexture()
    {
        EnsureSdfInitialized();

        using var _scope = ProfileScope.Begin(ProfileScopes.SdfRender);

        var renderer = _sdfRenderer!;
        if (renderer.Buffer.Count == 0)
            return;

        int framebufferWidth = _engine!.Window.FramebufferWidth;
        int framebufferHeight = _engine.Window.FramebufferHeight;
        renderer.Resize((uint)framebufferWidth, (uint)framebufferHeight);
        SyncSdfOutputTexture();

        {
            using var _buildScope = ProfileScope.Begin(ProfileScopes.SdfBuild);
            renderer.Buffer.Build(framebufferWidth, framebufferHeight);
        }

        {
            using var _flushScope = ProfileScope.Begin(ProfileScopes.SdfFlush);
            renderer.Buffer.Flush(_engine.FrameIndex);
        }

        var cmd = _engine.GetCommandBuffer();
        {
            using var _dispatchScope = ProfileScope.Begin(ProfileScopes.SdfDispatch);
            using (GpuScope.Begin(cmd, _engine.FrameIndex, GpuScopes.SdfDispatch))
            {
                renderer.Dispatch(cmd, _engine.FrameIndex);
            }
        }

        {
            using var _transitionScope = ProfileScope.Begin(ProfileScopes.SdfTransition);
            using (GpuScope.Begin(cmd, _engine.FrameIndex, GpuScopes.SdfTransition))
            {
                renderer.TransitionOutputForSampling(cmd);
            }
        }

        renderer.Reset();
    }

    /// <summary>
    /// Gets the bindless texture that references the SDF output image.
    /// Call DispatchSdfToTexture() each frame before sampling it.
    /// </summary>
    public static Texture SdfOutputTexture
    {
        get
        {
            EnsureSdfInitialized();
            if (!_hasSdfOutputTexture)
                throw new InvalidOperationException("SDF output texture not initialized.");
            return _sdfOutputTexture;
        }
    }

    /// <summary>
    /// Get the current SDF command count.
    /// </summary>
    public static int SdfCommandCount => _sdfRenderer?.Buffer.Count ?? 0;

    private static void EnsureSdfInitialized()
    {
        if (_sdfRenderer == null)
            throw new InvalidOperationException("SDF not initialized. Call InitSdf() first.");
    }

    private static void EnsureDebugAsciiFontAtlas()
    {
        if (_hasDebugAsciiFontAtlas)
            return;

        var rgba = DebugAsciiFontAtlas.CreateRgba8Atlas();
        _debugAsciiFontAtlas = LoadTexture(rgba, DebugAsciiFontAtlas.Width, DebugAsciiFontAtlas.Height);
        SetSdfFontAtlas(_debugAsciiFontAtlas);
        _hasDebugAsciiFontAtlas = true;
    }

    private static void SyncSdfOutputTexture()
    {
        if (!_hasSdfOutputTexture)
            return;

        int width = (int)_sdfRenderer!.StorageImage.Width;
        int height = (int)_sdfRenderer.StorageImage.Height;
        if (_sdfOutputTexture.Width == width && _sdfOutputTexture.Height == height)
            return;

        // StorageImage recreates its image view on resize, so keep bindless slot in sync.
        var index = _sdfOutputTexture.GetIndex();
        _engine!.TextureArray.UpdateExternal(index, _sdfRenderer!.StorageImage.ImageView);
        UpdateTextureDescriptors();
        _sdfOutputTexture = new Texture(index, width, height);
    }

    #endregion

    #region Profiler

    /// <summary>
    /// Whether the profiler window is visible.
    /// </summary>
    public static bool ProfilerVisible => ProfilerWindow.Visible;

    /// <summary>
    /// Toggle the profiler window visibility.
    /// </summary>
    public static void ToggleProfiler() => ProfilerWindow.Toggle();

    /// <summary>
    /// Initialize the CPU profiling service.
    /// Call once after InitWindow and before the main loop.
    /// </summary>
    public static void InitProfiler()
    {
        EnsureInitialized();
        // Engine initialization creates the profiling services. This enables optional allocation tracking.
        AllocationTracker.Enable(Log.Logger, logEveryFrame: false);
    }

    /// <summary>
    /// Draw the profiler overlay. Call after Im.End() but before EndDrawing().
    /// </summary>
    public static void DrawProfiler()
    {
        if (!ProfilerWindow.Visible) return;

        EnsureInitialized();
        var stats = ProfilerStats.Gather(
            meshInstances: _engine!.MeshInstanceCount,
            sdfCommands: SdfCommandCount,
            textureCount: _engine.TextureCount);

        ProfilerWindow.Draw(stats);
    }

    /// <summary>
    /// Whether the flame graph window is visible.
    /// </summary>
    public static bool FlameGraphVisible => FlameGraphWindow.Visible;

    /// <summary>
    /// Toggle the flame graph window visibility.
    /// </summary>
    public static void ToggleFlameGraph() => FlameGraphWindow.Toggle();

    /// <summary>
    /// Draw the flame graph overlay. Call after Im.End() but before EndDrawing().
    /// </summary>
    public static void DrawFlameGraph()
    {
        if (!FlameGraphWindow.Visible) return;

        EnsureInitialized();
        FlameGraphWindow.Draw();
    }

    #endregion

    private static void EnsureInitialized()
    {
        if (_engine == null)
            throw new InvalidOperationException("Derp not initialized. Call InitWindow() first.");
    }
}
