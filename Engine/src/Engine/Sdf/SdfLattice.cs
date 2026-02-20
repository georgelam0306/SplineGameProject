using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DerpLib.Sdf;

/// <summary>
/// A 4x4 lattice grid for Free-Form Deformation (FFD).
/// Each control point stores an offset from its rest position.
/// The lattice covers a normalized [-1, 1] space around the shape center.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SdfLattice
{
    // 4x4 grid = 16 control points, each is a vec2 offset
    // Total: 16 * 8 = 128 bytes
    public Vector2 P00, P01, P02, P03;  // Row 0 (bottom)
    public Vector2 P10, P11, P12, P13;  // Row 1
    public Vector2 P20, P21, P22, P23;  // Row 2
    public Vector2 P30, P31, P32, P33;  // Row 3 (top)

    /// <summary>Size of this struct in bytes (128).</summary>
    public const int SizeInBytes = 16 * 8;

    /// <summary>Creates an identity lattice (no deformation).</summary>
    public static SdfLattice Identity => new();

    /// <summary>
    /// Get/set control point by grid index.
    /// </summary>
    public Vector2 this[int row, int col]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetPoint(row, col);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => SetPoint(row, col, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly Vector2 GetPoint(int row, int col)
    {
        int index = row * 4 + col;
        return index switch
        {
            0 => P00, 1 => P01, 2 => P02, 3 => P03,
            4 => P10, 5 => P11, 6 => P12, 7 => P13,
            8 => P20, 9 => P21, 10 => P22, 11 => P23,
            12 => P30, 13 => P31, 14 => P32, 15 => P33,
            _ => Vector2.Zero
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetPoint(int row, int col, Vector2 value)
    {
        int index = row * 4 + col;
        switch (index)
        {
            case 0: P00 = value; break;
            case 1: P01 = value; break;
            case 2: P02 = value; break;
            case 3: P03 = value; break;
            case 4: P10 = value; break;
            case 5: P11 = value; break;
            case 6: P12 = value; break;
            case 7: P13 = value; break;
            case 8: P20 = value; break;
            case 9: P21 = value; break;
            case 10: P22 = value; break;
            case 11: P23 = value; break;
            case 12: P30 = value; break;
            case 13: P31 = value; break;
            case 14: P32 = value; break;
            case 15: P33 = value; break;
        }
    }

    /// <summary>
    /// Create a wave-like lattice deformation.
    /// </summary>
    public static SdfLattice Wave(float amplitude, float frequency, float phase = 0f)
    {
        var lattice = new SdfLattice();
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                // Normalized position [-1, 1]
                float x = (col / 3f) * 2f - 1f;
                float y = (row / 3f) * 2f - 1f;

                // Wave displacement
                float offset = MathF.Sin(x * frequency + phase) * amplitude;
                lattice[row, col] = new Vector2(0, offset);
            }
        }
        return lattice;
    }

    /// <summary>
    /// Create a pinch/bulge lattice centered at origin.
    /// </summary>
    public static SdfLattice Bulge(float strength)
    {
        var lattice = new SdfLattice();
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                // Normalized position [-1, 1]
                float x = (col / 3f) * 2f - 1f;
                float y = (row / 3f) * 2f - 1f;

                // Distance from center
                float dist = MathF.Sqrt(x * x + y * y);
                float falloff = MathF.Max(0, 1f - dist);

                // Push outward (positive strength) or inward (negative)
                float scale = falloff * falloff * strength;
                lattice[row, col] = new Vector2(x * scale, y * scale);
            }
        }
        return lattice;
    }

    /// <summary>
    /// Create a twist lattice.
    /// </summary>
    public static SdfLattice Twist(float angle)
    {
        var lattice = new SdfLattice();
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                // Normalized position [-1, 1]
                float x = (col / 3f) * 2f - 1f;
                float y = (row / 3f) * 2f - 1f;

                // Distance-based rotation
                float dist = MathF.Sqrt(x * x + y * y);
                float theta = dist * angle;
                float c = MathF.Cos(theta);
                float s = MathF.Sin(theta);

                // Rotated position minus original = offset
                float rx = x * c - y * s;
                float ry = x * s + y * c;
                lattice[row, col] = new Vector2(rx - x, ry - y);
            }
        }
        return lattice;
    }

    /// <summary>
    /// Create a shear lattice.
    /// </summary>
    public static SdfLattice Shear(float shearX, float shearY)
    {
        var lattice = new SdfLattice();
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                float x = (col / 3f) * 2f - 1f;
                float y = (row / 3f) * 2f - 1f;
                lattice[row, col] = new Vector2(y * shearX, x * shearY);
            }
        }
        return lattice;
    }

    /// <summary>
    /// Create a bend lattice (bends along Y axis).
    /// </summary>
    public static SdfLattice Bend(float curvature)
    {
        var lattice = new SdfLattice();
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                float x = (col / 3f) * 2f - 1f;
                float y = (row / 3f) * 2f - 1f;

                // Quadratic bend based on Y position
                float bend = y * y * curvature;
                lattice[row, col] = new Vector2(bend, 0);
            }
        }
        return lattice;
    }

    /// <summary>
    /// Interpolate two lattices.
    /// </summary>
    public static SdfLattice Lerp(in SdfLattice a, in SdfLattice b, float t)
    {
        var result = new SdfLattice();
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                result[row, col] = Vector2.Lerp(a[row, col], b[row, col], t);
            }
        }
        return result;
    }
}
