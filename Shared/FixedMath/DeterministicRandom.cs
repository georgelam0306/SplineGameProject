using System.Runtime.CompilerServices;

namespace FixedMath;

public static class DeterministicRandom
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Hash(int frame, int slot, int salt)
    {
        uint state = (uint)frame;
        state = Scramble(state, (uint)slot);
        state = Scramble(state, (uint)salt);
        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Scramble(uint state, uint value)
    {
        state ^= value;
        state ^= state >> 16;
        state *= 0x85EBCA6B;
        state ^= state >> 13;
        state *= 0xC2B2AE35;
        state ^= state >> 16;
        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Range(int frame, int slot, int salt, int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            return minInclusive;
        }

        uint hash = Hash(frame, slot, salt);
        int range = maxExclusive - minInclusive;
        return minInclusive + (int)(hash % (uint)range);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 RangeFixed64(int frame, int slot, int salt, int minInclusive, int maxExclusive)
    {
        return Fixed64.FromInt(Range(frame, slot, salt, minInclusive, maxExclusive));
    }

    // Seed-aware variants for per-session randomization
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint HashWithSeed(int sessionSeed, int frame, int slot, int salt)
    {
        uint state = (uint)sessionSeed;
        state = Scramble(state, (uint)frame);
        state = Scramble(state, (uint)slot);
        state = Scramble(state, (uint)salt);
        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RangeWithSeed(int sessionSeed, int frame, int slot, int salt, int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            return minInclusive;
        }

        uint hash = HashWithSeed(sessionSeed, frame, slot, salt);
        int range = maxExclusive - minInclusive;
        return minInclusive + (int)(hash % (uint)range);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 RangeFixed64WithSeed(int sessionSeed, int frame, int slot, int salt, int minInclusive, int maxExclusive)
    {
        return Fixed64.FromInt(RangeWithSeed(sessionSeed, frame, slot, salt, minInclusive, maxExclusive));
    }

    // ============================================================
    // Additional Random Functions
    // ============================================================

    /// <summary>
    /// Returns a random Fixed64 in range [0, 1).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Value(int frame, int slot, int salt)
    {
        uint hash = Hash(frame, slot, salt);
        // Use lower 16 bits to get fraction
        return Fixed64.FromRaw((long)(hash & 0xFFFF));
    }

    /// <summary>
    /// Returns a random Fixed64 in range [0, 1) with session seed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 ValueWithSeed(int sessionSeed, int frame, int slot, int salt)
    {
        uint hash = HashWithSeed(sessionSeed, frame, slot, salt);
        return Fixed64.FromRaw((long)(hash & 0xFFFF));
    }

    /// <summary>
    /// Returns a random Fixed64 in range [min, max).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Range(int frame, int slot, int salt, Fixed64 min, Fixed64 max)
    {
        Fixed64 t = Value(frame, slot, salt);
        return min + (max - min) * t;
    }

    /// <summary>
    /// Returns a random Fixed64 in range [min, max) with session seed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 RangeWithSeed(int sessionSeed, int frame, int slot, int salt, Fixed64 min, Fixed64 max)
    {
        Fixed64 t = ValueWithSeed(sessionSeed, frame, slot, salt);
        return min + (max - min) * t;
    }

    /// <summary>
    /// Returns a random boolean.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Bool(int frame, int slot, int salt)
    {
        return (Hash(frame, slot, salt) & 1) == 0;
    }

    /// <summary>
    /// Returns a random boolean with session seed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoolWithSeed(int sessionSeed, int frame, int slot, int salt)
    {
        return (HashWithSeed(sessionSeed, frame, slot, salt) & 1) == 0;
    }

    /// <summary>
    /// Returns a random boolean with specified probability of being true.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Chance(int frame, int slot, int salt, Fixed64 probability)
    {
        return Value(frame, slot, salt) < probability;
    }

    /// <summary>
    /// Returns a random boolean with specified probability of being true (with seed).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ChanceWithSeed(int sessionSeed, int frame, int slot, int salt, Fixed64 probability)
    {
        return ValueWithSeed(sessionSeed, frame, slot, salt) < probability;
    }

    /// <summary>
    /// Returns -1 or 1 randomly.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Sign(int frame, int slot, int salt)
    {
        return Bool(frame, slot, salt) ? 1 : -1;
    }

    /// <summary>
    /// Returns -1 or 1 randomly (with seed).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SignWithSeed(int sessionSeed, int frame, int slot, int salt)
    {
        return BoolWithSeed(sessionSeed, frame, slot, salt) ? 1 : -1;
    }

    /// <summary>
    /// Returns a random angle in radians [0, 2π).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 Angle(int frame, int slot, int salt)
    {
        return Value(frame, slot, salt) * Fixed64.TwoPi;
    }

    /// <summary>
    /// Returns a random angle in radians [0, 2π) with seed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 AngleWithSeed(int sessionSeed, int frame, int slot, int salt)
    {
        return ValueWithSeed(sessionSeed, frame, slot, salt) * Fixed64.TwoPi;
    }

    /// <summary>
    /// Returns a random unit vector in 2D.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 UnitVector2D(int frame, int slot, int salt)
    {
        Fixed64 angle = Angle(frame, slot, salt);
        Fixed64.SinCosLUT(angle, out Fixed64 sin, out Fixed64 cos);
        return new Fixed64Vec2(cos, sin);
    }

    /// <summary>
    /// Returns a random unit vector in 2D with seed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64Vec2 UnitVector2DWithSeed(int sessionSeed, int frame, int slot, int salt)
    {
        Fixed64 angle = AngleWithSeed(sessionSeed, frame, slot, salt);
        Fixed64.SinCosLUT(angle, out Fixed64 sin, out Fixed64 cos);
        return new Fixed64Vec2(cos, sin);
    }

    /// <summary>
    /// Returns a random point inside a unit circle.
    /// </summary>
    public static Fixed64Vec2 InsideUnitCircle(int frame, int slot, int salt)
    {
        Fixed64Vec2 dir = UnitVector2D(frame, slot, salt);
        Fixed64 r = Fixed64.Sqrt(Value(frame, slot, salt + 1));
        return dir * r;
    }

    /// <summary>
    /// Returns a random point inside a unit circle with seed.
    /// </summary>
    public static Fixed64Vec2 InsideUnitCircleWithSeed(int sessionSeed, int frame, int slot, int salt)
    {
        Fixed64Vec2 dir = UnitVector2DWithSeed(sessionSeed, frame, slot, salt);
        Fixed64 r = Fixed64.Sqrt(ValueWithSeed(sessionSeed, frame, slot, salt + 1));
        return dir * r;
    }

    /// <summary>
    /// Returns a random unit vector in 3D.
    /// </summary>
    public static Fixed64Vec3 UnitVector3D(int frame, int slot, int salt)
    {
        // Use spherical coordinates with uniform distribution
        Fixed64 z = Range(frame, slot, salt, -Fixed64.OneValue, Fixed64.OneValue);
        Fixed64 phi = Value(frame, slot, salt + 1) * Fixed64.TwoPi;

        Fixed64 sqrtPart = Fixed64.Sqrt(Fixed64.OneValue - z * z);
        Fixed64.SinCosLUT(phi, out Fixed64 sinPhi, out Fixed64 cosPhi);

        return new Fixed64Vec3(sqrtPart * cosPhi, sqrtPart * sinPhi, z);
    }

    /// <summary>
    /// Returns a random unit vector in 3D with seed.
    /// </summary>
    public static Fixed64Vec3 UnitVector3DWithSeed(int sessionSeed, int frame, int slot, int salt)
    {
        Fixed64 z = RangeWithSeed(sessionSeed, frame, slot, salt, -Fixed64.OneValue, Fixed64.OneValue);
        Fixed64 phi = ValueWithSeed(sessionSeed, frame, slot, salt + 1) * Fixed64.TwoPi;

        Fixed64 sqrtPart = Fixed64.Sqrt(Fixed64.OneValue - z * z);
        Fixed64.SinCosLUT(phi, out Fixed64 sinPhi, out Fixed64 cosPhi);

        return new Fixed64Vec3(sqrtPart * cosPhi, sqrtPart * sinPhi, z);
    }

    /// <summary>
    /// Returns a random point inside a unit sphere.
    /// </summary>
    public static Fixed64Vec3 InsideUnitSphere(int frame, int slot, int salt)
    {
        Fixed64Vec3 dir = UnitVector3D(frame, slot, salt);
        // Use cube root for uniform volume distribution
        Fixed64 u = Value(frame, slot, salt + 2);
        Fixed64 r = Fixed64.Pow(u, 3) == Fixed64.Zero ? u : u; // Simplified - using linear for now
        // More accurate: r = cbrt(u), but we don't have cbrt, so use u^(1/3) approximation
        // For simplicity, use sqrt(sqrt(u)) as rough approximation
        r = Fixed64.Sqrt(Fixed64.Sqrt(u));
        return dir * r;
    }

    /// <summary>
    /// Returns a random point inside a unit sphere with seed.
    /// </summary>
    public static Fixed64Vec3 InsideUnitSphereWithSeed(int sessionSeed, int frame, int slot, int salt)
    {
        Fixed64Vec3 dir = UnitVector3DWithSeed(sessionSeed, frame, slot, salt);
        Fixed64 u = ValueWithSeed(sessionSeed, frame, slot, salt + 2);
        Fixed64 r = Fixed64.Sqrt(Fixed64.Sqrt(u));
        return dir * r;
    }

    /// <summary>
    /// Picks a random index based on weights (returns -1 if all weights are zero).
    /// </summary>
    public static int WeightedChoice(int frame, int slot, int salt, ReadOnlySpan<Fixed64> weights)
    {
        if (weights.Length == 0) return -1;

        Fixed64 total = Fixed64.Zero;
        for (int i = 0; i < weights.Length; i++)
        {
            total = total + weights[i];
        }

        if (total.Raw == 0) return -1;

        Fixed64 pick = Range(frame, slot, salt, Fixed64.Zero, total);
        Fixed64 cumulative = Fixed64.Zero;

        for (int i = 0; i < weights.Length; i++)
        {
            cumulative = cumulative + weights[i];
            if (pick < cumulative)
            {
                return i;
            }
        }

        return weights.Length - 1;
    }

    /// <summary>
    /// Picks a random index based on weights with seed.
    /// </summary>
    public static int WeightedChoiceWithSeed(int sessionSeed, int frame, int slot, int salt, ReadOnlySpan<Fixed64> weights)
    {
        if (weights.Length == 0) return -1;

        Fixed64 total = Fixed64.Zero;
        for (int i = 0; i < weights.Length; i++)
        {
            total = total + weights[i];
        }

        if (total.Raw == 0) return -1;

        Fixed64 pick = RangeWithSeed(sessionSeed, frame, slot, salt, Fixed64.Zero, total);
        Fixed64 cumulative = Fixed64.Zero;

        for (int i = 0; i < weights.Length; i++)
        {
            cumulative = cumulative + weights[i];
            if (pick < cumulative)
            {
                return i;
            }
        }

        return weights.Length - 1;
    }
}

