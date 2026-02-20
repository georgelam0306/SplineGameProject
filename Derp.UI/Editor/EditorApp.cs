using System.Numerics;
using DerpEngine = DerpLib.Derp;
using DerpLib.ImGui;
using DerpLib.ImGui.Docking;
using DerpLib.ImGui.Windows;
using DerpLib.ImGui.Widgets;
using DerpLib.Rendering;
using DerpLib.Text;

namespace Derp.UI;

internal sealed class EditorApp
{
    private readonly UiWorld _world;
    private readonly UiWorkspace _workspace;
    private readonly CanvasSurface _canvasSurface;

    public EditorApp(UiWorld world, UiWorkspace workspace, CanvasSurface canvasSurface)
    {
        _world = world;
        _workspace = workspace;
        _canvasSurface = canvasSurface;
    }

    public void Run()
    {
        DerpEngine.InitWindow(1400, 900, "Derp.UI");
        DerpEngine.InitSdf();

        Font font = DerpEngine.LoadFont("arial");
        Font iconFont = DerpEngine.LoadFont("fa-solid-900");
        DerpEngine.SetSdfFontAtlas(font.Atlas);
        DerpEngine.SetSdfSecondaryFontAtlas(iconFont.Atlas);
        _canvasSurface.SetFontAtlas(font.Atlas);
        _workspace.SetCanvasSurface(_canvasSurface);

        Im.Initialize(enableMultiViewport: true);
        Im.SetFonts(font, iconFont);
        Im.SetMainDockTopInset(ImMainMenuBar.MenuBarHeight + Toolbar.BarHeight);
        SetupDefaultDockLayout();

        const float deltaTime = 1f / 60f;

        while (!DerpEngine.WindowShouldClose())
        {
            DerpEngine.PollEvents();

            if (!DerpEngine.BeginDrawing())
            {
                continue;
            }

            _workspace.BeginFrame();
            DerpEngine.SdfBuffer.Reset();
            Im.Begin(deltaTime);

            DrawWorkspace();

            Im.End();
            _canvasSurface.DispatchToTexture();
            DerpEngine.DispatchSdfToTexture();
            CompositePrimaryViewport();
            Im.UpdateSecondaryViewports(deltaTime);
            DerpEngine.EndDrawing();
        }

        _canvasSurface.Dispose();
        DerpEngine.CloseWindow();
    }

    private static void SetupDefaultDockLayout()
    {
        var dockController = Im.Context.DockController;
        if (dockController.MainLayout.Root != null)
        {
            return;
        }

        // Pre-create windows so docking can mark them as docked on the first frame.
        // Coordinates are screen-space; these will be overridden by the dock layout once docked.
        Im.WindowManager.GetOrCreateWindow("Layers", 0, 0, 320, 700);
        Im.WindowManager.GetOrCreateWindow("Canvas", 0, 0, 700, 700, ImWindowFlags.NoBackground | ImWindowFlags.NoScrollbar | ImWindowFlags.NoMove);
        Im.WindowManager.GetOrCreateWindow("Inspector", 0, 0, 300, 700);
        Im.WindowManager.GetOrCreateWindow("Variables", 0, 0, 300, 700, ImWindowFlags.NoBackground);
        Im.WindowManager.GetOrCreateWindow("Animations", 0, 0, 1000, 280);

        int layersWindowId = ImWindow.GetIdForTitle("Layers");
        int canvasWindowId = ImWindow.GetIdForTitle("Canvas");
        int inspectorWindowId = ImWindow.GetIdForTitle("Inspector");
        int variablesWindowId = ImWindow.GetIdForTitle("Variables");
        int animationsWindowId = ImWindow.GetIdForTitle("Animations");

        var layersLeaf = new ImDockLeaf();
        layersLeaf.WindowIds.Add(layersWindowId);
        layersLeaf.ActiveTabIndex = 0;

        var canvasLeaf = new ImDockLeaf();
        canvasLeaf.WindowIds.Add(canvasWindowId);
        canvasLeaf.ActiveTabIndex = 0;

        var inspectorLeaf = new ImDockLeaf();
        inspectorLeaf.WindowIds.Add(inspectorWindowId);
        inspectorLeaf.WindowIds.Add(variablesWindowId);
        inspectorLeaf.ActiveTabIndex = 0;

        var animationsLeaf = new ImDockLeaf();
        animationsLeaf.WindowIds.Add(animationsWindowId);
        animationsLeaf.ActiveTabIndex = 0;

        var canvasInspectorSplit = new ImDockSplit(canvasLeaf, inspectorLeaf, ImSplitDirection.Horizontal, ratio: 0.72f);
        var topMainSplit = new ImDockSplit(layersLeaf, canvasInspectorSplit, ImSplitDirection.Horizontal, ratio: 0.24f);
        var mainWithAnimations = new ImDockSplit(topMainSplit, animationsLeaf, ImSplitDirection.Vertical, ratio: 0.70f);

        dockController.MainLayout.Root = mainWithAnimations;
    }

