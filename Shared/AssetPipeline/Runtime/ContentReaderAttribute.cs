namespace DerpLib.AssetPipeline;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ContentReaderAttribute : Attribute
{
    public Type RuntimeType { get; }
    public ContentReaderAttribute(Type runtimeType) => RuntimeType = runtimeType;
}
