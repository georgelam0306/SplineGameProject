using System.Net;
using System.Text.Json;
using BugReportContracts.Grains;
using BugReportContracts.Models;
using Orleans.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var serverIp = Environment.GetEnvironmentVariable("SERVER_IP");
var isProduction = !string.IsNullOrEmpty(serverIp);
var storagePath = Environment.GetEnvironmentVariable("BUGREPORT_STORAGE_PATH") ?? "./bugreports";
var port = int.Parse(Environment.GetEnvironmentVariable("BUGREPORT_PORT") ?? "5052");

builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Host.UseOrleans(siloBuilder =>
{
    if (isProduction && serverIp != null)
    {
        var siloPort = int.Parse(Environment.GetEnvironmentVariable("ORLEANS_SILO_PORT") ?? "11112");
        var gatewayPort = int.Parse(Environment.GetEnvironmentVariable("ORLEANS_GATEWAY_PORT") ?? "30001");

        var siloEndpoint = new IPEndPoint(IPAddress.Parse(serverIp), siloPort);
        siloBuilder.UseDevelopmentClustering(siloEndpoint);
        siloBuilder.ConfigureEndpoints(
            advertisedIP: IPAddress.Parse(serverIp),
            siloPort: siloPort,
            gatewayPort: gatewayPort,
            listenOnAnyHostAddress: true);
        siloBuilder.Configure<ClusterOptions>(options =>
        {
            options.ClusterId = "derptech-bugreports";
            options.ServiceId = "DerpTechBugReports";
        });
    }
    else
    {
        siloBuilder.UseLocalhostClustering(siloPort: 11112, gatewayPort: 30001);
    }
    siloBuilder.AddMemoryGrainStorage("Default");
});

var app = builder.Build();

// Ensure storage directory exists
Directory.CreateDirectory(storagePath);

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

// Health check
app.MapGet("/health", () => Results.Ok("healthy"));

// Debug endpoint to check storage
app.MapGet("/debug/storage", () =>
{
    var reports = new List<object>();
    if (Directory.Exists(storagePath))
    {
        foreach (var d in Directory.GetDirectories(storagePath))
        {
            reports.Add(new
            {
                name = Path.GetFileName(d),
                hasMetadata = File.Exists(Path.Combine(d, "metadata.json")),
                files = Directory.GetFiles(d).Select(Path.GetFileName).ToList()
            });
        }
    }
    return Results.Json(new { storagePath, exists = Directory.Exists(storagePath), reports });
});

