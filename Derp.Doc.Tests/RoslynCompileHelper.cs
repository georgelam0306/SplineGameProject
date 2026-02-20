using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Derp.Doc.Tests;

internal static class RoslynCompileHelper
{
    public static Assembly CompileToAssembly(string assemblyName, IReadOnlyList<string> sources, IEnumerable<MetadataReference> extraReferences)
    {
        var syntaxTrees = new SyntaxTree[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            syntaxTrees[i] = CSharpSyntaxTree.ParseText(sources[i]);
        }

        var references = new List<MetadataReference>();

        string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(tpa))
        {
            var paths = tpa.Split(Path.PathSeparator);
            for (int i = 0; i < paths.Length; i++)
            {
                references.Add(MetadataReference.CreateFromFile(paths[i]));
            }
        }

        foreach (var reference in extraReferences)
        {
            references.Add(reference);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        if (!emitResult.Success)
        {
            var messages = new List<string>(64);
            foreach (var diagnostic in emitResult.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    messages.Add(diagnostic.ToString());
                }
            }

            throw new InvalidOperationException("Roslyn compilation failed:\n" + string.Join("\n", messages));
        }

        peStream.Position = 0;
        return Assembly.Load(peStream.ToArray());
    }
}

