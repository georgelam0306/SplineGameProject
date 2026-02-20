using System.Text.Json;

namespace Derp.Doc.Mcp;

public sealed class DerpDocMcpToolDefinition
{
    public DerpDocMcpToolDefinition(string name, string title, string description, string inputSchemaJson, string? outputSchemaJson)
    {
        Name = name;
        Title = title;
        Description = description;
        InputSchemaJson = inputSchemaJson;
        OutputSchemaJson = outputSchemaJson;
    }

    public string Name { get; }
    public string Title { get; }
    public string Description { get; }
    public string InputSchemaJson { get; }
    public string? OutputSchemaJson { get; }

    internal void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("name", Name);
        if (!string.IsNullOrWhiteSpace(Title))
        {
            writer.WriteString("title", Title);
        }
        writer.WriteString("description", Description);
        writer.WritePropertyName("inputSchema");
        using (var schema = JsonDocument.Parse(InputSchemaJson))
        {
            schema.RootElement.WriteTo(writer);
        }

        if (!string.IsNullOrWhiteSpace(OutputSchemaJson))
        {
            writer.WritePropertyName("outputSchema");
            using var outSchema = JsonDocument.Parse(OutputSchemaJson);
            outSchema.RootElement.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}

