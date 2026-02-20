using System.Text;

namespace Derp.Doc.Tables;

internal static class DocumentFormulaSyntax
{
    public static bool StartsWithFormulaShortcut(string text)
    {
        return text.StartsWith("=(", StringComparison.Ordinal);
    }

    public static bool TryParseVariableDeclaration(
        string text,
        out string variableName,
        out bool hasExpression,
        out string expression)
    {
        variableName = "";
        hasExpression = false;
        expression = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> span = text.AsSpan().Trim();
        if (span.Length < 2 || span[0] != '@')
        {
            return false;
        }

        int nameStart = 1;
        if (!IsIdentifierStart(span[nameStart]))
        {
            return false;
        }

        int index = nameStart + 1;
        while (index < span.Length && IsIdentifierPart(span[index]))
        {
            index++;
        }

        variableName = span[nameStart..index].ToString();

        while (index < span.Length && char.IsWhiteSpace(span[index]))
        {
            index++;
        }

        if (index >= span.Length)
        {
            hasExpression = false;
            expression = "";
            return true;
        }

        if (span[index] != '=')
        {
            return false;
        }

        index++;
        while (index < span.Length && char.IsWhiteSpace(span[index]))
        {
            index++;
        }

        hasExpression = true;
        expression = index < span.Length ? span[index..].ToString() : "";
        return true;
    }

    public static string NormalizeDocumentAlias(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "doc";
        }

        ReadOnlySpan<char> span = rawName.AsSpan().Trim();
        var builder = new StringBuilder(span.Length);
        bool previousUnderscore = false;

        for (int index = 0; index < span.Length; index++)
        {
            char value = span[index];
            if (IsIdentifierPart(value))
            {
                builder.Append(value);
                previousUnderscore = false;
                continue;
            }

            if (builder.Length == 0 || previousUnderscore)
            {
                continue;
            }

            builder.Append('_');
            previousUnderscore = true;
        }

        string alias = builder.ToString().Trim('_');
        if (alias.Length == 0)
        {
            return "doc";
        }

        if (!IsIdentifierStart(alias[0]))
        {
            alias = "_" + alias;
        }

        return alias;
    }

    public static bool IsValidIdentifier(ReadOnlySpan<char> identifier)
    {
        if (identifier.Length == 0 || !IsIdentifierStart(identifier[0]))
        {
            return false;
        }

        for (int charIndex = 1; charIndex < identifier.Length; charIndex++)
        {
            if (!IsIdentifierPart(identifier[charIndex]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }
}
