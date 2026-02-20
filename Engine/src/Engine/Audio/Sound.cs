namespace DerpLib.Audio;

/// <summary>
/// Lightweight handle to a loaded sound.
/// </summary>
public readonly struct Sound
{
    internal readonly int Index;

    internal Sound(int index)
    {
        Index = index;
    }

    public bool IsValid => Index > 0;
}

