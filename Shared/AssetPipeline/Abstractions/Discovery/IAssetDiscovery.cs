using Serilog;

namespace DerpLib.AssetPipeline;

public interface IAssetDiscovery
{
    void DiscoverInto(IAssetRegistry registry, DiscoveryOptions options, ILogger? logger = null);
}
