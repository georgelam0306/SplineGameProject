using System;
using Derp.Doc.Commands;
using Derp.Doc.Editor;
using Derp.Doc.Model;
using Derp.Doc.Tables;

namespace Derp.Doc.Panels;

/// <summary>
/// Detects markdown-style shortcuts in the text buffer and converts block types.
/// E.g., typing "# " at the start of a Paragraph converts it to Heading1.
/// </summary>
internal static class MarkdownShortcuts
{
    private static string _lastCheckedText = "";

    public static void Check(DocWorkspace workspace, DocDocument document)
    {
        if (workspace.FocusedBlockIndex < 0 || workspace.FocusedBlockIndex >= document.Blocks.Count)
            return;

        var block = document.Blocks[workspace.FocusedBlockIndex];

        string text = DocumentRenderer.GetEditBufferText();

        // Avoid re-checking the same text
        if (text == _lastCheckedText) return;
        _lastCheckedText = text;

        // ``` toggle: CodeBlock → Paragraph
        if (block.Type == DocBlockType.CodeBlock && text == "```")
        {
            ApplyBlockConversion(workspace, document, block, DocBlockType.Paragraph, 3);
            return;
        }

        // All other shortcuts only apply to Paragraph blocks
        if (block.Type != DocBlockType.Paragraph) return;

        if (DocumentFormulaSyntax.TryParseVariableDeclaration(
                text,
                out string variableName,
                out bool hasExpression,
                out _))
        {
            string variableText = text.Trim();
            if (TryFindExistingVariableBlockByName(document, variableName, block.Id, out int existingBlockIndex, out var existingBlock))
            {
                ReuseExistingVariableBlock(
                    workspace,
                    document,
                    block,
                    variableText,
                    hasExpression,
                    existingBlockIndex,
                    existingBlock);
                return;
            }

            ApplyBlockConversionWithText(
                workspace,
                document,
                block,
                DocBlockType.Variable,
                variableText,
                variableText.Length);
            return;
        }

        if (TryExtractFormulaShortcutExpression(text, out string formulaText))
        {
            ApplyBlockConversionWithText(
                workspace,
                document,
                block,
                DocBlockType.Formula,
                formulaText,
                formulaText.Length);
            return;
        }

        // Check block-level shortcuts (prefix patterns)
        var (matchType, prefixLen) = MatchBlockPrefix(text);
        if (matchType != null)
        {
            ApplyBlockConversion(workspace, document, block, matchType.Value, prefixLen);
            return;
        }

        // Divider shortcut
        if (text is "---" or "***" or "___")
        {
            ApplyBlockConversion(workspace, document, block, DocBlockType.Divider, text.Length);
        }
    }

    private static bool TryFindExistingVariableBlockByName(
        DocDocument document,
        string variableName,
        string excludeBlockId,
        out int blockIndex,
        out DocBlock block)
    {
        blockIndex = -1;
        block = null!;
        for (int candidateBlockIndex = 0; candidateBlockIndex < document.Blocks.Count; candidateBlockIndex++)
        {
            var candidateBlock = document.Blocks[candidateBlockIndex];
            if (candidateBlock.Type != DocBlockType.Variable ||
                string.Equals(candidateBlock.Id, excludeBlockId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!DocumentFormulaSyntax.TryParseVariableDeclaration(
                    candidateBlock.Text.PlainText,
                    out string candidateVariableName,
                    out _,
                    out _))
            {
                continue;
            }

            if (!string.Equals(candidateVariableName, variableName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            blockIndex = candidateBlockIndex;
            block = candidateBlock;
            return true;
        }

        return false;
    }

    private static void ReuseExistingVariableBlock(
        DocWorkspace workspace,
        DocDocument document,
        DocBlock focusedBlock,
        string declarationText,
        bool shouldUpdateExistingValue,
        int existingBlockIndex,
        DocBlock existingBlock)
    {
        DocumentRenderer.CommitFocusedBlock(workspace, document);

        if (shouldUpdateExistingValue &&
            !string.Equals(existingBlock.Text.PlainText, declarationText, StringComparison.Ordinal))
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetBlockText,
                DocumentId = document.Id,
                BlockId = existingBlock.Id,
                OldBlockText = existingBlock.Text.PlainText,
                NewBlockText = declarationText,
            });
        }

        if (!string.IsNullOrEmpty(focusedBlock.Text.PlainText))
        {
            workspace.ExecuteCommand(new DocCommand
            {
                Kind = DocCommandKind.SetBlockText,
                DocumentId = document.Id,
                BlockId = focusedBlock.Id,
                OldBlockText = focusedBlock.Text.PlainText,
                NewBlockText = "",
            });
        }

        int caretPos = shouldUpdateExistingValue
            ? declarationText.Length
            : existingBlock.Text.PlainText.Length;
        DocumentRenderer.FocusBlock(workspace, document, existingBlockIndex, caretPos);
        _lastCheckedText = "";
    }

