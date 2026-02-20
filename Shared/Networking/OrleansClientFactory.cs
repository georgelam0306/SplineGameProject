using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Serilog;

namespace Networking;

/// <summary>
/// Factory for creating and managing IClusterClient connections.
/// </summary>
public static class OrleansClientFactory
{
    /// <summary>
    /// Creates an IClusterClient configured for localhost development.
    /// </summary>
    public static async Task<IClusterClient> CreateLocalClientAsync()
    {
        var client = new HostBuilder()
            .UseOrleansClient(clientBuilder =>
            {
                clientBuilder.UseLocalhostClustering();
            })
            .Build();

        await client.StartAsync();
        return client.Services.GetRequiredService<IClusterClient>();
    }

    /// <summary>
    /// Creates an IClusterClient configured for a specific server address.
    /// </summary>
    public static async Task<IClusterClient> CreateClientAsync(string serverAddress, int gatewayPort = 30000)
    {
        var client = new HostBuilder()
            .UseOrleansClient(clientBuilder =>
            {
                clientBuilder.UseStaticClustering(new System.Net.IPEndPoint(
                    System.Net.IPAddress.Parse(serverAddress),
                    gatewayPort));

                // Must match server's ClusterOptions
                clientBuilder.Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "derptech-matchmaking";
                    options.ServiceId = "DerpTechMatchmaking";
                });
            })
            .Build();

        await client.StartAsync();
        return client.Services.GetRequiredService<IClusterClient>();
    }

    /// <summary>
    /// Production matchmaking server address.
    /// </summary>
    private const string ProductionServer = "45.76.79.231";

    /// <summary>
    /// Creates an IClusterClient, auto-detecting local vs remote based on MATCHMAKING_SERVER env var.
    /// Falls back to production server if no env var is set.
    /// Set MATCHMAKING_SERVER=localhost for local development.
    /// </summary>
    public static async Task<IClusterClient> CreateClientAutoAsync()
    {
        var serverAddress = Environment.GetEnvironmentVariable("MATCHMAKING_SERVER");
        var gatewayPort = int.Parse(Environment.GetEnvironmentVariable("MATCHMAKING_PORT") ?? "30000");

        // Use production server as default if no env var is set
        if (string.IsNullOrEmpty(serverAddress))
        {
            serverAddress = ProductionServer;
        }

        // Special case: "localhost" uses the simpler localhost clustering
        if (serverAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug("[Orleans] Connecting to localhost...");
            return await CreateLocalClientAsync();
        }

        Log.Debug("[Orleans] Connecting to {ServerAddress}:{GatewayPort}...", serverAddress, gatewayPort);
        return await CreateClientAsync(serverAddress, gatewayPort);
    }
}
