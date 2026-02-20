using System;
using Derp.Doc.Chat;
using Derp.Doc.Commands;
using Derp.Doc.Export;
using Derp.Doc.Model;
using Derp.Doc.Preferences;
using Derp.Doc.Plugins;
using Derp.Doc.Storage;
using Derp.Doc.Tables;
using DerpLib.ImGui;
using DerpLib.ImGui.Input;
using FixedMath;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Derp.Doc.Editor;

/// <summary>
/// Tracks the state of an active inline cell edit.
/// </summary>
internal struct TableCellEditState
{
    public bool IsEditing;
    public int RowIndex;
    public int ColIndex;
    public string TableId;
    public string OwnerStateKey;
    public string RowId;
    public string ColumnId;
    public char[] Buffer;
    public int BufferLength;
    public int DropdownIndex;
    public bool HasNumberPreviewValue;
    public double NumberPreviewOriginalValue;
    public bool NumberDragPressed;
    public bool IsNumberDragging;
    public float NumberDragStartMouseX;
    public double NumberDragStartValue;
    public float NumberDragAccumulatedDeltaX;
    public bool NumberDragCursorLocked;
    public int NumberDragLockOwnerId;

    public void BeginEdit(
        int rowIndex,
        int colIndex,
        string tableId,
        string ownerStateKey,
        string rowId,
        string columnId,
        string initialValue)
    {
        IsEditing = true;
        RowIndex = rowIndex;
        ColIndex = colIndex;
        TableId = tableId;
        OwnerStateKey = ownerStateKey;
        RowId = rowId;
        ColumnId = columnId;
        Buffer ??= new char[256];
        var span = initialValue.AsSpan();
        int len = Math.Min(span.Length, 256);
        span[..len].CopyTo(Buffer);
        BufferLength = len;
        DropdownIndex = 0;
        HasNumberPreviewValue = false;
        NumberPreviewOriginalValue = 0;
        NumberDragPressed = false;
        IsNumberDragging = false;
        NumberDragStartMouseX = 0;
        NumberDragStartValue = 0;
        NumberDragAccumulatedDeltaX = 0;
        NumberDragCursorLocked = false;
        NumberDragLockOwnerId = HashCode.Combine(tableId, rowId, columnId);
    }

    public void EndEdit()
    {
        if (NumberDragCursorLocked)
        {
            ImMouseDragLock.End(NumberDragLockOwnerId);
        }

        IsEditing = false;
        HasNumberPreviewValue = false;
        NumberPreviewOriginalValue = 0;
        NumberDragPressed = false;
        IsNumberDragging = false;
        NumberDragStartMouseX = 0;
        NumberDragStartValue = 0;
        NumberDragAccumulatedDeltaX = 0;
        NumberDragCursorLocked = false;
        NumberDragLockOwnerId = 0;

        // Close any dropdown that was open for this cell (Select/Relation columns).
        // Once IsEditing is false, DrawEditOverlay won't call DropdownSized,
        // so the dropdown's own close-on-click-outside logic would never run.
        if (Im.IsAnyDropdownOpen)
        {
            Im.CloseAllDropdowns();
        }
    }
}

internal enum ActiveViewKind
{
    Table,
    Document,
}

internal sealed class DocWorkspace : IDerpDocEditorContext
{
    private const long LiveExportDebounceTicks = TimeSpan.TicksPerMillisecond * 250;
    private const long LiveExportRetryTicks = TimeSpan.TicksPerMillisecond * 500;
    private const long AutoSaveDebounceTicks = TimeSpan.TicksPerMillisecond * 750;
    private const long FormulaPreviewDebounceTicks = TimeSpan.TicksPerMillisecond * 48;

    private readonly DocFormulaEngine _formulaEngine = new();
    public Dictionary<string, DerivedMaterializeResult> DerivedResults => _formulaEngine.DerivedResults;

    private int _externalChangePollFrames;
    private DateTime _externalChangeLastWriteUtc;
    private bool _liveExportPending;
    private long _liveExportDueTicks;
    private Task<LiveExportResult>? _liveExportTask;
    private bool _autoSavePending;
    private long _autoSaveDueTicks;
    private Task<AutoSaveResult>? _autoSaveTask;
    private AutoSaveWorker? _autoSaveWorker;
    private int _autoSaveWorkerQueuedRevision = -1;
    private string? _autoSaveWorkerQueuedPath;
    private long _commandOperationCount;
    private long _commandItemCount;
    private long _commandTotalTicks;
    private long _commandMaxTicks;
    private long _formulaRecalculationCount;
    private long _formulaRecalculationTotalTicks;
    private long _formulaRecalculationMaxTicks;
    private long _formulaIncrementalCount;
    private long _formulaFullCount;
    private long _formulaCompileTotalTicks;
    private long _formulaCompileMaxTicks;
    private long _formulaPlanTotalTicks;
    private long _formulaPlanMaxTicks;
    private long _formulaDerivedTotalTicks;
    private long _formulaDerivedMaxTicks;
    private long _formulaEvaluateTotalTicks;
    private long _formulaEvaluateMaxTicks;
    private long _autoSaveCount;
    private long _autoSaveTotalTicks;
    private long _autoSaveMaxTicks;
    private int _latestTableMutationRevisionForLiveExport;
    private int _lastSuccessfulLiveExportRevision;
    private bool _pendingDebouncedFormulaRefresh;
    private long _pendingDebouncedFormulaRefreshDueTicks;
    private int _projectRevision = 1;
    private int _liveValueRevision = 1;
    private int _formulaContextRevision = -1;
    private int _scopeTargetFormulaColumnsRevision = -1;
    private DocFormulaEvalScope _scopeTargetFormulaColumnsCachedScope = DocFormulaEvalScope.None;
    private DocProject? _scopeTargetFormulaColumnsProjectReference;
    private int _nonInteractiveFormulaCoverageRevision = -1;
    private DocProject? _nonInteractiveFormulaCoverageProjectReference;
    private bool _hasNonInteractiveFormulaColumns;
    private ProjectFormulaContext? _formulaContext;
    private readonly List<ViewRowIndexCacheEntry> _viewRowIndexCacheEntries = new();
    private readonly List<ResolvedViewCacheEntry> _resolvedViewCacheEntries = new();
    private readonly List<ViewBindingEvaluationCacheEntry> _viewBindingEvaluationCacheEntries = new();
    private readonly string[] _singleDirtyTableIdScratch = new string[1];
    private readonly Dictionary<string, int> _selectedVariantIdByTableId = new(StringComparer.Ordinal);
    private readonly List<string> _selectedVariantCleanupKeysScratch = new(8);
    private readonly HashSet<string> _pendingDebouncedFormulaDirtyTableIds = new(StringComparer.Ordinal);
    private readonly List<string> _pendingDebouncedFormulaDirtyTableListScratch = new(4);
    private readonly Dictionary<string, List<string>> _scopeTargetFormulaColumnsByTableScratch = new(StringComparer.Ordinal);
    private readonly Dictionary<VariantTableCacheKey, VariantTableSnapshotCacheEntry> _variantTableSnapshotCacheByKey = new();
    private int[] _viewRowScratch = Array.Empty<int>();
    private DocColumn?[] _filterColumnScratch = Array.Empty<DocColumn?>();
    private DocColumn?[] _sortColumnScratch = Array.Empty<DocColumn?>();
    private readonly ViewRowComparer _viewRowComparer = new();
    private readonly ChatController _chatController = new();
    private readonly DocPluginHost _pluginHost = new();

    public string WorkspaceRoot { get; }
    public DocProject Project { get; set; }
    public DocContentTabs ContentTabs { get; }
    public DocTable? ActiveTable { get; set; }
    public DocView? ActiveTableView { get; set; }
    public DocDocument? ActiveDocument { get; set; }
    public ActiveViewKind ActiveView { get; set; } = ActiveViewKind.Table;
    public int SelectedRowIndex { get; set; } = -1;
    public string? ProjectPath { get; set; }
    public string? GameRoot { get; private set; }
    public string? AssetsRoot => string.IsNullOrWhiteSpace(GameRoot) ? null : Path.Combine(GameRoot, "Assets");
    public bool AutoSave { get; set; } = true;
    public bool AutoLiveExport { get; set; } = true;
    public bool IsDirty { get; private set; }
    public TableCellEditState EditState;
    public DocUndoStack UndoStack { get; } = new();

    // Document editor state
    public int FocusedBlockIndex { get; set; } = -1;
    public string? FocusedBlockTextSnapshot { get; set; }
    public bool ShowInspector { get; set; }
    public bool ShowPreferences { get; set; }
    /// <summary>Table to inspect (set from embedded table options button). Null = use ActiveTable.</summary>
    public DocTable? InspectedTable { get; set; }
    /// <summary>Block ID that triggered the inspector (for per-block view selection).</summary>
    public string? InspectedBlockId { get; set; }
    /// <summary>When viewing a child subtable from a specific parent row, this holds the parent row ID for filtering and auto-populating new rows.</summary>
    public string? ActiveParentRowId { get; set; }
    public string? LastComputeError { get; private set; }
    public string? LastStatusMessage { get; private set; }
    public int ProjectRevision => _projectRevision;
    public int LiveValueRevision => _liveValueRevision;
    public ChatSession ChatSession { get; } = new();
    public DocUserPreferences UserPreferences { get; }

    public DocWorkspacePerformanceCounters GetPerformanceCounters()
    {
        return new DocWorkspacePerformanceCounters
        {
            CommandOperationCount = Volatile.Read(ref _commandOperationCount),
            CommandItemCount = Volatile.Read(ref _commandItemCount),
            CommandTotalTicks = Volatile.Read(ref _commandTotalTicks),
            CommandMaxTicks = Volatile.Read(ref _commandMaxTicks),
            FormulaRecalculationCount = Volatile.Read(ref _formulaRecalculationCount),
            FormulaRecalculationTotalTicks = Volatile.Read(ref _formulaRecalculationTotalTicks),
            FormulaRecalculationMaxTicks = Volatile.Read(ref _formulaRecalculationMaxTicks),
            FormulaIncrementalCount = Volatile.Read(ref _formulaIncrementalCount),
            FormulaFullCount = Volatile.Read(ref _formulaFullCount),
            FormulaCompileTotalTicks = Volatile.Read(ref _formulaCompileTotalTicks),
            FormulaCompileMaxTicks = Volatile.Read(ref _formulaCompileMaxTicks),
            FormulaPlanTotalTicks = Volatile.Read(ref _formulaPlanTotalTicks),
            FormulaPlanMaxTicks = Volatile.Read(ref _formulaPlanMaxTicks),
            FormulaDerivedTotalTicks = Volatile.Read(ref _formulaDerivedTotalTicks),
            FormulaDerivedMaxTicks = Volatile.Read(ref _formulaDerivedMaxTicks),
            FormulaEvaluateTotalTicks = Volatile.Read(ref _formulaEvaluateTotalTicks),
            FormulaEvaluateMaxTicks = Volatile.Read(ref _formulaEvaluateMaxTicks),
            AutoSaveCount = Volatile.Read(ref _autoSaveCount),
            AutoSaveTotalTicks = Volatile.Read(ref _autoSaveTotalTicks),
            AutoSaveMaxTicks = Volatile.Read(ref _autoSaveMaxTicks),
        };
    }

    private readonly struct VariantTableCacheKey : IEquatable<VariantTableCacheKey>
    {
        public VariantTableCacheKey(string tableId, int variantId)
        {
            TableId = tableId;
            VariantId = variantId;
        }

        public string TableId { get; }
        public int VariantId { get; }

        public bool Equals(VariantTableCacheKey other)
        {
            return VariantId == other.VariantId &&
                   string.Equals(TableId, other.TableId, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is VariantTableCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(StringComparer.Ordinal.GetHashCode(TableId), VariantId);
        }
    }

    private sealed class VariantTableSnapshotCacheEntry
    {
        public int Revision = -1;
        public DocTable? TableSnapshot;
        public Dictionary<string, DocRow>? RowById;
    }

    private enum VariantCommandRewriteResult
    {
        NotRewritten,
        Rewritten,
        Suppressed,
    }

    private sealed class AutoSaveRequest
    {
        public required string Path { get; init; }
        public required string? GameRoot { get; init; }
        public required string ProjectName { get; init; }
        public required int ProjectRevision { get; init; }
    }

    private readonly struct AutoSaveResult
    {
        public readonly bool Success;
        public readonly string ErrorMessage;
        public readonly AutoSaveRequest Request;
        public readonly long ElapsedTicks;

        public AutoSaveResult(bool success, string errorMessage, AutoSaveRequest request, long elapsedTicks)
        {
            Success = success;
            ErrorMessage = errorMessage;
            Request = request;
            ElapsedTicks = elapsedTicks;
        }
    }

    private sealed class LiveExportRequest
    {
        public required string DbRoot { get; init; }
        public required string? GameRoot { get; init; }
        public required int ProjectRevision { get; init; }
    }

    private readonly struct LiveExportResult
    {
        public readonly bool Success;
        public readonly bool HasErrors;
        public readonly string ErrorMessage;
        public readonly LiveExportRequest Request;
        public readonly long ElapsedTicks;

        public LiveExportResult(bool success, bool hasErrors, string errorMessage, LiveExportRequest request, long elapsedTicks)
        {
            Success = success;
            HasErrors = hasErrors;
            ErrorMessage = errorMessage;
            Request = request;
            ElapsedTicks = elapsedTicks;
        }
    }

    private sealed class AutoSaveWorker
    {
        private readonly Channel<WorkerMessage> _messageChannel = Channel.CreateUnbounded<WorkerMessage>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });

        private readonly Task _workerTask;
        private readonly DocUndoStack _undoStack = new();
        private DocProject? _mirrorProject;
        private bool _mirrorDirty;
        private bool _isStopping;

        public AutoSaveWorker()
        {
            _workerTask = Task.Run(ProcessMessagesAsync);
        }

        public void EnqueueReset(DocProject snapshot, bool isDirty)
        {
            WriteMessage(WorkerMessage.CreateReset(snapshot, isDirty));
        }

        public void EnqueueExecute(DocCommand command)
        {
            WriteMessage(WorkerMessage.CreateExecute(command));
        }

        public void EnqueueUndo()
        {
            WriteMessage(WorkerMessage.CreateUndo());
        }

        public void EnqueueRedo()
        {
            WriteMessage(WorkerMessage.CreateRedo());
        }

        public Task<AutoSaveResult> RequestSaveAsync(AutoSaveRequest request)
        {
            var completion = new TaskCompletionSource<AutoSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            WriteMessage(WorkerMessage.CreateSave(request, completion));
            return completion.Task;
        }

        public Task<LiveExportResult> RequestLiveExportAsync(LiveExportRequest request)
        {
            var completion = new TaskCompletionSource<LiveExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            WriteMessage(WorkerMessage.CreateLiveExport(request, completion));
            return completion.Task;
        }

        public void Shutdown()
        {
            if (_isStopping)
            {
                return;
            }

            _isStopping = true;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            WriteMessage(WorkerMessage.CreateStop(completion));
            completion.Task.GetAwaiter().GetResult();
            _workerTask.GetAwaiter().GetResult();
        }

        private void WriteMessage(WorkerMessage message)
        {
            if (!_messageChannel.Writer.TryWrite(message))
            {
                throw new InvalidOperationException("Auto-save worker is not accepting messages.");
            }
        }

        private async Task ProcessMessagesAsync()
        {
            while (await _messageChannel.Reader.WaitToReadAsync())
            {
                while (_messageChannel.Reader.TryRead(out var message))
                {
                    switch (message.Kind)
                    {
                        case WorkerMessageKind.Reset:
                            _mirrorProject = message.ProjectSnapshot!;
                            _undoStack.Clear();
                            _mirrorDirty = message.IsDirty;
                            break;
                        case WorkerMessageKind.Execute:
                            if (_mirrorProject != null && message.Command != null)
                            {
                                _undoStack.Execute(message.Command, _mirrorProject);
                                _mirrorDirty = true;
                            }
                            break;
                        case WorkerMessageKind.Undo:
                            if (_mirrorProject != null)
                            {
                                _undoStack.Undo(_mirrorProject);
                                _mirrorDirty = true;
                            }
                            break;
                        case WorkerMessageKind.Redo:
                            if (_mirrorProject != null)
                            {
                                _undoStack.Redo(_mirrorProject);
                                _mirrorDirty = true;
                            }
                            break;
                        case WorkerMessageKind.Save:
                            if (message.SaveCompletion != null && message.SaveRequest != null)
                            {
                                var saveResult = SaveMirrorProject(message.SaveRequest);
                                message.SaveCompletion.TrySetResult(saveResult);
                            }
                            break;
                        case WorkerMessageKind.LiveExport:
                            if (message.LiveExportCompletion != null && message.LiveExportRequest != null)
                            {
                                var exportResult = ExportMirrorProject(message.LiveExportRequest);
                                message.LiveExportCompletion.TrySetResult(exportResult);
                            }
                            break;
                        case WorkerMessageKind.Stop:
                            if (message.StopCompletion != null)
                            {
                                message.StopCompletion.TrySetResult(true);
                            }
                            return;
                    }
                }
            }
        }

        private AutoSaveResult SaveMirrorProject(AutoSaveRequest request)
        {
            long saveStart = Stopwatch.GetTimestamp();
            try
            {
                if (_mirrorProject == null)
                {
                    throw new InvalidOperationException("Auto-save mirror project is not initialized.");
                }

                ProjectSerializer.Save(_mirrorProject, request.Path);
                _mirrorDirty = false;
                return new AutoSaveResult(
                    success: true,
                    errorMessage: "",
                    request,
                    elapsedTicks: Stopwatch.GetTimestamp() - saveStart);
            }
            catch (Exception ex)
            {
                return new AutoSaveResult(
                    success: false,
                    errorMessage: ex.Message,
                    request,
                    elapsedTicks: Stopwatch.GetTimestamp() - saveStart);
            }
        }

        private LiveExportResult ExportMirrorProject(LiveExportRequest request)
        {
            long exportStart = Stopwatch.GetTimestamp();
            try
            {
                if (_mirrorProject == null)
                {
                    throw new InvalidOperationException("Live-export mirror project is not initialized.");
                }

                var options = new ExportPipelineOptions
                {
                    BinaryOutputPath = DocProjectPaths.ResolveDefaultBinaryPath(request.DbRoot, request.GameRoot),
                    LiveBinaryOutputPath = DocProjectPaths.ResolveDefaultLiveBinaryPath(request.DbRoot),
                    GeneratedOutputDirectory = "",
                    WriteManifest = false,
                };

                var pipeline = new DocExportPipeline();
                var exportResult = pipeline.Export(_mirrorProject, options);
                bool hasErrors = exportResult.HasErrors;
                return new LiveExportResult(
                    success: !hasErrors,
                    hasErrors: hasErrors,
                    errorMessage: hasErrors ? BuildFirstExportErrorMessage(exportResult.Diagnostics) : "",
                    request,
                    elapsedTicks: Stopwatch.GetTimestamp() - exportStart);
            }
            catch (Exception ex)
            {
                return new LiveExportResult(
                    success: false,
                    hasErrors: true,
                    errorMessage: ex.Message,
                    request,
                    elapsedTicks: Stopwatch.GetTimestamp() - exportStart);
            }
        }

        private static string BuildFirstExportErrorMessage(List<ExportDiagnostic> diagnostics)
        {
            for (int diagnosticIndex = 0; diagnosticIndex < diagnostics.Count; diagnosticIndex++)
            {
                if (diagnostics[diagnosticIndex].Severity == ExportDiagnosticSeverity.Error)
                {
                    return diagnostics[diagnosticIndex].Message;
                }
            }

            return "Live export failed.";
        }

        private enum WorkerMessageKind
        {
            Reset,
            Execute,
            Undo,
            Redo,
            Save,
            LiveExport,
            Stop,
        }

        private sealed class WorkerMessage
        {
            public WorkerMessageKind Kind { get; private init; }
            public DocProject? ProjectSnapshot { get; private init; }
            public bool IsDirty { get; private init; }
            public DocCommand? Command { get; private init; }
            public AutoSaveRequest? SaveRequest { get; private init; }
            public TaskCompletionSource<AutoSaveResult>? SaveCompletion { get; private init; }
            public LiveExportRequest? LiveExportRequest { get; private init; }
            public TaskCompletionSource<LiveExportResult>? LiveExportCompletion { get; private init; }
            public TaskCompletionSource<bool>? StopCompletion { get; private init; }

            public static WorkerMessage CreateReset(DocProject projectSnapshot, bool isDirty)
            {
                return new WorkerMessage
                {
                    Kind = WorkerMessageKind.Reset,
                    ProjectSnapshot = projectSnapshot,
                    IsDirty = isDirty,
                };
            }

            public static WorkerMessage CreateExecute(DocCommand command)
            {
                return new WorkerMessage
                {
                    Kind = WorkerMessageKind.Execute,
                    Command = command,
                };
            }

            public static WorkerMessage CreateUndo()
            {
                return new WorkerMessage
                {
                    Kind = WorkerMessageKind.Undo,
                };
            }

            public static WorkerMessage CreateRedo()
            {
                return new WorkerMessage
                {
                    Kind = WorkerMessageKind.Redo,
                };
            }

            public static WorkerMessage CreateSave(AutoSaveRequest request, TaskCompletionSource<AutoSaveResult> completion)
            {
                return new WorkerMessage
                {
                    Kind = WorkerMessageKind.Save,
                    SaveRequest = request,
                    SaveCompletion = completion,
                };
            }

            public static WorkerMessage CreateLiveExport(LiveExportRequest request, TaskCompletionSource<LiveExportResult> completion)
            {
                return new WorkerMessage
                {
                    Kind = WorkerMessageKind.LiveExport,
                    LiveExportRequest = request,
                    LiveExportCompletion = completion,
                };
            }

