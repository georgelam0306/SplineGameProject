using Core;
using DerpLib.Ecs;
using FixedMath;
using Property;
using System.Numerics;

namespace DerpLib.Ecs.Editor.Smoke;

public partial struct DemoComponent : IEcsComponent
{
    public float X;

    [Property(Name = "Speed", Min = 0, Max = 100)]
    public float Speed;

    [Property(Group = "Debug", Order = 100)]
    public int DebugId;

    public bool Enabled;

    public Fixed64 SimSpeed;
    public Fixed64Vec2 SimPos;

    public Vector2 ViewOffset;
    public Color32 Tint;
    public StringHandle Label;

    [EditorResizable]
    public ListHandle<int> Ints;
}
