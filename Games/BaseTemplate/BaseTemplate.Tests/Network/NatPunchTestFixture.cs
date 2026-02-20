namespace BaseTemplate.Tests.Network;

/// <summary>
/// Test fixture that provides a mock NAT punch server for integration tests.
/// Use with IClassFixture to share across test methods.
/// </summary>
public sealed class NatPunchTestFixture : IAsyncLifetime
{
    public MockNatPunchServer NatServer { get; private set; } = null!;
    public int NatServerPort => NatServer.Port;

    public Task InitializeAsync()
    {
        // Use port 0 to get an ephemeral port
        NatServer = new MockNatPunchServer(0);
        NatServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        NatServer.Dispose();
        return Task.CompletedTask;
    }
}
