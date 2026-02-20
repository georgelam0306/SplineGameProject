using System.Net;
using DerpTech2D.MatchmakingServer.Services;
using MatchmakingContracts.Grains;
using MatchmakingContracts.Models;
using Orleans.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5050");

// Check if running in production mode (SERVER_IP environment variable set)
var serverIp = Environment.GetEnvironmentVariable("SERVER_IP");
var isProduction = !string.IsNullOrEmpty(serverIp);

builder.Host.UseOrleans(siloBuilder =>
{
    if (isProduction && serverIp != null)
    {
        // Production: bind to public IP for remote client connections
        var siloPort = int.Parse(Environment.GetEnvironmentVariable("ORLEANS_SILO_PORT") ?? "11111");
        var gatewayPort = int.Parse(Environment.GetEnvironmentVariable("ORLEANS_GATEWAY_PORT") ?? "30000");

        Console.WriteLine($"[Orleans] Production mode: {serverIp}:{siloPort} (silo), :{gatewayPort} (gateway)");

        // Use development clustering for single-server deployment
        // This provides in-memory membership without external dependencies
        var siloEndpoint = new IPEndPoint(IPAddress.Parse(serverIp), siloPort);
        siloBuilder.UseDevelopmentClustering(siloEndpoint);

        siloBuilder.ConfigureEndpoints(
            advertisedIP: IPAddress.Parse(serverIp),
            siloPort: siloPort,
            gatewayPort: gatewayPort,
            listenOnAnyHostAddress: true);

        siloBuilder.Configure<ClusterOptions>(options =>
        {
            options.ClusterId = "derptech-matchmaking";
            options.ServiceId = "DerpTechMatchmaking";
        });
    }
    else
    {
        // Development: localhost only
        Console.WriteLine("[Orleans] Development mode: localhost clustering");
        siloBuilder.UseLocalhostClustering();
    }

    siloBuilder.AddMemoryGrainStorage("Default");
});

builder.Services.AddHostedService<NatPunchService>();
builder.Services.AddHostedService<MatchCleanupService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("healthy"));

// === HTTP Matchmaking API (AOT-compatible alternative to Orleans RPC) ===

// List all open lobbies
app.MapGet("/api/lobbies", async (IGrainFactory grains) =>
{
    var matchmakingGrain = grains.GetGrain<IMatchmakingGrain>("global");
    var matches = await matchmakingGrain.ListOpenMatches();

    return Results.Ok(new
    {
        lobbies = matches.Select(m => new
        {
            lobbyId = m.MatchId,
            hostName = m.HostName,
            playerCount = m.PlayerCount,
            maxPlayers = m.MaxPlayers
        })
    });
});

// Create a new lobby
app.MapPost("/api/lobbies", async (HttpContext ctx, IGrainFactory grains, CreateLobbyRequest request) =>
{
    var matchmakingGrain = grains.GetGrain<IMatchmakingGrain>("global");
    string matchId = await matchmakingGrain.CreateMatch();

    var matchGrain = grains.GetGrain<IMatchGrain>(matchId);

    // Set visibility and password if private
    if (request.IsPrivate)
    {
        await matchGrain.SetVisibility(LobbyVisibility.Private);
        if (!string.IsNullOrEmpty(request.PasswordHash))
        {
            await matchGrain.SetPassword(request.PasswordHash);
        }
    }

    // Get client IP from request if not provided
    var publicIp = request.PublicIp;
    if (string.IsNullOrEmpty(publicIp))
    {
        publicIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
    }

    // Add host player
    var (playerId, success, error) = await matchGrain.AddPlayer(
        request.DisplayName, publicIp, request.Port, isHost: true);

    if (!success)
    {
        return Results.Ok(new { success = false, error = error ?? "Failed to create lobby" });
    }

    var matchInfo = await matchGrain.GetMatchInfo();
    var host = matchInfo.Players.Find(p => p.IsHost);

    return Results.Ok(new
    {
        success = true,
        lobbyId = matchId,
        playerId = playerId,
        lobbyInfo = new
        {
            lobbyId = matchId,
            hostName = host?.DisplayName ?? request.DisplayName,
            hostIp = host?.PublicIp ?? publicIp,
            hostPort = request.Port,
            playerCount = matchInfo.Players.Count,
            maxPlayers = matchInfo.MaxPlayers,
            hostPlayerId = matchInfo.HostPlayerId,
            players = matchInfo.Players.Select(p => new
            {
                playerId = p.PlayerId,
                displayName = p.DisplayName,
                isHost = p.IsHost,
                isReady = p.IsReady
            })
        }
    });
});

