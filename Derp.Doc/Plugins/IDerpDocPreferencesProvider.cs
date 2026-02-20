using DerpLib.ImGui.Core;

namespace Derp.Doc.Plugins;

public interface IDerpDocPreferencesProvider
{
    string Id { get; }

    string DisplayName { get; }

    float DrawPreferences(
        IDerpDocEditorContext workspace,
        ImRect contentRect,
        float y,
        ImStyle style);
}
