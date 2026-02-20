using System;
using System.Numerics;
using Derp.UI;
using Core;
using DerpLib.AssetPipeline;
using DerpLib.Text;
using Friflo.Engine.ECS.Systems;
using DerpEngine = DerpLib.Derp;

namespace Derp.UI.Example;

public sealed class UiCanvasUpdateSystem : QuerySystem<UiCanvasComponent>
{
    private readonly UiCanvasFrameContext _frame;
    private readonly ContentManager _content;
    private readonly Font _font;

    public UiCanvasUpdateSystem(UiCanvasFrameContext frame, ContentManager content, Font font)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _font = font ?? throw new ArgumentNullException(nameof(font));
    }

    protected override void OnUpdate()
    {
        foreach (var entity in Query.Entities)
        {
            ref var canvas = ref entity.GetComponent<UiCanvasComponent>();
            UpdateCanvas(ref canvas);
        }
    }

    private void UpdateCanvas(ref UiCanvasComponent canvas)
    {
        if (canvas.Runtime == null)
        {
            canvas.Runtime = new UiRuntime();
            canvas.Runtime.SetFont(_font);
        }

        if (canvas.Surface == null)
        {
            canvas.Surface = new CanvasSurface();
            canvas.Surface.SetFontAtlas(_font.Atlas);
        }

        UiRuntime runtime = canvas.Runtime;
        CanvasSurface surface = canvas.Surface;

        string? assetUrl = canvas.AssetUrl;
        if (!string.IsNullOrWhiteSpace(assetUrl))
        {
            if (!string.Equals(canvas.LastAssetUrl, assetUrl, StringComparison.Ordinal))
            {
                canvas.LastAssetUrl = assetUrl;
                canvas.Loaded = false;
                canvas.LoadFailed = false;
                canvas.LoggedLoad = false;
                canvas.LoggedLoadFailure = false;
                canvas.LoadError = null;
                canvas.LastPrefabName = StringHandle.Invalid;
            }

            if (!canvas.Loaded && !canvas.LoadFailed)
            {
                try
                {
                    CompiledUi compiled = _content.Load<CompiledUi>(assetUrl);
                    runtime.Load(compiled);
                    canvas.Loaded = true;
                    canvas.LoggedLoad = false;
                    canvas.LoadFailed = false;
                    canvas.LoggedLoadFailure = false;
                    canvas.LoadError = null;
                    canvas.LoggedFirstFrameStats = false;
                    canvas.LoggedPrefabSelection = false;
                    canvas.LastPrefabName = StringHandle.Invalid;
                }
                catch (Exception ex)
                {
                    canvas.Loaded = false;
                    canvas.LoadFailed = true;
                    canvas.LoadError = ex.Message;
                }
            }
        }
        else
        {
            canvas.LastAssetUrl = assetUrl;
            canvas.Loaded = false;
            canvas.LoadFailed = false;
            canvas.LoggedLoad = false;
            canvas.LoggedLoadFailure = false;
            canvas.LoadError = null;
            canvas.LastPrefabName = StringHandle.Invalid;
        }

        if (!canvas.Loaded)
        {
            if (canvas.LoadFailed && !canvas.LoggedLoadFailure)
            {
                canvas.LoggedLoadFailure = true;
                string error = string.IsNullOrWhiteSpace(canvas.LoadError) ? "Unknown error" : canvas.LoadError;
                DerpEngine.SetWindowTitle($"Derp.UI.Example - Failed to load {assetUrl}: {error}");
                Console.WriteLine($"Derp.UI.Example: Failed to load `{assetUrl}`: {error}");
            }

            canvas.OutputTexture = default;
            canvas.OutputWidth = 0;
            canvas.OutputHeight = 0;
            canvas.HoveredStableId = 0;
            return;
        }

        if (!canvas.LoggedLoad)
        {
            canvas.LoggedLoad = true;
            DerpEngine.SetWindowTitle($"Derp.UI.Example - Loaded {runtime.PrefabCount} prefab(s) from {assetUrl}");
        }

        if (canvas.PrefabName.IsValid)
        {
            if (canvas.LastPrefabName != canvas.PrefabName)
            {
                canvas.LastPrefabName = canvas.PrefabName;
                bool ok = runtime.TrySetActivePrefabByName(canvas.PrefabName);
                if (!canvas.LoggedPrefabSelection)
                {
                    canvas.LoggedPrefabSelection = true;
                    Console.WriteLine($"Derp.UI.Example: TrySetActivePrefabByName({canvas.PrefabName}) => {ok}");
                }
            }
        }
        else if (canvas.PrefabStableId != 0)
        {
            bool shouldSelect = runtime.ActivePrefabStableId != canvas.PrefabStableId;
            bool ok = !shouldSelect || runtime.TrySetActivePrefabByStableId(canvas.PrefabStableId);
            if (shouldSelect && !canvas.LoggedPrefabSelection)
            {
                canvas.LoggedPrefabSelection = true;
                Console.WriteLine($"Derp.UI.Example: TrySetActivePrefabByStableId({canvas.PrefabStableId}) => {ok}");
            }
        }
        else if (canvas.PrefabIndex >= 0)
        {
            uint desiredStableId = 0;
            if (runtime.TryGetPrefabInfo(canvas.PrefabIndex, out uint stableId, out _))
            {
                desiredStableId = stableId;
            }

            bool shouldSelect = desiredStableId == 0 || runtime.ActivePrefabStableId != desiredStableId;
            bool ok = !shouldSelect ? true : runtime.TrySetActivePrefabByIndex(canvas.PrefabIndex);
            if (!canvas.LoggedPrefabSelection && shouldSelect)
            {
                canvas.LoggedPrefabSelection = true;
                Console.WriteLine($"Derp.UI.Example: TrySetActivePrefabByIndex({canvas.PrefabIndex}) => {ok}");
            }
        }

        canvas.ActivePrefabStableIdRuntime = runtime.ActivePrefabStableId;

        int targetWidth = canvas.CanvasWidth > 0 ? canvas.CanvasWidth : _frame.WindowWidth;
        int targetHeight = canvas.CanvasHeight > 0 ? canvas.CanvasHeight : _frame.WindowHeight;

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            targetWidth = 1;
            targetHeight = 1;
        }

        bool sizeChanged = canvas.LastAppliedCanvasWidth != targetWidth || canvas.LastAppliedCanvasHeight != targetHeight;
        if (sizeChanged)
        {
            _ = runtime.TrySetActivePrefabCanvasSize(targetWidth, targetHeight, resolveLayout: false);
            canvas.LastAppliedCanvasWidth = targetWidth;
            canvas.LastAppliedCanvasHeight = targetHeight;
        }

        // Map screen-space pointer into the canvas' local coordinate space.
        // If the canvas is letterboxed (canvas size != window size), we center it like the renderer does.
        float xOffset = (_frame.WindowWidth - targetWidth) * 0.5f;
        float yOffset = (_frame.WindowHeight - targetHeight) * 0.5f;

        Vector2 pointerCanvas = _frame.MousePosition - new Vector2(xOffset, yOffset);

        bool pointerValid = pointerCanvas.X >= 0f &&
            pointerCanvas.Y >= 0f &&
            pointerCanvas.X < targetWidth &&
            pointerCanvas.Y < targetHeight;

        Vector2 pointerWorld = pointerValid ? pointerCanvas : default;

        canvas.DebugPointerValid = pointerValid;
        canvas.DebugPointerCanvas = pointerCanvas;
        canvas.DebugPointerWorld = pointerWorld;
        canvas.DebugLetterboxXOffset = xOffset;
        canvas.DebugLetterboxYOffset = yOffset;

        var input = new UiPointerFrameInput(
            pointerValid: pointerValid,
            pointerWorld: pointerWorld,
            primaryDown: _frame.PrimaryDown,
            wheelDelta: _frame.WheelDelta,
            hoveredStableId: pointerValid ? UiPointerFrameInput.ComputeHoveredStableId : 0);

        runtime.Tick(_frame.DeltaMicroseconds, input);
        canvas.HoveredStableId = runtime.Input.Current.HoveredStableId;

        // Layout is currently an explicit call in Derp.UI runtime.
        // For now, re-resolve when the canvas size changes or when input can drive layout via scroll constraints.
        if (sizeChanged || _frame.WheelDelta != 0f || _frame.PrimaryDown)
        {
            runtime.ResolveLayout();
        }

        runtime.BuildFrame(surface, targetWidth, targetHeight);

        if (!canvas.LoggedFirstFrameStats)
        {
            canvas.LoggedFirstFrameStats = true;
            Console.WriteLine($"Derp.UI.Example: ui=`{assetUrl}`, prefabStableId={runtime.ActivePrefabStableId}, canvas={targetWidth}x{targetHeight}, sdfCommands={surface.Buffer.Count}, textureIndex={surface.Texture.GetIndex()}, textureSize={surface.Texture.Width}x{surface.Texture.Height}");
        }

        surface.DispatchToTexture();

        canvas.OutputTexture = surface.Texture;
        canvas.OutputWidth = targetWidth;
        canvas.OutputHeight = targetHeight;
    }
}
