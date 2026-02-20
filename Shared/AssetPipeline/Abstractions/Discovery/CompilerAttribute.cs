namespace DerpLib.AssetPipeline;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CompilerAttribute : Attribute
{
    public Type AssetType { get; }
    public CompilerAttribute(Type assetType) => AssetType = assetType;
}
