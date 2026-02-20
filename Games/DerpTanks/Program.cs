using System.IO;
using System.Numerics;
using Serilog;
using DerpLib;
using DerpLib.Diagnostics;
using DerpLib.ImGui;
using DerpLib.Rendering;
using DerpLib.Text;
using Core;
using Core.Input;
using DerpLib.Ecs;
using DerpTanks.Simulation.Ecs;
using DerpTanks.Simulation;
using DerpTanks.Simulation.Services;
using DerpTanks.Simulation.Systems;
using FixedMath;
using FlowField;
using Pooled.Runtime;
using Silk.NET.Input;

namespace DerpTanks;

/// <summary>
/// Simple 3D tank game demonstrating DerpLib Engine with top-down camera
/// and JSON-based tank controls via DerpInputManager.
/// </summary>
public static class Program
{
    private static readonly StringHandle TankMapName = "Tank";
    private static readonly StringHandle AimActionName = "Aim";
    private static readonly StringHandle FireActionName = "Fire";

    public static void Main(string[] args)
    {
        // Setup logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== DerpTanks ===");

        // Initialize window
        Derp.InitWindow(1280, 720, "DerpTanks");
        Derp.InitSdf();
        Derp.InitProfiler();

        // Load assets
        var font = Derp.LoadFont("arial");
        Derp.SetSdfFontAtlas(font.Atlas);
        var cubeMesh = Derp.LoadMesh("cube", instanced: true, capacity: 12000);
        var turretMesh = CreateTurretMesh();
        var densityQuadMesh = CreateDensityQuadMesh();
        Derp.InitializeInstancedMeshes();

        // Debug UI (ProfilerWindow / FlameGraphWindow)
        Im.Initialize(enableMultiViewport: false);
        Im.SetFont(font);

        // Initialize input with DerpInputManager
        var inputManager = new DerpInputManager();
        var inputConfigPath = Path.Combine(AppContext.BaseDirectory, "Resources", "input-tank.json");
        InputConfigLoader.LoadFromFile(inputManager, inputConfigPath);
        inputManager.PushContext("Tank");

        // Tank state
        var tankPos = Vector3.Zero;
        float tankRotation = 0f;
        var tankController = new TankController(inputManager);

        // Simulation world (deterministic)
        var simWorld = new SimEcsWorld();
        var worldProvider = new TankWorldProvider();
        var zoneGraph = new ZoneGraph(worldProvider);
        var poolRegistry = new World();
        IZoneFlowService flowService = new ZoneFlowService(zoneGraph, worldProvider, poolRegistry);

        simWorld.Initialize(flowService, sessionSeed: 12345, tileSize: worldProvider.TileSize, queryBufferSize: 2048);
        simWorld.Horde.RebuildSpatialIndex();

        var simPipeline = new EcsSystemPipeline<SimEcsWorld>(new IEcsSystem<SimEcsWorld>[]
        {
            new HordeSpawnSystem(),
            new HordeFlowMovementSystem(),
            new HordeSeparationSystem(),
            new HordeApplyMovementSystem(),
            new ProjectileFireSystem(),
            new ProjectileMovementSystem(),
            new ProjectileHitSystem(),
            new HordeDeathSystem(),
        });

        // Top-down camera
        float cameraHeight = 80f;
        bool showDensityGrid = false;
        bool showFrameInfo = false;
        bool showShotDebug = false;
        Vector2 aimDirection = Vector2.UnitY;
        float turretRotation = 0f;

        while (!Derp.WindowShouldClose())
        {
            Derp.PollEvents();
            float dt = 0.016f; // ~60fps

            // Update input
            inputManager.Update(dt);

            // Update tank
            tankController.Update(dt, ref tankPos, ref tankRotation);

            // Camera is needed for mouse aim (screen->world raycast) and for rendering.
            var camera = new Camera3D(
                position: new Vector3(tankPos.X, cameraHeight, tankPos.Z + 5f),  // Slight offset
                target: tankPos,
                fovY: MathF.PI / 4f,
                near: 0.1f,
                far: 2000f
            );

            aimDirection = ResolveAimDirection(inputManager, camera, tankPos, aimDirection);
            if (aimDirection.LengthSquared() > 0f)
            {
                turretRotation = MathF.Atan2(aimDirection.X, aimDirection.Y);
            }

            if (Derp.IsKeyPressed(Key.F1))
            {
                showDensityGrid = !showDensityGrid;
            }

            if (Derp.IsKeyPressed(Key.F2))
            {
                Derp.ToggleProfiler();
            }

            if (Derp.IsKeyPressed(Key.F3))
            {
                Derp.ToggleFlameGraph();
            }

            if (Derp.IsKeyPressed(Key.F4))
            {
                showFrameInfo = !showFrameInfo;
            }

            if (Derp.IsKeyPressed(Key.F6))
            {
                showShotDebug = !showShotDebug;
            }

            // Simulation tick
            simWorld.DeltaTime = Fixed64.FromFloat(dt);
            simWorld.PlayerPosition = Fixed64Vec2.FromFloat(tankPos.X, tankPos.Z);
            simWorld.PlayerForward = Fixed64Vec2.FromFloat(aimDirection.X, aimDirection.Y);
            simWorld.FireRequested = inputManager.IsActive(TankMapName, FireActionName);
            simWorld.CurrentFrame++;
            simPipeline.RunFrame(simWorld);

            if (!Derp.BeginDrawing())
                continue;

            // Clear background (dark gray ground)
            Derp.ClearBackground(0.2f, 0.25f, 0.3f);

            // Top-down camera (looking straight down at tank)
            Derp.BeginCamera3D(camera);

            // Draw tank (green cube)
            Derp.DrawMesh(cubeMesh, tankPos, tankRotation, 1f, 50, 180, 80, 255);
            DrawTankTurret(turretMesh, tankPos, turretRotation);

            // Draw ground grid (simple reference cubes)
            for (int x = -10; x <= 10; x += 2)
            {
                for (int z = -10; z <= 10; z += 2)
                {
                    Derp.DrawMesh(cubeMesh, new Vector3(x, -0.5f, z), 0f, 0.1f, 80, 80, 80, 255);
                }
            }

            if (showDensityGrid)
            {
                RenderDensityGridQuads(densityQuadMesh, simWorld);
            }

            // Draw horde (red cubes)
            for (int row = 0; row < simWorld.Horde.Count; row++)
            {
                ref var transform = ref simWorld.Horde.Transform(row);
                var hordePos = new Vector3(transform.Position.X.ToFloat(), 0f, transform.Position.Y.ToFloat());
                Derp.DrawMesh(cubeMesh, hordePos, 0f, 0.6f, 200, 60, 60, 255);
            }

            // Draw projectiles (yellow cubes)
            for (int row = 0; row < simWorld.Projectile.Count; row++)
            {
                ref var transform = ref simWorld.Projectile.ProjectileTransform(row);
                var projectilePos = new Vector3(transform.Position.X.ToFloat(), 0.2f, transform.Position.Y.ToFloat());
                Derp.DrawMesh(cubeMesh, projectilePos, 0f, 0.25f, 240, 220, 80, 255);
            }

            if (showShotDebug && simWorld.DebugLastShotFrame >= 0)
            {
                int age = simWorld.CurrentFrame - simWorld.DebugLastShotFrame;
                if ((uint)age <= 90u)
                {
                    var spawnPos = new Vector3(simWorld.DebugLastShotPosition.X.ToFloat(), 0.35f, simWorld.DebugLastShotPosition.Y.ToFloat());
                    Derp.DrawMesh(cubeMesh, spawnPos, 0f, 0.40f, 80, 200, 255, 255);
                }
            }

            Derp.EndCamera3D();

            // SDF UI overlay
            Derp.SdfBuffer.Reset();
            Derp.DrawText(font, $"Tank: ({tankPos.X:F1}, {tankPos.Z:F1}) Rot: {tankRotation:F2}", 10, 10, 20f);
            Derp.DrawText(font, $"Horde: {simWorld.Horde.Count} Wave: {simWorld.CurrentWave} Remaining: {simWorld.WaveSpawnRemaining}", 10, 35, 16f);
            Derp.DrawText(font, $"Projectiles: {simWorld.Projectile.Count} | Fire cd: {Math.Max(0, simWorld.NextFireFrame - simWorld.CurrentFrame)}f", 10, 55, 16f);
            int playerCellX = HordeSeparationGrid.GetCellX(simWorld.PlayerPosition);
            int playerCellY = HordeSeparationGrid.GetCellY(simWorld.PlayerPosition);
            int playerCellIndex = playerCellX + playerCellY * HordeSeparationGrid.GridSize;
            int playerDensity = simWorld.SeparationDensity[playerCellIndex];
            Derp.DrawText(font, $"Density: cell ({playerCellX}, {playerCellY}) = {playerDensity}", 10, 75, 16f);
            Derp.DrawText(font, "A/D/LS-X: Turn | W/S/LT/RT: Move | Mouse/RS: Aim | Space/LMB/A/RS: Fire", 10, 95, 16f, 0f, 0.7f, 0.7f, 0.7f);
            Derp.DrawText(font, showDensityGrid ? "F1: Density grid ON" : "F1: Density grid OFF", 10, 115, 16f, 0f, 0.7f, 0.7f, 0.7f);
            Derp.DrawText(font, $"F2: Profiler {(Derp.ProfilerVisible ? "ON" : "OFF")} | F3: Flame {(Derp.FlameGraphVisible ? "ON" : "OFF")}", 10, 135, 16f, 0f, 0.7f, 0.7f, 0.7f);
            Derp.DrawText(font, showFrameInfo ? "F4: Frame info ON" : "F4: Frame info OFF", 10, 155, 16f, 0f, 0.7f, 0.7f, 0.7f);
            Derp.DrawText(font, showShotDebug ? "F6: Shot debug ON" : "F6: Shot debug OFF", 10, 175, 16f, 0f, 0.7f, 0.7f, 0.7f);

            if (showFrameInfo)
            {
                var stats = ProfilerStats.Gather(meshInstances: 0, sdfCommands: Derp.SdfCommandCount, textureCount: 0);
                double cpuMs = stats.CpuFrameMs;
                double gpuMs = stats.GpuFrameMs;
                double frameMs = Math.Max(cpuMs, gpuMs);
                double fps = frameMs > 0 ? 1000.0 / frameMs : 0;

                Derp.DrawText(font, $"Frame: {frameMs:F2}ms ({fps:F0} FPS) | CPU: {cpuMs:F2}ms | GPU: {gpuMs:F2}ms", 10, 200, 16f);
                Derp.DrawText(font, $"SDF cmds: {Derp.SdfCommandCount} | Alloc: {stats.AllocatedThisFrame}B | GC: {stats.GcGen0}/{stats.GcGen1}/{stats.GcGen2}", 10, 220, 16f);
            }

            if (showShotDebug && simWorld.DebugLastShotFrame >= 0)
            {
                int shotAge = simWorld.CurrentFrame - simWorld.DebugLastShotFrame;
                Derp.DrawText(font, $"Shot: age {shotAge}f rawId {simWorld.DebugLastShotRawId} pos ({simWorld.DebugLastShotPosition.X.ToFloat():F2},{simWorld.DebugLastShotPosition.Y.ToFloat():F2}) dir ({simWorld.DebugLastShotDirection.X.ToFloat():F2},{simWorld.DebugLastShotDirection.Y.ToFloat():F2})", 10, 240, 16f);
            }

            if (Derp.ProfilerVisible || Derp.FlameGraphVisible)
            {
                Im.Begin(dt);
                Derp.DrawProfiler();
                Derp.DrawFlameGraph();
                Im.End();
            }

            // Dispatch SDF into an output texture and composite it via a 2D pass, so it doesn't overwrite the 3D frame.
            Derp.DispatchSdfToTexture();
            var camera2D = new Camera2D(Vector2.Zero, Vector2.Zero, 0f, 1f);
            Derp.BeginCamera2D(camera2D);

            // Composite the SDF output texture over the 3D frame.
            // Important: 2D camera projection uses logical window size, but SDF output texture is framebuffer-sized on HiDPI.
            // Scale/translate using logical screen dimensions so UI isn't offset or clipped.
            var sdfTex = Derp.SdfOutputTexture;
            int screenWidth = Derp.GetScreenWidth();
            int screenHeight = Derp.GetScreenHeight();
            var sdfTransform =
                Matrix4x4.CreateScale(screenWidth, -screenHeight, 1f) *
                Matrix4x4.CreateTranslation(screenWidth * 0.5f, screenHeight * 0.5f, 0f);
            Derp.DrawTextureTransform(sdfTex, sdfTransform, 255, 255, 255, 255);

            Derp.EndCamera2D();

            Derp.EndDrawing();
        }

        Derp.CloseWindow();
        Log.Information("=== DerpTanks Closed ===");
    }

