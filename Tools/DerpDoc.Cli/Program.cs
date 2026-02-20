using System.CommandLine;
using Derp.Doc.Export;
using Derp.Doc.Mcp;
using Derp.Doc.Storage;

var rootCommand = new RootCommand("Derp.Doc CLI - export pipeline tool");

var exportCommand = new Command("export", "Export a Derp.Doc database to .derpdoc + generated C#");

var pathArg = new Argument<string>("path", "Game root (contains 'derpgame') or DB root (contains project.json)");

var generatedOpt = new Option<string>(
    ["--generated", "-g"],
    () => "",
    "Output directory for generated .g.cs files");

var binOpt = new Option<string>(
    ["--bin", "-b"],
    () => "",
    "Output path for the compiled .derpdoc binary");

var noManifestOpt = new Option<bool>(
    ["--no-manifest"],
    () => false,
    "Skip writing the .manifest.json sidecar");

var noLiveOpt = new Option<bool>(
    ["--no-live"],
    () => false,
    "Skip writing the .derpdoc-live.bin hot-reload sidecar.");

exportCommand.AddArgument(pathArg);
exportCommand.AddOption(generatedOpt);
exportCommand.AddOption(binOpt);
exportCommand.AddOption(noManifestOpt);
exportCommand.AddOption(noLiveOpt);

exportCommand.SetHandler(
    (string path, string generatedDir, string binPath, bool noManifest, bool noLive) =>
    {
        string dbRoot = DocProjectPaths.ResolveDbRootFromPath(path, allowCreate: true, out string? gameRoot);
        string projectName = !string.IsNullOrWhiteSpace(gameRoot) ? new DirectoryInfo(gameRoot).Name : new DirectoryInfo(dbRoot).Name;
        DocProjectScaffolder.EnsureDbRoot(dbRoot, projectName);

        string resolvedBinPath = string.IsNullOrWhiteSpace(binPath)
            ? DocProjectPaths.ResolveDefaultBinaryPath(dbRoot, gameRoot)
            : Path.GetFullPath(binPath);

        var options = new ExportPipelineOptions
        {
            GeneratedOutputDirectory = generatedDir ?? "",
            BinaryOutputPath = resolvedBinPath,
            LiveBinaryOutputPath = noLive ? "" : DocProjectPaths.ResolveDefaultLiveBinaryPath(dbRoot),
            WriteManifest = !noManifest,
        };

        var pipeline = new DocExportPipeline();
        var result = pipeline.ExportFromDirectory(dbRoot, options);

        for (int i = 0; i < result.Diagnostics.Count; i++)
        {
            var d = result.Diagnostics[i];
            var prefix = d.Severity.ToString().ToUpperInvariant();
            Console.Error.WriteLine($"{prefix} {d.Code}: {d.Message}");
        }

        if (result.HasErrors)
        {
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Wrote {resolvedBinPath}");
        if (!string.IsNullOrWhiteSpace(options.GeneratedOutputDirectory))
        {
            Console.WriteLine($"Wrote generated code to {options.GeneratedOutputDirectory}");
        }
    },
    pathArg,
    generatedOpt,
    binOpt,
    noManifestOpt,
    noLiveOpt);

rootCommand.AddCommand(exportCommand);

var initGameCommand = new Command("init-game", "Create a new Derp game root layout (derpgame + Assets/ + Database/ + Resources/Database/)");
var initPathArg = new Argument<string>("path", "Game root directory to create");
var initNameOpt = new Option<string>(
    ["--name"],
    () => "",
    "Optional project display name (defaults to folder name).");
initGameCommand.AddArgument(initPathArg);
initGameCommand.AddOption(initNameOpt);
initGameCommand.SetHandler(
    (string path, string name) =>
    {
        string fullGameRoot = DocProjectScaffolder.EnsureGameRoot(path, name ?? "");
        Console.WriteLine($"Initialized game root: {fullGameRoot}");
    },
    initPathArg,
    initNameOpt);
rootCommand.AddCommand(initGameCommand);

var mcpCommand = new Command("mcp", "Run Derp.Doc MCP server over stdio");
var workspaceOpt = new Option<string>(
    ["--workspace", "-w"],
    () => Directory.GetCurrentDirectory(),
    "Workspace root for resolving relative paths (defaults to current directory).");
var onceOpt = new Option<bool>(
    ["--once"],
    () => false,
    "Handle a single JSON-RPC message from stdin and exit (testing).");

mcpCommand.AddOption(workspaceOpt);
mcpCommand.AddOption(onceOpt);

mcpCommand.SetHandler(
    async (string workspaceRoot, bool once) =>
    {
        var server = new DerpDocMcpServer(new DerpDocMcpServerOptions
        {
            WorkspaceRoot = workspaceRoot ?? "",
        });

        if (once)
        {
            string? line = await Console.In.ReadLineAsync();
            if (line == null)
            {
                return;
            }

            if (server.TryHandleJsonRpc(line, out var response) && response != null)
            {
                await Console.Out.WriteLineAsync(response);
            }
            return;
        }

        await server.RunStdioAsync(CancellationToken.None);
    },
    workspaceOpt,
    onceOpt);

rootCommand.AddCommand(mcpCommand);
return await rootCommand.InvokeAsync(args);
