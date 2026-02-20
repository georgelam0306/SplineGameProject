using System;
using System.Diagnostics;
using System.Numerics;
using Derp.UI;
using DerpLib.AssetPipeline;
using DerpLib.Rendering;
using DerpLib.Text;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using DerpLib.ImGui;
using Silk.NET.Input;
using DerpEngine = DerpLib.Derp;

namespace Derp.UI.Example;

public static class Program
{
    public static void Main(string[] args)
    {
        string uiAssetUrl = "ui/TestAsset.bdui";
        int prefabIndex = 2;
        bool probe = false;
        bool probeHover = false;
        bool debugUi = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--probe", StringComparison.Ordinal))
            {
                probe = true;
                continue;
            }
            if (string.Equals(arg, "--probe-hover", StringComparison.Ordinal))
            {
                probeHover = true;
                continue;
            }
            if (string.Equals(arg, "--debug-ui", StringComparison.Ordinal))
            {
                debugUi = true;
                continue;
            }
            if (string.Equals(arg, "--ui", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                uiAssetUrl = args[++i];
                continue;
            }

            if (string.Equals(arg, "--prefab", StringComparison.Ordinal) && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed))
            {
                prefabIndex = parsed;
                i++;
            }
        }

        if (probe)
        {
            UiRuntimeProbe.Run(uiAssetUrl);
            return;
        }

        if (probeHover)
        {
            UiRuntimeProbe.RunHover(uiAssetUrl);
            return;
        }

        DerpEngine.InitWindow(1280, 720, "Derp.UI.Example");
        DerpEngine.InitSdf();

        Font font = DerpEngine.LoadFont("arial");
        Font iconFont = DerpEngine.LoadFont("fa-solid-900");
        DerpEngine.SetSdfFontAtlas(font.Atlas);
        DerpEngine.SetSdfSecondaryFontAtlas(iconFont.Atlas);

        ContentManager content = DerpEngine.Engine.Content;
        UiRuntimeContent.Register(content);

        if (debugUi)
        {
            Im.Initialize(enableMultiViewport: false);
            Im.SetFonts(font, iconFont);
        }

        var store = new EntityStore();
        Entity uiEntity = store.CreateEntity();
        uiEntity.AddComponent(new UiCanvasComponent
        {
            AssetUrl = uiAssetUrl,
            PrefabIndex = prefabIndex,
            PrefabStableId = 0,
            CanvasWidth = 0,
            CanvasHeight = 0
        });

        var frame = new UiCanvasFrameContext();
        var updateRoot = new SystemRoot(store)
        {
            new UiCanvasUpdateSystem(frame, content, font),
        };

        var renderRoot = new SystemRoot(store)
        {
            new UiCanvasRenderSystem(),
        };

        var camera2D = new Camera2D(Vector2.Zero, Vector2.Zero, 0f, 1f);

        long lastTimestamp = Stopwatch.GetTimestamp();

        while (!DerpEngine.WindowShouldClose())
        {
            DerpEngine.PollEvents();

            if (!DerpEngine.BeginDrawing())
            {
                continue;
            }

            long now = Stopwatch.GetTimestamp();
            double dtSeconds = (now - lastTimestamp) / (double)Stopwatch.Frequency;
            lastTimestamp = now;

            if (!double.IsFinite(dtSeconds) || dtSeconds < 0)
            {
                dtSeconds = 0;
            }

            float deltaSeconds = (float)Math.Clamp(dtSeconds, 0, 0.25);
            uint deltaUs = (uint)Math.Clamp((int)MathF.Round(deltaSeconds * 1_000_000f), 0, int.MaxValue);

            int windowWidth = DerpEngine.GetScreenWidth();
            int windowHeight = DerpEngine.GetScreenHeight();

            frame.DeltaMicroseconds = deltaUs;
            frame.WindowWidth = windowWidth;
            frame.WindowHeight = windowHeight;
            frame.MousePosition = DerpEngine.GetMousePosition();
            frame.PrimaryDown = DerpEngine.IsMouseButtonDown(MouseButton.Left);
            frame.WheelDelta = DerpEngine.GetScrollDelta();

            var tick = new UpdateTick(deltaSeconds, 0);

            updateRoot.Update(tick);

            if (debugUi)
            {
                DerpEngine.SdfBuffer.Reset();
                Im.Begin(deltaSeconds);
                ref readonly var canvas = ref uiEntity.GetComponent<UiCanvasComponent>();
                UiRuntimeDebugOverlay.Draw(in canvas, frame);
                Im.End();
                DerpEngine.DispatchSdfToTexture();
            }

            DerpEngine.BeginCamera2D(camera2D);

            // Background
            {
                var bgTransform =
                    Matrix4x4.CreateScale(windowWidth, windowHeight, 1f) *
                    Matrix4x4.CreateTranslation(windowWidth * 0.5f, windowHeight * 0.5f, 0f);
                DerpEngine.DrawTextureTransform(Texture.White, bgTransform, 15, 15, 20, 255);
            }

            renderRoot.Update(tick);

            if (debugUi)
            {
                var overlayTexture = DerpEngine.SdfOutputTexture;
                var overlayTransform =
                    Matrix4x4.CreateScale(windowWidth, -windowHeight, 1f) *
                    Matrix4x4.CreateTranslation(windowWidth * 0.5f, windowHeight * 0.5f, 0f);
                DerpEngine.DrawTextureTransform(overlayTexture, overlayTransform, 255, 255, 255, 255);
            }
            DerpEngine.EndCamera2D();

            DerpEngine.EndDrawing();
        }

        // Cleanup owned resources (stored in ECS components).
        foreach (var entity in store.Query<UiCanvasComponent>().Entities)
        {
            ref var canvas = ref entity.GetComponent<UiCanvasComponent>();
            canvas.Surface?.Dispose();
            canvas.Surface = null;
            canvas.Runtime = null;
        }

        DerpEngine.CloseWindow();
    }
}
