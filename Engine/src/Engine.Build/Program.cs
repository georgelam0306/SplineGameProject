using DerpLib.AssetPipeline;
using DerpLib.Assets;
using Serilog;
using System.Text.Json;

namespace DerpLib.Build;

public static class Program
{
    public static int Main(string[] args)
    {
        var log = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        if (args.Length < 2)
        {
            log.Error("Usage: DerpLib.Build <source-dir> <output-dir> [--asset <relative-path> ...]");
            log.Information("  source-dir: Directory containing source assets (for example Assets/)");
            log.Information("  output-dir: Directory for compiled output (data/)");
            log.Information("  --asset: Optional Assets-root-relative path filter (can be repeated)");
            return 1;
        }

        var sourceDir = Path.GetFullPath(args[0]);
        var outputDir = Path.GetFullPath(args[1]);

        if (!Directory.Exists(sourceDir))
        {
            log.Error("Source directory not found: {Dir}", sourceDir);
            return 1;
        }

        if (!TryParseSelectedAssetFilters(args, out HashSet<string> selectedAssetFilters))
        {
            log.Error("Invalid arguments. Expected '--asset <relative-path>' after source/output.");
            return 1;
        }

        log.Information("Building assets from {Source} to {Output}", sourceDir, outputDir);
        if (selectedAssetFilters.Count > 0)
        {
            log.Information("Applying asset filter with {Count} path(s)", selectedAssetFilters.Count);
        }

        try
        {
            BuildShaders(log, sourceDir, outputDir, selectedAssetFilters);
            BuildTextures(log, sourceDir, outputDir, selectedAssetFilters);
            BuildFonts(log, sourceDir, outputDir, selectedAssetFilters);
            BuildMeshes(log, sourceDir, outputDir, selectedAssetFilters);
            log.Information("Build complete");
            return 0;
        }
        catch (Exception ex)
        {
            log.Error(ex, "Build failed");
            return 1;
        }
    }

    private static bool TryParseSelectedAssetFilters(string[] args, out HashSet<string> selectedAssetFilters)
    {
        selectedAssetFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int argumentIndex = 2;
        while (argumentIndex < args.Length)
        {
            if (!string.Equals(args[argumentIndex], "--asset", StringComparison.Ordinal))
            {
                return false;
            }

            int valueIndex = argumentIndex + 1;
            if (valueIndex >= args.Length)
            {
                return false;
            }

            if (!TryNormalizeRelativePath(args[valueIndex], out string normalizedRelativePath))
            {
                return false;
            }

            selectedAssetFilters.Add(normalizedRelativePath);
            argumentIndex += 2;
        }

        return true;
    }

    private static void BuildShaders(ILogger log, string sourceDir, string outputDir, HashSet<string> selectedAssetFilters)
    {
        var dbPath = Path.Combine(outputDir, "db", "shaders");
        Directory.CreateDirectory(dbPath);

        var importer = new ShaderImporter();
        var compiler = new ShaderCompiler(log);

        // Find all shader files and group by shader name
        var vertFiles = Directory.GetFiles(sourceDir, "*.vert", SearchOption.AllDirectories);
        var fragFiles = Directory.GetFiles(sourceDir, "*.frag", SearchOption.AllDirectories);
        if (selectedAssetFilters.Count > 0)
        {
            vertFiles = FilterBySelectedAssets(sourceDir, vertFiles, selectedAssetFilters);
            fragFiles = FilterBySelectedAssets(sourceDir, fragFiles, selectedAssetFilters);
        }

        // Group by base name (e.g., "colored_2d" from "colored_2d.vert")
        var shaderNames = vertFiles
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Distinct()
            .ToList();

        log.Information("Found {Count} shaders to compile", shaderNames.Count);

        var serializer = ContentModule.CreateSerializer();

        foreach (var shaderName in shaderNames)
        {
            var vertPath = vertFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == shaderName);
            var fragPath = fragFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == shaderName);

            if (vertPath == null || fragPath == null)
            {
                log.Warning("Shader {Name} missing vert or frag file, skipping", shaderName);
                continue;
            }

            // Import and compile both stages
            var vertItem = importer.Import(vertPath, shaderName + ".vert");
            var fragItem = importer.Import(fragPath, shaderName + ".frag");

            var vertSpirv = compiler.CompileToBytes(vertItem);
            var fragSpirv = compiler.CompileToBytes(fragItem);

