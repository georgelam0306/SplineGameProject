namespace DerpLib.AssetPipeline;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ImporterAttribute : Attribute
{
    public string Extension { get; }
    public ImporterAttribute(string extension) => Extension = extension;
}
