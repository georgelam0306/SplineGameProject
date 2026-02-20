using Derp.Doc.Model;
using FontAwesome.Sharp;

namespace Derp.Doc.Plugins;

public sealed class SplineGamePlugin : IDerpDocPlugin, IDerpDocEditorPlugin
{
    public string Id => "splinegame.plugin";

    public void Register(IDerpDocPluginRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        registrar.RegisterColumnType(new ColumnTypeDefinition
        {
            ColumnTypeId = SplineGameLevelIds.ColumnTypeId,
            DisplayName = "SplineGameLevel",
            IconGlyph = ((char)IconChar.BezierCurve).ToString(),
            FallbackKind = DocColumnKind.Subtable,
            IsTextWrappedByDefault = false,
            MinimumRowHeight = 24f,
        });
    }

    public void RegisterEditor(IDerpDocEditorPluginRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        registrar.RegisterColumnUiPlugin(new SplineGameLevelColumnUiPlugin());
        registrar.RegisterTableViewRenderer(new SplineGameLevelTableViewRenderer());
    }
}
