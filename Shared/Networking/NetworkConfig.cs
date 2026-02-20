using System.Net;

namespace Networking;

/// <summary>
/// Configuration for network connection including the profile and optional NAT punch server override.
/// </summary>
public readonly record struct NetworkConfig(
    NetworkProfile Profile,
    IPEndPoint? NatPunchServerOverride = null,
    bool DisableNatPunch = false);
