using System.Diagnostics;
using System.Numerics;
using Serilog;
using Silk.NET.Input;
using DerpLib.ImGui;
using DerpLib.ImGui.Layout;
using DerpLib.Rendering;
using DerpLib.Text;

namespace DerpLib.Examples;

/// <summary>
/// 3D rendering demo with FPS camera, Sponza model, and instanced cubes.
/// </summary>
public static class Demo3D
{
    public static void Run()
    {
        // Initialize window and graphics
        Derp.InitWindow(1280, 720, "Derp Example - 3D + 2D Rendering");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Derp.InitProfiler();

        // Initialize SDF for ImGui rendering
        Derp.InitSdf();

        // Load font for text rendering
        var font = Derp.LoadFont("arial");
        Derp.SetSdfFontAtlas(font.Atlas);

        // Initialize ImGUI (single viewport mode - no window extraction)
        Im.Initialize(enableMultiViewport: false);
        Im.SetFont(font);

        // Create textures
        var whiteTexture = Derp.LoadSolidColor(255, 255, 255);
        var redTexture = Derp.LoadSolidColor(255, 100, 100);

        // Load texture from file (compiled PNG)
        var testTexture = Derp.LoadTexture("test");
        Log.Information("Loaded texture from file: {Width}x{Height}", testTexture.Width, testTexture.Height);

        // Load 3D mesh (instanced for high-frequency rendering)
        var cubeMesh = Derp.LoadMesh("cube", instanced: true, capacity: 10_000_000);
        Log.Information("Loaded cube mesh (instanced, capacity: 1,000,000)");

        // Load Sponza model with embedded textures
        var sponzaModel = Derp.LoadModel("sponza");
        Log.Information("Loaded Sponza: {Submeshes} submeshes, {Textures} textures",
            sponzaModel.SubmeshCount, sponzaModel.TextureCount);

        // Initialize instanced mesh buffer (must call after registering all instanced meshes)
        Derp.InitializeInstancedMeshes();

        // === TEST COMPUTE SHADER ===
        TestComputeShader();

        // Track stats
        int initialVkAllocs = Derp.MemoryAllocator.AllocationCount;
        long initialHeapBytes = GC.GetAllocatedBytesForCurrentThread();
        int initialGen0 = GC.CollectionCount(0);
        var stopwatch = Stopwatch.StartNew();

        float time = 0f;
        int frameCount = 0;

        // FPS tracking
        double lastFpsUpdate = 0;
        int framesSinceLastUpdate = 0;

        // FPS camera state
        var camPos = new Vector3(0, 2f, 10f);  // Starting position
        float camYaw = MathF.PI;    // Facing -Z (into Sponza)
        float camPitch = 0f;        // Level
        const float mouseSensitivity = 0.003f;
        const float moveSpeed = 5f;  // Units per second
        const float sprintMultiplier = 2.5f;
        int cubeCount = 100;  // Adjustable with Q/E (start high to stress test instanced buffer)
        bool qWasDown = false, eWasDown = false;
        bool f5WasDown = false, f6WasDown = false;

        while (!Derp.WindowShouldClose())
        {
            Derp.PollEvents();

            // === FPS CAMERA CONTROLS ===
            float dt = 0.016f; // ~60fps, TODO: use actual delta time

            // Right-click to enable mouse look
            if (Derp.IsMouseButtonDown(MouseButton.Right))
            {
                var delta = Derp.GetMouseDelta();
                camYaw -= delta.X * mouseSensitivity;  // Negative for natural feel
                camPitch -= delta.Y * mouseSensitivity;
                // Clamp pitch to avoid flipping
                camPitch = Math.Clamp(camPitch, -1.5f, 1.5f);
            }

            // Calculate forward and right vectors from yaw/pitch
            var forward = new Vector3(
                MathF.Cos(camPitch) * MathF.Sin(camYaw),
                MathF.Sin(camPitch),
                MathF.Cos(camPitch) * MathF.Cos(camYaw)
            );
            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

            // WASD movement (XZ plane for consistent ground movement)
            var forwardXZ = Vector3.Normalize(new Vector3(forward.X, forward.Y, forward.Z));
            var rightXZ = Vector3.Normalize(new Vector3(right.X, 0, right.Z));

            float forwardInput = (Derp.IsKeyDown(Key.W) ? 1 : 0) - (Derp.IsKeyDown(Key.S) ? 1 : 0);
            float sideInput = (Derp.IsKeyDown(Key.D) ? 1 : 0) - (Derp.IsKeyDown(Key.A) ? 1 : 0);

            float speed = moveSpeed * (Derp.IsKeyDown(Key.ShiftLeft) ? sprintMultiplier : 1f);
            camPos += forwardXZ * speed * dt * forwardInput;
            camPos += rightXZ * speed * dt * sideInput;

            // Q/E to adjust cube count (with debounce)
            bool qDown = Derp.IsKeyDown(Key.Q);
            bool eDown = Derp.IsKeyDown(Key.E);
            if (qDown && !qWasDown) cubeCount = Math.Max(1, cubeCount - 10);
            if (eDown && !eWasDown) cubeCount += 10;
            qWasDown = qDown;
            eWasDown = eDown;

            // F5 to toggle profiler, F6 to toggle flame graph
            bool f5Down = Derp.IsKeyDown(Key.F5);
            if (f5Down && !f5WasDown) Derp.ToggleProfiler();
            f5WasDown = f5Down;

            bool f6Down = Derp.IsKeyDown(Key.F6);
            if (f6Down && !f6WasDown) Derp.ToggleFlameGraph();
            f6WasDown = f6Down;

            if (!Derp.BeginDrawing())
                continue;

            time += 0.016f;

            // === IMGUI FRAME START ===
            Derp.SdfBuffer.Reset();
            Im.Begin(dt);

            // === 3D RENDERING ===
            var camTarget = camPos + forward;

            var camera3D = new Camera3D(
                position: camPos,
                target: camTarget,
                fovY: MathF.PI / 4f,
                near: 0.1f,
                far: 10000f
            );

            // Begin 3D pass (clears with sky blue background)
            Derp.ClearBackground(0.4f, 0.6f, 0.9f);
            Derp.BeginCamera3D(camera3D);

            // Draw Sponza (scaled down - model is typically in centimeters)
            Derp.DrawModel(sponzaModel, Vector3.Zero, 0.01f);  // Convert cm to meters

            // Optional: Draw some cubes for reference
            if (cubeCount > 0)
            {
                for (int z = -cubeCount; z <= cubeCount; z++)
                {
                    for (int x = -cubeCount; x <= cubeCount; x++)
                    {
                        float cubeY = 5f;  // MathF.Sin(time * 2f + x + z) * 0.3f + 5f;
                        float rotationY = 0f;  // time + (x + z) * 0.5f;

                        var position = new Vector3(x * 1.5f, cubeY, z * 1.5f);

                        // Color based on position
                        byte r = (byte)(128 + x * 30);
                        byte g = (byte)(128 + z * 30);
                        byte b = 200;

                        // Direct to GPU - no CPU matrix
                        Derp.DrawMesh(cubeMesh, position, rotationY, 0.4f, r, g, b, 255);
                    }
                }
            }

            Derp.EndCamera3D();

            // === IMGUI UI ===
            int totalCubes = (2 * cubeCount + 1) * (2 * cubeCount + 1);
            if (Im.BeginWindow("Camera Info", 10, 10, 250, 180))
            {
                Im.Label($"Position: {camPos.X:F1}, {camPos.Y:F1}, {camPos.Z:F1}");
                Im.Label($"Yaw: {camYaw:F2}, Pitch: {camPitch:F2}");
                Im.Label($"Cubes: {totalCubes} (Q/E to adjust)");
                Im.Label($"Frame: {frameCount}");

                ImLayout.Space(10);

                float newCubeCount = cubeCount;
                if (Im.Slider("Cube Grid", ref newCubeCount, 0, 50))
                {
                    cubeCount = (int)newCubeCount;
                }
            }
            Im.EndWindow();

            // Draw profiler overlays (F5 for stacked, F6 for flame graph)
            Derp.DrawProfiler();
            Derp.DrawFlameGraph();

            // End ImGui and dispatch SDF to texture
            Im.End();
            Derp.DispatchSdfToTexture();

            // === 2D RENDERING: Composite SDF UI ===
            var camera2D = new Camera2D(Vector2.Zero, Vector2.Zero, 0f, 1f);
            Derp.BeginCamera2D(camera2D);

            // Draw SDF output texture as fullscreen overlay (with alpha blending)
            // Flip Y because quad mesh UVs are designed for Y-up, but screen coords are Y-down
            var sdfTex = Derp.SdfOutputTexture;
            var sdfTransform = Matrix4x4.CreateScale(sdfTex.Width, -sdfTex.Height, 1) *
                               Matrix4x4.CreateTranslation(sdfTex.Width / 2f, sdfTex.Height / 2f, 0);
            Derp.DrawTextureTransform(sdfTex, sdfTransform, 255, 255, 255, 255);

            Derp.EndCamera2D();

            Derp.EndDrawing();
            frameCount++;
            framesSinceLastUpdate++;

            // Update FPS in title every 0.5 seconds
            double elapsed = stopwatch.Elapsed.TotalSeconds;
            if (elapsed - lastFpsUpdate >= 0.5)
            {
                double currentFps = framesSinceLastUpdate / (elapsed - lastFpsUpdate);
                Derp.SetWindowTitle($"Derp - {currentFps:F1} FPS | WASD+Mouse(RMB) | Shift=Sprint | Q/E={totalCubes} cubes");
                lastFpsUpdate = elapsed;
                framesSinceLastUpdate = 0;
            }

            if (frameCount == 1)
            {
                // 5x5 grid of cubes + 1 center cube + 1 UI quad = 27 instances
                Log.Information("First frame: 27 3D instances (5x5 grid + center cube + UI)");
            }
        }

        // Report stats
        stopwatch.Stop();
        double seconds = stopwatch.Elapsed.TotalSeconds;
        double fps = frameCount / seconds;
        int vkAllocs = Derp.MemoryAllocator.AllocationCount - initialVkAllocs;
        long heapBytes = GC.GetAllocatedBytesForCurrentThread() - initialHeapBytes;
        long bytesPerFrame = heapBytes / Math.Max(1, frameCount);
        int gen0 = GC.CollectionCount(0) - initialGen0;

        Log.Information("=== Results ===");
        Log.Information("3D Demo: 26 cubes + 1 UI quad per frame");
        Log.Information("Frames: {Frames} in {Seconds:F1}s ({Fps:F1} FPS)", frameCount, seconds, fps);
        Log.Information("Vulkan allocs during loop: {Allocs}", vkAllocs);
        Log.Information("Heap: {Bytes} bytes ({PerFrame} bytes/frame)", heapBytes, bytesPerFrame);
        Log.Information("GC Gen0: {Count}", gen0);

        Derp.CloseWindow();
    }

