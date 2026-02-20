using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Serilog;

namespace DerpLib.AssetPipeline;

/// <summary>
/// Build-time asset discovery using reflection.
/// NOT AOT-compatible - use only during asset compilation, not at runtime.
/// </summary>
[RequiresUnreferencedCode("Asset discovery uses reflection and dynamic assembly loading. Use only during build, not at runtime.")]
public sealed class DefaultAssetDiscovery : IAssetDiscovery
{
    public void DiscoverInto(IAssetRegistry registry, DiscoveryOptions options, ILogger? logger = null)
    {
        var assemblies = new List<Assembly>(AppDomain.CurrentDomain.GetAssemblies());
        var pluginAssemblies = new HashSet<Assembly>();
        foreach (var dir in options.PluginDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var dll in Directory.GetFiles(dir, "*.dll"))
            {
                try
                {
                    var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dll));
                    assemblies.Add(asm);
                    pluginAssemblies.Add(asm);
                    logger?.Information("[discover] Loaded plugin {Dll}", dll);
                }
                catch (Exception ex)
                {
                    logger?.Warning(ex, "[discover] Failed to load {Dll}", dll);
                }
            }
        }

        foreach (var asm in assemblies.Distinct())
        {
            foreach (var type in SafeGetTypes(asm))
            {
                if (type.IsAbstract || type.IsInterface) continue;
                try
                {
                    foreach (var imp in type.GetCustomAttributes<ImporterAttribute>())
                    {
                        if (typeof(IAssetImporter).IsAssignableFrom(type))
                        {
                            var instance = (IAssetImporter?)Activator.CreateInstance(type);
                            if (instance != null)
                            {
                                var fromPlugin = pluginAssemblies.Contains(type.Assembly);
                                TryRegisterImporter(registry, imp.Extension, instance, fromPlugin, options, logger);
                            }
                        }
                    }

                    foreach (var compAttr in type.GetCustomAttributes<CompilerAttribute>())
                    {
                        if (typeof(IAssetCompiler).IsAssignableFrom(type))
                        {
                            var instance = (IAssetCompiler?)Activator.CreateInstance(type);
                            if (instance != null)
                            {
                                var fromPlugin = pluginAssemblies.Contains(type.Assembly);
                                TryRegisterCompiler(registry, compAttr.AssetType, instance, fromPlugin, options, logger);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.Warning(ex, "[discover] Error inspecting {Type}", type.FullName);
                }
            }
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch { return Array.Empty<Type>(); }
    }

    private static void TryRegisterImporter(IAssetRegistry registry, string ext, IAssetImporter importer, bool fromPlugin, DiscoveryOptions options, ILogger? logger)
    {
        try
        {
            if (registry.TryGetImporter(ext, out var existing))
            {
                var existingFromPlugin = existing.GetType().Assembly != null && existing.GetType().Assembly.Location.Length > 0 && options.PluginDirectories.Any(d => SafeIsParentOf(d, existing.GetType().Assembly.Location));
                if (fromPlugin != existingFromPlugin)
                {
                    var preferPlugin = options.PreferPlugin;
                    var shouldOverride = (fromPlugin && preferPlugin) || (!fromPlugin && !preferPlugin);
                    if (shouldOverride)
                    {
                        registry.RegisterImporter(ext, importer);
                        logger?.Warning("[discover] Importer conflict for {Extension}: {New} replaced {Old} ({Preferred} preferred)", ext, importer.GetType().Name, existing.GetType().Name, preferPlugin ? "plugin" : "built-in");
                    }
                    else
                    {
                        logger?.Warning("[discover] Importer conflict for {Extension}: kept {Old}, ignored {New} ({Preferred} preferred)", ext, existing.GetType().Name, importer.GetType().Name, preferPlugin ? "plugin" : "built-in");
                    }
                }
                else
                {
                    logger?.Warning("[discover] Duplicate importer for {Extension}: kept {Old}, ignored {New}", ext, existing.GetType().Name, importer.GetType().Name);
                }
            }
            else
            {
                registry.RegisterImporter(ext, importer);
                logger?.Information("[discover] Importer {Importer} for {Extension}", importer.GetType().Name, ext);
            }
        }
        catch (Exception ex)
        {
            logger?.Warning(ex, "[discover] Importer conflict for {Extension}", ext);
        }
    }

    private static void TryRegisterCompiler(IAssetRegistry registry, Type assetType, IAssetCompiler compiler, bool fromPlugin, DiscoveryOptions options, ILogger? logger)
    {
        try
        {
            if (registry.TryGetCompiler(assetType, out var existing))
            {
                var existingFromPlugin = existing.GetType().Assembly != null && existing.GetType().Assembly.Location.Length > 0 && options.PluginDirectories.Any(d => SafeIsParentOf(d, existing.GetType().Assembly.Location));
                if (fromPlugin != existingFromPlugin)
                {
                    var preferPlugin = options.PreferPlugin;
                    var shouldOverride = (fromPlugin && preferPlugin) || (!fromPlugin && !preferPlugin);
                    if (shouldOverride)
                    {
                        var mi = typeof(IAssetRegistry).GetMethod(nameof(IAssetRegistry.RegisterCompiler))!.MakeGenericMethod(assetType);
                        mi.Invoke(registry, new object[] { compiler });
                        logger?.Warning("[discover] Compiler conflict for {AssetType}: {New} replaced {Old} ({Preferred} preferred)", assetType.Name, compiler.GetType().Name, existing.GetType().Name, preferPlugin ? "plugin" : "built-in");
                    }
                    else
                    {
                        logger?.Warning("[discover] Compiler conflict for {AssetType}: kept {Old}, ignored {New} ({Preferred} preferred)", assetType.Name, existing.GetType().Name, compiler.GetType().Name, preferPlugin ? "plugin" : "built-in");
                    }
                }
                else
                {
                    logger?.Warning("[discover] Duplicate compiler for {AssetType}: kept {Old}, ignored {New}", assetType.Name, existing.GetType().Name, compiler.GetType().Name);
                }
            }
            else
            {
                var mi = typeof(IAssetRegistry).GetMethod(nameof(IAssetRegistry.RegisterCompiler))!.MakeGenericMethod(assetType);
                mi.Invoke(registry, new object[] { compiler });
                logger?.Information("[discover] Compiler {Compiler} for {AssetType}", compiler.GetType().Name, assetType.Name);
            }
        }
        catch (Exception ex)
        {
            logger?.Warning(ex, "[discover] Compiler conflict for {AssetType}", assetType.Name);
        }
    }

    private static bool SafeIsParentOf(string parentDir, string path)
    {
        try
        {
            var parent = Path.GetFullPath(parentDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            return full.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
