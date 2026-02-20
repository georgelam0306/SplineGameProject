namespace Derp.Doc.Assets;

internal static class DocAssetServices
{
    public static AssetScanner AssetScanner { get; } = new();
    public static AssetThumbnailCache ThumbnailCache { get; } = new();
    public static LazyAssetCompiler LazyAssetCompiler { get; } = new();
    public static AudioPreviewPlayer AudioPreviewPlayer { get; } = new();
    public static DerpUiPreviewCache DerpUiPreviewCache { get; } = new();
}
