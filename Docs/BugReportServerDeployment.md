# Bug Report Server Deployment Guide

This guide covers deploying the DerpTech bug report server alongside the matchmaking server.

## Server Requirements

Uses the **same VPS** as matchmaking server:
- **IP:** 45.76.79.231
- **Provider:** Vultr (Los Angeles)
- Additional port: TCP 5052 (HTTP API)

No new server needed - the bug report server runs alongside the matchmaking server.

## Current Production Configuration

| Service | Port | Path |
|---------|------|------|
| Matchmaking HTTP | 5050 | /opt/matchmaking/ |
| Matchmaking Orleans | 30000 | |
| NAT Punch UDP | 5051 | |
| **Bug Report HTTP** | **5052** | **/opt/bugreport/** |
| **Bug Report Orleans** | **30001** | |

## Initial Deployment

### 1. Build Server for Linux

From the project root:
```bash
dotnet publish DerpTech2D.BugReportServer/DerpTech2D.BugReportServer.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -o ./publish-bugreport
```

### 2. Prepare Server

SSH into the server and set up the directory and firewall:
```bash
ssh -i ~/.ssh/derptech_server root@45.76.79.231

# Create directories
mkdir -p /opt/bugreport/bugreports

# Add firewall rule
ufw allow 5052/tcp
ufw reload
ufw status
```

### 3. Copy to Server

```bash
scp -i ~/.ssh/derptech_server -r ./publish-bugreport/* root@45.76.79.231:/opt/bugreport/
```

### 4. Create Systemd Service

Create `/etc/systemd/system/bugreport.service`:
```ini
[Unit]
Description=DerpTech Bug Report Server
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/bugreport
Environment=BUGREPORT_PORT=5052
Environment=SERVER_IP=45.76.79.231
Environment=BUGREPORT_STORAGE_PATH=/opt/bugreport/bugreports
Environment=ORLEANS_SILO_PORT=11112
Environment=ORLEANS_GATEWAY_PORT=30001
ExecStart=/opt/bugreport/DerpTech2D.BugReportServer
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

### 5. Start Service

```bash
chmod +x /opt/bugreport/DerpTech2D.BugReportServer
systemctl daemon-reload
systemctl enable bugreport
systemctl start bugreport
```

### 6. Verify

```bash
# Check service status
systemctl status bugreport

# Test health endpoint
curl http://45.76.79.231:5052/health
# Should return: "healthy"

# Test report listing
curl http://45.76.79.231:5052/api/reports
# Should return: []
```

## Updating the Server

### Quick Update Script

```bash
# Build
dotnet publish DerpTech2D.BugReportServer/DerpTech2D.BugReportServer.csproj \
  -c Release -r linux-x64 --self-contained -o ./publish-bugreport

# Deploy
ssh -i ~/.ssh/derptech_server root@45.76.79.231 'systemctl stop bugreport'
scp -i ~/.ssh/derptech_server -r ./publish-bugreport/* root@45.76.79.231:/opt/bugreport/
ssh -i ~/.ssh/derptech_server root@45.76.79.231 'systemctl start bugreport'
```

## Monitoring

### View Logs

```bash
# Recent logs
journalctl -u bugreport -n 100 --no-pager

# Follow logs in real-time
journalctl -u bugreport -f
```

### Check Report Activity

Look for these log entries:
- `[BugReport] Received report <id> (crash=true/false)` - New report uploaded

### Check Disk Usage

Reports are stored in `/opt/bugreport/bugreports/`. Each report creates a subdirectory with:
- `metadata.json` - Report metadata
- Attached files (replays, logs, etc.)

```bash
# Check storage usage
du -sh /opt/bugreport/bugreports/

# Count reports
ls -1 /opt/bugreport/bugreports/ | wc -l
```

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/health` | Health check (returns "healthy") |
| POST | `/api/reports` | Upload report (multipart form) |
| GET | `/api/reports` | List reports (?count=50&crashesOnly=true) |
| GET | `/api/reports/{id}` | Get report metadata |
| GET | `/api/reports/{id}/files/{name}` | Download attached file |
| PATCH | `/api/reports/{id}/status` | Update report status |

## Troubleshooting

### Server won't start
```bash
# Check for port conflicts
ss -tlnp | grep 5052

# Check detailed error
journalctl -u bugreport -n 50
```

### Check both services
```bash
systemctl status matchmaking
systemctl status bugreport
```

### Storage issues
```bash
# Check disk space
df -h /opt/bugreport/

# Check directory permissions
ls -la /opt/bugreport/
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Vultr VPS (45.76.79.231)                 │
│                                                             │
│  ┌──────────────────────────┐  ┌──────────────────────────┐ │
│  │  Matchmaking Server      │  │  Bug Report Server       │ │
│  │  HTTP: 5050              │  │  HTTP: 5052              │ │
│  │  Orleans Gateway: 30000  │  │  Orleans Gateway: 30001  │ │
│  │  NAT Punch UDP: 5051     │  │  Orleans Silo: 11112     │ │
│  │  /opt/matchmaking/       │  │  /opt/bugreport/         │ │
│  └──────────────────────────┘  └──────────────────────────┘ │
│                                                             │
│  Storage: /opt/bugreport/bugreports/                        │
│  └── {report-id}/                                           │
│      ├── metadata.json                                      │
│      ├── replay.dat                                         │
│      ├── game.log                                           │
│      └── ...                                                │
└─────────────────────────────────────────────────────────────┘

Report Flow:
1. Game client detects crash or user submits bug report
2. Client collects: replay, logs, system info, exception details
3. Client uploads multipart form to POST /api/reports
4. Server saves files to disk, registers with Orleans grains
5. Report appears in GET /api/reports listing
```

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `SERVER_IP` | (none) | VPS public IP (enables production mode) |
| `BUGREPORT_PORT` | 5052 | HTTP API port |
| `BUGREPORT_STORAGE_PATH` | ./bugreports | Report storage directory |
| `ORLEANS_SILO_PORT` | 11112 | Orleans silo port |
| `ORLEANS_GATEWAY_PORT` | 30001 | Orleans gateway port |
