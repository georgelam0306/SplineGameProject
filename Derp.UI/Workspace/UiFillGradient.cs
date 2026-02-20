using System;
using System.Numerics;
using Core;
using DerpLib.Sdf;

namespace Derp.UI;

internal static class UiFillGradient
{
    public const int MaxStops = 8;

    public static int GetFillGradientStopCount(in FillComponent.ViewProxy fillView)
    {
        int count = fillView.GradientStopCount;
        if (count < 2)
        {
            return 0;
        }

        if (count > MaxStops)
        {
            return MaxStops;
        }

        return count;
    }

    public static float GetFillGradientStopT(in FillComponent.ViewProxy fillView, int stopIndex)
    {
        if ((uint)stopIndex >= MaxStops)
        {
            return 0f;
        }

        if (stopIndex < 4)
        {
            Vector4 t0123 = fillView.GradientStopT0To3;
            return stopIndex switch
            {
                0 => t0123.X,
                1 => t0123.Y,
                2 => t0123.Z,
                _ => t0123.W
            };
        }

        Vector4 t4567 = fillView.GradientStopT4To7;
        return (stopIndex - 4) switch
        {
            0 => t4567.X,
            1 => t4567.Y,
            2 => t4567.Z,
            _ => t4567.W
        };
    }

    public static void SetFillGradientStopT(ref FillComponent.ViewProxy fillView, int stopIndex, float value)
    {
        if ((uint)stopIndex >= MaxStops)
        {
            return;
        }

        value = Math.Clamp(value, 0f, 1f);
        if (stopIndex < 4)
        {
            Vector4 t0123 = fillView.GradientStopT0To3;
            switch (stopIndex)
            {
                case 0: t0123.X = value; break;
                case 1: t0123.Y = value; break;
                case 2: t0123.Z = value; break;
                default: t0123.W = value; break;
            }
            fillView.GradientStopT0To3 = t0123;
            return;
        }

        Vector4 t4567 = fillView.GradientStopT4To7;
        switch (stopIndex - 4)
        {
            case 0: t4567.X = value; break;
            case 1: t4567.Y = value; break;
            case 2: t4567.Z = value; break;
            default: t4567.W = value; break;
        }
        fillView.GradientStopT4To7 = t4567;
    }

    public static Color32 GetFillGradientStopColor(in FillComponent.ViewProxy fillView, int stopIndex)
    {
        return stopIndex switch
        {
            0 => fillView.GradientStopColor0,
            1 => fillView.GradientStopColor1,
            2 => fillView.GradientStopColor2,
            3 => fillView.GradientStopColor3,
            4 => fillView.GradientStopColor4,
            5 => fillView.GradientStopColor5,
            6 => fillView.GradientStopColor6,
            7 => fillView.GradientStopColor7,
            _ => default
        };
    }

    public static void SetFillGradientStopColor(ref FillComponent.ViewProxy fillView, int stopIndex, Color32 color)
    {
        switch (stopIndex)
        {
            case 0: fillView.GradientStopColor0 = color; break;
            case 1: fillView.GradientStopColor1 = color; break;
            case 2: fillView.GradientStopColor2 = color; break;
            case 3: fillView.GradientStopColor3 = color; break;
            case 4: fillView.GradientStopColor4 = color; break;
            case 5: fillView.GradientStopColor5 = color; break;
            case 6: fillView.GradientStopColor6 = color; break;
            case 7: fillView.GradientStopColor7 = color; break;
        }
    }

    public static void EnsureFillGradientStopsInitialized(ref FillComponent.ViewProxy fillView)
    {
        if (fillView.GradientStopCount >= 2)
        {
            return;
        }

        fillView.GradientStopCount = 2;
        fillView.GradientStopT0To3 = new Vector4(0f, 1f, 0f, 0f);
        fillView.GradientStopT4To7 = Vector4.Zero;
        fillView.GradientStopColor0 = fillView.GradientColorA;
        fillView.GradientStopColor1 = fillView.GradientColorB;
    }