    private static Vector2 ResolveAimDirection(IInputManager inputManager, Camera3D camera, Vector3 tankPos, Vector2 previousAimDirection)
    {
        var aim = inputManager.ReadAction(TankMapName, AimActionName).Vector2;
        aim = new Vector2(aim.X, -aim.Y);
        float aimLenSq = aim.LengthSquared();
        if (aimLenSq > 0.0001f)
        {
            return Vector2.Normalize(aim);
        }

        if (TryGetMouseAimDirection(camera, tankPos, out Vector2 mouseAim))
        {
            return mouseAim;
        }

        return previousAimDirection;
    }

    private static void DrawTankTurret(MeshHandle turretMesh, Vector3 tankPos, float turretRotation)
    {
        const float turretY = 0.65f;

        var pos = new Vector3(tankPos.X, turretY, tankPos.Z);
        Derp.DrawMesh(turretMesh, pos, turretRotation, 1f, 70, 220, 120, 255);
    }

    private static bool TryGetMouseAimDirection(Camera3D camera, Vector3 tankPos, out Vector2 aimDirection)
    {
        int targetWidth = Derp.GetFramebufferWidth();
        int targetHeight = Derp.GetFramebufferHeight();
        float aspect = targetHeight > 0 ? targetWidth / (float)targetHeight : 1f;

        Matrix4x4 viewProjection = camera.GetViewProjection(aspect);

        Vector2 mouse = Derp.GetMousePosition();
        float contentScale = Derp.GetContentScale();
        Vector2 mouseFramebuffer = mouse * contentScale;

        if (!Projection3D.TryScreenToWorldRay(viewProjection, targetWidth, targetHeight, mouseFramebuffer, out Ray3D ray))
        {
            aimDirection = default;
            return false;
        }

        float denom = ray.Direction.Y;
        if (MathF.Abs(denom) < 1e-6f)
        {
            aimDirection = default;
            return false;
        }

        float t = -ray.Origin.Y / denom;
        if (t <= 0f || !float.IsFinite(t))
        {
            aimDirection = default;
            return false;
        }

        Vector3 hit = ray.Origin + ray.Direction * t;
        var dir = new Vector2(hit.X - tankPos.X, hit.Z - tankPos.Z);
        float lenSq = dir.LengthSquared();
        if (lenSq <= 1e-6f)
        {
            aimDirection = default;
            return false;
        }

        aimDirection = Vector2.Normalize(dir);
        return true;
    }

