namespace Derp.UI;

internal static class AnimationsLibraryDragDropPreviewState
{
    private static int _suppressGlobalFrame;

    public static void SuppressGlobalThisFrame(int frameCount)
    {
        _suppressGlobalFrame = frameCount;
    }

    public static bool IsGlobalSuppressed(int frameCount)
    {
        return _suppressGlobalFrame == frameCount;
    }
}

