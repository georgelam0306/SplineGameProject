using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ShaderCompiler;

/// <summary>
/// Wrapper for glslc GLSL-to-SPIR-V compiler.
/// Attempts to use bundled glslc first, falls back to system PATH.
/// </summary>
public static class GlslCompiler
{
    private static string? _cachedPath;

    /// <summary>
    /// Gets the path to the glslc executable.
    /// Tries bundled binary first, then falls back to system PATH.
    /// </summary>
    public static string? GetGlslcPath()
    {
        if (_cachedPath != null)
            return _cachedPath;

        // Try bundled binary first
        var bundledPath = GetBundledGlslcPath();
        if (bundledPath != null && File.Exists(bundledPath))
        {
            _cachedPath = bundledPath;
            return _cachedPath;
        }

        // Fall back to system PATH
        var systemPath = FindInPath("glslc");
        if (systemPath != null)
        {
            _cachedPath = systemPath;
            return _cachedPath;
        }

        return null;
    }

    /// <summary>
    /// Gets the path to bundled glslc based on current platform.
    /// </summary>
    private static string? GetBundledGlslcPath()
    {
        var rid = GetRuntimeIdentifier();
        if (rid == null) return null;

        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "glslc.exe" : "glslc";

        // Try relative to assembly (for runtime)
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (assemblyDir != null)
        {
            // Look in Tools/glslc/{rid}/ relative to project root
            var projectRoot = FindProjectRoot(assemblyDir);
            if (projectRoot != null)
            {
                var path = Path.Combine(projectRoot, "Tools", "glslc", rid, exe);
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the project root by looking for .git or .sln files.
    /// </summary>
    private static string? FindProjectRoot(string startDir)
    {
        var dir = startDir;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) ||
                Directory.GetFiles(dir, "*.sln").Length > 0)
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>
    /// Gets the runtime identifier for the current platform.
    /// </summary>
    private static string? GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "win-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "osx-arm64"
                : "osx-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux-x64";

        return null;
    }

    /// <summary>
    /// Finds an executable in the system PATH.
    /// </summary>
    private static string? FindInPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv == null) return null;

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        foreach (var dir in pathEnv.Split(separator))
        {
            foreach (var ext in extensions)
            {
                var path = Path.Combine(dir, executable + ext);
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Compiles a GLSL shader to SPIR-V.
    /// </summary>
    /// <param name="inputPath">Path to GLSL source file (.vert, .frag, .comp)</param>
    /// <param name="outputPath">Path to output SPIR-V file (.spv)</param>
    /// <param name="error">Error message if compilation fails</param>
    /// <returns>True if compilation succeeded</returns>
    public static bool Compile(string inputPath, string outputPath, out string error)
    {
        var glslcPath = GetGlslcPath();
        if (glslcPath == null)
        {
            error = "glslc not found. Install Vulkan SDK or run scripts/download-glslc.sh";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = glslcPath,
                Arguments = $"\"{inputPath}\" -o \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                error = "Failed to start glslc process";
                return false;
            }

            error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            error = $"Exception running glslc: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Compiles a GLSL shader to SPIR-V with additional options.
    /// </summary>
    /// <param name="inputPath">Path to GLSL source file</param>
    /// <param name="outputPath">Path to output SPIR-V file</param>
    /// <param name="targetEnv">Target environment (e.g., "vulkan1.0", "vulkan1.2")</param>
    /// <param name="optimizationLevel">Optimization level (0, s, or empty)</param>
    /// <param name="error">Error message if compilation fails</param>
    /// <returns>True if compilation succeeded</returns>
    public static bool Compile(
        string inputPath,
        string outputPath,
        string? targetEnv,
        string? optimizationLevel,
        out string error)
    {
        var glslcPath = GetGlslcPath();
        if (glslcPath == null)
        {
            error = "glslc not found. Install Vulkan SDK or run scripts/download-glslc.sh";
            return false;
        }

        try
        {
            var args = new List<string> { $"\"{inputPath}\"", "-o", $"\"{outputPath}\"" };

            if (!string.IsNullOrEmpty(targetEnv))
                args.Add($"--target-env={targetEnv}");

            if (!string.IsNullOrEmpty(optimizationLevel))
                args.Add($"-O{optimizationLevel}");

            var psi = new ProcessStartInfo
            {
                FileName = glslcPath,
                Arguments = string.Join(" ", args),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                error = "Failed to start glslc process";
                return false;
            }

            error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            error = $"Exception running glslc: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Gets the version string of glslc.
    /// </summary>
    public static string? GetVersion()
    {
        var glslcPath = GetGlslcPath();
        if (glslcPath == null) return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = glslcPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
