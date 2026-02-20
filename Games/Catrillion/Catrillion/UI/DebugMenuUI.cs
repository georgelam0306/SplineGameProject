using System.Numerics;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Config;
using Catrillion.GameData.Schemas;
using Catrillion.Rendering.Components;
using Catrillion.Simulation;
using Catrillion.Simulation.Components;
using Catrillion.Stores;
using Core;
using SimTable;

namespace Catrillion.UI;

/// <summary>
/// In-game debug menu for testing win/lose conditions.
/// Toggle with F4 key.
/// </summary>
public sealed class DebugMenuUI : BaseSystem
{
    private readonly GameSimulation _gameSimulation;
    private readonly InputStore _inputStore;
    private bool _isVisible;

    // Layout constants
    private const int PanelX = 10;
    private const int PanelY = 10;
    private const int PanelWidth = 180;
    private const int ButtonWidth = 160;
    private const int ButtonHeight = 30;
    private const int ButtonSpacing = 5;
    private const int HeaderHeight = 30;

    public DebugMenuUI(GameSimulation gameSimulation, InputStore inputStore)
    {
        _gameSimulation = gameSimulation;
        _inputStore = inputStore;
    }

    protected override void OnUpdateGroup()
    {
        // Toggle visibility with F4
        if (Raylib.IsKeyPressed(KeyboardKey.F4))
        {
            _isVisible = !_isVisible;
        }

        if (!_isVisible) return;

        // Use software cursor position for UI interactions
        Vector2 mousePos = _inputStore.InputManager.MousePosition;

        // Calculate panel height based on button count
        int buttonCount = 6;
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
        if (ImmediateModeUI.Button(buttonX, buttonY, ButtonWidth, ButtonHeight, "Trigger Victory", mousePos))
        {
            TriggerVictory();
        }
        buttonY += ButtonHeight + ButtonSpacing;

        // Button 2: Trigger Defeat
        if (ImmediateModeUI.Button(buttonX, buttonY, ButtonWidth, ButtonHeight, "Trigger Defeat", mousePos))
        {
            TriggerDefeat();
        }
        buttonY += ButtonHeight + ButtonSpacing;

        // Button 3: Kill All Zombies
        if (ImmediateModeUI.Button(buttonX, buttonY, ButtonWidth, ButtonHeight, "Kill All Zombies", mousePos))
        {
            KillAllZombies();
        }
        buttonY += ButtonHeight + ButtonSpacing;

        // Button 4: Spawn Test Zombie
        if (ImmediateModeUI.Button(buttonX, buttonY, ButtonWidth, ButtonHeight, "Spawn Zombie", mousePos))
        {
            SpawnTestZombie();
        }
        buttonY += ButtonHeight + ButtonSpacing;

        // Button 5: Spawn 100 Soldiers
        if (ImmediateModeUI.Button(buttonX, buttonY, ButtonWidth, ButtonHeight, "Spawn 100 Soldiers", mousePos))
        {
            SpawnSoldiers();
        }
        buttonY += ButtonHeight + ButtonSpacing;

        // Button 6: Toggle Fog of War
        string fowLabel = GameConfig.Debug.DisableFogOfWar ? "FoW: OFF" : "FoW: ON";
        if (ImmediateModeUI.Button(buttonX, buttonY, ButtonWidth, ButtonHeight, fowLabel, mousePos))
        {
            GameConfig.Debug.DisableFogOfWar = !GameConfig.Debug.DisableFogOfWar;
        }
    }

    private void TriggerVictory()
    {
        var simWorld = _gameSimulation.SimWorld;

        // Set final wave flag and clear active spawning flags
        if (simWorld.WaveStateRows.TryGetRow(0, out var waveState))
        {
            waveState.Flags |= WaveStateFlags.IsFinalWave;
            waveState.Flags &= ~WaveStateFlags.HordeActive;
            waveState.Flags &= ~WaveStateFlags.MiniWaveActive;
        }

        // Kill all zombies
        KillAllZombies();
    }

