using System.Text;
using DerpLib.AssetPipeline;
using DerpLib.AssetPipeline.AotTest;
using DerpLib.Vfs;
using Serilog;

// Setup logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

var logger = Log.Logger;

try
{
    logger.Information("=== Asset Pipeline AOT Test (with Source Generator) ===");

    // Create temp directory for test
    var tempDir = Path.Combine(Path.GetTempPath(), "aot-test-" + Guid.NewGuid().ToString("N")[..8]);
    var contentDir = Path.Combine(tempDir, "db-content");
    Directory.CreateDirectory(Path.Combine(contentDir, "assets"));

    logger.Information("Test directory: {Dir}", tempDir);

    // === Test 1: Generated ContentModule.CreateSerializer ===
    logger.Information("=== Test 1: Generated ContentModule.CreateSerializer ===");

    var serializer = ContentModule.CreateSerializer();

    var testAsset = new TestAsset
    {
        Name = "TestItem",
        Value = 42,
        Tags = new[] { "aot", "test", "asset" }
    };

    var assetBytes = serializer.Serialize(testAsset);
    var loadedAsset = serializer.Deserialize<TestAsset>(assetBytes);

    if (loadedAsset.Name != testAsset.Name)
        throw new Exception($"Name mismatch: expected '{testAsset.Name}', got '{loadedAsset.Name}'");

    logger.Information("ContentModule.CreateSerializer verified: Name={Name}, Value={Value}",
        loadedAsset.Name, loadedAsset.Value);

    // === Test 2: Generated ContentModule.CreateContentManager ===
    logger.Information("=== Test 2: Generated ContentModule.CreateContentManager ===");

    var vfs = new VirtualFileSystem(logger);
    vfs.MountPhysical("/data/db", contentDir);

    var contentManager = ContentModule.CreateContentManager(vfs, logger);

    // Write chunk-formatted content directly to the VFS path
    var assetChunkBytes = ChunkHeader.Write(serializer, testAsset);
    File.WriteAllBytes(Path.Combine(contentDir, "assets", "test.asset"), assetChunkBytes);

    var cmAsset = contentManager.Load<TestAsset>("assets/test.asset");

    if (cmAsset.Name != testAsset.Name)
        throw new Exception($"ContentManager load mismatch: expected '{testAsset.Name}', got '{cmAsset.Name}'");

    logger.Information("ContentModule.CreateContentManager verified: Name={Name}, Value={Value}",
        cmAsset.Name, cmAsset.Value);

    // === Test 3: Generated Content static facade ===
    logger.Information("=== Test 3: Generated Content static facade ===");

    // Create a fresh VFS for the static facade test
    var vfs2 = new VirtualFileSystem(logger);
    vfs2.MountPhysical("/data/db", contentDir);

    Content.Initialize(vfs2, logger);

    if (!Content.IsInitialized)
        throw new Exception("Content.IsInitialized should be true");

    var staticAsset = Content.Load<TestAsset>("assets/test.asset");

    if (staticAsset.Name != testAsset.Name)
        throw new Exception($"Content.Load mismatch: expected '{testAsset.Name}', got '{staticAsset.Name}'");

    logger.Information("Content static facade verified: Name={Name}, Value={Value}",
        staticAsset.Name, staticAsset.Value);

    // === Test 4: Package file with Content.Initialize(packagePath) ===
    logger.Information("=== Test 4: Package-based initialization ===");

    // Create package with content at root (will be mounted at /data/db)
    var packageSourceDir = Path.Combine(tempDir, "package-source");
    Directory.CreateDirectory(Path.Combine(packageSourceDir, "assets"));

    var testConfig = new TestConfig { Enabled = true, Scale = 2.5f };
    var configChunkBytes = ChunkHeader.Write(serializer, testConfig);
    File.WriteAllBytes(Path.Combine(packageSourceDir, "assets", "test.config"), configChunkBytes);

    var packagePath = Path.Combine(tempDir, "game.pak");
    PackageFileProvider.CreatePackage(packagePath, packageSourceDir, compress: true);

    logger.Information("Created package: {Path}", packagePath);

    // Note: Content is already initialized, so we test via ContentModule directly
    var pkgVfs = new VirtualFileSystem(logger);
    pkgVfs.RegisterProvider(new PackageFileProvider("/data/db", packagePath));
    var pkgContent = ContentModule.CreateContentManager(pkgVfs, logger);

    var pkgConfig = pkgContent.Load<TestConfig>("assets/test.config");

    if (!pkgConfig.Enabled)
        throw new Exception("TestConfig.Enabled should be true");
    if (Math.Abs(pkgConfig.Scale - 2.5f) > 0.001f)
        throw new Exception($"TestConfig.Scale mismatch: expected 2.5, got {pkgConfig.Scale}");

    logger.Information("Package content verified: Enabled={Enabled}, Scale={Scale}",
        pkgConfig.Enabled, pkgConfig.Scale);

    // === Cleanup ===
    logger.Information("Cleaning up...");
    Directory.Delete(tempDir, recursive: true);

    logger.Information("=== ALL TESTS PASSED ===");
    return 0;
}
catch (Exception ex)
{
    logger.Error(ex, "Test failed!");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
