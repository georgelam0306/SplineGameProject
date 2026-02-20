using DerpLib.AssetPipeline;
using Serilog;
using StbImageSharp;

namespace DerpLib.Build;

[Compiler(typeof(TextureAsset))]
public sealed class TextureCompiler : IAssetCompiler
{
    private readonly ILogger _log;

    public TextureCompiler(ILogger log)
    {
        _log = log;
    }

    public IEnumerable<string> GetInputFiles(AssetItem item)
    {
        var asset = (TextureAsset)item.Asset;
        yield return asset.Source;
    }

    public ObjectId Compile(AssetItem item, IObjectDatabase db, IBlobSerializer serializer)
    {
        var asset = (TextureAsset)item.Asset;
        var sourceFile = asset.Source;

        if (!File.Exists(sourceFile))
        {
            throw new FileNotFoundException($"Texture source not found: {sourceFile}");
        }

        // Load and decode image to RGBA8
        ImageResult image;
        using (var stream = File.OpenRead(sourceFile))
        {
            image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        }

        _log.Information("Compiled {Source} -> {Width}x{Height} ({Size} bytes)",
            Path.GetFileName(sourceFile), image.Width, image.Height, image.Data.Length);

        // Store as compiled texture
        var compiled = new Assets.CompiledTexture
        {
            Width = image.Width,
            Height = image.Height,
            Pixels = image.Data
        };

        return db.Put(serializer.Serialize(compiled));
    }
}
