using System;

namespace GpuStruct;

/// <summary>
/// Marks a partial struct for GPU-compatible std430 layout generation.
/// The source generator will implement partial properties with correct [FieldOffset] attributes.
/// </summary>
/// <example>
/// <code>
/// [GpuStruct]
/// public partial struct ParticleData
/// {
///     public partial Vector3 Position { get; set; }
///     public partial float Lifetime { get; set; }
///     public partial Vector4 Color { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GpuStructAttribute : Attribute
{
}