// Dashboard
app.MapGet("/", () => Results.Content("""
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>Bug Reports</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #1a1a2e; color: #eee; padding: 20px; }
        h1 { margin-bottom: 20px; color: #fff; }
        .filters { margin-bottom: 20px; }
        .filters label { margin-right: 15px; cursor: pointer; }
        .stats { display: flex; gap: 20px; margin-bottom: 20px; }
        .stat { background: #16213e; padding: 15px 25px; border-radius: 8px; }
        .stat-value { font-size: 2em; font-weight: bold; }
        .stat-label { color: #888; font-size: 0.9em; }
        .crash .stat-value { color: #e74c3c; }
        table { width: 100%; border-collapse: collapse; background: #16213e; border-radius: 8px; overflow: hidden; }
        th, td { padding: 12px 15px; text-align: left; border-bottom: 1px solid #2a2a4a; }
        th { background: #0f3460; color: #fff; font-weight: 600; }
        tr:hover { background: #1f4068; cursor: pointer; }
        .crash-row { background: #2d1f1f; }
        .crash-row:hover { background: #3d2525; }
        .badge { padding: 3px 8px; border-radius: 4px; font-size: 0.8em; }
        .badge-crash { background: #e74c3c; color: white; }
        .badge-bug { background: #3498db; color: white; }
        .badge-new { background: #9b59b6; }
        .badge-progress { background: #f39c12; }
        .badge-resolved { background: #27ae60; }
        .modal { display: none; position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.8); z-index: 100; }
        .modal-content { background: #16213e; margin: 5% auto; padding: 25px; width: 80%; max-width: 800px; border-radius: 10px; max-height: 80vh; overflow-y: auto; }
        .modal-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; }
        .close { font-size: 28px; cursor: pointer; color: #888; }
        .close:hover { color: #fff; }
        .detail-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 15px; }
        .detail-item { background: #0f3460; padding: 12px; border-radius: 6px; }
        .detail-label { color: #888; font-size: 0.85em; margin-bottom: 4px; }
        .detail-value { word-break: break-all; }
        .exception { grid-column: 1 / -1; background: #2d1f1f; }
        .files { margin-top: 20px; }
        .file-link { display: inline-block; background: #0f3460; padding: 8px 15px; margin: 5px; border-radius: 5px; color: #3498db; text-decoration: none; }
        .file-link:hover { background: #1f4068; }
        .status-select { background: #0f3460; color: #fff; border: none; padding: 8px 12px; border-radius: 5px; cursor: pointer; }
        .repro-section { margin-top: 20px; padding: 15px; background: #0d2847; border-radius: 8px; border: 2px solid #3498db; }
        .repro-key { font-family: monospace; font-size: 1.2em; background: #16213e; padding: 10px 15px; border-radius: 5px; display: flex; align-items: center; gap: 10px; margin-bottom: 15px; }
        .repro-key code { flex: 1; color: #3498db; }
        .copy-btn { background: #3498db; color: white; border: none; padding: 8px 15px; border-radius: 5px; cursor: pointer; }
        .copy-btn:hover { background: #2980b9; }
        .action-buttons { display: flex; gap: 10px; flex-wrap: wrap; }
        .action-btn { background: #27ae60; color: white; border: none; padding: 10px 20px; border-radius: 5px; cursor: pointer; text-decoration: none; display: inline-flex; align-items: center; gap: 8px; }
        .action-btn:hover { background: #219a52; }
        .action-btn.secondary { background: #9b59b6; }
        .action-btn.secondary:hover { background: #8e44ad; }
    </style>
</head>
<body>
    <h1>🐛 Bug Reports Dashboard</h1>
    <div class="stats">
        <div class="stat"><div class="stat-value" id="total">-</div><div class="stat-label">Total Reports</div></div>
        <div class="stat crash"><div class="stat-value" id="crashes">-</div><div class="stat-label">Crashes</div></div>
        <div class="stat"><div class="stat-value" id="new">-</div><div class="stat-label">New</div></div>
    </div>
    <div class="filters">
        <label><input type="checkbox" id="crashOnly"> Show crashes only</label>
    </div>
    <table>
        <thead><tr><th>Type</th><th>Date</th><th>Platform</th><th>Version</th><th>Description</th><th>Status</th></tr></thead>
        <tbody id="reports"></tbody>
    </table>
    <div id="modal" class="modal">
        <div class="modal-content">
            <div class="modal-header"><h2 id="modal-title">Report Details</h2><span class="close" onclick="closeModal()">&times;</span></div>
            <div id="modal-body"></div>
        </div>
    </div>
    <script>
        const statusNames = ['New', 'In Progress', 'Resolved', 'Won\'t Fix', 'Duplicate'];
        const statusClasses = ['new', 'progress', 'resolved', 'resolved', 'resolved'];
        let allReports = [];

        async function loadReports() {
            const res = await fetch('/api/reports?count=100');
            allReports = await res.json();
            updateStats();
            renderReports();
        }

        function updateStats() {
            document.getElementById('total').textContent = allReports.length;
            document.getElementById('crashes').textContent = allReports.filter(r => r.isCrashReport).length;
            document.getElementById('new').textContent = allReports.filter(r => r.status === 0).length;
        }

        function renderReports() {
            const crashOnly = document.getElementById('crashOnly').checked;
            const filtered = crashOnly ? allReports.filter(r => r.isCrashReport) : allReports;
            const tbody = document.getElementById('reports');
            tbody.innerHTML = filtered.map(r => `
                <tr class="${r.isCrashReport ? 'crash-row' : ''}" onclick="showDetail('${r.reportId}')">
                    <td><span class="badge ${r.isCrashReport ? 'badge-crash' : 'badge-bug'}">${r.isCrashReport ? 'CRASH' : 'Bug'}</span></td>
                    <td>${new Date(r.submittedAt).toLocaleString()}</td>
                    <td>${r.platform || '-'}</td>
                    <td>${r.gameVersion || '-'}</td>
                    <td>${r.exceptionType || r.description || '-'}</td>
                    <td><span class="badge badge-${statusClasses[r.status]}">${statusNames[r.status]}</span></td>
                </tr>
            `).join('');
        }

        async function showDetail(id) {
            const res = await fetch(`/api/reports/${id}`);
            const r = await res.json();
            const shortCommit = r.gitCommitHash ? r.gitCommitHash.substring(0, 7) : null;
            const reproKey = shortCommit ? `${r.reportId}:${shortCommit}` : null;
            const replayFile = r.attachedFiles?.find(f => f.endsWith('.bin') && f.includes('replay'));

            document.getElementById('modal-title').textContent = r.isCrashReport ? '💥 Crash Report' : '🐛 Bug Report';
            document.getElementById('modal-body').innerHTML = `
                <div class="detail-grid">
                    <div class="detail-item"><div class="detail-label">Report ID</div><div class="detail-value">${r.reportId}</div></div>
                    <div class="detail-item"><div class="detail-label">Submitted</div><div class="detail-value">${new Date(r.submittedAt).toLocaleString()}</div></div>
                    <div class="detail-item"><div class="detail-label">Platform</div><div class="detail-value">${r.platform}</div></div>
                    <div class="detail-item"><div class="detail-label">Game Version</div><div class="detail-value">${r.gameVersion}</div></div>
                    <div class="detail-item"><div class="detail-label">Git Commit</div><div class="detail-value">${r.gitCommitHash || 'unknown'}</div></div>
                    <div class="detail-item"><div class="detail-label">OS Version</div><div class="detail-value">${r.osVersion}</div></div>
                    <div class="detail-item"><div class="detail-label">Memory Usage</div><div class="detail-value">${r.memoryUsageMb} MB</div></div>
                    ${r.description ? `<div class="detail-item" style="grid-column:1/-1"><div class="detail-label">Description</div><div class="detail-value">${r.description}</div></div>` : ''}
                    ${r.exceptionType ? `<div class="detail-item exception"><div class="detail-label">Exception</div><div class="detail-value"><strong>${r.exceptionType}</strong><br>${r.exceptionMessage}</div></div>` : ''}
                </div>
                ${reproKey ? `
                <div class="repro-section">
                    <div class="detail-label" style="margin-bottom:10px">🔄 Reproduction</div>
                    <div class="repro-key">
                        <code id="repro-key">${reproKey}</code>
                        <button class="copy-btn" onclick="copyReproKey()">📋 Copy</button>
                    </div>
                    <div class="action-buttons">
                        ${replayFile ? `<a class="action-btn" href="/api/reports/${r.reportId}/replay" download>⬇️ Download Replay</a>` : ''}
                        <button class="action-btn secondary" onclick="showReproInstructions('${reproKey}')">📝 Show Instructions</button>
                    </div>
                </div>
                ` : ''}
                <div class="detail-item" style="margin-top:15px">
                    <div class="detail-label">Status</div>
                    <select class="status-select" onchange="updateStatus('${r.reportId}', this.value)">
                        ${statusNames.map((s, i) => `<option value="${i}" ${r.status === i ? 'selected' : ''}>${s}</option>`).join('')}
                    </select>
                </div>
                ${r.attachedFiles?.length ? `<div class="files"><div class="detail-label">Attached Files</div>${r.attachedFiles.map(f => `<a class="file-link" href="/api/reports/${r.reportId}/files/${f}" download>📎 ${f}</a>`).join('')}</div>` : ''}
            `;
            document.getElementById('modal').style.display = 'block';
        }

        function copyReproKey() {
            const key = document.getElementById('repro-key').textContent;
            navigator.clipboard.writeText(key);
            const btn = event.target;
            btn.textContent = '✓ Copied!';
            setTimeout(() => btn.textContent = '📋 Copy', 2000);
        }

        function showReproInstructions(reproKey) {
            alert('To reproduce this crash locally:\n\n1. Download the script:\n   curl -O http://45.76.79.231:5052/scripts/reproduce-crash.sh\n\n2. Run:\n   bash reproduce-crash.sh ' + reproKey + '\n\nThis will download the replay, checkout the exact commit, build, and run the game.');
        }

        async function updateStatus(id, status) {
            await fetch(`/api/reports/${id}/status?status=${status}`, { method: 'PATCH' });
            loadReports();
        }

        function closeModal() { document.getElementById('modal').style.display = 'none'; }
        window.onclick = e => { if (e.target.id === 'modal') closeModal(); }
        document.getElementById('crashOnly').onchange = renderReports;
        loadReports();
    </script>
</body>
</html>
""", "text/html"));