            // Create compiled shader asset
            var compiledShader = new CompiledShader
            {
                VertexSpirv = vertSpirv,
                FragmentSpirv = fragSpirv
            };

            // Serialize with chunk header for ContentManager compatibility
            var assetBytes = ChunkHeader.Write(serializer, compiledShader);

            // Write to data/db/shaders/{name}.shader
            var outputPath = Path.Combine(dbPath, shaderName + ".shader");
            File.WriteAllBytes(outputPath, assetBytes);

            log.Information("  {Name}: vert={VertSize}b, frag={FragSize}b -> {Output}",
                shaderName, vertSpirv.Length, fragSpirv.Length, shaderName + ".shader");
        }

        // Compile compute shaders (standalone, not paired)
        var compFiles = Directory.GetFiles(sourceDir, "*.comp", SearchOption.AllDirectories);
        if (selectedAssetFilters.Count > 0)
        {
            compFiles = FilterBySelectedAssets(sourceDir, compFiles, selectedAssetFilters);
        }

        if (compFiles.Length > 0)
        {
            log.Information("Found {Count} compute shaders to compile", compFiles.Length);

            foreach (var compPath in compFiles)
            {
                var shaderName = Path.GetFileNameWithoutExtension(compPath);
                var compItem = importer.Import(compPath, shaderName + ".comp");
                var compSpirv = compiler.CompileToBytes(compItem);

                var compiledShader = new CompiledComputeShader
                {
                    Spirv = compSpirv
                };

                var assetBytes = ChunkHeader.Write(serializer, compiledShader);
                var outputPath = Path.Combine(dbPath, shaderName + ".compute");
                File.WriteAllBytes(outputPath, assetBytes);

                log.Information("  {Name}: comp={Size}b -> {Output}",
                    shaderName, compSpirv.Length, shaderName + ".compute");
            }
        }
    }

    private static void BuildTextures(ILogger log, string sourceDir, string outputDir, HashSet<string> selectedAssetFilters)
    {
        var dbPath = Path.Combine(outputDir, "db", "textures");
        Directory.CreateDirectory(dbPath);

        var importer = new TextureImporter();
        var compiler = new TextureCompiler(log);
        var serializer = ContentModule.CreateSerializer();

        // Find all image files
        var extensions = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga" };
        var imageFiles = extensions
            .SelectMany(ext => Directory.GetFiles(sourceDir, ext, SearchOption.AllDirectories))
            .ToList();
        if (selectedAssetFilters.Count > 0)
        {
            imageFiles = FilterBySelectedAssets(sourceDir, imageFiles, selectedAssetFilters).ToList();
        }

        if (imageFiles.Count == 0)
        {
            log.Debug("No textures found in {Dir}", sourceDir);
            return;
        }

        log.Information("Found {Count} textures to compile", imageFiles.Count);

        // Create a simple object database for the compiler
        var db = new InMemoryObjectDatabase();
        var legacyAliasOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var imagePath in imageFiles)
        {
            if (!TryGetNormalizedRelativePath(sourceDir, imagePath, out string relativeAssetPath))
            {
                continue;
            }

            var item = importer.Import(imagePath, relativeAssetPath);
            var objectId = compiler.Compile(item, db, serializer);

            // Get compiled data and wrap with chunk header
            var compiledData = db.Get(objectId);
            var assetBytes = ChunkHeader.Write(typeof(CompiledTexture), compiledData);

            // Write deterministic output to data/db/textures/{assets-relative-path}.texture
            string deterministicOutputRelativePath = relativeAssetPath + ".texture";
            string deterministicOutputPath = BuildOutputPath(dbPath, deterministicOutputRelativePath);
            File.WriteAllBytes(deterministicOutputPath, assetBytes);

            // Legacy compatibility alias: {basename}.texture when unambiguous.
            string legacyAliasRelativePath = Path.GetFileNameWithoutExtension(relativeAssetPath) + ".texture";
            TryWriteLegacyAlias(
                log,
                dbPath,
                legacyAliasOwners,
                legacyAliasRelativePath,
                deterministicOutputRelativePath,
                assetBytes);
        }
    }

    private static void BuildFonts(ILogger log, string sourceDir, string outputDir, HashSet<string> selectedAssetFilters)
    {
        var dbPath = Path.Combine(outputDir, "db", "fonts");
        Directory.CreateDirectory(dbPath);

        var compiler = new FontCompiler(log);
        var serializer = ContentModule.CreateSerializer();

        var extensions = new[] { "*.ttf", "*.otf" };
        var fontFiles = extensions
            .SelectMany(ext => Directory.GetFiles(sourceDir, ext, SearchOption.AllDirectories))
            .ToList();
        if (selectedAssetFilters.Count > 0)
        {
            fontFiles = FilterBySelectedAssets(sourceDir, fontFiles, selectedAssetFilters).ToList();
        }

        if (fontFiles.Count == 0)
        {
            log.Debug("No fonts found in {Dir}", sourceDir);
            return;
        }

        log.Information("Found {Count} fonts to compile", fontFiles.Count);

        var db = new InMemoryObjectDatabase();

        foreach (var fontPath in fontFiles)
        {
            if (!TryGetNormalizedRelativePath(sourceDir, fontPath, out _))
            {
                continue;
            }

            string fontName = Path.GetFileNameWithoutExtension(fontPath);
            string location = fontName + Path.GetExtension(fontPath);

            var asset = new FontAsset
            {
                Source = fontPath
            };

            var configPath = Path.Combine(Path.GetDirectoryName(fontPath) ?? sourceDir, fontName + ".fontcfg.json");
            if (File.Exists(configPath))
            {
                var configJson = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<FontBuildConfig>(configJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config == null)
                {
                    throw new InvalidOperationException($"Failed to parse font config: {configPath}");
                }

                if (config.FontSizePixels.HasValue) asset.FontSizePixels = config.FontSizePixels.Value;
                if (config.AtlasSizePixels.HasValue) asset.AtlasSizePixels = config.AtlasSizePixels.Value;
                if (config.FirstCodepoint.HasValue) asset.FirstCodepoint = config.FirstCodepoint.Value;
                if (config.LastCodepoint.HasValue) asset.LastCodepoint = config.LastCodepoint.Value;
                asset.BoldSource = ResolveOptionalSourcePath(config.BoldSource, Path.GetDirectoryName(configPath) ?? sourceDir);
                asset.ItalicSource = ResolveOptionalSourcePath(config.ItalicSource, Path.GetDirectoryName(configPath) ?? sourceDir);
                asset.BoldItalicSource = ResolveOptionalSourcePath(config.BoldItalicSource, Path.GetDirectoryName(configPath) ?? sourceDir);
            }

            var item = new AssetItem
            {
                Location = location,
                Asset = asset
            };
            var objectId = compiler.Compile(item, db, serializer);

            var compiledData = db.Get(objectId);
            var assetBytes = ChunkHeader.Write(typeof(CompiledFont), compiledData);

            var outputPath = Path.Combine(dbPath, fontName + ".font");
            File.WriteAllBytes(outputPath, assetBytes);
        }
    }

    private sealed class FontBuildConfig
    {
        public int? FontSizePixels { get; set; }
        public int? AtlasSizePixels { get; set; }
        public int? FirstCodepoint { get; set; }
        public int? LastCodepoint { get; set; }
        public string? BoldSource { get; set; }
        public string? ItalicSource { get; set; }
        public string? BoldItalicSource { get; set; }
    }

    private static string ResolveOptionalSourcePath(string? sourcePath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(sourcePath))
        {
            return sourcePath;
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, sourcePath));
    }

    private static void BuildMeshes(ILogger log, string sourceDir, string outputDir, HashSet<string> selectedAssetFilters)
    {
        var dbPath = Path.Combine(outputDir, "db", "meshes");
        Directory.CreateDirectory(dbPath);

        var importer = new MeshImporter();
        var compiler = new MeshCompiler(log);
        var serializer = ContentModule.CreateSerializer();

        // Find all model files
        var extensions = new[] { "*.obj", "*.fbx", "*.gltf", "*.glb", "*.dae", "*.3ds" };
        var modelFiles = extensions
            .SelectMany(ext => Directory.GetFiles(sourceDir, ext, SearchOption.AllDirectories))
            .ToList();
        if (selectedAssetFilters.Count > 0)
        {
            modelFiles = FilterBySelectedAssets(sourceDir, modelFiles, selectedAssetFilters).ToList();
        }

        if (modelFiles.Count == 0)
        {
            log.Debug("No meshes found in {Dir}", sourceDir);
            return;
        }

        log.Information("Found {Count} meshes to compile", modelFiles.Count);

        var db = new InMemoryObjectDatabase();
        var legacyAliasOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var modelPath in modelFiles)
        {
            if (!TryGetNormalizedRelativePath(sourceDir, modelPath, out string relativeAssetPath))
            {
                continue;
            }

            var item = importer.Import(modelPath, relativeAssetPath);
            var objectId = compiler.Compile(item, db, serializer);

            // Get compiled data and wrap with chunk header
            var compiledData = db.Get(objectId);
            var assetBytes = ChunkHeader.Write(typeof(CompiledMesh), compiledData);

            // Write deterministic output to data/db/meshes/{assets-relative-path}.mesh
            string deterministicOutputRelativePath = relativeAssetPath + ".mesh";
            string deterministicOutputPath = BuildOutputPath(dbPath, deterministicOutputRelativePath);
            File.WriteAllBytes(deterministicOutputPath, assetBytes);

            // Legacy compatibility alias: {basename}.mesh when unambiguous.
            string legacyAliasRelativePath = Path.GetFileNameWithoutExtension(relativeAssetPath) + ".mesh";
            TryWriteLegacyAlias(
                log,
                dbPath,
                legacyAliasOwners,
                legacyAliasRelativePath,
                deterministicOutputRelativePath,
                assetBytes);
        }
    }

    private static void TryWriteLegacyAlias(
        ILogger log,
        string outputRootPath,
        Dictionary<string, string> aliasOwners,
        string legacyAliasRelativePath,
        string deterministicOutputRelativePath,
        byte[] assetBytes)
    {
        if (legacyAliasRelativePath.Length == 0)
        {
            return;
        }

        if (!aliasOwners.TryGetValue(legacyAliasRelativePath, out string? existingDeterministicOwner))
        {
            aliasOwners[legacyAliasRelativePath] = deterministicOutputRelativePath;

            if (string.Equals(legacyAliasRelativePath, deterministicOutputRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string legacyAliasPath = BuildOutputPath(outputRootPath, legacyAliasRelativePath);
            File.WriteAllBytes(legacyAliasPath, assetBytes);
            return;
        }

        if (!string.Equals(existingDeterministicOwner, deterministicOutputRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            log.Warning(
                "Skipping legacy alias {Alias} because it collides between {OwnerA} and {OwnerB}",
                legacyAliasRelativePath,
                existingDeterministicOwner,
                deterministicOutputRelativePath);
        }
    }

    private static string[] FilterBySelectedAssets(
        string sourceDir,
        IEnumerable<string> candidatePaths,
        HashSet<string> selectedAssetFilters)
    {
        if (selectedAssetFilters.Count == 0)
        {
            return candidatePaths.ToArray();
        }

        var filteredPaths = new List<string>();
        foreach (string candidatePath in candidatePaths)
        {
            if (!TryGetNormalizedRelativePath(sourceDir, candidatePath, out string relativeAssetPath))
            {
                continue;
            }

            if (!selectedAssetFilters.Contains(relativeAssetPath))
            {
                continue;
            }

            filteredPaths.Add(candidatePath);
        }

        return filteredPaths.ToArray();
    }

    private static bool TryGetNormalizedRelativePath(string rootPath, string filePath, out string normalizedRelativePath)
    {
        normalizedRelativePath = "";
        string relativePath = Path.GetRelativePath(rootPath, filePath);
        return TryNormalizeRelativePath(relativePath, out normalizedRelativePath);
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

    private static string BuildOutputPath(string rootPath, string normalizedRelativePathWithSuffix)
    {
        string relativePath = normalizedRelativePathWithSuffix.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.Combine(rootPath, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }

    /// <summary>
    /// Simple in-memory object database for build-time compilation.
    /// </summary>
    private sealed class InMemoryObjectDatabase : IObjectDatabase
    {
        private readonly Dictionary<ObjectId, byte[]> _objects = new();
        private int _nextId;

        public ObjectId Put(byte[] data)
        {
            var id = new ObjectId(Interlocked.Increment(ref _nextId).ToString());
            _objects[id] = data;
            return id;
        }

        public byte[] Get(ObjectId id) => _objects[id];

        public bool Exists(ObjectId id) => _objects.ContainsKey(id);

        public bool Has(ObjectId id) => _objects.ContainsKey(id);
    }
}
