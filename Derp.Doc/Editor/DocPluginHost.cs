using System.Reflection;
using System.Text;
using System.Text.Json;
using Derp.Doc.Export;
using Derp.Doc.Plugins;
using Derp.Doc.Tables;

namespace Derp.Doc.Editor;

internal sealed class DocPluginHost
{
    private const string PluginManifestFileName = "plugins.json";
    private const string BundledAssemblyPath = "(bundled)";

    private readonly List<DocLoadedPlugin> _loadedPlugins = new();

    public string LastLoadMessage { get; private set; } = "";

    public int LoadedPluginCount => _loadedPlugins.Count;

    public void CopyLoadedPluginInfos(List<DocLoadedPluginInfo> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        for (int pluginIndex = 0; pluginIndex < _loadedPlugins.Count; pluginIndex++)
        {
            var loadedPlugin = _loadedPlugins[pluginIndex];
            destination.Add(new DocLoadedPluginInfo(loadedPlugin.Id, loadedPlugin.AssemblyPath));
        }
    }

    public void ReloadForProject(string projectRoot)
    {
        UnloadAll();
        LastLoadMessage = "";
        var loadMessageBuilder = new StringBuilder();
        RegisterBundledPlugins(loadMessageBuilder);

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            LastLoadMessage = BuildLoadMessage(LoadedPluginCount, baseMessage: "", loadMessageBuilder);
            return;
        }

        string manifestPath = Path.Combine(projectRoot, PluginManifestFileName);
        if (!File.Exists(manifestPath))
        {
            LastLoadMessage = BuildLoadMessage(LoadedPluginCount, "No plugins manifest found.", loadMessageBuilder);
            return;
        }

        List<string> pluginAssemblyPaths;
        if (!TryReadPluginAssemblyPaths(projectRoot, manifestPath, pluginAssemblyPaths: out pluginAssemblyPaths, loadMessageBuilder))
        {
            LastLoadMessage = BuildLoadMessage(LoadedPluginCount, baseMessage: "", loadMessageBuilder);
            return;
        }

        if (pluginAssemblyPaths.Count == 0)
        {
            LastLoadMessage = BuildLoadMessage(LoadedPluginCount, "Plugin manifest contains no enabled plugins.", loadMessageBuilder);
            return;
        }

        for (int pluginAssemblyIndex = 0; pluginAssemblyIndex < pluginAssemblyPaths.Count; pluginAssemblyIndex++)
        {
            LoadPluginAssembly(pluginAssemblyPaths[pluginAssemblyIndex], loadMessageBuilder);
        }

        if (LoadedPluginCount == 0)
        {
            if (loadMessageBuilder.Length == 0)
            {
                LastLoadMessage = "No plugins were loaded.";
            }
            else
            {
                LastLoadMessage = loadMessageBuilder.ToString();
            }

            return;
        }