// Upload bug report (multipart form)
app.MapPost("/api/reports", async (HttpRequest request, IGrainFactory grainFactory) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart form data");

    var form = await request.ReadFormAsync();
    var reportId = Guid.NewGuid().ToString("N");
    var reportDir = Path.Combine(storagePath, reportId);
    Directory.CreateDirectory(reportDir);

    // Save uploaded files
    var attachedFiles = new List<string>();
    foreach (var file in form.Files)
    {
        var safeFileName = Path.GetFileName(file.FileName);
        var filePath = Path.Combine(reportDir, safeFileName);
        await using var stream = File.Create(filePath);
        await file.CopyToAsync(stream);
        attachedFiles.Add(safeFileName);
    }

    // Parse metadata from form fields
    var metadata = new BugReportMetadata
    {
        ReportId = reportId,
        SubmittedAt = DateTime.UtcNow,
        GameVersion = form["gameVersion"].ToString(),
        Platform = form["platform"].ToString(),
        Description = form["description"].ToString(),
        IsCrashReport = bool.TryParse(form["isCrashReport"], out var isCrash) && isCrash,
        ExceptionType = form["exceptionType"].ToString(),
        ExceptionMessage = form["exceptionMessage"].ToString(),
        MemoryUsageMb = long.TryParse(form["memoryUsageMb"], out var mem) ? mem : 0,
        OsVersion = form["osVersion"].ToString(),
        AttachedFiles = attachedFiles,
        Status = BugReportStatus.New,
        GitCommitHash = form["gitCommitHash"].ToString()
    };

    // Save metadata as JSON
    var metadataPath = Path.Combine(reportDir, "metadata.json");
    await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, jsonOptions));

    // Register with grains
    var reportGrain = grainFactory.GetGrain<IBugReportGrain>(reportId);
    await reportGrain.SetMetadata(metadata);

    var indexGrain = grainFactory.GetGrain<IBugReportIndexGrain>(0);
    await indexGrain.RegisterReport(reportId, metadata);

    Console.WriteLine($"[BugReport] Received report {reportId} (crash={isCrash})");

    return Results.Ok(new { reportId });
});

