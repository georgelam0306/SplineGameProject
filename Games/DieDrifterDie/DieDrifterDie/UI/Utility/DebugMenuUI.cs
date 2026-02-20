using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using DieDrifterDie.GameApp.Config;
using DieDrifterDie.GameData.Schemas;
using DieDrifterDie.Presentation.Rendering.Components;
using DieDrifterDie.Simulation;
using DieDrifterDie.Simulation.Components;
using Core;
using SimTable;

namespace DieDrifterDie.Presentation.UI;

/// <summary>
/// In-game debug menu for testing win/lose conditions.
/// Toggle with F4 key.
/// </summary>
public sealed class DebugMenuUI : BaseSystem
{
    private readonly GameSimulation _gameSimulation;
    private bool _isVisible;

    // Layout constants
    private const int PanelX = 10;
    private const int PanelY = 10;
    private const int PanelWidth = 180;
    private const int ButtonWidth = 160;
    private const int ButtonHeight = 30;
    private const int ButtonSpacing = 5;
    private const int HeaderHeight = 30;

    public DebugMenuUI(GameSimulation gameSimulation)
    {
        _gameSimulation = gameSimulation;
    }

    protected override void OnUpdateGroup()
    {
        // Toggle visibility with F4
        if (Raylib.IsKeyPressed(KeyboardKey.F4))
        {
            _isVisible = !_isVisible;
        }

        if (!_isVisible) return;

        // Calculate panel height based on button count
        int buttonCount = 4;
        int panelHeight = HeaderHeight + (ButtonHeight + ButtonSpacing) * buttonCount + ButtonSpacing;

        // Draw panel background
        ImmediateModeUI.DrawPanel(PanelX, PanelY, PanelWidth, panelHeight,
            ImmediateModeUI.PanelColor, ImmediateModeUI.PanelBorderColor);

        // Header
        Raylib.DrawText("DEBUG (F4)", PanelX + 10, PanelY + 8, 16, ImmediateModeUI.TextColor);

        // Buttons
        int buttonX = PanelX + (PanelWidth - ButtonWidth) / 2;
        int buttonY = PanelY + HeaderHeight;

        // Button 1: Trigger Victory
        if (ImmediateModeUI.Button(buttonX, buttonY, ButtonWidth, ButtonHeight, "Trigger Victory"))
        {

        }
        buttonY += ButtonHeight + ButtonSpacing;

        // Button 2: Trigger Defeat
        if (ImmediateModeUI.Button(buttonX, buttonY, ButtonWidth, ButtonHeight, "Trigger Defeat"))
        {

        }

    }

}
