using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Input;

namespace Derp.UI;

internal static class EntityReferenceDragDrop
{
    private static uint _stableId;
    private static Vector2 _startMousePos;
    private static bool _thresholdMet;
    private static int _releaseFrame;

    public static bool HasActiveDrag => _stableId != 0;
    public static bool IsDragging => _stableId != 0 && _thresholdMet;
    public static bool ThresholdMet => _thresholdMet;
    public static uint StableId => _stableId;
    public static int ReleaseFrame => _releaseFrame;

    public static void BeginEntityDrag(uint stableId, Vector2 mousePos)
    {
        if (stableId == 0)
        {
            return;
        }

        _stableId = stableId;
        _startMousePos = mousePos;
        _thresholdMet = false;
        _releaseFrame = 0;
    }

    public static void Update(ImInput input, int frameCount)
    {
        if (_stableId == 0)
        {
            return;
        }

        if (_releaseFrame != 0)
        {
            if (frameCount > _releaseFrame)
            {
                Clear();
            }
            return;
        }

        if (!_thresholdMet)
        {
            Vector2 delta = Im.MousePos - _startMousePos;
            if (delta.LengthSquared() >= 8f * 8f)
            {
                _thresholdMet = true;
            }
        }

        if (input.MouseReleased)
        {
            _releaseFrame = frameCount;
            return;
        }

        if (!input.MouseDown)
        {
            Clear();
        }
    }

    public static void Clear()
    {
        _stableId = 0;
        _startMousePos = default;
        _thresholdMet = false;
        _releaseFrame = 0;
    }
}

