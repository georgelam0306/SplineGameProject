using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Catrillion.Tests.Network;

/// <summary>
/// Mock NAT punch server for integration testing.
/// Simulates the NAT punch protocol for testing peer-to-peer connections.
/// </summary>
public sealed class MockNatPunchServer : IDisposable
{
    private readonly UdpClient _udpClient;
    private readonly Dictionary<string, IPEndPoint> _registeredPeers = new();
    private readonly int _port;
    private bool _running;

    public int Port => _port;
    public int RegisteredPeerCount => _registeredPeers.Count;

    public MockNatPunchServer(int port = 0)
    {
        _port = port;
        _udpClient = new UdpClient(port);
        _udpClient.Client.Blocking = false;

        // Get actual port if we used 0 (ephemeral)
        if (port == 0)
        {
            _port = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;
        }
    }

    public void Start()
    {
        _running = true;
    }

    public void Stop()
    {
        _running = false;
    }

    /// <summary>
    /// Poll for incoming messages and process them.
    /// Call this repeatedly during tests.
    /// </summary>
    public void Poll()
    {
        if (!_running) return;

        // Try to receive without blocking
        while (_udpClient.Available > 0)
        {
            try
            {
                IPEndPoint remoteEp = new(IPAddress.Any, 0);
                byte[] data = _udpClient.Receive(ref remoteEp);

                if (data.Length < 1) continue;

                byte msgType = data[0];
                switch (msgType)
                {
                    case 1: // Registration
                        HandleRegistration(data, remoteEp);
                        break;
                    case 2: // Punch request
                        HandlePunchRequest(data, remoteEp);
                        break;
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                // No more data available
                break;
            }
        }
    }

    private void HandleRegistration(byte[] data, IPEndPoint remoteEp)
    {
        // Format: [1:msgType] [string:token]
        if (data.Length < 2) return;

        int pos = 1;
        string token = ReadString(data, ref pos);

        if (!string.IsNullOrEmpty(token))
        {
            _registeredPeers[token] = remoteEp;
            Console.WriteLine($"[MockNatPunchServer] Registered: {token} from {remoteEp}");

            // Send registration confirmation
            SendRegistrationConfirmation(remoteEp, true);
        }
    }

    private void HandlePunchRequest(byte[] data, IPEndPoint remoteEp)
    {
        // Format: [1:msgType] [string:localToken] [string:targetToken]
        if (data.Length < 3) return;

        int pos = 1;
        string localToken = ReadString(data, ref pos);
        string targetToken = ReadString(data, ref pos);

        Console.WriteLine($"[MockNatPunchServer] Punch request: {localToken} -> {targetToken}");

        // Register the requester if not already registered
        if (!string.IsNullOrEmpty(localToken))
        {
            _registeredPeers[localToken] = remoteEp;
        }

        // Check if target is registered
        if (_registeredPeers.TryGetValue(targetToken, out IPEndPoint? targetEp))
        {
            // Send introduction to both parties
            // This triggers LiteNetLib's NatPunchModule to call OnNatIntroductionSuccess
            SendNatIntroduction(remoteEp, targetEp, targetToken);
            SendNatIntroduction(targetEp, remoteEp, localToken);

            Console.WriteLine($"[MockNatPunchServer] Sent introductions between {remoteEp} and {targetEp}");
        }
        else
        {
            Console.WriteLine($"[MockNatPunchServer] Target {targetToken} not registered");
        }
    }

    private void SendRegistrationConfirmation(IPEndPoint target, bool success)
    {
        // Format: [1:msgType] [1:success]
        byte[] response = new byte[2];
        response[0] = 1;  // Registration confirmation type
        response[1] = success ? (byte)1 : (byte)0;
        _udpClient.Send(response, response.Length, target);
    }

    private void SendNatIntroduction(IPEndPoint target, IPEndPoint peerEndpoint, string token)
    {
        // LiteNetLib's NAT punch module expects a specific format for introductions
        // Format: NatPunchModule internal format - we'll use the NatPunchModule's built-in format
        // The format is: [addressType:1] [address bytes] [port:2] [token string]

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Address type (0 = IPv4, 1 = IPv6)
        byte addressType = peerEndpoint.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)1 : (byte)0;
        writer.Write(addressType);

        // Address bytes
        byte[] addressBytes = peerEndpoint.Address.GetAddressBytes();
        writer.Write(addressBytes);

        // Port (2 bytes, little endian)
        writer.Write((ushort)peerEndpoint.Port);

        // Token (LiteNetLib string format: length prefixed)
        byte[] tokenBytes = Encoding.UTF8.GetBytes(token);
        writer.Write(tokenBytes.Length);
        writer.Write(tokenBytes);

        byte[] data = ms.ToArray();

        // Send via NatPunchModule's expected channel
        // Note: In real LiteNetLib, this goes through NatPunchModule.OnNatIntroductionResponse
        // For testing, we just send the punch result directly
        _udpClient.Send(data, data.Length, target);
    }

    private static string ReadString(byte[] data, ref int pos)
    {
        if (pos >= data.Length) return string.Empty;

        // LiteNetLib NetDataWriter format: 4-byte length prefix + UTF8 bytes
        if (pos + 4 > data.Length) return string.Empty;

        int length = BitConverter.ToInt32(data, pos);
        pos += 4;

        if (length <= 0 || pos + length > data.Length) return string.Empty;

        string result = Encoding.UTF8.GetString(data, pos, length);
        pos += length;
        return result;
    }

    public void Dispose()
    {
        _running = false;
        _udpClient.Dispose();
    }
}
