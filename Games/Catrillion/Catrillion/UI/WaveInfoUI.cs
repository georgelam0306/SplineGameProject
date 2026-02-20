using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Core;
using Catrillion.GameData.Schemas;
using Catrillion.Simulation;
using Catrillion.Simulation.Components;

namespace Catrillion.UI;

/// <summary>
/// Displays wave system information:
/// - Current day counter with hours (top-left)
/// - Wave warning with countdown and direction (centered, when active)
/// - Horde/mini wave indicators
/// </summary>
public sealed class WaveInfoUI : BaseSystem
{
    private readonly SimWorld _simWorld;
    private readonly int _framesPerDay;

    // Colors for wave warnings
    private static readonly Color WarningBgColor = new(80, 20, 20, 220);
    private static readonly Color WarningTextColor = new(255, 200, 100, 255);
    private static readonly Color DayTextColor = new(200, 200, 220, 255);
    private static readonly Color WaveActiveColor = new(255, 80, 80, 255);

    // Direction names for display
    private static readonly string[] DirectionNames = { "NORTH", "EAST", "SOUTH", "WEST", "ALL DIRECTIONS" };
    private static readonly string[] DirectionArrows = { "^", ">", "v", "<", "*" };

    public WaveInfoUI(SimWorld simWorld, GameDataManager<GameDocDb> gameData)
    {
        _simWorld = simWorld;
        ref readonly var waveConfig = ref gameData.Db.WaveConfigData.FindById(0);
        _framesPerDay = waveConfig.FramesPerDay;
    }

    protected override void OnUpdateGroup()
    {
        if (!_simWorld.IsPlaying()) return;
        if (!_simWorld.WaveStateRows.TryGetRow(0, out var state)) return;

        int screenWidth = Raylib.GetScreenWidth();

        // Calculate hour of day (0-23)
        int hour = (state.FramesSinceDayStart * 24) / _framesPerDay;
        hour = System.Math.Clamp(hour, 0, 23);

        // Draw day counter with hours (top-left)
        DrawDayCounter(state.CurrentDay, hour, state.CurrentHordeWave, state.CurrentMiniWave);

        // Draw wave warning or active horde indicator (centered)
        if (state.Flags.HasFlag(WaveStateFlags.WarningActive))
        {
            int secondsRemaining = (state.WarningCountdownFrames + SimulationConfig.TickRate - 1) / SimulationConfig.TickRate;
            DrawWaveWarning(screenWidth, state.WarningDirection, secondsRemaining,
                state.Flags.HasFlag(WaveStateFlags.IsFinalWave));
        }
        else if (state.Flags.HasFlag(WaveStateFlags.HordeActive))
        {
            DrawActiveWaveIndicator(screenWidth, "HORDE WAVE", state.HordeSpawnedCount, state.HordeZombieCount, state.HordeDirection);
        }
    }

    private void DrawDayCounter(int day, int hour, int hordeWave, int miniWave)
    {
        int x = 10;
        int y = 10;

        // Background panel
        ImmediateModeUI.DrawPanel(x, y, 150, 40, ImmediateModeUI.PanelColor, ImmediateModeUI.PanelBorderColor);

        // Day number and time
        string dayText = $"Day {day}";
        Raylib.DrawText(dayText, x + 10, y + 10, 24, DayTextColor);

        // Hour display (24-hour format)
        string hourText = $"{hour:D2}:00";
        Raylib.DrawText(hourText, x + 100, y + 14, 18, ImmediateModeUI.TextColor);
    }

    private void DrawWaveWarning(int screenWidth, byte direction, int secondsRemaining, bool isFinalWave)
    {
        int panelWidth = 220;
        int panelHeight = 50;
        int x = (screenWidth - panelWidth) / 2;
        int y = 10;

        // Background panel
        ImmediateModeUI.DrawPanel(x, y, panelWidth, panelHeight, WarningBgColor, WarningTextColor);

        // Title with direction arrow and countdown
        string dirArrow = direction < DirectionArrows.Length ? DirectionArrows[direction] : "?";
        string title = isFinalWave ? "FINAL WAVE" : "HORDE";
        int titleWidth = Raylib.MeasureText(title, 16);
        Raylib.DrawText(title, x + (panelWidth - titleWidth) / 2, y + 6, 16, WarningTextColor);

        // Direction and countdown on second line
        string infoText = $"{dirArrow} {secondsRemaining}s";
        int infoWidth = Raylib.MeasureText(infoText, 18);
        Raylib.DrawText(infoText, x + (panelWidth - infoWidth) / 2, y + 26, 18, Color.White);
    }

    private void DrawActiveWaveIndicator(int screenWidth, string waveType, int spawned, int total, byte direction)
    {
        int panelWidth = 200;
        int panelHeight = 50;
        int x = (screenWidth - panelWidth) / 2;
        int y = 10;

        // Background
        ImmediateModeUI.DrawPanel(x, y, panelWidth, panelHeight, new Color(60, 20, 20, 200), WaveActiveColor);

        // Wave type
        int typeWidth = Raylib.MeasureText(waveType, 18);
        Raylib.DrawText(waveType, x + (panelWidth - typeWidth) / 2, y + 6, 18, WaveActiveColor);

        // Progress
        string progressText = $"{spawned}/{total} spawned";
        int progressWidth = Raylib.MeasureText(progressText, 14);
        Raylib.DrawText(progressText, x + (panelWidth - progressWidth) / 2, y + 28, 14, ImmediateModeUI.TextColor);
    }
}
