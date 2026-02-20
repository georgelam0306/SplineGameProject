# Matchmaking Server Deployment Guide

This guide covers deploying the DerpTech matchmaking server to a VPS for cross-network multiplayer.

## Server Requirements

- Linux VPS with public IP (Ubuntu 24.04 recommended)
- Minimum: 1 vCPU, 512MB RAM
- Open ports: TCP 5050 (HTTP), TCP 30000 (Orleans gateway), UDP 5051 (NAT punch)
- Cost: ~$5-6/month (Vultr, DigitalOcean, Linode, Hetzner)

## Current Production Server

- **IP:** 45.76.79.231
- **Provider:** Vultr (Los Angeles)
- **HTTP:** http://45.76.79.231:5050
- **NAT Punch UDP:** 45.76.79.231:5051

## Initial Deployment

### 1. Create VPS

Using Vultr CLI:
```bash
brew tap vultr/vultr-cli && brew install vultr-cli
export VULTR_API_KEY="ARIWLFD4FVA6PMPQRBQ4HXP2YNZ3GBQRRNBQ"

vultr-cli instance create \
  --region lax \
  --plan vc2-1c-1gb \
  --os 2284 \
  --label derptech-server \
  --host derptech
```

Note the IP address and root password from the output.

### 2. Build Server for Linux

From the project root:
```bash
dotnet publish DerpTech2D.MatchmakingServer/DerpTech2D.MatchmakingServer.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -o ./publish-server
```

### 3. Copy to Server

```bash
scp -r ./publish-server/* root@<SERVER_IP>:/opt/matchmaking/
```

### 4. Configure Firewall

SSH into the server and run:
```bash
ufw allow 22/tcp
ufw allow 5050/tcp
ufw allow 30000/tcp   # Orleans gateway for game clients
ufw allow 5051/udp
ufw enable
ufw status
```

### 5. Create Systemd Service

Create `/etc/systemd/system/matchmaking.service`:
```ini
[Unit]
Description=DerpTech Matchmaking Server
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/matchmaking
Environment=ASPNETCORE_URLS=http://0.0.0.0:5050
Environment=SERVER_IP=<SERVER_IP>
Environment=ORLEANS_GATEWAY_PORT=30000
ExecStart=/opt/matchmaking/DerpTech2D.MatchmakingServer
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

**Important:** Replace `<SERVER_IP>` with your VPS public IP (e.g., `45.76.79.231`).

### 6. Start Service

```bash
chmod +x /opt/matchmaking/DerpTech2D.MatchmakingServer
systemctl daemon-reload
systemctl enable matchmaking
systemctl start matchmaking
```

### 7. Verify

```bash
# Check service status
systemctl status matchmaking

# Test HTTP endpoint
curl http://<SERVER_IP>:5050/health
# Should return: "healthy"

# Test match listing
curl http://<SERVER_IP>:5050/match/list
# Should return: []
```

## Updating the Server

### Quick Update Script

```bash
# Build
dotnet publish DerpTech2D.MatchmakingServer/DerpTech2D.MatchmakingServer.csproj \
  -c Release -r linux-x64 --self-contained -o ./publish-server

