using System.Diagnostics;

namespace Derp.Doc.Assets;

internal sealed class LazyAssetCompiler
{
    private static readonly string[] SourceMeshExtensionsRequiringCompile =
    [
        ".obj",
        ".fbx",
        ".gltf",
        ".glb",
        ".dae",
        ".3ds",
    ];

    private readonly object _gate = new();
    private readonly Queue<CompileRequest> _requestQueue = new();
    private readonly HashSet<string> _queuedOrRunningRequestKeys = new(StringComparer.Ordinal);

    private Task? _workerTask;
    private bool _engineBuildProjectPathResolved;
    private string _engineBuildProjectPath = "";

    private readonly struct CompileRequest
    {
        public CompileRequest(string requestKey, string assetsRoot, string dataOutputRoot, string relativeAssetPath)
        {
            RequestKey = requestKey;
            AssetsRoot = assetsRoot;
            DataOutputRoot = dataOutputRoot;
            RelativeAssetPath = relativeAssetPath;
        }

        public string RequestKey { get; }
        public string AssetsRoot { get; }
        public string DataOutputRoot { get; }
        public string RelativeAssetPath { get; }
    }

    public bool EnsureMeshCompileQueued(string assetsRoot, string relativeAssetPath)
    {
        if (!TryNormalizeRelativePath(relativeAssetPath, out string normalizedRelativeAssetPath))
        {
            return false;
        }

        if (!RequiresMeshCompilation(normalizedRelativeAssetPath))
        {
            return false;
        }

        string fullAssetsRoot = Path.GetFullPath(assetsRoot);
        if (!TryResolveGameRoot(fullAssetsRoot, out string gameRoot))
        {
            return false;
        }

        string requestKey = fullAssetsRoot + "|" + normalizedRelativeAssetPath;
        lock (_gate)
        {
            if (_queuedOrRunningRequestKeys.Contains(requestKey))
            {
                return true;
            }

            string dataOutputRoot = Path.Combine(gameRoot, "data");
            _requestQueue.Enqueue(new CompileRequest(
                requestKey,
                fullAssetsRoot,
                dataOutputRoot,
                normalizedRelativeAssetPath));
            _queuedOrRunningRequestKeys.Add(requestKey);

            if (_workerTask == null || _workerTask.IsCompleted)
            {
                _workerTask = Task.Run(ProcessQueueWorker);
            }
        }

        return true;
    }

    public bool IsMeshCompilePending(string assetsRoot, string relativeAssetPath)
    {
        if (!TryNormalizeRelativePath(relativeAssetPath, out string normalizedRelativeAssetPath))
        {
            return false;
        }

        string fullAssetsRoot = Path.GetFullPath(assetsRoot);
        string requestKey = fullAssetsRoot + "|" + normalizedRelativeAssetPath;
        lock (_gate)
        {
            return _queuedOrRunningRequestKeys.Contains(requestKey);
        }
    }

    private void ProcessQueueWorker()
    {
        while (true)
        {
            CompileRequest request;
            lock (_gate)
            {
                if (_requestQueue.Count == 0)
                {
                    return;
                }

                request = _requestQueue.Dequeue();
            }

            try
            {
                ExecuteCompileRequest(request);
            }
            catch
            {
                // Do not fail the editor UI on background compile failures.
            }
            finally
            {
                lock (_gate)
                {
                    _queuedOrRunningRequestKeys.Remove(request.RequestKey);
                }
            }
        }
    }

    private void ExecuteCompileRequest(CompileRequest request)
    {
        if (!TryResolveEngineBuildProjectPath(out string engineBuildProjectPath))
        {
            return;
        }

        var processStartInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        processStartInfo.ArgumentList.Add("run");
        processStartInfo.ArgumentList.Add("--project");
        processStartInfo.ArgumentList.Add(engineBuildProjectPath);
        processStartInfo.ArgumentList.Add("--");
        processStartInfo.ArgumentList.Add(request.AssetsRoot);
        processStartInfo.ArgumentList.Add(request.DataOutputRoot);
        processStartInfo.ArgumentList.Add("--asset");
        processStartInfo.ArgumentList.Add(request.RelativeAssetPath);

        using Process? process = Process.Start(processStartInfo);
        if (process == null)
        {
            return;
        }

        process.WaitForExit();
    }

    private bool TryResolveEngineBuildProjectPath(out string engineBuildProjectPath)
    {
        lock (_gate)
        {
            if (_engineBuildProjectPathResolved)
            {
                engineBuildProjectPath = _engineBuildProjectPath;
                return engineBuildProjectPath.Length > 0;
            }
        }

        string[] candidateStartDirectories =
        [
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        ];

        for (int candidateIndex = 0; candidateIndex < candidateStartDirectories.Length; candidateIndex++)
        {
            if (!TryFindEngineBuildProjectPath(candidateStartDirectories[candidateIndex], out string discoveredPath))
            {
                continue;
            }

            lock (_gate)
            {
                _engineBuildProjectPathResolved = true;
                _engineBuildProjectPath = discoveredPath;
                engineBuildProjectPath = discoveredPath;
                return true;
            }
        }

        lock (_gate)
        {
            _engineBuildProjectPathResolved = true;
            _engineBuildProjectPath = "";
            engineBuildProjectPath = "";
            return false;
        }
    }

    private static bool TryFindEngineBuildProjectPath(string startDirectory, out string projectPath)
    {
        projectPath = "";
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return false;
        }

        DirectoryInfo? currentDirectory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        int maxTraversalDepth = 14;
        for (int depth = 0; depth < maxTraversalDepth && currentDirectory != null; depth++)
        {
            string candidate = Path.Combine(currentDirectory.FullName, "Engine", "src", "Engine.Build", "Engine.Build.csproj");
            if (File.Exists(candidate))
            {
                projectPath = candidate;
                return true;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return false;
    }

    private static bool RequiresMeshCompilation(string normalizedRelativeAssetPath)
    {
        string extension = Path.GetExtension(normalizedRelativeAssetPath);
        for (int extensionIndex = 0; extensionIndex < SourceMeshExtensionsRequiringCompile.Length; extensionIndex++)
        {
            if (string.Equals(extension, SourceMeshExtensionsRequiringCompile[extensionIndex], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveGameRoot(string fullAssetsRoot, out string gameRoot)
    {
        gameRoot = "";

        var assetsDirectoryInfo = new DirectoryInfo(fullAssetsRoot);
        if (!string.Equals(assetsDirectoryInfo.Name, "Assets", StringComparison.Ordinal))
        {
            return false;
        }

        if (assetsDirectoryInfo.Parent == null)
        {
            return false;
        }

        gameRoot = assetsDirectoryInfo.Parent.FullName;
        return true;
    }

    private static bool TryNormalizeRelativePath(string path, out string normalizedRelativePath)
    {
        normalizedRelativePath = "";
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        string slashNormalized = path.Trim().Replace('\\', '/');
        while (slashNormalized.StartsWith("/", StringComparison.Ordinal))
        {
            slashNormalized = slashNormalized[1..];
        }

        if (slashNormalized.Length == 0)
        {
            return false;
        }

        string[] segments = slashNormalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            if (segments[segmentIndex] == "." || segments[segmentIndex] == "..")
            {
                return false;
            }
        }

        normalizedRelativePath = string.Join('/', segments);
        return true;
    }
}
