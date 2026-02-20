using Derp.UI;
using Core;
using DerpLib.Rendering;
using Friflo.Engine.ECS;
using System.Numerics;

namespace Derp.UI.Example;

public struct UiCanvasComponent : IComponent
{
    public string? AssetUrl;
    public string? LastAssetUrl;

    // Optional prefab selector.
    // If PrefabStableId != 0, it takes precedence over PrefabIndex.
    // If PrefabName is set, it takes precedence over both.
    public StringHandle PrefabName;
    public uint PrefabStableId;
    public int PrefabIndex;
    public StringHandle LastPrefabName;

    // Canvas size the UI should simulate and render at (in pixels).
    // If either is <= 0, the system uses the window size.
    public int CanvasWidth;
    public int CanvasHeight;

    // Cached runtime objects (owned by this component).
    public UiRuntime? Runtime;
    public CanvasSurface? Surface;

    // Cached values to avoid redundant work each frame.
    public int LastAppliedCanvasWidth;
    public int LastAppliedCanvasHeight;
    public uint ActivePrefabStableIdRuntime;

    // Render output (written by the update system, read by the render system).
    public Texture OutputTexture;
    public int OutputWidth;
    public int OutputHeight;

    // Diagnostics
    public bool Loaded;
    public bool LoadFailed;
    public bool LoggedLoad;
    public bool LoggedLoadFailure;
    public string? LoadError;
    public bool LoggedFirstFrameStats;
    public bool LoggedPrefabSelection;
    public uint HoveredStableId;

    // Debug (written by update system; consumed by debug overlay).
    public bool DebugPointerValid;
    public Vector2 DebugPointerCanvas;
    public Vector2 DebugPointerWorld;
    public float DebugLetterboxXOffset;
    public float DebugLetterboxYOffset;
}
