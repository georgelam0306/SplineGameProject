using Silk.NET.Vulkan;

namespace DerpLib.Core;

/// <summary>
/// A user-managed descriptor set for custom shader resources (set 1).
/// Engine resources use set 0; user resources use set 1.
/// </summary>
public readonly struct UserDescriptorSet
{
    internal readonly DescriptorSet Handle;
    internal readonly DescriptorSetLayout Layout;

    internal UserDescriptorSet(DescriptorSet handle, DescriptorSetLayout layout)
    {
        Handle = handle;
        Layout = layout;
    }

    public bool IsValid => Handle.Handle != 0;
}