    private static void RenderDensityGridQuads(MeshHandle quadMesh, SimEcsWorld simWorld)
    {
        int[] density = simWorld.SeparationDensity;
        int max = 1;
        for (int i = 0; i < density.Length; i++)
        {
            int d = density[i];
            if (d > max)
            {
                max = d;
            }
        }

        const float y = -0.44f;

        for (int cellY = 0; cellY < HordeSeparationGrid.GridSize; cellY++)
        {
            int cellRowStart = cellY * HordeSeparationGrid.GridSize;
            int z0Int = HordeSeparationGrid.GetCellWorldY(cellY);
            float centerZ = z0Int + HordeSeparationGrid.CellSize * 0.5f;

            for (int cellX = 0; cellX < HordeSeparationGrid.GridSize; cellX++)
            {
                int d = density[cellRowStart + cellX];
                if (d <= 0)
                {
                    continue;
                }

                int x0Int = HordeSeparationGrid.GetCellWorldX(cellX);
                float centerX = x0Int + HordeSeparationGrid.CellSize * 0.5f;

                float t = d / (float)max;
                byte r = (byte)(255f * t);
                byte g = (byte)(255f * (1f - t));
                byte b = 32;
                byte a = 120;

                var transform =
                    Matrix4x4.CreateScale(HordeSeparationGrid.CellSize, 1f, HordeSeparationGrid.CellSize) *
                    Matrix4x4.CreateTranslation(centerX, y, centerZ);

                Derp.DrawMesh(quadMesh, transform, r, g, b, a);
            }
        }
    }

