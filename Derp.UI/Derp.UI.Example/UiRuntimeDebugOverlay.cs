using System;
using System.Numerics;
using Core;
using Derp.UI;
using DerpLib.ImGui;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Windows;
using Property;

namespace Derp.UI.Example;

public static class UiRuntimeDebugOverlay
{
    private const string DebugWindowTitle = "Derp.UI.Example - Runtime Debug";
    private static bool _draggingTitleBar;
    private static Vector2 _dragOffsetScreen;

    public static void Draw(in UiCanvasComponent canvas, UiCanvasFrameContext frame)
    {
        if (frame == null)
        {
            return;
        }

        const int x = 12;
        const int y = 12;
        const int width = 520;
        const int height = 520;

        bool open = Im.BeginWindow(DebugWindowTitle, x, y, width, height, ImWindowFlags.NoMove | ImWindowFlags.AlwaysOnTop);
        HandleManualTitleBarDrag(DebugWindowTitle);
        if (open)
        {
            DrawInput(in canvas, frame);
            ImLayout.Space(8f);
            DrawStateMachine(in canvas);
            ImLayout.Space(8f);
            DrawPrefabVariables(in canvas);
        }
        Im.EndWindow();
    }

    private static void HandleManualTitleBarDrag(string title)
    {
        var window = Im.Context.WindowManager.FindWindow(title);
        if (window == null || !window.IsOpen)
        {
            _draggingTitleBar = false;
            return;
        }

        float titleBarHeight = Im.Style.TitleBarHeight;
        float width = Im.WindowRect.Width;

        var titleBarRect = new DerpLib.ImGui.Core.ImRect(0f, 0f, width, titleBarHeight);
        bool inTitleBar = titleBarRect.Contains(Im.MousePos);
        bool onTitleBarButton = IsMouseOnTitleBarButtons(width, titleBarHeight);

        if (!_draggingTitleBar)
        {
            if (Im.MousePressed && inTitleBar && !onTitleBarButton)
            {
                Vector2 mousePosScreen = GetMousePosScreen();
                _draggingTitleBar = true;
                _dragOffsetScreen = mousePosScreen - window.Rect.Position;
                Im.Context.WindowManager.BringToFront(window);
            }
            return;
        }

        if (!Im.MouseDown)
        {
            _draggingTitleBar = false;
            return;
        }

        Vector2 mouseScreen = GetMousePosScreen();
        window.Rect = new DerpLib.ImGui.Core.ImRect(
            mouseScreen.X - _dragOffsetScreen.X,
            mouseScreen.Y - _dragOffsetScreen.Y,
            window.Rect.Width,
            window.Rect.Height);
    }

    private static bool IsMouseOnTitleBarButtons(float windowWidth, float titleBarHeight)
    {
        float btnSize = titleBarHeight - 8f;
        if (btnSize <= 0f)
        {
            return false;
        }

        float btnY = 4f;
        float closeX = windowWidth - btnSize - 4f;
        float collapseX = closeX - btnSize - 4f;

        var closeRect = new DerpLib.ImGui.Core.ImRect(closeX, btnY, btnSize, btnSize);
        var collapseRect = new DerpLib.ImGui.Core.ImRect(collapseX, btnY, btnSize, btnSize);

        Vector2 mouse = Im.MousePos;
        return closeRect.Contains(mouse) || collapseRect.Contains(mouse);
    }

    private static Vector2 GetMousePosScreen()
    {
        var viewport = Im.Context.PrimaryViewport;
        Vector2 viewportScreenPos = viewport == null ? Vector2.Zero : viewport.ScreenPosition;
        return viewportScreenPos + Im.MousePosViewport;
    }