    private static bool TryExtractFormulaShortcutExpression(string text, out string formulaText)
    {
        formulaText = "";
        if (!DocumentFormulaSyntax.StartsWithFormulaShortcut(text))
        {
            return false;
        }

        ReadOnlySpan<char> expressionSpan = text.AsSpan(2);
        if (expressionSpan.Length > 0 && expressionSpan[^1] == ')')
        {
            expressionSpan = expressionSpan[..^1];
        }

        formulaText = expressionSpan.ToString();
        return true;
    }

    private static (DocBlockType? type, int prefixLen) MatchBlockPrefix(string text)
    {
        if (text.StartsWith("###### ")) return (DocBlockType.Heading6, 7);
        if (text.StartsWith("##### ")) return (DocBlockType.Heading5, 6);
        if (text.StartsWith("#### ")) return (DocBlockType.Heading4, 5);
        if (text.StartsWith("### ")) return (DocBlockType.Heading3, 4);
        if (text.StartsWith("## ")) return (DocBlockType.Heading2, 3);
        if (text.StartsWith("# ")) return (DocBlockType.Heading1, 2);
        if (text.StartsWith("- ") || text.StartsWith("* ")) return (DocBlockType.BulletList, 2);
        if (text.StartsWith("> ")) return (DocBlockType.Quote, 2);
        if (text.StartsWith("[] ")) return (DocBlockType.CheckboxList, 3);
        if (text.StartsWith("```")) return (DocBlockType.CodeBlock, 3);

        // Numbered list: "1. ", "2. ", etc.
        if (text.Length >= 3)
        {
            int i = 0;
            while (i < text.Length && char.IsDigit(text[i])) i++;
            if (i > 0 && i < text.Length - 1 && text[i] == '.' && text[i + 1] == ' ')
                return (DocBlockType.NumberedList, i + 2);
        }

        return (null, 0);
    }

    private static void ApplyBlockConversion(DocWorkspace workspace, DocDocument document,
        DocBlock block, DocBlockType newType, int prefixLen)
    {
        string remainingText = block.Text.PlainText.Length > prefixLen
            ? block.Text.PlainText[prefixLen..]
            : "";
        ApplyBlockConversionWithText(workspace, document, block, newType, remainingText, 0);
    }

    private static void ApplyBlockConversionWithText(
        DocWorkspace workspace,
        DocDocument document,
        DocBlock block,
        DocBlockType newType,
        string newText,
        int caretPos)
    {
        // Commit current text first
        DocumentRenderer.CommitFocusedBlock(workspace, document);

        // Change block type
        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.ChangeBlockType,
            DocumentId = document.Id,
            BlockId = block.Id,
            OldBlockType = block.Type,
            NewBlockType = newType,
        });

        workspace.ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetBlockText,
            DocumentId = document.Id,
            BlockId = block.Id,
            OldBlockText = block.Text.PlainText,
            NewBlockText = newText,
        });

        DocumentRenderer.FocusBlock(workspace, document, workspace.FocusedBlockIndex, Math.Max(0, caretPos));
        _lastCheckedText = newText;
    }
}
