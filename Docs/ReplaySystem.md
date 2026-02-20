# Replay and Debug Logging System

Systems for recording, replaying, and debugging game sessions.

---

## Overview

The replay system serves three purposes:

1. **Replay files**: Record and playback complete game sessions
2. **Debug logging**: Log state and input for debugging
3. **Determinism verification**: Compare replayed state to original

---

## Replay File Format

### File Structure

```
replay.derp
├── Header (64 bytes)
├── Initial State Snapshot
├── Frame Records[]
└── Footer (checksum)
```

### Header

```
Offset  Size  Field
0       4     Magic: 0x52455044 ("REPR")
4       4     Version: 1
8       4     Game version hash
12      8     Timestamp (Unix ms)
20      4     Random seed
24      4     Initial frame number
28      4     Total frame count
32      4     Initial state size (bytes)
36      4     Player count
40      24    Reserved
```

### Initial State Snapshot

Complete serialized world state at recording start:

```
[4 bytes]  Snapshot size
[N bytes]  Snapshot data (see RollbackArchitecture.md format)
```

### Frame Record

Per-frame input and optional state hash:

```
[4 bytes]  Frame number
[4 bytes]  Input data size
[N bytes]  Input data
[8 bytes]  State hash (optional, for verification)
```

### Input Data Format

```
[1 byte]   Input type
[varies]   Type-specific data

Input Types:
  0x00: No-op (empty frame)
  0x01: Unit selection
  0x02: Move command
  0x03: Attack move command
  0x04: Build command
  0x05: Pause/Resume
  ...
```

### Footer

```
[8 bytes]  Final state hash
[4 bytes]  CRC32 of entire file
```

---

## Recording

### Replay Recorder

```csharp
public class ReplayRecorder : IDisposable {
    private readonly BinaryWriter _writer;
    private readonly SnapshotSerializer _serializer;
    private int _frameCount;
    
    public ReplayRecorder(Stream output, int randomSeed) {
        _writer = new BinaryWriter(output);
        WriteHeader(randomSeed);
    }
    
    public void WriteInitialState(byte[] snapshot) {
        _writer.Write(snapshot.Length);
        _writer.Write(snapshot);
    }
    
    public void RecordFrame(int frameNumber, ReadOnlySpan<byte> inputs, ulong stateHash) {
        _writer.Write(frameNumber);
        _writer.Write(inputs.Length);
        _writer.Write(inputs);
        _writer.Write(stateHash);
        _frameCount++;
    }
    
    public void Dispose() {
        WriteFooter();
        _writer.Dispose();
    }
}
```

### What to Record

**Always record**:
- Frame number
- All player inputs (commands, selections)

**Optional**:
- State hash (enables verification, increases file size)
- Periodic full snapshots (enables seeking)

### Recording Triggers

- Start recording: Manual trigger or auto-record option
- Stop recording: Game end, manual stop, or error
- Auto-save: Periodic saves during long sessions

---

## Playback

### Replay Player

```csharp
public class ReplayPlayer {
    private readonly BinaryReader _reader;
    private readonly Simulation _simulation;
    private int _currentFrame;
    
    public ReplayPlayer(Stream input, Simulation simulation) {
        _reader = new BinaryReader(input);
        _simulation = simulation;
        
        ReadHeader();
        LoadInitialState();
    }
    
    public bool AdvanceFrame() {
        if (_currentFrame >= _totalFrames) return false;
        
        var (frameNum, inputs, expectedHash) = ReadFrameRecord();
        
        _simulation.ApplyInputs(inputs);
        _simulation.Tick();
        
        // Verify determinism
        ulong actualHash = _simulation.ComputeStateHash();
        if (expectedHash != 0 && actualHash != expectedHash) {
            OnDesyncDetected(frameNum, expectedHash, actualHash);
        }
        
        _currentFrame++;
        return true;
    }
    
    public void SeekToFrame(int targetFrame) {
        // Find nearest snapshot before targetFrame
        // Load snapshot, replay frames to target
    }
}
```

### Playback Controls

- **Play/Pause**: Toggle simulation advancement
- **Speed**: 0.5x, 1x, 2x, 4x playback speed
- **Seek**: Jump to specific frame (requires periodic snapshots)
- **Step**: Advance single frame

---

## Debug Session Logging

### Purpose

Detailed logging for debugging non-determinism and bugs:
- Log inputs and state every frame
- Log intermediate values in systems
- Enable diff between sessions

### Debug Log Format

Text-based for easy inspection:

```
=== Frame 1234 ===
Inputs:
  [MoveCommand] entity=42 dest=(15,20) attack=false
  [Selection] entities=[42,43,44]

State Hash: 0x1234567890ABCDEF

Entity Positions (sample):
  entity=42: (152.5, 203.7)
  entity=43: (155.2, 201.1)

System Timings:
  UnitMovement: 0.42ms
  UnitIdle: 0.31ms
  Combat: 0.15ms
```

