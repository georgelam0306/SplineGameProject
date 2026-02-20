using System.Runtime.CompilerServices;

namespace FixedMath;

/// <summary>
/// Deterministic Perlin noise using Fixed64 math.
/// Suitable for use in simulation code across networked clients.
/// All operations are fully deterministic given the same inputs.
/// </summary>
public static class DeterministicPerlinNoise
{
    // 256 pre-computed permutation table (standard Perlin)
    private static readonly byte[] Perm = GeneratePermutationTable();

    // Pre-computed gradient vectors (8 directions for 2D)
    private static readonly Fixed64[] GradX;
    private static readonly Fixed64[] GradY;

    static DeterministicPerlinNoise()
    {
        // Initialize gradient vectors
        // 8 directions: N, NE, E, SE, S, SW, W, NW
        GradX = new Fixed64[8];
        GradY = new Fixed64[8];

        // (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)
        // Normalized approximations using Fixed64
        var sqrt2Inv = Fixed64.FromRaw(46341); // ~0.7071 in Q16

        GradX[0] = Fixed64.OneValue;
        GradY[0] = Fixed64.Zero;
        GradX[1] = sqrt2Inv;
        GradY[1] = sqrt2Inv;
        GradX[2] = Fixed64.Zero;
        GradY[2] = Fixed64.OneValue;
        GradX[3] = -sqrt2Inv;
        GradY[3] = sqrt2Inv;
        GradX[4] = -Fixed64.OneValue;
        GradY[4] = Fixed64.Zero;
        GradX[5] = -sqrt2Inv;
        GradY[5] = -sqrt2Inv;
        GradX[6] = Fixed64.Zero;
        GradY[6] = -Fixed64.OneValue;
        GradX[7] = sqrt2Inv;
        GradY[7] = -sqrt2Inv;
    }

    private static byte[] GeneratePermutationTable()
    {
        // Standard permutation table for Perlin noise
        // Using a fixed sequence for determinism
        var perm = new byte[512];
        var p = new byte[256];

        // Initialize with identity
        for (int i = 0; i < 256; i++)
            p[i] = (byte)i;

        // Shuffle using a fixed seed (deterministic)
        uint state = 0x12345678;
        for (int i = 255; i > 0; i--)
        {
            // Simple hash-based swap
            state = (state ^ (state >> 16)) * 0x85EBCA6B;
            state = (state ^ (state >> 13)) * 0xC2B2AE35;
            state = state ^ (state >> 16);
            int j = (int)(state % (uint)(i + 1));

            (p[i], p[j]) = (p[j], p[i]);
        }

        // Double the permutation table to avoid index wrapping
        for (int i = 0; i < 256; i++)
        {
            perm[i] = p[i];
            perm[256 + i] = p[i];
        }

        return perm;
    }

    /// <summary>
    /// Returns noise value in range [0, 1] for the given coordinates.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Noise(Fixed64 x, Fixed64 y, int seed)
    {
        // Apply seed offset to coordinates for variation
        x += Fixed64.FromInt(seed & 0xFF);
        y += Fixed64.FromInt((seed >> 8) & 0xFF);

        // Find unit grid cell containing point
        int xi = FloorToInt(x);
        int yi = FloorToInt(y);

        // Relative coordinates within cell (0 to 1)
        Fixed64 xf = x - Fixed64.FromInt(xi);
        Fixed64 yf = y - Fixed64.FromInt(yi);

        // Wrap to 0-255 for permutation table lookup
        xi &= 255;
        yi &= 255;

        // Hash coordinates to get gradient indices
        int gi00 = Perm[Perm[xi] + yi] & 7;
        int gi10 = Perm[Perm[xi + 1] + yi] & 7;
        int gi01 = Perm[Perm[xi] + yi + 1] & 7;
        int gi11 = Perm[Perm[xi + 1] + yi + 1] & 7;

        // Calculate dot products with gradient vectors
        Fixed64 n00 = DotGradient(gi00, xf, yf);
        Fixed64 n10 = DotGradient(gi10, xf - Fixed64.OneValue, yf);
        Fixed64 n01 = DotGradient(gi01, xf, yf - Fixed64.OneValue);
        Fixed64 n11 = DotGradient(gi11, xf - Fixed64.OneValue, yf - Fixed64.OneValue);

        // Compute fade curves
        Fixed64 u = Fade(xf);
        Fixed64 v = Fade(yf);

        // Bilinear interpolation
        Fixed64 nx0 = Lerp(n00, n10, u);
        Fixed64 nx1 = Lerp(n01, n11, u);
        Fixed64 result = Lerp(nx0, nx1, v);

        // Convert from [-1, 1] to [0, 1]
        return (result + Fixed64.OneValue) >> 1;
    }

    /// <summary>
    /// Fractal Brownian Motion - multiple octaves of noise.
    /// Returns value in range [0, 1].
    /// </summary>
    public static Fixed64 FBM(Fixed64 x, Fixed64 y, int octaves, Fixed64 persistence, int seed)
    {
        Fixed64 total = Fixed64.Zero;
        Fixed64 amplitude = Fixed64.OneValue;
        Fixed64 frequency = Fixed64.OneValue;
        Fixed64 maxValue = Fixed64.Zero;

        for (int i = 0; i < octaves; i++)
        {
            total += Noise(x * frequency, y * frequency, seed + i) * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= Fixed64.FromInt(2);
        }

        // Normalize to [0, 1]
        return total / maxValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64 DotGradient(int gradientIndex, Fixed64 x, Fixed64 y)
    {
        return GradX[gradientIndex] * x + GradY[gradientIndex] * y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64 Fade(Fixed64 t)
    {
        // Smootherstep: 6t^5 - 15t^4 + 10t^3
        // Approximated for Fixed64
        Fixed64 t3 = t * t * t;
        Fixed64 t4 = t3 * t;
        Fixed64 t5 = t4 * t;

        return Fixed64.FromInt(6) * t5 - Fixed64.FromInt(15) * t4 + Fixed64.FromInt(10) * t3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64 Lerp(Fixed64 a, Fixed64 b, Fixed64 t)
    {
        return a + (b - a) * t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloorToInt(Fixed64 value)
    {
        // Fixed64 stores value * 65536, so we need to handle negative values correctly
        long raw = value.Raw;
        if (raw >= 0)
            return (int)(raw >> 16);
        else
            return (int)((raw - 65535) >> 16);
    }
}
