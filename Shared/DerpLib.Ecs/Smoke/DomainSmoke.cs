using System;
using DerpLib.Ecs;

namespace DerpLib.Ecs.Smoke;

internal static class DomainSmoke
{
    public static string GetDomain()
    {
        return DerpEcsDomain.Value ?? throw new InvalidOperationException("DerpEcsDomain generator did not run.");
    }
}