// List reports
app.MapGet("/api/reports", async (IGrainFactory grainFactory, int? count, bool? crashesOnly) =>
{
    var indexGrain = grainFactory.GetGrain<IBugReportIndexGrain>(0);
    var reports = crashesOnly == true
        ? await indexGrain.GetCrashReports(count ?? 50)
        : await indexGrain.GetRecentReports(count ?? 50);
    return Results.Ok(reports);
});

// Get report details
app.MapGet("/api/reports/{reportId}", async (string reportId, IGrainFactory grainFactory) =>
{
    var reportGrain = grainFactory.GetGrain<IBugReportGrain>(reportId);
    var metadata = await reportGrain.GetMetadata();
    if (metadata == null)
        return Results.NotFound();
    return Results.Ok(metadata);
});

// Download report file
app.MapGet("/api/reports/{reportId}/files/{fileName}", (string reportId, string fileName) =>
{
    // Sanitize to prevent path traversal
    var safeReportId = Path.GetFileName(reportId);
    var safeFileName = Path.GetFileName(fileName);
    var filePath = Path.Combine(storagePath, safeReportId, safeFileName);

    if (!File.Exists(filePath))
        return Results.NotFound();

    return Results.File(filePath, "application/octet-stream", safeFileName);
});

// Download replay file directly (finds the replay .bin in attached files)
app.MapGet("/api/reports/{reportId}/replay", async (string reportId, IGrainFactory grainFactory) =>
{
    var reportGrain = grainFactory.GetGrain<IBugReportGrain>(reportId);
    var metadata = await reportGrain.GetMetadata();
    if (metadata == null)
        return Results.NotFound();

    // Find replay file in attachments
    var replayFile = metadata.AttachedFiles?.FirstOrDefault(f =>
        f.EndsWith(".bin") && f.Contains("replay", StringComparison.OrdinalIgnoreCase));

    if (string.IsNullOrEmpty(replayFile))
        return Results.NotFound("No replay file found");

    var safeReportId = Path.GetFileName(reportId);
    var filePath = Path.Combine(storagePath, safeReportId, replayFile);

    if (!File.Exists(filePath))
        return Results.NotFound();

    return Results.File(filePath, "application/octet-stream", replayFile);
});

