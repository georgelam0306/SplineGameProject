namespace Derp.Doc.Plugins;

public interface IDerpDocAutomationProvider
{
    string ActionId { get; }

    string DisplayName { get; }

    string Description { get; }

    bool Execute(IDerpDocEditorContext workspace, out string statusMessage);
}
