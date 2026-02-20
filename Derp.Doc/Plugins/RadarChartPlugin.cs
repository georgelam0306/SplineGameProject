namespace Derp.Doc.Plugins;

public sealed class RadarChartPlugin : IDerpDocEditorPlugin
{
    public string Id => "sample.radar-chart";

    public void RegisterEditor(IDerpDocEditorPluginRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        registrar.RegisterTableViewRenderer(new RadarChartTableViewRenderer());
    }
}
