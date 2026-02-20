using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Raylib_cs;
using Serilog;
using BaseTemplate; // For AotSchema generated class
using BaseTemplate.GameApp.Config;
using BaseTemplate.GameApp.Core;
using BaseTemplate.GameApp.Stores;
using BaseTemplate.Infrastructure.Networking;
using Networking;
using Log = Serilog.Log;

namespace BaseTemplate;

public partial class Program
{
    private static readonly ILogger _log = Log.ForContext<Program>();

    private static int _windowWidth;
    private static int _windowHeight;

    private static AppComposition? _composition;
    private static BugReportService? _bugReportService;

    /// <summary>
    /// The global bug report service instance.
    /// </summary>
    public static BugReportService? BugReportServiceInstance => _bugReportService;

    // CLI args
    private static bool _skipMenu;
    private static int _expectedPlayerCount = 1;
    private static int _localPlayerSlot = 0;
    private static NetworkProfile _networkProfile = NetworkProfile.None;
    private static bool _useSteamMatchmaking;

    // Recording/replay paths (set via environment variables)
    private static string? _recordFilePath;
    private static string? _replayFilePath;

    public static string? RecordFilePath => _recordFilePath;
    public static string? ReplayFilePath => _replayFilePath;

    // Window position (legacy --x/--y args)
    private static int _pendingWindowX;
    private static int _pendingWindowY;
    private static bool _hasPendingWindowPosition;

    // Multi-client layout (new approach - let Raylib calculate dimensions)
    private static int _clientIndex = -1;  // -1 means not in multi-client mode
    private static int _clientCount = 1;

