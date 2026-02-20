using System.Diagnostics;

namespace Derp.Doc.Editor;

internal readonly struct DocWorkspacePerformanceCounters
{
    public long CommandOperationCount { get; init; }
    public long CommandItemCount { get; init; }
    public long CommandTotalTicks { get; init; }
    public long CommandMaxTicks { get; init; }
    public long FormulaRecalculationCount { get; init; }
    public long FormulaRecalculationTotalTicks { get; init; }
    public long FormulaRecalculationMaxTicks { get; init; }
    public long FormulaIncrementalCount { get; init; }
    public long FormulaFullCount { get; init; }
    public long FormulaCompileTotalTicks { get; init; }
    public long FormulaCompileMaxTicks { get; init; }
    public long FormulaPlanTotalTicks { get; init; }
    public long FormulaPlanMaxTicks { get; init; }
    public long FormulaDerivedTotalTicks { get; init; }
    public long FormulaDerivedMaxTicks { get; init; }
    public long FormulaEvaluateTotalTicks { get; init; }
    public long FormulaEvaluateMaxTicks { get; init; }
    public long AutoSaveCount { get; init; }
    public long AutoSaveTotalTicks { get; init; }
    public long AutoSaveMaxTicks { get; init; }

    public double CommandAverageMilliseconds =>
        CommandOperationCount > 0
            ? TicksToMilliseconds(CommandTotalTicks) / CommandOperationCount
            : 0;

    public double CommandMaxMilliseconds => TicksToMilliseconds(CommandMaxTicks);

    public double FormulaRecalculationAverageMilliseconds =>
        FormulaRecalculationCount > 0
            ? TicksToMilliseconds(FormulaRecalculationTotalTicks) / FormulaRecalculationCount
            : 0;

    public double FormulaRecalculationMaxMilliseconds => TicksToMilliseconds(FormulaRecalculationMaxTicks);

    public double FormulaCompileAverageMilliseconds =>
        FormulaRecalculationCount > 0
            ? TicksToMilliseconds(FormulaCompileTotalTicks) / FormulaRecalculationCount
            : 0;

    public double FormulaCompileMaxMilliseconds => TicksToMilliseconds(FormulaCompileMaxTicks);

    public double FormulaPlanAverageMilliseconds =>
        FormulaRecalculationCount > 0
            ? TicksToMilliseconds(FormulaPlanTotalTicks) / FormulaRecalculationCount
            : 0;

    public double FormulaPlanMaxMilliseconds => TicksToMilliseconds(FormulaPlanMaxTicks);

    public double FormulaDerivedAverageMilliseconds =>
        FormulaRecalculationCount > 0
            ? TicksToMilliseconds(FormulaDerivedTotalTicks) / FormulaRecalculationCount
            : 0;

    public double FormulaDerivedMaxMilliseconds => TicksToMilliseconds(FormulaDerivedMaxTicks);

    public double FormulaEvaluateAverageMilliseconds =>
        FormulaRecalculationCount > 0
            ? TicksToMilliseconds(FormulaEvaluateTotalTicks) / FormulaRecalculationCount
            : 0;

    public double FormulaEvaluateMaxMilliseconds => TicksToMilliseconds(FormulaEvaluateMaxTicks);

    public double AutoSaveAverageMilliseconds =>
        AutoSaveCount > 0
            ? TicksToMilliseconds(AutoSaveTotalTicks) / AutoSaveCount
            : 0;

    public double AutoSaveMaxMilliseconds => TicksToMilliseconds(AutoSaveMaxTicks);

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}
