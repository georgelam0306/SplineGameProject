using System;
using System.Collections.Generic;

namespace GpuStruct.Generator;

/// <summary>
/// Calculates std430 layout offsets and alignments for GPU structs.
///
/// std430 rules (GLSL spec):
/// - Scalars (float, int, uint): size 4, align 4
/// - vec2: size 8, align 8
/// - vec3: size 12, align 16 (!)
/// - vec4: size 16, align 16
/// - mat4: size 64, align 16
/// - Structs: align to max member alignment, size rounded up to alignment
/// </summary>
public static class Std430LayoutCalculator
{
    /// <summary>
    /// Known type layouts for std430.
    /// </summary>
    public static readonly Dictionary<string, (int Size, int Alignment)> KnownTypes = new()
    {
        // Scalars
        ["System.Single"] = (4, 4),      // float
        ["System.Int32"] = (4, 4),       // int
        ["System.UInt32"] = (4, 4),      // uint
        ["float"] = (4, 4),
        ["int"] = (4, 4),
        ["uint"] = (4, 4),

        // System.Numerics vectors
        ["System.Numerics.Vector2"] = (8, 8),
        ["System.Numerics.Vector3"] = (12, 16),  // Key: vec3 has 16-byte alignment!
        ["System.Numerics.Vector4"] = (16, 16),
        ["System.Numerics.Matrix4x4"] = (64, 16),

        // With global:: prefix (from Roslyn)
        ["global::System.Single"] = (4, 4),
        ["global::System.Int32"] = (4, 4),
        ["global::System.UInt32"] = (4, 4),
        ["global::System.Numerics.Vector2"] = (8, 8),
        ["global::System.Numerics.Vector3"] = (12, 16),
        ["global::System.Numerics.Vector4"] = (16, 16),
        ["global::System.Numerics.Matrix4x4"] = (64, 16),

        // Silk.NET vectors (if used)
        ["Silk.NET.Maths.Vector2D`1"] = (8, 8),
        ["Silk.NET.Maths.Vector3D`1"] = (12, 16),
        ["Silk.NET.Maths.Vector4D`1"] = (16, 16),
    };

    /// <summary>
    /// Types that are not allowed in GPU structs.
    /// </summary>
    public static readonly HashSet<string> UnsupportedTypes = new()
    {
        "System.Boolean", // bool - use int instead
        "bool",
        "System.String",
        "string",
        "System.Object",
        "object",
    };

    /// <summary>
    /// Gets the std430 layout info for a type.
    /// </summary>
    /// <param name="typeName">Fully qualified type name</param>
    /// <returns>Size and alignment, or null if unknown</returns>
    public static (int Size, int Alignment)? GetTypeLayout(string typeName)
    {
        // Handle generic types like Vector2D<float>
        var baseType = typeName.Contains("<")
            ? typeName.Substring(0, typeName.IndexOf('<')) + "`1"
            : typeName;

        if (KnownTypes.TryGetValue(baseType, out var layout))
        {
            return layout;
        }

        // Try short name
        if (KnownTypes.TryGetValue(typeName, out layout))
        {
            return layout;
        }

        return null;
    }

    /// <summary>
    /// Checks if a type is unsupported for GPU structs.
    /// </summary>
    public static bool IsUnsupportedType(string typeName)
    {
        return UnsupportedTypes.Contains(typeName);
    }

    /// <summary>
    /// Calculates the next offset aligned to the given alignment.
    /// </summary>
    public static int AlignOffset(int currentOffset, int alignment)
    {
        if (alignment <= 0) return currentOffset;
        var remainder = currentOffset % alignment;
        return remainder == 0 ? currentOffset : currentOffset + (alignment - remainder);
    }

    /// <summary>
    /// Calculates field offsets for a list of fields.
    /// </summary>
    /// <param name="fields">List of (fieldName, typeName) pairs in declaration order</param>
    /// <returns>List of (fieldName, offset, size, alignment) and total struct size</returns>
    public static (List<FieldLayout> Fields, int TotalSize, int MaxAlignment) CalculateLayout(
        IReadOnlyList<(string FieldName, string TypeName)> fields)
    {
        var result = new List<FieldLayout>();
        int currentOffset = 0;
        int maxAlignment = 1;

        foreach (var (fieldName, typeName) in fields)
        {
            var layout = GetTypeLayout(typeName);
            if (layout == null)
            {
                // Unknown type - will be reported as diagnostic
                result.Add(new FieldLayout(fieldName, typeName, -1, 0, 0, isUnknown: true));
                continue;
            }

            var (size, alignment) = layout.Value;

            // Align current offset
            currentOffset = AlignOffset(currentOffset, alignment);

            result.Add(new FieldLayout(fieldName, typeName, currentOffset, size, alignment, isUnknown: false));

            currentOffset += size;
            maxAlignment = Math.Max(maxAlignment, alignment);
        }

        // Final struct size is aligned to max alignment
        int totalSize = AlignOffset(currentOffset, maxAlignment);

        return (result, totalSize, maxAlignment);
    }
}

/// <summary>
/// Layout information for a single field.
/// </summary>
public readonly struct FieldLayout
{
    public string FieldName { get; }
    public string TypeName { get; }
    public int Offset { get; }
    public int Size { get; }
    public int Alignment { get; }
    public bool IsUnknown { get; }

    public FieldLayout(string fieldName, string typeName, int offset, int size, int alignment, bool isUnknown)
    {
        FieldName = fieldName;
        TypeName = typeName;
        Offset = offset;
        Size = size;
        Alignment = alignment;
        IsUnknown = isUnknown;
    }
}
