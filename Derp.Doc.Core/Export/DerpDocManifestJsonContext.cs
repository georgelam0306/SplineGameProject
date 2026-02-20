using System.Text.Json.Serialization;

namespace Derp.Doc.Export;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DerpDocManifest))]
internal partial class DerpDocManifestJsonContext : JsonSerializerContext
{
}