            public static WorkerMessage CreateStop(TaskCompletionSource<bool> completion)
            {
                return new WorkerMessage
                {
                    Kind = WorkerMessageKind.Stop,
                    StopCompletion = completion,
                };
            }
        }
    }

    public void ExecuteCommand(DocCommand command)
    {
        VariantCommandRewriteResult rewriteResult = TryRewriteCommandForSelectedVariant(command, out DocCommand rewrittenCommand);
        if (rewriteResult == VariantCommandRewriteResult.Suppressed)
        {
            return;
        }

        if (rewriteResult == VariantCommandRewriteResult.Rewritten)
        {
            command = rewrittenCommand;
        }

        if (!TryValidateSystemTableCommand(command, out string systemTableError))
        {
            SetStatusMessage(systemTableError);
            return;
        }

        if (!TryValidatePluginLockedTableCommand(command, out string pluginTableError))
        {
            SetStatusMessage(pluginTableError);
            return;
        }

        if (!TryValidateTableInheritanceCommand(command, out string inheritanceCommandError))
        {
            SetStatusMessage(inheritanceCommandError);
            return;
        }

        if (!TryValidateInheritedColumnCommand(command, out string inheritedColumnError))
        {
            SetStatusMessage(inheritedColumnError);
            return;
        }

        CancelPendingDebouncedFormulaRefresh();
        int previousRevision = _projectRevision;
        long commandStart = Stopwatch.GetTimestamp();
        var impactFlags = DocCommandImpact.GetFlags(command.Kind);
        UndoStack.Execute(command, Project);
        if (TryBuildFormulaEvaluationRequestForSingleCommand(command, out var evaluationRequest))
        {
            RecalculateComputedColumnsWithTiming(evaluationRequest);
        }

        RebindActiveTableViewReference();
        ValidateSelectedTableVariants();
        BumpProjectRevision();
        RegisterLiveExportImpact(impactFlags);
        QueueAutoSaveWorkerExecuteCommand(command, previousRevision);
        MarkDirtyAndMaybeAutoSave();
        RecordCommandExecutionTiming(Stopwatch.GetTimestamp() - commandStart, commandItemCount: 1);
    }

    public void ExecuteCommands(IReadOnlyList<DocCommand> commands)
    {
        if (commands.Count == 0)
        {
            return;
        }

        CancelPendingDebouncedFormulaRefresh();
        int previousRevision = _projectRevision;
        long commandStart = Stopwatch.GetTimestamp();
        bool affectsTableState = false;
        var executedCommands = new List<DocCommand>(commands.Count);
        for (int i = 0; i < commands.Count; i++)
        {
            DocCommand command = commands[i];
            VariantCommandRewriteResult rewriteResult = TryRewriteCommandForSelectedVariant(command, out DocCommand rewrittenCommand);
            if (rewriteResult == VariantCommandRewriteResult.Suppressed)
            {
                continue;
            }

            if (rewriteResult == VariantCommandRewriteResult.Rewritten)
            {
                command = rewrittenCommand;
            }

            if (!TryValidateSystemTableCommand(command, out string systemTableError))
            {
                SetStatusMessage(systemTableError);
                continue;
            }

            if (!TryValidatePluginLockedTableCommand(command, out string pluginTableError))
            {
                SetStatusMessage(pluginTableError);
                continue;
            }

            if (!TryValidateTableInheritanceCommand(command, out string inheritanceCommandError))
            {
                SetStatusMessage(inheritanceCommandError);
                continue;
            }

            if (!TryValidateInheritedColumnCommand(command, out string inheritedColumnError))
            {
                SetStatusMessage(inheritedColumnError);
                continue;
            }

            executedCommands.Add(command);
            var impactFlags = DocCommandImpact.GetFlags(command.Kind);
            UndoStack.Execute(command, Project);
            if ((impactFlags & DocCommandImpact.Flags.AffectsTableState) != 0)
            {
                affectsTableState = true;
            }
        }

        if (executedCommands.Count == 0)
        {
            return;
        }

        if (TryBuildFormulaEvaluationRequestForCommands(executedCommands, out var evaluationRequest))
        {
            RecalculateComputedColumnsWithTiming(evaluationRequest);
        }

        RebindActiveTableViewReference();
        ValidateSelectedTableVariants();
        BumpProjectRevision();
        if (affectsTableState)
        {
            RegisterLiveExportImpact(DocCommandImpact.Flags.AffectsTableState);
        }
        QueueAutoSaveWorkerExecuteCommands(executedCommands, previousRevision);
        MarkDirtyAndMaybeAutoSave();
        RecordCommandExecutionTiming(Stopwatch.GetTimestamp() - commandStart, executedCommands.Count);
    }

    public void Undo()
    {
        if (!UndoStack.TryPeekUndoCommand(out var command))
        {
            return;
        }

        CancelPendingDebouncedFormulaRefresh();
        int previousRevision = _projectRevision;
        long commandStart = Stopwatch.GetTimestamp();
        var impactFlags = DocCommandImpact.GetFlags(command.Kind);
        UndoStack.Undo(Project);
        if (TryBuildFormulaEvaluationRequestForSingleCommand(command, out var evaluationRequest))
        {
            RecalculateComputedColumnsWithTiming(evaluationRequest);
        }

        ValidateActiveTable();
        ValidateActiveDocument();
        BumpProjectRevision();
        RegisterLiveExportImpact(impactFlags);
        if (command.Kind == DocCommandKind.ReplaceProjectSnapshot)
        {
            ResetAutoSaveWorkerBaseline(isDirty: true);
            MarkDirtyAndMaybeAutoSave();
        }
        else
        {
            QueueAutoSaveWorkerUndo(previousRevision);
        }

        RecordCommandExecutionTiming(Stopwatch.GetTimestamp() - commandStart, commandItemCount: 1);
    }

    public void Redo()
    {
        if (!UndoStack.TryPeekRedoCommand(out var command))
        {
            return;
        }

        CancelPendingDebouncedFormulaRefresh();
        int previousRevision = _projectRevision;
        long commandStart = Stopwatch.GetTimestamp();
        var impactFlags = DocCommandImpact.GetFlags(command.Kind);
        UndoStack.Redo(Project);
        if (TryBuildFormulaEvaluationRequestForSingleCommand(command, out var evaluationRequest))
        {
            RecalculateComputedColumnsWithTiming(evaluationRequest);
        }

        ValidateActiveTable();
        ValidateActiveDocument();
        BumpProjectRevision();
        RegisterLiveExportImpact(impactFlags);
        if (command.Kind == DocCommandKind.ReplaceProjectSnapshot)
        {
            ResetAutoSaveWorkerBaseline(isDirty: true);
            MarkDirtyAndMaybeAutoSave();
        }
        else
        {
            QueueAutoSaveWorkerRedo(previousRevision);
        }

        RecordCommandExecutionTiming(Stopwatch.GetTimestamp() - commandStart, commandItemCount: 1);
    }

    /// <summary>
    /// Ensures ActiveTable still references a table in the project.
    /// Resets to first table or null if the active table was removed.
    /// </summary>
    public void ValidateActiveTable()
    {
        if (ActiveTable != null && !Project.Tables.Contains(ActiveTable))
        {
            ActiveTable = Project.Tables.Count > 0 ? Project.Tables[0] : null;
            ActiveTableView = ActiveTable?.Views.Count > 0 ? ActiveTable.Views[0] : null;
            SelectedRowIndex = -1;
        }
        else if (ActiveTable != null && ActiveTableView != null)
        {
            RebindActiveTableViewReference();
        }

        if (InspectedTable != null && !Project.Tables.Contains(InspectedTable))
        {
            InspectedTable = null;
            InspectedBlockId = null;
        }

        ValidateSelectedTableVariants();
    }

    private static bool IsKnownTableVariantId(DocTable table, int variantId)
    {
        if (variantId == DocTableVariant.BaseVariantId)
        {
            return true;
        }

        for (int variantIndex = 0; variantIndex < table.Variants.Count; variantIndex++)
        {
            if (table.Variants[variantIndex].Id == variantId)
            {
                return true;
            }
        }

        return false;
    }

    private void ValidateSelectedTableVariants()
    {
        if (_selectedVariantIdByTableId.Count <= 0)
        {
            return;
        }

        _selectedVariantCleanupKeysScratch.Clear();
        foreach (var selectedVariantEntry in _selectedVariantIdByTableId)
        {
            DocTable? table = null;
            for (int tableIndex = 0; tableIndex < Project.Tables.Count; tableIndex++)
            {
                if (string.Equals(Project.Tables[tableIndex].Id, selectedVariantEntry.Key, StringComparison.Ordinal))
                {
                    table = Project.Tables[tableIndex];
                    break;
                }
            }

            if (table == null || !IsKnownTableVariantId(table, selectedVariantEntry.Value))
            {
                _selectedVariantCleanupKeysScratch.Add(selectedVariantEntry.Key);
            }
        }

        for (int cleanupIndex = 0; cleanupIndex < _selectedVariantCleanupKeysScratch.Count; cleanupIndex++)
        {
            _selectedVariantIdByTableId.Remove(_selectedVariantCleanupKeysScratch[cleanupIndex]);
        }
    }

    private void RebindActiveTableViewReference()
    {
        if (ActiveTable == null || ActiveTableView == null)
        {
            return;
        }

        for (int viewIndex = 0; viewIndex < ActiveTable.Views.Count; viewIndex++)
        {
            DocView candidateView = ActiveTable.Views[viewIndex];
            if (ReferenceEquals(candidateView, ActiveTableView))
            {
                return;
            }
        }

        string activeViewId = ActiveTableView.Id;
        if (!string.IsNullOrWhiteSpace(activeViewId))
        {
            for (int viewIndex = 0; viewIndex < ActiveTable.Views.Count; viewIndex++)
            {
                DocView candidateView = ActiveTable.Views[viewIndex];
                if (string.Equals(candidateView.Id, activeViewId, StringComparison.Ordinal))
                {
                    ActiveTableView = candidateView;
                    return;
                }
            }
        }

        ActiveTableView = ActiveTable.Views.Count > 0 ? ActiveTable.Views[0] : null;
    }

    /// <summary>
    /// Ensures ActiveDocument still references a document in the project.
    /// Resets to first document or null if the active document was removed.
    /// </summary>
    public void ValidateActiveDocument()
    {
        if (ActiveDocument != null && !Project.Documents.Contains(ActiveDocument))
        {
            if (Project.Documents.Count > 0)
            {
                ActiveDocument = Project.Documents[0];
                ActiveView = ActiveViewKind.Document;
            }
            else if (Project.Tables.Count > 0)
            {
                ActiveDocument = null;
                ActiveTable ??= Project.Tables[0];
                ActiveView = ActiveViewKind.Table;
            }
            else
            {
                ActiveDocument = null;
                ActiveView = ActiveViewKind.Table;
            }
            FocusedBlockIndex = -1;
            FocusedBlockTextSnapshot = null;
        }
    }

    public DocWorkspace()
    {
        WorkspaceRoot = DocWorkspaceRootLocator.FindWorkspaceRoot();
        Project = CreateDefaultProject();
        _pluginHost.ReloadForProject(string.Empty);
        ChatSession.ProviderKind = ChatProviderKind.Claude;
        ChatSession.AgentType = ChatAgentType.Mcp;
        ContentTabs = new DocContentTabs(this);
        UserPreferences = DocUserPreferencesFile.Read();
        UserPreferences.ClampInPlace();

        if (DocActiveProjectStateFile.TryReadState(WorkspaceRoot, out var state))
        {
            var projectJson = Path.Combine(state.DbRoot, DocProjectPaths.ProjectJsonFileName);
            if (File.Exists(projectJson))
            {
                try
                {
                    LoadProject(state.DbRoot);
                    ContentTabs.RestoreFromActiveProjectState(state);
                    ContentTabs.EnsureDefaultTabOpen();
                    EnsureChatProviderStarted();
                    return;
                }
                catch (Exception ex)
                {
                    LastStatusMessage = "Failed to open last project: " + ex.Message;
                }
            }
        }

        RecalculateComputedColumns();
        if (Project.Tables.Count > 0)
        {
            ActiveTable = Project.Tables[0];
        }
        ContentTabs.EnsureDefaultTabOpen();

        EnsureChatProviderStarted();
    }

    public bool SetUiFontSize(float fontSize)
    {
        if (!UserPreferences.SetUiFontSize(fontSize))
        {
            return false;
        }

        SaveUserPreferences();
        return true;
    }

    public bool SetChatMessageFontSize(float fontSize)
    {
        if (!UserPreferences.SetChatMessageFontSize(fontSize))
        {
            return false;
        }

        SaveUserPreferences();
        return true;
    }

    public bool SetChatInputFontSize(float fontSize)
    {
        if (!UserPreferences.SetChatInputFontSize(fontSize))
        {
            return false;
        }

        SaveUserPreferences();
        return true;
    }

    public bool SetSubtablePreviewQuality(DocSubtablePreviewQuality quality)
    {
        if (!UserPreferences.SetSubtablePreviewQuality(quality))
        {
            return false;
        }

        SaveUserPreferences();
        return true;
    }

    public bool SetSubtablePreviewFrameBudget(int frameBudget)
    {
        if (!UserPreferences.SetSubtablePreviewFrameBudget(frameBudget))
        {
            return false;
        }

        SaveUserPreferences();
        return true;
    }

    public bool ResetUserPreferences()
    {
        if (!UserPreferences.ResetToDefaults())
        {
            return false;
        }

        SaveUserPreferences();
        return true;
    }

    public bool TryGetGlobalPluginSetting(string key, out string value)
    {
        return UserPreferences.TryGetPluginSetting(key, out value);
    }

    public bool SetGlobalPluginSetting(string key, string value)
    {
        if (!UserPreferences.SetPluginSetting(key, value))
        {
            return false;
        }

        SaveUserPreferences();
        return true;
    }

    public bool RemoveGlobalPluginSetting(string key)
    {
        if (!UserPreferences.RemovePluginSetting(key))
        {
            return false;
        }

        SaveUserPreferences();
        return true;
    }

    public bool TryGetProjectPluginSetting(string key, out string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = "";
            return false;
        }

        return Project.PluginSettingsByKey.TryGetValue(key.Trim(), out value!);
    }

    public bool SetProjectPluginSetting(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        string normalizedKey = key.Trim();
        string normalizedValue = value ?? "";
        if (Project.PluginSettingsByKey.TryGetValue(normalizedKey, out string? existingValue) &&
            string.Equals(existingValue, normalizedValue, StringComparison.Ordinal))
        {
            return false;
        }

        Project.PluginSettingsByKey[normalizedKey] = normalizedValue;
        BumpProjectRevision();
        ResetAutoSaveWorkerBaseline(isDirty: true);
        MarkDirtyAndMaybeAutoSave();
        return true;
    }

    public bool RemoveProjectPluginSetting(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!Project.PluginSettingsByKey.Remove(key.Trim()))
        {
            return false;
        }

        BumpProjectRevision();
        ResetAutoSaveWorkerBaseline(isDirty: true);
        MarkDirtyAndMaybeAutoSave();
        return true;
    }

    public bool SetColumnPluginSettings(DocTable table, DocColumn column, string? pluginSettingsJson)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(column);

        string? normalizedSettings = string.IsNullOrWhiteSpace(pluginSettingsJson)
            ? null
            : pluginSettingsJson;
        string? oldSettings = string.IsNullOrWhiteSpace(column.PluginSettingsJson)
            ? null
            : column.PluginSettingsJson;
        if (string.Equals(oldSettings, normalizedSettings, StringComparison.Ordinal))
        {
            return false;
        }

        ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetColumnPluginSettings,
            TableId = table.Id,
            ColumnId = column.Id,
            OldPluginSettingsJson = oldSettings,
            NewPluginSettingsJson = normalizedSettings,
        });
        return true;
    }

    public int GetLoadedPluginCount()
    {
        return _pluginHost.LoadedPluginCount;
    }

    public string GetPluginLoadMessage()
    {
        return _pluginHost.LastLoadMessage;
    }

    public void CopyLoadedPluginInfos(List<DocLoadedPluginInfo> destination)
    {
        _pluginHost.CopyLoadedPluginInfos(destination);
    }

    public void ReloadPluginsForActiveProject()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            _pluginHost.ReloadForProject(string.Empty);
            LastStatusMessage = "Bundled plugins reloaded (no active project path).";
            return;
        }

        ReloadProjectFromDiskPreserveSelection();
        if (string.IsNullOrWhiteSpace(_pluginHost.LastLoadMessage))
        {
            LastStatusMessage = "Plugins reloaded.";
        }
        else
        {
            LastStatusMessage = "Plugins reloaded. " + _pluginHost.LastLoadMessage;
        }
    }

    public void SetStatusMessage(string statusMessage)
    {
        LastStatusMessage = statusMessage;
    }

    private void SaveUserPreferences()
    {
        DocUserPreferencesFile.Write(UserPreferences);
    }

    public IReadOnlyList<string> GetRecentProjectPaths()
    {
        return UserPreferences.RecentProjectPaths;
    }

    public bool TryOpenRecentProject(string path, out string error)
    {
        if (TryOpenFromPath(path, createIfMissing: false, out error))
        {
            return true;
        }

        LastStatusMessage = "Failed to open recent project: " + error;
        if (UserPreferences.RemoveRecentProjectPath(path))
        {
            SaveUserPreferences();
        }

        return false;
    }

    public bool ClearRecentProjects()
    {
        if (!UserPreferences.ClearRecentProjectPaths())
        {
            return false;
        }

        SaveUserPreferences();
        return true;
    }

    private void TrackRecentProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (UserPreferences.AddRecentProjectPath(path))
        {
            SaveUserPreferences();
        }
    }

    public void SetActiveTable(DocTable table, string? parentRowId = null)
    {
        CommitTableCellEditIfActive();
        ActiveTable = table;
        ActiveTableView = table.Views.Count > 0 ? table.Views[0] : null;
        ActiveView = ActiveViewKind.Table;
        InspectedTable = null;
        InspectedBlockId = null;
        ActiveParentRowId = parentRowId;
        SelectedRowIndex = -1;
    }

    public int GetSelectedVariantIdForTable(DocTable table)
    {
        if (table == null)
        {
            return DocTableVariant.BaseVariantId;
        }

        return GetSelectedVariantIdForTable(table.Id);
    }

    public int GetSelectedVariantIdForTable(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return DocTableVariant.BaseVariantId;
        }

        DocTable? table = FindTableById(tableId);
        if (table != null &&
            _selectedVariantIdByTableId.TryGetValue(tableId, out int variantId) &&
            IsKnownTableVariantId(table, variantId))
        {
            return variantId;
        }

        return DocTableVariant.BaseVariantId;
    }

    public bool SetSelectedVariantIdForTable(string tableId, int variantId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return false;
        }

        DocTable? table = FindTableById(tableId);
        if (table == null || !IsKnownTableVariantId(table, variantId))
        {
            return false;
        }

        if (variantId == DocTableVariant.BaseVariantId)
        {
            return _selectedVariantIdByTableId.Remove(tableId);
        }

        if (_selectedVariantIdByTableId.TryGetValue(tableId, out int existingVariantId) &&
            existingVariantId == variantId)
        {
            return false;
        }

        _selectedVariantIdByTableId[tableId] = variantId;
        return true;
    }

    public DocProject CreateProjectSnapshot()
    {
        return CloneProjectSnapshot(Project);
    }

    public DocTable ResolveTableForVariant(DocTable table, int variantId)
    {
        if (variantId == DocTableVariant.BaseVariantId ||
            !IsKnownTableVariantId(table, variantId))
        {
            return table;
        }

        var cacheKey = new VariantTableCacheKey(table.Id, variantId);
        if (!_variantTableSnapshotCacheByKey.TryGetValue(cacheKey, out VariantTableSnapshotCacheEntry? cacheEntry))
        {
            cacheEntry = new VariantTableSnapshotCacheEntry();
            _variantTableSnapshotCacheByKey[cacheKey] = cacheEntry;
        }

        if (cacheEntry.Revision == _projectRevision &&
            cacheEntry.TableSnapshot != null)
        {
            return cacheEntry.TableSnapshot;
        }

        DocTable tableSnapshot = BuildVariantTableSnapshot(table, variantId);
        cacheEntry.Revision = _projectRevision;
        cacheEntry.TableSnapshot = tableSnapshot;
        cacheEntry.RowById = BuildRowByIdMap(tableSnapshot);
        return tableSnapshot;
    }

    private static Dictionary<string, DocRow> BuildRowByIdMap(DocTable table)
    {
        var rowById = new Dictionary<string, DocRow>(table.Rows.Count, StringComparer.Ordinal);
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            DocRow row = table.Rows[rowIndex];
            rowById[row.Id] = row;
        }

        return rowById;
    }

    private VariantCommandRewriteResult TryRewriteCommandForSelectedVariant(
        DocCommand command,
        out DocCommand rewrittenCommand)
    {
        rewrittenCommand = command;
        if (!IsVariantTableMutationCommand(command.Kind))
        {
            return VariantCommandRewriteResult.NotRewritten;
        }

        if (command.TableVariantId != DocTableVariant.BaseVariantId)
        {
            return VariantCommandRewriteResult.NotRewritten;
        }

        if (string.IsNullOrWhiteSpace(command.TableId))
        {
            return VariantCommandRewriteResult.NotRewritten;
        }

        int selectedVariantId = GetSelectedVariantIdForTable(command.TableId);
        if (selectedVariantId == DocTableVariant.BaseVariantId)
        {
            return VariantCommandRewriteResult.NotRewritten;
        }

        if (FindTableById(command.TableId) is not DocTable selectedTable ||
            !IsKnownTableVariantId(selectedTable, selectedVariantId))
        {
            return VariantCommandRewriteResult.NotRewritten;
        }

        if (command.Kind == DocCommandKind.MoveRow)
        {
            SetStatusMessage("Row reorder is not supported in variants.");
            return VariantCommandRewriteResult.Suppressed;
        }

        if (FindTableById(command.TableId) is DocTable table &&
            table.IsDerived)
        {
            SetStatusMessage("Variants are not supported for derived tables.");
            return VariantCommandRewriteResult.Suppressed;
        }

        command.TableVariantId = selectedVariantId;
        return VariantCommandRewriteResult.Rewritten;
    }

    private static bool IsVariantTableMutationCommand(DocCommandKind commandKind)
    {
        return commandKind == DocCommandKind.SetCell ||
               commandKind == DocCommandKind.AddRow ||
               commandKind == DocCommandKind.RemoveRow ||
               commandKind == DocCommandKind.MoveRow;
    }

    private bool TryValidateSystemTableCommand(DocCommand command, out string errorMessage)
    {
        errorMessage = "";
        if (string.IsNullOrWhiteSpace(command.TableId))
        {
            return true;
        }

        DocTable? table = FindTableById(command.TableId);
        if (table == null || !DocSystemTableRules.IsSystemTable(table))
        {
            return true;
        }

        if (IsSystemDataMutationCommand(command.Kind) && DocSystemTableRules.IsDataLocked(table))
        {
            errorMessage = "System table '" + table.Name + "' has locked row data.";
            return false;
        }

        if (IsSystemSchemaMutationCommand(command.Kind) && DocSystemTableRules.IsSchemaLocked(table))
        {
            errorMessage = "System table '" + table.Name + "' has a locked schema.";
            return false;
        }

        if (command.Kind == DocCommandKind.AddTableVariant && !DocSystemTableRules.AllowsVariants(table))
        {
            errorMessage = "System table '" + table.Name + "' does not support variants.";
            return false;
        }

        return true;
    }

    public static bool IsPluginSchemaLockedTable(DocTable? table)
    {
        return table != null &&
               table.IsPluginSchemaLocked &&
               !string.IsNullOrWhiteSpace(table.PluginTableTypeId);
    }

    private bool TryValidatePluginLockedTableCommand(DocCommand command, out string errorMessage)
    {
        errorMessage = "";
        if (string.IsNullOrWhiteSpace(command.TableId))
        {
            return true;
        }

        DocTable? table = FindTableById(command.TableId);
        if (table == null)
        {
            return true;
        }

        if (command.Kind == DocCommandKind.RemoveColumn &&
            TryResolveRemoveColumnSnapshot(table, command, out DocColumn? removedColumn) &&
            removedColumn != null &&
            removedColumn.Kind == DocColumnKind.Subtable &&
            !string.IsNullOrWhiteSpace(removedColumn.SubtableId))
        {
            DocTable? childTable = FindTableById(removedColumn.SubtableId);
            if (IsPluginSchemaLockedTable(childTable))
            {
                errorMessage = "Column '" + removedColumn.Name + "' references plugin table '" + childTable!.Name + "' with a locked schema.";
                return false;
            }
        }

        if (!IsPluginSchemaLockedTable(table))
        {
            return true;
        }

        if (!IsSystemSchemaMutationCommand(command.Kind))
        {
            return true;
        }

        errorMessage = "Plugin table '" + table.Name + "' has a locked schema.";
        return false;
    }

    private bool TryValidateTableInheritanceCommand(DocCommand command, out string errorMessage)
    {
        errorMessage = "";
        if (string.IsNullOrWhiteSpace(command.TableId))
        {
            return true;
        }

        if (command.Kind == DocCommandKind.SetTableSchemaSource)
        {
            DocTable? schemaLinkedTable = FindTableById(command.TableId);
            if (schemaLinkedTable == null ||
                string.IsNullOrWhiteSpace(command.NewSchemaSourceTableId))
            {
                return true;
            }

            if (schemaLinkedTable.IsInherited)
            {
                errorMessage = "Inherited table '" + schemaLinkedTable.Name + "' cannot also be schema-linked.";
                return false;
            }

            return true;
        }

        if (command.Kind != DocCommandKind.SetTableInheritanceSource)
        {
            return true;
        }

        DocTable? table = FindTableById(command.TableId);
        if (table == null)
        {
            return true;
        }

        if (table.IsDerived)
        {
            errorMessage = "Derived tables cannot inherit a schema source.";
            return false;
        }

        if (table.IsSchemaLinked)
        {
            errorMessage = "Schema-linked table '" + table.Name + "' cannot also use inheritance.";
            return false;
        }

        string normalizedSourceTableId = command.NewInheritanceSourceTableId ?? "";
        if (string.IsNullOrWhiteSpace(normalizedSourceTableId))
        {
            return true;
        }

        if (string.Equals(normalizedSourceTableId, table.Id, StringComparison.Ordinal))
        {
            errorMessage = "A table cannot inherit from itself.";
            return false;
        }

        DocTable? sourceTable = FindTableById(normalizedSourceTableId);
        if (sourceTable == null)
        {
            errorMessage = "Inheritance source table '" + normalizedSourceTableId + "' was not found.";
            return false;
        }

        if (!TryValidateInheritanceColumnCollisions(table, sourceTable, out errorMessage))
        {
            return false;
        }

        if (WouldCreateInheritanceCycle(Project, table.Id, normalizedSourceTableId))
        {
            errorMessage = "Table inheritance cycle detected.";
            return false;
        }

        return true;
    }

    private bool TryValidateInheritedColumnCommand(DocCommand command, out string errorMessage)
    {
        errorMessage = "";
        if (!IsInheritedColumnMutationCommand(command.Kind) ||
            string.IsNullOrWhiteSpace(command.TableId))
        {
            return true;
        }

        DocTable? table = FindTableById(command.TableId);
        if (table == null || !table.IsInherited)
        {
            return true;
        }

        if (!TryResolveTargetColumnForCommand(table, command, out DocColumn? targetColumn) ||
            targetColumn == null ||
            !targetColumn.IsInherited)
        {
            return true;
        }

        string sourceTableName = ResolveInheritanceSourceTableName(table);
        errorMessage = "Column '" + targetColumn.Name + "' is inherited from '" + sourceTableName + "' and cannot be modified.";
        return false;
    }

    private bool TryResolveTargetColumnForCommand(DocTable table, DocCommand command, out DocColumn? targetColumn)
    {
        targetColumn = null;
        if (command.Kind == DocCommandKind.RemoveColumn)
        {
            return TryResolveRemoveColumnSnapshot(table, command, out targetColumn);
        }

        if (command.Kind == DocCommandKind.MoveColumn)
        {
            if (command.ColumnIndex >= 0 &&
                command.ColumnIndex < table.Columns.Count)
            {
                targetColumn = table.Columns[command.ColumnIndex];
                return true;
            }

            return false;
        }

        if (!string.IsNullOrWhiteSpace(command.ColumnId))
        {
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                DocColumn candidateColumn = table.Columns[columnIndex];
                if (string.Equals(candidateColumn.Id, command.ColumnId, StringComparison.Ordinal))
                {
                    targetColumn = candidateColumn;
                    return true;
                }
            }
        }

        return false;
    }

    private string ResolveInheritanceSourceTableName(DocTable table)
    {
        string sourceTableId = table.InheritanceSourceTableId ?? "";
        if (string.IsNullOrWhiteSpace(sourceTableId))
        {
            return "source table";
        }

        DocTable? sourceTable = FindTableById(sourceTableId);
        if (sourceTable != null)
        {
            return sourceTable.Name;
        }

        return sourceTableId;
    }

    private bool TryValidateInheritanceColumnCollisions(DocTable targetTable, DocTable sourceTable, out string errorMessage)
    {
        errorMessage = "";
        for (int targetColumnIndex = 0; targetColumnIndex < targetTable.Columns.Count; targetColumnIndex++)
        {
            DocColumn targetColumn = targetTable.Columns[targetColumnIndex];
            if (targetColumn.IsInherited)
            {
                continue;
            }

            if (!TryFindInheritanceCollisionSourceColumn(sourceTable, targetColumn, out DocColumn? sourceColumn) ||
                sourceColumn == null)
            {
                continue;
            }

            if (targetColumn.Kind == sourceColumn.Kind &&
                string.Equals(targetColumn.ColumnTypeId, sourceColumn.ColumnTypeId, StringComparison.Ordinal))
            {
                continue;
            }

            errorMessage = "Column '" + targetColumn.Name + "' collides with inherited column '" +
                           sourceColumn.Name + "' from table '" + sourceTable.Name +
                           "' with an incompatible type.";
            return false;
        }

        return true;
    }

    private static bool TryFindInheritanceCollisionSourceColumn(
        DocTable sourceTable,
        DocColumn targetColumn,
        out DocColumn? sourceColumn)
    {
        sourceColumn = null;
        for (int sourceColumnIndex = 0; sourceColumnIndex < sourceTable.Columns.Count; sourceColumnIndex++)
        {
            DocColumn candidateSourceColumn = sourceTable.Columns[sourceColumnIndex];
            if (!string.IsNullOrWhiteSpace(targetColumn.Id) &&
                string.Equals(candidateSourceColumn.Id, targetColumn.Id, StringComparison.Ordinal))
            {
                sourceColumn = candidateSourceColumn;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(targetColumn.Name) &&
                string.Equals(candidateSourceColumn.Name, targetColumn.Name, StringComparison.OrdinalIgnoreCase))
            {
                sourceColumn = candidateSourceColumn;
                return true;
            }
        }

        return false;
    }

    private static bool IsInheritedColumnMutationCommand(DocCommandKind commandKind)
    {
        return commandKind == DocCommandKind.RemoveColumn ||
               commandKind == DocCommandKind.RenameColumn ||
               commandKind == DocCommandKind.MoveColumn ||
               commandKind == DocCommandKind.SetColumnWidth ||
               commandKind == DocCommandKind.SetColumnFormula ||
               commandKind == DocCommandKind.SetColumnPluginSettings ||
               commandKind == DocCommandKind.SetColumnRelation ||
               commandKind == DocCommandKind.SetColumnOptions ||
               commandKind == DocCommandKind.SetColumnModelPreview ||
               commandKind == DocCommandKind.SetColumnHidden ||
               commandKind == DocCommandKind.SetColumnExportIgnore ||
               commandKind == DocCommandKind.SetColumnExportType ||
               commandKind == DocCommandKind.SetColumnNumberSettings ||
               commandKind == DocCommandKind.SetColumnExportEnumName ||
               commandKind == DocCommandKind.SetColumnSubtableDisplay;
    }

    private static bool WouldCreateInheritanceCycle(DocProject project, string tableId, string sourceTableId)
    {
        string currentTableId = sourceTableId;
        for (int depth = 0; depth <= project.Tables.Count; depth++)
        {
            if (string.IsNullOrWhiteSpace(currentTableId))
            {
                return false;
            }

            if (string.Equals(currentTableId, tableId, StringComparison.Ordinal))
            {
                return true;
            }

            DocTable? currentTable = null;
            for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
            {
                DocTable candidateTable = project.Tables[tableIndex];
                if (!string.Equals(candidateTable.Id, currentTableId, StringComparison.Ordinal))
                {
                    continue;
                }

                currentTable = candidateTable;
                break;
            }

            if (currentTable == null)
            {
                return false;
            }

            currentTableId = currentTable.InheritanceSourceTableId ?? "";
        }

        return true;
    }

    private static bool TryResolveRemoveColumnSnapshot(DocTable table, DocCommand command, out DocColumn? removedColumn)
    {
        removedColumn = command.ColumnSnapshot;
        if (removedColumn != null)
        {
            return true;
        }

        if (command.ColumnIndex >= 0 &&
            command.ColumnIndex < table.Columns.Count)
        {
            removedColumn = table.Columns[command.ColumnIndex];
            return true;
        }

        if (!string.IsNullOrWhiteSpace(command.ColumnId))
        {
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                if (string.Equals(table.Columns[columnIndex].Id, command.ColumnId, StringComparison.Ordinal))
                {
                    removedColumn = table.Columns[columnIndex];
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSystemDataMutationCommand(DocCommandKind commandKind)
    {
        return commandKind == DocCommandKind.SetCell ||
               commandKind == DocCommandKind.AddRow ||
               commandKind == DocCommandKind.RemoveRow ||
               commandKind == DocCommandKind.MoveRow;
    }

    private static bool IsSystemSchemaMutationCommand(DocCommandKind commandKind)
    {
        return commandKind == DocCommandKind.RemoveTable ||
               commandKind == DocCommandKind.RenameTable ||
               commandKind == DocCommandKind.AddColumn ||
               commandKind == DocCommandKind.RemoveColumn ||
               commandKind == DocCommandKind.RenameColumn ||
               commandKind == DocCommandKind.MoveColumn ||
               commandKind == DocCommandKind.SetColumnWidth ||
               commandKind == DocCommandKind.SetColumnFormula ||
               commandKind == DocCommandKind.SetColumnPluginSettings ||
               commandKind == DocCommandKind.SetColumnRelation ||
               commandKind == DocCommandKind.SetColumnOptions ||
               commandKind == DocCommandKind.SetColumnModelPreview ||
               commandKind == DocCommandKind.SetColumnHidden ||
               commandKind == DocCommandKind.SetColumnExportIgnore ||
               commandKind == DocCommandKind.SetColumnExportType ||
               commandKind == DocCommandKind.SetColumnNumberSettings ||
               commandKind == DocCommandKind.SetColumnExportEnumName ||
               commandKind == DocCommandKind.SetColumnSubtableDisplay ||
               commandKind == DocCommandKind.SetTableExportConfig ||
               commandKind == DocCommandKind.SetTableKeys ||
               commandKind == DocCommandKind.SetTableSchemaSource ||
               commandKind == DocCommandKind.SetTableInheritanceSource ||
               commandKind == DocCommandKind.SetDerivedConfig ||
               commandKind == DocCommandKind.SetDerivedBaseTable ||
               commandKind == DocCommandKind.AddDerivedStep ||
               commandKind == DocCommandKind.RemoveDerivedStep ||
               commandKind == DocCommandKind.UpdateDerivedStep ||
               commandKind == DocCommandKind.ReorderDerivedStep ||
               commandKind == DocCommandKind.AddDerivedProjection ||
               commandKind == DocCommandKind.RemoveDerivedProjection ||
               commandKind == DocCommandKind.UpdateDerivedProjection ||
               commandKind == DocCommandKind.ReorderDerivedProjection ||
               commandKind == DocCommandKind.SetTableFolder ||
               commandKind == DocCommandKind.AddTableVariable ||
               commandKind == DocCommandKind.RemoveTableVariable ||
               commandKind == DocCommandKind.RenameTableVariable ||
               commandKind == DocCommandKind.SetTableVariableExpression ||
               commandKind == DocCommandKind.SetTableVariableType;
    }

    private static DocTable BuildVariantTableSnapshot(DocTable sourceTable, int variantId)
    {
        DocTable snapshotTable = CloneTableSnapshot(sourceTable);
        if (!TryGetVariantDeltaForTable(snapshotTable, variantId, out DocTableVariantDelta? variantDelta))
        {
            snapshotTable.VariantDeltas.Clear();
            return snapshotTable;
        }

        if (snapshotTable.IsDerived)
        {
            MaterializeDerivedVariantRows(snapshotTable, variantDelta!);
        }
        else
        {
            MaterializeBaseVariantRows(snapshotTable, variantDelta!);
        }

        snapshotTable.VariantDeltas.Clear();
        return snapshotTable;
    }

    private static bool TryGetVariantDeltaForTable(DocTable table, int variantId, out DocTableVariantDelta? variantDelta)
    {
        variantDelta = null;
        if (variantId == DocTableVariant.BaseVariantId)
        {
            return false;
        }

        for (int deltaIndex = 0; deltaIndex < table.VariantDeltas.Count; deltaIndex++)
        {
            DocTableVariantDelta candidateDelta = table.VariantDeltas[deltaIndex];
            if (candidateDelta.VariantId == variantId)
            {
                variantDelta = candidateDelta;
                return true;
            }
        }

        return false;
    }

    private static void MaterializeBaseVariantRows(DocTable table, DocTableVariantDelta variantDelta)
    {
        var materializedRows = new List<DocRow>(table.Rows.Count + variantDelta.AddedRows.Count);
        var materializedRowById = new Dictionary<string, DocRow>(StringComparer.Ordinal);
        var deletedBaseRowIds = new HashSet<string>(variantDelta.DeletedBaseRowIds, StringComparer.Ordinal);
        var validColumnIds = BuildColumnIdSetForTable(table);

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            DocRow row = table.Rows[rowIndex];
            if (deletedBaseRowIds.Contains(row.Id))
            {
                continue;
            }

            TrimRowToColumnSet(row, validColumnIds);
            materializedRows.Add(row);
            materializedRowById[row.Id] = row;
        }

        for (int addedRowIndex = 0; addedRowIndex < variantDelta.AddedRows.Count; addedRowIndex++)
        {
            DocRow addedRow = CloneRowSnapshot(variantDelta.AddedRows[addedRowIndex]);
            TrimRowToColumnSet(addedRow, validColumnIds);
            materializedRows.Add(addedRow);
            materializedRowById[addedRow.Id] = addedRow;
        }

        for (int overrideIndex = 0; overrideIndex < variantDelta.CellOverrides.Count; overrideIndex++)
        {
            DocTableCellOverride cellOverride = variantDelta.CellOverrides[overrideIndex];
            if (!validColumnIds.Contains(cellOverride.ColumnId) ||
                !materializedRowById.TryGetValue(cellOverride.RowId, out DocRow? row))
            {
                continue;
            }

            row.Cells[cellOverride.ColumnId] = cellOverride.Value.Clone();
        }

        table.Rows = materializedRows;
    }

    private static void MaterializeDerivedVariantRows(DocTable table, DocTableVariantDelta variantDelta)
    {
        var validColumnIds = BuildColumnIdSetForTable(table);
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            TrimRowToColumnSet(table.Rows[rowIndex], validColumnIds);
        }

        var rowById = new Dictionary<string, DocRow>(table.Rows.Count, StringComparer.Ordinal);
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            rowById[table.Rows[rowIndex].Id] = table.Rows[rowIndex];
        }

        if (variantDelta.DeletedBaseRowIds.Count > 0)
        {
            for (int rowIndex = table.Rows.Count - 1; rowIndex >= 0; rowIndex--)
            {
                if (!variantDelta.DeletedBaseRowIds.Contains(table.Rows[rowIndex].Id))
                {
                    continue;
                }

                rowById.Remove(table.Rows[rowIndex].Id);
                table.Rows.RemoveAt(rowIndex);
            }
        }

        for (int addedRowIndex = 0; addedRowIndex < variantDelta.AddedRows.Count; addedRowIndex++)
        {
            DocRow addedRow = CloneRowSnapshot(variantDelta.AddedRows[addedRowIndex]);
            TrimRowToColumnSet(addedRow, validColumnIds);
            if (rowById.TryGetValue(addedRow.Id, out DocRow? existingRow))
            {
                existingRow.Cells = addedRow.Cells;
                continue;
            }

            table.Rows.Add(addedRow);
            rowById[addedRow.Id] = addedRow;
        }

        for (int overrideIndex = 0; overrideIndex < variantDelta.CellOverrides.Count; overrideIndex++)
        {
            DocTableCellOverride cellOverride = variantDelta.CellOverrides[overrideIndex];
            if (!validColumnIds.Contains(cellOverride.ColumnId))
            {
                continue;
            }

            if (!rowById.TryGetValue(cellOverride.RowId, out DocRow? row))
            {
                row = new DocRow
                {
                    Id = cellOverride.RowId,
                };
                table.Rows.Add(row);
                rowById[cellOverride.RowId] = row;
            }

            row.Cells[cellOverride.ColumnId] = cellOverride.Value.Clone();
        }
    }

    private static HashSet<string> BuildColumnIdSetForTable(DocTable table)
    {
        var columnIds = new HashSet<string>(table.Columns.Count, StringComparer.Ordinal);
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            columnIds.Add(table.Columns[columnIndex].Id);
        }

        return columnIds;
    }

    private static void TrimRowToColumnSet(DocRow row, HashSet<string> validColumnIds)
    {
        if (row.Cells.Count <= 0)
        {
            return;
        }

        var invalidColumnIds = new List<string>(row.Cells.Count);
        foreach (var cellEntry in row.Cells)
        {
            if (!validColumnIds.Contains(cellEntry.Key))
            {
                invalidColumnIds.Add(cellEntry.Key);
            }
        }

        for (int invalidIndex = 0; invalidIndex < invalidColumnIds.Count; invalidIndex++)
        {
            row.Cells.Remove(invalidColumnIds[invalidIndex]);
        }
    }

    public void SetActiveDocument(DocDocument document)
    {
        CommitTableCellEditIfActive();
        ActiveDocument = document;
        ActiveView = ActiveViewKind.Document;
        FocusedBlockIndex = -1;
        FocusedBlockTextSnapshot = null;
    }

    public void EnsureChatProviderStarted()
    {
        string dbRoot = ProjectPath ?? WorkspaceRoot;
        _chatController.EnsureStarted(ChatSession.ProviderKind, ChatSession.AgentType, WorkspaceRoot, dbRoot);
        ChatSession.ProviderKind = _chatController.ProviderKind;
        ChatSession.AgentType = _chatController.AgentType;
    }

    public void StopChatProvider()
    {
        _chatController.Stop();
        ChatSession.IsConnected = false;
        ChatSession.IsProcessing = false;
    }

    public void SwitchChatProvider(ChatProviderKind providerKind)
    {
        if (providerKind == ChatSession.ProviderKind && _chatController.IsProviderRunning)
        {
            return;
        }

        _chatController.SwitchProvider(this, providerKind);
        ChatSession.ProviderKind = providerKind;
    }

    public void SwitchChatAgentType(ChatAgentType agentType)
    {
        if (agentType == ChatSession.AgentType && _chatController.IsProviderRunning)
        {
            return;
        }

        _chatController.SwitchAgentType(this, agentType);
        ChatSession.AgentType = agentType;
    }

    public bool TrySetChatModel(string modelName, out string errorMessage)
    {
        return _chatController.TrySetModel(this, modelName, out errorMessage);
    }

    public void ClearChatMessages()
    {
        ChatSession.ClearMessages();
    }

    public bool TrySendChatInput(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return false;
        }

        EnsureChatProviderStarted();
        var command = ChatSlashCommandParser.Parse(rawInput, ChatSession.ProviderCommands);
        if (command.Kind == ChatCommandKind.Local)
        {
            return TryExecuteLocalChatCommand(command, rawInput);
        }

        if (command.Kind == ChatCommandKind.Provider)
        {
            return _chatController.SendMessage(this, rawInput);
        }

        return _chatController.SendMessage(this, rawInput);
    }

    public void RetryLastChatMessage()
    {
        if (string.IsNullOrWhiteSpace(ChatSession.LastUserMessage))
        {
            ChatSession.Messages.Add(new ChatMessage
            {
                Role = ChatRole.System,
                Content = "No previous user message to retry.",
            });
            ChatSession.RequestScrollToBottom = true;
            return;
        }

        _ = _chatController.SendMessage(this, ChatSession.LastUserMessage);
    }

    public bool GetFolderExpanded(DocFolderScope scope, string folderId, bool defaultExpanded = true)
    {
        if (Project.UiState.TryGetFolderExpanded(scope, folderId, out bool expanded))
        {
            return expanded;
        }

        return defaultExpanded;
    }

    public void SetFolderExpanded(DocFolderScope scope, string folderId, bool expanded)
    {
        if (!Project.UiState.SetFolderExpanded(scope, folderId, expanded))
        {
            return;
        }

        ResetAutoSaveWorkerBaseline(isDirty: true);
        MarkDirtyAndMaybeAutoSave();
    }

    /// <summary>
    /// Computes an ordered array of source row indices that pass the view's filters and sorts.
    /// Returns null if the view has no filters or sorts (raw order).
    /// </summary>
    public DocView? ResolveViewConfig(DocTable table, DocView? view, DocBlock? tableInstanceBlock = null)
    {
        if (view == null)
        {
            return null;
        }

        if (!HasViewBindings(view))
        {
            return view;
        }

        string tableInstanceBlockId = GetTableInstanceBlockCacheKey(table, tableInstanceBlock);
        var cacheEntry = GetOrCreateResolvedViewCacheEntry(table.Id, view.Id, tableInstanceBlockId);
        if (cacheEntry.Revision == _projectRevision &&
            cacheEntry.LiveValueRevision == _liveValueRevision)
        {
            return cacheEntry.ResolvedView;
        }

        var resolvedView = view.Clone();
        ResolveBoundViewConfigInPlace(table, view, resolvedView, tableInstanceBlock);
        cacheEntry.ResolvedView = resolvedView;
        cacheEntry.Revision = _projectRevision;
        cacheEntry.LiveValueRevision = _liveValueRevision;
        return resolvedView;
    }

    public int[]? ComputeViewRowIndices(DocTable table, DocView? view)
    {
        return ComputeViewRowIndices(table, view, tableInstanceBlock: null);
    }

    public int[]? ComputeViewRowIndices(DocTable table, DocView? view, DocBlock? tableInstanceBlock)
    {
        var resolvedView = ResolveViewConfig(table, view, tableInstanceBlock);
        if (resolvedView == null)
        {
            return null;
        }

        bool hasFilters = resolvedView.Filters.Count > 0;
        bool hasSorts = resolvedView.Sorts.Count > 0;
        if (!hasFilters && !hasSorts)
        {
            return null;
        }

        string tableInstanceBlockId = GetTableInstanceBlockCacheKey(table, tableInstanceBlock);
        var cacheEntry = GetOrCreateViewRowIndexCacheEntry(table.Id, resolvedView.Id, tableInstanceBlockId);
        if (cacheEntry.Revision == _projectRevision &&
            cacheEntry.LiveValueRevision == _liveValueRevision)
        {
            return cacheEntry.Indices;
        }

        int rowCount = table.Rows.Count;
        EnsureRowScratchCapacity(rowCount);

        int filterCount = resolvedView.Filters.Count;
        EnsureFilterColumnScratchCapacity(filterCount);
        for (int filterIndex = 0; filterIndex < filterCount; filterIndex++)
        {
            _filterColumnScratch[filterIndex] = FindColumnById(table, resolvedView.Filters[filterIndex].ColumnId);
        }

        int includedCount = 0;
        for (int i = 0; i < table.Rows.Count; i++)
        {
            if (hasFilters && !PassesFilters(table.Rows[i], resolvedView.Filters, filterCount))
            {
                continue;
            }

            _viewRowScratch[includedCount] = i;
            includedCount++;
        }

        if (hasSorts && includedCount > 1)
        {
            int sortCount = resolvedView.Sorts.Count;
            EnsureSortColumnScratchCapacity(sortCount);
            for (int sortIndex = 0; sortIndex < sortCount; sortIndex++)
            {
                _sortColumnScratch[sortIndex] = FindColumnById(table, resolvedView.Sorts[sortIndex].ColumnId);
            }

            _viewRowComparer.Configure(table, resolvedView.Sorts, _sortColumnScratch, sortCount);
            Array.Sort(_viewRowScratch, 0, includedCount, _viewRowComparer);
        }

        if (cacheEntry.Indices.Length != includedCount)
        {
            cacheEntry.Indices = new int[includedCount];
        }

        Array.Copy(_viewRowScratch, cacheEntry.Indices, includedCount);
        cacheEntry.Revision = _projectRevision;
        cacheEntry.LiveValueRevision = _liveValueRevision;
        return cacheEntry.Indices;
    }

    private void ResolveBoundViewConfigInPlace(
        DocTable table,
        DocView sourceView,
        DocView resolvedView,
        DocBlock? tableInstanceBlock)
    {
        if (TryResolveBindingToColumnReference(table, sourceView.GroupByColumnBinding, out string? boundGroupByColumnId, tableInstanceBlock))
        {
            resolvedView.GroupByColumnId = string.IsNullOrWhiteSpace(boundGroupByColumnId) ? null : boundGroupByColumnId;
        }

        if (TryResolveBindingToColumnReference(table, sourceView.CalendarDateColumnBinding, out string? boundCalendarDateColumnId, tableInstanceBlock))
        {
            resolvedView.CalendarDateColumnId = string.IsNullOrWhiteSpace(boundCalendarDateColumnId) ? null : boundCalendarDateColumnId;
        }

        if (TryResolveBindingToChartKind(table, sourceView.ChartKindBinding, out DocChartKind? chartKind, tableInstanceBlock))
        {
            resolvedView.ChartKind = chartKind;
        }

        if (TryResolveBindingToColumnReference(table, sourceView.ChartCategoryColumnBinding, out string? boundChartCategoryColumnId, tableInstanceBlock))
        {
            resolvedView.ChartCategoryColumnId = string.IsNullOrWhiteSpace(boundChartCategoryColumnId) ? null : boundChartCategoryColumnId;
        }

        if (TryResolveBindingToColumnReference(table, sourceView.ChartValueColumnBinding, out string? boundChartValueColumnId, tableInstanceBlock))
        {
            resolvedView.ChartValueColumnId = string.IsNullOrWhiteSpace(boundChartValueColumnId) ? null : boundChartValueColumnId;
        }

        for (int sortIndex = 0; sortIndex < sourceView.Sorts.Count && sortIndex < resolvedView.Sorts.Count; sortIndex++)
        {
            var sourceSort = sourceView.Sorts[sortIndex];
            var resolvedSort = resolvedView.Sorts[sortIndex];
            if (TryResolveBindingToColumnReference(table, sourceSort.ColumnIdBinding, out string? boundSortColumnId, tableInstanceBlock))
            {
                resolvedSort.ColumnId = boundSortColumnId ?? "";
            }

            if (TryResolveBindingToBool(table, sourceSort.DescendingBinding, out bool boundSortDescending, tableInstanceBlock))
            {
                resolvedSort.Descending = boundSortDescending;
            }
        }

        for (int filterIndex = 0; filterIndex < sourceView.Filters.Count && filterIndex < resolvedView.Filters.Count; filterIndex++)
        {
            var sourceFilter = sourceView.Filters[filterIndex];
            var resolvedFilter = resolvedView.Filters[filterIndex];
            if (TryResolveBindingToColumnReference(table, sourceFilter.ColumnIdBinding, out string? boundFilterColumnId, tableInstanceBlock))
            {
                resolvedFilter.ColumnId = boundFilterColumnId ?? "";
            }

            if (TryResolveBindingToFilterOp(table, sourceFilter.OpBinding, out DocViewFilterOp boundFilterOp, tableInstanceBlock))
            {
                resolvedFilter.Op = boundFilterOp;
            }

            if (TryResolveBindingToFilterValue(table, sourceFilter.ValueBinding, out string? boundFilterValue, tableInstanceBlock))
            {
                resolvedFilter.Value = boundFilterValue ?? "";
            }
        }
    }

    public bool TryResolveViewBindingToString(
        DocTable table,
        DocViewBinding? binding,
        out string? value,
        DocBlock? tableInstanceBlock = null)
    {
        return TryResolveBindingToString(table, binding, out value, tableInstanceBlock);
    }

    public bool TryResolveViewBindingToBool(
        DocTable table,
        DocViewBinding? binding,
        out bool value,
        DocBlock? tableInstanceBlock = null)
    {
        return TryResolveBindingToBool(table, binding, out value, tableInstanceBlock);
    }

    public bool TryResolveViewBindingToFilterOp(
        DocTable table,
        DocViewBinding? binding,
        out DocViewFilterOp value,
        DocBlock? tableInstanceBlock = null)
    {
        return TryResolveBindingToFilterOp(table, binding, out value, tableInstanceBlock);
    }

    public bool TryResolveViewBindingToChartKind(
        DocTable table,
        DocViewBinding? binding,
        out DocChartKind? value,
        DocBlock? tableInstanceBlock = null)
    {
        return TryResolveBindingToChartKind(table, binding, out value, tableInstanceBlock);
    }

    private bool TryResolveBindingToString(
        DocTable table,
        DocViewBinding? binding,
        out string? value,
        DocBlock? tableInstanceBlock)
    {
        value = null;
        if (!TryEvaluateViewBinding(table, binding, out FormulaValue result, tableInstanceBlock))
        {
            return false;
        }

        value = ConvertFormulaValueToBindingString(result);
        return true;
    }

    private bool TryResolveBindingToColumnReference(
        DocTable table,
        DocViewBinding? binding,
        out string? value,
        DocBlock? tableInstanceBlock)
    {
        value = null;
        if (!TryEvaluateViewBinding(table, binding, IsColumnReferenceVariableKind, out FormulaValue result, tableInstanceBlock))
        {
            return false;
        }

        value = ConvertFormulaValueToBindingString(result);
        return true;
    }

    private bool TryResolveBindingToFilterValue(
        DocTable table,
        DocViewBinding? binding,
        out string? value,
        DocBlock? tableInstanceBlock)
    {
        value = null;
        if (!TryEvaluateViewBinding(table, binding, IsFilterValueVariableKind, out FormulaValue result, tableInstanceBlock))
        {
            return false;
        }

        value = ConvertFormulaValueToBindingString(result);
        return true;
    }

    private bool TryResolveBindingToBool(
        DocTable table,
        DocViewBinding? binding,
        out bool value,
        DocBlock? tableInstanceBlock)
    {
        value = false;
        if (!TryEvaluateViewBinding(table, binding, IsBooleanVariableKind, out FormulaValue result, tableInstanceBlock))
        {
            return false;
        }

        if (result.Kind == FormulaValueKind.Bool)
        {
            value = result.BoolValue;
            return true;
        }

        if (result.Kind == FormulaValueKind.Number)
        {
            value = Math.Abs(result.NumberValue) > 0.0000001;
            return true;
        }

        if (result.Kind == FormulaValueKind.String)
        {
            string text = result.StringValue ?? "";
            if (bool.TryParse(text, out bool parsedBool))
            {
                value = parsedBool;
                return true;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedNumber))
            {
                value = Math.Abs(parsedNumber) > 0.0000001;
                return true;
            }
        }

        return false;
    }

    private bool TryResolveBindingToFilterOp(
        DocTable table,
        DocViewBinding? binding,
        out DocViewFilterOp value,
        DocBlock? tableInstanceBlock)
    {
        value = DocViewFilterOp.Equals;
        if (!TryEvaluateViewBinding(table, binding, IsEnumStringVariableKind, out FormulaValue result, tableInstanceBlock))
        {
            return false;
        }

        if (TryConvertFormulaValueToEnum(result, out value))
        {
            return true;
        }

        return false;
    }

    private bool TryResolveBindingToChartKind(
        DocTable table,
        DocViewBinding? binding,
        out DocChartKind? value,
        DocBlock? tableInstanceBlock)
    {
        value = null;
        if (!TryEvaluateViewBinding(table, binding, IsEnumStringVariableKind, out FormulaValue result, tableInstanceBlock))
        {
            return false;
        }

        if (TryConvertFormulaValueToEnum(result, out DocChartKind chartKind))
        {
            value = chartKind;
            return true;
        }

        if (result.Kind == FormulaValueKind.Null)
        {
            value = null;
            return true;
        }

        return false;
    }

    private bool TryEvaluateViewBinding(
        DocTable table,
        DocViewBinding? binding,
        out FormulaValue result,
        DocBlock? tableInstanceBlock)
    {
        return TryEvaluateViewBinding(table, binding, null, out result, tableInstanceBlock);
    }

    private bool TryEvaluateViewBinding(
        DocTable table,
        DocViewBinding? binding,
        Func<DocColumnKind, bool>? isVariableKindAllowed,
        out FormulaValue result,
        DocBlock? tableInstanceBlock)
    {
        result = FormulaValue.Null();
        if (binding == null || binding.IsEmpty)
        {
            return false;
        }

        string expression = "";
        if (!string.IsNullOrWhiteSpace(binding.FormulaExpression))
        {
            expression = binding.FormulaExpression;
        }
        else if (!string.IsNullOrWhiteSpace(binding.VariableName))
        {
            if (isVariableKindAllowed != null &&
                (!TryFindTableVariableByName(table, binding.VariableName, out DocTableVariable variable) ||
                 !isVariableKindAllowed(variable.Kind)))
            {
                return false;
            }

            expression = "thisTable." + binding.VariableName;
        }

        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        return TryEvaluateViewBindingExpressionWithCache(table, expression, out result, tableInstanceBlock);
    }

    private bool TryEvaluateViewBindingExpressionWithCache(
        DocTable table,
        string expression,
        out FormulaValue result,
        DocBlock? tableInstanceBlock)
    {
        result = FormulaValue.Null();
        if (string.IsNullOrWhiteSpace(table.Id) || string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        string tableInstanceBlockId = GetTableInstanceBlockCacheKey(table, tableInstanceBlock);
        var cacheEntry = GetOrCreateViewBindingEvaluationCacheEntry(table.Id, expression, tableInstanceBlockId);
        if (cacheEntry.Revision == _projectRevision &&
            cacheEntry.LiveValueRevision == _liveValueRevision)
        {
            result = cacheEntry.Result;
            return cacheEntry.Success;
        }

        bool success = _formulaEngine.TryEvaluateTableExpression(
            Project,
            table,
            expression,
            GetTableInstanceVariableOverrides(table, tableInstanceBlock),
            out result);
        cacheEntry.Revision = _projectRevision;
        cacheEntry.LiveValueRevision = _liveValueRevision;
        cacheEntry.Success = success;
        cacheEntry.Result = result;
        return success;
    }

    private static string GetTableInstanceBlockCacheKey(DocTable table, DocBlock? tableInstanceBlock)
    {
        if (tableInstanceBlock == null ||
            string.IsNullOrWhiteSpace(tableInstanceBlock.Id) ||
            !string.Equals(tableInstanceBlock.TableId, table.Id, StringComparison.Ordinal))
        {
            return "";
        }

        return tableInstanceBlock.Id;
    }

    private static IReadOnlyList<DocBlockTableVariableOverride>? GetTableInstanceVariableOverrides(
        DocTable table,
        DocBlock? tableInstanceBlock)
    {
        if (tableInstanceBlock == null ||
            !string.Equals(tableInstanceBlock.TableId, table.Id, StringComparison.Ordinal) ||
            tableInstanceBlock.TableVariableOverrides.Count <= 0)
        {
            return null;
        }

        return tableInstanceBlock.TableVariableOverrides;
    }

    private static bool TryFindTableVariableByName(DocTable table, string variableName, out DocTableVariable variable)
    {
        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            DocTableVariable candidate = table.Variables[variableIndex];
            if (string.Equals(candidate.Name, variableName, StringComparison.OrdinalIgnoreCase))
            {
                variable = candidate;
                return true;
            }
        }

        variable = null!;
        return false;
    }

    private static bool IsColumnReferenceVariableKind(DocColumnKind variableKind)
    {
        return variableKind == DocColumnKind.Text ||
               variableKind == DocColumnKind.Select ||
               variableKind == DocColumnKind.Formula;
    }

    private static bool IsFilterValueVariableKind(DocColumnKind variableKind)
    {
        return variableKind == DocColumnKind.Text ||
               variableKind == DocColumnKind.Number ||
               variableKind == DocColumnKind.Checkbox ||
               variableKind == DocColumnKind.Select ||
               variableKind == DocColumnKind.Formula;
    }

    private static bool IsBooleanVariableKind(DocColumnKind variableKind)
    {
        return variableKind == DocColumnKind.Checkbox ||
               variableKind == DocColumnKind.Formula;
    }

    private static bool IsEnumStringVariableKind(DocColumnKind variableKind)
    {
        return variableKind == DocColumnKind.Text ||
               variableKind == DocColumnKind.Select ||
               variableKind == DocColumnKind.Formula;
    }

    private static bool TryConvertFormulaValueToEnum<TEnum>(FormulaValue value, out TEnum parsedEnum)
        where TEnum : struct
    {
        if (value.Kind == FormulaValueKind.String &&
            Enum.TryParse<TEnum>(value.StringValue, ignoreCase: true, out parsedEnum))
        {
            return true;
        }

        if (value.Kind == FormulaValueKind.Number)
        {
            int candidateValue = (int)Math.Round(value.NumberValue);
            if (Enum.IsDefined(typeof(TEnum), candidateValue))
            {
                parsedEnum = (TEnum)Enum.ToObject(typeof(TEnum), candidateValue);
                return true;
            }
        }

        parsedEnum = default;
        return false;
    }

    private static string ConvertFormulaValueToBindingString(FormulaValue value)
    {
        return value.Kind switch
        {
            FormulaValueKind.String => value.StringValue ?? "",
            FormulaValueKind.Number => value.NumberValue.ToString("G", CultureInfo.InvariantCulture),
            FormulaValueKind.Bool => value.BoolValue ? "true" : "false",
            FormulaValueKind.Vec2 => "(" +
                                     value.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                                     value.YValue.ToString("G", CultureInfo.InvariantCulture) + ")",
            FormulaValueKind.Vec3 => "(" +
                                     value.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                                     value.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                                     value.ZValue.ToString("G", CultureInfo.InvariantCulture) + ")",
            FormulaValueKind.Vec4 => "(" +
                                     value.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                                     value.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                                     value.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                                     value.WValue.ToString("G", CultureInfo.InvariantCulture) + ")",
            FormulaValueKind.Color => "rgba(" +
                                      value.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                                      value.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                                      value.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                                      value.WValue.ToString("G", CultureInfo.InvariantCulture) + ")",
            FormulaValueKind.RowReference => value.RowValue?.Id ?? "",
            FormulaValueKind.TableReference => value.TableValue?.Id ?? "",
            FormulaValueKind.DocumentReference => value.DocumentValue?.Id ?? "",
            FormulaValueKind.DateTime => value.DateTimeValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => "",
        };
    }

    private static bool HasViewBindings(DocView view)
    {
        if (!IsBindingEmpty(view.GroupByColumnBinding) ||
            !IsBindingEmpty(view.CalendarDateColumnBinding) ||
            !IsBindingEmpty(view.ChartKindBinding) ||
            !IsBindingEmpty(view.ChartCategoryColumnBinding) ||
            !IsBindingEmpty(view.ChartValueColumnBinding))
        {
            return true;
        }

        for (int sortIndex = 0; sortIndex < view.Sorts.Count; sortIndex++)
        {
            var sort = view.Sorts[sortIndex];
            if (!IsBindingEmpty(sort.ColumnIdBinding) ||
                !IsBindingEmpty(sort.DescendingBinding))
            {
                return true;
            }
        }

        for (int filterIndex = 0; filterIndex < view.Filters.Count; filterIndex++)
        {
            var filter = view.Filters[filterIndex];
            if (!IsBindingEmpty(filter.ColumnIdBinding) ||
                !IsBindingEmpty(filter.OpBinding) ||
                !IsBindingEmpty(filter.ValueBinding))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBindingEmpty(DocViewBinding? binding)
    {
        return binding == null || binding.IsEmpty;
    }

    private bool PassesFilters(DocRow row, List<DocViewFilter> filters, int filterCount)
    {
        for (int i = 0; i < filterCount; i++)
        {
            var column = _filterColumnScratch[i];
            if (column == null)
            {
                continue;
            }

            var filter = filters[i];
            var cell = row.GetCell(column);
            string cellStr = GetCellStringForFilter(column, cell);

            bool pass = filter.Op switch
            {
                DocViewFilterOp.Equals => string.Equals(cellStr, filter.Value, StringComparison.OrdinalIgnoreCase),
                DocViewFilterOp.NotEquals => !string.Equals(cellStr, filter.Value, StringComparison.OrdinalIgnoreCase),
                DocViewFilterOp.Contains => cellStr.Contains(filter.Value, StringComparison.OrdinalIgnoreCase),
                DocViewFilterOp.NotContains => !cellStr.Contains(filter.Value, StringComparison.OrdinalIgnoreCase),
                DocViewFilterOp.GreaterThan => CompareValues(cellStr, filter.Value) > 0,
                DocViewFilterOp.LessThan => CompareValues(cellStr, filter.Value) < 0,
                DocViewFilterOp.IsEmpty => string.IsNullOrWhiteSpace(cellStr),
                DocViewFilterOp.IsNotEmpty => !string.IsNullOrWhiteSpace(cellStr),
                _ => true,
            };

            if (!pass)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class ViewRowIndexCacheEntry
    {
        public string TableId = "";
        public string ViewId = "";
        public string TableInstanceBlockId = "";
        public int Revision = -1;
        public int LiveValueRevision = -1;
        public int[] Indices = Array.Empty<int>();
    }

    private sealed class ResolvedViewCacheEntry
    {
        public string TableId = "";
        public string ViewId = "";
        public string TableInstanceBlockId = "";
        public int Revision = -1;
        public int LiveValueRevision = -1;
        public DocView? ResolvedView;
    }

    private sealed class ViewBindingEvaluationCacheEntry
    {
        public string TableId = "";
        public string Expression = "";
        public string TableInstanceBlockId = "";
        public int Revision = -1;
        public int LiveValueRevision = -1;
        public bool Success;
        public FormulaValue Result;
    }

    private sealed class ViewRowComparer : IComparer<int>
    {
        private DocTable? _table;
        private List<DocViewSort>? _sorts;
        private DocColumn?[]? _sortColumns;
        private int _sortCount;

        public void Configure(DocTable table, List<DocViewSort> sorts, DocColumn?[] sortColumns, int sortCount)
        {
            _table = table;
            _sorts = sorts;
            _sortColumns = sortColumns;
            _sortCount = sortCount;
        }

        public int Compare(int a, int b)
        {
            var table = _table;
            var sorts = _sorts;
            var sortColumns = _sortColumns;
            if (table == null || sorts == null || sortColumns == null)
            {
                return a.CompareTo(b);
            }

            var rowA = table.Rows[a];
            var rowB = table.Rows[b];
            for (int i = 0; i < _sortCount; i++)
            {
                var column = sortColumns[i];
                if (column == null)
                {
                    continue;
                }

                var sort = sorts[i];
                var cellA = rowA.GetCell(column);
                var cellB = rowB.GetCell(column);
                int cmp;

                if (column.Kind == DocColumnKind.Number)
                {
                    cmp = cellA.NumberValue.CompareTo(cellB.NumberValue);
                }
                else if (column.Kind == DocColumnKind.Checkbox)
                {
                    cmp = cellA.BoolValue.CompareTo(cellB.BoolValue);
                }
                else
                {
                    cmp = string.Compare(cellA.StringValue ?? "", cellB.StringValue ?? "", StringComparison.OrdinalIgnoreCase);
                }

                if (sort.Descending)
                {
                    cmp = -cmp;
                }

                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return a.CompareTo(b);
        }
    }

    private static string GetCellStringForFilter(DocColumn column, DocCellValue cell)
    {
        if (column.Kind == DocColumnKind.Number)
            return cell.NumberValue.ToString("G");
        if (column.Kind == DocColumnKind.Checkbox)
            return cell.BoolValue ? "true" : "false";
        if (column.Kind == DocColumnKind.Vec2)
            return "(" + cell.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cell.YValue.ToString("G", CultureInfo.InvariantCulture) + ")";
        if (column.Kind == DocColumnKind.Vec3)
            return "(" + cell.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cell.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cell.ZValue.ToString("G", CultureInfo.InvariantCulture) + ")";
        if (column.Kind == DocColumnKind.Vec4)
            return "(" + cell.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cell.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cell.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cell.WValue.ToString("G", CultureInfo.InvariantCulture) + ")";
        if (column.Kind == DocColumnKind.Color)
            return "rgba(" + cell.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cell.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cell.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cell.WValue.ToString("G", CultureInfo.InvariantCulture) + ")";
        return cell.StringValue ?? "";
    }

    private static int CompareValues(string a, string b)
    {
        if (double.TryParse(a, out double numA) && double.TryParse(b, out double numB))
            return numA.CompareTo(numB);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private ViewRowIndexCacheEntry GetOrCreateViewRowIndexCacheEntry(
        string tableId,
        string viewId,
        string tableInstanceBlockId)
    {
        for (int i = 0; i < _viewRowIndexCacheEntries.Count; i++)
        {
            var entry = _viewRowIndexCacheEntries[i];
            if (string.Equals(entry.TableId, tableId, StringComparison.Ordinal) &&
                string.Equals(entry.ViewId, viewId, StringComparison.Ordinal) &&
                string.Equals(entry.TableInstanceBlockId, tableInstanceBlockId, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        var created = new ViewRowIndexCacheEntry
        {
            TableId = tableId,
            ViewId = viewId,
            TableInstanceBlockId = tableInstanceBlockId,
        };
        _viewRowIndexCacheEntries.Add(created);
        return created;
    }

    private ResolvedViewCacheEntry GetOrCreateResolvedViewCacheEntry(
        string tableId,
        string viewId,
        string tableInstanceBlockId)
    {
        for (int cacheIndex = 0; cacheIndex < _resolvedViewCacheEntries.Count; cacheIndex++)
        {
            var cacheEntry = _resolvedViewCacheEntries[cacheIndex];
            if (string.Equals(cacheEntry.TableId, tableId, StringComparison.Ordinal) &&
                string.Equals(cacheEntry.ViewId, viewId, StringComparison.Ordinal) &&
                string.Equals(cacheEntry.TableInstanceBlockId, tableInstanceBlockId, StringComparison.Ordinal))
            {
                return cacheEntry;
            }
        }

        var createdCacheEntry = new ResolvedViewCacheEntry
        {
            TableId = tableId,
            ViewId = viewId,
            TableInstanceBlockId = tableInstanceBlockId,
        };
        _resolvedViewCacheEntries.Add(createdCacheEntry);
        return createdCacheEntry;
    }

    private ViewBindingEvaluationCacheEntry GetOrCreateViewBindingEvaluationCacheEntry(
        string tableId,
        string expression,
        string tableInstanceBlockId)
    {
        for (int cacheIndex = 0; cacheIndex < _viewBindingEvaluationCacheEntries.Count; cacheIndex++)
        {
            var cacheEntry = _viewBindingEvaluationCacheEntries[cacheIndex];
            if (string.Equals(cacheEntry.TableId, tableId, StringComparison.Ordinal) &&
                string.Equals(cacheEntry.Expression, expression, StringComparison.Ordinal) &&
                string.Equals(cacheEntry.TableInstanceBlockId, tableInstanceBlockId, StringComparison.Ordinal))
            {
                return cacheEntry;
            }
        }

        var createdCacheEntry = new ViewBindingEvaluationCacheEntry
        {
            TableId = tableId,
            Expression = expression,
            TableInstanceBlockId = tableInstanceBlockId,
        };
        _viewBindingEvaluationCacheEntries.Add(createdCacheEntry);
        return createdCacheEntry;
    }

    private static DocColumn? FindColumnById(DocTable table, string columnId)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (string.Equals(column.Id, columnId, StringComparison.Ordinal))
            {
                return column;
            }
        }

        return null;
    }

    private void EnsureRowScratchCapacity(int requiredCapacity)
    {
        if (_viewRowScratch.Length >= requiredCapacity)
        {
            return;
        }

        int newSize = Math.Max(requiredCapacity, (_viewRowScratch.Length * 2) + 8);
        _viewRowScratch = new int[newSize];
    }

    private void EnsureFilterColumnScratchCapacity(int requiredCapacity)
    {
        if (_filterColumnScratch.Length >= requiredCapacity)
        {
            return;
        }

        int newSize = Math.Max(requiredCapacity, (_filterColumnScratch.Length * 2) + 4);
        _filterColumnScratch = new DocColumn?[newSize];
    }

    private void EnsureSortColumnScratchCapacity(int requiredCapacity)
    {
        if (_sortColumnScratch.Length >= requiredCapacity)
        {
            return;
        }

        int newSize = Math.Max(requiredCapacity, (_sortColumnScratch.Length * 2) + 4);
        _sortColumnScratch = new DocColumn?[newSize];
    }

    public void SaveProject(string path)
    {
        CancelPendingDebouncedFormulaRefresh();
        _autoSavePending = false;
        _autoSaveDueTicks = 0;
        FlushAutoSave();
        ProjectPath = path;
        ProjectSerializer.Save(Project, path);
        ResetAutoSaveWorkerBaseline(isDirty: false);
        IsDirty = false;
        LastStatusMessage = "Saved.";
        TrackRecentProjectPath(ProjectPath);
        PersistActiveProjectState();
        RequestLiveExport(immediate: true);
    }

    public void LoadProject(string path)
    {
        CancelPendingDebouncedFormulaRefresh();
        FlushAutoSave();
        ProjectPath = path;
        _pluginHost.ReloadForProject(path);
        Project = ProjectLoader.Load(path);
        _selectedVariantIdByTableId.Clear();
        _selectedVariantCleanupKeysScratch.Clear();
        _variantTableSnapshotCacheByKey.Clear();
        _autoSavePending = false;
        _autoSaveDueTicks = 0;
        RecalculateComputedColumns();
        BumpProjectRevision();
        ActiveTable = Project.Tables.Count > 0 ? Project.Tables[0] : null;
        ActiveTableView = ActiveTable?.Views.Count > 0 ? ActiveTable.Views[0] : null;
        ActiveDocument = null;
        ActiveView = ActiveViewKind.Table;
        SelectedRowIndex = -1;
        FocusedBlockIndex = -1;
        IsDirty = false;
        LastStatusMessage = "Opened.";
        TrackRecentProjectPath(ProjectPath);
        GameRoot = null;
        if (DocProjectPaths.TryGetGameRootFromDbRoot(ProjectPath, out var gameRoot))
        {
            GameRoot = gameRoot;
        }
        ContentTabs.ResetForLoadedProject();

        _externalChangePollFrames = 0;
        if (DocExternalChangeSignalFile.TryGetLastWriteTimeUtc(ProjectPath, out var lastWriteUtc))
        {
            _externalChangeLastWriteUtc = lastWriteUtc;
        }

        ResetAutoSaveWorkerBaseline(isDirty: false);
        RequestLiveExport(immediate: true);
    }

    public void Shutdown()
    {
        PersistActiveProjectState();
        StopChatProvider();
        _pluginHost.UnloadAll();
        FlushAutoSave();
        if (_autoSaveWorker != null)
        {
            _autoSaveWorker.Shutdown();
            _autoSaveWorker = null;
        }
    }

    public void PersistActiveProjectState()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            return;
        }

        ContentTabs.CaptureWorkspaceStateIntoTabIfActive();

        var state = new DocActiveProjectStateFile.ActiveProjectState
        {
            DbRoot = ProjectPath,
            GameRoot = GameRoot,
            ProjectName = Project.Name,
            NextContentTabWindowId = ContentTabs.GetNextWindowIdForPersist(),
            ActiveContentTabInstanceId = ContentTabs.ActiveTab?.TabInstanceId ?? "",
        };

        for (int tabIndex = 0; tabIndex < ContentTabs.TabCount; tabIndex++)
        {
            var tab = ContentTabs.GetTabAt(tabIndex);
            var entry = new DocActiveProjectStateFile.ContentTabState
            {
                TabInstanceId = tab.TabInstanceId,
                WindowId = tab.WindowId,
                Kind = tab.Kind == DocContentTabs.TabKind.Document ? "document" : "table",
                TargetId = tab.TargetId,
                ActiveViewId = tab.ActiveViewId,
                ParentRowId = tab.ParentRowId,
                FocusedBlockIndex = tab.FocusedBlockIndex,
            };
            state.OpenContentTabs.Add(entry);
        }

        DocActiveProjectStateFile.WriteState(WorkspaceRoot, state);
    }

    public void PollExternalChanges()
    {
        PollChatEvents();
        ProcessPendingDebouncedFormulaRefresh();
        ProcessPendingAutoSave();
        ProcessPendingLiveExport();

        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            return;
        }

        if (IsDirty || EditState.IsEditing || FocusedBlockIndex >= 0)
        {
            return;
        }

        _externalChangePollFrames++;
        if (_externalChangePollFrames < 15)
        {
            return;
        }
        _externalChangePollFrames = 0;

        if (!DocExternalChangeSignalFile.TryGetLastWriteTimeUtc(ProjectPath, out var lastWriteUtc))
        {
            return;
        }

        if (_externalChangeLastWriteUtc != default && lastWriteUtc <= _externalChangeLastWriteUtc)
        {
            return;
        }

        _externalChangeLastWriteUtc = lastWriteUtc;
        ReloadProjectFromDiskPreserveSelection();
    }

    private void PollChatEvents()
    {
        _chatController.Poll(this);
    }

    private void ReloadProjectFromDiskPreserveSelection()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            return;
        }

        WorkspaceSelectionState selectionState = CaptureWorkspaceSelectionState();
        DocProject previousProjectSnapshot = CloneProjectSnapshot(Project);
        _pluginHost.ReloadForProject(ProjectPath);
        DocProject loadedProject = ProjectLoader.Load(ProjectPath);
        Project = loadedProject;
        _selectedVariantIdByTableId.Clear();
        _selectedVariantCleanupKeysScratch.Clear();
        _variantTableSnapshotCacheByKey.Clear();

        UndoStack.Execute(
            new DocCommand
            {
                Kind = DocCommandKind.ReplaceProjectSnapshot,
                OldProjectSnapshot = previousProjectSnapshot,
                NewProjectSnapshot = CloneProjectSnapshot(loadedProject),
            },
            Project);

        RecalculateComputedColumns();
        GameRoot = null;
        if (DocProjectPaths.TryGetGameRootFromDbRoot(ProjectPath, out var gameRoot))
        {
            GameRoot = gameRoot;
        }

        RestoreWorkspaceSelectionState(selectionState);

        FocusedBlockIndex = -1;
        FocusedBlockTextSnapshot = null;
        CancelTableCellEditIfActive();
        IsDirty = false;
        BumpProjectRevision();
        ResetAutoSaveWorkerBaseline(isDirty: false);
        LastStatusMessage = "Reloaded (external changes).";
    }

    private readonly struct WorkspaceSelectionState
    {
        public readonly string ActiveTableId;
        public readonly string ActiveTableViewId;
        public readonly string ActiveDocumentId;
        public readonly string SelectedRowId;
        public readonly ActiveViewKind ActiveView;

        public WorkspaceSelectionState(
            string activeTableId,
            string activeTableViewId,
            string activeDocumentId,
            string selectedRowId,
            ActiveViewKind activeView)
        {
            ActiveTableId = activeTableId;
            ActiveTableViewId = activeTableViewId;
            ActiveDocumentId = activeDocumentId;
            SelectedRowId = selectedRowId;
            ActiveView = activeView;
        }
    }

    private WorkspaceSelectionState CaptureWorkspaceSelectionState()
    {
        string selectedRowId = "";
        if (ActiveTable != null && SelectedRowIndex >= 0 && SelectedRowIndex < ActiveTable.Rows.Count)
        {
            selectedRowId = ActiveTable.Rows[SelectedRowIndex].Id;
        }

        return new WorkspaceSelectionState(
            activeTableId: ActiveTable?.Id ?? "",
            activeTableViewId: ActiveTableView?.Id ?? "",
            activeDocumentId: ActiveDocument?.Id ?? "",
            selectedRowId: selectedRowId,
            activeView: ActiveView);
    }

    private void RestoreWorkspaceSelectionState(WorkspaceSelectionState selectionState)
    {
        ActiveTable = null;
        if (!string.IsNullOrWhiteSpace(selectionState.ActiveTableId))
        {
            for (int tableIndex = 0; tableIndex < Project.Tables.Count; tableIndex++)
            {
                DocTable table = Project.Tables[tableIndex];
                if (string.Equals(table.Id, selectionState.ActiveTableId, StringComparison.Ordinal))
                {
                    ActiveTable = table;
                    break;
                }
            }
        }

        ActiveTable ??= Project.Tables.Count > 0 ? Project.Tables[0] : null;

        ActiveTableView = null;
        if (ActiveTable != null && !string.IsNullOrWhiteSpace(selectionState.ActiveTableViewId))
        {
            for (int viewIndex = 0; viewIndex < ActiveTable.Views.Count; viewIndex++)
            {
                DocView view = ActiveTable.Views[viewIndex];
                if (string.Equals(view.Id, selectionState.ActiveTableViewId, StringComparison.Ordinal))
                {
                    ActiveTableView = view;
                    break;
                }
            }
        }

        if (ActiveTableView == null && ActiveTable != null)
        {
            ActiveTableView = ActiveTable.Views.Count > 0 ? ActiveTable.Views[0] : null;
        }

        ActiveDocument = null;
        if (!string.IsNullOrWhiteSpace(selectionState.ActiveDocumentId))
        {
            for (int documentIndex = 0; documentIndex < Project.Documents.Count; documentIndex++)
            {
                DocDocument document = Project.Documents[documentIndex];
                if (string.Equals(document.Id, selectionState.ActiveDocumentId, StringComparison.Ordinal))
                {
                    ActiveDocument = document;
                    break;
                }
            }
        }

        ActiveView = selectionState.ActiveView;
        if (ActiveView == ActiveViewKind.Document && ActiveDocument == null)
        {
            ActiveView = ActiveViewKind.Table;
        }

        if (ActiveView == ActiveViewKind.Table && ActiveTable == null && ActiveDocument != null)
        {
            ActiveView = ActiveViewKind.Document;
        }

        SelectedRowIndex = -1;
        if (ActiveTable != null && !string.IsNullOrWhiteSpace(selectionState.SelectedRowId))
        {
            for (int rowIndex = 0; rowIndex < ActiveTable.Rows.Count; rowIndex++)
            {
                DocRow row = ActiveTable.Rows[rowIndex];
                if (string.Equals(row.Id, selectionState.SelectedRowId, StringComparison.Ordinal))
                {
                    SelectedRowIndex = rowIndex;
                    break;
                }
            }
        }
    }

    private bool TryExecuteLocalChatCommand(ChatCommand command, string rawInput)
    {
        if (string.Equals(command.Name, "/help", StringComparison.OrdinalIgnoreCase))
        {
            AppendSystemChatMessage(
                "Chat commands: /help, /provider <claude|codex>, /agent <mcp|workspace>, /model <name>, /mcp, /clear, /retry.\n" +
                "Use Ctrl+Enter to send messages.");
            return true;
        }

        if (string.Equals(command.Name, "/provider", StringComparison.OrdinalIgnoreCase))
        {
            return TryExecuteProviderCommand(command.Argument);
        }

        if (string.Equals(command.Name, "/model", StringComparison.OrdinalIgnoreCase))
        {
            return TryExecuteModelCommand(command.Argument);
        }

        if (string.Equals(command.Name, "/agent", StringComparison.OrdinalIgnoreCase))
        {
            return TryExecuteAgentCommand(command.Argument);
        }

        if (string.Equals(command.Name, "/mcp", StringComparison.OrdinalIgnoreCase))
        {
            AppendMcpStatusMessage();
            return true;
        }

        if (string.Equals(command.Name, "/clear", StringComparison.OrdinalIgnoreCase))
        {
            ChatSession.ClearMessages();
            return true;
        }

        if (string.Equals(command.Name, "/retry", StringComparison.OrdinalIgnoreCase))
        {
            RetryLastChatMessage();
            return true;
        }

        return _chatController.SendMessage(this, rawInput);
    }

    private bool TryExecuteProviderCommand(string commandArgument)
    {
        if (string.IsNullOrWhiteSpace(commandArgument))
        {
            AppendSystemChatMessage("Current provider: " + ChatSession.ProviderKind + ". Available: claude, codex.");
            return true;
        }

        if (TryParseChatProviderKind(commandArgument, out ChatProviderKind providerKind))
        {
            SwitchChatProvider(providerKind);
            return true;
        }

        AppendSystemChatMessage("Unknown provider: " + commandArgument, isError: true);
        return false;
    }

    private bool TryExecuteModelCommand(string commandArgument)
    {
        if (string.IsNullOrWhiteSpace(commandArgument))
        {
            string modelOptions = BuildCommaSeparatedList(ChatSession.ModelOptions);
            if (string.IsNullOrWhiteSpace(modelOptions))
            {
                AppendSystemChatMessage("Current model: " + ChatSession.CurrentModel);
            }
            else
            {
                AppendSystemChatMessage("Current model: " + ChatSession.CurrentModel + ". Options: " + modelOptions);
            }

            return true;
        }

        if (TrySetChatModel(commandArgument, out string errorMessage))
        {
            return true;
        }

        AppendSystemChatMessage(errorMessage, isError: true);
        return false;
    }

    private bool TryExecuteAgentCommand(string commandArgument)
    {
        if (string.IsNullOrWhiteSpace(commandArgument))
        {
            AppendSystemChatMessage("Current agent type: " + DescribeChatAgentType(ChatSession.AgentType) + ". Available: mcp, workspace.");
            return true;
        }

        if (TryParseChatAgentType(commandArgument, out ChatAgentType agentType))
        {
            SwitchChatAgentType(agentType);
            return true;
        }

        AppendSystemChatMessage("Unknown agent type: " + commandArgument, isError: true);
        return false;
    }

    private void AppendMcpStatusMessage()
    {
        string servers = BuildCommaSeparatedList(ChatSession.McpServers);
        if (string.IsNullOrWhiteSpace(servers))
        {
            servers = "(none reported)";
        }

        AppendSystemChatMessage(
            "Provider: " + ChatSession.ProviderKind +
            ". Agent: " + DescribeChatAgentType(ChatSession.AgentType) +
            ". MCP-only: " + (ChatSession.IsMcpOnly ? "yes" : "no") +
            ". Servers: " + servers +
            ". Tools: " + ChatSession.AvailableTools.Count + ".");
    }

    private void AppendSystemChatMessage(string message, bool isError = false)
    {
        ChatSession.Messages.Add(new ChatMessage
        {
            Role = isError ? ChatRole.Error : ChatRole.System,
            Content = message,
            IsError = isError,
        });
        ChatSession.RequestScrollToBottom = true;
    }

    private static bool TryParseChatProviderKind(string rawProviderName, out ChatProviderKind providerKind)
    {
        providerKind = ChatProviderKind.Claude;
        if (string.IsNullOrWhiteSpace(rawProviderName))
        {
            return false;
        }

        if (string.Equals(rawProviderName, "claude", StringComparison.OrdinalIgnoreCase))
        {
            providerKind = ChatProviderKind.Claude;
            return true;
        }

        if (string.Equals(rawProviderName, "codex", StringComparison.OrdinalIgnoreCase))
        {
            providerKind = ChatProviderKind.Codex;
            return true;
        }

        return false;
    }

    private static bool TryParseChatAgentType(string rawAgentType, out ChatAgentType agentType)
    {
        agentType = ChatAgentType.Mcp;
        if (string.IsNullOrWhiteSpace(rawAgentType))
        {
            return false;
        }

        if (string.Equals(rawAgentType, "mcp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawAgentType, "mcp-only", StringComparison.OrdinalIgnoreCase))
        {
            agentType = ChatAgentType.Mcp;
            return true;
        }

        if (string.Equals(rawAgentType, "workspace", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawAgentType, "workspace-agent", StringComparison.OrdinalIgnoreCase))
        {
            agentType = ChatAgentType.Workspace;
            return true;
        }

        return false;
    }

    private static string DescribeChatAgentType(ChatAgentType agentType)
    {
        return agentType == ChatAgentType.Workspace ? "Workspace Agent" : "MCP Agent";
    }

    private static string BuildCommaSeparatedList(List<string> values)
    {
        if (values.Count == 0)
        {
            return "";
        }

        var builder = new System.Text.StringBuilder(values.Count * 24);
        for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
        {
            if (valueIndex > 0)
            {
                builder.Append(", ");
            }

            builder.Append(values[valueIndex]);
        }

        return builder.ToString();
    }

    private void BumpProjectRevision()
    {
        _formulaContext = null;
        _formulaContextRevision = -1;
        _scopeTargetFormulaColumnsRevision = -1;
        _scopeTargetFormulaColumnsCachedScope = DocFormulaEvalScope.None;
        _scopeTargetFormulaColumnsProjectReference = null;
        _nonInteractiveFormulaCoverageRevision = -1;
        _nonInteractiveFormulaCoverageProjectReference = null;
        _hasNonInteractiveFormulaColumns = false;
        _variantTableSnapshotCacheByKey.Clear();

        if (_projectRevision == int.MaxValue)
        {
            _projectRevision = 1;
            _liveValueRevision = 1;
            for (int cacheIndex = 0; cacheIndex < _viewRowIndexCacheEntries.Count; cacheIndex++)
            {
                _viewRowIndexCacheEntries[cacheIndex].Revision = -1;
                _viewRowIndexCacheEntries[cacheIndex].LiveValueRevision = -1;
            }
            for (int cacheIndex = 0; cacheIndex < _resolvedViewCacheEntries.Count; cacheIndex++)
            {
                _resolvedViewCacheEntries[cacheIndex].Revision = -1;
                _resolvedViewCacheEntries[cacheIndex].LiveValueRevision = -1;
            }
            _viewBindingEvaluationCacheEntries.Clear();
            return;
        }

        _projectRevision++;
        BumpLiveValueRevision();
    }

    private void BumpLiveValueRevision()
    {
        if (_liveValueRevision == int.MaxValue)
        {
            _liveValueRevision = 1;
            for (int cacheIndex = 0; cacheIndex < _viewRowIndexCacheEntries.Count; cacheIndex++)
            {
                _viewRowIndexCacheEntries[cacheIndex].LiveValueRevision = -1;
            }
            for (int cacheIndex = 0; cacheIndex < _resolvedViewCacheEntries.Count; cacheIndex++)
            {
                _resolvedViewCacheEntries[cacheIndex].LiveValueRevision = -1;
            }
            _viewBindingEvaluationCacheEntries.Clear();
            return;
        }

        _liveValueRevision++;
    }

    private ProjectFormulaContext GetProjectFormulaContext()
    {
        if (_formulaContext != null && _formulaContextRevision == _projectRevision)
        {
            return _formulaContext;
        }

        _formulaContext = new ProjectFormulaContext(Project);
        _formulaContextRevision = _projectRevision;
        return _formulaContext;
    }

    public bool TryOpenFromPath(string path, bool createIfMissing, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is required.";
            return false;
        }

        try
        {
            string dbRoot = DocProjectPaths.ResolveDbRootFromPath(path, allowCreate: createIfMissing, out var gameRoot);
            if (createIfMissing)
            {
                string projectName = !string.IsNullOrWhiteSpace(gameRoot) ? new DirectoryInfo(gameRoot).Name : new DirectoryInfo(dbRoot).Name;
                DocProjectScaffolder.EnsureDbRoot(dbRoot, projectName);
            }

            GameRoot = gameRoot;
            LoadProject(dbRoot);
            ContentTabs.EnsureDefaultTabOpen();
            PersistActiveProjectState();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryCreateNewGameProject(string gameRoot, string projectName, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            error = "Game root path is required.";
            return false;
        }

        try
        {
            string fullGameRoot = DocProjectScaffolder.EnsureGameRoot(gameRoot, projectName);
            GameRoot = fullGameRoot;
            string dbRoot = Path.Combine(fullGameRoot, DocProjectPaths.DatabaseDirectoryName);
            LoadProject(dbRoot);
            ContentTabs.EnsureDefaultTabOpen();
            PersistActiveProjectState();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool CanStartGame()
    {
        return TryResolveStartGameWorkingDirectory(out _, out _);
    }

    public bool TryStartGame(out string error)
    {
        if (!TryResolveStartGameWorkingDirectory(out string workingDirectory, out error))
        {
            LastStatusMessage = error;
            return false;
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        startInfo.ArgumentList.Add("run");

        try
        {
            Process? process = Process.Start(startInfo);
            if (process == null)
            {
                error = "Failed to start game process.";
                LastStatusMessage = error;
                return false;
            }

            process.Dispose();
            LastStatusMessage = "Started game from " + workingDirectory + ".";
            return true;
        }
        catch (Exception ex)
        {
            error = "Failed to start game: " + ex.Message;
            LastStatusMessage = error;
            return false;
        }
    }

    private bool TryResolveStartGameWorkingDirectory(out string workingDirectory, out string error)
    {
        workingDirectory = "";
        error = "";

        string? candidateDirectory = GameRoot;
        if (string.IsNullOrWhiteSpace(candidateDirectory) && !string.IsNullOrWhiteSpace(ProjectPath))
        {
            if (DocProjectPaths.TryGetGameRootFromDbRoot(ProjectPath, out string inferredGameRoot))
            {
                candidateDirectory = inferredGameRoot;
                GameRoot = inferredGameRoot;
            }
            else
            {
                candidateDirectory = ProjectPath;
            }
        }

        if (string.IsNullOrWhiteSpace(candidateDirectory))
        {
            error = "No active project path. Open a project first.";
            return false;
        }

        string fullDirectory = Path.GetFullPath(candidateDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            error = "Project directory does not exist: " + fullDirectory;
            return false;
        }

        workingDirectory = fullDirectory;
        return true;
    }

    public bool TryExportActiveProject(bool writeManifest, out ExportPipelineResult? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            LastStatusMessage = "No project path set. Open a game/db first.";
            return false;
        }

        string dbRoot = ProjectPath;
        string? gameRoot = GameRoot;
        if (string.IsNullOrWhiteSpace(gameRoot) && DocProjectPaths.TryGetGameRootFromDbRoot(dbRoot, out var gr))
        {
            gameRoot = gr;
            GameRoot = gr;
        }

        string binPath = DocProjectPaths.ResolveDefaultBinaryPath(dbRoot, gameRoot);

        var options = new ExportPipelineOptions
        {
            BinaryOutputPath = binPath,
            LiveBinaryOutputPath = DocProjectPaths.ResolveDefaultLiveBinaryPath(dbRoot),
            GeneratedOutputDirectory = "",
            WriteManifest = writeManifest,
        };

        var pipeline = new DocExportPipeline();
        var exportResult = pipeline.Export(Project, options);
        result = exportResult;

        if (exportResult.HasErrors)
        {
            LastStatusMessage = "Export failed.";
            return false;
        }

        LastStatusMessage = "Exported.";
        return true;
    }

    public string ResolveRelationDisplayLabel(DocColumn relationColumn, string relationRowId)
    {
        if (string.IsNullOrEmpty(relationRowId))
        {
            return "";
        }

        if (string.IsNullOrEmpty(relationColumn.RelationTableId))
        {
            return relationRowId;
        }

        DocTable? relationTable = FindTableById(relationColumn.RelationTableId);
        if (relationTable == null)
        {
            return relationRowId;
        }

        int variantId = relationColumn.RelationTableVariantId;
        if (!IsKnownTableVariantId(relationTable, variantId))
        {
            variantId = DocTableVariant.BaseVariantId;
        }

        DocRow? relationRow = null;
        if (!TryResolveRelationRow(relationTable, variantId, relationRowId, out relationRow))
        {
            return relationRowId;
        }

        if (TryResolveRelationDisplayColumn(relationColumn, relationTable, out var displayColumn))
        {
            string explicitDisplayValue = GetCellDisplayText(displayColumn, relationRow.GetCell(displayColumn));
            if (!string.IsNullOrWhiteSpace(explicitDisplayValue))
            {
                return explicitDisplayValue;
            }
        }

        if (variantId == DocTableVariant.BaseVariantId)
        {
            var formulaContext = GetProjectFormulaContext();
            return formulaContext.GetRowDisplayLabel(relationTable, relationRow);
        }

        return ResolveRowDisplayLabel(relationTable, relationRow);
    }

    private bool TryResolveRelationRow(DocTable relationTable, int variantId, string relationRowId, out DocRow row)
    {
        row = null!;
        if (variantId == DocTableVariant.BaseVariantId)
        {
            var formulaContext = GetProjectFormulaContext();
            return formulaContext.TryGetRowById(relationTable, relationRowId, out row);
        }

        _ = ResolveTableForVariant(relationTable, variantId);
        var cacheKey = new VariantTableCacheKey(relationTable.Id, variantId);
        if (_variantTableSnapshotCacheByKey.TryGetValue(cacheKey, out VariantTableSnapshotCacheEntry? cacheEntry) &&
            cacheEntry.RowById != null &&
            cacheEntry.RowById.TryGetValue(relationRowId, out row!))
        {
            return true;
        }

        DocTable variantTable = ResolveTableForVariant(relationTable, variantId);
        for (int rowIndex = 0; rowIndex < variantTable.Rows.Count; rowIndex++)
        {
            DocRow candidateRow = variantTable.Rows[rowIndex];
            if (string.Equals(candidateRow.Id, relationRowId, StringComparison.Ordinal))
            {
                row = candidateRow;
                return true;
            }
        }

        return false;
    }

    private static string ResolveRowDisplayLabel(DocTable table, DocRow row)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (column.Kind != DocColumnKind.Text &&
                column.Kind != DocColumnKind.Select &&
                column.Kind != DocColumnKind.TextureAsset &&
                column.Kind != DocColumnKind.MeshAsset &&
                column.Kind != DocColumnKind.AudioAsset &&
                column.Kind != DocColumnKind.UiAsset &&
                column.Kind != DocColumnKind.Formula)
            {
                continue;
            }

            string value = row.GetCell(column).StringValue ?? "";
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return row.Id;
    }

    public string ResolveRelationDisplayLabel(string relationTableId, string relationRowId)
    {
        if (string.IsNullOrEmpty(relationTableId) || string.IsNullOrEmpty(relationRowId))
        {
            return "";
        }

        var formulaContext = GetProjectFormulaContext();
        if (!formulaContext.TryGetTableById(relationTableId, out var relationTable))
        {
            return relationRowId;
        }

        if (!formulaContext.TryGetRowById(relationTable, relationRowId, out var relationRow))
        {
            return relationRowId;
        }

        return formulaContext.GetRowDisplayLabel(relationTable, relationRow);
    }

    public bool TryResolveRelationDisplayColumn(DocColumn relationColumn, DocTable relationTable, out DocColumn displayColumn)
    {
        if (!string.IsNullOrWhiteSpace(relationColumn.RelationDisplayColumnId))
        {
            for (int columnIndex = 0; columnIndex < relationTable.Columns.Count; columnIndex++)
            {
                var candidateColumn = relationTable.Columns[columnIndex];
                if (string.Equals(candidateColumn.Id, relationColumn.RelationDisplayColumnId, StringComparison.Ordinal))
                {
                    displayColumn = candidateColumn;
                    return true;
                }
            }
        }

        for (int columnIndex = 0; columnIndex < relationTable.Columns.Count; columnIndex++)
        {
            var candidateColumn = relationTable.Columns[columnIndex];
            if (candidateColumn.Kind != DocColumnKind.Text &&
                candidateColumn.Kind != DocColumnKind.Select &&
                candidateColumn.Kind != DocColumnKind.Formula)
            {
                continue;
            }

            displayColumn = candidateColumn;
            return true;
        }

        displayColumn = null!;
        return false;
    }

    private static string GetCellDisplayText(DocColumn column, DocCellValue cellValue)
    {
        if (column.Kind == DocColumnKind.Number)
        {
            return cellValue.NumberValue.ToString("G");
        }

        if (column.Kind == DocColumnKind.Checkbox)
        {
            return cellValue.BoolValue ? "true" : "false";
        }

        if (column.Kind == DocColumnKind.Formula &&
            string.IsNullOrWhiteSpace(cellValue.StringValue))
        {
            return cellValue.NumberValue.ToString("G");
        }

        if (column.Kind == DocColumnKind.Vec2)
        {
            return "(" + cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ")";
        }

        if (column.Kind == DocColumnKind.Vec3)
        {
            return "(" + cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cellValue.ZValue.ToString("G", CultureInfo.InvariantCulture) + ")";
        }

        if (column.Kind == DocColumnKind.Vec4)
        {
            return "(" + cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cellValue.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cellValue.WValue.ToString("G", CultureInfo.InvariantCulture) + ")";
        }

        if (column.Kind == DocColumnKind.Color)
        {
            return "rgba(" + cellValue.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cellValue.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cellValue.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                   cellValue.WValue.ToString("G", CultureInfo.InvariantCulture) + ")";
        }

        return cellValue.StringValue ?? "";
    }

    private enum FormulaRefreshMode
    {
        None = 0,
        Incremental = 1,
        StructuralIncremental = 2,
        Full = 3,
    }

    private sealed class FormulaRefreshPlan
    {
        public FormulaRefreshMode Mode { get; private set; }
        public bool RefreshDirtyTableIndexes { get; set; }
        public HashSet<string> DirtyTableIds { get; } = new(StringComparer.Ordinal);
        public HashSet<string> DirtyDocumentIds { get; } = new(StringComparer.Ordinal);

        public void PromoteMode(FormulaRefreshMode nextMode)
        {
            if ((int)nextMode > (int)Mode)
            {
                Mode = nextMode;
            }
        }

        public void AddDirtyTable(string tableId)
        {
            if (string.IsNullOrWhiteSpace(tableId))
            {
                return;
            }

            DirtyTableIds.Add(tableId);
        }

        public void AddDirtyDocument(string documentId)
        {
            if (string.IsNullOrWhiteSpace(documentId))
            {
                return;
            }

            DirtyDocumentIds.Add(documentId);
        }
    }

    private bool TryBuildFormulaEvaluationRequestForSingleCommand(
        DocCommand command,
        out DocFormulaEngine.EvaluationRequest evaluationRequest)
    {
        var singleCommandList = new List<DocCommand>(1) { command };
        return TryBuildFormulaEvaluationRequestForCommands(singleCommandList, out evaluationRequest);
    }

    private bool TryBuildFormulaEvaluationRequestForCommands(
        IReadOnlyList<DocCommand> commands,
        out DocFormulaEngine.EvaluationRequest evaluationRequest)
    {
        FormulaRefreshPlan refreshPlan = AnalyzeFormulaRefreshPlan(commands);
        if (refreshPlan.Mode == FormulaRefreshMode.None)
        {
            evaluationRequest = default;
            return false;
        }

        if (refreshPlan.Mode == FormulaRefreshMode.Full)
        {
            evaluationRequest = DocFormulaEngine.EvaluationRequest.Full();
            return true;
        }

        List<string>? dirtyTableIds = null;
        if (refreshPlan.DirtyTableIds.Count > 0)
        {
            dirtyTableIds = new List<string>(refreshPlan.DirtyTableIds.Count);
            foreach (string tableId in refreshPlan.DirtyTableIds)
            {
                dirtyTableIds.Add(tableId);
            }
        }

        List<string>? dirtyDocumentIds = null;
        if (refreshPlan.DirtyDocumentIds.Count > 0)
        {
            dirtyDocumentIds = new List<string>(refreshPlan.DirtyDocumentIds.Count);
            foreach (string documentId in refreshPlan.DirtyDocumentIds)
            {
                dirtyDocumentIds.Add(documentId);
            }
        }

        if ((dirtyTableIds == null || dirtyTableIds.Count == 0) &&
            (dirtyDocumentIds == null || dirtyDocumentIds.Count == 0))
        {
            evaluationRequest = default;
            return false;
        }

        evaluationRequest = refreshPlan.Mode == FormulaRefreshMode.StructuralIncremental
            ? DocFormulaEngine.EvaluationRequest.StructuralIncremental(
                dirtyTableIds,
                dirtyDocumentIds,
                refreshDirtyTableIndexes: refreshPlan.RefreshDirtyTableIndexes)
            : new DocFormulaEngine.EvaluationRequest
            {
                EnableIncremental = true,
                RequiresStructuralRefresh = false,
                RefreshDirtyTableIndexes = refreshPlan.RefreshDirtyTableIndexes,
                DirtyTableIds = dirtyTableIds,
                DirtyDocumentIds = dirtyDocumentIds,
                TargetFormulaColumnIdsByTableId = null,
            };

        return true;
    }

    private FormulaRefreshPlan AnalyzeFormulaRefreshPlan(IReadOnlyList<DocCommand> commands)
    {
        var refreshPlan = new FormulaRefreshPlan();
        bool projectContainsFormulaArtifacts = ProjectContainsFormulaArtifacts();

        for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
        {
            AnalyzeFormulaRefreshForCommand(commands[commandIndex], refreshPlan, projectContainsFormulaArtifacts);
        }

        if (refreshPlan.Mode == FormulaRefreshMode.StructuralIncremental &&
            refreshPlan.DirtyTableIds.Count == 0 &&
            refreshPlan.DirtyDocumentIds.Count == 0)
        {
            AddAllFormulaRelevantArtifacts(refreshPlan);
        }

        return refreshPlan;
    }

    private void AnalyzeFormulaRefreshForCommand(
        DocCommand command,
        FormulaRefreshPlan refreshPlan,
        bool projectContainsFormulaArtifacts)
    {
        if (command.TableVariantId != DocTableVariant.BaseVariantId &&
            IsVariantTableMutationCommand(command.Kind))
        {
            return;
        }

        switch (command.Kind)
        {
            case DocCommandKind.SetCell:
            {
                if (DoesSetCellCommandChangeCellFormulaExpression(command))
                {
                    refreshPlan.PromoteMode(FormulaRefreshMode.StructuralIncremental);
                }
                else
                {
                    refreshPlan.PromoteMode(FormulaRefreshMode.Incremental);
                }

                refreshPlan.AddDirtyTable(command.TableId);
                return;
            }
            case DocCommandKind.AddRow:
            case DocCommandKind.RemoveRow:
            case DocCommandKind.MoveRow:
            {
                refreshPlan.PromoteMode(FormulaRefreshMode.Incremental);
                refreshPlan.AddDirtyTable(command.TableId);
                refreshPlan.RefreshDirtyTableIndexes = true;
                return;
            }
            case DocCommandKind.AddTableVariable:
            case DocCommandKind.RemoveTableVariable:
            case DocCommandKind.RenameTableVariable:
            case DocCommandKind.SetTableVariableExpression:
            case DocCommandKind.SetTableVariableType:
            {
                refreshPlan.PromoteMode(FormulaRefreshMode.Incremental);
                refreshPlan.AddDirtyTable(command.TableId);
                refreshPlan.RefreshDirtyTableIndexes = true;
                return;
            }
            case DocCommandKind.SetColumnNumberSettings:
            {
                if (!CommandChangesNumberValues(command))
                {
                    return;
                }

                refreshPlan.PromoteMode(FormulaRefreshMode.Incremental);
                refreshPlan.AddDirtyTable(command.TableId);
                return;
            }
            case DocCommandKind.SetColumnFormula:
            {
                if (!ColumnFormulaExpressionChanged(command))
                {
                    return;
                }

                refreshPlan.PromoteMode(FormulaRefreshMode.StructuralIncremental);
                refreshPlan.AddDirtyTable(command.TableId);
                return;
            }
            case DocCommandKind.AddColumn:
            {
                AnalyzeAddColumnFormulaRefresh(command, refreshPlan, projectContainsFormulaArtifacts);
                return;
            }
            case DocCommandKind.AddTable:
            {
                AnalyzeAddTableFormulaRefresh(command, refreshPlan, projectContainsFormulaArtifacts);
                return;
            }
            case DocCommandKind.SetColumnRelation:
            {
                if (!ColumnRelationChanged(command))
                {
                    return;
                }

                if (!projectContainsFormulaArtifacts)
                {
                    return;
                }

                refreshPlan.PromoteMode(FormulaRefreshMode.StructuralIncremental);
                AddAllFormulaRelevantArtifacts(refreshPlan);
                return;
            }
            case DocCommandKind.RenameColumn:
            case DocCommandKind.RemoveColumn:
            case DocCommandKind.MoveColumn:
            case DocCommandKind.RenameTable:
            case DocCommandKind.RemoveTable:
            case DocCommandKind.SetTableSchemaSource:
            case DocCommandKind.SetTableInheritanceSource:
            case DocCommandKind.SetDerivedConfig:
            case DocCommandKind.SetDerivedBaseTable:
            case DocCommandKind.AddDerivedStep:
            case DocCommandKind.RemoveDerivedStep:
            case DocCommandKind.UpdateDerivedStep:
            case DocCommandKind.ReorderDerivedStep:
            case DocCommandKind.AddDerivedProjection:
            case DocCommandKind.RemoveDerivedProjection:
            case DocCommandKind.UpdateDerivedProjection:
            case DocCommandKind.ReorderDerivedProjection:
            {
                if (!projectContainsFormulaArtifacts)
                {
                    return;
                }

                refreshPlan.PromoteMode(FormulaRefreshMode.StructuralIncremental);
                AddAllFormulaRelevantArtifacts(refreshPlan);
                return;
            }
            case DocCommandKind.AddBlock:
            case DocCommandKind.RemoveBlock:
            case DocCommandKind.SetBlockText:
            case DocCommandKind.ChangeBlockType:
            case DocCommandKind.MoveBlock:
            {
                if (!CommandAffectsFormulaState(command))
                {
                    return;
                }

                refreshPlan.PromoteMode(FormulaRefreshMode.Incremental);
                refreshPlan.AddDirtyDocument(command.DocumentId);
                return;
            }
            case DocCommandKind.AddDocument:
            case DocCommandKind.RemoveDocument:
            case DocCommandKind.RenameDocument:
            {
                if (!CommandAffectsFormulaState(command))
                {
                    return;
                }

                refreshPlan.PromoteMode(FormulaRefreshMode.StructuralIncremental);
                AddAllFormulaRelevantArtifacts(refreshPlan);
                if (command.DocumentSnapshot != null)
                {
                    refreshPlan.AddDirtyDocument(command.DocumentSnapshot.Id);
                }

                refreshPlan.AddDirtyDocument(command.DocumentId);
                return;
            }
            case DocCommandKind.ReplaceProjectSnapshot:
            {
                refreshPlan.PromoteMode(FormulaRefreshMode.Full);
                return;
            }
            default:
            {
                DocCommandImpact.Flags impactFlags = DocCommandImpact.GetFlags(command.Kind);
                if ((impactFlags & DocCommandImpact.Flags.RequiresFormulaRecalculation) == 0)
                {
                    return;
                }

                if (!CommandAffectsFormulaState(command))
                {
                    return;
                }

                if (!projectContainsFormulaArtifacts)
                {
                    return;
                }

                refreshPlan.PromoteMode(FormulaRefreshMode.Full);
                return;
            }
        }
    }

    private void AnalyzeAddColumnFormulaRefresh(
        DocCommand command,
        FormulaRefreshPlan refreshPlan,
        bool projectContainsFormulaArtifacts)
    {
        if (command.ColumnSnapshot == null || string.IsNullOrWhiteSpace(command.TableId))
        {
            return;
        }

        DocTable? table = FindTableById(command.TableId);
        if (table == null)
        {
            return;
        }

        bool addedColumnHasFormula = command.ColumnSnapshot.Kind == DocColumnKind.Formula ||
                                     !string.IsNullOrWhiteSpace(command.ColumnSnapshot.FormulaExpression);
        if (addedColumnHasFormula)
        {
            refreshPlan.PromoteMode(FormulaRefreshMode.StructuralIncremental);
            refreshPlan.AddDirtyTable(table.Id);
            return;
        }

        if (!projectContainsFormulaArtifacts)
        {
            return;
        }

        if (AddedColumnChangesFirstNameBinding(table, command.ColumnSnapshot))
        {
            refreshPlan.PromoteMode(FormulaRefreshMode.StructuralIncremental);
            AddAllFormulaRelevantArtifacts(refreshPlan);
        }
    }

    private void AnalyzeAddTableFormulaRefresh(
        DocCommand command,
        FormulaRefreshPlan refreshPlan,
        bool projectContainsFormulaArtifacts)
    {
        if (command.TableSnapshot == null)
        {
            return;
        }

        DocTable? addedTable = FindTableById(command.TableSnapshot.Id);
        if (addedTable == null)
        {
            return;
        }

        if (TableContainsFormulaArtifacts(addedTable))
        {
            refreshPlan.PromoteMode(FormulaRefreshMode.StructuralIncremental);
            refreshPlan.AddDirtyTable(addedTable.Id);
            return;
        }

        if (!projectContainsFormulaArtifacts)
        {
            return;
        }

        if (AddedTableChangesFirstNameBinding(addedTable))
        {
            refreshPlan.PromoteMode(FormulaRefreshMode.StructuralIncremental);
            AddAllFormulaRelevantArtifacts(refreshPlan);
        }
    }

    private void AddAllFormulaRelevantArtifacts(FormulaRefreshPlan refreshPlan)
    {
        for (int tableIndex = 0; tableIndex < Project.Tables.Count; tableIndex++)
        {
            DocTable table = Project.Tables[tableIndex];
            if (!TableContainsFormulaArtifacts(table))
            {
                continue;
            }

            refreshPlan.AddDirtyTable(table.Id);
        }

        for (int documentIndex = 0; documentIndex < Project.Documents.Count; documentIndex++)
        {
            DocDocument document = Project.Documents[documentIndex];
            if (!DocumentContainsVariableBlock(document))
            {
                continue;
            }

            refreshPlan.AddDirtyDocument(document.Id);
        }
    }

    private bool ProjectContainsFormulaArtifacts()
    {
        for (int tableIndex = 0; tableIndex < Project.Tables.Count; tableIndex++)
        {
            if (TableContainsFormulaArtifacts(Project.Tables[tableIndex]))
            {
                return true;
            }
        }

        for (int documentIndex = 0; documentIndex < Project.Documents.Count; documentIndex++)
        {
            if (DocumentContainsVariableBlock(Project.Documents[documentIndex]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TableContainsFormulaArtifacts(DocTable table)
    {
        if (table.IsDerived ||
            table.DerivedConfig != null ||
            !string.IsNullOrWhiteSpace(table.SchemaSourceTableId) ||
            !string.IsNullOrWhiteSpace(table.InheritanceSourceTableId))
        {
            return true;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (!string.IsNullOrWhiteSpace(column.FormulaExpression))
            {
                return true;
            }
        }

        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            DocTableVariable tableVariable = table.Variables[variableIndex];
            if (!string.IsNullOrWhiteSpace(tableVariable.Expression))
            {
                return true;
            }
        }

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            DocRow row = table.Rows[rowIndex];
            foreach (KeyValuePair<string, DocCellValue> cellPair in row.Cells)
            {
                if (!string.IsNullOrWhiteSpace(cellPair.Value.CellFormulaExpression))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AddedColumnChangesFirstNameBinding(DocTable table, DocColumn addedColumn)
    {
        if (string.IsNullOrWhiteSpace(addedColumn.Name))
        {
            return false;
        }

        int matchingColumnCount = 0;
        int firstMatchingColumnIndex = -1;
        int addedColumnIndex = -1;
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn candidateColumn = table.Columns[columnIndex];
            if (!string.Equals(candidateColumn.Name, addedColumn.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (firstMatchingColumnIndex < 0)
            {
                firstMatchingColumnIndex = columnIndex;
            }

            matchingColumnCount++;
            if (string.Equals(candidateColumn.Id, addedColumn.Id, StringComparison.Ordinal))
            {
                addedColumnIndex = columnIndex;
            }
        }

        if (matchingColumnCount <= 1 || addedColumnIndex < 0)
        {
            return false;
        }

        return firstMatchingColumnIndex == addedColumnIndex;
    }

    private bool AddedTableChangesFirstNameBinding(DocTable addedTable)
    {
        if (string.IsNullOrWhiteSpace(addedTable.Name))
        {
            return false;
        }

        int matchingTableCount = 0;
        int firstMatchingTableIndex = -1;
        int addedTableIndex = -1;
        for (int tableIndex = 0; tableIndex < Project.Tables.Count; tableIndex++)
        {
            DocTable candidateTable = Project.Tables[tableIndex];
            if (!string.Equals(candidateTable.Name, addedTable.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (firstMatchingTableIndex < 0)
            {
                firstMatchingTableIndex = tableIndex;
            }

            matchingTableCount++;
            if (string.Equals(candidateTable.Id, addedTable.Id, StringComparison.Ordinal))
            {
                addedTableIndex = tableIndex;
            }
        }

        if (matchingTableCount <= 1 || addedTableIndex < 0)
        {
            return false;
        }

        return firstMatchingTableIndex == addedTableIndex;
    }

    private static bool ColumnFormulaExpressionChanged(DocCommand command)
    {
        string oldExpression = command.OldFormulaExpression?.Trim() ?? "";
        string newExpression = command.NewFormulaExpression?.Trim() ?? "";
        return !string.Equals(oldExpression, newExpression, StringComparison.Ordinal);
    }

    private static bool ColumnRelationChanged(DocCommand command)
    {
        if (!string.Equals(command.OldRelationTableId, command.NewRelationTableId, StringComparison.Ordinal))
        {
            return true;
        }

        if (command.OldRelationTargetMode != command.NewRelationTargetMode)
        {
            return true;
        }

        if (command.OldRelationTableVariantId != command.NewRelationTableVariantId)
        {
            return true;
        }

        return !string.Equals(command.OldRelationDisplayColumnId, command.NewRelationDisplayColumnId, StringComparison.Ordinal);
    }

    private static bool CommandChangesNumberValues(DocCommand command)
    {
        return NumberValueMapHasEntries(command.NewNumberValuesByRowId) ||
               NumberValueMapHasEntries(command.OldNumberValuesByRowId);
    }

    private static bool NumberValueMapHasEntries(Dictionary<string, double>? valuesByRowId)
    {
        return valuesByRowId != null && valuesByRowId.Count > 0;
    }

    private bool CommandAffectsFormulaState(DocCommand command)
    {
        switch (command.Kind)
        {
            case DocCommandKind.AddBlock:
            case DocCommandKind.RemoveBlock:
            {
                return command.BlockSnapshot != null && command.BlockSnapshot.Type == DocBlockType.Variable;
            }
            case DocCommandKind.SetBlockText:
            case DocCommandKind.MoveBlock:
            {
                return TryGetDocumentBlockType(command.DocumentId, command.BlockId, out DocBlockType blockType) &&
                       blockType == DocBlockType.Variable;
            }
            case DocCommandKind.ChangeBlockType:
            {
                return command.OldBlockType == DocBlockType.Variable ||
                       command.NewBlockType == DocBlockType.Variable;
            }
            case DocCommandKind.AddDocument:
            case DocCommandKind.RemoveDocument:
            {
                return DocumentContainsVariableBlock(command.DocumentSnapshot);
            }
            case DocCommandKind.RenameDocument:
            {
                return TryGetDocument(command.DocumentId, out DocDocument document) &&
                       DocumentContainsVariableBlock(document);
            }
            default:
            {
                return true;
            }
        }
    }

    private bool TryGetDocument(string documentId, out DocDocument document)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            document = null!;
            return false;
        }

        for (int documentIndex = 0; documentIndex < Project.Documents.Count; documentIndex++)
        {
            var candidateDocument = Project.Documents[documentIndex];
            if (string.Equals(candidateDocument.Id, documentId, StringComparison.Ordinal))
            {
                document = candidateDocument;
                return true;
            }
        }

        document = null!;
        return false;
    }

    private bool TryGetDocumentBlockType(string documentId, string blockId, out DocBlockType blockType)
    {
        if (!TryGetDocument(documentId, out DocDocument document) || string.IsNullOrWhiteSpace(blockId))
        {
            blockType = default;
            return false;
        }

        for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            if (string.Equals(block.Id, blockId, StringComparison.Ordinal))
            {
                blockType = block.Type;
                return true;
            }
        }

        blockType = default;
        return false;
    }

    private static bool DocumentContainsVariableBlock(DocDocument? document)
    {
        if (document == null)
        {
            return false;
        }

        for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex].Type == DocBlockType.Variable)
            {
                return true;
            }
        }

        return false;
    }

    private static bool DoesSetCellCommandChangeCellFormulaExpression(DocCommand command)
    {
        if (command.Kind != DocCommandKind.SetCell)
        {
            return false;
        }

        string oldExpression = command.OldCellValue.CellFormulaExpression?.Trim() ?? "";
        string newExpression = command.NewCellValue.CellFormulaExpression?.Trim() ?? "";
        return !string.Equals(oldExpression, newExpression, StringComparison.Ordinal);
    }

    private DocFormulaEngine.EvaluationMetrics RecalculateComputedColumns(DocFormulaEngine.EvaluationRequest evaluationRequest)
    {
        try
        {
            var evaluationMetrics = _formulaEngine.EvaluateProject(Project, evaluationRequest);
            LastComputeError = null;
            return evaluationMetrics;
        }
        catch (Exception ex)
        {
            LastComputeError = ex.Message;
            return default;
        }
    }

    private void RecalculateComputedColumns()
    {
        RecalculateComputedColumnsWithTiming(DocFormulaEngine.EvaluationRequest.Full());
    }

    private void RecalculateComputedColumnsWithTiming(DocFormulaEngine.EvaluationRequest evaluationRequest)
    {
        var evaluationMetrics = RecalculateComputedColumns(evaluationRequest);
        RecordFormulaRecalculationTiming(evaluationMetrics);
    }

    private void RecordCommandExecutionTiming(long elapsedTicks, int commandItemCount)
    {
        if (elapsedTicks < 0)
        {
            elapsedTicks = 0;
        }

        Interlocked.Increment(ref _commandOperationCount);
        Interlocked.Add(ref _commandItemCount, commandItemCount);
        Interlocked.Add(ref _commandTotalTicks, elapsedTicks);
        UpdateMax(ref _commandMaxTicks, elapsedTicks);
    }

    private void RecordFormulaRecalculationTiming(DocFormulaEngine.EvaluationMetrics evaluationMetrics)
    {
        long elapsedTicks = evaluationMetrics.TotalTicks;
        if (elapsedTicks < 0)
        {
            elapsedTicks = 0;
        }

        Interlocked.Increment(ref _formulaRecalculationCount);
        Interlocked.Add(ref _formulaRecalculationTotalTicks, elapsedTicks);
        UpdateMax(ref _formulaRecalculationMaxTicks, elapsedTicks);

        if (evaluationMetrics.UsedIncrementalPlan)
        {
            Interlocked.Increment(ref _formulaIncrementalCount);
        }
        else
        {
            Interlocked.Increment(ref _formulaFullCount);
        }

        RecordFormulaPhaseTiming(ref _formulaCompileTotalTicks, ref _formulaCompileMaxTicks, evaluationMetrics.CompileTicks);
        RecordFormulaPhaseTiming(ref _formulaPlanTotalTicks, ref _formulaPlanMaxTicks, evaluationMetrics.PlanTicks);
        RecordFormulaPhaseTiming(ref _formulaDerivedTotalTicks, ref _formulaDerivedMaxTicks, evaluationMetrics.DerivedTicks);
        RecordFormulaPhaseTiming(ref _formulaEvaluateTotalTicks, ref _formulaEvaluateMaxTicks, evaluationMetrics.EvaluateTicks);
    }

    private static void RecordFormulaPhaseTiming(ref long totalTicksTarget, ref long maxTicksTarget, long elapsedTicks)
    {
        if (elapsedTicks <= 0)
        {
            return;
        }

        Interlocked.Add(ref totalTicksTarget, elapsedTicks);
        UpdateMax(ref maxTicksTarget, elapsedTicks);
    }

    private void RecordAutoSaveTiming(long elapsedTicks)
    {
        if (elapsedTicks < 0)
        {
            elapsedTicks = 0;
        }

        Interlocked.Increment(ref _autoSaveCount);
        Interlocked.Add(ref _autoSaveTotalTicks, elapsedTicks);
        UpdateMax(ref _autoSaveMaxTicks, elapsedTicks);
    }

    private static void UpdateMax(ref long target, long value)
    {
        while (true)
        {
            long current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }

            long observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
            {
                return;
            }
        }
    }

    private void RegisterLiveExportImpact(DocCommandImpact.Flags impactFlags)
    {
        if ((impactFlags & DocCommandImpact.Flags.AffectsTableState) != 0)
        {
            _latestTableMutationRevisionForLiveExport = _projectRevision;
        }
    }

    private void InvalidateAutoSaveWorkerQueueState()
    {
        _autoSaveWorkerQueuedRevision = -1;
        _autoSaveWorkerQueuedPath = null;
    }

    private bool IsBackgroundWorkerEnabledForCurrentProjectPath()
    {
        return !string.IsNullOrWhiteSpace(ProjectPath) && (AutoSave || AutoLiveExport);
    }

    private AutoSaveWorker GetOrCreateAutoSaveWorker()
    {
        _autoSaveWorker ??= new AutoSaveWorker();
        return _autoSaveWorker;
    }

    private void ResetAutoSaveWorkerBaseline(bool isDirty)
    {
        if (!IsBackgroundWorkerEnabledForCurrentProjectPath())
        {
            InvalidateAutoSaveWorkerQueueState();
            return;
        }

        var worker = GetOrCreateAutoSaveWorker();
        worker.EnqueueReset(CloneProjectSnapshot(Project), isDirty);
        _autoSaveWorkerQueuedRevision = _projectRevision;
        _autoSaveWorkerQueuedPath = ProjectPath;
    }

    private bool TryPrepareAutoSaveWorkerForIncrementalMutation(int expectedWorkerRevision)
    {
        if (!IsBackgroundWorkerEnabledForCurrentProjectPath())
        {
            InvalidateAutoSaveWorkerQueueState();
            return false;
        }

        if (_autoSaveWorker == null ||
            !string.Equals(_autoSaveWorkerQueuedPath, ProjectPath, StringComparison.Ordinal) ||
            _autoSaveWorkerQueuedRevision != expectedWorkerRevision)
        {
            ResetAutoSaveWorkerBaseline(isDirty: true);
            return false;
        }

        return true;
    }

    private void QueueAutoSaveWorkerExecuteCommand(DocCommand command, int expectedWorkerRevision)
    {
        if (!TryPrepareAutoSaveWorkerForIncrementalMutation(expectedWorkerRevision))
        {
            return;
        }

        _autoSaveWorker!.EnqueueExecute(CloneCommandForAutoSaveWorker(command));
        _autoSaveWorkerQueuedRevision = _projectRevision;
    }

    private void QueueAutoSaveWorkerExecuteCommands(IReadOnlyList<DocCommand> commands, int expectedWorkerRevision)
    {
        if (!TryPrepareAutoSaveWorkerForIncrementalMutation(expectedWorkerRevision))
        {
            return;
        }

        for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
        {
            _autoSaveWorker!.EnqueueExecute(CloneCommandForAutoSaveWorker(commands[commandIndex]));
        }

        _autoSaveWorkerQueuedRevision = _projectRevision;
    }

    private void QueueAutoSaveWorkerUndo(int expectedWorkerRevision)
    {
        if (!TryPrepareAutoSaveWorkerForIncrementalMutation(expectedWorkerRevision))
        {
            return;
        }

        _autoSaveWorker!.EnqueueUndo();
        _autoSaveWorkerQueuedRevision = _projectRevision;
    }

    private void QueueAutoSaveWorkerRedo(int expectedWorkerRevision)
    {
        if (!TryPrepareAutoSaveWorkerForIncrementalMutation(expectedWorkerRevision))
        {
            return;
        }

        _autoSaveWorker!.EnqueueRedo();
        _autoSaveWorkerQueuedRevision = _projectRevision;
    }

    private void MarkDirtyAndMaybeAutoSave()
    {
        IsDirty = true;
        if (!IsBackgroundWorkerEnabledForCurrentProjectPath())
        {
            InvalidateAutoSaveWorkerQueueState();
            return;
        }

        if (!AutoSave)
        {
            return;
        }

        _autoSavePending = true;
        _autoSaveDueTicks = DateTime.UtcNow.Ticks + AutoSaveDebounceTicks;
    }

    private void ProcessPendingAutoSave(bool forceDue = false)
    {
        if (_autoSaveTask != null && _autoSaveTask.IsCompleted)
        {
            CompleteAutoSaveTask();
        }

        if (!_autoSavePending)
        {
            return;
        }

        if (_autoSaveTask != null)
        {
            return;
        }

        if (!forceDue)
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            if (nowTicks < _autoSaveDueTicks)
            {
                return;
            }
        }

        StartAutoSaveTask();
    }

    private void FlushAutoSave()
    {
        while (true)
        {
            ProcessPendingAutoSave(forceDue: true);
            if (_autoSaveTask == null && !_autoSavePending)
            {
                return;
            }

            if (_autoSaveTask != null)
            {
                _autoSaveTask.GetAwaiter().GetResult();
            }
        }
    }

    private void StartAutoSaveTask()
    {
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            InvalidateAutoSaveWorkerQueueState();
            _autoSavePending = false;
            return;
        }

        if (!AutoSave)
        {
            _autoSavePending = false;
            return;
        }

        if (_autoSaveWorker == null ||
            _autoSaveWorkerQueuedRevision != _projectRevision ||
            !string.Equals(_autoSaveWorkerQueuedPath, ProjectPath, StringComparison.Ordinal))
        {
            ResetAutoSaveWorkerBaseline(isDirty: IsDirty);
        }

        if (_autoSaveWorker == null)
        {
            _autoSavePending = false;
            return;
        }

        var request = new AutoSaveRequest
        {
            Path = ProjectPath,
            GameRoot = GameRoot,
            ProjectName = Project.Name,
            ProjectRevision = _projectRevision,
        };

        _autoSavePending = false;
        _autoSaveTask = _autoSaveWorker.RequestSaveAsync(request);
    }

    private void CompleteAutoSaveTask()
    {
        if (_autoSaveTask == null)
        {
            return;
        }

        var saveResult = _autoSaveTask.GetAwaiter().GetResult();
        _autoSaveTask = null;
        RecordAutoSaveTiming(saveResult.ElapsedTicks);

        if (!saveResult.Success)
        {
            LastStatusMessage = saveResult.ErrorMessage;
            IsDirty = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(ProjectPath) &&
            string.Equals(ProjectPath, saveResult.Request.Path, StringComparison.Ordinal))
        {
            DocActiveProjectStateFile.Write(WorkspaceRoot, ProjectPath, GameRoot, Project.Name);
        }

        if (!_autoSavePending && saveResult.Request.ProjectRevision == _projectRevision)
        {
            IsDirty = false;
            if (_latestTableMutationRevisionForLiveExport > _lastSuccessfulLiveExportRevision)
            {
                RequestLiveExport(immediate: false);
            }
        }
    }

    private void RequestLiveExport(bool immediate)
    {
        if (!AutoLiveExport || string.IsNullOrWhiteSpace(ProjectPath))
        {
            return;
        }

        if (!HasEnabledExportTable(Project))
        {
            return;
        }

        _liveExportPending = true;
        _liveExportDueTicks = DateTime.UtcNow.Ticks + (immediate ? 0 : LiveExportDebounceTicks);
    }

    private void ProcessPendingLiveExport()
    {
        if (_liveExportTask != null && _liveExportTask.IsCompleted)
        {
            CompleteLiveExportTask();
        }

        if (!_liveExportPending)
        {
            return;
        }

        if (!AutoLiveExport)
        {
            _liveExportPending = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            _liveExportPending = false;
            return;
        }

        if (_liveExportTask != null)
        {
            return;
        }

        long nowTicks = DateTime.UtcNow.Ticks;
        if (nowTicks < _liveExportDueTicks)
        {
            return;
        }

        string dbRoot = ProjectPath;
        string? gameRoot = GameRoot;
        if (string.IsNullOrWhiteSpace(gameRoot) && DocProjectPaths.TryGetGameRootFromDbRoot(dbRoot, out var inferredGameRoot))
        {
            gameRoot = inferredGameRoot;
            GameRoot = inferredGameRoot;
        }

        if (!HasEnabledExportTable(Project))
        {
            _liveExportPending = false;
            return;
        }

        if (_autoSaveWorker == null ||
            _autoSaveWorkerQueuedRevision != _projectRevision ||
            !string.Equals(_autoSaveWorkerQueuedPath, ProjectPath, StringComparison.Ordinal))
        {
            ResetAutoSaveWorkerBaseline(isDirty: IsDirty);
        }

        if (_autoSaveWorker == null)
        {
            _liveExportPending = false;
            return;
        }

        var request = new LiveExportRequest
        {
            DbRoot = dbRoot,
            GameRoot = gameRoot,
            ProjectRevision = _projectRevision,
        };

        _liveExportPending = false;
        _liveExportTask = _autoSaveWorker.RequestLiveExportAsync(request);
    }

    private void CompleteLiveExportTask()
    {
        if (_liveExportTask == null)
        {
            return;
        }

        var liveExportResult = _liveExportTask.GetAwaiter().GetResult();
        _liveExportTask = null;
        if (liveExportResult.Success)
        {
            if (liveExportResult.Request.ProjectRevision > _lastSuccessfulLiveExportRevision)
            {
                _lastSuccessfulLiveExportRevision = liveExportResult.Request.ProjectRevision;
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(liveExportResult.ErrorMessage))
        {
            LastStatusMessage = liveExportResult.ErrorMessage;
        }

        _liveExportPending = true;
        _liveExportDueTicks = DateTime.UtcNow.Ticks + LiveExportRetryTicks;
    }

    private void QueueDebouncedFormulaRefresh(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return;
        }

        if (!_pendingDebouncedFormulaRefresh)
        {
            _pendingDebouncedFormulaRefresh = true;
            _pendingDebouncedFormulaRefreshDueTicks = DateTime.UtcNow.Ticks + FormulaPreviewDebounceTicks;
        }

        _pendingDebouncedFormulaDirtyTableIds.Add(tableId);
    }

    private void CancelPendingDebouncedFormulaRefresh()
    {
        _pendingDebouncedFormulaRefresh = false;
        _pendingDebouncedFormulaRefreshDueTicks = 0;
        _pendingDebouncedFormulaDirtyTableIds.Clear();
    }

    private void ProcessPendingDebouncedFormulaRefresh()
    {
        if (!_pendingDebouncedFormulaRefresh || _pendingDebouncedFormulaDirtyTableIds.Count == 0)
        {
            return;
        }

        long nowTicks = DateTime.UtcNow.Ticks;
        if (nowTicks < _pendingDebouncedFormulaRefreshDueTicks)
        {
            return;
        }

        _pendingDebouncedFormulaDirtyTableListScratch.Clear();
        foreach (var tableId in _pendingDebouncedFormulaDirtyTableIds)
        {
            _pendingDebouncedFormulaDirtyTableListScratch.Add(tableId);
        }

        _pendingDebouncedFormulaRefresh = false;
        _pendingDebouncedFormulaRefreshDueTicks = 0;
        _pendingDebouncedFormulaDirtyTableIds.Clear();

        if (_pendingDebouncedFormulaDirtyTableListScratch.Count == 0)
        {
            return;
        }

        RecalculateComputedColumnsWithTiming(
            DocFormulaEngine.EvaluationRequest.Incremental(
                _pendingDebouncedFormulaDirtyTableListScratch,
                refreshDirtyTableIndexes: false));
        BumpLiveValueRevision();
    }

    private bool TryCreateFormulaEvaluationRequestForScope(
        string dirtyTableId,
        DocFormulaEvalScope scope,
        out DocFormulaEngine.EvaluationRequest evaluationRequest)
    {
        evaluationRequest = default;
        if (string.IsNullOrWhiteSpace(dirtyTableId))
        {
            return false;
        }

        BuildTargetFormulaColumnsByTableForScope(scope);
        if (_scopeTargetFormulaColumnsByTableScratch.Count == 0)
        {
            return false;
        }

        _singleDirtyTableIdScratch[0] = dirtyTableId;
        evaluationRequest = DocFormulaEngine.EvaluationRequest.IncrementalTargeted(
            _singleDirtyTableIdScratch,
            _scopeTargetFormulaColumnsByTableScratch,
            refreshDirtyTableIndexes: false);
        return true;
    }

    private void ApplyPreviewFormulaRefresh(
        string dirtyTableId,
        bool allowDeferredNonInteractiveRefresh)
    {
        if (string.IsNullOrWhiteSpace(dirtyTableId))
        {
            return;
        }

        if (TryCreateFormulaEvaluationRequestForScope(
                dirtyTableId,
                DocFormulaEvalScope.Interactive,
                out var interactiveRequest))
        {
            RecalculateComputedColumnsWithTiming(interactiveRequest);
            if (allowDeferredNonInteractiveRefresh && HasNonInteractiveFormulaColumns())
            {
                QueueDebouncedFormulaRefresh(dirtyTableId);
            }

            return;
        }

        if (!allowDeferredNonInteractiveRefresh)
        {
            return;
        }

        _singleDirtyTableIdScratch[0] = dirtyTableId;
        RecalculateComputedColumnsWithTiming(
            DocFormulaEngine.EvaluationRequest.Incremental(
                _singleDirtyTableIdScratch,
                refreshDirtyTableIndexes: false));
    }

    private bool HasNonInteractiveFormulaColumns()
    {
        if (_nonInteractiveFormulaCoverageRevision == _projectRevision &&
            ReferenceEquals(_nonInteractiveFormulaCoverageProjectReference, Project))
        {
            return _hasNonInteractiveFormulaColumns;
        }

        BuildTargetFormulaColumnsByTableForScope(DocFormulaEvalScope.Interactive);
        _hasNonInteractiveFormulaColumns = false;
        for (int tableIndex = 0; tableIndex < Project.Tables.Count; tableIndex++)
        {
            var table = Project.Tables[tableIndex];
            _scopeTargetFormulaColumnsByTableScratch.TryGetValue(table.Id, out List<string>? interactiveColumnIds);
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                if (string.IsNullOrWhiteSpace(column.FormulaExpression))
                {
                    continue;
                }

                if (!ContainsColumnId(interactiveColumnIds, column.Id))
                {
                    _hasNonInteractiveFormulaColumns = true;
                    break;
                }
            }

            if (_hasNonInteractiveFormulaColumns)
            {
                break;
            }
        }

        _nonInteractiveFormulaCoverageRevision = _projectRevision;
        _nonInteractiveFormulaCoverageProjectReference = Project;
        return _hasNonInteractiveFormulaColumns;
    }

    private static bool ContainsColumnId(List<string>? columnIds, string columnId)
    {
        if (columnIds == null || columnIds.Count <= 0)
        {
            return false;
        }

        for (int columnIndex = 0; columnIndex < columnIds.Count; columnIndex++)
        {
            if (string.Equals(columnIds[columnIndex], columnId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void BuildTargetFormulaColumnsByTableForScope(DocFormulaEvalScope scope)
    {
        if (_scopeTargetFormulaColumnsRevision == _projectRevision &&
            _scopeTargetFormulaColumnsCachedScope == scope &&
            ReferenceEquals(_scopeTargetFormulaColumnsProjectReference, Project))
        {
            return;
        }

        _scopeTargetFormulaColumnsByTableScratch.Clear();

        for (int tableIndex = 0; tableIndex < Project.Tables.Count; tableIndex++)
        {
            var table = Project.Tables[tableIndex];

            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                if (HasFormulaEvalScope(column, scope))
                {
                    AddScopeTargetFormulaColumn(table, column.Id);
                }
            }

            if (scope != DocFormulaEvalScope.Interactive)
            {
                continue;
            }

            for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
            {
                var view = table.Views[viewIndex];
                if (view.Type != DocViewType.Chart || string.IsNullOrWhiteSpace(view.ChartValueColumnId))
                {
                    continue;
                }

                var valueColumn = FindColumnById(table, view.ChartValueColumnId);
                if (valueColumn == null)
                {
                    continue;
                }

                if (valueColumn.Kind == DocColumnKind.Subtable)
                {
                    if (string.IsNullOrWhiteSpace(valueColumn.SubtableId))
                    {
                        continue;
                    }

                    var childTable = FindTableById(valueColumn.SubtableId);
                    if (childTable == null)
                    {
                        continue;
                    }

                    var childChartView = FindFirstChartView(childTable);
                    if (childChartView == null || string.IsNullOrWhiteSpace(childChartView.ChartValueColumnId))
                    {
                        continue;
                    }

                    AddScopeTargetFormulaColumn(childTable, childChartView.ChartValueColumnId);
                    continue;
                }

                AddScopeTargetFormulaColumn(table, valueColumn.Id);
            }
        }

        _scopeTargetFormulaColumnsRevision = _projectRevision;
        _scopeTargetFormulaColumnsCachedScope = scope;
        _scopeTargetFormulaColumnsProjectReference = Project;
    }

    private void AddScopeTargetFormulaColumn(DocTable table, string? columnId)
    {
        if (string.IsNullOrWhiteSpace(columnId))
        {
            return;
        }

        var column = FindColumnById(table, columnId);
        if (column == null || string.IsNullOrWhiteSpace(column.FormulaExpression))
        {
            return;
        }

        if (!_scopeTargetFormulaColumnsByTableScratch.TryGetValue(table.Id, out var columnIds))
        {
            columnIds = new List<string>(4);
            _scopeTargetFormulaColumnsByTableScratch[table.Id] = columnIds;
        }

        for (int existingIndex = 0; existingIndex < columnIds.Count; existingIndex++)
        {
            if (string.Equals(columnIds[existingIndex], column.Id, StringComparison.Ordinal))
            {
                return;
            }
        }

        columnIds.Add(column.Id);
    }

    private static bool HasFormulaEvalScope(DocColumn column, DocFormulaEvalScope scope)
    {
        return (column.FormulaEvalScopes & scope) == scope;
    }

    private DocTable? FindTableById(string tableId)
    {
        for (int tableIndex = 0; tableIndex < Project.Tables.Count; tableIndex++)
        {
            var table = Project.Tables[tableIndex];
            if (string.Equals(table.Id, tableId, StringComparison.Ordinal))
            {
                return table;
            }
        }

        return null;
    }

    private static DocView? FindFirstChartView(DocTable table)
    {
        for (int viewIndex = 0; viewIndex < table.Views.Count; viewIndex++)
        {
            var view = table.Views[viewIndex];
            if (view.Type == DocViewType.Chart)
            {
                return view;
            }
        }

        return null;
    }

    private static bool HasEnabledExportTable(DocProject project)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            if (project.Tables[tableIndex].ExportConfig?.Enabled == true)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cancels the current inline table edit, reverting any active number-drag preview.
    /// </summary>
    public void CancelTableCellEditIfActive()
    {
        if (!EditState.IsEditing)
        {
            return;
        }

        RevertNumberCellPreviewIfActive();
        EditState.EndEdit();
    }

    /// <summary>
    /// Applies a live preview value for a number cell edit and recomputes formulas incrementally.
    /// This does not create an undo command or mark the document dirty.
    /// </summary>
    public bool PreviewNumberCellValueFromEdit(
        double previewValue,
        bool allowDeferredNonInteractiveRefresh = true)
    {
        if (!EditState.IsEditing || !EditState.HasNumberPreviewValue)
        {
            return false;
        }

        if (!TryResolveActiveEditCell(out var table, out var row, out var column))
        {
            return false;
        }

        if (column.Kind != DocColumnKind.Number ||
            column.IsProjected ||
            !string.IsNullOrWhiteSpace(column.FormulaExpression))
        {
            return false;
        }

        double normalizedPreviewValue = NormalizeNumberForColumn(column, previewValue);

        var currentCell = row.GetCell(column);
        if (Math.Abs(currentCell.NumberValue - normalizedPreviewValue) < 0.0000001)
        {
            return false;
        }

        row.SetCell(column.Id, DocCellValue.Number(normalizedPreviewValue));
        ApplyPreviewFormulaRefresh(table.Id, allowDeferredNonInteractiveRefresh);
        BumpLiveValueRevision();
        return true;
    }

    public bool PreviewTextCellValue(string tableId, string rowId, string columnId, string previewText)
    {
        if (!TryResolveCellByIds(tableId, rowId, columnId, out var table, out var row, out var column))
        {
            return false;
        }

        if (column.IsProjected ||
            !string.IsNullOrWhiteSpace(column.FormulaExpression))
        {
            return false;
        }

        if (column.Kind == DocColumnKind.Number ||
            column.Kind == DocColumnKind.Checkbox ||
            column.Kind == DocColumnKind.Formula)
        {
            return false;
        }

        string normalizedPreviewText = previewText ?? "";
        var currentCell = row.GetCell(column);
        string currentText = currentCell.StringValue ?? "";
        if (string.Equals(currentText, normalizedPreviewText, StringComparison.Ordinal))
        {
            return false;
        }

        row.SetCell(column.Id, DocCellValue.Text(normalizedPreviewText));
        ApplyPreviewFormulaRefresh(table.Id, allowDeferredNonInteractiveRefresh: true);
        BumpLiveValueRevision();
        return true;
    }

    /// <summary>
    /// Commits the currently active inline table cell edit (if any) as a SetCell command.
    /// This ensures computed/derived tables stay in sync even when focus changes outside the spreadsheet.
    /// </summary>
    public bool CommitTableCellEditIfActive()
    {
        if (!EditState.IsEditing)
        {
            return false;
        }

        if (!TryResolveActiveEditCell(out var table, out var row, out var column) ||
            column.IsProjected ||
            !string.IsNullOrWhiteSpace(column.FormulaExpression))
        {
            EditState.EndEdit();
            return false;
        }

        string text = new string(EditState.Buffer, 0, EditState.BufferLength);
        var oldCell = row.GetCell(column);
        DocCellValue newCell;

        if (column.Kind == DocColumnKind.Number)
        {
            if (!double.TryParse(text, out double num))
            {
                RevertNumberCellPreviewIfActive();
                EditState.EndEdit();
                return false;
            }

            num = NormalizeNumberForColumn(column, num);

            double originalNumberValue = EditState.HasNumberPreviewValue
                ? EditState.NumberPreviewOriginalValue
                : oldCell.NumberValue;
            if (Math.Abs(originalNumberValue - num) < 0.0000001)
            {
                RevertNumberCellPreviewIfActive();
                EditState.EndEdit();
                return false;
            }

            oldCell = DocCellValue.Number(originalNumberValue);
            newCell = DocCellValue.Number(num);
        }
        else
        {
            string oldText = oldCell.StringValue ?? "";
            if (string.Equals(oldText, text, StringComparison.Ordinal))
            {
                EditState.EndEdit();
                return false;
            }
            newCell = column.Kind == DocColumnKind.MeshAsset
                ? DocCellValue.Text(text, oldCell.ModelPreviewSettings)
                : DocCellValue.Text(text);
        }

        ExecuteCommand(new DocCommand
        {
            Kind = DocCommandKind.SetCell,
            TableId = table.Id,
            RowId = row.Id,
            ColumnId = column.Id,
            OldCellValue = oldCell,
            NewCellValue = newCell,
        });

        EditState.EndEdit();
        return true;
    }

    public double NormalizeNumberForColumn(DocColumn column, double rawValue)
    {
        return DocCellValueNormalizer.NormalizeNumber(column, rawValue);
    }

    private bool TryResolveActiveEditCell(out DocTable table, out DocRow row, out DocColumn column)
    {
        table = null!;
        row = null!;
        column = null!;

        if (!EditState.IsEditing)
        {
            return false;
        }

        string tableId = EditState.TableId;
        string rowId = EditState.RowId;
        string columnId = EditState.ColumnId;
        if (string.IsNullOrEmpty(tableId) || string.IsNullOrEmpty(rowId) || string.IsNullOrEmpty(columnId))
        {
            return false;
        }

        var formulaContext = GetProjectFormulaContext();
        if (!formulaContext.TryGetTableById(tableId, out table))
        {
            return false;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var candidateColumn = table.Columns[columnIndex];
            if (string.Equals(candidateColumn.Id, columnId, StringComparison.Ordinal))
            {
                column = candidateColumn;
                break;
            }
        }

        if (column == null)
        {
            return false;
        }

        return formulaContext.TryGetRowById(table, rowId, out row);
    }

    private bool TryResolveCellByIds(
        string tableId,
        string rowId,
        string columnId,
        out DocTable table,
        out DocRow row,
        out DocColumn column)
    {
        table = null!;
        row = null!;
        column = null!;

        if (string.IsNullOrWhiteSpace(tableId) ||
            string.IsNullOrWhiteSpace(rowId) ||
            string.IsNullOrWhiteSpace(columnId))
        {
            return false;
        }

        var formulaContext = GetProjectFormulaContext();
        if (!formulaContext.TryGetTableById(tableId, out table))
        {
            return false;
        }

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var candidateColumn = table.Columns[columnIndex];
            if (string.Equals(candidateColumn.Id, columnId, StringComparison.Ordinal))
            {
                column = candidateColumn;
                break;
            }
        }

        if (column == null)
        {
            return false;
        }

        return formulaContext.TryGetRowById(table, rowId, out row);
    }

    private bool RevertNumberCellPreviewIfActive()
    {
        if (!EditState.IsEditing || !EditState.HasNumberPreviewValue)
        {
            return false;
        }

        if (!TryResolveActiveEditCell(out var table, out var row, out var column))
        {
            return false;
        }

        if (column.Kind != DocColumnKind.Number ||
            column.IsProjected ||
            !string.IsNullOrWhiteSpace(column.FormulaExpression))
        {
            return false;
        }

        double originalNumberValue = EditState.NumberPreviewOriginalValue;
        var currentCell = row.GetCell(column);
        if (Math.Abs(currentCell.NumberValue - originalNumberValue) < 0.0000001)
        {
            return false;
        }

        row.SetCell(column.Id, DocCellValue.Number(originalNumberValue));
        ApplyPreviewFormulaRefresh(table.Id, allowDeferredNonInteractiveRefresh: true);
        BumpLiveValueRevision();
        return true;
    }

    private static DocCommand CloneCommandForAutoSaveWorker(DocCommand sourceCommand)
    {
        return new DocCommand
        {
            Kind = sourceCommand.Kind,
            TableId = sourceCommand.TableId,
            RowId = sourceCommand.RowId,
            ColumnId = sourceCommand.ColumnId,
            TableVariantId = sourceCommand.TableVariantId,
            OldCellValue = sourceCommand.OldCellValue.Clone(),
            NewCellValue = sourceCommand.NewCellValue.Clone(),
            RowIndex = sourceCommand.RowIndex,
            TargetRowIndex = sourceCommand.TargetRowIndex,
            RowSnapshot = sourceCommand.RowSnapshot != null ? CloneRowSnapshot(sourceCommand.RowSnapshot) : null,
            RemovedVariantCellOverrides = sourceCommand.RemovedVariantCellOverrides != null
                ? CloneVariantCellOverrideList(sourceCommand.RemovedVariantCellOverrides)
                : null,
            ColumnIndex = sourceCommand.ColumnIndex,
            TargetColumnIndex = sourceCommand.TargetColumnIndex,
            ColumnSnapshot = sourceCommand.ColumnSnapshot != null ? CloneColumnSnapshot(sourceCommand.ColumnSnapshot) : null,
            ColumnCellSnapshots = CloneCellSnapshotMap(sourceCommand.ColumnCellSnapshots),
            TableIndex = sourceCommand.TableIndex,
            TableSnapshot = sourceCommand.TableSnapshot != null ? CloneTableSnapshot(sourceCommand.TableSnapshot) : null,
            OldName = sourceCommand.OldName,
            NewName = sourceCommand.NewName,
            OldColumnWidth = sourceCommand.OldColumnWidth,
            NewColumnWidth = sourceCommand.NewColumnWidth,
            OldFormulaExpression = sourceCommand.OldFormulaExpression,
            NewFormulaExpression = sourceCommand.NewFormulaExpression,
            OldPluginSettingsJson = sourceCommand.OldPluginSettingsJson,
            NewPluginSettingsJson = sourceCommand.NewPluginSettingsJson,
            OldRelationTableId = sourceCommand.OldRelationTableId,
            NewRelationTableId = sourceCommand.NewRelationTableId,
            OldRelationTargetMode = sourceCommand.OldRelationTargetMode,
            NewRelationTargetMode = sourceCommand.NewRelationTargetMode,
            OldRelationTableVariantId = sourceCommand.OldRelationTableVariantId,
            NewRelationTableVariantId = sourceCommand.NewRelationTableVariantId,
            OldRelationDisplayColumnId = sourceCommand.OldRelationDisplayColumnId,
            NewRelationDisplayColumnId = sourceCommand.NewRelationDisplayColumnId,
            OldOptionsSnapshot = CloneStringList(sourceCommand.OldOptionsSnapshot),
            NewOptionsSnapshot = CloneStringList(sourceCommand.NewOptionsSnapshot),
            OldModelPreviewSettings = sourceCommand.OldModelPreviewSettings?.Clone(),
            NewModelPreviewSettings = sourceCommand.NewModelPreviewSettings?.Clone(),
            OldHidden = sourceCommand.OldHidden,
            NewHidden = sourceCommand.NewHidden,
            OldExportIgnore = sourceCommand.OldExportIgnore,
            NewExportIgnore = sourceCommand.NewExportIgnore,
            OldExportType = sourceCommand.OldExportType,
            NewExportType = sourceCommand.NewExportType,
            OldNumberMin = sourceCommand.OldNumberMin,
            NewNumberMin = sourceCommand.NewNumberMin,
            OldNumberMax = sourceCommand.OldNumberMax,
            NewNumberMax = sourceCommand.NewNumberMax,
            OldNumberValuesByRowId = CloneNumberValueMap(sourceCommand.OldNumberValuesByRowId),
            NewNumberValuesByRowId = CloneNumberValueMap(sourceCommand.NewNumberValuesByRowId),
            OldExportEnumName = sourceCommand.OldExportEnumName,
            NewExportEnumName = sourceCommand.NewExportEnumName,
            OldSubtableDisplayRendererId = sourceCommand.OldSubtableDisplayRendererId,
            NewSubtableDisplayRendererId = sourceCommand.NewSubtableDisplayRendererId,
            OldSubtableDisplayCellWidth = sourceCommand.OldSubtableDisplayCellWidth,
            NewSubtableDisplayCellWidth = sourceCommand.NewSubtableDisplayCellWidth,
            OldSubtableDisplayCellHeight = sourceCommand.OldSubtableDisplayCellHeight,
            NewSubtableDisplayCellHeight = sourceCommand.NewSubtableDisplayCellHeight,
            OldSubtableDisplayPluginSettingsJson = sourceCommand.OldSubtableDisplayPluginSettingsJson,
            NewSubtableDisplayPluginSettingsJson = sourceCommand.NewSubtableDisplayPluginSettingsJson,
            OldSubtableDisplayPreviewQuality = sourceCommand.OldSubtableDisplayPreviewQuality,
            NewSubtableDisplayPreviewQuality = sourceCommand.NewSubtableDisplayPreviewQuality,
            OldExportConfigSnapshot = sourceCommand.OldExportConfigSnapshot?.Clone(),
            NewExportConfigSnapshot = sourceCommand.NewExportConfigSnapshot?.Clone(),
            OldKeysSnapshot = sourceCommand.OldKeysSnapshot?.Clone(),
            NewKeysSnapshot = sourceCommand.NewKeysSnapshot?.Clone(),
            OldDerivedConfig = sourceCommand.OldDerivedConfig?.Clone(),
            NewDerivedConfig = sourceCommand.NewDerivedConfig?.Clone(),
            OldSchemaSourceTableId = sourceCommand.OldSchemaSourceTableId,
            NewSchemaSourceTableId = sourceCommand.NewSchemaSourceTableId,
            OldInheritanceSourceTableId = sourceCommand.OldInheritanceSourceTableId,
            NewInheritanceSourceTableId = sourceCommand.NewInheritanceSourceTableId,
            OldBaseTableId = sourceCommand.OldBaseTableId,
            NewBaseTableId = sourceCommand.NewBaseTableId,
            StepIndex = sourceCommand.StepIndex,
            TargetStepIndex = sourceCommand.TargetStepIndex,
            StepSnapshot = sourceCommand.StepSnapshot?.Clone(),
            OldStepSnapshot = sourceCommand.OldStepSnapshot?.Clone(),
            ProjectionIndex = sourceCommand.ProjectionIndex,
            TargetProjectionIndex = sourceCommand.TargetProjectionIndex,
            ProjectionSnapshot = sourceCommand.ProjectionSnapshot?.Clone(),
            OldProjectionSnapshot = sourceCommand.OldProjectionSnapshot?.Clone(),
            ViewId = sourceCommand.ViewId,
            ViewIndex = sourceCommand.ViewIndex,
            ViewSnapshot = sourceCommand.ViewSnapshot?.Clone(),
            OldViewSnapshot = sourceCommand.OldViewSnapshot?.Clone(),
            TableVariantIndex = sourceCommand.TableVariantIndex,
            TableVariantSnapshot = sourceCommand.TableVariantSnapshot?.Clone(),
            TableVariableId = sourceCommand.TableVariableId,
            TableVariableIndex = sourceCommand.TableVariableIndex,
            TableVariableSnapshot = sourceCommand.TableVariableSnapshot?.Clone(),
            OldTableVariableExpression = sourceCommand.OldTableVariableExpression,
            NewTableVariableExpression = sourceCommand.NewTableVariableExpression,
            OldTableVariableKind = sourceCommand.OldTableVariableKind,
            NewTableVariableKind = sourceCommand.NewTableVariableKind,
            OldTableVariableTypeId = sourceCommand.OldTableVariableTypeId,
            NewTableVariableTypeId = sourceCommand.NewTableVariableTypeId,
            FolderId = sourceCommand.FolderId,
            FolderIndex = sourceCommand.FolderIndex,
            FolderSnapshot = sourceCommand.FolderSnapshot?.Clone(),
            OldParentFolderId = sourceCommand.OldParentFolderId,
            NewParentFolderId = sourceCommand.NewParentFolderId,
            OldFolderId = sourceCommand.OldFolderId,
            NewFolderId = sourceCommand.NewFolderId,
            DocumentId = sourceCommand.DocumentId,
            DocumentIndex = sourceCommand.DocumentIndex,
            DocumentSnapshot = sourceCommand.DocumentSnapshot != null ? CloneDocumentSnapshot(sourceCommand.DocumentSnapshot) : null,
            BlockId = sourceCommand.BlockId,
            BlockIndex = sourceCommand.BlockIndex,
            BlockSnapshot = sourceCommand.BlockSnapshot?.Clone(),
            OldBlockText = sourceCommand.OldBlockText,
            NewBlockText = sourceCommand.NewBlockText,
            OldSpans = CloneSpanList(sourceCommand.OldSpans),
            NewSpans = CloneSpanList(sourceCommand.NewSpans),
            OldTableId = sourceCommand.OldTableId,
            NewTableId = sourceCommand.NewTableId,
            OldBlockType = sourceCommand.OldBlockType,
            NewBlockType = sourceCommand.NewBlockType,
            OldChecked = sourceCommand.OldChecked,
            NewChecked = sourceCommand.NewChecked,
            SpanStart = sourceCommand.SpanStart,
            SpanLength = sourceCommand.SpanLength,
            SpanStyle = sourceCommand.SpanStyle,
            OldIndentLevel = sourceCommand.OldIndentLevel,
            NewIndentLevel = sourceCommand.NewIndentLevel,
            OldEmbeddedWidth = sourceCommand.OldEmbeddedWidth,
            NewEmbeddedWidth = sourceCommand.NewEmbeddedWidth,
            OldEmbeddedHeight = sourceCommand.OldEmbeddedHeight,
            NewEmbeddedHeight = sourceCommand.NewEmbeddedHeight,
            OldBlockTableVariantId = sourceCommand.OldBlockTableVariantId,
            NewBlockTableVariantId = sourceCommand.NewBlockTableVariantId,
            OldBlockTableVariableExpression = sourceCommand.OldBlockTableVariableExpression,
            NewBlockTableVariableExpression = sourceCommand.NewBlockTableVariableExpression,
            TargetBlockIndex = sourceCommand.TargetBlockIndex,
            OldProjectSnapshot = sourceCommand.OldProjectSnapshot != null ? CloneProjectSnapshot(sourceCommand.OldProjectSnapshot) : null,
            NewProjectSnapshot = sourceCommand.NewProjectSnapshot != null ? CloneProjectSnapshot(sourceCommand.NewProjectSnapshot) : null,
        };
    }

    private static List<DocTableCellOverride> CloneVariantCellOverrideList(List<DocTableCellOverride> sourceOverrides)
    {
        var cloned = new List<DocTableCellOverride>(sourceOverrides.Count);
        for (int overrideIndex = 0; overrideIndex < sourceOverrides.Count; overrideIndex++)
        {
            cloned.Add(sourceOverrides[overrideIndex].Clone());
        }

        return cloned;
    }

    private static Dictionary<string, DocCellValue>? CloneCellSnapshotMap(Dictionary<string, DocCellValue>? sourceMap)
    {
        if (sourceMap == null)
        {
            return null;
        }

        var clonedMap = new Dictionary<string, DocCellValue>(sourceMap.Count, StringComparer.Ordinal);
        foreach (var mapEntry in sourceMap)
        {
            clonedMap[mapEntry.Key] = mapEntry.Value.Clone();
        }

        return clonedMap;
    }

    private static Dictionary<string, double>? CloneNumberValueMap(Dictionary<string, double>? sourceMap)
    {
        if (sourceMap == null)
        {
            return null;
        }

        var clonedMap = new Dictionary<string, double>(sourceMap.Count, StringComparer.Ordinal);
        foreach (var mapEntry in sourceMap)
        {
            clonedMap[mapEntry.Key] = mapEntry.Value;
        }

        return clonedMap;
    }

    private static List<string>? CloneStringList(List<string>? sourceList)
    {
        if (sourceList == null)
        {
            return null;
        }

        return new List<string>(sourceList);
    }

    private static List<RichSpan>? CloneSpanList(List<RichSpan>? sourceList)
    {
        if (sourceList == null)
        {
            return null;
        }

        return new List<RichSpan>(sourceList);
    }

    private static DocProject CloneProjectSnapshot(DocProject sourceProject)
    {
        var clonedProject = new DocProject
        {
            Name = sourceProject.Name,
            UiState = CloneProjectUiState(sourceProject.UiState),
            PluginSettingsByKey = new Dictionary<string, string>(sourceProject.PluginSettingsByKey, StringComparer.Ordinal),
            Folders = new List<DocFolder>(sourceProject.Folders.Count),
            Tables = new List<DocTable>(sourceProject.Tables.Count),
            Documents = new List<DocDocument>(sourceProject.Documents.Count),
        };

        for (int folderIndex = 0; folderIndex < sourceProject.Folders.Count; folderIndex++)
        {
            clonedProject.Folders.Add(sourceProject.Folders[folderIndex].Clone());
        }

        for (int tableIndex = 0; tableIndex < sourceProject.Tables.Count; tableIndex++)
        {
            clonedProject.Tables.Add(CloneTableSnapshot(sourceProject.Tables[tableIndex]));
        }

        for (int documentIndex = 0; documentIndex < sourceProject.Documents.Count; documentIndex++)
        {
            clonedProject.Documents.Add(CloneDocumentSnapshot(sourceProject.Documents[documentIndex]));
        }

        return clonedProject;
    }

    private static DocProjectUiState CloneProjectUiState(DocProjectUiState sourceUiState)
    {
        return new DocProjectUiState
        {
            TableFolderExpandedById = new Dictionary<string, bool>(sourceUiState.TableFolderExpandedById, StringComparer.Ordinal),
            DocumentFolderExpandedById = new Dictionary<string, bool>(sourceUiState.DocumentFolderExpandedById, StringComparer.Ordinal),
        };
    }

    private static DocTable CloneTableSnapshot(DocTable sourceTable)
    {
        var clonedTable = new DocTable
        {
            Id = sourceTable.Id,
            Name = sourceTable.Name,
            FolderId = sourceTable.FolderId,
            FileName = sourceTable.FileName,
            SchemaSourceTableId = sourceTable.SchemaSourceTableId,
            InheritanceSourceTableId = sourceTable.InheritanceSourceTableId,
            SystemKey = sourceTable.SystemKey,
            IsSystemSchemaLocked = sourceTable.IsSystemSchemaLocked,
            IsSystemDataLocked = sourceTable.IsSystemDataLocked,
            DerivedConfig = sourceTable.DerivedConfig?.Clone(),
            ExportConfig = sourceTable.ExportConfig?.Clone(),
            Keys = sourceTable.Keys.Clone(),
            ParentTableId = sourceTable.ParentTableId,
            ParentRowColumnId = sourceTable.ParentRowColumnId,
            PluginTableTypeId = sourceTable.PluginTableTypeId,
            PluginOwnerColumnTypeId = sourceTable.PluginOwnerColumnTypeId,
            IsPluginSchemaLocked = sourceTable.IsPluginSchemaLocked,
            Columns = new List<DocColumn>(sourceTable.Columns.Count),
            Variants = new List<DocTableVariant>(sourceTable.Variants.Count),
            Rows = new List<DocRow>(sourceTable.Rows.Count),
            Views = new List<DocView>(sourceTable.Views.Count),
            Variables = new List<DocTableVariable>(sourceTable.Variables.Count),
            VariantDeltas = new List<DocTableVariantDelta>(sourceTable.VariantDeltas.Count),
        };

        for (int columnIndex = 0; columnIndex < sourceTable.Columns.Count; columnIndex++)
        {
            clonedTable.Columns.Add(CloneColumnSnapshot(sourceTable.Columns[columnIndex]));
        }

        for (int rowIndex = 0; rowIndex < sourceTable.Rows.Count; rowIndex++)
        {
            clonedTable.Rows.Add(CloneRowSnapshot(sourceTable.Rows[rowIndex]));
        }

        for (int viewIndex = 0; viewIndex < sourceTable.Views.Count; viewIndex++)
        {
            clonedTable.Views.Add(sourceTable.Views[viewIndex].Clone());
        }

        for (int variableIndex = 0; variableIndex < sourceTable.Variables.Count; variableIndex++)
        {
            clonedTable.Variables.Add(sourceTable.Variables[variableIndex].Clone());
        }

        for (int variantIndex = 0; variantIndex < sourceTable.Variants.Count; variantIndex++)
        {
            clonedTable.Variants.Add(sourceTable.Variants[variantIndex].Clone());
        }

        for (int variantDeltaIndex = 0; variantDeltaIndex < sourceTable.VariantDeltas.Count; variantDeltaIndex++)
        {
            clonedTable.VariantDeltas.Add(sourceTable.VariantDeltas[variantDeltaIndex].Clone());
        }

        return clonedTable;
    }

    private static DocColumn CloneColumnSnapshot(DocColumn sourceColumn)
    {
        return new DocColumn
        {
            Id = sourceColumn.Id,
            Name = sourceColumn.Name,
            Kind = sourceColumn.Kind,
            ColumnTypeId = sourceColumn.ColumnTypeId,
            PluginSettingsJson = sourceColumn.PluginSettingsJson,
            Width = sourceColumn.Width,
            Options = sourceColumn.Options != null ? new List<string>(sourceColumn.Options) : null,
            FormulaExpression = sourceColumn.FormulaExpression,
            RelationTableId = sourceColumn.RelationTableId,
            TableRefBaseTableId = sourceColumn.TableRefBaseTableId,
            RowRefTableRefColumnId = sourceColumn.RowRefTableRefColumnId,
            RelationTargetMode = sourceColumn.RelationTargetMode,
            RelationTableVariantId = sourceColumn.RelationTableVariantId,
            RelationDisplayColumnId = sourceColumn.RelationDisplayColumnId,
            IsHidden = sourceColumn.IsHidden,
            IsProjected = sourceColumn.IsProjected,
            IsInherited = sourceColumn.IsInherited,
            ExportType = sourceColumn.ExportType,
            NumberMin = sourceColumn.NumberMin,
            NumberMax = sourceColumn.NumberMax,
            ExportEnumName = sourceColumn.ExportEnumName,
            ExportIgnore = sourceColumn.ExportIgnore,
            SubtableId = sourceColumn.SubtableId,
            SubtableDisplayRendererId = sourceColumn.SubtableDisplayRendererId,
            SubtableDisplayCellWidth = sourceColumn.SubtableDisplayCellWidth,
            SubtableDisplayCellHeight = sourceColumn.SubtableDisplayCellHeight,
            SubtableDisplayPreviewQuality = sourceColumn.SubtableDisplayPreviewQuality,
            FormulaEvalScopes = sourceColumn.FormulaEvalScopes,
            ModelPreviewSettings = sourceColumn.ModelPreviewSettings?.Clone(),
        };
    }

    private static DocRow CloneRowSnapshot(DocRow sourceRow)
    {
        var clonedRow = new DocRow
        {
            Id = sourceRow.Id,
            Cells = new Dictionary<string, DocCellValue>(sourceRow.Cells.Count),
        };

        foreach (var cellEntry in sourceRow.Cells)
        {
            clonedRow.Cells[cellEntry.Key] = cellEntry.Value.Clone();
        }

        return clonedRow;
    }

    private static DocDocument CloneDocumentSnapshot(DocDocument sourceDocument)
    {
        var clonedDocument = new DocDocument
        {
            Id = sourceDocument.Id,
            Title = sourceDocument.Title,
            FolderId = sourceDocument.FolderId,
            FileName = sourceDocument.FileName,
            Blocks = new List<DocBlock>(sourceDocument.Blocks.Count),
        };

        for (int blockIndex = 0; blockIndex < sourceDocument.Blocks.Count; blockIndex++)
        {
            clonedDocument.Blocks.Add(sourceDocument.Blocks[blockIndex].Clone());
        }

        return clonedDocument;
    }

    private static DocProject CreateDefaultProject()
    {
        var project = new DocProject { Name = "Sample Project" };

        // Tasks table
        var tasksTable = new DocTable { Name = "Tasks", FileName = "tasks" };

        var nameCol = new DocColumn { Name = "Name", Kind = DocColumnKind.Text, Width = 200 };
        var statusCol = new DocColumn
        {
            Name = "Status", Kind = DocColumnKind.Select, Width = 120,
            Options = new List<string> { "Todo", "In Progress", "Done" }
        };
        var priorityCol = new DocColumn { Name = "Priority", Kind = DocColumnKind.Number, Width = 80 };
        var doneCol = new DocColumn { Name = "Done", Kind = DocColumnKind.Checkbox, Width = 60 };
        var scoreCol = new DocColumn
        {
            Name = "Score",
            Kind = DocColumnKind.Number,
            Width = 100,
            FormulaExpression = "thisRow.Done ? 0 : (10 - thisRow.Priority)"
        };

        tasksTable.Columns.AddRange([nameCol, statusCol, priorityCol, doneCol, scoreCol]);

        // Sample rows
        AddRow(tasksTable, nameCol, "Design data model", statusCol, "Done", priorityCol, 1, doneCol, true);
        AddRow(tasksTable, nameCol, "Build table editor", statusCol, "In Progress", priorityCol, 2, doneCol, false);
        AddRow(tasksTable, nameCol, "Add persistence", statusCol, "Todo", priorityCol, 3, doneCol, false);
        AddRow(tasksTable, nameCol, "Implement undo/redo", statusCol, "Todo", priorityCol, 4, doneCol, false);
        AddRow(tasksTable, nameCol, "Write tests", statusCol, "Todo", priorityCol, 5, doneCol, false);

        project.Tables.Add(tasksTable);

        // People table
        var peopleTable = new DocTable { Name = "People", FileName = "people" };
        var pNameCol = new DocColumn { Name = "Name", Kind = DocColumnKind.Text, Width = 180 };
        var roleCol = new DocColumn
        {
            Name = "Role", Kind = DocColumnKind.Select, Width = 140,
            Options = new List<string> { "Engineer", "Designer", "PM", "QA" }
        };
        var activeCol = new DocColumn { Name = "Active", Kind = DocColumnKind.Checkbox, Width = 60 };

        peopleTable.Columns.AddRange([pNameCol, roleCol, activeCol]);

        AddRow(peopleTable, pNameCol, "Alice", roleCol, "Engineer", activeCol, true);
        AddRow(peopleTable, pNameCol, "Bob", roleCol, "Designer", activeCol, true);
        AddRow(peopleTable, pNameCol, "Charlie", roleCol, "PM", activeCol, false);

        project.Tables.Add(peopleTable);

        // Sample document
        var doc = new DocDocument
        {
            Title = "Getting Started",
            FileName = "getting-started",
        };

        doc.Blocks.Add(new DocBlock
        {
            Type = DocBlockType.Heading1,
            Order = "a0",
            Text = new RichText { PlainText = "Getting Started" },
        });
        doc.Blocks.Add(new DocBlock
        {
            Type = DocBlockType.Paragraph,
            Order = "a1",
            Text = new RichText { PlainText = "Welcome to Derp.Doc, a block-based document editor." },
        });
        doc.Blocks.Add(new DocBlock
        {
            Type = DocBlockType.Heading2,
            Order = "a2",
            Text = new RichText { PlainText = "Features" },
        });
        doc.Blocks.Add(new DocBlock
        {
            Type = DocBlockType.BulletList,
            Order = "a3",
            Text = new RichText { PlainText = "Rich text formatting" },
        });
        doc.Blocks.Add(new DocBlock
        {
            Type = DocBlockType.BulletList,
            Order = "a4",
            Text = new RichText { PlainText = "Multiple block types" },
        });
        doc.Blocks.Add(new DocBlock
        {
            Type = DocBlockType.BulletList,
            Order = "a5",
            Text = new RichText { PlainText = "Undo and redo" },
        });
        doc.Blocks.Add(new DocBlock
        {
            Type = DocBlockType.Paragraph,
            Order = "a6",
            Text = new RichText { PlainText = "" },
        });

        project.Documents.Add(doc);

        return project;
    }

    private static void AddRow(DocTable table, DocColumn c1, string v1, DocColumn c2, string v2, DocColumn c3, double v3, DocColumn c4, bool v4)
    {
        var row = new DocRow();
        row.SetCell(c1.Id, DocCellValue.Text(v1));
        row.SetCell(c2.Id, DocCellValue.Text(v2));
        row.SetCell(c3.Id, DocCellValue.Number(v3));
        row.SetCell(c4.Id, DocCellValue.Bool(v4));
        table.Rows.Add(row);
    }

    private static void AddRow(DocTable table, DocColumn c1, string v1, DocColumn c2, string v2, DocColumn c3, bool v3)
    {
        var row = new DocRow();
        row.SetCell(c1.Id, DocCellValue.Text(v1));
        row.SetCell(c2.Id, DocCellValue.Text(v2));
        row.SetCell(c3.Id, DocCellValue.Bool(v3));
        table.Rows.Add(row);
    }
}
