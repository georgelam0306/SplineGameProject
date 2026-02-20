using System.Diagnostics;
using DerpLib.AssetPipeline;
using Serilog;

namespace DerpLib.Build;

[Compiler(typeof(ShaderStageAsset))]
public sealed class ShaderCompiler : IAssetCompiler
{
    private readonly ILogger _log;

    public ShaderCompiler(ILogger log)
    {
        _log = log;
    }

    public IEnumerable<string> GetInputFiles(AssetItem item)
    {
        var asset = (ShaderStageAsset)item.Asset;
        yield return asset.Source;
    }

    public ObjectId Compile(AssetItem item, IObjectDatabase db, IBlobSerializer serializer)
    {
        var spirvBytes = CompileToBytes(item);
        return db.Put(spirvBytes);
    }

    /// <summary>
    /// Compiles a shader to SPIR-V bytes without storing in database.
    /// </summary>
    public byte[] CompileToBytes(AssetItem item)
    {
        var asset = (ShaderStageAsset)item.Asset;
        var sourceFile = asset.Source;

        if (!File.Exists(sourceFile))
        {
            throw new FileNotFoundException($"Shader source not found: {sourceFile}");
        }

        // Compile to SPIR-V using glslc
        var spirvBytes = CompileToSpirv(sourceFile, asset.Stage);

        _log.Information("Compiled {Source} ({Stage}) -> {Size} bytes",
            Path.GetFileName(sourceFile), asset.Stage, spirvBytes.Length);

        return spirvBytes;
    }

    private byte[] CompileToSpirv(string sourceFile, ShaderStage stage)
    {
        // Create temp file for output
        var tempOutput = Path.GetTempFileName();

        try
        {
            var stageFlag = stage switch
            {
                ShaderStage.Vertex => "-fshader-stage=vertex",
                ShaderStage.Fragment => "-fshader-stage=fragment",
                ShaderStage.Compute => "-fshader-stage=compute",
                _ => throw new ArgumentOutOfRangeException(nameof(stage))
            };

            var psi = new ProcessStartInfo
            {
                FileName = "glslc",
                Arguments = $"{stageFlag} \"{sourceFile}\" -o \"{tempOutput}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                throw new InvalidOperationException("Failed to start glslc. Is it installed and in PATH?");
            }

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"glslc failed for {sourceFile}:\n{stderr}");
            }

            return File.ReadAllBytes(tempOutput);
        }
        finally
        {
            if (File.Exists(tempOutput))
            {
                File.Delete(tempOutput);
            }
        }
    }
}
