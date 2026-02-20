// SPDX-License-Identifier: MIT
#nullable enable
using System.Globalization;
using System.Text;

namespace Property.Generator
{
    internal static class NameFormatter
    {
        public static string ToDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            var builder = new StringBuilder(name.Length + 8);
            char previous = '\0';
            for (int index = 0; index < name.Length; index++)
            {
                char current = name[index];
                if (current == '_' || current == '-')
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
                    {
                        builder.Append(' ');
                    }
                    previous = current;
                    continue;
                }

                bool isUpper = char.IsUpper(current);
                bool isLower = char.IsLower(current);
                bool isDigit = char.IsDigit(current);

                if (index > 0)
                {
                    if (isUpper && (char.IsLower(previous) || char.IsDigit(previous)))
                    {
                        builder.Append(' ');
                    }
                    else if (isDigit && char.IsLetter(previous))
                    {
                        builder.Append(' ');
                    }
                    else if (char.IsLetter(current) && char.IsDigit(previous))
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append(current);
                previous = current;
            }

            return builder.ToString();
        }

        public static string ToIdentifierSegment(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Value";
            }

            var builder = new StringBuilder(name.Length);
            bool upperNext = true;
            for (int index = 0; index < name.Length; index++)
            {
                char current = name[index];
                if (current == '_' || current == '-' || current == ' ')
                {
                    upperNext = true;
                    continue;
                }

                if (upperNext)
                {
                    builder.Append(char.ToUpper(current, CultureInfo.InvariantCulture));
                    upperNext = false;
                }
                else
                {
                    builder.Append(current);
                }
            }

            return builder.Length == 0 ? "Value" : builder.ToString();
        }

        public static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            if (name.Length == 1)
            {
                return name.ToLowerInvariant();
            }

            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }
    }
}