    private static void DrawInput(in UiCanvasComponent canvas, UiCanvasFrameContext frame)
    {
        DrawVec2Row("Mouse Screen", frame.MousePosition);
        DrawBoolRow("Primary Down", frame.PrimaryDown);
        DrawFloatRow("Wheel Delta", frame.WheelDelta);

        DrawFloatRow("Letterbox X", canvas.DebugLetterboxXOffset);
        DrawFloatRow("Letterbox Y", canvas.DebugLetterboxYOffset);
        DrawBoolRow("Pointer Valid", canvas.DebugPointerValid);
        DrawVec2Row("Pointer Canvas", canvas.DebugPointerCanvas);

        UiRuntime? runtime = canvas.Runtime;
        if (runtime == null)
        {
            DrawTextRow("Runtime", "null".AsSpan());
            return;
        }

        ref readonly UiPointerFrameInput input = ref runtime.Input.Current;
        DrawVec2Row("Pointer World", input.PointerWorld);
        DrawBoolRow("Pressed", runtime.Input.PrimaryPressed);
        DrawBoolRow("Released", runtime.Input.PrimaryReleased);
        DrawU32Row("Hovered StableId", input.HoveredStableId);
        DrawU32Row("Active Prefab", runtime.ActivePrefabStableId);

        if (runtime.TryGetHoveredListenerStableId(out uint listenerStableId))
        {
            DrawU32Row("Hovered Listener", listenerStableId);
            if (runtime.TryGetRuntimeVariableStoreStableIdForListener(listenerStableId, out uint storeStableId))
            {
                DrawU32Row("Listener Store", storeStableId);
                if (runtime.TryGetVariableSchemaStableIdForStore(storeStableId, out uint schemaStableId))
                {
                    DrawU32Row("Store Schema Prefab", schemaStableId);
                }
            }
        }

        uint hit = input.HoveredStableId;
        if (hit != 0 && hit != UiPointerFrameInput.ComputeHoveredStableId && runtime.TryGetOwningPrefabInstanceStableId(hit, out uint owningInstanceStableId))
        {
            DrawU32Row("Owning Instance", owningInstanceStableId);
            if (runtime.TryGetPrefabInstanceSourcePrefabStableId(owningInstanceStableId, out uint sourcePrefabStableId))
            {
                DrawU32Row("Instance Source Prefab", sourcePrefabStableId);
            }
            if (runtime.TryGetStateMachineRuntimeDebug(owningInstanceStableId, out UiRuntimeStateMachineDebug nestedSm))
            {
                DrawU32Row("Instance SM State", (uint)Math.Clamp(nestedSm.DebugActiveStateId, 0, ushort.MaxValue));
            }
        }
    }

    private static void DrawStateMachine(in UiCanvasComponent canvas)
    {
        UiRuntime? runtime = canvas.Runtime;
        if (runtime == null)
        {
            return;
        }

        if (!runtime.TryGetActivePrefabStateMachineRuntimeDebug(out UiRuntimeStateMachineDebug debug))
        {
            DrawTextRow("State Machine", "none".AsSpan());
            return;
        }

        DrawBoolRow("SM Initialized", debug.IsInitialized);
        DrawU32Row("SM Active Machine", (uint)Math.Clamp(debug.ActiveMachineId, 0, ushort.MaxValue));
        DrawU32Row("SM Debug Layer", (uint)Math.Clamp(debug.DebugActiveLayerId, 0, ushort.MaxValue));
        DrawU32Row("SM Debug State", (uint)Math.Clamp(debug.DebugActiveStateId, 0, ushort.MaxValue));
        DrawU32Row("SM Last Transition", (uint)Math.Clamp(debug.DebugLastTransitionId, 0, ushort.MaxValue));

        DrawU32Row("L0 State", debug.Layer0CurrentStateId);
        DrawU32Row("L0 Prev", debug.Layer0PreviousStateId);
        DrawU32Row("L0 State Time (us)", debug.Layer0StateTimeUs);
        DrawU32Row("L0 Transition", debug.Layer0TransitionId);
        DrawU32Row("L0 T From", debug.Layer0TransitionFromStateId);
        DrawU32Row("L0 T To", debug.Layer0TransitionToStateId);
        DrawU32Row("L0 T Time (us)", debug.Layer0TransitionTimeUs);
        DrawU32Row("L0 T Dur (us)", debug.Layer0TransitionDurationUs);
    }

