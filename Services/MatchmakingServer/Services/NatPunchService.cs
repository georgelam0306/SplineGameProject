using System.Collections.Concurrent;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace DerpTech2D.MatchmakingServer.Services;

public sealed class NatPunchService : BackgroundService, INatPunchListener
{
    private readonly NetManager _server;
    private readonly ConcurrentDictionary<string, IPEndPoint> _registeredPeers;
    private readonly ConcurrentDictionary<string, string> _pendingIntroductions;
    private readonly ILogger<NatPunchService> _logger;
    private const int NatPunchPort = 5051;

    public NatPunchService(ILogger<NatPunchService> logger)
    {
        _logger = logger;
        _registeredPeers = new ConcurrentDictionary<string, IPEndPoint>();
        _pendingIntroductions = new ConcurrentDictionary<string, string>();

        EventBasedNetListener listener = new EventBasedNetListener();
        _server = new NetManager(listener)
        {
            NatPunchEnabled = true,
            UnconnectedMessagesEnabled = true
        };

        _server.NatPunchModule.Init(this);

        listener.NetworkReceiveUnconnectedEvent += OnUnconnectedMessage;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _server.Start(NatPunchPort);
        _logger.LogInformation("NAT Punch server started on UDP port {Port}", NatPunchPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            _server.PollEvents();
            _server.NatPunchModule.PollEvents();
            await Task.Delay(15, stoppingToken);
        }

        _server.Stop();
    }

    private void OnUnconnectedMessage(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        if (reader.AvailableBytes < 1)
        {
            return;
        }

        byte msgType = reader.GetByte();

        switch (msgType)
        {
            case 1:
                HandleRegister(remoteEndPoint, reader);
                break;
            case 2:
                HandleRequestIntroduction(remoteEndPoint, reader);
                break;
        }
    }

    private void HandleRegister(IPEndPoint remoteEndPoint, NetPacketReader reader)
    {
        if (reader.AvailableBytes < 1)
        {
            return;
        }

        string token = reader.GetString();
        _registeredPeers[token] = remoteEndPoint;
        _logger.LogInformation("Peer registered: {Token} -> {Endpoint}", token, remoteEndPoint);

        NetDataWriter writer = new NetDataWriter();
        writer.Put((byte)1);
        writer.Put(true);
        _server.SendUnconnectedMessage(writer, remoteEndPoint);

        if (_pendingIntroductions.TryRemove(token, out string? hostToken))
        {
            if (_registeredPeers.TryGetValue(hostToken, out IPEndPoint? hostEndpoint))
            {
                PerformIntroduction(token, remoteEndPoint, hostToken, hostEndpoint);
            }
        }
    }

    private void HandleRequestIntroduction(IPEndPoint clientEndPoint, NetPacketReader reader)
    {
        if (reader.AvailableBytes < 2)
        {
            return;
        }

        string clientToken = reader.GetString();
        string hostToken = reader.GetString();

        _registeredPeers[clientToken] = clientEndPoint;
        _logger.LogInformation("Client {ClientToken} requesting introduction to host {HostToken}", clientToken, hostToken);

        if (_registeredPeers.TryGetValue(hostToken, out IPEndPoint? hostEndpoint))
        {
            PerformIntroduction(clientToken, clientEndPoint, hostToken, hostEndpoint);
        }
        else
        {
            _pendingIntroductions[clientToken] = hostToken;
            _logger.LogInformation("Host {HostToken} not yet registered, queuing introduction", hostToken);
        }
    }

    private void PerformIntroduction(string clientToken, IPEndPoint clientEndpoint, string hostToken, IPEndPoint hostEndpoint)
    {
        _logger.LogInformation(
            "Introducing {ClientToken} ({ClientEndpoint}) <-> {HostToken} ({HostEndpoint})",
            clientToken, clientEndpoint, hostToken, hostEndpoint);

        try
        {
            _server.NatPunchModule.NatIntroduce(
                hostEndpoint,
                hostEndpoint,
                clientEndpoint,
                clientEndpoint,
                hostToken);
            _logger.LogInformation("NatIntroduce called successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NatIntroduce failed");
        }
    }

    void INatPunchListener.OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
    {
        _logger.LogDebug("NAT introduction request from {Remote}, token: {Token}", remoteEndPoint, token);
    }

    void INatPunchListener.OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
    {
        _logger.LogInformation("NAT introduction success: {Target}, type: {Type}, token: {Token}", targetEndPoint, type, token);
    }
}