// Serve the reproduce-crash.sh script
app.MapGet("/scripts/reproduce-crash.sh", () =>
{
    var script = """
#!/bin/bash
set -e

# Crash Reproduction Script for Catrillion
# Usage: bash reproduce-crash.sh <repro-key>
# Example: bash reproduce-crash.sh 994ab6ac40f8:f43024d

REPRO_KEY="$1"
SERVER="http://45.76.79.231:5052"

if [ -z "$REPRO_KEY" ]; then
    echo "Usage: bash reproduce-crash.sh <repro-key>"
    echo "Get the repro key from the bug report dashboard"
    exit 1
fi

# Parse repro key
REPORT_ID=$(echo "$REPRO_KEY" | cut -d':' -f1)
COMMIT_HASH=$(echo "$REPRO_KEY" | cut -d':' -f2)

if [ -z "$REPORT_ID" ] || [ -z "$COMMIT_HASH" ]; then
    echo "Invalid repro key format. Expected: reportId:commitHash"
    exit 1
fi

echo "=== Crash Reproduction ==="
echo "Report ID: $REPORT_ID"
echo "Commit: $COMMIT_HASH"
echo ""

# Create repro directory
REPRO_DIR=".repro/$COMMIT_HASH"
mkdir -p "$REPRO_DIR"

# Download replay
echo "[1/4] Downloading replay..."
REPLAY_PATH="$REPRO_DIR/replay.bin"
curl -s -f -o "$REPLAY_PATH" "$SERVER/api/reports/$REPORT_ID/replay" || {
    echo "Failed to download replay. Make sure the report has a replay file attached."
    exit 1
}
echo "  Downloaded: $REPLAY_PATH"

# Check if we need to create worktree
WORKTREE_PATH=".repro/worktree-$COMMIT_HASH"
if [ ! -d "$WORKTREE_PATH" ]; then
    echo "[2/4] Creating git worktree at $COMMIT_HASH..."
    git fetch origin --quiet 2>/dev/null || true
    git worktree add "$WORKTREE_PATH" "$COMMIT_HASH" --quiet 2>/dev/null || {
        echo "  Commit not found locally, trying to fetch..."
        git fetch --all --quiet
        git worktree add "$WORKTREE_PATH" "$COMMIT_HASH" --quiet || {
            echo "Failed to checkout commit $COMMIT_HASH"
            exit 1
        }
    }
    echo "  Worktree created: $WORKTREE_PATH"
else
    echo "[2/4] Using existing worktree: $WORKTREE_PATH"
fi

# Build
echo "[3/4] Building..."
cd "$WORKTREE_PATH"
dotnet build Games/Catrillion/Catrillion/Catrillion.csproj -c Release --nologo -v q || {
    echo "Build failed!"
    exit 1
}
echo "  Build complete"

# Run with replay
echo "[4/4] Running game with replay..."
cd "$(git rev-parse --show-toplevel)"
REPLAY_FILE="$(pwd)/$REPLAY_PATH" dotnet run --project "$WORKTREE_PATH/Games/Catrillion/Catrillion/Catrillion.csproj" -c Release --no-build

echo ""
echo "=== Reproduction complete ==="
""";
    return Results.Text(script, "text/x-shellscript");
});

// Update report status
app.MapPatch("/api/reports/{reportId}/status", async (string reportId, int status, IGrainFactory grainFactory) =>
{
    var statusEnum = (BugReportStatus)status;
    var reportGrain = grainFactory.GetGrain<IBugReportGrain>(reportId);
    await reportGrain.SetStatus(statusEnum);

    // Also update the index
    var indexGrain = grainFactory.GetGrain<IBugReportIndexGrain>(0);
    await indexGrain.UpdateStatus(reportId, statusEnum);

    return Results.Ok();
});

Console.WriteLine($"Bug Report Server starting on port {port}...");
Console.WriteLine($"Storage path: {storagePath}");

// Re-index existing reports on startup
app.Lifetime.ApplicationStarted.Register(() =>
{
    Task.Run(async () =>
    {
        try
        {
            var grainFactory = app.Services.GetRequiredService<IGrainFactory>();
            var indexGrain = grainFactory.GetGrain<IBugReportIndexGrain>(0);
            var reindexed = 0;

            foreach (var dir in Directory.GetDirectories(storagePath))
            {
                var metadataPath = Path.Combine(dir, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    var json = await File.ReadAllTextAsync(metadataPath);
                    var metadata = JsonSerializer.Deserialize<BugReportMetadata>(json);
                    if (metadata != null)
                    {
                        var reportGrain = grainFactory.GetGrain<IBugReportGrain>(metadata.ReportId);
                        await reportGrain.SetMetadata(metadata);
                        await indexGrain.RegisterReport(metadata.ReportId, metadata);
                        reindexed++;
                    }
                }
            }

            Console.WriteLine($"[BugReport] Re-indexed {reindexed} existing reports");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BugReport] Failed to re-index reports: {ex.Message}");
        }
    });
});

app.Run();
