using System.Net;
using Catrillion.Core;
using Catrillion.OnlineServices;
using Xunit;

namespace Catrillion.Tests.Network;

/// <summary>
/// Unit tests for NAT punch-through functionality.
/// Tests the NetworkService state management and configuration.
/// Note: Full connection tests require NetworkCoordinator for handshaking.
/// These tests focus on NAT punch state machine and configuration.
/// </summary>
[Collection("NetworkTests")]
public class NatPunchThroughTests
{
    private static int _portCounter = 19000;

    private static int GetUniquePort() => Interlocked.Increment(ref _portCounter);

    private NetworkService CreateNetworkService(IPEndPoint? natServer = null, bool disableNatPunch = false)
    {
        var config = new NetworkConfig(NetworkProfile.None, natServer, disableNatPunch);
        return new NetworkService(config);
    }

    [Fact]
    public void HostStartsWithCompleteMesh()
    {
        using var host = CreateNetworkService(disableNatPunch: true);
        int hostPort = GetUniquePort();
        var hostId = Guid.NewGuid();

        host.StartAsCoordinator(hostId, "Host", hostPort);

        // A solo host should have a complete mesh (no other peers to connect to)
        Assert.True(host.IsMeshComplete, "Solo host should have complete mesh");
        Assert.True(host.IsCoordinator, "Host should be coordinator");
        Assert.Equal(0, host.LocalSlot);
    }

    [Fact]
    public void HostStartsWithCompleteMeshWithNatPunch()
    {
        using var host = CreateNetworkService(disableNatPunch: true);
        int hostPort = GetUniquePort();
        var hostId = Guid.NewGuid();
        string matchId = Guid.NewGuid().ToString();

        host.StartAsCoordinatorWithNatPunch(hostId, "Host", matchId, hostPort);

        Assert.True(host.IsMeshComplete, "Solo host should have complete mesh");
        Assert.True(host.IsCoordinator, "Host should be coordinator");
        Assert.Equal(0, host.LocalSlot);
    }

    [Fact]
    public void ClientEntersWaitingForNatPunchState()
    {
        // Use a non-responsive endpoint to ensure NAT punch doesn't succeed immediately
        var fakeNatServer = new IPEndPoint(IPAddress.Loopback, 59999);
        using var client = CreateNetworkService(fakeNatServer);

        int hostPort = GetUniquePort();
        var hostId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        string matchId = Guid.NewGuid().ToString();

        // Client joins with NAT punch - this should set WaitingForNatPunch
        client.JoinMeshWithNatPunch(clientId, "Client", matchId, hostId, "127.0.0.1", hostPort);

        Assert.True(client.WaitingForNatPunch, "Client should be waiting for NAT punch");
        Assert.False(client.IsCoordinator, "Client should not be coordinator");
    }

    [Fact]
    public void NatPunchDisabledDoesNotWait()
    {
        using var client = CreateNetworkService(disableNatPunch: true);

        int hostPort = GetUniquePort();
        var hostId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        string matchId = Guid.NewGuid().ToString();

        // When NAT punch is disabled, JoinMeshWithNatPunch should fall back immediately
        client.JoinMeshWithNatPunch(clientId, "Client", matchId, hostId, "127.0.0.1", hostPort);

        // Should not be waiting because NAT is disabled
        Assert.False(client.WaitingForNatPunch, "Should not wait when NAT punch disabled");
    }

    [Fact]
    public void NatPunchServerOverrideIsUsed()
    {
        var customEndpoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 5555);
        using var service = CreateNetworkService(customEndpoint);

        // The service should accept the custom endpoint without throwing
        // (We can't easily verify it's used without actually connecting)
        Assert.NotNull(service);
    }

    [Fact]
    public void NatPunchTimeoutEventFires()
    {
        // Use a non-responsive NAT server endpoint
        var badNatServer = new IPEndPoint(IPAddress.Loopback, 1);
        using var client = CreateNetworkService(badNatServer);

        int hostPort = GetUniquePort();
        var hostId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        string matchId = Guid.NewGuid().ToString();

        bool natPunchFailed = false;
        client.OnNatPunchResult += (success, _) =>
        {
            if (!success) natPunchFailed = true;
        };

        client.JoinMeshWithNatPunch(clientId, "Client", matchId, hostId, "127.0.0.1", hostPort);

        Assert.True(client.WaitingForNatPunch, "Client should initially wait for NAT punch");

        // Poll until NAT punch times out (10s timeout)
        int polls = 0;
        while (!natPunchFailed && polls < 1200)  // 12 seconds max
        {
            client.Poll();
            Thread.Sleep(10);
            polls++;
        }

        Assert.True(natPunchFailed, "NAT punch should have failed/timed out");
        Assert.False(client.WaitingForNatPunch, "Should no longer be waiting after timeout");
    }

    [Fact]
    public void LowLevelConnectionWorks()
    {
        // Test that LiteNetLib low-level connection works (OnPeerConnected callback)
        using var host = CreateNetworkService(disableNatPunch: true);
        using var client = CreateNetworkService(disableNatPunch: true);

        int hostPort = GetUniquePort();
        var hostId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        bool hostReceivedConnection = false;

        // Subscribe to low-level connection event via OnPeerJoinReceived
        // (This fires when coordinator receives a join request)
        host.OnPeerJoinReceived += (_, _, _) => hostReceivedConnection = true;

        // For client, we can check if it gets past connection request
        // by looking for OnPeerListReceived (but that requires NetworkCoordinator)

        // Start host
        host.StartAsCoordinator(hostId, "Host", hostPort);

        // Client joins - this triggers ClientJoin message to host
        client.JoinMesh(clientId, "Client", "127.0.0.1", hostPort);

        // Poll until host receives join request
        int polls = 0;
        while (!hostReceivedConnection && polls < 100)
        {
            host.Poll();
            client.Poll();
            Thread.Sleep(10);
            polls++;
        }

        Assert.True(hostReceivedConnection, "Host should receive join request from client");
    }

    [Fact]
    public void MultipleConfigurationsWork()
    {
        // Test creating multiple services with different configurations
        using var service1 = CreateNetworkService(disableNatPunch: true);
        using var service2 = CreateNetworkService(new IPEndPoint(IPAddress.Loopback, 5555));
        using var service3 = CreateNetworkService();  // Default - resolves production server

        int port1 = GetUniquePort();
        int port2 = GetUniquePort();
        int port3 = GetUniquePort();

        service1.StartAsCoordinator(Guid.NewGuid(), "Service1", port1);
        service2.StartAsCoordinator(Guid.NewGuid(), "Service2", port2);
        service3.StartAsCoordinator(Guid.NewGuid(), "Service3", port3);

        // All should start successfully
        Assert.True(service1.IsCoordinator);
        Assert.True(service2.IsCoordinator);
        Assert.True(service3.IsCoordinator);
    }
}
