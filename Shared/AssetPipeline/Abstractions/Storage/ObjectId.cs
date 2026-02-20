namespace DerpLib.AssetPipeline;

public readonly record struct ObjectId(string Value)
{
    public override string ToString() => Value;
}
