using System.Numerics;

namespace Derp.UI;

internal static class AnimationsLibraryDragDrop
{
    public enum DragKind : byte
    {
        None = 0,
        Timeline = 1
    }

    private static DragKind _kind;
    private static int _timelineId;
    private static Vector2 _startMousePos;
    private static bool _thresholdMet;
    private static int _releaseFrame;

    public static bool HasActiveDrag => _kind != DragKind.None;
    public static bool IsDragging => _kind != DragKind.None && _thresholdMet;
    public static bool ThresholdMet => _thresholdMet;
    public static DragKind Kind => _kind;
    public static int TimelineId => _timelineId;
    public static int ReleaseFrame => _releaseFrame;

    public static void BeginTimelineDrag(int timelineId, Vector2 mousePos)
    {
        _kind = DragKind.Timeline;
        _timelineId = timelineId;
        _startMousePos = mousePos;
        _thresholdMet = false;
        _releaseFrame = 0;
    }

    public static void Update(DerpLib.ImGui.Input.ImInput input, int frameCount)
    {
        if (_kind == DragKind.None)
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
            Vector2 delta = DerpLib.ImGui.Im.MousePos - _startMousePos;
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
        _kind = DragKind.None;
        _timelineId = 0;
        _startMousePos = default;
        _thresholdMet = false;
        _releaseFrame = 0;
    }
}

