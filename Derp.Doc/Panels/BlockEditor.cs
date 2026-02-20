using DerpLib.ImGui.Input;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;

namespace Derp.Doc.Panels;

/// <summary>
/// Handles block-boundary logic: Enter splits/creates blocks, Backspace merges blocks.
/// </summary>
internal static class BlockEditor
{
    public static void HandleKeys(DocWorkspace workspace, DocDocument document, ImInput input)
    {
        int focusedIndex = workspace.FocusedBlockIndex;
        if (focusedIndex < 0 || focusedIndex >= document.Blocks.Count) return;

        var block = document.Blocks[focusedIndex];

        // Enter key — create new block or split
        if (input.KeyEnter && block.Type != DocBlockType.CodeBlock)
        {
            HandleEnter(workspace, document, block, focusedIndex);
        }

        // Tab / Shift-Tab — indent/outdent list items
        if (input.KeyTab && IsListType(block.Type))
        {
            HandleIndent(workspace, document, block, input.KeyShift);
        }

        // Backspace at position 0 — merge with previous
        if (input.KeyBackspace)
        {
            int caretPos = DocumentRenderer.GetCaretPos(block);
            if (caretPos == 0)
            {
                HandleBackspaceAtStart(workspace, document, block, focusedIndex);
            }
        }
    }

    private static DocBlockType GetNewBlockType(DocBlockType currentType)
    {
        // Lists and Quotes continue on Enter; Headings demote to Paragraph
        return currentType switch
        {
            DocBlockType.BulletList or DocBlockType.NumberedList or DocBlockType.CheckboxList => currentType,
            DocBlockType.Quote => DocBlockType.Quote,
            _ => DocBlockType.Paragraph,
        };
    }

    private static bool IsListType(DocBlockType type) =>
        type is DocBlockType.BulletList or DocBlockType.NumberedList or DocBlockType.CheckboxList;

