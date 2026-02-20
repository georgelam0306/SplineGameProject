using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;

namespace Derp.UI;

internal static class InspectorButtonRow
{
    public static bool Draw(string id, string label)
    {
        var rect = ImLayout.AllocateRect(0f, Im.Style.MinButtonHeight);
        rect = InspectorRow.GetPaddedRect(rect);

        var ctx = Im.Context;
        ctx.PushId(id);
        bool pressed = Im.Button(label, rect.X, rect.Y, rect.Width, rect.Height);
        ctx.PopId();
        return pressed;
    }
}

