using System;

namespace DerpLib.Ecs.Setup;

/// <summary>
/// Syntax-only builder used by the Derp.Ecs world setup source generator.
/// Never instantiated at runtime.
/// </summary>
public readonly struct DerpEcsSetupBuilder
{
    public DerpEcsArchetypeSetupBuilder<TKind> Archetype<TKind>() where TKind : unmanaged
    {
        throw new InvalidOperationException(DerpEcsSetupConstants.SyntaxOnlyMessage);
    }
}
