using DerpLib.AssetPipeline;
using DerpLib.Vfs;
using Serilog;

namespace DerpLib.Assets;

/// <summary>
/// Factory for creating AOT-compatible ContentManager.
/// Implementation provided by AssetPipeline.Generator.
/// </summary>
public static partial class ContentModule
{
    /// <summary>
    /// Creates a ContentManager with AOT-compatible serialization for all compiled asset types.
    /// </summary>
    public static partial ContentManager CreateContentManager(VirtualFileSystem vfs, ILogger? logger = null);
}
