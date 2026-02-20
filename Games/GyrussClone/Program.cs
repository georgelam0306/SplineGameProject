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
using GyrussClone.Core;
using GyrussClone.Simulation.Ecs;
using FixedMath;

namespace GyrussClone;

public static class Program
{
    private static readonly StringHandle GyrussMapName = "Gyruss";
    private static readonly StringHandle MoveActionName = "Move";
    private static readonly StringHandle FireActionName = "Fire";

    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== GyrussClone ===");

        // Initialize window + engine
        Derp.InitWindow(1280, 720, "Gyruss Clone");
        Derp.InitSdf();
        Derp.InitProfiler();

        // Load font
        var font = Derp.LoadFont("arial");
        Derp.SetSdfFontAtlas(font.Atlas);

        // Initialize instanced meshes (none for 2D SDF game, but required)
        Derp.InitializeInstancedMeshes();

        // Debug UI
        Im.Initialize(enableMultiViewport: false);
        Im.SetFont(font);

        // Initialize input
        var inputManager = new DerpInputManager();
        var inputConfigPath = Path.Combine(AppContext.BaseDirectory, "Resources", "input-gyruss.json");
        InputConfigLoader.LoadFromFile(inputManager, inputConfigPath);
        inputManager.PushContext("Gyruss");

        // DI composition — creates GameManager with GameComposition.Factory
        using var app = new AppComposition();
        var gameManager = app.GameManager;
        gameManager.StartGame(sessionSeed: 42);

        // Game state
        float dt = 0.016f;
        bool gameOverAcknowledged = false;

        while (!Derp.WindowShouldClose())
        {
            Derp.PollEvents();

            var simWorld = gameManager.World;

            // Update input
            inputManager.Update(dt);

            // Read input into world state
            if (simWorld.PlayerAlive)
            {
                var moveValue = inputManager.ReadAction(GyrussMapName, MoveActionName);
                simWorld.PlayerAngularInput = Fixed64.FromFloat(moveValue.Vector2.X);
                simWorld.FireRequested = inputManager.WasPerformed(GyrussMapName, FireActionName)
                                      || inputManager.IsActive(GyrussMapName, FireActionName);
            }
            else
            {
                simWorld.PlayerAngularInput = Fixed64.Zero;
                simWorld.FireRequested = false;

                // Restart on fire press after game over
                if (inputManager.WasPerformed(GyrussMapName, FireActionName))
                {
                    if (gameOverAcknowledged)
                    {
                        gameManager.StartGame(sessionSeed: simWorld.SessionSeed + 1);
                        gameOverAcknowledged = false;
                        continue; // Skip rest of frame — new world is ready next iteration
                    }
                    else
                    {
                        gameOverAcknowledged = true;
                    }
                }
            }

            // Simulation tick
            simWorld.DeltaTime = Fixed64.FromFloat(dt);
            simWorld.CurrentFrame++;
            gameManager.Pipeline.RunFrame(simWorld);

            if (!Derp.BeginDrawing())
                continue;

            // SDF rendering (SDF works in framebuffer pixel coords, not logical screen coords)
            Derp.SdfBuffer.Reset();

            int screenWidth = Derp.GetScreenWidth();
            int screenHeight = Derp.GetScreenHeight();
            float scale = Derp.GetContentScale();
            int fbWidth = Derp.GetFramebufferWidth();
            int fbHeight = Derp.GetFramebufferHeight();
            float cx = fbWidth * 0.5f;
            float cy = fbHeight * 0.5f;
            float playerRadiusF = simWorld.PlayerRadius.ToFloat() * scale;

            // Draw playfield
            GyrussRenderer.DrawPlayfield(Derp.SdfBuffer, cx, cy, playerRadiusF, scale);

            // Draw enemies
            GyrussRenderer.DrawEnemies(Derp.SdfBuffer, simWorld, cx, cy, scale);

            // Draw bullets
            GyrussRenderer.DrawBullets(Derp.SdfBuffer, simWorld, cx, cy, scale);

            // Draw player
            if (simWorld.PlayerAlive)
            {
                GyrussRenderer.DrawPlayer(Derp.SdfBuffer, cx, cy, simWorld.PlayerAngle.ToFloat(), playerRadiusF, scale);
            }

            // HUD (all positions and sizes scaled for framebuffer coords)
            Derp.DrawText(font, $"Score: {simWorld.Score}", 10 * scale, 10 * scale, 24f * scale);
            Derp.DrawText(font, $"Lives: {simWorld.Lives}", 10 * scale, 40 * scale, 20f * scale);
            Derp.DrawText(font, $"Wave: {simWorld.CurrentWave}", 10 * scale, 65 * scale, 18f * scale);
            Derp.DrawText(font, $"Enemies: {simWorld.Enemy.Count}  Bullets: {simWorld.PlayerBullet.Count}", 10 * scale, 88 * scale, 14f * scale, 0f, 0.6f, 0.6f, 0.6f);
            Derp.DrawText(font, "A/D: Orbit  Space: Fire", 10 * scale, fbHeight - 25 * scale, 14f * scale, 0f, 0.5f, 0.5f, 0.5f);

            // Game Over overlay (centered text)
            if (!simWorld.PlayerAlive)
            {
                const string gameOverText = "GAME OVER";
                const string restartText = "Press Space to Restart";
                float goFontSize = 36f * scale;
                float restartFontSize = 18f * scale;

                var goSize = font.MeasureText(gameOverText, goFontSize);
                var restartSize = font.MeasureText(restartText, restartFontSize);

                Derp.DrawText(font, gameOverText, cx - goSize.X * 0.5f, cy - 20 * scale, goFontSize, 0f, 1.0f, 0.3f, 0.3f);
                Derp.DrawText(font, restartText, cx - restartSize.X * 0.5f, cy + 25 * scale, restartFontSize, 0f, 0.7f, 0.7f, 0.7f);
            }

            // Dispatch SDF compute before starting the 2D render pass
            Derp.DispatchSdfToTexture();

            // 2D camera pass (BeginColorPass uses LOAD_OP_LOAD, so we must draw an opaque
            // background quad ourselves — ClearBackground only works with 3D camera passes)
            var camera2D = new Camera2D(Vector2.Zero, Vector2.Zero, 0f, 1f);
            Derp.BeginCamera2D(camera2D);

            // Opaque background quad (dark space)
            var bgTransform =
                Matrix4x4.CreateScale(screenWidth, screenHeight, 1f) *
                Matrix4x4.CreateTranslation(screenWidth * 0.5f, screenHeight * 0.5f, 0f);
            Derp.DrawTextureTransform(Texture.White, bgTransform, 5, 5, 20, 255);

            // Composite SDF on top
            var sdfTex = Derp.SdfOutputTexture;
            var sdfTransform =
                Matrix4x4.CreateScale(screenWidth, -screenHeight, 1f) *
                Matrix4x4.CreateTranslation(screenWidth * 0.5f, screenHeight * 0.5f, 0f);
            Derp.DrawTextureTransform(sdfTex, sdfTransform, 255, 255, 255, 255);

            Derp.EndCamera2D();

            Derp.EndDrawing();
        }

        Derp.CloseWindow();
        Log.Information("=== GyrussClone Closed ===");
    }
}
