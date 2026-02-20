using System.Text.Json;
using Derp.Doc.Model;

namespace Derp.Doc.Storage;

/// <summary>
/// Saves a DocDocument to the docs/ directory:
///   {fileName}.meta.json
///   {fileName}.blocks.jsonl
/// </summary>
internal static class DocumentSerializer
{
    public static void Save(DocDocument document, string docsDir)
    {
        Directory.CreateDirectory(docsDir);

        // Write meta.json
        var metaDto = new DocumentMetaDto
        {
            Id = document.Id,
            Title = document.Title,
        };

        var metaJson = JsonSerializer.Serialize(metaDto, DocJsonContext.Default.DocumentMetaDto);
        File.WriteAllText(Path.Combine(docsDir, $"{document.FileName}.meta.json"), metaJson);

        // Write blocks.jsonl (one block per line, ordered by Order)
        var sortedBlocks = document.Blocks.OrderBy(b => b.Order, StringComparer.Ordinal).ToList();
        var blocksPath = Path.Combine(docsDir, $"{document.FileName}.blocks.jsonl");
        using var writer = new StreamWriter(blocksPath);

        foreach (var block in sortedBlocks)
        {
            var spanDtos = block.Text.Spans.Select(s => new RichSpanDto
            {
                S = s.Start,
                L = s.Length,
                St = (int)s.Style,
            }).ToList();

            var blockDto = new BlockDto
            {
                Id = block.Id,
                Order = block.Order,
                Type = block.Type.ToString(),
                Text = block.Text.PlainText,
                Spans = spanDtos,
                Indent = block.IndentLevel,
                Checked = block.Checked,
                Lang = block.Language,
                TableId = string.IsNullOrWhiteSpace(block.TableId) ? null : block.TableId,
                TableVariantId = block.TableVariantId,
                ViewId = string.IsNullOrWhiteSpace(block.ViewId) ? null : block.ViewId,
                EmbeddedWidth = block.EmbeddedWidth,
                EmbeddedHeight = block.EmbeddedHeight,
                TableVariableOverrides = block.TableVariableOverrides.Count > 0
                    ? block.TableVariableOverrides.Select(variableOverride => new BlockTableVariableOverrideDto
                    {
                        VariableId = variableOverride.VariableId,
                        Expression = string.IsNullOrWhiteSpace(variableOverride.Expression)
                            ? null
                            : variableOverride.Expression,
                    }).ToList()
                    : null,
            };

            var line = JsonSerializer.Serialize(blockDto, DocJsonCompactContext.Default.BlockDto);
            writer.WriteLine(line);
        }
    }
}
