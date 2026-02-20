namespace Core.Input;

internal struct DerivedTrigger
{
    public StringHandle SourceActionName;
    public int SourceActionIndex;
    public DerivedTriggerKind Kind;
    public DerivedTriggerMode Mode;
    public float Threshold;
    public float ThresholdSq;
}

