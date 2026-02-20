using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Core.Input;

/// <summary>
/// Polymorphic input value packed into a Vector4 for zero-allocation storage.
/// Interpretation depends on ActionType.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct InputValue : IEquatable<InputValue>
{
    private readonly Vector4 _value;

    public static readonly InputValue Zero = new(Vector4.Zero);
    public static readonly InputValue One = new(Vector4.One);

    public InputValue(float x = 0f, float y = 0f, float z = 0f, float w = 0f)
    {
        _value = new Vector4(x, y, z, w);
    }

    public InputValue(Vector4 value) => _value = value;
    public InputValue(Vector2 value) => _value = new Vector4(value.X, value.Y, 0f, 0f);
    public InputValue(Vector3 value) => _value = new Vector4(value.X, value.Y, value.Z, 0f);
    public InputValue(bool value) => _value = new Vector4(value ? 1f : 0f, 0f, 0f, 0f);
    public InputValue(float value) => _value = new Vector4(value, 0f, 0f, 0f);

    // Accessors for different interpretations
    public bool IsPressed => _value.X > 0.5f;
    public float Value => _value.X;
    public Vector2 Vector2 => new(_value.X, _value.Y);
    public Vector3 Vector3 => new(_value.X, _value.Y, _value.Z);
    public Vector4 Vector4 => _value;

    // Magnitude for dead zone calculations
    public float Magnitude => _value.Length();
    public float MagnitudeSquared => _value.LengthSquared();

    public static InputValue FromButton(bool pressed) => new(pressed);
    public static InputValue FromAxis(float value) => new(value);
    public static InputValue FromVector2(float x, float y) => new(x, y);
    public static InputValue FromVector2(Vector2 v) => new(v);
    public static InputValue FromVector3(float x, float y, float z) => new(x, y, z);
    public static InputValue FromVector3(Vector3 v) => new(v);

    public bool Equals(InputValue other) => _value == other._value;
    public override bool Equals(object? obj) => obj is InputValue other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();

    public static bool operator ==(InputValue left, InputValue right) => left.Equals(right);
    public static bool operator !=(InputValue left, InputValue right) => !left.Equals(right);
}
