using System.Text.Json.Serialization;

namespace DerpTech.Cli.Models;

/// <summary>
/// Project configuration for tooling and build metadata.
/// Generated at Games/{ProjectName}/ProjectConfig.json
/// </summary>
public sealed class ProjectConfig
{
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = "./project-config.schema.json";

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("namespacePrefix")]
    public string NamespacePrefix { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("createdDate")]
    public DateTime CreatedDate { get; set; }

    [JsonPropertyName("templateVersion")]
    public string TemplateVersion { get; set; } = "1.0.0";
}
