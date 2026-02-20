using System.Collections.Generic;
using System.Numerics;
using Friflo.Engine.ECS.Systems;
using Raylib_cs;
using Serilog;
using Serilog.Events;
using DieDrifterDie.Presentation.Rendering.Components;
using Core;

namespace DieDrifterDie.Presentation.Rendering.Systems;

public sealed class SpriteRenderSystem : QuerySystem<Transform2D, SpriteRenderer>
{
    private static readonly ILogger _log = Log.ForContext<SpriteRenderSystem>();

    private readonly Dictionary<string, Texture2D> _textureCache = new();
    private int _frameCounter;

    protected override void OnUpdate()
    {
        bool debugEnabled = _log.IsEnabled(LogEventLevel.Debug);
        _frameCounter++;
        bool shouldLog = debugEnabled && _frameCounter % 120 == 1;
        int offscreenCount = 0;

        foreach (var entity in Query.Entities)
        {
            ref readonly var transform = ref entity.GetComponent<Transform2D>();
            ref readonly var sprite = ref entity.GetComponent<SpriteRenderer>();

            // Skip invisible sprites (fog of war)
            if (!sprite.IsVisible) continue;

            // Skip rendering for far-offscreen sprites (dead units moved to -10000)
            if (transform.Position.X < -5000f || transform.Position.Y < -5000f)
            {
                offscreenCount++;
                continue;
            }

            float drawX = transform.Position.X - sprite.Width * 0.5f;
            float drawY = transform.Position.Y - sprite.Height * 0.5f;

            if (!string.IsNullOrEmpty(sprite.TexturePath))
            {
                var texture = GetOrLoadTexture(sprite.TexturePath);
                if (texture.Id != 0)
                {
                    var sourceRect = new Rectangle(
                        sprite.SourceX,
                        sprite.SourceY,
                        sprite.SourceWidth > 0 ? sprite.SourceWidth : sprite.Width,
                        sprite.SourceHeight > 0 ? sprite.SourceHeight : sprite.Height
                    );
                    var destRect = new Rectangle(drawX, drawY, sprite.Width, sprite.Height);
                    Raylib.DrawTexturePro(texture, sourceRect, destRect, Vector2.Zero, 0f, sprite.Color);
                    continue;
                }
            }

            Raylib.DrawRectangle((int)drawX, (int)drawY, sprite.Width, sprite.Height, sprite.Color);
        }

        if (shouldLog && offscreenCount > 0)
        {
            _log.Debug("{OffscreenCount} sprites at offscreen positions (X < -1000)", offscreenCount);
        }
    }

    private Texture2D GetOrLoadTexture(string path)
    {
        if (_textureCache.TryGetValue(path, out var texture))
        {
            return texture;
        }

        texture = Raylib.LoadTexture(path);
        if (texture.Id != 0)
        {
            Raylib.SetTextureFilter(texture, TextureFilter.Point);
        }
        _textureCache[path] = texture;
        return texture;
    }
}