    private static void HandleEnter(DocWorkspace workspace, DocDocument document,
        DocBlock block, int blockIndex)
    {
        int caretPos = DocumentRenderer.GetCaretPos(block);
        string text = DocumentRenderer.GetEditBufferText();

        // Empty list/quote item → exit to Paragraph (demote current block)
        if (text.Length == 0 && (IsListType(block.Type) || block.Type == DocBlockType.Quote))
        {
            DocumentRenderer.CommitFocusedBlock(workspace, document);
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.ChangeBlockType,
                DocumentId = document.Id,
                BlockId = block.Id,
                OldBlockType = block.Type,
                NewBlockType = DocBlockType.Paragraph,
            });
            DocumentRenderer.FocusBlock(workspace, document, blockIndex, 0);
            return;
        }

        // Commit current block text first
        DocumentRenderer.CommitFocusedBlock(workspace, document);

        DocBlockType newType = GetNewBlockType(block.Type);

        if (caretPos >= text.Length)
        {
            // At end of block — create new empty block after
            DocumentRenderer.AddNewBlockAfter(workspace, document, blockIndex, newType);
        }
        else
        {
            // In middle — split block at caret
            string textBefore = text[..caretPos];
            string textAfter = text[caretPos..];

            // Update current block to keep text before caret
            block.Text.PlainText = textBefore;
            // Adjust spans for split
            var spansForNewBlock = SplitSpans(block.Text.Spans, caretPos);

            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetBlockText,
                DocumentId = document.Id,
                BlockId = block.Id,
                OldBlockText = text,
                NewBlockText = textBefore,
            });

            // Create new block with text after caret (inherits type for lists/quotes)
            string prevOrder = block.Order;
            string nextOrder = blockIndex + 1 < document.Blocks.Count
                ? document.Blocks[blockIndex + 1].Order
                : "";
            string newOrder = string.IsNullOrEmpty(nextOrder)
                ? FractionalIndex.After(prevOrder)
                : FractionalIndex.Between(prevOrder, nextOrder);

            var newBlock = new DocBlock
            {
                Type = newType,
                Order = newOrder,
                Text = new RichText
                {
                    PlainText = textAfter,
                    Spans = spansForNewBlock,
                },
            };

            int insertIndex = blockIndex + 1;
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.AddBlock,
                DocumentId = document.Id,
                BlockIndex = insertIndex,
                BlockSnapshot = newBlock,
            });

            // Focus new block at start
            DocumentRenderer.FocusBlock(workspace, document, insertIndex, 0);
        }
    }

    private static void HandleBackspaceAtStart(DocWorkspace workspace, DocDocument document,
        DocBlock block, int blockIndex)
    {
        if (blockIndex == 0) return; // Can't merge with nothing

        string currentText = DocumentRenderer.GetEditBufferText();

        // If the block has a special type, first convert it back to Paragraph
        if (block.Type != DocBlockType.Paragraph)
        {
            DocumentRenderer.CommitFocusedBlock(workspace, document);
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.ChangeBlockType,
                DocumentId = document.Id,
                BlockId = block.Id,
                OldBlockType = block.Type,
                NewBlockType = DocBlockType.Paragraph,
            });
            DocumentRenderer.FocusBlock(workspace, document, blockIndex, 0);
            return;
        }

        if (currentText.Length == 0)
        {
            // Empty paragraph — remove it and focus previous at end
            DocumentRenderer.CommitFocusedBlock(workspace, document);

            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.RemoveBlock,
                DocumentId = document.Id,
                BlockIndex = blockIndex,
                BlockSnapshot = block.Clone(),
            });

            int prevIndex = blockIndex - 1;
            var prevBlock = document.Blocks[prevIndex];
            DocumentRenderer.FocusBlock(workspace, document, prevIndex, prevBlock.Text.PlainText.Length);
        }
        else
        {
            // Non-empty — merge text into previous block
            DocumentRenderer.CommitFocusedBlock(workspace, document);

            var prevBlock = document.Blocks[blockIndex - 1];
            int joinPoint = prevBlock.Text.PlainText.Length;
            string mergedText = prevBlock.Text.PlainText + currentText;

            // Update previous block text
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetBlockText,
                DocumentId = document.Id,
                BlockId = prevBlock.Id,
                OldBlockText = prevBlock.Text.PlainText,
                NewBlockText = mergedText,
            });

            // Remove current block
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.RemoveBlock,
                DocumentId = document.Id,
                BlockIndex = blockIndex,
                BlockSnapshot = block.Clone(),
            });

            // Focus previous block at join point
            DocumentRenderer.FocusBlock(workspace, document, blockIndex - 1, joinPoint);
        }
    }

    private static void HandleIndent(DocWorkspace workspace, DocDocument document, DocBlock block, bool outdent)
    {
        const int MaxIndent = 3;
        int newLevel = outdent
            ? Math.Max(0, block.IndentLevel - 1)
            : Math.Min(MaxIndent, block.IndentLevel + 1);

        if (newLevel == block.IndentLevel) return;

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetBlockIndent,
            DocumentId = document.Id,
            BlockId = block.Id,
            OldIndentLevel = block.IndentLevel,
            NewIndentLevel = newLevel,
        });
    }

    /// <summary>
    /// Splits spans at a caret position. Returns spans for the text after the caret,
    /// with offsets adjusted to start from 0.
    /// </summary>
    private static List<RichSpan> SplitSpans(List<RichSpan> spans, int splitPos)
    {
        var result = new List<RichSpan>();
        foreach (var span in spans)
        {
            if (span.End <= splitPos) continue; // Entirely before split

            int newStart = Math.Max(0, span.Start - splitPos);
            int newEnd = span.End - splitPos;
            if (newEnd > 0)
            {
                result.Add(new RichSpan
                {
                    Start = newStart,
                    Length = newEnd - newStart,
                    Style = span.Style,
                });
            }
        }
        return result;
    }
}
