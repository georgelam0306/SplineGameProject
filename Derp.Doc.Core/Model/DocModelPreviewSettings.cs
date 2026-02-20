using System.Globalization;

namespace Derp.Doc.Model;

public sealed class DocModelPreviewSettings
{
    public const float DefaultOrbitYawDegrees = -38.96f;
    public const float DefaultOrbitPitchDegrees = 31.51f;
    public const float DefaultPanX = 0f;
    public const float DefaultPanY = 0.06f;
    public const float DefaultZoom = 1f;

    public const float MinOrbitPitchDegrees = -89f;
    public const float MaxOrbitPitchDegrees = 89f;
    public const float MinPan = -1.5f;
    public const float MaxPan = 1.5f;
    public const float MinZoom = 0.2f;
    public const float MaxZoom = 4f;

    public float OrbitYawDegrees { get; set; } = DefaultOrbitYawDegrees;
    public float OrbitPitchDegrees { get; set; } = DefaultOrbitPitchDegrees;
    public float PanX { get; set; } = DefaultPanX;
    public float PanY { get; set; } = DefaultPanY;
    public float Zoom { get; set; } = DefaultZoom;
    public string? TextureRelativePath { get; set; }

    public DocModelPreviewSettings Clone()
    {
        return new DocModelPreviewSettings
        {
            OrbitYawDegrees = OrbitYawDegrees,
            OrbitPitchDegrees = OrbitPitchDegrees,
            PanX = PanX,
            PanY = PanY,
            Zoom = Zoom,
            TextureRelativePath = TextureRelativePath,
        };
    }

    public void ClampInPlace()
    {
        OrbitYawDegrees = WrapDegrees(OrbitYawDegrees);
        OrbitPitchDegrees = Math.Clamp(OrbitPitchDegrees, MinOrbitPitchDegrees, MaxOrbitPitchDegrees);
        PanX = Math.Clamp(PanX, MinPan, MaxPan);
        PanY = Math.Clamp(PanY, MinPan, MaxPan);
        Zoom = Math.Clamp(Zoom, MinZoom, MaxZoom);

        if (!string.IsNullOrWhiteSpace(TextureRelativePath))
        {
            TextureRelativePath = TextureRelativePath.Trim();
        }

        if (string.IsNullOrWhiteSpace(TextureRelativePath))
        {
            TextureRelativePath = null;
        }
    }

    public static string BuildCacheSignature(DocModelPreviewSettings? settings)
    {
        DocModelPreviewSettings normalized = settings?.Clone() ?? new DocModelPreviewSettings();
        normalized.ClampInPlace();

        float quantizedYaw = Quantize(normalized.OrbitYawDegrees, 0.25f);
        float quantizedPitch = Quantize(normalized.OrbitPitchDegrees, 0.25f);
        float quantizedPanX = Quantize(normalized.PanX, 0.0025f);
        float quantizedPanY = Quantize(normalized.PanY, 0.0025f);
        float quantizedZoom = Quantize(normalized.Zoom, 0.005f);
        string texturePath = NormalizeTexturePath(normalized.TextureRelativePath);

        return string.Concat(
            quantizedYaw.ToString("F2", CultureInfo.InvariantCulture), "|",
            quantizedPitch.ToString("F2", CultureInfo.InvariantCulture), "|",
            quantizedPanX.ToString("F4", CultureInfo.InvariantCulture), "|",
            quantizedPanY.ToString("F4", CultureInfo.InvariantCulture), "|",
            quantizedZoom.ToString("F3", CultureInfo.InvariantCulture), "|",
            texturePath);
    }

    public static bool IsDefault(DocModelPreviewSettings? settings)
    {
        if (settings == null)
        {
            return true;
        }

        DocModelPreviewSettings normalized = settings.Clone();
        normalized.ClampInPlace();

        return MathF.Abs(normalized.OrbitYawDegrees - DefaultOrbitYawDegrees) <= 0.0001f &&
               MathF.Abs(normalized.OrbitPitchDegrees - DefaultOrbitPitchDegrees) <= 0.0001f &&
               MathF.Abs(normalized.PanX - DefaultPanX) <= 0.0001f &&
               MathF.Abs(normalized.PanY - DefaultPanY) <= 0.0001f &&
               MathF.Abs(normalized.Zoom - DefaultZoom) <= 0.0001f &&
               string.IsNullOrWhiteSpace(normalized.TextureRelativePath);
    }

    private static float WrapDegrees(float degrees)
    {
        float wrapped = degrees % 360f;
        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        else if (wrapped < -180f)
        {
            wrapped += 360f;
        }

        return wrapped;
    }

    private static float Quantize(float value, float step)
    {
        if (step <= 0f)
        {
            return value;
        }

        return MathF.Round(value / step) * step;
    }

    private static string NormalizeTexturePath(string? textureRelativePath)
    {
        if (string.IsNullOrWhiteSpace(textureRelativePath))
        {
            return "";
        }

        string normalized = textureRelativePath.Trim().Replace('\\', '/');
        while (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        return normalized;
    }
}
