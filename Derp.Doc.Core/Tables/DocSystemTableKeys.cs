namespace Derp.Doc.Tables;

public static class DocSystemTableKeys
{
    public const string Assets = "system.assets";
    public const string Packages = "system.packages";
    public const string Exports = "system.exports";
    public const string Textures = "system.textures";
    public const string Models = "system.models";
    public const string Audio = "system.audio";
    public const string Ui = "system.ui";
    public const string Materials = "system.materials";
    public const string AssetDependencies = "system.asset_deps";
    public const string SplineGameEntityBase = "system.splinegame.entity_base";
    public const string SplineGameEntityTools = "system.splinegame.entity_tools";

    public static bool IsKnown(string? key)
    {
        return string.Equals(key, Assets, StringComparison.Ordinal) ||
               string.Equals(key, Packages, StringComparison.Ordinal) ||
               string.Equals(key, Exports, StringComparison.Ordinal) ||
               string.Equals(key, Textures, StringComparison.Ordinal) ||
               string.Equals(key, Models, StringComparison.Ordinal) ||
               string.Equals(key, Audio, StringComparison.Ordinal) ||
               string.Equals(key, Ui, StringComparison.Ordinal) ||
               string.Equals(key, Materials, StringComparison.Ordinal) ||
               string.Equals(key, AssetDependencies, StringComparison.Ordinal) ||
               string.Equals(key, SplineGameEntityBase, StringComparison.Ordinal) ||
               string.Equals(key, SplineGameEntityTools, StringComparison.Ordinal);
    }
}
