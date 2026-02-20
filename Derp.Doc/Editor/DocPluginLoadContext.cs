using System.Reflection;
using System.Runtime.Loader;
using Derp.Doc.Plugins;
using DerpLib.ImGui;

namespace Derp.Doc.Editor;

internal sealed class DocPluginLoadContext : AssemblyLoadContext
{
    private static readonly string CoreAssemblyName = typeof(IDerpDocPlugin).Assembly.GetName().Name ?? "";
    private static readonly string EditorAssemblyName = typeof(DocPluginLoadContext).Assembly.GetName().Name ?? "";
    private static readonly string EngineAssemblyName = typeof(Im).Assembly.GetName().Name ?? "";

    private readonly AssemblyDependencyResolver _dependencyResolver;

    public DocPluginLoadContext(string pluginAssemblyPath)
        : base("DerpDocPlugin:" + Path.GetFileNameWithoutExtension(pluginAssemblyPath), isCollectible: true)
    {
        _dependencyResolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsSharedAssembly(assemblyName.Name))
        {
            return null;
        }

        string? assemblyPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (!string.IsNullOrWhiteSpace(libraryPath))
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return nint.Zero;
    }

    private static bool IsSharedAssembly(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return false;
        }

        // Keep host/editor contracts and shared UI types in the default context so
        // plugin implementations match interface method signatures exactly.
        return string.Equals(assemblyName, CoreAssemblyName, StringComparison.Ordinal) ||
               string.Equals(assemblyName, EditorAssemblyName, StringComparison.Ordinal) ||
               string.Equals(assemblyName, EngineAssemblyName, StringComparison.Ordinal);
    }
}
