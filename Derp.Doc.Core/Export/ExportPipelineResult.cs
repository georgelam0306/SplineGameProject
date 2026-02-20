namespace Derp.Doc.Export;

public sealed class ExportPipelineResult
{
    public List<ExportDiagnostic> Diagnostics { get; } = new();
    public List<GeneratedFile> GeneratedFiles { get; } = new();
    public byte[] Binary { get; set; } = Array.Empty<byte>();

    public bool HasErrors
    {
        get
        {
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                if (Diagnostics[i].Severity == ExportDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

