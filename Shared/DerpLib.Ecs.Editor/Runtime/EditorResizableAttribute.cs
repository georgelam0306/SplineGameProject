#nullable enable
using System;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Marks a field as editor-resizable (per-instance variable length). In the View domain this is intended
/// to be baked into blob-backed immutable runtime data.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class EditorResizableAttribute : Attribute
{
}