    private static void DrawPrefabVariables(in UiCanvasComponent canvas)
    {
        UiRuntime? runtime = canvas.Runtime;
        if (runtime == null)
        {
            return;
        }

        uint prefabStableId = runtime.ActivePrefabStableId;
        if (prefabStableId == 0)
        {
            DrawTextRow("Prefab Vars", "no active prefab".AsSpan());
            return;
        }

        Span<ushort> ids = stackalloc ushort[PrefabVariablesComponent.MaxVariables];
        Span<StringHandle> names = stackalloc StringHandle[PrefabVariablesComponent.MaxVariables];
        Span<PropertyKind> kinds = stackalloc PropertyKind[PrefabVariablesComponent.MaxVariables];
        Span<PropertyValue> defaults = stackalloc PropertyValue[PrefabVariablesComponent.MaxVariables];

        if (!runtime.TryGetActivePrefabVariableSchema(ids, names, kinds, defaults, out int count) || count <= 0)
        {
            DrawTextRow("Prefab Vars", "none".AsSpan());
            return;
        }

        DrawU32Row("Prefab Var Count", (uint)count);

        Span<char> buffer = stackalloc char[160];
        for (int i = 0; i < count; i++)
        {
            ushort id = ids[i];
            if (id == 0)
            {
                continue;
            }

            if (!runtime.TryGetPrefabVariableValueDetailed(prefabStableId, id, out PropertyKind kind, out PropertyValue value, out bool overridden, out StringHandle resolvedName))
            {
                continue;
            }

            int written = 0;
            written += WriteLiteral(buffer.Slice(written), "#");
            written += WriteUShort(buffer.Slice(written), id);

            StringHandle name = resolvedName.IsValid ? resolvedName : names[i];
            if (name.IsValid)
            {
                written += WriteLiteral(buffer.Slice(written), " ");
                string nameString = name.ToString();
                written += WriteString(buffer.Slice(written), nameString);
            }

            written += WriteLiteral(buffer.Slice(written), " = ");
            written += WriteValue(buffer.Slice(written), kind, in value);
            written += WriteLiteral(buffer.Slice(written), overridden ? " (override)" : " (default)");

            DrawTextRow("Var", buffer.Slice(0, written));
        }

        if (runtime.TryGetHoveredListenerStableId(out uint hoveredListenerStableId) &&
            runtime.TryGetRuntimeVariableStoreStableIdForListener(hoveredListenerStableId, out uint storeStableId) &&
            storeStableId != prefabStableId)
        {
            ImLayout.Space(8f);
            DrawU32Row("Store Var Count", (uint)count);

            for (int i = 0; i < count; i++)
            {
                ushort id = ids[i];
                if (id == 0)
                {
                    continue;
                }

                if (!runtime.TryGetStoreVariableValueDetailed(storeStableId, id, out PropertyKind kind, out PropertyValue value, out bool overridden, out StringHandle resolvedName))
                {
                    continue;
                }

                int written = 0;
                written += WriteLiteral(buffer.Slice(written), "#");
                written += WriteUShort(buffer.Slice(written), id);

                StringHandle name = resolvedName.IsValid ? resolvedName : names[i];
                if (name.IsValid)
                {
                    written += WriteLiteral(buffer.Slice(written), " ");
                    string nameString = name.ToString();
                    written += WriteString(buffer.Slice(written), nameString);
                }

                written += WriteLiteral(buffer.Slice(written), " = ");
                written += WriteValue(buffer.Slice(written), kind, in value);
                written += WriteLiteral(buffer.Slice(written), overridden ? " (override)" : " (default)");

                DrawTextRow("Store Var", buffer.Slice(0, written));
            }
        }
    }

    private static void DrawBoolRow(string label, bool value)
    {
        ReadOnlySpan<char> v = value ? "True".AsSpan() : "False".AsSpan();
        DrawTextRow(label, v);
    }

    private static void DrawFloatRow(string label, float value)
    {
        Span<char> buffer = stackalloc char[32];
        if (!value.TryFormat(buffer, out int written, "0.###"))
        {
            written = 0;
        }
        DrawTextRow(label, buffer.Slice(0, written));
    }

