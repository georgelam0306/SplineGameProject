namespace DerpLib.Sdf;

public readonly struct SdfStrokeDash
{
    public readonly float DashLengthPx;
    public readonly float GapLengthPx;
    public readonly float OffsetPx;
    public readonly SdfStrokeCap Cap;
    public readonly float CapSoftnessPx;

    public SdfStrokeDash(
        float dashLengthPx,
        float gapLengthPx,
        float offsetPx = 0f,
        SdfStrokeCap cap = SdfStrokeCap.Butt,
        float capSoftnessPx = 12f)
    {
        DashLengthPx = dashLengthPx;
        GapLengthPx = gapLengthPx;
        OffsetPx = offsetPx;
        Cap = cap;
        CapSoftnessPx = capSoftnessPx;
    }

    public bool Enabled => DashLengthPx > 0f;
}

