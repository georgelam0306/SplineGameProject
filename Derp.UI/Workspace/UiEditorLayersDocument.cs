using System.Collections.Generic;

namespace Derp.UI;

internal sealed class UiEditorLayersDocument
{
    public readonly List<byte> ExpandedByStableId = new(capacity: 1024);
    public readonly List<byte> HiddenByStableId = new(capacity: 1024);
    public readonly List<byte> LockedByStableId = new(capacity: 1024);
    public readonly List<string> NameByStableId = new(capacity: 1024);

    public UiEditorLayersDocument()
    {
        // Index 0 is unused (stable ids start at 1).
        Reset();
    }

    public void Reset()
    {
        ExpandedByStableId.Clear();
        HiddenByStableId.Clear();
        LockedByStableId.Clear();
        NameByStableId.Clear();

        ExpandedByStableId.Add(0);
        HiddenByStableId.Add(0);
        LockedByStableId.Add(0);
        NameByStableId.Add(string.Empty);
    }
}
