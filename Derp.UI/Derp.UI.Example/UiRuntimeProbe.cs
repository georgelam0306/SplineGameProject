using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Reflection;
using Core;
using Derp.UI;

namespace Derp.UI.Example;

public static class UiRuntimeProbe
{
    public static void Run(string uiAssetUrl)
    {
        if (string.IsNullOrWhiteSpace(uiAssetUrl))
        {
            uiAssetUrl = "ui/TestAsset.bdui";
        }

        string assetPath = Path.Combine(AppContext.BaseDirectory, "data", "db", uiAssetUrl.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(assetPath))
        {
            Console.WriteLine($"UiRuntimeProbe: asset not found at `{assetPath}`");
            return;
        }

        byte[] fileBytes = File.ReadAllBytes(assetPath);
        int bduiOffset = IndexOfBduiMagic(fileBytes);
        if (bduiOffset < 0)
        {
            Console.WriteLine("UiRuntimeProbe: BDUI magic not found in file.");
            return;
        }

        byte[] payload = new byte[fileBytes.Length - bduiOffset];
        Array.Copy(fileBytes, bduiOffset, payload, 0, payload.Length);

        ushort bduiVersion = ReadBduiVersion(payload);
        if (bduiVersion == 0)
        {
            Console.WriteLine("UiRuntimeProbe: invalid BDUI header.");
            return;
        }

        uint firstShapeStableId = FindFirstShapeStableId(payload);
        if (firstShapeStableId == 0)
        {
            Console.WriteLine("UiRuntimeProbe: no shape nodes found in BDUI.");
            return;
        }

        CompiledUi compiled = InvokeCompiledUiFromPayload(payload);

        var runtime = new UiRuntime();
        runtime.Load(compiled);

        DumpShapeState("After Load", runtime, firstShapeStableId);

        runtime.Tick(deltaMicroseconds: 16_666);
        runtime.ResolveLayout();

        DumpShapeState("After Tick", runtime, firstShapeStableId);
    }

