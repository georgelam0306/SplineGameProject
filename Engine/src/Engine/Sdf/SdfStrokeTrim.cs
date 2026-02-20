namespace DerpLib.Sdf;

public readonly struct SdfStrokeTrim
{
    public readonly float StartPx;
    public readonly float LengthPx;
    public readonly float OffsetPx;
    public readonly SdfStrokeCap Cap;
    public readonly float CapSoftnessPx;

    public SdfStrokeTrim(
        float startPx,
        float lengthPx,
        float offsetPx = 0f,
        SdfStrokeCap cap = SdfStrokeCap.Butt,
        float capSoftnessPx = 12f)
    {
        StartPx = startPx;
        LengthPx = lengthPx;
        OffsetPx = offsetPx;
        Cap = cap;
        CapSoftnessPx = capSoftnessPx;
    }

    public bool Enabled => LengthPx > 0f;
}