    private static void SetupDesktopNative()
    {
        try
        {
            var raylibAssembly = typeof(Raylib).Assembly;
            NativeLibrary.SetDllImportResolver(raylibAssembly, (name, assembly, path) =>
            {
                string baseDir = AppContext.BaseDirectory;
                string[] candidates = OperatingSystem.IsMacOS()
                    ? new[] { "libraylib_bundle.dylib", "libraylib.dylib", "libraylib.5.5.0.dylib", "libraylib.550.dylib" }
                    : OperatingSystem.IsWindows()
                        ? new[] { "raylib_bundle.dll", "raylib.dll" }
                        : new[] { "libraylib_bundle.so", "libraylib.so" };

                foreach (var fileName in candidates)
                {
                    string filePath = Path.Combine(baseDir, fileName);
                    if (File.Exists(filePath) && NativeLibrary.TryLoad(filePath, out var libraryHandle))
                    {
                        return libraryHandle;
                    }
                }

                return IntPtr.Zero;
            });
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    private static void ParseArgs(string[] args)
    {
        int? windowX = null;
        int? windowY = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg == "--width" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int w))
                {
                    _windowWidth = w;
                }
                i++;
            }
            else if (arg == "--height" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int h))
                {
                    _windowHeight = h;
                }
                i++;
            }
            else if (arg == "--x" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int x))
                {
                    windowX = x;
                }
                i++;
            }
            else if (arg == "--y" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int y))
                {
                    windowY = y;
                }
                i++;
            }
            else if (arg == "--skip-menu")
            {
                _skipMenu = true;
            }
            else if (arg == "--steam-matchmaking")
            {
                _useSteamMatchmaking = true;
            }
            else if (arg == "--client-index" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int idx))
                {
                    _clientIndex = idx;
                }
                i++;
            }
            else if (arg == "--client-count" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int cnt) && cnt > 0)
                {
                    _clientCount = cnt;
                }
                i++;
            }
        }

        // Parse environment variables
        string? netPlayersEnv = Environment.GetEnvironmentVariable("NET_PLAYERS");

        if (int.TryParse(netPlayersEnv, out int playerCount) && playerCount > 0)
        {
            _expectedPlayerCount = playerCount;
        }

        string? netPlayerSlotEnv = Environment.GetEnvironmentVariable("NET_PLAYER_SLOT");
        if (int.TryParse(netPlayerSlotEnv, out int playerSlot) && playerSlot >= 0)
        {
            _localPlayerSlot = playerSlot;
        }

        // Recording/replay paths
        _recordFilePath = Environment.GetEnvironmentVariable("RECORD_FILE");
        _replayFilePath = Environment.GetEnvironmentVariable("REPLAY_FILE");

        if (!string.IsNullOrEmpty(_replayFilePath))
        {
            _log.Information("Replay mode enabled: {ReplayFilePath}", _replayFilePath);
        }
        else if (!string.IsNullOrEmpty(_recordFilePath))
        {
            _log.Information("Recording mode enabled: {RecordFilePath}", _recordFilePath);
        }

        // Network simulation profile
        string? netProfileEnv = Environment.GetEnvironmentVariable("NET_PROFILE");
        if (!string.IsNullOrEmpty(netProfileEnv) &&
            Enum.TryParse<NetworkProfile>(netProfileEnv, ignoreCase: true, out var parsedProfile))
        {
            _networkProfile = parsedProfile;
            _log.Information("Network profile: {NetworkProfile}", _networkProfile);
        }

        // Matchmaking provider selection (env var overrides CLI)
        string? matchmakingProviderEnv = Environment.GetEnvironmentVariable("MATCHMAKING_PROVIDER");
        if (string.Equals(matchmakingProviderEnv, "steam", StringComparison.OrdinalIgnoreCase))
        {
            _useSteamMatchmaking = true;
        }

        if (_useSteamMatchmaking)
        {
            _log.Information("Using Steam matchmaking provider");
        }

        // Store window position for later (after window is created)
        if (windowX.HasValue || windowY.HasValue)
        {
            _pendingWindowX = windowX ?? 0;
            _pendingWindowY = windowY ?? 0;
            _hasPendingWindowPosition = true;
        }
    }

    public static void Main(string[] args)
    {
        BaseTemplate.GameApp.Core.Logging.Initialize();

        // Set up global exception handlers FIRST
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Initialize bug report service early (before anything that could crash)
        _bugReportService = new BugReportService();
        BugReportService.GameVersion = GetGameVersion();

        try
        {
            RunGame(args);
        }
        catch (Exception ex)
        {
            HandleFatalException(ex);
        }
        finally
        {
            _bugReportService?.Dispose();
        }
    }

    private static void RunGame(string[] args)
    {
        SetupDesktopNative();

        // Register ECS component types for NativeAOT (must be called before any ECS operations)
        AotSchema.Create();

        _windowWidth = GameConfig.WindowWidth;
        _windowHeight = GameConfig.WindowHeight;

        // Parse command line args first
        ParseArgs(args);

        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.HighDpiWindow);
        Raylib.InitWindow(_windowWidth, _windowHeight, "BaseTemplate");
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.Null);  // Disable ESC from closing window

        // Get monitor dimensions for window sizing
        int monitor = Raylib.GetCurrentMonitor();
        int monitorW = Raylib.GetMonitorWidth(monitor);
        int monitorH = Raylib.GetMonitorHeight(monitor);

        // Multi-client layout: divide screen horizontally
        if (_clientIndex >= 0 && _clientCount > 1)
        {
            // Calculate window dimensions (divide screen horizontally)
            int windowW = monitorW / _clientCount;
            int windowH = monitorH - 25;  // Leave space for menu bar
            int windowX = _clientIndex * windowW;
            int windowY = 25;

            _log.Information("Multi-client layout: monitor {MonW}x{MonH}, window {WinW}x{WinH} at ({X},{Y})",
                monitorW, monitorH, windowW, windowH, windowX, windowY);

            Raylib.SetWindowSize(windowW, windowH);
            Raylib.SetWindowPosition(windowX, windowY);
        }
        // Legacy: explicit position args
        else if (_hasPendingWindowPosition)
        {
            Raylib.SetWindowPosition(_pendingWindowX, _pendingWindowY);
        }
        // Default: start at max resolution
        else
        {
            Raylib.SetWindowSize(monitorW, monitorH);
            Raylib.SetWindowPosition(0, 0);
        }

        // Initialize app composition (DI container)
        var appLaunch = new AppLaunch(
            SkipMenu: _skipMenu,
            ExpectedPlayerCount: _expectedPlayerCount,
            LocalPlayerSlot: _localPlayerSlot,
            RecordFilePath: _recordFilePath,
            ReplayFilePath: _replayFilePath,
            UseSteamMatchmaking: _useSteamMatchmaking);

        // Connect to Orleans cluster for matchmaking (unless using Steam)
        if (!_useSteamMatchmaking)
        {
            _log.Information("Connecting to Orleans cluster...");
            var orleansClient = OrleansClientFactory.CreateClientAutoAsync().GetAwaiter().GetResult();
            AppComposition.SetOrleansClient(orleansClient);
            _log.Information("Connected to Orleans cluster");
        }

        _composition = new AppComposition(appLaunch, new NetworkConfig(_networkProfile));

        try
        {
            // Run the game
            _composition.GameRunner.Run();
        }
        finally
        {
            // Cleanup
            _composition?.Dispose();
            BaseTemplate.GameApp.Core.Logging.Shutdown();
            Raylib.CloseWindow();
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            HandleFatalException(ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log.Error(e.Exception, "Unobserved task exception");
        // Mark as observed to prevent crash, but still log
        e.SetObserved();
    }

    private static void HandleFatalException(Exception ex)
    {
        _log.Fatal(ex, "Fatal exception occurred");

        // Get current replay path if available
        string? replayPath = null;
        try
        {
            replayPath = GetCurrentReplayPath();
        }
        catch { /* Ignore */ }

        // Create crash report
        _bugReportService?.CreateCrashReport(ex, replayPath);

        // Give upload a moment to start
        Thread.Sleep(500);
    }

    private static string? GetCurrentReplayPath()
    {
        // Find most recent replay file
        var logsDir = "Logs";
        if (!Directory.Exists(logsDir)) return null;

        return Directory.GetFiles(logsDir, "replay_*.bin")
            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
            .FirstOrDefault();
    }

    private static string GetGameVersion()
    {
        // Return assembly version or build info
        var assembly = typeof(Program).Assembly;
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "dev";
    }
}
