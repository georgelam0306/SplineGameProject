using System;
using Core;

namespace Derp.UI;

internal static class UiColor32
{
    public static uint ToArgb(Color32 color)
    {
        return (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);
    }

    public static Color32 FromArgb(uint argb)
    {
        byte alpha = (byte)(argb >> 24);
        byte red = (byte)(argb >> 16);
        byte green = (byte)(argb >> 8);
        byte blue = (byte)argb;
        return new Color32(red, green, blue, alpha);
    }

    public static Color32 LerpColor(Color32 from, Color32 to, float lerpT)
    {
        lerpT = Math.Clamp(lerpT, 0f, 1f);
        byte red = (byte)(from.R + (to.R - from.R) * lerpT);
        byte green = (byte)(from.G + (to.G - from.G) * lerpT);
        byte blue = (byte)(from.B + (to.B - from.B) * lerpT);
        byte alpha = (byte)(from.A + (to.A - from.A) * lerpT);
        return new Color32(red, green, blue, alpha);
    }

    public static uint ToArgb(Color32 from, Color32 to, float lerpT)
    {
        return ToArgb(LerpColor(from, to, lerpT));
    }

    public static Color32 ApplyTintAndOpacity(Color32 color, Color32 tint, float opacity)
    {
        float alphaScale = Math.Clamp(opacity, 0f, 1f);
        byte red = (byte)((color.R * tint.R) / 255);
        byte green = (byte)((color.G * tint.G) / 255);
        byte blue = (byte)((color.B * tint.B) / 255);
        byte alpha = (byte)((color.A * tint.A) / 255);
        alpha = (byte)(alpha * alphaScale);
        return new Color32(red, green, blue, alpha);
    }
}

