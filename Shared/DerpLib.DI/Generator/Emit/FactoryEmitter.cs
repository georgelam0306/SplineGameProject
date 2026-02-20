// SPDX-License-Identifier: MIT
#nullable enable
using DerpLib.DI.Generator.Model;
using System.Collections.Generic;
using System.Text;

namespace DerpLib.DI.Generator.Emit
{
    /// <summary>
    /// Emits the nested Factory class for child compositions.
    /// </summary>
    internal static class FactoryEmitter
    {
        /// <summary>
        /// Emits the Factory nested class.
        /// </summary>
        public static void Emit(StringBuilder sb, CompositionModel composition, string indent)
        {
            if (composition.Parent is null)
                return;

            sb.AppendLine($"{indent}//==========================================================================");
            sb.AppendLine($"{indent}// Nested Factory Class");
            sb.AppendLine($"{indent}//==========================================================================");
            sb.AppendLine();

            sb.AppendLine($"{indent}/// <summary>");
            sb.AppendLine($"{indent}/// Factory for creating <see cref=\"{composition.ClassName}\"/> scopes.");
            sb.AppendLine($"{indent}/// Inject this into services that need to create {composition.ClassName.ToLowerInvariant()} instances.");
            sb.AppendLine($"{indent}/// </summary>");
            sb.AppendLine($"{indent}public sealed class Factory");
            sb.AppendLine($"{indent}{{");

            var memberIndent = indent + "    ";

            // Parent reference
            sb.AppendLine($"{memberIndent}private readonly {composition.Parent.FullTypeName} _parent;");
            sb.AppendLine();

            // Constructor
            sb.AppendLine($"{memberIndent}internal Factory({composition.Parent.FullTypeName} parent)");
            sb.AppendLine($"{memberIndent}{{");
            sb.AppendLine($"{memberIndent}    _parent = parent;");
            sb.AppendLine($"{memberIndent}}}");
            sb.AppendLine();

            // Create method
            EmitCreateMethod(sb, composition, memberIndent);

            sb.AppendLine($"{indent}}}");
        }

        private static void EmitCreateMethod(StringBuilder sb, CompositionModel composition, string indent)
        {
            // Build XML documentation
            sb.AppendLine($"{indent}/// <summary>Creates a new {composition.ClassName.ToLowerInvariant()} scope.</summary>");
            foreach (var arg in composition.Args)
            {
                sb.AppendLine($"{indent}/// <param name=\"{arg.Name}\">The {FormatParamName(arg.Name)} for this scope.</param>");
            }
            sb.AppendLine($"{indent}/// <returns>A new <see cref=\"{composition.ClassName}\"/> that must be disposed when no longer needed.</returns>");

            // Build parameter list
            var parameters = new List<string>();
            foreach (var arg in composition.Args)
            {
                parameters.Add($"{arg.FullTypeName} {arg.Name}");
            }

            // Build argument list for constructor call
            var args = new List<string> { "_parent" };
            foreach (var arg in composition.Args)
            {
                args.Add(arg.Name);
            }

            sb.Append($"{indent}public {composition.FullTypeName} Create(");

            if (parameters.Count <= 2)
            {
                sb.AppendLine($"{string.Join(", ", parameters)})");
            }
            else
            {
                sb.AppendLine();
                for (int i = 0; i < parameters.Count; i++)
                {
                    var comma = i < parameters.Count - 1 ? "," : ")";
                    sb.AppendLine($"{indent}    {parameters[i]}{comma}");
                }
            }

            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    return new {composition.FullTypeName}({string.Join(", ", args)});");
            sb.AppendLine($"{indent}}}");
        }

        private static string FormatParamName(string name)
        {
            // Convert camelCase to "camel case" for documentation
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (i > 0 && char.IsUpper(c))
                {
                    sb.Append(' ');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
