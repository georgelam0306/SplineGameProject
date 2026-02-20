using System.Diagnostics;
using Serilog;
using DerpLib.ImGui;
using DerpLib.ImGui.Layout;
using DerpLib.Text;

namespace DerpLib.Examples;

/// <summary>
/// Scene selector menu using ImGUI. Shows buttons to launch different demo scenes.
/// </summary>
public static class SceneSelector
{
    private static readonly (string Name, string Key, Action Run)[] Scenes =
    {
        ("3D Demo", "3d", Demo3D.Run),
        ("SDF Demo", "sdf", SdfDemo.Run),
        ("Warp Stack Demo", "warp", WarpStackDemo.Run),
        ("Trim + Dash Demo", "trim", TrimDashDemo.Run),
        ("SDF Stress Test", "stress", SdfStressTest.Run),
        ("ImGUI Input", "imgui", ImGuiInputDemo.Run),
        ("Font Demo", "font", FontDemo.Run),
        ("Masking Demo", "mask", MaskingDemo.Run),
        ("Widget Zoo", "widgets", WidgetZoo.Run),
        ("Scroll Widgets Demo", "scroll", ScrollWidgetsDemo.Run),
    };

    /// <summary>
    /// Run a scene by its key (for CLI dispatch).
    /// </summary>
    public static bool TryRunScene(string key)
    {
        foreach (var scene in Scenes)
        {
            if (scene.Key == key)
            {
                scene.Run();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Show the scene selector UI.
    /// </summary>
    public static void Run()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== Scene Selector ===");

        Derp.InitWindow(400, 500, "Scene Selector");
        Derp.InitSdf();

        // Load font for text rendering
        Font font = Derp.LoadFont("arial");
        Derp.SetSdfFontAtlas(font.Atlas);

        Im.Initialize(enableMultiViewport: false);
        Im.SetFont(font);

        string? selectedSceneKey = null;

        while (!Derp.WindowShouldClose())
        {
            Derp.PollEvents();

            if (!Derp.BeginDrawing())
                continue;

            Derp.SdfBuffer.Reset();
            Im.Begin(0.016f);

            // Draw scene selector window
            if (Im.BeginWindow("Select Scene", 20, 20, 360, 450))
            {
                Im.LabelText("Choose a demo to run:");
                ImLayout.Space(15);

                foreach (var scene in Scenes)
                {
                    if (Im.Button(scene.Name, 340))
                    {
                        selectedSceneKey = scene.Key;
                        Log.Information("Selected: {Scene}", scene.Name);
                    }
                    ImLayout.Space(5);
                }
            }
            Im.EndWindow();

            Im.End();
            Derp.RenderSdf();
            Derp.EndDrawing();

            // If a scene was selected, launch it as a new process
            if (selectedSceneKey != null)
            {
                break;
            }
        }

        Derp.CloseWindow();

        // Launch the selected scene as a new process (avoids GLFW termination issues)
        if (selectedSceneKey != null)
        {
            Log.Information("Launching scene: {Key}", selectedSceneKey);

            // Get the current assembly's DLL path and run it with dotnet
            var dllPath = typeof(SceneSelector).Assembly.Location;
            if (!string.IsNullOrEmpty(dllPath))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{dllPath}\" {selectedSceneKey}",
                    UseShellExecute = false,
                };
                Process.Start(startInfo);
            }
        }

        Log.Information("=== Scene Selector Closed ===");
    }
}
