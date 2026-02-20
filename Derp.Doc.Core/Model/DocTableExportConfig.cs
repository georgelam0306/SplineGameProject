namespace Derp.Doc.Model;

public sealed class DocTableExportConfig
{
    public bool Enabled { get; set; }
    public string Namespace { get; set; } = "";
    public string StructName { get; set; } = "";

    public DocTableExportConfig Clone()
    {
        return new DocTableExportConfig
        {
            Enabled = Enabled,
            Namespace = Namespace,
            StructName = StructName,
        };
    }
}