// Join an existing lobby
app.MapPost("/api/lobbies/{lobbyId}/join", async (HttpContext ctx, IGrainFactory grains, string lobbyId, JoinLobbyRequest request) =>
{
    var matchGrain = grains.GetGrain<IMatchGrain>(lobbyId);

    // Validate password if provided
    if (!string.IsNullOrEmpty(request.PasswordHash))
    {
        bool valid = await matchGrain.ValidatePassword(request.PasswordHash);
        if (!valid)
        {
            return Results.Ok(new { success = false, error = "Invalid password" });
        }
    }

    // Get client IP from request if not provided
    var publicIp = request.PublicIp;
    if (string.IsNullOrEmpty(publicIp))
    {
        publicIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
    }

    // Add player
    var (playerId, success, error) = await matchGrain.AddPlayer(
        request.DisplayName, publicIp, request.Port, isHost: false);

    if (!success)
    {
        return Results.Ok(new { success = false, error = error ?? "Failed to join lobby" });
    }

    var matchInfo = await matchGrain.GetMatchInfo();
    var host = matchInfo.Players.Find(p => p.IsHost);

    return Results.Ok(new
    {
        success = true,
        playerId = playerId,
        lobbyInfo = new
        {
            lobbyId = lobbyId,
            hostName = host?.DisplayName ?? "Unknown",
            hostIp = host?.PublicIp ?? "",
            hostPort = host?.Port ?? 0,
            playerCount = matchInfo.Players.Count,
            maxPlayers = matchInfo.MaxPlayers,
            hostPlayerId = matchInfo.HostPlayerId,
            players = matchInfo.Players.Select(p => new
            {
                playerId = p.PlayerId,
                displayName = p.DisplayName,
                isHost = p.IsHost,
                isReady = p.IsReady
            })
        }
    });
});

// Heartbeat (updates player presence, returns current state)
app.MapPost("/api/lobbies/{lobbyId}/heartbeat", async (IGrainFactory grains, string lobbyId, HeartbeatRequest request) =>
{
    var matchGrain = grains.GetGrain<IMatchGrain>(lobbyId);
    var (success, kicked, matchStarted, players) = await matchGrain.Heartbeat(request.PlayerId);

    return Results.Ok(new
    {
        success = success,
        kicked = kicked,
        matchStarted = matchStarted,
        players = players?.Select(p => new
        {
            playerId = p.PlayerId,
            displayName = p.DisplayName,
            isHost = p.IsHost,
            isReady = p.IsReady
        }) ?? Enumerable.Empty<object>()
    });
});

// Set ready state
app.MapPost("/api/lobbies/{lobbyId}/ready", async (IGrainFactory grains, string lobbyId, SetReadyRequest request) =>
{
    var matchGrain = grains.GetGrain<IMatchGrain>(lobbyId);
    var (success, error) = await matchGrain.SetReady(request.PlayerId, request.IsReady);

    return Results.Ok(new { success = success, error = error });
});

// Start match (host only)
app.MapPost("/api/lobbies/{lobbyId}/start", async (IGrainFactory grains, string lobbyId, StartMatchRequest request) =>
{
    var matchGrain = grains.GetGrain<IMatchGrain>(lobbyId);
    var (success, error) = await matchGrain.StartMatch(request.PlayerId);

    return Results.Ok(new { success = success, error = error });
});

// Kick player (host only)
app.MapPost("/api/lobbies/{lobbyId}/kick", async (IGrainFactory grains, string lobbyId, KickPlayerRequest request) =>
{
    var matchGrain = grains.GetGrain<IMatchGrain>(lobbyId);
    var (success, error) = await matchGrain.KickPlayer(request.RequestingPlayerId, request.TargetPlayerId);

    return Results.Ok(new { success = success, error = error });
});

// Leave lobby
app.MapPost("/api/lobbies/{lobbyId}/leave", async (IGrainFactory grains, string lobbyId, LeaveLobbyRequest request) =>
{
    var matchGrain = grains.GetGrain<IMatchGrain>(lobbyId);
    await matchGrain.RemovePlayer(request.PlayerId);

    return Results.Ok(new { success = true });
});

app.Run();

// === Request DTOs for HTTP API ===
record CreateLobbyRequest(string DisplayName, string PublicIp, int Port, bool IsPrivate, string? PasswordHash);
record JoinLobbyRequest(string DisplayName, string PublicIp, int Port, string? PasswordHash);
record HeartbeatRequest(Guid PlayerId);
record SetReadyRequest(Guid PlayerId, bool IsReady);
record StartMatchRequest(Guid PlayerId);
record KickPlayerRequest(Guid RequestingPlayerId, Guid TargetPlayerId);
record LeaveLobbyRequest(Guid PlayerId);
