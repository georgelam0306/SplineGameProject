using System.Text.Json;
using Derp.Doc.Model;

namespace Derp.Doc.Storage;

/// <summary>
/// Loads a DocDocument from a docs/ directory.
/// </summary>
internal static class DocumentLoader
{
    public static DocDocument Load(DocumentRefDto docRef, string docsDir)
    {
        // Load meta.json
        var metaPath = Path.Combine(docsDir, $"{docRef.FileName}.meta.json");
        var metaJson = File.ReadAllText(metaPath);
        var metaDto = JsonSerializer.Deserialize(metaJson, DocJsonContext.Default.DocumentMetaDto)
            ?? throw new InvalidOperationException($"Failed to parse {metaPath}");

        var document = new DocDocument
        {
            Id = metaDto.Id,
            Title = metaDto.Title,
            FolderId = docRef.FolderId,
            FileName = docRef.FileName,
        };

        // Load blocks.jsonl
        var blocksPath = Path.Combine(docsDir, $"{docRef.FileName}.blocks.jsonl");
        if (File.Exists(blocksPath))
        {
            foreach (var line in File.ReadLines(blocksPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var blockDto = JsonSerializer.Deserialize(line, DocJsonCompactContext.Default.BlockDto);
                if (blockDto == null) continue;

                var block = new DocBlock
                {
                    Id = blockDto.Id,
                    Order = blockDto.Order,
                    Type = Enum.TryParse<DocBlockType>(blockDto.Type, out var type) ? type : DocBlockType.Paragraph,
                    IndentLevel = blockDto.Indent,
                    Checked = blockDto.Checked,
                    Language = blockDto.Lang,
                    TableId = blockDto.TableId ?? "",
                    TableVariantId = blockDto.TableVariantId,
                    ViewId = blockDto.ViewId ?? "",
                    EmbeddedWidth = blockDto.EmbeddedWidth,
                    EmbeddedHeight = blockDto.EmbeddedHeight,
                };

                if (blockDto.TableVariableOverrides != null)
                {
                    for (int overrideIndex = 0; overrideIndex < blockDto.TableVariableOverrides.Count; overrideIndex++)
                    {
                        var variableOverrideDto = blockDto.TableVariableOverrides[overrideIndex];
                        if (variableOverrideDto == null ||
                            string.IsNullOrWhiteSpace(variableOverrideDto.VariableId))
                        {
                            continue;
                        }

                        block.TableVariableOverrides.Add(new DocBlockTableVariableOverride
                        {
                            VariableId = variableOverrideDto.VariableId,
                            Expression = variableOverrideDto.Expression ?? "",
                        });
                    }
                }

                block.Text.PlainText = blockDto.Text;
                if (blockDto.Spans != null)
                {
                    foreach (var spanDto in blockDto.Spans)
                    {
                        block.Text.Spans.Add(new RichSpan
                        {
                            Start = spanDto.S,
                            Length = spanDto.L,
                            Style = (RichSpanStyle)spanDto.St,
                        });
                    }
                }

                document.Blocks.Add(block);
            }
        }

        NormalizeBlockOrders(document);
        return document;
    }

    private static void NormalizeBlockOrders(DocDocument document)
    {
        string nextOrder = FractionalIndex.Initial();
        for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            document.Blocks[blockIndex].Order = nextOrder;
            nextOrder = FractionalIndex.After(nextOrder);
        }
    }
}