    private static void TestComputeShader()
    {
        Log.Information("=== Testing Compute Shader ===");

        // Load compute shader
        var computeShader = Derp.LoadComputeShader("test_compute");
        Log.Information("Loaded compute shader: test_compute");

        // Create buffer with readback support
        const int count = 1024;
        var buffer = Derp.CreateBuffer<uint>(count, readable: true);
        Log.Information("Created readable buffer: {Count} elements", count);

        // We need to be in a frame to dispatch compute
        // Do a minimal frame just for compute
        if (!Derp.BeginDrawing())
        {
            Log.Error("Failed to begin frame for compute test");
            return;
        }

        // Dispatch compute shader
        Derp.BeginCompute();
        Derp.BindStorageBuffer(0, buffer);
        Derp.SetComputePushConstants(computeShader, count);  // push count as uint
        uint groups = (uint)((count + 255) / 256);  // ceil(count / 256)
        Derp.DispatchCompute(computeShader, groups, 1, 1, sizeof(uint));
        Derp.EndCompute();

        Log.Information("Dispatched compute: {Groups} groups x 256 threads", groups);

        // End the frame (submits commands)
        Derp.EndDrawing();

        // Now read back results (blocking)
        var data = buffer.Map();

        // Verify first 10 values
        bool allCorrect = true;
        for (int i = 0; i < 10; i++)
        {
            uint expected = (uint)(i * 2);
            if (data[i] != expected)
            {
                Log.Error("Mismatch at [{Index}]: expected {Expected}, got {Actual}", i, expected, data[i]);
                allCorrect = false;
            }
        }

        buffer.Unmap();

        if (allCorrect)
        {
            Log.Information("Compute shader test PASSED: data[0..9] = {V0}, {V1}, {V2}, {V3}, {V4}, {V5}, {V6}, {V7}, {V8}, {V9}",
                data[0], data[1], data[2], data[3], data[4], data[5], data[6], data[7], data[8], data[9]);
        }
        else
        {
            Log.Error("Compute shader test FAILED");
        }

        Log.Information("=== Compute Test Complete ===");
    }
}
