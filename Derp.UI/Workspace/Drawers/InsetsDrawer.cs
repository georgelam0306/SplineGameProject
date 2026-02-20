using System;
using System.Numerics;

namespace Derp.UI;

internal static class InsetsDrawer
{
    [Flags]
    public enum Options
    {
        None = 0
    }

    public static bool Draw(
        string label,
        string widgetId,
        float labelWidth,
        float inputWidth,
        float minValue,
        float maxValue,
        float rowPaddingX,
        int options,
        uint mixedMask,
        ref Vector4 value,
        out bool isEditing)
    {
        _ = options;
        return InsetsField.DrawInsets(label, widgetId, labelWidth, inputWidth, minValue, maxValue, rowPaddingX, ref value, mixedMask, out isEditing);
    }
}

