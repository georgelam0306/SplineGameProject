using Derp.Doc.Preferences;

namespace Derp.Doc.Mcp;

public sealed class DerpDocMcpServerOptions
{
    public string WorkspaceRoot { get; set; } = "";
    public bool FollowUiActiveProject { get; set; } = true;
    public bool AutoLiveExportOnMutation { get; set; } = true;

    internal IDerpDocNanobananaClient? NanobananaClient { get; set; }

    internal IDerpDocElevenLabsClient? ElevenLabsClient { get; set; }

    internal Func<DocUserPreferences>? UserPreferencesReader { get; set; }
}
