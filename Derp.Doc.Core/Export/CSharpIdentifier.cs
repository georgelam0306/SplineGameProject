using System.Text;

namespace Derp.Doc.Export;

internal static class CSharpIdentifier
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue",
        "decimal","default","delegate","do","double","else","enum","event","explicit","extern","false","finally",
        "fixed","float","for","foreach","goto","if","implicit","in","int","interface","internal","is","lock",
        "long","namespace","new","null","object","operator","out","override","params","private","protected",
        "public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static","string",
        "struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort",
        "using","virtual","void","volatile","while",
    };

    public static string ToPascalCase(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var sb = new StringBuilder(text.Length);
        bool upperNext = true;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (!char.IsLetterOrDigit(ch))
            {
                upperNext = true;
                continue;
            }

            if (upperNext)
            {
                sb.Append(char.ToUpperInvariant(ch));
                upperNext = false;
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    public static string Sanitize(string name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char ch = name[i];
            if (i == 0)
            {
                if (char.IsLetter(ch) || ch == '_')
                {
                    sb.Append(ch);
                }
                else if (char.IsDigit(ch))
                {
                    sb.Append('_');
                    sb.Append(ch);
                }
                else
                {
                    sb.Append('_');
                }
            }
            else
            {
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }
        }

        var sanitized = sb.ToString();
        if (Keywords.Contains(sanitized))
        {
            return "_" + sanitized;
        }

        return sanitized;
    }
}