    private void DrawWorkspace()
    {
        MainMenuBar.Draw(_workspace);
        _workspace.DrawToolbarWindow();

        if (Im.BeginWindow("Layers", 20, 160, 320, 700))
        {
            _workspace.DrawLayersPanel();
        }
        Im.EndWindow();

        if (Im.BeginWindow("Canvas", 360, 160, 700, 700, ImWindowFlags.NoBackground | ImWindowFlags.NoScrollbar | ImWindowFlags.NoMove))
        {
            _workspace.DrawCanvas();
        }
        Im.EndWindow();

        if (Im.BeginWindow("Animations", 20, 740, 1360, 240))
        {
            _workspace.DrawAnimationEditorWindow();
        }
        Im.EndWindow();

        if (Im.BeginWindow("Inspector", 1080, 160, 300, 700, ImWindowFlags.NoBackground))
        {
            _workspace.DrawInspectorPanel();
        }
        Im.EndWindow();

        if (Im.BeginWindow("Variables", 1080, 160, 300, 700, ImWindowFlags.NoBackground))
        {
            _workspace.DrawVariablesPanel();
        }
        Im.EndWindow();

        _workspace.DrawToasts();
    }

    private void CompositePrimaryViewport()
    {
        int screenWidth = DerpEngine.GetScreenWidth();
        int screenHeight = DerpEngine.GetScreenHeight();
        var camera2D = new Camera2D(Vector2.Zero, Vector2.Zero, 0f, 1f);
        DerpEngine.BeginCamera2D(camera2D);

        // We don't run a 3D pass, and the 2D color pass uses LoadOp.Load. Draw a full-screen background to
        // avoid showing swapchain garbage in transparent UI areas.
        {
            var bgTransform =
                Matrix4x4.CreateScale(screenWidth, screenHeight, 1f) *
                Matrix4x4.CreateTranslation(screenWidth * 0.5f, screenHeight * 0.5f, 0f);
            DerpEngine.DrawTextureTransform(Texture.White, bgTransform, 15, 15, 20, 255);
        }

        if (_workspace.TryGetCanvasComposite(out var canvasRect, out var canvasTexture))
        {
            var transform =
                Matrix4x4.CreateScale(canvasRect.Width, -canvasRect.Height, 1f) *
                Matrix4x4.CreateTranslation(canvasRect.X + canvasRect.Width * 0.5f, canvasRect.Y + canvasRect.Height * 0.5f, 0f);
            DerpEngine.DrawTextureTransform(canvasTexture, transform, 255, 255, 255, 255);
        }

        {
            var uiTexture = DerpEngine.SdfOutputTexture;
            var transform =
                Matrix4x4.CreateScale(screenWidth, -screenHeight, 1f) *
                Matrix4x4.CreateTranslation(screenWidth * 0.5f, screenHeight * 0.5f, 0f);
            DerpEngine.DrawTextureTransform(uiTexture, transform, 255, 255, 255, 255);
        }

        DerpEngine.EndCamera2D();
    }
}
