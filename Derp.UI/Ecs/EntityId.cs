using System.Runtime.InteropServices;

namespace Derp.UI;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct EntityId
{
    public static readonly EntityId Null = new(0);

    public readonly int Value;

    public EntityId(int value)
    {
        Value = value;
    }

    public bool IsNull => Value == 0;

    public override string ToString()
    {
        return Value.ToString();
    }
}