        LastLoadMessage = BuildLoadMessage(LoadedPluginCount, baseMessage: "", loadMessageBuilder);
    }

    public void UnloadAll()
    {
        for (int pluginIndex = 0; pluginIndex < _loadedPlugins.Count; pluginIndex++)
        {
            if (_loadedPlugins[pluginIndex].Instance is IDisposable disposablePlugin)
            {
                try
                {
                    disposablePlugin.Dispose();
                }
                catch
                {
                    // Plugin disposal errors should not block shutdown/unload.
                }
            }
        }

        var weakContextReferences = new List<WeakReference>(_loadedPlugins.Count);
        for (int pluginIndex = 0; pluginIndex < _loadedPlugins.Count; pluginIndex++)
        {
            var loadContext = _loadedPlugins[pluginIndex].LoadContext;
            if (loadContext == null)
            {
                continue;
            }

            weakContextReferences.Add(new WeakReference(loadContext, trackResurrection: false));
            loadContext.Unload();
        }

        _loadedPlugins.Clear();
        ResetRegistries();
        ForceContextCollection(weakContextReferences);
    }

    private void RegisterBundledPlugins(StringBuilder loadMessageBuilder)
    {
        _ = TryRegisterPluginInstance(
            pluginInstance: new RadarChartPlugin(),
            pluginType: typeof(RadarChartPlugin),
            assemblyPath: BundledAssemblyPath,
            loadContext: null,
            loadMessageBuilder);
        _ = TryRegisterPluginInstance(
            pluginInstance: new NodeGraphPlugin(),
            pluginType: typeof(NodeGraphPlugin),
            assemblyPath: BundledAssemblyPath,
            loadContext: null,
            loadMessageBuilder);
        _ = TryRegisterPluginInstance(
            pluginInstance: new SplineGamePlugin(),
            pluginType: typeof(SplineGamePlugin),
            assemblyPath: BundledAssemblyPath,
            loadContext: null,
            loadMessageBuilder);
        ColumnExportProviderRegistry.Register(new SplineGameLevelExportProvider());
    }

    private static void ResetRegistries()
    {
        ColumnTypeDefinitionRegistry.Clear();
        ColumnDefaultValueProviderRegistry.Clear();
        ColumnCellCodecProviderRegistry.Clear();
        ColumnExportProviderRegistry.Clear();
        FormulaFunctionRegistry.Clear();
        TableViewRendererRegistry.Clear();
        NodeSubtableSectionRendererRegistry.Clear();
        ColumnUiPluginRegistry.Clear();
        PluginPreferencesProviderRegistry.Clear();
        PluginAutomationProviderRegistry.Clear();
    }

    private static bool TryReadPluginAssemblyPaths(
        string projectRoot,
        string manifestPath,
        out List<string> pluginAssemblyPaths,
        StringBuilder loadMessageBuilder)
    {
        pluginAssemblyPaths = new List<string>();
        string manifestJson;
        try
        {
            manifestJson = File.ReadAllText(manifestPath);
        }
        catch (Exception exception)
        {
            loadMessageBuilder.Append("Failed to read plugins manifest: ").Append(exception.Message);
            return false;
        }

        JsonDocument manifestDocument;
        try
        {
            manifestDocument = JsonDocument.Parse(manifestJson);
        }
        catch (JsonException exception)
        {
            loadMessageBuilder.Append("Failed to parse plugins manifest JSON: ").Append(exception.Message);
            return false;
        }

        using (manifestDocument)
        {
            if (!manifestDocument.RootElement.TryGetProperty("plugins", out var pluginsElement) ||
                pluginsElement.ValueKind != JsonValueKind.Array)
            {
                loadMessageBuilder.Append("plugins.json must contain a 'plugins' array.");
                return false;
            }

            foreach (var pluginEntry in pluginsElement.EnumerateArray())
            {
                if (!TryResolvePluginPath(projectRoot, pluginEntry, out string? pluginAssemblyPath))
                {
                    continue;
                }

                if (!File.Exists(pluginAssemblyPath))
                {
                    if (loadMessageBuilder.Length > 0)
                    {
                        loadMessageBuilder.Append(' ');
                    }

                    loadMessageBuilder.Append("Plugin dll not found: ").Append(pluginAssemblyPath).Append('.');
                    continue;
                }

                pluginAssemblyPaths.Add(pluginAssemblyPath);
            }
        }

        return true;
    }

    private static bool TryResolvePluginPath(string projectRoot, JsonElement pluginEntry, out string? pluginAssemblyPath)
    {
        pluginAssemblyPath = null;
        switch (pluginEntry.ValueKind)
        {
            case JsonValueKind.String:
            {
                string pathValue = pluginEntry.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(pathValue))
                {
                    return false;
                }

                pluginAssemblyPath = ResolvePath(projectRoot, pathValue);
                return true;
            }
            case JsonValueKind.Object:
            {
                bool enabled = true;
                if (pluginEntry.TryGetProperty("enabled", out var enabledElement) &&
                    (enabledElement.ValueKind == JsonValueKind.True || enabledElement.ValueKind == JsonValueKind.False))
                {
                    enabled = enabledElement.GetBoolean();
                }

                if (!enabled)
                {
                    return false;
                }

                if (!pluginEntry.TryGetProperty("path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                string pathValue = pathElement.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(pathValue))
                {
                    return false;
                }

                pluginAssemblyPath = ResolvePath(projectRoot, pathValue);
                return true;
            }
            default:
                return false;
        }
    }

    private static string ResolvePath(string projectRoot, string pluginPath)
    {
        if (Path.IsPathRooted(pluginPath))
        {
            return Path.GetFullPath(pluginPath);
        }

        return Path.GetFullPath(Path.Combine(projectRoot, pluginPath));
    }

    private void LoadPluginAssembly(string pluginAssemblyPath, StringBuilder loadMessageBuilder)
    {
        var loadContext = new DocPluginLoadContext(pluginAssemblyPath);
        try
        {
            Assembly pluginAssembly = loadContext.LoadFromAssemblyPath(pluginAssemblyPath);
            Type[] pluginTypes = GetPluginTypes(pluginAssembly);
            int loadedCount = 0;
            for (int typeIndex = 0; typeIndex < pluginTypes.Length; typeIndex++)
            {
                Type pluginType = pluginTypes[typeIndex];
                if (pluginType.IsAbstract || pluginType.IsInterface)
                {
                    continue;
                }

                bool isCorePlugin = typeof(IDerpDocPlugin).IsAssignableFrom(pluginType);
                bool isEditorPlugin = typeof(IDerpDocEditorPlugin).IsAssignableFrom(pluginType);
                if (!isCorePlugin && !isEditorPlugin)
                {
                    continue;
                }

                if (pluginType.GetConstructor(Type.EmptyTypes) == null)
                {
                    AppendLoadMessage(loadMessageBuilder, "Skipping plugin type without parameterless constructor: " + pluginType.FullName + ".");
                    continue;
                }

                object? pluginObject = Activator.CreateInstance(pluginType);
                if (pluginObject == null)
                {
                    AppendLoadMessage(loadMessageBuilder, "Failed to instantiate plugin type: " + pluginType.FullName + ".");
                    continue;
                }

                if (TryRegisterPluginInstance(
                        pluginObject,
                        pluginType,
                        pluginAssemblyPath,
                        loadContext,
                        loadMessageBuilder))
                {
                    loadedCount++;
                }
            }

            if (loadedCount == 0)
            {
                loadContext.Unload();
                AppendLoadMessage(loadMessageBuilder, "No plugin entry types loaded from " + pluginAssemblyPath + ".");
            }
        }
        catch (Exception exception)
        {
            loadContext.Unload();
            AppendLoadMessage(loadMessageBuilder, "Failed to load plugin assembly " + pluginAssemblyPath + ": " + exception.Message + ".");
        }
    }

    private bool TryRegisterPluginInstance(
        object pluginInstance,
        Type pluginType,
        string assemblyPath,
        DocPluginLoadContext? loadContext,
        StringBuilder loadMessageBuilder)
    {
        var corePlugin = pluginInstance as IDerpDocPlugin;
        var editorPlugin = pluginInstance as IDerpDocEditorPlugin;
        if (corePlugin == null && editorPlugin == null)
        {
            AppendLoadMessage(loadMessageBuilder, "Failed to cast plugin type: " + pluginType.FullName + ".");
            return false;
        }

        var registrationContext = new DocPluginRegistrationContext();
        try
        {
            corePlugin?.Register(registrationContext);
            editorPlugin?.RegisterEditor(registrationContext);
            registrationContext.Commit();
        }
        catch (Exception exception)
        {
            if (pluginInstance is IDisposable disposablePlugin)
            {
                try
                {
                    disposablePlugin.Dispose();
                }
                catch
                {
                    // Ignore plugin cleanup failures while recovering from load failures.
                }
            }

            AppendLoadMessage(
                loadMessageBuilder,
                "Failed to register plugin type " + pluginType.FullName + ": " + exception.Message + ".");
            return false;
        }

        _loadedPlugins.Add(new DocLoadedPlugin
        {
            Id = ResolvePluginId(corePlugin, editorPlugin, pluginType),
            AssemblyPath = assemblyPath,
            Instance = pluginInstance,
            LoadContext = loadContext,
        });
        return true;
    }

    private static Type[] GetPluginTypes(Assembly pluginAssembly)
    {
        try
        {
            return pluginAssembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var pluginTypes = new List<Type>();
            for (int typeIndex = 0; typeIndex < exception.Types.Length; typeIndex++)
            {
                var pluginType = exception.Types[typeIndex];
                if (pluginType != null)
                {
                    pluginTypes.Add(pluginType);
                }
            }

            return pluginTypes.ToArray();
        }
    }

    private static void AppendLoadMessage(StringBuilder loadMessageBuilder, string message)
    {
        if (loadMessageBuilder.Length > 0)
        {
            loadMessageBuilder.Append(' ');
        }

        loadMessageBuilder.Append(message);
    }

    private static string BuildLoadMessage(
        int loadedPluginCount,
        string baseMessage,
        StringBuilder loadMessageBuilder)
    {
        bool hasBaseMessage = !string.IsNullOrWhiteSpace(baseMessage);
        bool hasExtraMessages = loadMessageBuilder.Length > 0;
        if (loadedPluginCount <= 0)
        {
            if (hasBaseMessage && hasExtraMessages)
            {
                return baseMessage + " " + loadMessageBuilder;
            }

            if (hasBaseMessage)
            {
                return baseMessage;
            }

            if (hasExtraMessages)
            {
                return loadMessageBuilder.ToString();
            }

            return string.Empty;
        }

        if (!hasBaseMessage && !hasExtraMessages)
        {
            return $"Loaded {loadedPluginCount} plugin(s).";
        }

        var messageBuilder = new StringBuilder();
        messageBuilder.Append("Loaded ").Append(loadedPluginCount).Append(" plugin(s).");
        if (hasBaseMessage)
        {
            messageBuilder.Append(' ').Append(baseMessage);
        }

        if (hasExtraMessages)
        {
            messageBuilder.Append(' ').Append(loadMessageBuilder);
        }

        return messageBuilder.ToString();
    }

    private static string ResolvePluginId(
        IDerpDocPlugin? corePlugin,
        IDerpDocEditorPlugin? editorPlugin,
        Type pluginType)
    {
        string candidateId = corePlugin?.Id ?? editorPlugin?.Id ?? "";
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            return pluginType.FullName ?? pluginType.Name;
        }

        return candidateId.Trim();
    }

    private static void ForceContextCollection(List<WeakReference> weakContextReferences)
    {
        if (weakContextReferences.Count == 0)
        {
            return;
        }

        for (int collectAttempt = 0; collectAttempt < 10; collectAttempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            bool anyAlive = false;
            for (int weakReferenceIndex = 0; weakReferenceIndex < weakContextReferences.Count; weakReferenceIndex++)
            {
                if (weakContextReferences[weakReferenceIndex].IsAlive)
                {
                    anyAlive = true;
                    break;
                }
            }

            if (!anyAlive)
            {
                return;
            }
        }
    }
}
