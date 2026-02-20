using Microsoft.CodeAnalysis;

namespace DerpLib.Ecs.Generator;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor InvalidDomain = new(
        id: "DERPECS0001",
        title: "Invalid DerpEcsDomain value",
        messageFormat: "DerpEcsDomain must be 'Simulation' or 'View', but was '{0}'",
        category: "DerpLib.Ecs",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidEcsSetup = new(
        id: "DERPECS0002",
        title: "Invalid Derp.Ecs Setup method",
        messageFormat: "Derp.Ecs setup error: {0}",
        category: "DerpLib.Ecs",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

}
