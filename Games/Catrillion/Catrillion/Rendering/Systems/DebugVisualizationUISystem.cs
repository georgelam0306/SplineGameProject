using Friflo.Engine.ECS.Systems;
using Raylib_cs;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders debug visualization UI text in screen space (after camera transform ends).
/// Reads state from DebugVisualizationSystem.
/// </summary>
public sealed class DebugVisualizationUISystem : BaseSystem
{
    private readonly DebugVisualizationSystem _debugVizSystem;

    public DebugVisualizationUISystem(DebugVisualizationSystem debugVizSystem)
    {
        _debugVizSystem = debugVizSystem;
    }

    protected override void OnUpdateGroup()
    {
        if (!_debugVizSystem.IsEnabled)
        {
            return;
        }

        int screenHeight = Raylib.GetScreenHeight();
        int y = screenHeight - 25;

        // Draw mode indicator
        string modeText = $"Debug: {_debugVizSystem.CurrentModeName} (F3 to cycle)";
        Raylib.DrawText(modeText, 10, y, 16, Color.White);

        // Draw additional stats based on mode
        string modeName = _debugVizSystem.CurrentModeName;

        if (modeName == "Combined")
        {
            y -= 20;
            string statsText = $"Zones: {_debugVizSystem.ZoneCount} | Portals: {_debugVizSystem.PortalCount} | Seeds: {_debugVizSystem.SeedCount} | Flow Arrows: {_debugVizSystem.FlowArrowCount}";
            Raylib.DrawText(statsText, 10, y, 14, Color.Yellow);
        }
        else if (modeName == "FlowField")
        {
            y -= 20;
            string statsText = $"Zones: {_debugVizSystem.ZoneCount} | Portals: {_debugVizSystem.PortalCount} | Flow Arrows: {_debugVizSystem.FlowArrowCount}";
            Raylib.DrawText(statsText, 10, y, 14, Color.Yellow);

            y -= 18;
            if (_debugVizSystem.HasDebugDestination)
            {
                string destText = $"Dest: ({_debugVizSystem.DebugDestTileX}, {_debugVizSystem.DebugDestTileY}) | A* Path: {_debugVizSystem.ZonePathInfo}";
                Raylib.DrawText(destText, 10, y, 14, new Color(255, 100, 100, 255));
            }
            else
            {
                Raylib.DrawText("Middle-click to set destination, Tab to cycle moving units", 10, y, 14, Color.Gray);
            }

            y -= 16;
            Raylib.DrawText("Green arrows = flow direction | Red line = A* zone path | Yellow = destination", 10, y, 12, Color.Gray);
        }
        else if (modeName == "NoiseGrid")
        {
            y -= 20;
            string statsText = $"Active Noise Cells: {_debugVizSystem.NoiseGridCellCount}";
            Raylib.DrawText(statsText, 10, y, 14, Color.Yellow);
        }
        else if (modeName == "ThreatGrid")
        {
            y -= 20;
            string statsText = $"Active Threat Cells: {_debugVizSystem.ThreatGridCellCount} | Max: {_debugVizSystem.ThreatGridMaxValue} | Peak Memory Cells: {_debugVizSystem.PeakThreatCellCount}";
            Raylib.DrawText(statsText, 10, y, 14, Color.Yellow);

            // Add legend
            y -= 18;
            Raylib.DrawText("Blue: <20 (low) | Cyan: 20-50 | Yellow: 50-100 | Red: >=100 (chase) | Purple corners: peak memory", 10, y, 12, Color.Gray);
        }
    }
}
