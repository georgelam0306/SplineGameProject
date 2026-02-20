using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Catrillion.Config;
using Catrillion.Simulation.Components;
using Catrillion.Simulation.Services;

namespace Catrillion.Rendering.Systems;

/// <summary>
/// Renders fog of war overlay using a GPU texture.
/// Uses a 256x256 RGBA texture (1:1 with tiles) uploaded each frame.
/// - Unexplored: opaque black
/// - Visible: fully transparent
/// - Fogged: semi-transparent gray
/// </summary>
public sealed class FogOfWarRenderSystem : BaseSystem
{
    private readonly SimWorld _simWorld;
    private readonly int _tileSize;
    private readonly int _mapWidthPixels;
    private readonly int _mapHeightPixels;

    private Image _fogImage;
    private Texture2D _fogTexture;
    private bool _initialized;

    // Pre-computed color values for each visibility state
    private readonly Color _unexploredColor = new(0, 0, 0, 255);      // Opaque black
    private readonly Color _visibleColor = new(0, 0, 0, 0);           // Fully transparent
    private readonly Color _foggedColor = new(30, 30, 40, 160);       // Semi-transparent dark gray

    public FogOfWarRenderSystem(SimWorld simWorld)
    {
        _simWorld = simWorld;
        _tileSize = GameConfig.Map.TileSize;
        _mapWidthPixels = GameConfig.Map.WidthTiles * _tileSize;
        _mapHeightPixels = GameConfig.Map.HeightTiles * _tileSize;
    }

    protected override void OnUpdateGroup()
    {
        // Skip rendering if fog of war is disabled for debugging
        if (Config.GameConfig.Debug.DisableFogOfWar) return;

        var fogTable = _simWorld.FogOfWarGridStateRows;
        if (fogTable.Count == 0) return;

        // Initialize texture on first use
        if (!_initialized)
        {
            InitializeTexture();
            _initialized = true;
        }

        // Update fog image pixels from visibility grid
        UpdateFogImage(fogTable);

        // Upload to GPU
        unsafe
        {
            Raylib.UpdateTexture(_fogTexture, _fogImage.Data);
        }

        // Draw fog overlay covering entire map
        var source = new Rectangle(0, 0, GameConfig.Map.WidthTiles, GameConfig.Map.HeightTiles);
        var dest = new Rectangle(0, 0, _mapWidthPixels, _mapHeightPixels);
        Raylib.DrawTexturePro(_fogTexture, source, dest, System.Numerics.Vector2.Zero, 0f, Color.White);
    }

    private void InitializeTexture()
    {
        // Create 256x256 RGBA image (1 pixel per tile)
        _fogImage = Raylib.GenImageColor(GameConfig.Map.WidthTiles, GameConfig.Map.HeightTiles, _unexploredColor);
        _fogTexture = Raylib.LoadTextureFromImage(_fogImage);

        // Use bilinear filtering for soft fog edges
        Raylib.SetTextureFilter(_fogTexture, TextureFilter.Bilinear);
    }

    private unsafe void UpdateFogImage(FogOfWarGridStateRowTable fogTable)
    {
        var pixels = (Color*)_fogImage.Data;

        for (int y = 0; y < GameConfig.Map.HeightTiles; y++)
        {
            var visibilityRow = FogOfWarService.GetVisibilityRow(fogTable, y);

            for (int x = 0; x < GameConfig.Map.WidthTiles; x++)
            {
                byte visibility = visibilityRow[x];

                // Countdown-based visibility:
                // 0 = Unexplored, 1 = Fogged, 2+ = Visible
                Color color;
                if (visibility >= FogOfWarService.VisibleMin)
                {
                    color = _visibleColor;  // Visible (transparent)
                }
                else if (visibility == FogOfWarService.Fogged)
                {
                    color = _foggedColor;   // Fogged (semi-transparent)
                }
                else
                {
                    color = _unexploredColor;  // Unexplored (opaque black)
                }

                pixels[y * GameConfig.Map.WidthTiles + x] = color;
            }
        }
    }

    /// <summary>
    /// Cleanup textures when system is disposed.
    /// </summary>
    public void Dispose()
    {
        if (_initialized)
        {
            Raylib.UnloadTexture(_fogTexture);
            Raylib.UnloadImage(_fogImage);
        }
    }
}