# Deploy using SSH key (~/.ssh/derptech_server)
ssh -i ~/.ssh/derptech_server root@45.76.79.231 'systemctl stop matchmaking'
scp -i ~/.ssh/derptech_server -r ./publish-server/* root@45.76.79.231:/opt/matchmaking/
ssh -i ~/.ssh/derptech_server root@45.76.79.231 'systemctl start matchmaking'
```

### First-Time Setup (After Code Changes)

If deploying for the first time with Orleans support, also update firewall and systemd:

```bash
# SSH into server
ssh -i ~/.ssh/derptech_server root@45.76.79.231

# Add Orleans port to firewall
ufw allow 30000/tcp
ufw reload

# Update systemd service with SERVER_IP
cat > /etc/systemd/system/matchmaking.service << 'EOF'
[Unit]
Description=DerpTech Matchmaking Server
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/matchmaking
Environment=ASPNETCORE_URLS=http://0.0.0.0:5050
Environment=SERVER_IP=45.76.79.231
Environment=ORLEANS_GATEWAY_PORT=30000
ExecStart=/opt/matchmaking/DerpTech2D.MatchmakingServer
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl restart matchmaking

# Verify
systemctl status matchmaking
curl http://localhost:5050/health
```

## Monitoring

### View Logs

```bash
# Recent logs
journalctl -u matchmaking -n 100 --no-pager

# Follow logs in real-time
journalctl -u matchmaking -f
```

### Check NAT Punch Activity

Look for these log entries:
- `Peer registered: <token> -> <ip:port>` - Host registered
- `Client <token> requesting introduction to host <token>` - Client joining
- `Introducing <client> <-> <host>` - NAT punch introduction sent

## Game Client Configuration

### Option 1: Environment Variables (Recommended)

Set these environment variables before running the game:
```bash
export MATCHMAKING_SERVER=45.76.79.231
export MATCHMAKING_PORT=30000   # Optional, defaults to 30000
export NET_USE_NAT_PUNCH=true   # Enable NAT hole punching
```

Or run directly:
```bash
MATCHMAKING_SERVER=45.76.79.231 ./Catrillion
```

### Option 2: Code Configuration

Update `DerpTech2D/Config/GameConfig.cs`:

```csharp
public static class Matchmaking
{
    public const string ServerUrl = "http://<SERVER_IP>:5050";
    public const string NatPunchHost = "<SERVER_IP>";
    public const int NatPunchPort = 5051;
    public const int DefaultPort = 7777;
    public const int MaxPlayers = 8;
}
```

Enable NAT punch in `DerpTech2D/Core/Application.cs`:
```csharp
Environment.SetEnvironmentVariable("NET_USE_NAT_PUNCH", "true");
```

## DNS Configuration (Optional)

Instead of using IP addresses directly, you can set up DNS:

1. Add A records in your DNS provider:
   - `matchmaking.yourdomain.com` → `<SERVER_IP>`
   - `natpunch.yourdomain.com` → `<SERVER_IP>`

2. Update GameConfig.cs:
   ```csharp
   public const string ServerUrl = "http://matchmaking.yourdomain.com:5050";
   public const string NatPunchHost = "natpunch.yourdomain.com";
   ```

## Troubleshooting

### Server won't start
```bash
# Check for port conflicts
ss -tlnp | grep 5050
ss -ulnp | grep 5051

# Check detailed error
journalctl -u matchmaking -n 50
```

### Clients can't connect
1. Verify firewall rules: `ufw status`
2. Test HTTP: `curl http://<IP>:5050/health`
3. Check if NAT punch UDP is reachable (use netcat from another machine)

### NAT punch not working
1. Check server logs for "Introducing" messages
2. Verify UDP 5051 is open in firewall
3. Check client console for "NAT punch success" or "NAT punch timed out"

### Stale lobbies showing
Matches auto-close after 5 minutes of inactivity. Force refresh by waiting or restarting the server.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Vultr VPS (45.76.79.231)                 │
│  ┌─────────────────────────────────────────────────────┐   │
│  │           DerpTech2D.MatchmakingServer              │   │
│  │  ┌─────────────────┐  ┌──────────────────────────┐  │   │
│  │  │  Orleans Silo   │  │  NAT Punch Service       │  │   │
│  │  │  Gateway: 30000 │  │  UDP Port 5051           │  │   │
│  │  │  HTTP: 5050     │  │                          │  │   │
│  │  │                 │  │  - Peer registration     │  │   │
│  │  │  Game clients   │  │  - NAT introduction      │  │   │
│  │  │  connect here   │  │  - Hole punching coord   │  │   │
│  │  └─────────────────┘  └──────────────────────────┘  │   │
│  │                                                      │   │
│  │  Orleans Grains (in-memory state)                   │   │
│  │  - MatchmakingGrain: tracks active matches          │   │
│  │  - MatchGrain: per-match state, players, timeout    │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘

Game Flow:
1. Client connects to Orleans gateway (TCP 30000)
2. Host creates lobby → Orleans RPC to MatchmakingGrain
3. Host registers with NAT punch → UDP to port 5051
4. Client lists lobbies → Orleans RPC to MatchmakingGrain
5. Client joins lobby → Orleans RPC to MatchGrain
6. Client requests NAT introduction → UDP to port 5051
7. Server introduces both peers → sends each other's endpoints
8. Peers punch through NAT → direct P2P connection
9. Game starts with direct UDP communication between peers
```