### Debug Logger

```csharp
public class DebugSessionLogger : IDisposable {
    private readonly StreamWriter _writer;
    private readonly bool _logPositions;
    private readonly bool _logTimings;
    
    public DebugSessionLogger(string path, DebugLogOptions options) {
        _writer = new StreamWriter(path);
        _logPositions = options.LogPositions;
        _logTimings = options.LogTimings;
    }
    
    public void LogFrameStart(int frame) {
        _writer.WriteLine($"\n=== Frame {frame} ===");
    }
    
    public void LogInputs(IReadOnlyList<IInput> inputs) {
        _writer.WriteLine("Inputs:");
        foreach (var input in inputs) {
            _writer.WriteLine($"  {input}");
        }
    }
    
    public void LogStateHash(ulong hash) {
        _writer.WriteLine($"State Hash: 0x{hash:X16}");
    }
    
    public void LogEntityPosition(int entityId, Fixed64 x, Fixed64 y) {
        if (_logPositions) {
            _writer.WriteLine($"  entity={entityId}: ({x.ToFloat():F1}, {y.ToFloat():F1})");
        }
    }
    
    public void LogSystemTiming(string systemName, double milliseconds) {
        if (_logTimings) {
            _writer.WriteLine($"  {systemName}: {milliseconds:F2}ms");
        }
    }
}
```

### Debug Log Comparison

Compare two debug logs to find divergence:

```bash
diff -u session1.log session2.log | head -50
```

Or programmatically:

```csharp
public static int FindFirstDivergentFrame(string log1Path, string log2Path) {
    // Parse both logs
    // Compare state hashes frame by frame
    // Return first frame where hashes differ
}
```

---

## Determinism Verification Workflow

### Recording Phase

1. Start game with known seed
2. Enable replay recording
3. Enable debug logging (state hashes only)
4. Play session
5. Save replay file

### Verification Phase

1. Load replay file
2. Initialize simulation with same seed
3. Load initial state
4. For each frame:
   - Apply recorded inputs
   - Tick simulation
   - Compare state hash to recorded hash
5. Report any divergences

### Automated Testing

```csharp
[Test]
public void ReplayProducesSameState() {
    // Record a session
    var recorder = new ReplayRecorder(recordStream, seed: 42);
    var sim1 = new Simulation(seed: 42);
    
    for (int i = 0; i < 1000; i++) {
        var inputs = GenerateRandomInputs(i);
        sim1.ApplyInputs(inputs);
        sim1.Tick();
        recorder.RecordFrame(i, inputs, sim1.StateHash);
    }
    recorder.Dispose();
    
    // Replay and verify
    recordStream.Position = 0;
    var player = new ReplayPlayer(recordStream, new Simulation(seed: 42));
    
    while (player.AdvanceFrame()) {
        // Desync handler will throw on mismatch
    }
}
```

### Cross-Platform Verification

1. Record replay on Platform A
2. Copy replay file to Platform B
3. Playback on Platform B
4. Compare final state hashes
5. If different, enable detailed logging and bisect to find divergent frame

---

## File Locations

### Default Paths

```
Windows: %APPDATA%/DerpTech2D/Replays/
macOS:   ~/Library/Application Support/DerpTech2D/Replays/
Linux:   ~/.local/share/DerpTech2D/Replays/
```

### Naming Convention

```
replay_2024-01-15_14-30-22_seed42.derp
debug_2024-01-15_14-30-22.log
```

---

## Integration Points

### Recording Hook

```csharp
// In game loop
void GameLoop() {
    while (running) {
        var inputs = CollectInputs();
        
        if (_replayRecorder != null) {
            _replayRecorder.RecordFrame(_frame, inputs, _simulation.StateHash);
        }
        
        _simulation.ApplyInputs(inputs);
        _simulation.Tick();
        _frame++;
    }
}
```

### Playback Hook

```csharp
// In replay mode
void ReplayLoop() {
    while (_replayPlayer.AdvanceFrame()) {
        Render(_replayPlayer.Simulation);
        
        if (paused) {
            WaitForUserInput();
        }
        
        WaitForFrameTime(playbackSpeed);
    }
}
```

---

## Implementation Checklist

- [ ] Define replay file format (header, frames, footer)
- [ ] Implement ReplayRecorder with input serialization
- [ ] Implement ReplayPlayer with input deserialization
- [ ] Add state hash recording/verification
- [ ] Implement DebugSessionLogger for verbose logging
- [ ] Add playback controls (pause, speed, seek)
- [ ] Create log comparison tool for debugging
- [ ] Add auto-save for long sessions
- [ ] Implement periodic snapshot embedding for seeking
- [ ] Create cross-platform verification test suite