    private static void DrawVec2Row(string label, Vector2 value)
    {
        Span<char> buffer = stackalloc char[64];
        int written = 0;
        written += WriteLiteral(buffer.Slice(written), "(");
        written += WriteFloat(buffer.Slice(written), value.X);
        written += WriteLiteral(buffer.Slice(written), ", ");
        written += WriteFloat(buffer.Slice(written), value.Y);
        written += WriteLiteral(buffer.Slice(written), ")");
        DrawTextRow(label, buffer.Slice(0, written));
    }

    private static void DrawU32Row(string label, uint value)
    {
        Span<char> buffer = stackalloc char[32];
        value.TryFormat(buffer, out int written);
        DrawTextRow(label, buffer.Slice(0, written));
    }

    private static void DrawTextRow(string label, ReadOnlySpan<char> value)
    {
        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = PadRect(rect, padX: 6f, padY: 3f);
        float textY = rect.Y + (rect.Height - Im.Style.FontSize) * 0.5f;

        float labelX = rect.X;
        float valueX = rect.X + rect.Width * 0.55f;

        Im.Text(label.AsSpan(), labelX, textY, Im.Style.FontSize, Im.Style.TextPrimary);
        Im.Text(value, valueX, textY, Im.Style.FontSize, Im.Style.TextSecondary);
    }

    private static DerpLib.ImGui.Core.ImRect PadRect(DerpLib.ImGui.Core.ImRect rect, float padX, float padY)
    {
        rect.X += padX;
        rect.Y += padY;
        rect.Width -= padX * 2f;
        rect.Height -= padY * 2f;
        return rect;
    }

    private static int WriteFloat(Span<char> dst, float v)
    {
        if (!v.TryFormat(dst, out int written, "0.###"))
        {
            return 0;
        }
        return written;
    }

    private static int WriteUShort(Span<char> dst, ushort v)
    {
        v.TryFormat(dst, out int written);
        return written;
    }

    private static int WriteString(Span<char> dst, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        int len = Math.Min(dst.Length, value.Length);
        value.AsSpan(0, len).CopyTo(dst);
        return len;
    }

    private static int WriteValue(Span<char> dst, PropertyKind kind, in PropertyValue value)
    {
        switch (kind)
        {
            case PropertyKind.Bool:
            case PropertyKind.Trigger:
                return WriteLiteral(dst, value.Bool ? "True" : "False");
            case PropertyKind.Int:
                return WriteInt(dst, value.Int);
            case PropertyKind.Float:
                return WriteFloat(dst, value.Float);
            case PropertyKind.Vec2:
                return WriteVec2(dst, value.Vec2);
            case PropertyKind.Color32:
                return WriteColor32(dst, value.Color32);
            case PropertyKind.StringHandle:
                return WriteString(dst, value.StringHandle.ToString());
            default:
                return WriteLiteral(dst, "—");
        }
    }

    private static int WriteVec2(Span<char> dst, Vector2 v)
    {
        int written = 0;
        written += WriteLiteral(dst.Slice(written), "(");
        written += WriteFloat(dst.Slice(written), v.X);
        written += WriteLiteral(dst.Slice(written), ", ");
        written += WriteFloat(dst.Slice(written), v.Y);
        written += WriteLiteral(dst.Slice(written), ")");
        return written;
    }

    private static int WriteInt(Span<char> dst, int v)
    {
        v.TryFormat(dst, out int written);
        return written;
    }

    private static int WriteColor32(Span<char> dst, Color32 c)
    {
        int written = 0;
        written += WriteLiteral(dst.Slice(written), "#");
        written += WriteHexByte(dst.Slice(written), c.R);
        written += WriteHexByte(dst.Slice(written), c.G);
        written += WriteHexByte(dst.Slice(written), c.B);
        written += WriteHexByte(dst.Slice(written), c.A);
        return written;
    }

    private static int WriteHexByte(Span<char> dst, byte b)
    {
        if (dst.Length < 2)
        {
            return 0;
        }

        const string Hex = "0123456789ABCDEF";
        dst[0] = Hex[(b >> 4) & 0xF];
        dst[1] = Hex[b & 0xF];
        return 2;
    }

    private static int WriteLiteral(Span<char> dst, ReadOnlySpan<char> literal)
    {
        int len = Math.Min(dst.Length, literal.Length);
        literal.Slice(0, len).CopyTo(dst);
        return len;
    }
}
