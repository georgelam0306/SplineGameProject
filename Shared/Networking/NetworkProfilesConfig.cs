namespace Networking;

public enum NetworkProfile
{
    None,      // No simulation (production)
    Mild,      // Good wifi: 20-40ms, 1% loss
    Moderate,  // Typical internet: 50-100ms, 5% loss
    Harsh      // Stress test: 100-200ms, 10% loss
}

public static class NetworkProfilesConfig
{
    public static (bool enabled, int minLatency, int maxLatency, int packetLossPercent) GetSettings(NetworkProfile profile)
    {
        return profile switch
        {
            NetworkProfile.Mild => (true, 20, 40, 1),
            NetworkProfile.Moderate => (true, 50, 100, 5),
            NetworkProfile.Harsh => (true, 200, 400, 10),
            _ => (false, 0, 0, 0)
        };
    }
}