    private static MeshHandle CreateDensityQuadMesh()
    {
        // Unit quad in XZ plane (y=0), normal +Y so it renders as a ground overlay without any X-rotation.
        Span<Vertex3D> vertices = stackalloc Vertex3D[4]
        {
            new(new(-0.5f, 0f, -0.5f), new(0f, 1f, 0f), new(0f, 1f)), // bottom-left
            new(new( 0.5f, 0f, -0.5f), new(0f, 1f, 0f), new(1f, 1f)), // bottom-right
            new(new( 0.5f, 0f,  0.5f), new(0f, 1f, 0f), new(1f, 0f)), // top-right
            new(new(-0.5f, 0f,  0.5f), new(0f, 1f, 0f), new(0f, 0f)), // top-left
        };

        Span<uint> indices = stackalloc uint[6]
        {
            // CCW winding when viewed from +Y (so the quad faces the top-down camera).
            0, 2, 1,
            2, 0, 3
        };

        return Derp.RegisterMesh(vertices, indices);
    }

    private static MeshHandle CreateTurretMesh()
    {
        const float turretHeight = 0.5f;
        const float turretWidth = 0.45f;
        const float turretLength = 1.7f;
        return CreateBoxMesh(turretWidth, turretHeight, turretLength);
    }

    private static MeshHandle CreateBoxMesh(float width, float height, float length)
    {
        float hx = width * 0.5f;
        float hy = height * 0.5f;
        float hz = length * 0.5f;

        // 24 vertices (4 per face) so each face has a distinct normal.
        Span<Vertex3D> vertices = stackalloc Vertex3D[24]
        {
            // Front (+Z)
            new(new(-hx, -hy,  hz), new(0f, 0f, 1f), new(0f, 1f)),
            new(new( hx, -hy,  hz), new(0f, 0f, 1f), new(1f, 1f)),
            new(new( hx,  hy,  hz), new(0f, 0f, 1f), new(1f, 0f)),
            new(new(-hx,  hy,  hz), new(0f, 0f, 1f), new(0f, 0f)),

            // Back (-Z)
            new(new( hx, -hy, -hz), new(0f, 0f, -1f), new(0f, 1f)),
            new(new(-hx, -hy, -hz), new(0f, 0f, -1f), new(1f, 1f)),
            new(new(-hx,  hy, -hz), new(0f, 0f, -1f), new(1f, 0f)),
            new(new( hx,  hy, -hz), new(0f, 0f, -1f), new(0f, 0f)),

            // Right (+X)
            new(new( hx, -hy,  hz), new(1f, 0f, 0f), new(0f, 1f)),
            new(new( hx, -hy, -hz), new(1f, 0f, 0f), new(1f, 1f)),
            new(new( hx,  hy, -hz), new(1f, 0f, 0f), new(1f, 0f)),
            new(new( hx,  hy,  hz), new(1f, 0f, 0f), new(0f, 0f)),

            // Left (-X)
            new(new(-hx, -hy, -hz), new(-1f, 0f, 0f), new(0f, 1f)),
            new(new(-hx, -hy,  hz), new(-1f, 0f, 0f), new(1f, 1f)),
            new(new(-hx,  hy,  hz), new(-1f, 0f, 0f), new(1f, 0f)),
            new(new(-hx,  hy, -hz), new(-1f, 0f, 0f), new(0f, 0f)),

            // Top (+Y)
            new(new(-hx,  hy,  hz), new(0f, 1f, 0f), new(0f, 1f)),
            new(new( hx,  hy,  hz), new(0f, 1f, 0f), new(1f, 1f)),
            new(new( hx,  hy, -hz), new(0f, 1f, 0f), new(1f, 0f)),
            new(new(-hx,  hy, -hz), new(0f, 1f, 0f), new(0f, 0f)),

            // Bottom (-Y)
            new(new(-hx, -hy, -hz), new(0f, -1f, 0f), new(0f, 1f)),
            new(new( hx, -hy, -hz), new(0f, -1f, 0f), new(1f, 1f)),
            new(new( hx, -hy,  hz), new(0f, -1f, 0f), new(1f, 0f)),
            new(new(-hx, -hy,  hz), new(0f, -1f, 0f), new(0f, 0f)),
        };

        Span<uint> indices = stackalloc uint[36]
        {
            0, 1, 2, 2, 3, 0,       // front
            4, 5, 6, 6, 7, 4,       // back
            8, 9, 10, 10, 11, 8,    // right
            12, 13, 14, 14, 15, 12, // left
            16, 17, 18, 18, 19, 16, // top
            20, 21, 22, 22, 23, 20, // bottom
        };

        return Derp.RegisterMesh(vertices, indices);
    }
}
