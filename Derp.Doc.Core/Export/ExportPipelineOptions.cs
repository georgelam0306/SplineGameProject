namespace Derp.Doc.Export;

public sealed class ExportPipelineOptions
{
    public string DefaultNamespace { get; set; } = "DerpDocDatabase";
    public string GeneratedOutputDirectory { get; set; } = "";
    public string BinaryOutputPath { get; set; } = "";
    public string LiveBinaryOutputPath { get; set; } = "";

    public bool WriteManifest { get; set; } = true;
    public bool WriteDebugJson { get; set; }
}