    private void TriggerDefeat()
    {
        var simWorld = _gameSimulation.SimWorld;
        var buildings = simWorld.BuildingRows;

        // Find and destroy command center
        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (building.TypeId != BuildingTypeId.CommandCenter) continue;

            // Set health to 0 - death system will handle the rest
            building.Health = 0;
            break;
        }
    }

    private void KillAllZombies()
    {
        var simWorld = _gameSimulation.SimWorld;
        var zombies = simWorld.ZombieRows;

        for (int slot = 0; slot < zombies.Count; slot++)
        {
            if (!zombies.TryGetRow(slot, out var zombie)) continue;
            zombie.Health = 0;
        }
    }

    private void SpawnTestZombie()
    {
        var simWorld = _gameSimulation.SimWorld;
        var zombies = simWorld.ZombieRows;

        // Get command center position for spawn location
        var buildings = simWorld.BuildingRows;
        Fixed64Vec2 spawnPos = new(Fixed64.FromInt(200), Fixed64.FromInt(200));

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (building.TypeId != BuildingTypeId.CommandCenter) continue;

            // Spawn near command center (offset by 100 pixels)
            spawnPos = new Fixed64Vec2(
                building.Position.X + Fixed64.FromInt(100),
                building.Position.Y + Fixed64.FromInt(100));
            break;
        }

        // Allocate new zombie
        var handle = zombies.Allocate();
        var zombie = zombies.GetRow(handle);

        zombie.Position = spawnPos;
        zombie.TypeId = ZombieTypeId.Aged;  // Aged is the basic zombie type
        zombie.Health = 100;
        zombie.MaxHealth = 100;
        zombie.Damage = 10;
        zombie.AttackRange = Fixed64.FromInt(16);
        zombie.MoveSpeed = Fixed64.FromInt(30);  // 30 pixels per second
        zombie.State = ZombieState.Idle;
        zombie.TargetHandle = SimHandle.Invalid;
    }

    private void SpawnSoldiers()
    {
        const int UnitCount = 100;
        const int Spacing = 24;  // Pixels between units
        const int UnitsPerRow = 10;

        var simWorld = _gameSimulation.SimWorld;
        var spawner = _gameSimulation.EntitySpawner;
        var gameData = _gameSimulation.GameData;
        var units = simWorld.CombatUnitRows;

        // Get command center position for spawn location
        var buildings = simWorld.BuildingRows;
        Fixed64Vec2 basePos = new(Fixed64.FromInt(200), Fixed64.FromInt(200));

        for (int slot = 0; slot < buildings.Count; slot++)
        {
            if (!buildings.TryGetRow(slot, out var building)) continue;
            if (building.TypeId != BuildingTypeId.CommandCenter) continue;

            // Spawn near command center
            basePos = new Fixed64Vec2(
                building.Position.X + Fixed64.FromInt(80),
                building.Position.Y + Fixed64.FromInt(80));
            break;
        }

        for (int i = 0; i < UnitCount; i++)
        {
            // Calculate grid position (10x10 grid)
            int col = i % UnitsPerRow;
            int rowNum = i / UnitsPerRow;
            Fixed64Vec2 spawnPos = new(
                basePos.X + Fixed64.FromInt(col * Spacing),
                basePos.Y + Fixed64.FromInt(rowNum * Spacing));

            // Use builder pattern - creates both SimWorld row AND ECS entity
            var handle = spawner.Create<CombatUnitRow>()
                .WithRender(new SpriteRenderer
                {
                    TexturePath = "Resources/Characters/cat.png",
                    Width = 32,
                    Height = 32,
                    SourceX = 0,
                    SourceY = 0,
                    SourceWidth = 32,
                    SourceHeight = 32,
                    Color = Color.Blue,
                    IsVisible = true
                })
                .Spawn();

            // Initialize SimWorld row data
            var row = units.GetRow(handle.SimHandle);
            row.Position = spawnPos;
            row.Velocity = Fixed64Vec2.Zero;
            row.OwnerPlayerId = 0;  // Team ownership
            row.TypeId = UnitTypeId.Soldier;  // Must set before InitializeStats
            row.GroupId = 0;
            row.SelectedByPlayerId = -1;
            row.CurrentOrder = OrderType.None;
            row.OrderTarget = Fixed64Vec2.Zero;
            row.Flags = MortalFlags.IsActive;

            // Initialize all [CachedStat] fields from UnitTypeData
            units.InitializeStats(handle.SimHandle, gameData.Db);

            // Non-cached stats
            row.Health = row.MaxHealth;
            row.AttackTimer = 0;
            row.TargetHandle = SimHandle.Invalid;
            row.OrderTargetHandle = SimHandle.Invalid;
            row.GarrisonedInHandle = SimHandle.Invalid;
        }
    }
}