    public static void SyncFillGradientEndpoints(ref FillComponent.ViewProxy fillView)
    {
        int stopCount = GetFillGradientStopCount(fillView);
        if (stopCount <= 0)
        {
            return;
        }

        fillView.GradientColorA = GetFillGradientStopColor(fillView, 0);
        fillView.GradientColorB = GetFillGradientStopColor(fillView, stopCount - 1);
    }

    public static Color32 SampleFillGradientAtT(in FillComponent.ViewProxy fillView, int stopCount, float sampleT)
    {
        if (stopCount <= 0)
        {
            return fillView.Color;
        }

        sampleT = Math.Clamp(sampleT, 0f, 1f);

        float firstT = GetFillGradientStopT(fillView, 0);
        float lastT = GetFillGradientStopT(fillView, stopCount - 1);
        if (sampleT <= firstT)
        {
            return GetFillGradientStopColor(fillView, 0);
        }
        if (sampleT >= lastT)
        {
            return GetFillGradientStopColor(fillView, stopCount - 1);
        }

        for (int stopIndex = 1; stopIndex < stopCount; stopIndex++)
        {
            float stopT = GetFillGradientStopT(fillView, stopIndex);
            if (sampleT <= stopT)
            {
                float prevT = GetFillGradientStopT(fillView, stopIndex - 1);
                Color32 prevColor = GetFillGradientStopColor(fillView, stopIndex - 1);
                Color32 nextColor = GetFillGradientStopColor(fillView, stopIndex);
                float denom = stopT - prevT;
                if (denom <= 0.00001f)
                {
                    return nextColor;
                }
                float lerpT = Math.Clamp((sampleT - prevT) / denom, 0f, 1f);
                return UiColor32.LerpColor(prevColor, nextColor, lerpT);
            }
        }

        return GetFillGradientStopColor(fillView, stopCount - 1);
    }

    public static void InsertFillGradientStop(ref FillComponent.ViewProxy fillView, int insertIndex, float stopT, Color32 color)
    {
        EnsureFillGradientStopsInitialized(ref fillView);

        int stopCount = (int)fillView.GradientStopCount;
        stopCount = Math.Clamp(stopCount, 2, MaxStops);
        if (stopCount >= MaxStops)
        {
            return;
        }

        insertIndex = Math.Clamp(insertIndex, 0, stopCount);
        for (int shiftIndex = stopCount; shiftIndex > insertIndex; shiftIndex--)
        {
            SetFillGradientStopT(ref fillView, shiftIndex, GetFillGradientStopT(fillView, shiftIndex - 1));
            SetFillGradientStopColor(ref fillView, shiftIndex, GetFillGradientStopColor(fillView, shiftIndex - 1));
        }

        SetFillGradientStopT(ref fillView, insertIndex, stopT);
        SetFillGradientStopColor(ref fillView, insertIndex, color);
        fillView.GradientStopCount = (byte)(stopCount + 1);
        SyncFillGradientEndpoints(ref fillView);
    }

    public static void RemoveFillGradientStop(ref FillComponent.ViewProxy fillView, int removeIndex)
    {
        int stopCount = GetFillGradientStopCount(fillView);
        if (stopCount <= 2)
        {
            return;
        }

        removeIndex = Math.Clamp(removeIndex, 0, stopCount - 1);
        for (int shiftIndex = removeIndex; shiftIndex < stopCount - 1; shiftIndex++)
        {
            SetFillGradientStopT(ref fillView, shiftIndex, GetFillGradientStopT(fillView, shiftIndex + 1));
            SetFillGradientStopColor(ref fillView, shiftIndex, GetFillGradientStopColor(fillView, shiftIndex + 1));
        }

        fillView.GradientStopCount = (byte)(stopCount - 1);
        SyncFillGradientEndpoints(ref fillView);
    }
}

