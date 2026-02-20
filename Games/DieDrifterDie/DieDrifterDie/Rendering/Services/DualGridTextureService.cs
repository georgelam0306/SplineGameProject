using Raylib_cs;

namespace DieDrifterDie.Presentation.Rendering.Services;

/// <summary>
/// Manages shared texture atlases for dual-grid rendering.
/// Textures are loaded once and shared across all chunks.
/// </summary>
public sealed class DualGridTextureService
{
    public Texture2D Grass { get; private set; }
    public Texture2D Dirt { get; private set; }
    public Texture2D Water { get; private set; }
    public Texture2D Mountain { get; private set; }
    public bool IsLoaded { get; private set; }

    public void LoadTextures()
    {
        if (IsLoaded)
        {
            return;
        }

        Grass = Raylib.LoadTexture("Resources/DebugDualGridTiles.png");
        Dirt = Raylib.LoadTexture("Resources/DebugDualGridTiles_Dirt.png");
        Water = Raylib.LoadTexture("Resources/DebugDualGridTiles_Water.png");
        Mountain = Raylib.LoadTexture("Resources/DebugDualGridTiles_Mountain.png");

        Raylib.SetTextureFilter(Grass, TextureFilter.Point);
        Raylib.SetTextureFilter(Dirt, TextureFilter.Point);
        Raylib.SetTextureFilter(Water, TextureFilter.Point);
        Raylib.SetTextureFilter(Mountain, TextureFilter.Point);

        Raylib.SetTextureWrap(Grass, TextureWrap.Clamp);
        Raylib.SetTextureWrap(Dirt, TextureWrap.Clamp);
        Raylib.SetTextureWrap(Water, TextureWrap.Clamp);
        Raylib.SetTextureWrap(Mountain, TextureWrap.Clamp);

        IsLoaded = true;
    }

    public void UnloadTextures()
    {
        if (!IsLoaded)
        {
            return;
        }

        Raylib.UnloadTexture(Grass);
        Raylib.UnloadTexture(Dirt);
        Raylib.UnloadTexture(Water);
        Raylib.UnloadTexture(Mountain);

        IsLoaded = false;
    }
}
