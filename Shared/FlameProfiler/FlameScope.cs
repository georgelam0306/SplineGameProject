using System;
using System.Runtime.CompilerServices;

namespace FlameProfiler;

/// <summary>
/// RAII wrapper for flame graph profiling scopes.
/// Use with 'using var _ = FlameScope.Begin(scopeId);'
/// </summary>
public readonly struct FlameScope : IDisposable
{
    private readonly int _nodeIndex;

    private FlameScope(int nodeIndex)
    {
        _nodeIndex = nodeIndex;
    }

    /// <summary>
    /// Begins a new profiling scope.
    /// </summary>
    /// <param name="scopeId">The scope identifier.</param>
    /// <returns>A FlameScope that will end the scope when disposed.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FlameScope Begin(int scopeId)
    {
        var service = FlameProfilerService.Instance;
        if (service == null)
        {
            return new FlameScope(-1);
        }

        int nodeIndex = service.BeginScope(scopeId);
        return new FlameScope(nodeIndex);
    }

    /// <summary>
    /// Ends the profiling scope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_nodeIndex < 0)
        {
            return;
        }

        FlameProfilerService.Instance?.EndScope(_nodeIndex);
    }
}
