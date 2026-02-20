using System.Numerics;
using DerpLib.Rendering;
using Friflo.Engine.ECS.Systems;
using DerpEngine = DerpLib.Derp;

namespace Derp.UI.Example;

public sealed class UiCanvasRenderSystem : QuerySystem<UiCanvasComponent>
{
    protected override void OnUpdate()
    {
        foreach (var entity in Query.Entities)
        {
            ref readonly var canvas = ref entity.GetComponent<UiCanvasComponent>();
            DrawCanvas(in canvas);
        }

        // Debug marker: draw last so it can't be covered by the UI canvas background/border.
        // If you can't see this, the 2D draw path isn't executing.
        var marker =
            Matrix4x4.CreateScale(40f, 40f, 1f) *
            Matrix4x4.CreateTranslation(40f * 0.5f, 40f * 0.5f, 0f);
        DerpEngine.DrawTextureTransform(Texture.White, marker, 255, 0, 255, 255);
    }

    private static void DrawCanvas(in UiCanvasComponent canvas)
    {
        if (canvas.OutputWidth <= 0 || canvas.OutputHeight <= 0)
        {
            return;
        }

        Texture texture = canvas.OutputTexture;
        if (texture.Width <= 0 || texture.Height <= 0)
        {
            return;
        }

        int screenWidth = DerpEngine.GetScreenWidth();
        int screenHeight = DerpEngine.GetScreenHeight();

        // Draw the UI texture pixel-perfect in screen space (top-left origin).
        // Note: The SDF output texture is Y-flipped relative to the engine's quad UV convention,
        // so we flip in Y here to keep authored UI orientation correct.
        var transform =
            Matrix4x4.CreateScale(canvas.OutputWidth, -canvas.OutputHeight, 1f) *
            Matrix4x4.CreateTranslation(canvas.OutputWidth * 0.5f, canvas.OutputHeight * 0.5f, 0f);

        // If the canvas isn't the same size as the window, center it with letterboxing.
        if (canvas.OutputWidth != screenWidth || canvas.OutputHeight != screenHeight)
        {
            float xOffset = (screenWidth - canvas.OutputWidth) * 0.5f;
            float yOffset = (screenHeight - canvas.OutputHeight) * 0.5f;
            transform =
                Matrix4x4.CreateScale(canvas.OutputWidth, -canvas.OutputHeight, 1f) *
                Matrix4x4.CreateTranslation(xOffset + canvas.OutputWidth * 0.5f, yOffset + canvas.OutputHeight * 0.5f, 0f);
        }

        // Visualize the UI canvas area even when the UI output is fully transparent.
        DerpEngine.DrawTextureTransform(Texture.White, transform, 28, 28, 34, 255);
        DerpEngine.DrawTextureTransform(texture, transform, 255, 255, 255, 255);

        const float BorderThickness = 2f;

        var top =
            Matrix4x4.CreateScale(canvas.OutputWidth, BorderThickness, 1f) *
            Matrix4x4.CreateTranslation(transform.Translation.X, transform.Translation.Y - canvas.OutputHeight * 0.5f + BorderThickness * 0.5f, 0f);
        var bottom =
            Matrix4x4.CreateScale(canvas.OutputWidth, BorderThickness, 1f) *
            Matrix4x4.CreateTranslation(transform.Translation.X, transform.Translation.Y + canvas.OutputHeight * 0.5f - BorderThickness * 0.5f, 0f);
        var left =
            Matrix4x4.CreateScale(BorderThickness, canvas.OutputHeight, 1f) *
            Matrix4x4.CreateTranslation(transform.Translation.X - canvas.OutputWidth * 0.5f + BorderThickness * 0.5f, transform.Translation.Y, 0f);
        var right =
            Matrix4x4.CreateScale(BorderThickness, canvas.OutputHeight, 1f) *
            Matrix4x4.CreateTranslation(transform.Translation.X + canvas.OutputWidth * 0.5f - BorderThickness * 0.5f, transform.Translation.Y, 0f);

        DerpEngine.DrawTextureTransform(Texture.White, top, 90, 150, 255, 255);
        DerpEngine.DrawTextureTransform(Texture.White, bottom, 90, 150, 255, 255);
        DerpEngine.DrawTextureTransform(Texture.White, left, 90, 150, 255, 255);
        DerpEngine.DrawTextureTransform(Texture.White, right, 90, 150, 255, 255);
    }
}