    public static void RunHover(string uiAssetUrl)
    {
        if (string.IsNullOrWhiteSpace(uiAssetUrl))
        {
            uiAssetUrl = "ui/TestAsset.bdui";
        }

        string assetPath = Path.Combine(AppContext.BaseDirectory, "data", "db", uiAssetUrl.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(assetPath))
        {
            Console.WriteLine($"UiRuntimeProbe: asset not found at `{assetPath}`");
            return;
        }

        byte[] fileBytes = File.ReadAllBytes(assetPath);
        int bduiOffset = IndexOfBduiMagic(fileBytes);
        if (bduiOffset < 0)
        {
            Console.WriteLine("UiRuntimeProbe: BDUI magic not found in file.");
            return;
        }

        byte[] payload = new byte[fileBytes.Length - bduiOffset];
        Array.Copy(fileBytes, bduiOffset, payload, 0, payload.Length);

        CompiledUi compiled = InvokeCompiledUiFromPayload(payload);

        var runtime = new UiRuntime();
        runtime.Load(compiled);
        _ = runtime.TrySetActivePrefabCanvasSize(1280, 720, resolveLayout: true);

        const uint stepUs = 16_666;
        for (int i = 0; i < 2; i++)
        {
            runtime.Tick(stepUs, new UiPointerFrameInput(pointerValid: false, pointerWorld: default, primaryDown: false, wheelDelta: 0f, hoveredStableId: 0));
        }

        if (!runtime.TryGetActivePrefabStateMachineRuntimeDebug(out UiRuntimeStateMachineDebug initialDebug))
        {
            Console.WriteLine("UiRuntimeProbe: no StateMachineRuntimeComponent on active prefab.");
            return;
        }

        int initialState = initialDebug.DebugActiveStateId;
        Console.WriteLine($"UiRuntimeProbe: initialStateId={initialState}");

        const int scanStep = 16;
        const int scanMax = 720;

        bool found = false;
        Vector2 foundPoint = default;
        int hoverStateId = initialState;
        uint hoveredStableId = 0;

        for (int y = 0; y < scanMax && !found; y += scanStep)
        {
            for (int x = 0; x < 1280 && !found; x += scanStep)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);

                var input = new UiPointerFrameInput(
                    pointerValid: true,
                    pointerWorld: p,
                    primaryDown: false,
                    wheelDelta: 0f,
                    hoveredStableId: UiPointerFrameInput.ComputeHoveredStableId);

                runtime.Tick(stepUs, input);

                if (!runtime.TryGetActivePrefabStateMachineRuntimeDebug(out UiRuntimeStateMachineDebug debugAfterHover))
                {
                    continue;
                }

                int stateId = debugAfterHover.DebugActiveStateId;
                if (stateId != initialState)
                {
                    found = true;
                    foundPoint = p;
                    hoverStateId = stateId;
                    hoveredStableId = runtime.Input.Current.HoveredStableId;
                }
            }
        }

        if (!found)
        {
            Console.WriteLine("UiRuntimeProbe: no hover-driven state change detected in scan region.");
            return;
        }

        Console.WriteLine($"UiRuntimeProbe: hoverPoint=({foundPoint.X:0.0},{foundPoint.Y:0.0}), hoverStateId={hoverStateId}, hoveredStableId={hoveredStableId}");

        if (hoveredStableId != 0)
        {
            DumpShapeState("Before Hover (target)", runtime, hoveredStableId);
        }

        // Ensure we're firmly in hover state and sample it.
        runtime.Tick(stepUs, new UiPointerFrameInput(pointerValid: true, pointerWorld: foundPoint, primaryDown: false, wheelDelta: 0f, hoveredStableId: UiPointerFrameInput.ComputeHoveredStableId));
        if (hoveredStableId != 0)
        {
            DumpShapeState("In Hover (target)", runtime, hoveredStableId);
        }

        // Find an in-canvas leave point (pointerValid=true) that returns the state machine to its initial state.
        bool foundLeave = false;
        Vector2 leavePoint = default;
        for (int y = 0; y < scanMax && !foundLeave; y += scanStep)
        {
            for (int x = 0; x < 1280 && !foundLeave; x += scanStep)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                runtime.Tick(stepUs, new UiPointerFrameInput(pointerValid: true, pointerWorld: p, primaryDown: false, wheelDelta: 0f, hoveredStableId: UiPointerFrameInput.ComputeHoveredStableId));
                if (!runtime.TryGetActivePrefabStateMachineRuntimeDebug(out UiRuntimeStateMachineDebug debugAfterMove))
                {
                    continue;
                }

                if (debugAfterMove.DebugActiveStateId == initialState)
                {
                    foundLeave = true;
                    leavePoint = p;
                }
            }
        }

        if (foundLeave)
        {
            Console.WriteLine($"UiRuntimeProbe: leavePoint=({leavePoint.X:0.0},{leavePoint.Y:0.0})");
        }
        else
        {
            Console.WriteLine("UiRuntimeProbe: no in-canvas leave point found; falling back to pointerValid=false.");
        }

        // Simulate leaving for a couple frames.
        for (int i = 0; i < 4; i++)
        {
            if (foundLeave)
            {
                runtime.Tick(stepUs, new UiPointerFrameInput(pointerValid: true, pointerWorld: leavePoint, primaryDown: false, wheelDelta: 0f, hoveredStableId: UiPointerFrameInput.ComputeHoveredStableId));
            }
            else
            {
                runtime.Tick(stepUs, new UiPointerFrameInput(pointerValid: false, pointerWorld: default, primaryDown: false, wheelDelta: 0f, hoveredStableId: 0));
            }
        }

        if (!runtime.TryGetActivePrefabStateMachineRuntimeDebug(out UiRuntimeStateMachineDebug afterLeave))
        {
            Console.WriteLine("UiRuntimeProbe: failed to read state machine after leave.");
            return;
        }

        Console.WriteLine($"UiRuntimeProbe: afterLeaveStateId={afterLeave.DebugActiveStateId}, lastTransitionId={afterLeave.DebugLastTransitionId}");

        if (hoveredStableId != 0)
        {
            DumpShapeState("After Leave (target)", runtime, hoveredStableId);
        }
    }

    private static int IndexOfBduiMagic(byte[] data)
    {
        // "BDUI" in ASCII
        const uint magic = 0x49554442;
        ReadOnlySpan<byte> span = data;

        for (int i = 0; i + 4 <= span.Length; i++)
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(i, 4));
            if (v == magic)
            {
                return i;
            }
        }

        return -1;
    }

    private static ushort ReadBduiVersion(byte[] payload)
    {
        if (payload.Length < 8)
        {
            return 0;
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));
        if (magic != 0x49554442)
        {
            return 0;
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4));
        return (ushort)version;
    }

    private static uint FindFirstShapeStableId(byte[] payload)
    {
        // Mirrors Runtime/CompiledUi.cs ReadV2 parsing for just node headers.
        // Layout: magic u32, version i32, strings, prefabs, nodes...
        ReadOnlySpan<byte> span = payload;
        int offset = 8;

        if (span.Length < offset + 4)
        {
            return 0;
        }

        int stringCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;
        for (int i = 0; i < stringCount; i++)
        {
            if (span.Length < offset + 4)
            {
                return 0;
            }
            int len = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            offset += 4 + Math.Max(0, len);
            if (offset > span.Length)
            {
                return 0;
            }
        }

        if (span.Length < offset + 4)
        {
            return 0;
        }

        int prefabCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4 + Math.Max(0, prefabCount) * 12;
        if (offset > span.Length)
        {
            return 0;
        }

        if (span.Length < offset + 4)
        {
            return 0;
        }

        int nodeCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;
        if (nodeCount <= 0)
        {
            return 0;
        }

        for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            if (span.Length < offset + 4 + 1 + 4 + 2)
            {
                return 0;
            }

            uint stableId = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
            offset += 4;

            byte nodeType = span[offset];
            offset += 1;

            _ = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
            offset += 4;

            ushort componentCount = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
            offset += 2;

            for (int i = 0; i < componentCount; i++)
            {
                if (span.Length < offset + 2 + 4)
                {
                    return 0;
                }

                _ = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
                offset += 2;

                int size = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
                offset += 4 + Math.Max(0, size);
                if (offset > span.Length)
                {
                    return 0;
                }
            }

            // UiNodeType.Shape == 2
            if (nodeType == 2 && stableId != 0)
            {
                return stableId;
            }
        }

        return 0;
    }

    private static CompiledUi InvokeCompiledUiFromPayload(byte[] payload)
    {
        MethodInfo? method = typeof(CompiledUi).GetMethod("FromBduiPayload", BindingFlags.NonPublic | BindingFlags.Static);
        if (method == null)
        {
            throw new InvalidOperationException("CompiledUi.FromBduiPayload not found via reflection.");
        }

        object? result = method.Invoke(null, new object[] { payload });
        if (result is not CompiledUi compiled)
        {
            throw new InvalidOperationException("CompiledUi.FromBduiPayload returned null or wrong type.");
        }

        return compiled;
    }

    private static void DumpShapeState(string label, UiRuntime runtime, uint shapeStableId)
    {
        object workspace = GetPrivateField(runtime, "_workspace")!;
        object propertyWorld = GetPrivateField(workspace, "_propertyWorld")!;

        bool hasBlend = BlendComponent.Api.TryGetById((Pooled.Runtime.IPoolRegistry)propertyWorld, new BlendComponentId(shapeStableId), out var blend);
        bool hasPaint = PaintComponent.Api.TryGetById((Pooled.Runtime.IPoolRegistry)propertyWorld, new PaintComponentId(shapeStableId), out var paint);
        bool hasRect = RectGeometryComponent.Api.TryGetById((Pooled.Runtime.IPoolRegistry)propertyWorld, new RectGeometryComponentId(shapeStableId), out var rect);
        bool hasTransform = TransformComponent.Api.TryGetById((Pooled.Runtime.IPoolRegistry)propertyWorld, new TransformComponentId(shapeStableId), out var transform);

        Color32 fill0 = default;
        if (hasPaint && paint.IsAlive)
        {
            ReadOnlySpan<Color32> fill = paint.FillColorReadOnlySpan();
            if (!fill.IsEmpty)
            {
                fill0 = fill[0];
            }
        }

        Vector2 rectSize = default;
        if (hasRect && rect.IsAlive)
        {
            rectSize = rect.Size;
        }

        Vector2 position = default;
        if (hasTransform && transform.IsAlive)
        {
            position = transform.Position;
        }

        Console.WriteLine(
            $"UiRuntimeProbe: {label}: shapeStableId={shapeStableId}, " +
            $"transform={(hasTransform && transform.IsAlive ? $"pos={position.X:0.###},{position.Y:0.###}" : "missing")}, " +
            $"rect={(hasRect && rect.IsAlive ? $"size={rectSize.X:0.###}x{rectSize.Y:0.###}" : "missing")}, " +
            $"blend={(hasBlend && blend.IsAlive ? $"visible={blend.IsVisible} opacity={blend.Opacity:0.###}" : "missing")}, " +
            $"paint={(hasPaint && paint.IsAlive ? $"layers={paint.LayerCount} fill0=({fill0.R},{fill0.G},{fill0.B},{fill0.A})" : "missing")}");
    }

    private static object? GetPrivateField(object instance, string name)
    {
        FieldInfo? field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            throw new InvalidOperationException($"Field `{name}` not found on {instance.GetType().FullName}.");
        }

        return field.GetValue(instance);
    }
}
