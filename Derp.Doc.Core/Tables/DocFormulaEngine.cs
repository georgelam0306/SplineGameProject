using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using Derp.Doc.Model;

namespace Derp.Doc.Tables;

internal sealed class DocFormulaEngine
{
    private const string FormulaErrorText = "#ERR";
    private const string TableNodePrefix = "table:";
    private const string DocumentVariableNodePrefix = "docvar:";
    private const string NodeGraphEdgesColumnName = "Edges";
    private const string NodeGraphFromNodeColumnName = "FromNode";
    private const string NodeGraphFromPinColumnName = "FromPinId";
    private const string NodeGraphToNodeColumnName = "ToNode";
    private const string NodeGraphToPinColumnName = "ToPinId";

    private static readonly HashSet<string> BuiltInFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Lookup",
        "CountIf",
        "SumIf",
        "If",
        "Abs",
        "Pow",
        "Exp",
        "EvalSpline",
        "Upper",
        "Lower",
        "Contains",
        "Concat",
        "Date",
        "Today",
        "AddDays",
        "DaysBetween",
        "Vec2",
        "Vec3",
        "Vec4",
        "Color",
    };

    /// <summary>
    /// Cached materialization results for derived tables, keyed by table ID.
    /// Refreshed each EvaluateProject call.
    /// </summary>
    public Dictionary<string, DerivedMaterializeResult> DerivedResults { get; } = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Dictionary<string, CompiledFormula>> _compiledByTableId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CompiledCellFormula>> _compiledCellFormulasByTableId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, List<CompiledCellFormula>>> _compiledCellFormulasByTableAndRowId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, CompiledFormula>> _compiledTableVariablesByTableId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, CompiledFormula>> _compiledDocumentVariablesByDocumentId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<DocColumn>> _orderedFormulaColumnsByTableId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, DocumentVariableEvaluationState>> _documentVariableValuesByDocumentId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, CompiledFormula> _compiledTableExpressionCacheByExpression = new(StringComparer.Ordinal);
    private FormulaDependencyPlan? _formulaDependencyPlan;
    private DocProject? _cachedProjectReference;
    private ProjectFormulaContext? _cachedFormulaContext;

    public readonly struct EvaluationRequest
    {
        public bool EnableIncremental { get; init; }
        public bool RequiresStructuralRefresh { get; init; }
        public bool RefreshDirtyTableIndexes { get; init; }
        public IReadOnlyList<string>? DirtyTableIds { get; init; }
        public IReadOnlyList<string>? DirtyDocumentIds { get; init; }
        public IReadOnlyDictionary<string, List<string>>? TargetFormulaColumnIdsByTableId { get; init; }

        public static EvaluationRequest Full()
        {
            return new EvaluationRequest
            {
                EnableIncremental = false,
                RequiresStructuralRefresh = true,
                RefreshDirtyTableIndexes = true,
                DirtyTableIds = null,
                DirtyDocumentIds = null,
                TargetFormulaColumnIdsByTableId = null,
            };
        }

        public static EvaluationRequest Incremental(
            IReadOnlyList<string> dirtyTableIds,
            bool refreshDirtyTableIndexes = true)
        {
            return new EvaluationRequest
            {
                EnableIncremental = true,
                RequiresStructuralRefresh = false,
                RefreshDirtyTableIndexes = refreshDirtyTableIndexes,
                DirtyTableIds = dirtyTableIds,
                DirtyDocumentIds = null,
                TargetFormulaColumnIdsByTableId = null,
            };
        }

        public static EvaluationRequest IncrementalDocuments(
            IReadOnlyList<string> dirtyDocumentIds)
        {
            return new EvaluationRequest
            {
                EnableIncremental = true,
                RequiresStructuralRefresh = false,
                RefreshDirtyTableIndexes = false,
                DirtyTableIds = null,
                DirtyDocumentIds = dirtyDocumentIds,
                TargetFormulaColumnIdsByTableId = null,
            };
        }

        public static EvaluationRequest IncrementalTargeted(
            IReadOnlyList<string> dirtyTableIds,
            IReadOnlyDictionary<string, List<string>> targetFormulaColumnIdsByTableId,
            bool refreshDirtyTableIndexes = true)
        {
            return new EvaluationRequest
            {
                EnableIncremental = true,
                RequiresStructuralRefresh = false,
                RefreshDirtyTableIndexes = refreshDirtyTableIndexes,
                DirtyTableIds = dirtyTableIds,
                DirtyDocumentIds = null,
                TargetFormulaColumnIdsByTableId = targetFormulaColumnIdsByTableId,
            };
        }

        public static EvaluationRequest StructuralIncremental(
            IReadOnlyList<string>? dirtyTableIds,
            IReadOnlyList<string>? dirtyDocumentIds,
            bool refreshDirtyTableIndexes = true,
            IReadOnlyDictionary<string, List<string>>? targetFormulaColumnIdsByTableId = null)
        {
            return new EvaluationRequest
            {
                EnableIncremental = true,
                RequiresStructuralRefresh = true,
                RefreshDirtyTableIndexes = refreshDirtyTableIndexes,
                DirtyTableIds = dirtyTableIds,
                DirtyDocumentIds = dirtyDocumentIds,
                TargetFormulaColumnIdsByTableId = targetFormulaColumnIdsByTableId,
            };
        }
    }

    public readonly struct EvaluationMetrics
    {
        public long TotalTicks { get; init; }
        public long CompileTicks { get; init; }
        public long PlanTicks { get; init; }
        public long DerivedTicks { get; init; }
        public long EvaluateTicks { get; init; }
        public int EvaluatedTableCount { get; init; }
        public bool UsedIncrementalPlan { get; init; }
    }

    private readonly struct DocumentVariableEvaluationState
    {
        public DocumentVariableEvaluationState(FormulaValue value, bool hasError)
        {
            Value = value;
            HasError = hasError;
        }

        public FormulaValue Value { get; }
        public bool HasError { get; }
    }

    private sealed class DocumentVariableNode
    {
        public required string NodeId { get; init; }
        public required string DocumentId { get; init; }
        public required string VariableName { get; init; }
        public required CompiledFormula CompiledFormula { get; init; }
    }

    private sealed class CompiledCellFormula
    {
        public required string RowId { get; init; }
        public required string ColumnId { get; init; }
        public required string FormulaExpression { get; init; }
        public required CompiledFormula CompiledFormula { get; init; }
    }

    private sealed class FormulaDependencyPlan
    {
        public required List<string> OrderedNodeIds { get; init; }
        public required Dictionary<string, List<string>> DependentsByNodeId { get; init; }
        public required Dictionary<string, List<string>> DependenciesByNodeId { get; init; }
        public required HashSet<string> TableNodeIds { get; init; }
        public required Dictionary<string, DocumentVariableNode> DocumentVariableNodesByNodeId { get; init; }
        public required Dictionary<string, List<string>> DocumentVariableNodeIdsByDocumentId { get; init; }
    }

    public void EvaluateProject(DocProject project)
    {
        EvaluateProject(project, EvaluationRequest.Full());
    }

    public EvaluationMetrics EvaluateProject(DocProject project, in EvaluationRequest request)
    {
        SchemaLinkedTableSynchronizer.Synchronize(project);

        long totalStart = Stopwatch.GetTimestamp();
        long compileTicks = 0;
        long planTicks = 0;
        long derivedTicks = 0;
        long evaluateTicks = 0;
        int evaluatedTableCount = 0;

        bool projectReferenceChanged = !ReferenceEquals(_cachedProjectReference, project);
        if (projectReferenceChanged)
        {
            ClearEvaluationCaches();
            _cachedProjectReference = project;
            _cachedFormulaContext = new ProjectFormulaContext(project);
        }
        else if (_cachedFormulaContext == null)
        {
            _cachedFormulaContext = new ProjectFormulaContext(project);
        }

        var formulaContext = _cachedFormulaContext;
        if (formulaContext == null)
        {
            throw new InvalidOperationException("Formula context was not initialized.");
        }

        if (request.RequiresStructuralRefresh)
        {
            // Structural mutations (add/remove/rename tables/columns) can invalidate table id/name lookups.
            // Rebuild the context so full evaluations always reflect the current project graph.
            formulaContext = new ProjectFormulaContext(project);
            _cachedFormulaContext = formulaContext;
        }
        else if (request.RefreshDirtyTableIndexes && request.DirtyTableIds != null)
        {
            RefreshDirtyTableIndexes(formulaContext, request.DirtyTableIds);
        }

        bool hasDirtyTables = request.DirtyTableIds != null && request.DirtyTableIds.Count > 0;
        bool hasDirtyDocuments = request.DirtyDocumentIds != null && request.DirtyDocumentIds.Count > 0;
        bool canEvaluateIncrementally = request.EnableIncremental &&
                                        (hasDirtyTables || hasDirtyDocuments);

        bool needsPlanRebuild = request.RequiresStructuralRefresh || _formulaDependencyPlan == null;
        if (needsPlanRebuild)
        {
            RebuildEvaluationPlan(project, formulaContext, ref compileTicks, ref planTicks);
        }

        if (!canEvaluateIncrementally)
        {
            DerivedResults.Clear();
            EvaluateNodes(
                formulaContext,
                _formulaDependencyPlan!,
                affectedNodeIds: null,
                affectedTableIds: null,
                isIncremental: false,
                forceDerivedSchemaSync: false,
                targetFormulaColumnIdsByTableId: null,
                ref derivedTicks,
                ref evaluateTicks,
                ref evaluatedTableCount);

            return new EvaluationMetrics
            {
                TotalTicks = Stopwatch.GetTimestamp() - totalStart,
                CompileTicks = compileTicks,
                PlanTicks = planTicks,
                DerivedTicks = derivedTicks,
                EvaluateTicks = evaluateTicks,
                EvaluatedTableCount = evaluatedTableCount,
                UsedIncrementalPlan = false,
            };
        }

        HashSet<string>? oldAffectedNodeIds = null;
        if (hasDirtyDocuments &&
            _formulaDependencyPlan != null &&
            !request.RequiresStructuralRefresh)
        {
            var oldDirtyNodeIds = BuildDirtyNodeIdSet(request, _formulaDependencyPlan);
            if (oldDirtyNodeIds.Count > 0)
            {
                oldAffectedNodeIds = BuildAffectedNodeIds(oldDirtyNodeIds, _formulaDependencyPlan.DependentsByNodeId);
            }

            RebuildDocumentVariableEvaluationPlan(project, formulaContext, ref compileTicks, ref planTicks);
        }

        var dirtyNodeIds = BuildDirtyNodeIdSet(request, _formulaDependencyPlan!);
        if (dirtyNodeIds.Count == 0 && (oldAffectedNodeIds == null || oldAffectedNodeIds.Count == 0))
        {
            return new EvaluationMetrics
            {
                TotalTicks = Stopwatch.GetTimestamp() - totalStart,
                CompileTicks = compileTicks,
                PlanTicks = planTicks,
                DerivedTicks = 0,
                EvaluateTicks = 0,
                EvaluatedTableCount = 0,
                UsedIncrementalPlan = true,
            };
        }

        var affectedNodeIds = BuildAffectedNodeIds(dirtyNodeIds, _formulaDependencyPlan!.DependentsByNodeId);
        MergeOldAffectedNodesIntoCurrentPlan(
            affectedNodeIds,
            oldAffectedNodeIds,
            _formulaDependencyPlan.DependentsByNodeId);
        var affectedTableIds = BuildAffectedTableIdsFromNodes(affectedNodeIds, _formulaDependencyPlan.TableNodeIds);
        ClearDocumentVariableValuesForDirtyDocuments(request.DirtyDocumentIds);

        EvaluateNodes(
            formulaContext,
            _formulaDependencyPlan!,
            affectedNodeIds,
            affectedTableIds,
            isIncremental: true,
            forceDerivedSchemaSync: request.RequiresStructuralRefresh,
            targetFormulaColumnIdsByTableId: request.TargetFormulaColumnIdsByTableId,
            ref derivedTicks,
            ref evaluateTicks,
            ref evaluatedTableCount);

        return new EvaluationMetrics
        {
            TotalTicks = Stopwatch.GetTimestamp() - totalStart,
            CompileTicks = compileTicks,
            PlanTicks = planTicks,
            DerivedTicks = derivedTicks,
            EvaluateTicks = evaluateTicks,
            EvaluatedTableCount = evaluatedTableCount,
            UsedIncrementalPlan = true,
        };
    }

    private void EvaluateNodes(
        ProjectFormulaContext formulaContext,
        FormulaDependencyPlan dependencyPlan,
        HashSet<string>? affectedNodeIds,
        HashSet<string>? affectedTableIds,
        bool isIncremental,
        bool forceDerivedSchemaSync,
        IReadOnlyDictionary<string, List<string>>? targetFormulaColumnIdsByTableId,
        ref long derivedTicks,
        ref long evaluateTicks,
        ref int evaluatedTableCount)
    {
        if (affectedNodeIds == null)
        {
            _documentVariableValuesByDocumentId.Clear();
        }

        var evaluator = new FormulaEvaluator(formulaContext, _documentVariableValuesByDocumentId);
        for (int nodeIndex = 0; nodeIndex < dependencyPlan.OrderedNodeIds.Count; nodeIndex++)
        {
            string nodeId = dependencyPlan.OrderedNodeIds[nodeIndex];
            if (affectedNodeIds != null && !affectedNodeIds.Contains(nodeId))
            {
                continue;
            }

            if (dependencyPlan.DocumentVariableNodesByNodeId.TryGetValue(nodeId, out var documentVariableNode))
            {
                EvaluateDocumentVariableNode(documentVariableNode, formulaContext, evaluator);
                continue;
            }

            if (!TryGetTableIdFromNodeId(nodeId, out string tableId))
            {
                continue;
            }

            if (!formulaContext.TryGetTableById(tableId, out var table))
            {
                continue;
            }

            List<string>? targetFormulaColumnIds = null;
            if (targetFormulaColumnIdsByTableId != null &&
                !targetFormulaColumnIdsByTableId.TryGetValue(table.Id, out targetFormulaColumnIds))
            {
                continue;
            }

            if (table.IsDerived && ShouldMaterializeDerivedTable(tableId, affectedTableIds, isIncremental))
            {
                long derivedStart = Stopwatch.GetTimestamp();
                if (!isIncremental || forceDerivedSchemaSync)
                {
                    // Default UX: derived tables project all source columns by default.
                    // Users can remove projections, which is preserved via SuppressedProjections.
                    SyncDerivedTableSchema(table, formulaContext);
                }

                MaterializeDerivedTable(table, formulaContext);
                formulaContext.RefreshTableIndexes(table);
                derivedTicks += Stopwatch.GetTimestamp() - derivedStart;
            }

            bool hasCompiledColumns = _compiledByTableId.TryGetValue(table.Id, out var compiledColumns);
            bool hasCompiledCellFormulas = _compiledCellFormulasByTableAndRowId.TryGetValue(table.Id, out var compiledCellFormulasByRowId);
            if (!hasCompiledColumns && !hasCompiledCellFormulas)
            {
                continue;
            }

            long evaluateStart = Stopwatch.GetTimestamp();
            bool forceOrderRebuild = !isIncremental && table.IsDerived;
            List<DocColumn>? orderedFormulaColumns = null;
            if (hasCompiledColumns && compiledColumns != null)
            {
                orderedFormulaColumns = GetOrBuildOrderedFormulaColumns(table, compiledColumns, forceOrderRebuild);
            }

            EvaluateTable(
                table,
                compiledColumns,
                orderedFormulaColumns,
                compiledCellFormulasByRowId,
                formulaContext,
                evaluator,
                targetFormulaColumnIds);
            evaluateTicks += Stopwatch.GetTimestamp() - evaluateStart;
            evaluatedTableCount++;
        }
    }

    private bool ShouldMaterializeDerivedTable(string tableId, HashSet<string>? affectedTableIds, bool isIncremental)
    {
        if (!isIncremental || _formulaDependencyPlan == null || affectedTableIds == null)
        {
            return true;
        }

        if (affectedTableIds.Contains(tableId))
        {
            return true;
        }

        string tableNodeId = CreateTableNodeId(tableId);
        if (!_formulaDependencyPlan.DependenciesByNodeId.TryGetValue(tableNodeId, out var dependencyNodeIds))
        {
            return true;
        }

        for (int dependencyIndex = 0; dependencyIndex < dependencyNodeIds.Count; dependencyIndex++)
        {
            string dependencyNodeId = dependencyNodeIds[dependencyIndex];
            if (!TryGetTableIdFromNodeId(dependencyNodeId, out string dependencyTableId))
            {
                continue;
            }

            if (affectedTableIds.Contains(dependencyTableId))
            {
                return true;
            }
        }

        return false;
    }

    private List<DocColumn> GetOrBuildOrderedFormulaColumns(
        DocTable table,
        Dictionary<string, CompiledFormula> compiledColumns,
        bool forceRebuild)
    {
        if (!forceRebuild &&
            _orderedFormulaColumnsByTableId.TryGetValue(table.Id, out var cachedColumns))
        {
            return cachedColumns;
        }

        var orderedColumns = BuildFormulaColumnEvaluationOrder(table, compiledColumns);
        _orderedFormulaColumnsByTableId[table.Id] = orderedColumns;
        return orderedColumns;
    }

    private void RebuildEvaluationPlan(
        DocProject project,
        ProjectFormulaContext formulaContext,
        ref long compileTicks,
        ref long planTicks)
    {
        long compileStart = Stopwatch.GetTimestamp();
        _compiledByTableId.Clear();
        _compiledCellFormulasByTableId.Clear();
        _compiledCellFormulasByTableAndRowId.Clear();
        _compiledTableVariablesByTableId.Clear();
        _compiledDocumentVariablesByDocumentId.Clear();
        CompileProjectFormulas(project, _compiledByTableId);
        CompileProjectCellFormulas(
            project,
            _compiledCellFormulasByTableId,
            _compiledCellFormulasByTableAndRowId);
        CompileProjectTableVariableFormulas(project, _compiledTableVariablesByTableId);
        var compiledDocumentVariableNodes = CompileProjectDocumentVariableFormulas(project, _compiledDocumentVariablesByDocumentId);
        compileTicks += Stopwatch.GetTimestamp() - compileStart;

        long planStart = Stopwatch.GetTimestamp();
        _formulaDependencyPlan = BuildFormulaDependencyPlan(
            project,
            formulaContext,
            _compiledByTableId,
            _compiledCellFormulasByTableId,
            _compiledTableVariablesByTableId,
            compiledDocumentVariableNodes,
            _compiledDocumentVariablesByDocumentId);
        planTicks += Stopwatch.GetTimestamp() - planStart;

        _orderedFormulaColumnsByTableId.Clear();
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var table = project.Tables[tableIndex];
            if (!_compiledByTableId.TryGetValue(table.Id, out var compiledColumns))
            {
                continue;
            }

            _orderedFormulaColumnsByTableId[table.Id] = BuildFormulaColumnEvaluationOrder(table, compiledColumns);
        }
    }

    private void RebuildDocumentVariableEvaluationPlan(
        DocProject project,
        ProjectFormulaContext formulaContext,
        ref long compileTicks,
        ref long planTicks)
    {
        long compileStart = Stopwatch.GetTimestamp();
        _compiledDocumentVariablesByDocumentId.Clear();
        var compiledDocumentVariableNodes = CompileProjectDocumentVariableFormulas(project, _compiledDocumentVariablesByDocumentId);
        compileTicks += Stopwatch.GetTimestamp() - compileStart;

        long planStart = Stopwatch.GetTimestamp();
        _formulaDependencyPlan = BuildFormulaDependencyPlan(
            project,
            formulaContext,
            _compiledByTableId,
            _compiledCellFormulasByTableId,
            _compiledTableVariablesByTableId,
            compiledDocumentVariableNodes,
            _compiledDocumentVariablesByDocumentId);
        planTicks += Stopwatch.GetTimestamp() - planStart;
    }

    private void ClearEvaluationCaches()
    {
        _compiledByTableId.Clear();
        _compiledCellFormulasByTableId.Clear();
        _compiledCellFormulasByTableAndRowId.Clear();
        _compiledTableVariablesByTableId.Clear();
        _compiledDocumentVariablesByDocumentId.Clear();
        _orderedFormulaColumnsByTableId.Clear();
        _documentVariableValuesByDocumentId.Clear();
        _compiledTableExpressionCacheByExpression.Clear();
        _formulaDependencyPlan = null;
        _cachedFormulaContext = null;
    }

    private void ClearDocumentVariableValuesForDirtyDocuments(IReadOnlyList<string>? dirtyDocumentIds)
    {
        if (dirtyDocumentIds == null)
        {
            return;
        }

        for (int documentIndex = 0; documentIndex < dirtyDocumentIds.Count; documentIndex++)
        {
            string documentId = dirtyDocumentIds[documentIndex];
            if (string.IsNullOrWhiteSpace(documentId))
            {
                continue;
            }

            _documentVariableValuesByDocumentId.Remove(documentId);
        }
    }

    private void EvaluateDocumentVariableNode(
        DocumentVariableNode documentVariableNode,
        IFormulaContext formulaContext,
        FormulaEvaluator evaluator)
    {
        if (!formulaContext.TryGetDocumentById(documentVariableNode.DocumentId, out var document))
        {
            SetDocumentVariableEvaluationState(
                documentVariableNode.DocumentId,
                documentVariableNode.VariableName,
                value: FormulaValue.Null(),
                hasError: true);
            return;
        }

        if (!documentVariableNode.CompiledFormula.IsValid)
        {
            SetDocumentVariableEvaluationState(
                documentVariableNode.DocumentId,
                documentVariableNode.VariableName,
                value: FormulaValue.Null(),
                hasError: true);
            return;
        }

        try
        {
            var frame = CreateDocumentEvaluationFrame(document);
            FormulaValue value = evaluator.Evaluate(documentVariableNode.CompiledFormula.Root, frame);
            SetDocumentVariableEvaluationState(
                documentVariableNode.DocumentId,
                documentVariableNode.VariableName,
                value,
                hasError: false);
        }
        catch
        {
            SetDocumentVariableEvaluationState(
                documentVariableNode.DocumentId,
                documentVariableNode.VariableName,
                value: FormulaValue.Null(),
                hasError: true);
        }
    }

    private void SetDocumentVariableEvaluationState(
        string documentId,
        string variableName,
        FormulaValue value,
        bool hasError)
    {
        if (!_documentVariableValuesByDocumentId.TryGetValue(documentId, out var valueByVariableName))
        {
            valueByVariableName = new Dictionary<string, DocumentVariableEvaluationState>(StringComparer.OrdinalIgnoreCase);
            _documentVariableValuesByDocumentId[documentId] = valueByVariableName;
        }

        valueByVariableName[variableName] = new DocumentVariableEvaluationState(value, hasError);
    }

    private static HashSet<string> BuildDirtyNodeIdSet(
        in EvaluationRequest request,
        FormulaDependencyPlan dependencyPlan)
    {
        var dirtyNodeIds = new HashSet<string>(StringComparer.Ordinal);
        if (request.DirtyTableIds != null)
        {
            for (int tableIndex = 0; tableIndex < request.DirtyTableIds.Count; tableIndex++)
            {
                string tableId = request.DirtyTableIds[tableIndex];
                if (string.IsNullOrWhiteSpace(tableId))
                {
                    continue;
                }

                string tableNodeId = CreateTableNodeId(tableId);
                if (dependencyPlan.TableNodeIds.Contains(tableNodeId))
                {
                    dirtyNodeIds.Add(tableNodeId);
                }
            }
        }

        if (request.DirtyDocumentIds != null)
        {
            for (int documentIndex = 0; documentIndex < request.DirtyDocumentIds.Count; documentIndex++)
            {
                string documentId = request.DirtyDocumentIds[documentIndex];
                if (string.IsNullOrWhiteSpace(documentId))
                {
                    continue;
                }

                if (!dependencyPlan.DocumentVariableNodeIdsByDocumentId.TryGetValue(documentId, out var documentVariableNodeIds))
                {
                    continue;
                }

                for (int nodeIndex = 0; nodeIndex < documentVariableNodeIds.Count; nodeIndex++)
                {
                    dirtyNodeIds.Add(documentVariableNodeIds[nodeIndex]);
                }
            }
        }

        return dirtyNodeIds;
    }

    private static void RefreshDirtyTableIndexes(
        ProjectFormulaContext formulaContext,
        IReadOnlyList<string> dirtyTableIds)
    {
        for (int tableIndex = 0; tableIndex < dirtyTableIds.Count; tableIndex++)
        {
            string tableId = dirtyTableIds[tableIndex];
            if (string.IsNullOrWhiteSpace(tableId))
            {
                continue;
            }

            if (!formulaContext.TryGetTableById(tableId, out var table))
            {
                continue;
            }

            formulaContext.RefreshTableIndexes(table);
        }
    }

    private static HashSet<string> BuildAffectedNodeIds(
        HashSet<string> dirtyNodeIds,
        Dictionary<string, List<string>> dependentsByNodeId)
    {
        var affectedNodeIds = new HashSet<string>(dirtyNodeIds, StringComparer.Ordinal);
        var pendingNodes = new Queue<string>(dirtyNodeIds.Count);
        foreach (var nodeId in dirtyNodeIds)
        {
            pendingNodes.Enqueue(nodeId);
        }

        while (pendingNodes.Count > 0)
        {
            string sourceNodeId = pendingNodes.Dequeue();
            if (!dependentsByNodeId.TryGetValue(sourceNodeId, out var dependentNodeIds))
            {
                continue;
            }

            for (int dependentIndex = 0; dependentIndex < dependentNodeIds.Count; dependentIndex++)
            {
                string dependentNodeId = dependentNodeIds[dependentIndex];
                if (affectedNodeIds.Add(dependentNodeId))
                {
                    pendingNodes.Enqueue(dependentNodeId);
                }
            }
        }

        return affectedNodeIds;
    }

    private static void MergeOldAffectedNodesIntoCurrentPlan(
        HashSet<string> affectedNodeIds,
        HashSet<string>? oldAffectedNodeIds,
        Dictionary<string, List<string>> currentDependentsByNodeId)
    {
        if (oldAffectedNodeIds == null || oldAffectedNodeIds.Count == 0)
        {
            return;
        }

        foreach (string oldNodeId in oldAffectedNodeIds)
        {
            if (currentDependentsByNodeId.ContainsKey(oldNodeId))
            {
                affectedNodeIds.Add(oldNodeId);
            }
        }
    }

    private static HashSet<string> BuildAffectedTableIdsFromNodes(
        HashSet<string> affectedNodeIds,
        HashSet<string> tableNodeIds)
    {
        var affectedTableIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string nodeId in affectedNodeIds)
        {
            if (!tableNodeIds.Contains(nodeId))
            {
                continue;
            }

            if (TryGetTableIdFromNodeId(nodeId, out string tableId))
            {
                affectedTableIds.Add(tableId);
            }
        }

        return affectedTableIds;
    }

    private static string CreateTableNodeId(string tableId)
    {
        return TableNodePrefix + tableId;
    }

    private static bool TryGetTableIdFromNodeId(string nodeId, out string tableId)
    {
        if (nodeId.StartsWith(TableNodePrefix, StringComparison.Ordinal))
        {
            tableId = nodeId[TableNodePrefix.Length..];
            return !string.IsNullOrWhiteSpace(tableId);
        }

        tableId = "";
        return false;
    }

    private static string CreateDocumentVariableNodeId(string documentId, string variableName)
    {
        return DocumentVariableNodePrefix + documentId + ":" + variableName.ToLowerInvariant();
    }

    private static void SyncDerivedTableSchema(DocTable derivedTable, ProjectFormulaContext context)
    {
        var config = derivedTable.DerivedConfig;
        if (config == null)
        {
            return;
        }

        var activeSourceTableIds = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(config.BaseTableId))
        {
            activeSourceTableIds.Add(config.BaseTableId);
        }

        for (int stepIndex = 0; stepIndex < config.Steps.Count; stepIndex++)
        {
            string sourceTableId = config.Steps[stepIndex].SourceTableId;
            if (!string.IsNullOrEmpty(sourceTableId))
            {
                activeSourceTableIds.Add(sourceTableId);
            }
        }

        // Build a quick lookup for existing derived columns by id.
        var derivedColumnById = new Dictionary<string, DocColumn>(derivedTable.Columns.Count, StringComparer.Ordinal);
        for (int i = 0; i < derivedTable.Columns.Count; i++)
        {
            derivedColumnById[derivedTable.Columns[i].Id] = derivedTable.Columns[i];
        }

        // Keep identity projections (output column id == source column id) in sync with their sources.
        // For remapped outputs, preserve existing output metadata because those projections can intentionally
        // diverge from source presentation (for example, a text source projected into an asset preview column).
        // Also drop projections that refer to missing source tables/columns to avoid stale/noisy UI.
        for (int i = 0; i < config.Projections.Count; i++)
        {
            var proj = config.Projections[i];
            if (string.IsNullOrEmpty(proj.SourceTableId) || string.IsNullOrEmpty(proj.SourceColumnId) || string.IsNullOrEmpty(proj.OutputColumnId))
            {
                continue;
            }

            if (!activeSourceTableIds.Contains(proj.SourceTableId))
            {
                config.Projections.RemoveAt(i);
                i--;
                continue;
            }

            if (!context.TryGetTableById(proj.SourceTableId, out var sourceTable))
            {
                config.Projections.RemoveAt(i);
                i--;
                continue;
            }

            DocColumn? sourceColumn = null;
            for (int c = 0; c < sourceTable.Columns.Count; c++)
            {
                var col = sourceTable.Columns[c];
                if (string.Equals(col.Id, proj.SourceColumnId, StringComparison.Ordinal))
                {
                    sourceColumn = col;
                    break;
                }
            }
            if (sourceColumn == null)
            {
                config.Projections.RemoveAt(i);
                i--;
                continue;
            }

            bool createdDerivedColumn = false;
            if (!derivedColumnById.TryGetValue(proj.OutputColumnId, out var derivedColumn))
            {
                derivedColumn = new DocColumn { Id = proj.OutputColumnId };
                derivedTable.Columns.Add(derivedColumn);
                derivedColumnById[derivedColumn.Id] = derivedColumn;
                createdDerivedColumn = true;
            }

            derivedColumn.IsProjected = true;
            bool isIdentityProjection = string.Equals(proj.OutputColumnId, proj.SourceColumnId, StringComparison.Ordinal);
            bool shouldSyncFromSourceMetadata = createdDerivedColumn || isIdentityProjection;
            if (shouldSyncFromSourceMetadata)
            {
                derivedColumn.Kind = sourceColumn.Kind;
                derivedColumn.Width = sourceColumn.Width;
                derivedColumn.Options = sourceColumn.Options;
                derivedColumn.IsHidden = sourceColumn.IsHidden;
                derivedColumn.SubtableId = sourceColumn.SubtableId;
                derivedColumn.RelationTableId = sourceColumn.RelationTableId;
                derivedColumn.RelationTargetMode = sourceColumn.RelationTargetMode;
                derivedColumn.RelationTableVariantId = sourceColumn.RelationTableVariantId;
                derivedColumn.RelationDisplayColumnId = sourceColumn.RelationDisplayColumnId;
                derivedColumn.RowRefTableRefColumnId = sourceColumn.RowRefTableRefColumnId;
                derivedColumn.FormulaEvalScopes = sourceColumn.FormulaEvalScopes;
                derivedColumn.ModelPreviewSettings = sourceColumn.ModelPreviewSettings?.Clone();
            }

            if (!string.IsNullOrEmpty(proj.RenameAlias))
            {
                derivedColumn.Name = proj.RenameAlias;
            }
            else if (shouldSyncFromSourceMetadata || string.IsNullOrWhiteSpace(derivedColumn.Name))
            {
                derivedColumn.Name = sourceColumn.Name;
            }
        }

        // Drop suppressions that refer to missing source columns.
        for (int i = 0; i < config.SuppressedProjections.Count; i++)
        {
            var sup = config.SuppressedProjections[i];
            if (string.IsNullOrEmpty(sup.SourceTableId) || string.IsNullOrEmpty(sup.SourceColumnId))
            {
                config.SuppressedProjections.RemoveAt(i);
                i--;
                continue;
            }

            if (!context.TryGetTableById(sup.SourceTableId, out var sourceTable))
            {
                config.SuppressedProjections.RemoveAt(i);
                i--;
                continue;
            }

            bool hasCol = false;
            for (int c = 0; c < sourceTable.Columns.Count; c++)
            {
                if (string.Equals(sourceTable.Columns[c].Id, sup.SourceColumnId, StringComparison.Ordinal))
                {
                    hasCol = true;
                    break;
                }
            }

            if (!hasCol)
            {
                config.SuppressedProjections.RemoveAt(i);
                i--;
            }
        }

        // Auto-add missing projections for base + step source tables (unless suppressed).
        if (!string.IsNullOrEmpty(config.BaseTableId) && context.TryGetTableById(config.BaseTableId, out var baseTable))
        {
            AppendMissingSourceProjections(derivedTable, config, baseTable);
        }

        for (int s = 0; s < config.Steps.Count; s++)
        {
            var step = config.Steps[s];
            if (string.IsNullOrEmpty(step.SourceTableId))
            {
                continue;
            }

            if (context.TryGetTableById(step.SourceTableId, out var sourceTable))
            {
                AppendMissingSourceProjections(derivedTable, config, sourceTable);
            }
        }

        // Rebuild lookup after any appended columns.
        derivedColumnById.Clear();
        for (int i = 0; i < derivedTable.Columns.Count; i++)
        {
            derivedColumnById[derivedTable.Columns[i].Id] = derivedTable.Columns[i];
        }

        // If a join step was created/edited while mappings were unset or stale, the UI can appear to show a selection
        // while the stored ids are empty/invalid. Normalize those steps now so derived tables update immediately
        // without requiring the user to "reselect" the same mapping.
        EnsureJoinStepKeyMappingsAreValid(derivedTable, config, context, derivedColumnById);

        // Keep the derived table's Columns list aligned with projection order.
        // Projected columns not referenced by Projections are removed here.
        var reordered = new List<DocColumn>(derivedTable.Columns.Count);
        for (int i = 0; i < config.Projections.Count; i++)
        {
            var proj = config.Projections[i];
            if (string.IsNullOrEmpty(proj.OutputColumnId))
            {
                continue;
            }

            if (derivedColumnById.TryGetValue(proj.OutputColumnId, out var col))
            {
                reordered.Add(col);
            }
        }

        for (int i = 0; i < derivedTable.Columns.Count; i++)
        {
            var col = derivedTable.Columns[i];
            if (!col.IsProjected)
            {
                reordered.Add(col);
                continue;
            }

            // Skip projected columns that are not in Projections (dangling) or already included.
            if (FindProjectionIndexByOutputId(config, col.Id) < 0)
            {
                continue;
            }
        }

        derivedTable.Columns.Clear();
        derivedTable.Columns.AddRange(reordered);

        SyncDerivedSubtableBinding(derivedTable, config, context);
    }

    private static void SyncDerivedSubtableBinding(
        DocTable derivedTable,
        DocDerivedConfig config,
        ProjectFormulaContext context)
    {
        derivedTable.ParentTableId = null;
        derivedTable.ParentRowColumnId = null;

        if (string.IsNullOrEmpty(config.BaseTableId))
        {
            return;
        }

        if (!context.TryGetTableById(config.BaseTableId, out var baseTable))
        {
            return;
        }

        if (!baseTable.IsSubtable ||
            string.IsNullOrEmpty(baseTable.ParentTableId) ||
            string.IsNullOrEmpty(baseTable.ParentRowColumnId))
        {
            return;
        }

        string? outputParentRowColumnId = null;
        for (int projectionIndex = 0; projectionIndex < config.Projections.Count; projectionIndex++)
        {
            var projection = config.Projections[projectionIndex];
            if (!string.Equals(projection.SourceTableId, baseTable.Id, StringComparison.Ordinal) ||
                !string.Equals(projection.SourceColumnId, baseTable.ParentRowColumnId, StringComparison.Ordinal))
            {
                continue;
            }

            outputParentRowColumnId = projection.OutputColumnId;
            break;
        }

        if (string.IsNullOrEmpty(outputParentRowColumnId))
        {
            return;
        }

        derivedTable.ParentTableId = baseTable.ParentTableId;
        derivedTable.ParentRowColumnId = outputParentRowColumnId;
    }

    private static void EnsureJoinStepKeyMappingsAreValid(
        DocTable derivedTable,
        DocDerivedConfig config,
        ProjectFormulaContext context,
        Dictionary<string, DocColumn> derivedColumnById)
    {
        for (int stepIndex = 0; stepIndex < config.Steps.Count; stepIndex++)
        {
            var step = config.Steps[stepIndex];
            if (step.Kind != DerivedStepKind.Join)
            {
                continue;
            }

            if (string.IsNullOrEmpty(step.SourceTableId))
            {
                continue;
            }

            if (!context.TryGetTableById(step.SourceTableId, out var sourceTable))
            {
                continue;
            }

            if (step.KeyMappings.Count <= 0)
            {
                step.KeyMappings.Add(new DerivedKeyMapping());
            }

            var mapping = step.KeyMappings[0];

            // Validate/repair left id.
            string baseId = mapping.BaseColumnId;
            if (!IsValidDerivedJoinLeftId(derivedTable, config, step.SourceTableId, derivedColumnById, baseId))
            {
                // Try to preserve intent by matching the currently-selected right column name.
                string? rightName = FindSourceColumnNameById(sourceTable, mapping.SourceColumnId);
                if (!string.IsNullOrEmpty(rightName))
                {
                    var candidateBaseId = FindFirstDerivedColumnIdByNameExcludingSource(derivedTable, config, step.SourceTableId, rightName);
                    if (!string.IsNullOrEmpty(candidateBaseId))
                    {
                        baseId = candidateBaseId;
                    }
                }

                if (string.IsNullOrEmpty(baseId))
                {
                    var fallback = FindFirstDerivedJoinLeftId(derivedTable, config, step.SourceTableId);
                    baseId = fallback ?? "";
                }
            }

            // Validate/repair right id.
            string sourceId = mapping.SourceColumnId;
            if (!IsValidSourceColumnId(sourceTable, sourceId))
            {
                // Try to preserve intent by matching the chosen left column name.
                if (!string.IsNullOrEmpty(baseId) && derivedColumnById.TryGetValue(baseId, out var leftCol))
                {
                    sourceId = FindFirstSourceColumnIdByName(sourceTable, leftCol.Name) ?? "";
                }

                if (string.IsNullOrEmpty(sourceId) && sourceTable.Columns.Count > 0)
                {
                    sourceId = sourceTable.Columns[0].Id;
                }
            }

            if (!string.Equals(mapping.BaseColumnId, baseId, StringComparison.Ordinal) ||
                !string.Equals(mapping.SourceColumnId, sourceId, StringComparison.Ordinal))
            {
                step.KeyMappings[0].BaseColumnId = baseId;
                step.KeyMappings[0].SourceColumnId = sourceId;
            }
        }
    }

    private static bool IsValidDerivedJoinLeftId(
        DocTable derivedTable,
        DocDerivedConfig config,
        string joinSourceTableId,
        Dictionary<string, DocColumn> derivedColumnById,
        string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (!derivedColumnById.TryGetValue(id, out var col))
        {
            return false;
        }

        if (!col.IsProjected)
        {
            return true;
        }

        // Do not allow join-left keys that are projected from the join source table.
        int projIndex = FindProjectionIndexByOutputId(config, id);
        if (projIndex < 0)
        {
            return true;
        }

        var proj = config.Projections[projIndex];
        return !string.Equals(proj.SourceTableId, joinSourceTableId, StringComparison.Ordinal);
    }

    private static string? FindFirstDerivedJoinLeftId(DocTable derivedTable, DocDerivedConfig config, string joinSourceTableId)
    {
        for (int i = 0; i < derivedTable.Columns.Count; i++)
        {
            var col = derivedTable.Columns[i];
            if (!col.IsProjected)
            {
                return col.Id;
            }

            int projIndex = FindProjectionIndexByOutputId(config, col.Id);
            if (projIndex < 0)
            {
                return col.Id;
            }

            var proj = config.Projections[projIndex];
            if (!string.Equals(proj.SourceTableId, joinSourceTableId, StringComparison.Ordinal))
            {
                return col.Id;
            }
        }

        return null;
    }

    private static string? FindFirstDerivedColumnIdByNameExcludingSource(
        DocTable derivedTable,
        DocDerivedConfig config,
        string joinSourceTableId,
        string name)
    {
        for (int i = 0; i < derivedTable.Columns.Count; i++)
        {
            var col = derivedTable.Columns[i];
            if (!string.Equals(col.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!col.IsProjected)
            {
                return col.Id;
            }

            int projIndex = FindProjectionIndexByOutputId(config, col.Id);
            if (projIndex < 0)
            {
                return col.Id;
            }

            var proj = config.Projections[projIndex];
            if (!string.Equals(proj.SourceTableId, joinSourceTableId, StringComparison.Ordinal))
            {
                return col.Id;
            }
        }

        return null;
    }

    private static bool IsValidSourceColumnId(DocTable sourceTable, string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        for (int i = 0; i < sourceTable.Columns.Count; i++)
        {
            if (string.Equals(sourceTable.Columns[i].Id, id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindSourceColumnNameById(DocTable sourceTable, string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        for (int i = 0; i < sourceTable.Columns.Count; i++)
        {
            var col = sourceTable.Columns[i];
            if (string.Equals(col.Id, id, StringComparison.Ordinal))
            {
                return col.Name;
            }
        }

        return null;
    }

    private static string? FindFirstSourceColumnIdByName(DocTable sourceTable, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        for (int i = 0; i < sourceTable.Columns.Count; i++)
        {
            var col = sourceTable.Columns[i];
            if (string.Equals(col.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return col.Id;
            }
        }

        return null;
    }

    private static int FindProjectionIndexByOutputId(DocDerivedConfig config, string outputColumnId)
    {
        for (int i = 0; i < config.Projections.Count; i++)
        {
            if (string.Equals(config.Projections[i].OutputColumnId, outputColumnId, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    private static void AppendMissingSourceProjections(DocTable derivedTable, DocDerivedConfig config, DocTable sourceTable)
    {
        for (int c = 0; c < sourceTable.Columns.Count; c++)
        {
            var srcCol = sourceTable.Columns[c];

            if (IsSuppressed(config, sourceTable.Id, srcCol.Id))
            {
                continue;
            }

            if (HasProjection(config, sourceTable.Id, srcCol.Id))
            {
                continue;
            }

            string outputColumnId = Guid.NewGuid().ToString();
            var proj = new DerivedProjection
            {
                SourceTableId = sourceTable.Id,
                SourceColumnId = srcCol.Id,
                OutputColumnId = outputColumnId,
                RenameAlias = "",
            };

            config.Projections.Add(proj);

            derivedTable.Columns.Add(new DocColumn
            {
                Id = outputColumnId,
                Name = srcCol.Name,
                Kind = srcCol.Kind,
                Width = srcCol.Width,
                IsProjected = true,
                IsHidden = srcCol.IsHidden,
                SubtableId = srcCol.SubtableId,
                Options = srcCol.Options,
                RelationTableId = srcCol.RelationTableId,
                RelationTargetMode = srcCol.RelationTargetMode,
                RelationTableVariantId = srcCol.RelationTableVariantId,
                RelationDisplayColumnId = srcCol.RelationDisplayColumnId,
                RowRefTableRefColumnId = srcCol.RowRefTableRefColumnId,
                FormulaEvalScopes = srcCol.FormulaEvalScopes,
                ModelPreviewSettings = srcCol.ModelPreviewSettings?.Clone(),
            });
        }
    }

    private static bool HasProjection(DocDerivedConfig config, string sourceTableId, string sourceColumnId)
    {
        for (int i = 0; i < config.Projections.Count; i++)
        {
            var proj = config.Projections[i];
            if (string.Equals(proj.SourceTableId, sourceTableId, StringComparison.Ordinal) &&
                string.Equals(proj.SourceColumnId, sourceColumnId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSuppressed(DocDerivedConfig config, string sourceTableId, string sourceColumnId)
    {
        for (int i = 0; i < config.SuppressedProjections.Count; i++)
        {
            var sup = config.SuppressedProjections[i];
            if (string.Equals(sup.SourceTableId, sourceTableId, StringComparison.Ordinal) &&
                string.Equals(sup.SourceColumnId, sourceColumnId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void MaterializeDerivedTable(DocTable table, IFormulaContext context)
    {
        // Snapshot local (non-projected) cell data keyed by OutRowKey before materialization
        var localCellsByRowId = new Dictionary<string, Dictionary<string, DocCellValue>>(StringComparer.Ordinal);
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var localCells = new Dictionary<string, DocCellValue>(StringComparer.Ordinal);
            for (int colIndex = 0; colIndex < table.Columns.Count; colIndex++)
            {
                var col = table.Columns[colIndex];
                if (!col.IsProjected && row.Cells.TryGetValue(col.Id, out var val))
                {
                    localCells[col.Id] = val;
                }
            }
            if (localCells.Count > 0)
                localCellsByRowId[row.Id] = localCells;
        }

        var result = DerivedResolver.Materialize(table, context);
        DerivedResults[table.Id] = result;

        // Replace table rows with materialized output
        table.Rows.Clear();
        for (int i = 0; i < result.Rows.Count; i++)
        {
            var row = result.Rows[i];

            // Restore local cell data if we have it for this row ID
            if (localCellsByRowId.TryGetValue(row.Id, out var savedLocals))
            {
                foreach (var (colId, val) in savedLocals)
                    row.SetCell(colId, val);
            }

            table.Rows.Add(row);
        }
    }

    public bool TryEvaluateExpression(
        DocProject project,
        DocTable table,
        DocRow row,
        DocColumn outputColumn,
        string formulaExpression,
        out DocCellValue result)
    {
        SchemaLinkedTableSynchronizer.Synchronize(project);

        string expressionText = string.IsNullOrWhiteSpace(formulaExpression) ? "0" : formulaExpression;
        var compiledFormula = CompileFormula(expressionText);
        if (!compiledFormula.IsValid)
        {
            result = CreateFormulaErrorCell();
            return false;
        }

        var formulaContext = new ProjectFormulaContext(project);
        var evaluator = new FormulaEvaluator(formulaContext);
        try
        {
            int rowIndexOneBased = formulaContext.GetRowIndexOneBased(table, row);
            var frame = CreateRootEvaluationFrame(formulaContext, table, row, rowIndexOneBased);
            var formulaValue = evaluator.Evaluate(compiledFormula.Root, frame);
            result = ConvertFormulaResultToCellValue(formulaValue, table, outputColumn, formulaContext);
            return true;
        }
        catch
        {
            result = CreateFormulaErrorCell();
            return false;
        }
    }

    public bool TryEvaluateDocumentExpression(
        DocProject project,
        DocDocument document,
        string formulaExpression,
        out string resultText)
    {
        resultText = "";
        string normalizedExpression = NormalizeDocumentFormulaExpression(formulaExpression);
        if (string.IsNullOrWhiteSpace(normalizedExpression))
        {
            return false;
        }

        var compiledFormula = CompileFormula(normalizedExpression);
        if (!compiledFormula.IsValid)
        {
            return false;
        }

        if (!TryGetOrCreateProjectFormulaContext(project, out var formulaContext))
        {
            return false;
        }

        var evaluator = new FormulaEvaluator(formulaContext, _documentVariableValuesByDocumentId);
        try
        {
            var frame = CreateDocumentEvaluationFrame(document);
            var formulaValue = evaluator.Evaluate(compiledFormula.Root, frame);
            resultText = ConvertFormulaResultToDisplayText(formulaValue, formulaContext);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryEvaluateTableExpression(
        DocProject project,
        DocTable table,
        string formulaExpression,
        out FormulaValue result)
    {
        return TryEvaluateTableExpression(project, table, formulaExpression, tableVariableOverrides: null, out result);
    }

    public bool TryEvaluateTableExpression(
        DocProject project,
        DocTable table,
        string formulaExpression,
        IReadOnlyList<DocBlockTableVariableOverride>? tableVariableOverrides,
        out FormulaValue result)
    {
        result = FormulaValue.Null();
        string normalizedExpression = NormalizeTableFormulaExpression(formulaExpression);
        if (string.IsNullOrWhiteSpace(normalizedExpression))
        {
            return false;
        }

        if (!TryGetOrCompileTableExpression(normalizedExpression, out var compiledFormula) ||
            !compiledFormula.IsValid)
        {
            return false;
        }

        if (!TryGetOrCreateProjectFormulaContext(project, out var formulaContext))
        {
            return false;
        }

        Dictionary<string, string>? tableVariableOverrideExpressionsByName = null;
        if (tableVariableOverrides != null && tableVariableOverrides.Count > 0)
        {
            tableVariableOverrideExpressionsByName = BuildTableVariableOverrideMapByName(table, tableVariableOverrides);
        }

        var evaluator = new FormulaEvaluator(
            formulaContext,
            _documentVariableValuesByDocumentId,
            tableVariableOverrideTableId: table.Id,
            tableVariableOverrideExpressionsByName: tableVariableOverrideExpressionsByName);
        try
        {
            var frame = CreateTableEvaluationFrame(table);
            result = evaluator.Evaluate(compiledFormula.Root, frame);
            return true;
        }
        catch
        {
            result = FormulaValue.Null();
            return false;
        }
    }

    private static Dictionary<string, string> BuildTableVariableOverrideMapByName(
        DocTable table,
        IReadOnlyList<DocBlockTableVariableOverride> tableVariableOverrides)
    {
        var tableVariableNameById = new Dictionary<string, string>(table.Variables.Count, StringComparer.Ordinal);
        for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
        {
            var tableVariable = table.Variables[variableIndex];
            if (string.IsNullOrWhiteSpace(tableVariable.Id) || string.IsNullOrWhiteSpace(tableVariable.Name))
            {
                continue;
            }

            if (!tableVariableNameById.ContainsKey(tableVariable.Id))
            {
                tableVariableNameById[tableVariable.Id] = tableVariable.Name;
            }
        }

        var tableVariableOverrideExpressionsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int overrideIndex = 0; overrideIndex < tableVariableOverrides.Count; overrideIndex++)
        {
            var tableVariableOverride = tableVariableOverrides[overrideIndex];
            if (tableVariableOverride == null ||
                string.IsNullOrWhiteSpace(tableVariableOverride.VariableId) ||
                !tableVariableNameById.TryGetValue(tableVariableOverride.VariableId, out string? variableName) ||
                string.IsNullOrWhiteSpace(variableName))
            {
                continue;
            }

            tableVariableOverrideExpressionsByName[variableName] = tableVariableOverride.Expression ?? "";
        }

        return tableVariableOverrideExpressionsByName;
    }

    private bool TryGetOrCompileTableExpression(string normalizedExpression, out CompiledFormula compiledFormula)
    {
        if (_compiledTableExpressionCacheByExpression.TryGetValue(normalizedExpression, out compiledFormula!))
        {
            return true;
        }

        compiledFormula = CompileFormula(normalizedExpression);
        _compiledTableExpressionCacheByExpression[normalizedExpression] = compiledFormula;
        return true;
    }

    private bool TryGetOrCreateProjectFormulaContext(DocProject project, out ProjectFormulaContext formulaContext)
    {
        SchemaLinkedTableSynchronizer.Synchronize(project);

        bool projectReferenceChanged = !ReferenceEquals(_cachedProjectReference, project);
        if (projectReferenceChanged)
        {
            ClearEvaluationCaches();
            _cachedProjectReference = project;
            _cachedFormulaContext = new ProjectFormulaContext(project);
        }
        else if (_cachedFormulaContext == null)
        {
            _cachedFormulaContext = new ProjectFormulaContext(project);
        }

        if (_cachedFormulaContext == null)
        {
            formulaContext = null!;
            return false;
        }

        formulaContext = _cachedFormulaContext;
        return true;
    }

    private static string NormalizeDocumentFormulaExpression(string formulaExpression)
    {
        return NormalizeFormulaExpression(formulaExpression);
    }

    private static string NormalizeTableFormulaExpression(string formulaExpression)
    {
        return NormalizeFormulaExpression(formulaExpression);
    }

    private static string NormalizeFormulaExpression(string formulaExpression)
    {
        string text = formulaExpression.Trim();
        if (text.StartsWith("=(", StringComparison.Ordinal))
        {
            if (text.Length > 3 && text[^1] == ')')
            {
                return text[2..^1];
            }

            return text.Length > 2 ? text[2..] : "";
        }

        if (text.StartsWith("=", StringComparison.Ordinal))
        {
            return text.Length > 1 ? text[1..] : "";
        }

        return text;
    }

    /// <summary>
    /// Validates that a formula expression compiles successfully without evaluating it.
    /// </summary>
    public static bool ValidateExpression(string formulaExpression)
    {
        if (string.IsNullOrWhiteSpace(formulaExpression))
            return false;
        return CompileFormula(formulaExpression).IsValid;
    }

    private static void CompileProjectFormulas(
        DocProject project,
        Dictionary<string, Dictionary<string, CompiledFormula>> compiledByTableId)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var table = project.Tables[tableIndex];
            var tableCompiledColumns = new Dictionary<string, CompiledFormula>(StringComparer.Ordinal);

            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                if (string.IsNullOrWhiteSpace(column.FormulaExpression))
                {
                    continue;
                }

                tableCompiledColumns[column.Id] = CompileFormula(column.FormulaExpression!);
            }

            if (tableCompiledColumns.Count > 0)
            {
                compiledByTableId[table.Id] = tableCompiledColumns;
            }
        }
    }

    private static void CompileProjectCellFormulas(
        DocProject project,
        Dictionary<string, List<CompiledCellFormula>> compiledCellFormulasByTableId,
        Dictionary<string, Dictionary<string, List<CompiledCellFormula>>> compiledCellFormulasByTableAndRowId)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (table.Rows.Count <= 0)
            {
                continue;
            }

            var columnById = new Dictionary<string, DocColumn>(table.Columns.Count, StringComparer.Ordinal);
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                columnById[table.Columns[columnIndex].Id] = table.Columns[columnIndex];
            }

            var compiledEntries = new List<CompiledCellFormula>();
            var compiledEntriesByRowId = new Dictionary<string, List<CompiledCellFormula>>(StringComparer.Ordinal);
            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                DocRow row = table.Rows[rowIndex];
                foreach (var cellEntry in row.Cells)
                {
                    if (!columnById.ContainsKey(cellEntry.Key))
                    {
                        continue;
                    }

                    DocCellValue cellValue = cellEntry.Value;
                    if (string.IsNullOrWhiteSpace(cellValue.CellFormulaExpression))
                    {
                        continue;
                    }

                    string normalizedExpression = NormalizeTableFormulaExpression(cellValue.CellFormulaExpression);
                    if (string.IsNullOrWhiteSpace(normalizedExpression))
                    {
                        continue;
                    }

                    var compiledCellFormula = new CompiledCellFormula
                    {
                        RowId = row.Id,
                        ColumnId = cellEntry.Key,
                        FormulaExpression = normalizedExpression,
                        CompiledFormula = CompileFormula(normalizedExpression),
                    };
                    compiledEntries.Add(compiledCellFormula);

                    if (!compiledEntriesByRowId.TryGetValue(row.Id, out List<CompiledCellFormula>? rowEntries))
                    {
                        rowEntries = new List<CompiledCellFormula>();
                        compiledEntriesByRowId[row.Id] = rowEntries;
                    }

                    rowEntries.Add(compiledCellFormula);
                }
            }

            if (compiledEntries.Count <= 0)
            {
                continue;
            }

            compiledCellFormulasByTableId[table.Id] = compiledEntries;
            compiledCellFormulasByTableAndRowId[table.Id] = compiledEntriesByRowId;
        }
    }

    private static void CompileProjectTableVariableFormulas(
        DocProject project,
        Dictionary<string, Dictionary<string, CompiledFormula>> compiledTableVariablesByTableId)
    {
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var table = project.Tables[tableIndex];
            var tableCompiledVariables = new Dictionary<string, CompiledFormula>(StringComparer.OrdinalIgnoreCase);

            for (int variableIndex = 0; variableIndex < table.Variables.Count; variableIndex++)
            {
                var tableVariable = table.Variables[variableIndex];
                if (string.IsNullOrWhiteSpace(tableVariable.Name) ||
                    string.IsNullOrWhiteSpace(tableVariable.Expression))
                {
                    continue;
                }

                string normalizedExpression = NormalizeTableFormulaExpression(tableVariable.Expression);
                if (string.IsNullOrWhiteSpace(normalizedExpression))
                {
                    continue;
                }

                if (tableCompiledVariables.ContainsKey(tableVariable.Name))
                {
                    continue;
                }

                tableCompiledVariables[tableVariable.Name] = CompileFormula(normalizedExpression);
            }

            if (tableCompiledVariables.Count > 0)
            {
                compiledTableVariablesByTableId[table.Id] = tableCompiledVariables;
            }
        }
    }

    private static List<DocumentVariableNode> CompileProjectDocumentVariableFormulas(
        DocProject project,
        Dictionary<string, Dictionary<string, CompiledFormula>> compiledDocumentVariablesByDocumentId)
    {
        var compiledVariableNodes = new List<DocumentVariableNode>();
        for (int documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
        {
            var document = project.Documents[documentIndex];
            var compiledByVariableName = new Dictionary<string, CompiledFormula>(StringComparer.OrdinalIgnoreCase);

            for (int blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
            {
                var block = document.Blocks[blockIndex];
                if (block.Type != DocBlockType.Variable)
                {
                    continue;
                }

                if (!DocumentFormulaSyntax.TryParseVariableDeclaration(
                        block.Text.PlainText,
                        out string variableName,
                        out bool hasExpression,
                        out string expression))
                {
                    continue;
                }

                if (!hasExpression || string.IsNullOrWhiteSpace(expression))
                {
                    continue;
                }

                string normalizedExpression = NormalizeDocumentFormulaExpression(expression);
                if (string.IsNullOrWhiteSpace(normalizedExpression))
                {
                    continue;
                }

                if (compiledByVariableName.ContainsKey(variableName))
                {
                    continue;
                }

                var compiledFormula = CompileFormula(normalizedExpression);
                compiledByVariableName[variableName] = compiledFormula;
                compiledVariableNodes.Add(new DocumentVariableNode
                {
                    NodeId = CreateDocumentVariableNodeId(document.Id, variableName),
                    DocumentId = document.Id,
                    VariableName = variableName,
                    CompiledFormula = compiledFormula,
                });
            }

            if (compiledByVariableName.Count > 0)
            {
                compiledDocumentVariablesByDocumentId[document.Id] = compiledByVariableName;
            }
        }

        return compiledVariableNodes;
    }

    private static FormulaDependencyPlan BuildFormulaDependencyPlan(
        DocProject project,
        IFormulaContext formulaContext,
        Dictionary<string, Dictionary<string, CompiledFormula>> compiledByTableId,
        Dictionary<string, List<CompiledCellFormula>> compiledCellFormulasByTableId,
        Dictionary<string, Dictionary<string, CompiledFormula>> compiledTableVariablesByTableId,
        List<DocumentVariableNode> compiledDocumentVariableNodes,
        Dictionary<string, Dictionary<string, CompiledFormula>> compiledDocumentVariablesByDocumentId)
    {
        var dependencyEdgesByNodeId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var dependenciesByNodeId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var dependencySourceSetsByNodeId = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var dependencyEdgeSetsByNodeId = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var indegreeByNodeId = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodeOrderByNodeId = new Dictionary<string, int>(StringComparer.Ordinal);
        var orderedNodeIds = new List<string>(project.Tables.Count + compiledDocumentVariableNodes.Count);
        var tableNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var documentVariableNodesByNodeId = new Dictionary<string, DocumentVariableNode>(StringComparer.Ordinal);
        var documentVariableNodeIdsByDocumentId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var tableById = new Dictionary<string, DocTable>(StringComparer.Ordinal);

        int nodeOrder = 0;
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var table = project.Tables[tableIndex];
            tableById[table.Id] = table;
            string tableNodeId = CreateTableNodeId(table.Id);
            orderedNodeIds.Add(tableNodeId);
            nodeOrderByNodeId[tableNodeId] = nodeOrder++;
            tableNodeIds.Add(tableNodeId);
            indegreeByNodeId[tableNodeId] = 0;
            dependencyEdgesByNodeId[tableNodeId] = new List<string>(4);
            dependencyEdgeSetsByNodeId[tableNodeId] = new HashSet<string>(StringComparer.Ordinal);
            dependenciesByNodeId[tableNodeId] = new List<string>(4);
            dependencySourceSetsByNodeId[tableNodeId] = new HashSet<string>(StringComparer.Ordinal);
        }

        for (int nodeIndex = 0; nodeIndex < compiledDocumentVariableNodes.Count; nodeIndex++)
        {
            var documentVariableNode = compiledDocumentVariableNodes[nodeIndex];
            orderedNodeIds.Add(documentVariableNode.NodeId);
            nodeOrderByNodeId[documentVariableNode.NodeId] = nodeOrder++;
            documentVariableNodesByNodeId[documentVariableNode.NodeId] = documentVariableNode;
            indegreeByNodeId[documentVariableNode.NodeId] = 0;
            dependencyEdgesByNodeId[documentVariableNode.NodeId] = new List<string>(4);
            dependencyEdgeSetsByNodeId[documentVariableNode.NodeId] = new HashSet<string>(StringComparer.Ordinal);
            dependenciesByNodeId[documentVariableNode.NodeId] = new List<string>(4);
            dependencySourceSetsByNodeId[documentVariableNode.NodeId] = new HashSet<string>(StringComparer.Ordinal);

            if (!documentVariableNodeIdsByDocumentId.TryGetValue(documentVariableNode.DocumentId, out var documentNodeList))
            {
                documentNodeList = new List<string>(4);
                documentVariableNodeIdsByDocumentId[documentVariableNode.DocumentId] = documentNodeList;
            }

            documentNodeList.Add(documentVariableNode.NodeId);
        }

        // Table formula edges
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var table = project.Tables[tableIndex];
            string dependentTableNodeId = CreateTableNodeId(table.Id);

            if (compiledByTableId.TryGetValue(table.Id, out var compiledColumns))
            {
                foreach (var (_, compiledFormula) in compiledColumns)
                {
                    AddTableNodeDependenciesFromCompiledFormula(
                        compiledFormula,
                        table,
                        dependentTableNodeId,
                        formulaContext,
                        compiledDocumentVariablesByDocumentId,
                        dependencyEdgesByNodeId,
                        dependencyEdgeSetsByNodeId,
                        dependenciesByNodeId,
                        dependencySourceSetsByNodeId,
                        indegreeByNodeId);
                }
            }

            if (compiledCellFormulasByTableId.TryGetValue(table.Id, out var compiledCellFormulas))
            {
                for (int compiledCellIndex = 0; compiledCellIndex < compiledCellFormulas.Count; compiledCellIndex++)
                {
                    AddTableNodeDependenciesFromCompiledFormula(
                        compiledCellFormulas[compiledCellIndex].CompiledFormula,
                        table,
                        dependentTableNodeId,
                        formulaContext,
                        compiledDocumentVariablesByDocumentId,
                        dependencyEdgesByNodeId,
                        dependencyEdgeSetsByNodeId,
                        dependenciesByNodeId,
                        dependencySourceSetsByNodeId,
                        indegreeByNodeId);
                }
            }

            if (!compiledTableVariablesByTableId.TryGetValue(table.Id, out var compiledTableVariables))
            {
                continue;
            }

            foreach (var (_, compiledTableVariableFormula) in compiledTableVariables)
            {
                AddTableNodeDependenciesFromCompiledFormula(
                    compiledTableVariableFormula,
                    table,
                    dependentTableNodeId,
                    formulaContext,
                    compiledDocumentVariablesByDocumentId,
                    dependencyEdgesByNodeId,
                    dependencyEdgeSetsByNodeId,
                    dependenciesByNodeId,
                    dependencySourceSetsByNodeId,
                    indegreeByNodeId);
            }
        }

        // Document variable formula edges
        for (int nodeIndex = 0; nodeIndex < compiledDocumentVariableNodes.Count; nodeIndex++)
        {
            var documentVariableNode = compiledDocumentVariableNodes[nodeIndex];
            var compiledFormula = documentVariableNode.CompiledFormula;

            if (compiledFormula.ReferencedTableNames.Count > 0)
            {
                var referencedTableNames = compiledFormula.ReferencedTableNames.ToList();
                referencedTableNames.Sort(StringComparer.OrdinalIgnoreCase);
                for (int referencedIndex = 0; referencedIndex < referencedTableNames.Count; referencedIndex++)
                {
                    string referencedTableName = referencedTableNames[referencedIndex];
                    if (!formulaContext.TryGetTableByName(referencedTableName, out var dependencyTable))
                    {
                        continue;
                    }

                    AddDependencyEdge(
                        CreateTableNodeId(dependencyTable.Id),
                        documentVariableNode.NodeId,
                        dependencyEdgesByNodeId,
                        dependencyEdgeSetsByNodeId,
                        dependenciesByNodeId,
                        dependencySourceSetsByNodeId,
                        indegreeByNodeId);
                }
            }

            var dependencyDocumentNodeIds = CollectDocumentVariableNodeDependencies(
                compiledFormula.Root,
                formulaContext,
                documentVariableNode.DocumentId,
                compiledDocumentVariablesByDocumentId);

            foreach (string dependencyDocumentNodeId in dependencyDocumentNodeIds)
            {
                AddDependencyEdge(
                    dependencyDocumentNodeId,
                    documentVariableNode.NodeId,
                    dependencyEdgesByNodeId,
                    dependencyEdgeSetsByNodeId,
                    dependenciesByNodeId,
                    dependencySourceSetsByNodeId,
                    indegreeByNodeId);
            }
        }

        // Derived table edges (table -> table)
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var table = project.Tables[tableIndex];
            if (!table.IsDerived)
            {
                continue;
            }

            string derivedNodeId = CreateTableNodeId(table.Id);
            var config = table.DerivedConfig!;
            if (!string.IsNullOrEmpty(config.BaseTableId) && tableById.ContainsKey(config.BaseTableId))
            {
                AddDependencyEdge(
                    CreateTableNodeId(config.BaseTableId),
                    derivedNodeId,
                    dependencyEdgesByNodeId,
                    dependencyEdgeSetsByNodeId,
                    dependenciesByNodeId,
                    dependencySourceSetsByNodeId,
                    indegreeByNodeId);
            }

            for (int stepIndex = 0; stepIndex < config.Steps.Count; stepIndex++)
            {
                string sourceTableId = config.Steps[stepIndex].SourceTableId;
                if (string.IsNullOrEmpty(sourceTableId) ||
                    !tableById.ContainsKey(sourceTableId) ||
                    string.Equals(sourceTableId, table.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                AddDependencyEdge(
                    CreateTableNodeId(sourceTableId),
                    derivedNodeId,
                    dependencyEdgesByNodeId,
                    dependencyEdgeSetsByNodeId,
                    dependenciesByNodeId,
                    dependencySourceSetsByNodeId,
                    indegreeByNodeId);
            }
        }

        // Schema-linked table edges (schema source -> schema-linked table).
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (!table.IsSchemaLinked || string.IsNullOrWhiteSpace(table.SchemaSourceTableId))
            {
                continue;
            }

            if (!tableById.ContainsKey(table.SchemaSourceTableId))
            {
                continue;
            }

            AddDependencyEdge(
                CreateTableNodeId(table.SchemaSourceTableId),
                CreateTableNodeId(table.Id),
                dependencyEdgesByNodeId,
                dependencyEdgeSetsByNodeId,
                dependenciesByNodeId,
                    dependencySourceSetsByNodeId,
                    indegreeByNodeId);
        }

        // Inherited table edges (inheritance source -> inherited table).
        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (!table.IsInherited || string.IsNullOrWhiteSpace(table.InheritanceSourceTableId))
            {
                continue;
            }

            if (!tableById.ContainsKey(table.InheritanceSourceTableId))
            {
                continue;
            }

            AddDependencyEdge(
                CreateTableNodeId(table.InheritanceSourceTableId),
                CreateTableNodeId(table.Id),
                dependencyEdgesByNodeId,
                dependencyEdgeSetsByNodeId,
                dependenciesByNodeId,
                dependencySourceSetsByNodeId,
                indegreeByNodeId);
        }

        foreach (var (_, dependentNodeIds) in dependencyEdgesByNodeId)
        {
            dependentNodeIds.Sort((leftNodeId, rightNodeId) => nodeOrderByNodeId[leftNodeId].CompareTo(nodeOrderByNodeId[rightNodeId]));
        }

        foreach (var (_, dependencyNodeIds) in dependenciesByNodeId)
        {
            dependencyNodeIds.Sort((leftNodeId, rightNodeId) => nodeOrderByNodeId[leftNodeId].CompareTo(nodeOrderByNodeId[rightNodeId]));
        }

        var evaluationOrderNodeIds = new List<string>(orderedNodeIds.Count);
        var processQueue = new Queue<string>(orderedNodeIds.Count);
        for (int nodeIndex = 0; nodeIndex < orderedNodeIds.Count; nodeIndex++)
        {
            string nodeId = orderedNodeIds[nodeIndex];
            if (indegreeByNodeId[nodeId] == 0)
            {
                processQueue.Enqueue(nodeId);
            }
        }

        while (processQueue.Count > 0)
        {
            string currentNodeId = processQueue.Dequeue();
            evaluationOrderNodeIds.Add(currentNodeId);

            var dependentNodeIds = dependencyEdgesByNodeId[currentNodeId];
            for (int dependentIndex = 0; dependentIndex < dependentNodeIds.Count; dependentIndex++)
            {
                string dependentNodeId = dependentNodeIds[dependentIndex];
                indegreeByNodeId[dependentNodeId]--;
                if (indegreeByNodeId[dependentNodeId] == 0)
                {
                    processQueue.Enqueue(dependentNodeId);
                }
            }
        }

        if (evaluationOrderNodeIds.Count != orderedNodeIds.Count)
        {
            string cyclePath = FindDependencyCyclePath(orderedNodeIds, dependencyEdgesByNodeId);
            throw new InvalidOperationException(string.IsNullOrEmpty(cyclePath)
                ? "Cycle detected in unified formula dependency graph."
                : $"Cycle detected in unified formula dependency graph: {cyclePath}");
        }

        return new FormulaDependencyPlan
        {
            OrderedNodeIds = evaluationOrderNodeIds,
            DependentsByNodeId = dependencyEdgesByNodeId,
            DependenciesByNodeId = dependenciesByNodeId,
            TableNodeIds = tableNodeIds,
            DocumentVariableNodesByNodeId = documentVariableNodesByNodeId,
            DocumentVariableNodeIdsByDocumentId = documentVariableNodeIdsByDocumentId,
        };
    }

    private static void AddTableNodeDependenciesFromCompiledFormula(
        CompiledFormula compiledFormula,
        DocTable dependentTable,
        string dependentTableNodeId,
        IFormulaContext formulaContext,
        Dictionary<string, Dictionary<string, CompiledFormula>> compiledDocumentVariablesByDocumentId,
        Dictionary<string, List<string>> dependencyEdgesByNodeId,
        Dictionary<string, HashSet<string>> dependencyEdgeSetsByNodeId,
        Dictionary<string, List<string>> dependenciesByNodeId,
        Dictionary<string, HashSet<string>> dependencySourceSetsByNodeId,
        Dictionary<string, int> indegreeByNodeId)
    {
        if (compiledFormula.ReferencedTableNames.Count > 0)
        {
            var referencedTableNames = compiledFormula.ReferencedTableNames.ToList();
            referencedTableNames.Sort(StringComparer.OrdinalIgnoreCase);
            for (int referencedIndex = 0; referencedIndex < referencedTableNames.Count; referencedIndex++)
            {
                string referencedTableName = referencedTableNames[referencedIndex];
                if (!formulaContext.TryGetTableByName(referencedTableName, out var dependencyTable))
                {
                    continue;
                }

                AddDependencyEdge(
                    CreateTableNodeId(dependencyTable.Id),
                    dependentTableNodeId,
                    dependencyEdgesByNodeId,
                    dependencyEdgeSetsByNodeId,
                    dependenciesByNodeId,
                    dependencySourceSetsByNodeId,
                    indegreeByNodeId);
            }
        }

        if (compiledFormula.ReferencesParentScope &&
            !string.IsNullOrWhiteSpace(dependentTable.ParentTableId))
        {
            AddDependencyEdge(
                CreateTableNodeId(dependentTable.ParentTableId),
                dependentTableNodeId,
                dependencyEdgesByNodeId,
                dependencyEdgeSetsByNodeId,
                dependenciesByNodeId,
                dependencySourceSetsByNodeId,
                indegreeByNodeId);
        }

        if (ContainsGraphInputCall(compiledFormula.Root) &&
            TryResolveNodeGraphEdgeTable(formulaContext, dependentTable, out DocTable nodeGraphEdgeTable))
        {
            AddDependencyEdge(
                CreateTableNodeId(nodeGraphEdgeTable.Id),
                dependentTableNodeId,
                dependencyEdgesByNodeId,
                dependencyEdgeSetsByNodeId,
                dependenciesByNodeId,
                dependencySourceSetsByNodeId,
                indegreeByNodeId);
        }

        if (!compiledFormula.ReferencesDocumentScope)
        {
            return;
        }

        var dependencyDocumentNodeIds = CollectDocumentVariableNodeDependencies(
            compiledFormula.Root,
            formulaContext,
            currentDocumentId: null,
            compiledDocumentVariablesByDocumentId);

        foreach (string dependencyDocumentNodeId in dependencyDocumentNodeIds)
        {
            AddDependencyEdge(
                dependencyDocumentNodeId,
                dependentTableNodeId,
                dependencyEdgesByNodeId,
                dependencyEdgeSetsByNodeId,
                dependenciesByNodeId,
                dependencySourceSetsByNodeId,
                indegreeByNodeId);
        }
    }

    private static void AddDependencyEdge(
        string dependencyNodeId,
        string dependentNodeId,
        Dictionary<string, List<string>> dependencyEdgesByNodeId,
        Dictionary<string, HashSet<string>> dependencyEdgeSetsByNodeId,
        Dictionary<string, List<string>> dependenciesByNodeId,
        Dictionary<string, HashSet<string>> dependencySourceSetsByNodeId,
        Dictionary<string, int> indegreeByNodeId)
    {
        if (string.IsNullOrWhiteSpace(dependencyNodeId) ||
            string.IsNullOrWhiteSpace(dependentNodeId) ||
            string.Equals(dependencyNodeId, dependentNodeId, StringComparison.Ordinal))
        {
            return;
        }

        if (!dependencyEdgeSetsByNodeId.TryGetValue(dependencyNodeId, out var dependentsSet) ||
            !dependencyEdgesByNodeId.TryGetValue(dependencyNodeId, out var dependentNodeList) ||
            !dependencySourceSetsByNodeId.TryGetValue(dependentNodeId, out var dependencySourceSet) ||
            !dependenciesByNodeId.TryGetValue(dependentNodeId, out var dependencyNodeList))
        {
            return;
        }

        if (!dependentsSet.Add(dependentNodeId))
        {
            return;
        }

        dependentNodeList.Add(dependentNodeId);
        indegreeByNodeId[dependentNodeId]++;
        if (dependencySourceSet.Add(dependencyNodeId))
        {
            dependencyNodeList.Add(dependencyNodeId);
        }
    }

    private static bool ContainsGraphInputCall(ExpressionNode node)
    {
        if (node.Kind == ExpressionNodeKind.Call &&
            node.Callee != null &&
            node.Callee.Kind == ExpressionNodeKind.MemberAccess &&
            node.Callee.Left != null &&
            node.Callee.Left.Kind == ExpressionNodeKind.Identifier &&
            string.Equals(node.Callee.Left.Text, "graph", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(node.Callee.Text, "in", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (node.Left != null && ContainsGraphInputCall(node.Left))
        {
            return true;
        }

        if (node.Right != null && ContainsGraphInputCall(node.Right))
        {
            return true;
        }

        if (node.Third != null && ContainsGraphInputCall(node.Third))
        {
            return true;
        }

        if (node.Callee != null && ContainsGraphInputCall(node.Callee))
        {
            return true;
        }

        if (node.Arguments != null)
        {
            for (int argumentIndex = 0; argumentIndex < node.Arguments.Count; argumentIndex++)
            {
                if (ContainsGraphInputCall(node.Arguments[argumentIndex]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveNodeGraphEdgeTable(
        IFormulaContext formulaContext,
        DocTable nodeTable,
        out DocTable edgeTable)
    {
        edgeTable = null!;
        for (int columnIndex = 0; columnIndex < nodeTable.Columns.Count; columnIndex++)
        {
            DocColumn column = nodeTable.Columns[columnIndex];
            if (column.Kind != DocColumnKind.Subtable ||
                !string.Equals(column.Name, NodeGraphEdgesColumnName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(column.SubtableId))
            {
                continue;
            }

            if (formulaContext.TryGetTableById(column.SubtableId, out DocTable tableById))
            {
                edgeTable = tableById;
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> CollectDocumentVariableNodeDependencies(
        ExpressionNode expressionRoot,
        IFormulaContext formulaContext,
        string? currentDocumentId,
        Dictionary<string, Dictionary<string, CompiledFormula>> compiledDocumentVariablesByDocumentId)
    {
        var dependencyNodeIds = new HashSet<string>(StringComparer.Ordinal);
        CollectDocumentVariableNodeDependenciesRecursive(
            expressionRoot,
            formulaContext,
            currentDocumentId,
            compiledDocumentVariablesByDocumentId,
            dependencyNodeIds);
        return dependencyNodeIds;
    }

    private static void CollectDocumentVariableNodeDependenciesRecursive(
        ExpressionNode node,
        IFormulaContext formulaContext,
        string? currentDocumentId,
        Dictionary<string, Dictionary<string, CompiledFormula>> compiledDocumentVariablesByDocumentId,
        HashSet<string> dependencyNodeIds)
    {
        if (node.Kind == ExpressionNodeKind.MemberAccess &&
            TryResolveDocumentVariableReferenceNodeId(
                node,
                formulaContext,
                currentDocumentId,
                compiledDocumentVariablesByDocumentId,
                out string dependencyNodeId))
        {
            dependencyNodeIds.Add(dependencyNodeId);
        }
        else if (node.Kind == ExpressionNodeKind.AtIdentifier &&
                 TryResolveCurrentDocumentAtIdentifierNodeId(
                     node,
                     currentDocumentId,
                     compiledDocumentVariablesByDocumentId,
                     out string atIdentifierDependencyNodeId))
        {
            dependencyNodeIds.Add(atIdentifierDependencyNodeId);
        }

        if (node.Left != null)
        {
            CollectDocumentVariableNodeDependenciesRecursive(
                node.Left,
                formulaContext,
                currentDocumentId,
                compiledDocumentVariablesByDocumentId,
                dependencyNodeIds);
        }

        if (node.Right != null)
        {
            CollectDocumentVariableNodeDependenciesRecursive(
                node.Right,
                formulaContext,
                currentDocumentId,
                compiledDocumentVariablesByDocumentId,
                dependencyNodeIds);
        }

        if (node.Third != null)
        {
            CollectDocumentVariableNodeDependenciesRecursive(
                node.Third,
                formulaContext,
                currentDocumentId,
                compiledDocumentVariablesByDocumentId,
                dependencyNodeIds);
        }

        if (node.Callee != null)
        {
            CollectDocumentVariableNodeDependenciesRecursive(
                node.Callee,
                formulaContext,
                currentDocumentId,
                compiledDocumentVariablesByDocumentId,
                dependencyNodeIds);
        }

        if (node.Arguments != null)
        {
            for (int argumentIndex = 0; argumentIndex < node.Arguments.Count; argumentIndex++)
            {
                CollectDocumentVariableNodeDependenciesRecursive(
                    node.Arguments[argumentIndex],
                    formulaContext,
                    currentDocumentId,
                    compiledDocumentVariablesByDocumentId,
                    dependencyNodeIds);
            }
        }
    }

    private static bool TryResolveDocumentVariableReference(
        ExpressionNode node,
        IFormulaContext formulaContext,
        string? currentDocumentId,
        out string documentId,
        out string variableName)
    {
        documentId = "";
        variableName = "";
        if (node.Kind != ExpressionNodeKind.MemberAccess ||
            node.Left == null ||
            !DocumentFormulaSyntax.IsValidIdentifier(node.Text.AsSpan()))
        {
            return false;
        }

        if (node.Left.Kind == ExpressionNodeKind.Identifier &&
            string.Equals(node.Left.Text, "thisDoc", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(currentDocumentId))
            {
                return false;
            }

            if (!formulaContext.TryGetDocumentById(currentDocumentId, out _))
            {
                return false;
            }

            documentId = currentDocumentId;
            variableName = node.Text;
            return true;
        }

        if (node.Left.Kind == ExpressionNodeKind.MemberAccess &&
            node.Left.Left != null &&
            node.Left.Left.Kind == ExpressionNodeKind.Identifier &&
            string.Equals(node.Left.Left.Text, "docs", StringComparison.OrdinalIgnoreCase))
        {
            string documentAlias = node.Left.Text;
            if (!DocumentFormulaSyntax.IsValidIdentifier(documentAlias.AsSpan()))
            {
                return false;
            }

            if (!formulaContext.TryGetDocumentByAlias(documentAlias, out var document))
            {
                return false;
            }

            documentId = document.Id;
            variableName = node.Text;
            return true;
        }

        return false;
    }

    private static bool TryResolveDocumentVariableReferenceNodeId(
        ExpressionNode node,
        IFormulaContext formulaContext,
        string? currentDocumentId,
        Dictionary<string, Dictionary<string, CompiledFormula>> compiledDocumentVariablesByDocumentId,
        out string documentVariableNodeId)
    {
        documentVariableNodeId = "";
        if (!TryResolveDocumentVariableReference(
                node,
                formulaContext,
                currentDocumentId,
                out string documentId,
                out string variableName))
        {
            return false;
        }

        if (!compiledDocumentVariablesByDocumentId.TryGetValue(documentId, out var compiledByVariableName) ||
            !compiledByVariableName.ContainsKey(variableName))
        {
            return false;
        }

        documentVariableNodeId = CreateDocumentVariableNodeId(documentId, variableName);
        return true;
    }

    private static bool TryResolveCurrentDocumentAtIdentifierNodeId(
        ExpressionNode node,
        string? currentDocumentId,
        Dictionary<string, Dictionary<string, CompiledFormula>> compiledDocumentVariablesByDocumentId,
        out string documentVariableNodeId)
    {
        documentVariableNodeId = "";
        if (node.Kind != ExpressionNodeKind.AtIdentifier ||
            string.IsNullOrWhiteSpace(currentDocumentId) ||
            !DocumentFormulaSyntax.IsValidIdentifier(node.Text.AsSpan()))
        {
            return false;
        }

        if (!compiledDocumentVariablesByDocumentId.TryGetValue(currentDocumentId, out var compiledByVariableName) ||
            !compiledByVariableName.ContainsKey(node.Text))
        {
            return false;
        }

        documentVariableNodeId = CreateDocumentVariableNodeId(currentDocumentId, node.Text);
        return true;
    }

    private static string FindDependencyCyclePath(
        IReadOnlyList<string> orderedNodeIds,
        Dictionary<string, List<string>> edges)
    {
        // DFS with an explicit recursion stack to find any back-edge.
        var state = new Dictionary<string, byte>(edges.Count, StringComparer.Ordinal); // 0=unvisited,1=visiting,2=done
        var stack = new List<string>(edges.Count);

        for (int i = 0; i < orderedNodeIds.Count; i++)
        {
            string node = orderedNodeIds[i];
            if (state.TryGetValue(node, out var s) && s != 0)
            {
                continue;
            }

            if (DfsFindCycle(node, edges, state, stack, out var path))
            {
                return path;
            }
        }

        return "";
    }

    private static bool DfsFindCycle(
        string node,
        Dictionary<string, List<string>> edges,
        Dictionary<string, byte> state,
        List<string> stack,
        out string path)
    {
        path = "";
        state[node] = 1;
        stack.Add(node);

        if (edges.TryGetValue(node, out var deps))
        {
            for (int i = 0; i < deps.Count; i++)
            {
                var next = deps[i];
                if (!state.TryGetValue(next, out var s))
                {
                    s = 0;
                }

                if (s == 0)
                {
                    if (DfsFindCycle(next, edges, state, stack, out path))
                        return true;
                }
                else if (s == 1)
                {
                    // Found a back-edge to "next". Extract cycle segment.
                    int start = stack.LastIndexOf(next);
                    if (start < 0) start = 0;
                    path = string.Join(" -> ", stack.GetRange(start, stack.Count - start)) + " -> " + next;
                    return true;
                }
            }
        }

        stack.RemoveAt(stack.Count - 1);
        state[node] = 2;
        return false;
    }

    private static EvaluationFrame CreateRootEvaluationFrame(
        IFormulaContext formulaContext,
        DocTable table,
        DocRow row,
        int rowIndexOneBased)
    {
        if (TryResolveParentRowContext(formulaContext, table, row, out var parentTable, out var parentRow))
        {
            int parentRowIndexOneBased = formulaContext.GetRowIndexOneBased(parentTable, parentRow);
            return new EvaluationFrame(
                currentTable: table,
                currentRow: row,
                currentRowIndexOneBased: rowIndexOneBased,
                currentDocument: null,
                candidateTable: null,
                candidateRow: null,
                candidateRowIndexOneBased: 0,
                parentTable: parentTable,
                parentRow: parentRow,
                parentRowIndexOneBased: parentRowIndexOneBased);
        }

        return new EvaluationFrame(
            currentTable: table,
            currentRow: row,
            currentRowIndexOneBased: rowIndexOneBased,
            currentDocument: null,
            candidateTable: null,
            candidateRow: null,
            candidateRowIndexOneBased: 0,
            parentTable: null,
            parentRow: null,
            parentRowIndexOneBased: 0);
    }

    private static EvaluationFrame CreateDocumentEvaluationFrame(DocDocument document)
    {
        return new EvaluationFrame(
            currentTable: null,
            currentRow: null,
            currentRowIndexOneBased: 0,
            currentDocument: document,
            candidateTable: null,
            candidateRow: null,
            candidateRowIndexOneBased: 0,
            parentTable: null,
            parentRow: null,
            parentRowIndexOneBased: 0);
    }

    private static EvaluationFrame CreateTableEvaluationFrame(DocTable table)
    {
        return new EvaluationFrame(
            currentTable: table,
            currentRow: null,
            currentRowIndexOneBased: 0,
            currentDocument: null,
            candidateTable: null,
            candidateRow: null,
            candidateRowIndexOneBased: 0,
            parentTable: null,
            parentRow: null,
            parentRowIndexOneBased: 0);
    }

    private static EvaluationFrame CreateCandidateEvaluationFrame(
        in EvaluationFrame baseFrame,
        DocTable candidateTable,
        DocRow candidateRow,
        int candidateRowIndexOneBased)
    {
        return new EvaluationFrame(
            currentTable: baseFrame.CurrentTable,
            currentRow: baseFrame.CurrentRow,
            currentRowIndexOneBased: baseFrame.CurrentRowIndexOneBased,
            currentDocument: baseFrame.CurrentDocument,
            candidateTable: candidateTable,
            candidateRow: candidateRow,
            candidateRowIndexOneBased: candidateRowIndexOneBased,
            parentTable: baseFrame.ParentTable,
            parentRow: baseFrame.ParentRow,
            parentRowIndexOneBased: baseFrame.ParentRowIndexOneBased);
    }

    private static bool TryResolveParentRowContext(
        IFormulaContext formulaContext,
        DocTable table,
        DocRow row,
        out DocTable parentTable,
        out DocRow parentRow)
    {
        parentTable = null!;
        parentRow = null!;
        if (string.IsNullOrWhiteSpace(table.ParentTableId) ||
            string.IsNullOrWhiteSpace(table.ParentRowColumnId))
        {
            return false;
        }

        if (!formulaContext.TryGetTableById(table.ParentTableId, out parentTable))
        {
            return false;
        }

        string parentRowId = row.GetCell(table.ParentRowColumnId).StringValue ?? "";
        if (string.IsNullOrWhiteSpace(parentRowId))
        {
            return false;
        }

        return formulaContext.TryGetRowById(parentTable, parentRowId, out parentRow);
    }

    private static void EvaluateTable(
        DocTable table,
        Dictionary<string, CompiledFormula>? compiledColumns,
        List<DocColumn>? orderedFormulaColumns,
        Dictionary<string, List<CompiledCellFormula>>? compiledCellFormulasByRowId,
        IFormulaContext formulaContext,
        FormulaEvaluator evaluator,
        List<string>? targetFormulaColumnIds)
    {
        List<DocColumn>? formulaColumnsToEvaluate = null;
        if (compiledColumns != null && orderedFormulaColumns != null)
        {
            formulaColumnsToEvaluate = ResolveFormulaColumnsToEvaluate(
                compiledColumns,
                orderedFormulaColumns,
                targetFormulaColumnIds);
        }

        if ((formulaColumnsToEvaluate == null || formulaColumnsToEvaluate.Count == 0) &&
            (compiledCellFormulasByRowId == null || compiledCellFormulasByRowId.Count == 0))
        {
            return;
        }

        Dictionary<string, DocColumn>? columnById = null;
        if (compiledCellFormulasByRowId != null && compiledCellFormulasByRowId.Count > 0)
        {
            columnById = new Dictionary<string, DocColumn>(table.Columns.Count, StringComparer.Ordinal);
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                columnById[table.Columns[columnIndex].Id] = table.Columns[columnIndex];
            }
        }

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            if (formulaColumnsToEvaluate != null)
            {
                for (int formulaColumnIndex = 0; formulaColumnIndex < formulaColumnsToEvaluate.Count; formulaColumnIndex++)
                {
                    var column = formulaColumnsToEvaluate[formulaColumnIndex];
                    if (compiledColumns == null || !compiledColumns.TryGetValue(column.Id, out var compiledFormula))
                    {
                        continue;
                    }

                    if (!compiledFormula.IsValid)
                    {
                        row.SetCell(column.Id, CreateFormulaErrorCell());
                        continue;
                    }

                    FormulaValue value;
                    try
                    {
                        var frame = CreateRootEvaluationFrame(formulaContext, table, row, rowIndex + 1);
                        value = evaluator.Evaluate(compiledFormula.Root, frame);
                    }
                    catch
                    {
                        row.SetCell(column.Id, CreateFormulaErrorCell());
                        continue;
                    }

                    row.SetCell(column.Id, ConvertFormulaResultToCellValue(value, table, column, formulaContext));
                }
            }

            if (compiledCellFormulasByRowId == null ||
                !compiledCellFormulasByRowId.TryGetValue(row.Id, out List<CompiledCellFormula>? rowCellFormulas) ||
                rowCellFormulas.Count <= 0 ||
                columnById == null)
            {
                continue;
            }

            for (int cellFormulaIndex = 0; cellFormulaIndex < rowCellFormulas.Count; cellFormulaIndex++)
            {
                CompiledCellFormula compiledCellFormula = rowCellFormulas[cellFormulaIndex];
                if (!columnById.TryGetValue(compiledCellFormula.ColumnId, out DocColumn? outputColumn))
                {
                    continue;
                }

                if (!compiledCellFormula.CompiledFormula.IsValid)
                {
                    row.SetCell(outputColumn.Id, CreateFormulaErrorCell(compiledCellFormula.FormulaExpression));
                    continue;
                }

                FormulaValue value;
                try
                {
                    var frame = CreateRootEvaluationFrame(formulaContext, table, row, rowIndex + 1);
                    value = evaluator.Evaluate(compiledCellFormula.CompiledFormula.Root, frame);
                }
                catch
                {
                    row.SetCell(outputColumn.Id, CreateFormulaErrorCell(compiledCellFormula.FormulaExpression));
                    continue;
                }

                DocCellValue evaluatedCellValue = ConvertFormulaResultToCellValue(value, table, outputColumn, formulaContext);
                evaluatedCellValue.CellFormulaExpression = compiledCellFormula.FormulaExpression;
                row.SetCell(outputColumn.Id, evaluatedCellValue);
            }
        }
    }

    private static List<DocColumn>? ResolveFormulaColumnsToEvaluate(
        Dictionary<string, CompiledFormula> compiledColumns,
        List<DocColumn> orderedFormulaColumns,
        List<string>? targetFormulaColumnIds)
    {
        if (orderedFormulaColumns.Count == 0)
        {
            return null;
        }

        if (targetFormulaColumnIds == null || targetFormulaColumnIds.Count == 0)
        {
            return orderedFormulaColumns;
        }

        var formulaColumnIdByName = new Dictionary<string, string>(orderedFormulaColumns.Count, StringComparer.OrdinalIgnoreCase);
        for (int columnIndex = 0; columnIndex < orderedFormulaColumns.Count; columnIndex++)
        {
            var formulaColumn = orderedFormulaColumns[columnIndex];
            if (!formulaColumnIdByName.ContainsKey(formulaColumn.Name))
            {
                formulaColumnIdByName[formulaColumn.Name] = formulaColumn.Id;
            }
        }

        var requiredColumnIds = new HashSet<string>(StringComparer.Ordinal);
        for (int targetIndex = 0; targetIndex < targetFormulaColumnIds.Count; targetIndex++)
        {
            string targetFormulaColumnId = targetFormulaColumnIds[targetIndex];
            if (string.IsNullOrWhiteSpace(targetFormulaColumnId))
            {
                continue;
            }

            AddRequiredFormulaColumnIds(
                targetFormulaColumnId,
                compiledColumns,
                formulaColumnIdByName,
                requiredColumnIds);
        }

        if (requiredColumnIds.Count == 0)
        {
            return null;
        }

        var resolvedColumns = new List<DocColumn>(Math.Min(requiredColumnIds.Count, orderedFormulaColumns.Count));
        for (int columnIndex = 0; columnIndex < orderedFormulaColumns.Count; columnIndex++)
        {
            var formulaColumn = orderedFormulaColumns[columnIndex];
            if (requiredColumnIds.Contains(formulaColumn.Id))
            {
                resolvedColumns.Add(formulaColumn);
            }
        }

        return resolvedColumns;
    }

    private static void AddRequiredFormulaColumnIds(
        string formulaColumnId,
        Dictionary<string, CompiledFormula> compiledColumns,
        Dictionary<string, string> formulaColumnIdByName,
        HashSet<string> requiredColumnIds)
    {
        if (!requiredColumnIds.Add(formulaColumnId))
        {
            return;
        }

        if (!compiledColumns.TryGetValue(formulaColumnId, out var compiledFormula) ||
            !compiledFormula.IsValid ||
            compiledFormula.ThisRowColumnNames.Count == 0)
        {
            return;
        }

        foreach (var dependencyColumnName in compiledFormula.ThisRowColumnNames)
        {
            if (!formulaColumnIdByName.TryGetValue(dependencyColumnName, out string? dependencyFormulaColumnId) ||
                string.IsNullOrWhiteSpace(dependencyFormulaColumnId))
            {
                continue;
            }

            AddRequiredFormulaColumnIds(
                dependencyFormulaColumnId,
                compiledColumns,
                formulaColumnIdByName,
                requiredColumnIds);
        }
    }

    private static List<DocColumn> BuildFormulaColumnEvaluationOrder(
        DocTable table,
        Dictionary<string, CompiledFormula> compiledColumns)
    {
        var formulaColumns = new List<DocColumn>();
        var formulaColumnByName = new Dictionary<string, DocColumn>(StringComparer.OrdinalIgnoreCase);
        var formulaColumnById = new Dictionary<string, DocColumn>(StringComparer.Ordinal);

        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var column = table.Columns[columnIndex];
            if (!compiledColumns.ContainsKey(column.Id))
            {
                continue;
            }

            formulaColumns.Add(column);
            formulaColumnById[column.Id] = column;
            if (!formulaColumnByName.ContainsKey(column.Name))
            {
                formulaColumnByName[column.Name] = column;
            }
        }

        if (formulaColumns.Count <= 1)
        {
            return formulaColumns;
        }

        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var edgeSets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var indegreeByColumnId = new Dictionary<string, int>(StringComparer.Ordinal);
        var columnIndexById = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int columnIndex = 0; columnIndex < formulaColumns.Count; columnIndex++)
        {
            var column = formulaColumns[columnIndex];
            edges[column.Id] = new List<string>(4);
            edgeSets[column.Id] = new HashSet<string>(StringComparer.Ordinal);
            indegreeByColumnId[column.Id] = 0;
            columnIndexById[column.Id] = columnIndex;
        }

        for (int columnIndex = 0; columnIndex < formulaColumns.Count; columnIndex++)
        {
            var column = formulaColumns[columnIndex];
            if (!compiledColumns.TryGetValue(column.Id, out var compiledFormula))
            {
                continue;
            }

            foreach (var dependencyColumnName in compiledFormula.ThisRowColumnNames)
            {
                if (!formulaColumnByName.TryGetValue(dependencyColumnName, out var dependencyColumn))
                {
                    continue;
                }

                if (dependencyColumn.Id == column.Id)
                {
                    continue;
                }

                if (edgeSets[dependencyColumn.Id].Add(column.Id))
                {
                    edges[dependencyColumn.Id].Add(column.Id);
                    indegreeByColumnId[column.Id]++;
                }
            }
        }

        // Deterministic: sort adjacency lists by formula column order within the table.
        foreach (var kvp in edges)
        {
            kvp.Value.Sort((a, b) => columnIndexById[a].CompareTo(columnIndexById[b]));
        }

        var orderedColumns = new List<DocColumn>(formulaColumns.Count);
        var processingQueue = new Queue<DocColumn>();
        for (int columnIndex = 0; columnIndex < formulaColumns.Count; columnIndex++)
        {
            var column = formulaColumns[columnIndex];
            if (indegreeByColumnId[column.Id] == 0)
            {
                processingQueue.Enqueue(column);
            }
        }

        while (processingQueue.Count > 0)
        {
            var column = processingQueue.Dequeue();
            orderedColumns.Add(column);

            var dependents = edges[column.Id];
            for (int i = 0; i < dependents.Count; i++)
            {
                string dependentColumnId = dependents[i];
                indegreeByColumnId[dependentColumnId]--;
                if (indegreeByColumnId[dependentColumnId] == 0)
                {
                    processingQueue.Enqueue(formulaColumnById[dependentColumnId]);
                }
            }
        }

        if (orderedColumns.Count == formulaColumns.Count)
        {
            return orderedColumns;
        }

        return formulaColumns;
    }

    private static CompiledFormula CompileFormula(string formulaText)
    {
        var lexer = new FormulaLexer(formulaText);
        var tokens = lexer.Tokenize();

        if (lexer.HasErrors)
        {
            return CompiledFormula.Invalid();
        }

        var parser = new FormulaParser(tokens);
        var expression = parser.ParseExpression();
        if (parser.HasErrors || expression == null)
        {
            return CompiledFormula.Invalid();
        }

        var typeChecker = new FormulaTypeChecker();
        if (!typeChecker.Validate(expression))
        {
            return CompiledFormula.Invalid();
        }

        var flattener = new FormulaFlattener();
        flattener.Flatten(expression);

        var thisRowDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referencedTableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool referencesParentScope = false;
        bool referencesDocumentScope = false;
        CollectDependencies(
            expression,
            thisRowDependencies,
            referencedTableNames,
            ref referencesParentScope,
            ref referencesDocumentScope);

        return new CompiledFormula(
            expression,
            thisRowDependencies,
            referencedTableNames,
            referencesParentScope,
            referencesDocumentScope);
    }

    private static void CollectDependencies(
        ExpressionNode node,
        HashSet<string> thisRowDependencies,
        HashSet<string> referencedTableNames,
        ref bool referencesParentScope,
        ref bool referencesDocumentScope)
    {
        if (node.Kind == ExpressionNodeKind.Identifier)
        {
            if (string.Equals(node.Text, "parentRow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Text, "parentTable", StringComparison.OrdinalIgnoreCase))
            {
                referencesParentScope = true;
            }

            if (string.Equals(node.Text, "docs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Text, "thisDoc", StringComparison.OrdinalIgnoreCase))
            {
                referencesDocumentScope = true;
            }
        }

        if (node.Kind == ExpressionNodeKind.MemberAccess && node.Left != null)
        {
            if (node.Left.Kind == ExpressionNodeKind.Identifier &&
                string.Equals(node.Left.Text, "thisRow", StringComparison.OrdinalIgnoreCase))
            {
                thisRowDependencies.Add(node.Text);
            }
            else if (node.Left.Kind == ExpressionNodeKind.Identifier &&
                     string.Equals(node.Left.Text, "thisTable", StringComparison.OrdinalIgnoreCase))
            {
                // Table variables are resolved at runtime and do not contribute table-level dependencies.
            }
            else if (node.Left.Kind == ExpressionNodeKind.Identifier &&
                     string.Equals(node.Left.Text, "tables", StringComparison.OrdinalIgnoreCase))
            {
                referencedTableNames.Add(node.Text);
            }
            else if (node.Left.Kind == ExpressionNodeKind.Identifier &&
                     (string.Equals(node.Left.Text, "parentRow", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(node.Left.Text, "parentTable", StringComparison.OrdinalIgnoreCase)))
            {
                referencesParentScope = true;
            }
            else if (node.Left.Kind == ExpressionNodeKind.Identifier &&
                     (string.Equals(node.Left.Text, "docs", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(node.Left.Text, "thisDoc", StringComparison.OrdinalIgnoreCase)))
            {
                referencesDocumentScope = true;
            }
            else if (node.Left.Kind == ExpressionNodeKind.Identifier)
            {
                referencedTableNames.Add(node.Left.Text);
            }
        }

        if (node.Kind == ExpressionNodeKind.Call && node.Callee != null)
        {
            bool firstArgumentReferencesTable = false;
            if (node.Callee.Kind == ExpressionNodeKind.Identifier)
            {
                firstArgumentReferencesTable =
                    string.Equals(node.Callee.Text, "Lookup", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.Callee.Text, "CountIf", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.Callee.Text, "SumIf", StringComparison.OrdinalIgnoreCase) ||
                    FormulaFunctionRegistry.RequiresFirstArgumentTableDependency(node.Callee.Text);
            }

            if (firstArgumentReferencesTable &&
                node.Arguments != null &&
                node.Arguments.Count > 0)
            {
                var tableArgument = node.Arguments[0];
                if (tableArgument.Kind == ExpressionNodeKind.Identifier)
                {
                    referencedTableNames.Add(tableArgument.Text);
                }
                else if (tableArgument.Kind == ExpressionNodeKind.StringLiteral)
                {
                    referencedTableNames.Add(tableArgument.Text);
                }
            }
        }

        if (node.Left != null)
        {
            CollectDependencies(
                node.Left,
                thisRowDependencies,
                referencedTableNames,
                ref referencesParentScope,
                ref referencesDocumentScope);
        }

        if (node.Right != null)
        {
            CollectDependencies(
                node.Right,
                thisRowDependencies,
                referencedTableNames,
                ref referencesParentScope,
                ref referencesDocumentScope);
        }

        if (node.Third != null)
        {
            CollectDependencies(
                node.Third,
                thisRowDependencies,
                referencedTableNames,
                ref referencesParentScope,
                ref referencesDocumentScope);
        }

        if (node.Callee != null)
        {
            CollectDependencies(
                node.Callee,
                thisRowDependencies,
                referencedTableNames,
                ref referencesParentScope,
                ref referencesDocumentScope);
        }

        if (node.Arguments != null)
        {
            for (int argumentIndex = 0; argumentIndex < node.Arguments.Count; argumentIndex++)
            {
                CollectDependencies(
                    node.Arguments[argumentIndex],
                    thisRowDependencies,
                    referencedTableNames,
                    ref referencesParentScope,
                    ref referencesDocumentScope);
            }
        }
    }

    private static DocCellValue ConvertFormulaResultToCellValue(
        FormulaValue value,
        DocTable sourceTable,
        DocColumn outputColumn,
        IFormulaContext formulaContext)
    {
        DocCellValue convertedCellValue;
        switch (outputColumn.Kind)
        {
            case DocColumnKind.Number:
                convertedCellValue = value.Kind == FormulaValueKind.Number
                    ? DocCellValue.Number(value.NumberValue)
                    : CreateFormulaErrorCell();
                break;
            case DocColumnKind.Checkbox:
                convertedCellValue = value.Kind == FormulaValueKind.Bool
                    ? DocCellValue.Bool(value.BoolValue)
                    : CreateFormulaErrorCell();
                break;
            case DocColumnKind.Relation:
                convertedCellValue = ConvertFormulaResultToRelationCellValue(
                    value,
                    sourceTable,
                    outputColumn,
                    formulaContext);
                break;
            case DocColumnKind.Formula:
                convertedCellValue = ConvertLegacyFormulaResultToCellValue(value, formulaContext);
                break;
            case DocColumnKind.TableRef:
                convertedCellValue = value.Kind switch
                {
                    FormulaValueKind.TableReference => DocCellValue.Text(value.TableValue?.Id ?? ""),
                    FormulaValueKind.String => DocCellValue.Text(value.StringValue ?? ""),
                    FormulaValueKind.Null => DocCellValue.Text(""),
                    _ => CreateFormulaErrorCell()
                };
                break;
            case DocColumnKind.Id:
            case DocColumnKind.Text:
            case DocColumnKind.Select:
            case DocColumnKind.TextureAsset:
            case DocColumnKind.MeshAsset:
            case DocColumnKind.AudioAsset:
            case DocColumnKind.UiAsset:
                convertedCellValue = value.Kind switch
                {
                    FormulaValueKind.String => DocCellValue.Text(value.StringValue ?? ""),
                    FormulaValueKind.Null => DocCellValue.Text(""),
                    _ => CreateFormulaErrorCell()
                };
                break;
            case DocColumnKind.Vec2:
                convertedCellValue = value.Kind switch
                {
                    FormulaValueKind.Vec2 => DocCellValue.Vec2(value.XValue, value.YValue),
                    FormulaValueKind.Vec3 => DocCellValue.Vec2(value.XValue, value.YValue),
                    FormulaValueKind.Vec4 => DocCellValue.Vec2(value.XValue, value.YValue),
                    FormulaValueKind.Color => DocCellValue.Vec2(value.XValue, value.YValue),
                    _ => CreateFormulaErrorCell()
                };
                break;
            case DocColumnKind.Vec3:
                convertedCellValue = value.Kind switch
                {
                    FormulaValueKind.Vec3 => DocCellValue.Vec3(value.XValue, value.YValue, value.ZValue),
                    FormulaValueKind.Vec4 => DocCellValue.Vec3(value.XValue, value.YValue, value.ZValue),
                    FormulaValueKind.Color => DocCellValue.Vec3(value.XValue, value.YValue, value.ZValue),
                    _ => CreateFormulaErrorCell()
                };
                break;
            case DocColumnKind.Vec4:
                convertedCellValue = value.Kind switch
                {
                    FormulaValueKind.Vec4 => DocCellValue.Vec4(value.XValue, value.YValue, value.ZValue, value.WValue),
                    FormulaValueKind.Color => DocCellValue.Vec4(value.XValue, value.YValue, value.ZValue, value.WValue),
                    _ => CreateFormulaErrorCell()
                };
                break;
            case DocColumnKind.Color:
                convertedCellValue = value.Kind switch
                {
                    FormulaValueKind.Color => DocCellValue.Color(value.XValue, value.YValue, value.ZValue, value.WValue),
                    FormulaValueKind.Vec4 => DocCellValue.Color(value.XValue, value.YValue, value.ZValue, value.WValue),
                    _ => CreateFormulaErrorCell()
                };
                break;
            default:
                convertedCellValue = CreateFormulaErrorCell();
                break;
        }

        if (!string.Equals(convertedCellValue.StringValue, FormulaErrorText, StringComparison.Ordinal))
        {
            convertedCellValue = DocCellValueNormalizer.NormalizeForColumn(outputColumn, convertedCellValue);
        }

        return convertedCellValue;
    }

    private static DocCellValue ConvertFormulaResultToRelationCellValue(
        FormulaValue value,
        DocTable sourceTable,
        DocColumn relationColumn,
        IFormulaContext formulaContext)
    {
        string? relationTableId = DocRelationTargetResolver.ResolveTargetTableId(sourceTable, relationColumn);
        if (value.Kind == FormulaValueKind.RowReference && value.TableValue != null && value.RowValue != null)
        {
            if (!string.IsNullOrEmpty(relationTableId) &&
                string.Equals(value.TableValue.Id, relationTableId, StringComparison.Ordinal))
            {
                return DocCellValue.Text(value.RowValue.Id);
            }

            return CreateFormulaErrorCell();
        }

        if (value.Kind == FormulaValueKind.String)
        {
            string relationRowId = value.StringValue ?? "";
            if (string.IsNullOrEmpty(relationRowId))
            {
                return DocCellValue.Text("");
            }

            if (string.IsNullOrEmpty(relationTableId))
            {
                return CreateFormulaErrorCell();
            }

            if (!formulaContext.TryGetTableById(relationTableId, out var relationTable))
            {
                return CreateFormulaErrorCell();
            }

            if (!formulaContext.TryGetRowById(relationTable, relationRowId, out _))
            {
                return CreateFormulaErrorCell();
            }

            return DocCellValue.Text(relationRowId);
        }

        return CreateFormulaErrorCell();
    }

    private static DocCellValue ConvertLegacyFormulaResultToCellValue(FormulaValue value, IFormulaContext formulaContext)
    {
        return value.Kind switch
        {
            FormulaValueKind.Number => DocCellValue.Number(value.NumberValue),
            FormulaValueKind.Bool => DocCellValue.Text(value.BoolValue ? "true" : "false"),
            FormulaValueKind.DateTime => DocCellValue.Text(value.DateTimeValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            FormulaValueKind.RowReference => value.TableValue != null && value.RowValue != null
                ? DocCellValue.Text(formulaContext.GetRowDisplayLabel(value.TableValue, value.RowValue))
                : DocCellValue.Text(""),
            FormulaValueKind.String => DocCellValue.Text(value.StringValue ?? ""),
            FormulaValueKind.Vec2 => DocCellValue.Text(FormatVectorText(value, 2)),
            FormulaValueKind.Vec3 => DocCellValue.Text(FormatVectorText(value, 3)),
            FormulaValueKind.Vec4 => DocCellValue.Text(FormatVectorText(value, 4)),
            FormulaValueKind.Color => DocCellValue.Text(FormatColorText(value)),
            _ => DocCellValue.Text("")
        };
    }

    private static string ConvertFormulaResultToDisplayText(FormulaValue value, IFormulaContext formulaContext)
    {
        return value.Kind switch
        {
            FormulaValueKind.Number => value.NumberValue.ToString("G", CultureInfo.InvariantCulture),
            FormulaValueKind.Bool => value.BoolValue ? "true" : "false",
            FormulaValueKind.DateTime => value.DateTimeValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FormulaValueKind.RowReference => value.TableValue != null && value.RowValue != null
                ? formulaContext.GetRowDisplayLabel(value.TableValue, value.RowValue)
                : "",
            FormulaValueKind.TableReference => value.TableValue?.Name ?? "",
            FormulaValueKind.DocumentReference => value.DocumentValue?.Title ?? "",
            FormulaValueKind.RowCollection => value.RowsValue != null
                ? value.RowsValue.Count.ToString(CultureInfo.InvariantCulture)
                : "0",
            FormulaValueKind.String => value.StringValue ?? "",
            FormulaValueKind.Vec2 => FormatVectorText(value, 2),
            FormulaValueKind.Vec3 => FormatVectorText(value, 3),
            FormulaValueKind.Vec4 => FormatVectorText(value, 4),
            FormulaValueKind.Color => FormatColorText(value),
            _ => ""
        };
    }

    private static string FormatVectorText(FormulaValue value, int dimension)
    {
        return dimension switch
        {
            2 => "(" + value.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 value.YValue.ToString("G", CultureInfo.InvariantCulture) + ")",
            3 => "(" + value.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 value.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 value.ZValue.ToString("G", CultureInfo.InvariantCulture) + ")",
            _ => "(" + value.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 value.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 value.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
                 value.WValue.ToString("G", CultureInfo.InvariantCulture) + ")",
        };
    }

    private static string FormatColorText(FormulaValue value)
    {
        return "rgba(" +
               value.XValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
               value.YValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
               value.ZValue.ToString("G", CultureInfo.InvariantCulture) + ", " +
               value.WValue.ToString("G", CultureInfo.InvariantCulture) + ")";
    }

    private static DocCellValue CreateFormulaErrorCell(string? cellFormulaExpression = null)
    {
        var errorCell = DocCellValue.Text(FormulaErrorText);
        errorCell.FormulaError = FormulaErrorText;
        if (!string.IsNullOrWhiteSpace(cellFormulaExpression))
        {
            errorCell.CellFormulaExpression = cellFormulaExpression;
        }

        return errorCell;
    }

    private readonly struct EvaluationFrame
    {
        public DocTable? CurrentTable { get; }
        public DocRow? CurrentRow { get; }
        public int CurrentRowIndexOneBased { get; }
        public DocDocument? CurrentDocument { get; }
        public DocTable? CandidateTable { get; }
        public DocRow? CandidateRow { get; }
        public int CandidateRowIndexOneBased { get; }
        public DocTable? ParentTable { get; }
        public DocRow? ParentRow { get; }
        public int ParentRowIndexOneBased { get; }

        public EvaluationFrame(
            DocTable? currentTable,
            DocRow? currentRow,
            int currentRowIndexOneBased,
            DocDocument? currentDocument,
            DocTable? candidateTable,
            DocRow? candidateRow,
            int candidateRowIndexOneBased,
            DocTable? parentTable,
            DocRow? parentRow,
            int parentRowIndexOneBased)
        {
            CurrentTable = currentTable;
            CurrentRow = currentRow;
            CurrentRowIndexOneBased = currentRowIndexOneBased;
            CurrentDocument = currentDocument;
            CandidateTable = candidateTable;
            CandidateRow = candidateRow;
            CandidateRowIndexOneBased = candidateRowIndexOneBased;
            ParentTable = parentTable;
            ParentRow = parentRow;
            ParentRowIndexOneBased = parentRowIndexOneBased;
        }
    }

    private sealed class CompiledFormula
    {
        public ExpressionNode Root { get; }
        public HashSet<string> ThisRowColumnNames { get; }
        public HashSet<string> ReferencedTableNames { get; }
        public bool ReferencesParentScope { get; }
        public bool ReferencesDocumentScope { get; }
        public bool IsValid { get; }

        private CompiledFormula(
            bool isValid,
            ExpressionNode root,
            HashSet<string> thisRowColumnNames,
            HashSet<string> referencedTableNames,
            bool referencesParentScope,
            bool referencesDocumentScope)
        {
            IsValid = isValid;
            Root = root;
            ThisRowColumnNames = thisRowColumnNames;
            ReferencedTableNames = referencedTableNames;
            ReferencesParentScope = referencesParentScope;
            ReferencesDocumentScope = referencesDocumentScope;
        }

        public CompiledFormula(
            ExpressionNode root,
            HashSet<string> thisRowColumnNames,
            HashSet<string> referencedTableNames,
            bool referencesParentScope,
            bool referencesDocumentScope)
            : this(
                true,
                root,
                thisRowColumnNames,
                referencedTableNames,
                referencesParentScope,
                referencesDocumentScope)
        {
        }

        public static CompiledFormula Invalid()
        {
            return new CompiledFormula(
                false,
                ExpressionNode.NullLiteral(),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                referencesParentScope: false,
                referencesDocumentScope: false);
        }
    }

    private sealed class FormulaLexer
    {
        private readonly string _text;
        private readonly List<FormulaToken> _tokens = new();
        private int _index;

        public bool HasErrors { get; private set; }

        public FormulaLexer(string text)
        {
            _text = text;
        }

        public List<FormulaToken> Tokenize()
        {
            while (!IsAtEnd())
            {
                char currentChar = Peek();
                if (char.IsWhiteSpace(currentChar))
                {
                    _index++;
                    continue;
                }

                if (currentChar == '@')
                {
                    TokenizeAtIdentifier();
                    continue;
                }

                if (char.IsLetter(currentChar) || currentChar == '_')
                {
                    TokenizeIdentifier();
                    continue;
                }

                if (char.IsDigit(currentChar))
                {
                    TokenizeNumber();
                    continue;
                }

                if (currentChar == '"')
                {
                    TokenizeString();
                    continue;
                }

                switch (currentChar)
                {
                    case '(':
                        AddToken(FormulaTokenKind.LeftParen);
                        _index++;
                        break;
                    case ')':
                        AddToken(FormulaTokenKind.RightParen);
                        _index++;
                        break;
                    case '.':
                        AddToken(FormulaTokenKind.Dot);
                        _index++;
                        break;
                    case ',':
                        AddToken(FormulaTokenKind.Comma);
                        _index++;
                        break;
                    case '?':
                        AddToken(FormulaTokenKind.Question);
                        _index++;
                        break;
                    case ':':
                        AddToken(FormulaTokenKind.Colon);
                        _index++;
                        break;
                    case '+':
                        AddToken(FormulaTokenKind.Plus);
                        _index++;
                        break;
                    case '-':
                        AddToken(FormulaTokenKind.Minus);
                        _index++;
                        break;
                    case '*':
                        AddToken(FormulaTokenKind.Star);
                        _index++;
                        break;
                    case '/':
                        AddToken(FormulaTokenKind.Slash);
                        _index++;
                        break;
                    case '%':
                        AddToken(FormulaTokenKind.Percent);
                        _index++;
                        break;
                    case '!':
                        if (Match('='))
                        {
                            AddToken(FormulaTokenKind.BangEqual);
                        }
                        else
                        {
                            AddToken(FormulaTokenKind.Bang);
                            _index++;
                        }
                        break;
                    case '=':
                        if (Match('='))
                        {
                            AddToken(FormulaTokenKind.EqualEqual);
                        }
                        else
                        {
                            HasErrors = true;
                            _index++;
                        }
                        break;
                    case '>':
                        if (Match('='))
                        {
                            AddToken(FormulaTokenKind.GreaterEqual);
                        }
                        else
                        {
                            AddToken(FormulaTokenKind.Greater);
                            _index++;
                        }
                        break;
                    case '<':
                        if (Match('='))
                        {
                            AddToken(FormulaTokenKind.LessEqual);
                        }
                        else
                        {
                            AddToken(FormulaTokenKind.Less);
                            _index++;
                        }
                        break;
                    case '&':
                        if (Match('&'))
                        {
                            AddToken(FormulaTokenKind.AndAnd);
                        }
                        else
                        {
                            HasErrors = true;
                            _index++;
                        }
                        break;
                    case '|':
                        if (Match('|'))
                        {
                            AddToken(FormulaTokenKind.OrOr);
                        }
                        else
                        {
                            HasErrors = true;
                            _index++;
                        }
                        break;
                    default:
                        HasErrors = true;
                        _index++;
                        break;
                }
            }

            _tokens.Add(new FormulaToken(FormulaTokenKind.End, "", 0));
            return _tokens;
        }

        private void TokenizeAtIdentifier()
        {
            _index++;
            int start = _index;
            while (!IsAtEnd() && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
            {
                _index++;
            }

            if (start == _index)
            {
                HasErrors = true;
                return;
            }

            string identifier = _text.Substring(start, _index - start);
            _tokens.Add(new FormulaToken(FormulaTokenKind.AtIdentifier, identifier, 0));
        }

        private void TokenizeIdentifier()
        {
            int start = _index;
            while (!IsAtEnd() && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
            {
                _index++;
            }

            string identifier = _text.Substring(start, _index - start);
            if (string.Equals(identifier, "true", StringComparison.OrdinalIgnoreCase))
            {
                _tokens.Add(new FormulaToken(FormulaTokenKind.True, identifier, 0));
                return;
            }

            if (string.Equals(identifier, "false", StringComparison.OrdinalIgnoreCase))
            {
                _tokens.Add(new FormulaToken(FormulaTokenKind.False, identifier, 0));
                return;
            }

            _tokens.Add(new FormulaToken(FormulaTokenKind.Identifier, identifier, 0));
        }

        private void TokenizeNumber()
        {
            int start = _index;
            bool hasDot = false;
            while (!IsAtEnd())
            {
                char currentChar = Peek();
                if (char.IsDigit(currentChar))
                {
                    _index++;
                    continue;
                }

                if (currentChar == '.' && !hasDot)
                {
                    hasDot = true;
                    _index++;
                    continue;
                }

                break;
            }

            string text = _text.Substring(start, _index - start);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double numberValue))
            {
                HasErrors = true;
                numberValue = 0;
            }

            _tokens.Add(new FormulaToken(FormulaTokenKind.Number, text, numberValue));
        }

        private void TokenizeString()
        {
            _index++;
            int start = _index;
            var textBuilder = new System.Text.StringBuilder();

            while (!IsAtEnd())
            {
                char currentChar = Peek();
                if (currentChar == '"')
                {
                    break;
                }

                if (currentChar == '\\')
                {
                    _index++;
                    if (IsAtEnd())
                    {
                        break;
                    }

                    char escapedChar = Peek();
                    textBuilder.Append(escapedChar switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '"' => '"',
                        '\\' => '\\',
                        _ => escapedChar
                    });
                    _index++;
                    continue;
                }

                textBuilder.Append(currentChar);
                _index++;
            }

            if (IsAtEnd() || Peek() != '"')
            {
                HasErrors = true;
                return;
            }

            _index++;
            _tokens.Add(new FormulaToken(FormulaTokenKind.String, textBuilder.ToString(), 0));
        }

        private void AddToken(FormulaTokenKind tokenKind)
        {
            _tokens.Add(new FormulaToken(tokenKind, "", 0));
        }

        private bool IsAtEnd()
        {
            return _index >= _text.Length;
        }

        private char Peek()
        {
            return _text[_index];
        }

        private bool Match(char expected)
        {
            if (_index + 1 >= _text.Length || _text[_index + 1] != expected)
            {
                return false;
            }

            _index += 2;
            return true;
        }
    }

    private readonly struct FormulaToken
    {
        public FormulaTokenKind Kind { get; }
        public string Text { get; }
        public double NumberValue { get; }

        public FormulaToken(FormulaTokenKind kind, string text, double numberValue)
        {
            Kind = kind;
            Text = text;
            NumberValue = numberValue;
        }
    }

    private enum FormulaTokenKind
    {
        End,
        Identifier,
        AtIdentifier,
        Number,
        String,
        True,
        False,
        LeftParen,
        RightParen,
        Dot,
        Comma,
        Question,
        Colon,
        Plus,
        Minus,
        Star,
        Slash,
        Percent,
        Bang,
        BangEqual,
        EqualEqual,
        Greater,
        GreaterEqual,
        Less,
        LessEqual,
        AndAnd,
        OrOr
    }

    private sealed class FormulaParser
    {
        private readonly List<FormulaToken> _tokens;
        private int _index;

        public bool HasErrors { get; private set; }

        public FormulaParser(List<FormulaToken> tokens)
        {
            _tokens = tokens;
        }

        public ExpressionNode ParseExpression()
        {
            var expression = ParseConditional();
            if (expression == null)
            {
                return ExpressionNode.NullLiteral();
            }

            if (!Check(FormulaTokenKind.End))
            {
                HasErrors = true;
            }

            return expression;
        }

        private ExpressionNode? ParseConditional()
        {
            var condition = ParseLogicalOr();
            if (condition == null)
            {
                return null;
            }

            if (!Match(FormulaTokenKind.Question))
            {
                return condition;
            }

            var whenTrue = ParseConditional();
            if (!Match(FormulaTokenKind.Colon))
            {
                HasErrors = true;
                return condition;
            }

            var whenFalse = ParseConditional();
            if (whenTrue == null || whenFalse == null)
            {
                HasErrors = true;
                return condition;
            }

            return ExpressionNode.Conditional(condition, whenTrue, whenFalse);
        }

        private ExpressionNode? ParseLogicalOr()
        {
            var expression = ParseLogicalAnd();
            while (Match(FormulaTokenKind.OrOr))
            {
                var rightSide = ParseLogicalAnd();
                if (expression == null || rightSide == null)
                {
                    HasErrors = true;
                    return null;
                }

                expression = ExpressionNode.Binary("||", expression, rightSide);
            }

            return expression;
        }

        private ExpressionNode? ParseLogicalAnd()
        {
            var expression = ParseEquality();
            while (Match(FormulaTokenKind.AndAnd))
            {
                var rightSide = ParseEquality();
                if (expression == null || rightSide == null)
                {
                    HasErrors = true;
                    return null;
                }

                expression = ExpressionNode.Binary("&&", expression, rightSide);
            }

            return expression;
        }

        private ExpressionNode? ParseEquality()
        {
            var expression = ParseComparison();
            while (true)
            {
                if (Match(FormulaTokenKind.EqualEqual))
                {
                    var rightSide = ParseComparison();
                    if (expression == null || rightSide == null)
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Binary("==", expression, rightSide);
                    continue;
                }

                if (Match(FormulaTokenKind.BangEqual))
                {
                    var rightSide = ParseComparison();
                    if (expression == null || rightSide == null)
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Binary("!=", expression, rightSide);
                    continue;
                }

                break;
            }

            return expression;
        }

        private ExpressionNode? ParseComparison()
        {
            var expression = ParseTerm();
            while (true)
            {
                if (Match(FormulaTokenKind.Greater))
                {
                    var rightSide = ParseTerm();
                    if (expression == null || rightSide == null)
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Binary(">", expression, rightSide);
                    continue;
                }

                if (Match(FormulaTokenKind.GreaterEqual))
                {
                    var rightSide = ParseTerm();
                    if (expression == null || rightSide == null)
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Binary(">=", expression, rightSide);
                    continue;
                }

                if (Match(FormulaTokenKind.Less))
                {
                    var rightSide = ParseTerm();
                    if (expression == null || rightSide == null)
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Binary("<", expression, rightSide);
                    continue;
                }

                if (Match(FormulaTokenKind.LessEqual))
                {
                    var rightSide = ParseTerm();
                    if (expression == null || rightSide == null)
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Binary("<=", expression, rightSide);
                    continue;
                }

                break;
            }

            return expression;
        }

        private ExpressionNode? ParseTerm()
        {
            var expression = ParseFactor();
            while (true)
            {
                if (Match(FormulaTokenKind.Plus))
                {
                    var rightSide = ParseFactor();
                    if (expression == null || rightSide == null)
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Binary("+", expression, rightSide);
                    continue;
                }

                if (Match(FormulaTokenKind.Minus))
                {
                    var rightSide = ParseFactor();
                    if (expression == null || rightSide == null)
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Binary("-", expression, rightSide);
                    continue;
                }

                break;
            }

            return expression;
        }

        private ExpressionNode? ParseFactor()
        {
            var expression = ParseUnary();
            while (true)
            {
                if (Match(FormulaTokenKind.Star))
                {
                    var rightSide = ParseUnary();
                    if (expression == null || rightSide == null)
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Binary("*", expression, rightSide);
                    continue;
                }

                if (Match(FormulaTokenKind.Slash))
                {
                    var rightSide = ParseUnary();
                    if (expression == null || rightSide == null)
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Binary("/", expression, rightSide);
                    continue;
                }

                if (Match(FormulaTokenKind.Percent))
                {
                    var rightSide = ParseUnary();
                    if (expression == null || rightSide == null)
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Binary("%", expression, rightSide);
                    continue;
                }

                break;
            }

            return expression;
        }

        private ExpressionNode? ParseUnary()
        {
            if (Match(FormulaTokenKind.Bang))
            {
                var rightSide = ParseUnary();
                if (rightSide == null)
                {
                    HasErrors = true;
                    return null;
                }

                return ExpressionNode.Unary("!", rightSide);
            }

            if (Match(FormulaTokenKind.Minus))
            {
                var rightSide = ParseUnary();
                if (rightSide == null)
                {
                    HasErrors = true;
                    return null;
                }

                return ExpressionNode.Unary("-", rightSide);
            }

            return ParsePostfix();
        }

        private ExpressionNode? ParsePostfix()
        {
            var expression = ParsePrimary();
            if (expression == null)
            {
                return null;
            }

            while (true)
            {
                if (Match(FormulaTokenKind.Dot))
                {
                    if (!Match(FormulaTokenKind.Identifier))
                    {
                        HasErrors = true;
                        return null;
                    }

                    var memberName = Previous().Text;
                    var memberAccess = ExpressionNode.MemberAccess(expression, memberName);

                    if (Match(FormulaTokenKind.LeftParen))
                    {
                        var arguments = ParseArguments();
                        if (!Match(FormulaTokenKind.RightParen))
                        {
                            HasErrors = true;
                            return null;
                        }

                        expression = ExpressionNode.Call(memberAccess, arguments);
                    }
                    else
                    {
                        expression = memberAccess;
                    }

                    continue;
                }

                if (Match(FormulaTokenKind.LeftParen))
                {
                    var arguments = ParseArguments();
                    if (!Match(FormulaTokenKind.RightParen))
                    {
                        HasErrors = true;
                        return null;
                    }

                    expression = ExpressionNode.Call(expression, arguments);
                    continue;
                }

                break;
            }

            return expression;
        }

        private List<ExpressionNode> ParseArguments()
        {
            var arguments = new List<ExpressionNode>();
            if (Check(FormulaTokenKind.RightParen))
            {
                return arguments;
            }

            while (true)
            {
                var argument = ParseConditional();
                if (argument == null)
                {
                    HasErrors = true;
                    return arguments;
                }

                arguments.Add(argument);
                if (!Match(FormulaTokenKind.Comma))
                {
                    break;
                }
            }

            return arguments;
        }

        private ExpressionNode? ParsePrimary()
        {
            if (Match(FormulaTokenKind.Number))
            {
                return ExpressionNode.NumberLiteral(Previous().NumberValue);
            }

            if (Match(FormulaTokenKind.String))
            {
                return ExpressionNode.StringLiteral(Previous().Text);
            }

            if (Match(FormulaTokenKind.True))
            {
                return ExpressionNode.BoolLiteral(true);
            }

            if (Match(FormulaTokenKind.False))
            {
                return ExpressionNode.BoolLiteral(false);
            }

            if (Match(FormulaTokenKind.Identifier))
            {
                return ExpressionNode.Identifier(Previous().Text);
            }

            if (Match(FormulaTokenKind.AtIdentifier))
            {
                return ExpressionNode.AtIdentifier(Previous().Text);
            }

            if (Match(FormulaTokenKind.LeftParen))
            {
                var expression = ParseConditional();
                if (!Match(FormulaTokenKind.RightParen))
                {
                    HasErrors = true;
                    return null;
                }

                return expression;
            }

            HasErrors = true;
            return null;
        }

        private bool Match(FormulaTokenKind tokenKind)
        {
            if (!Check(tokenKind))
            {
                return false;
            }

            _index++;
            return true;
        }

        private bool Check(FormulaTokenKind tokenKind)
        {
            return Peek().Kind == tokenKind;
        }

        private FormulaToken Peek()
        {
            return _tokens[_index];
        }

        private FormulaToken Previous()
        {
            return _tokens[_index - 1];
        }
    }

    private sealed class FormulaTypeChecker
    {
        public bool Validate(ExpressionNode root)
        {
            return ValidateNode(root);
        }

        private bool ValidateNode(ExpressionNode node)
        {
            if (node.Kind == ExpressionNodeKind.Unary)
            {
                if (node.Text != "!" && node.Text != "-")
                {
                    return false;
                }
            }

            if (node.Kind == ExpressionNodeKind.Binary)
            {
                if (node.Text != "&&" && node.Text != "||" &&
                    node.Text != "==" && node.Text != "!=" &&
                    node.Text != ">" && node.Text != ">=" &&
                    node.Text != "<" && node.Text != "<=" &&
                    node.Text != "+" && node.Text != "-" &&
                    node.Text != "*" && node.Text != "/" &&
                    node.Text != "%")
                {
                    return false;
                }
            }

            if (node.Left != null && !ValidateNode(node.Left))
            {
                return false;
            }

            if (node.Right != null && !ValidateNode(node.Right))
            {
                return false;
            }

            if (node.Third != null && !ValidateNode(node.Third))
            {
                return false;
            }

            if (node.Callee != null && !ValidateNode(node.Callee))
            {
                return false;
            }

            if (node.Arguments != null)
            {
                for (int argumentIndex = 0; argumentIndex < node.Arguments.Count; argumentIndex++)
                {
                    if (!ValidateNode(node.Arguments[argumentIndex]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    private sealed class FormulaFlattener
    {
        private readonly List<FlattenedInstruction> _instructions = new();

        public List<FlattenedInstruction> Flatten(ExpressionNode root)
        {
            _instructions.Clear();
            Emit(root);
            return _instructions;
        }

        private void Emit(ExpressionNode node)
        {
            if (node.Left != null)
            {
                Emit(node.Left);
            }

            if (node.Right != null)
            {
                Emit(node.Right);
            }

            if (node.Third != null)
            {
                Emit(node.Third);
            }

            if (node.Arguments != null)
            {
                for (int argumentIndex = 0; argumentIndex < node.Arguments.Count; argumentIndex++)
                {
                    Emit(node.Arguments[argumentIndex]);
                }
            }

            _instructions.Add(new FlattenedInstruction(node.Kind.ToString(), node.Text));
        }
    }

    private readonly struct FlattenedInstruction
    {
        public string OpCode { get; }
        public string Operand { get; }

        public FlattenedInstruction(string opCode, string operand)
        {
            OpCode = opCode;
            Operand = operand;
        }
    }

    private sealed class FormulaEvaluator
    {
        private sealed class DocumentVariableState
        {
            public bool IsEvaluating;
            public bool HasValue;
            public FormulaValue Value;
        }

        private sealed class TableVariableState
        {
            public bool IsEvaluating;
            public bool HasValue;
            public FormulaValue Value;
        }

        private readonly IFormulaContext _formulaContext;
        private readonly IReadOnlyDictionary<string, Dictionary<string, DocumentVariableEvaluationState>>? _precomputedDocumentVariableValuesByDocumentId;
        private readonly string _tableVariableOverrideTableId;
        private readonly IReadOnlyDictionary<string, string>? _tableVariableOverrideExpressionsByName;
        private readonly Dictionary<TableVariantCacheKey, DocTable> _tableVariantCacheByKey = new();
        private readonly Dictionary<string, SplineUtils.SplinePoint[]> _splinePointsCacheByJson = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, DocumentVariableState>> _documentVariableStateByDocumentId =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, TableVariableState>> _tableVariableStateByTableId =
            new(StringComparer.Ordinal);

        public FormulaEvaluator(
            IFormulaContext formulaContext,
            IReadOnlyDictionary<string, Dictionary<string, DocumentVariableEvaluationState>>? precomputedDocumentVariableValuesByDocumentId = null,
            string? tableVariableOverrideTableId = null,
            IReadOnlyDictionary<string, string>? tableVariableOverrideExpressionsByName = null)
        {
            _formulaContext = formulaContext;
            _precomputedDocumentVariableValuesByDocumentId = precomputedDocumentVariableValuesByDocumentId;
            _tableVariableOverrideTableId = tableVariableOverrideTableId ?? "";
            _tableVariableOverrideExpressionsByName = tableVariableOverrideExpressionsByName;
        }

        private readonly struct TableVariantCacheKey : IEquatable<TableVariantCacheKey>
        {
            public TableVariantCacheKey(string tableId, int variantId)
            {
                TableId = tableId;
                VariantId = variantId;
            }

            public string TableId { get; }
            public int VariantId { get; }

            public bool Equals(TableVariantCacheKey other)
            {
                return VariantId == other.VariantId &&
                       string.Equals(TableId, other.TableId, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj)
            {
                return obj is TableVariantCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(StringComparer.Ordinal.GetHashCode(TableId), VariantId);
            }
        }

        public FormulaValue Evaluate(ExpressionNode node, EvaluationFrame frame)
        {
            return node.Kind switch
            {
                ExpressionNodeKind.NullLiteral => FormulaValue.Null(),
                ExpressionNodeKind.NumberLiteral => FormulaValue.Number(node.NumberValue),
                ExpressionNodeKind.StringLiteral => FormulaValue.String(node.Text),
                ExpressionNodeKind.BoolLiteral => FormulaValue.Bool(node.BoolValue),
                ExpressionNodeKind.Identifier => EvaluateIdentifier(node.Text, frame),
                ExpressionNodeKind.AtIdentifier => EvaluateAtIdentifier(node.Text, frame),
                ExpressionNodeKind.MemberAccess => EvaluateMemberAccess(node, frame),
                ExpressionNodeKind.Unary => EvaluateUnary(node, frame),
                ExpressionNodeKind.Binary => EvaluateBinary(node, frame),
                ExpressionNodeKind.Conditional => EvaluateConditional(node, frame),
                ExpressionNodeKind.Call => EvaluateCall(node, frame),
                _ => FormulaValue.Null()
            };
        }

        private FormulaValue EvaluateIdentifier(string identifier, EvaluationFrame frame)
        {
            if (string.Equals(identifier, "thisRow", StringComparison.OrdinalIgnoreCase))
            {
                if (frame.CurrentTable != null && frame.CurrentRow != null)
                {
                    return FormulaValue.Row(frame.CurrentTable, frame.CurrentRow);
                }

                return FormulaValue.Null();
            }

            if (string.Equals(identifier, "parentRow", StringComparison.OrdinalIgnoreCase))
            {
                if (frame.ParentTable != null && frame.ParentRow != null)
                {
                    return FormulaValue.Row(frame.ParentTable, frame.ParentRow);
                }

                return FormulaValue.Null();
            }

            if (string.Equals(identifier, "parentTable", StringComparison.OrdinalIgnoreCase))
            {
                if (frame.ParentTable != null)
                {
                    return FormulaValue.Table(frame.ParentTable);
                }

                return FormulaValue.Null();
            }

            if (string.Equals(identifier, "thisRowIndex", StringComparison.OrdinalIgnoreCase))
            {
                if (frame.CurrentTable == null || frame.CurrentRow == null)
                {
                    return FormulaValue.Number(0);
                }

                int oneBasedIndex = frame.CurrentRowIndexOneBased > 0
                    ? frame.CurrentRowIndexOneBased
                    : ResolveRowIndexOneBased(frame.CurrentTable, frame.CurrentRow, frame);
                return FormulaValue.Number(oneBasedIndex);
            }

            if (string.Equals(identifier, "thisDoc", StringComparison.OrdinalIgnoreCase))
            {
                if (frame.CurrentDocument != null)
                {
                    return FormulaValue.Document(frame.CurrentDocument);
                }

                return FormulaValue.Null();
            }

            if (string.Equals(identifier, "thisTable", StringComparison.OrdinalIgnoreCase))
            {
                if (frame.CurrentTable != null)
                {
                    return FormulaValue.Table(frame.CurrentTable);
                }

                return FormulaValue.Null();
            }

            if (_formulaContext.TryGetTableByName(identifier, out var table))
            {
                return FormulaValue.Table(table);
            }

            return FormulaValue.Null();
        }

        private FormulaValue EvaluateAtIdentifier(string columnName, EvaluationFrame frame)
        {
            if (frame.CurrentDocument != null &&
                DocumentFormulaSyntax.IsValidIdentifier(columnName.AsSpan()))
            {
                return EvaluateDocumentVariable(frame.CurrentDocument, columnName);
            }

            if (frame.CandidateRow == null || frame.CandidateTable == null)
            {
                return FormulaValue.Null();
            }

            if (string.Equals(columnName, "rowIndex", StringComparison.OrdinalIgnoreCase))
            {
                int oneBasedIndex = frame.CandidateRowIndexOneBased > 0
                    ? frame.CandidateRowIndexOneBased
                    : ResolveRowIndexOneBased(frame.CandidateTable, frame.CandidateRow, frame);
                return FormulaValue.Number(oneBasedIndex);
            }

            if (!_formulaContext.TryGetColumnByName(frame.CandidateTable, columnName, out var column))
            {
                return FormulaValue.Null();
            }

            return GetCellAsFormulaValue(frame.CandidateTable, frame.CandidateRow, column);
        }

        private FormulaValue EvaluateMemberAccess(ExpressionNode node, EvaluationFrame frame)
        {
            if (node.Left == null)
            {
                return FormulaValue.Null();
            }

            if (node.Left.Kind == ExpressionNodeKind.Identifier &&
                string.Equals(node.Left.Text, "docs", StringComparison.OrdinalIgnoreCase))
            {
                if (_formulaContext.TryGetDocumentByAlias(node.Text, out var document))
                {
                    return FormulaValue.Document(document);
                }

                return FormulaValue.Null();
            }

            if (node.Left.Kind == ExpressionNodeKind.Identifier &&
                string.Equals(node.Left.Text, "tables", StringComparison.OrdinalIgnoreCase))
            {
                if (_formulaContext.TryGetTableByName(node.Text, out var table))
                {
                    return FormulaValue.Table(table);
                }

                return FormulaValue.Null();
            }

            var targetValue = Evaluate(node.Left, frame);
            if (targetValue.Kind == FormulaValueKind.TableReference &&
                targetValue.TableValue != null)
            {
                return EvaluateTableVariable(targetValue.TableValue, node.Text);
            }

            if (targetValue.Kind == FormulaValueKind.RowReference && targetValue.TableValue != null && targetValue.RowValue != null)
            {
                if (string.Equals(node.Text, "rowIndex", StringComparison.OrdinalIgnoreCase))
                {
                    int oneBasedIndex = ResolveRowIndexOneBased(targetValue.TableValue, targetValue.RowValue, frame);
                    return FormulaValue.Number(oneBasedIndex);
                }

                if (_formulaContext.TryGetColumnByName(targetValue.TableValue, node.Text, out var column))
                {
                    return GetCellAsFormulaValue(targetValue.TableValue, targetValue.RowValue, column);
                }

                return FormulaValue.Null();
            }

            if (targetValue.Kind == FormulaValueKind.DocumentReference &&
                targetValue.DocumentValue != null)
            {
                return EvaluateDocumentVariable(targetValue.DocumentValue, node.Text);
            }

            if (targetValue.Kind == FormulaValueKind.DateTime)
            {
                if (string.Equals(node.Text, "Year", StringComparison.OrdinalIgnoreCase))
                {
                    return FormulaValue.Number(targetValue.DateTimeValue.Year);
                }

                if (string.Equals(node.Text, "Month", StringComparison.OrdinalIgnoreCase))
                {
                    return FormulaValue.Number(targetValue.DateTimeValue.Month);
                }

                if (string.Equals(node.Text, "Day", StringComparison.OrdinalIgnoreCase))
                {
                    return FormulaValue.Number(targetValue.DateTimeValue.Day);
                }
            }

            if (targetValue.Kind == FormulaValueKind.Vec2 ||
                targetValue.Kind == FormulaValueKind.Vec3 ||
                targetValue.Kind == FormulaValueKind.Vec4 ||
                targetValue.Kind == FormulaValueKind.Color)
            {
                if (string.Equals(node.Text, "x", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.Text, "r", StringComparison.OrdinalIgnoreCase))
                {
                    return FormulaValue.Number(targetValue.XValue);
                }

                if (string.Equals(node.Text, "y", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.Text, "g", StringComparison.OrdinalIgnoreCase))
                {
                    return FormulaValue.Number(targetValue.YValue);
                }

                if ((targetValue.Kind == FormulaValueKind.Vec3 ||
                     targetValue.Kind == FormulaValueKind.Vec4 ||
                     targetValue.Kind == FormulaValueKind.Color) &&
                    (string.Equals(node.Text, "z", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(node.Text, "b", StringComparison.OrdinalIgnoreCase)))
                {
                    return FormulaValue.Number(targetValue.ZValue);
                }

                if ((targetValue.Kind == FormulaValueKind.Vec4 ||
                     targetValue.Kind == FormulaValueKind.Color) &&
                    (string.Equals(node.Text, "w", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(node.Text, "a", StringComparison.OrdinalIgnoreCase)))
                {
                    return FormulaValue.Number(targetValue.WValue);
                }
            }

            if (targetValue.Kind == FormulaValueKind.String &&
                string.Equals(node.Text, "Length", StringComparison.OrdinalIgnoreCase))
            {
                return FormulaValue.Number((targetValue.StringValue ?? "").Length);
            }

            return FormulaValue.Null();
        }

        private FormulaValue EvaluateTableVariable(DocTable table, string variableName)
        {
            string expression = "";
            bool hasExpression = false;
            bool hasOverrideExpression = false;
            if (!string.IsNullOrWhiteSpace(_tableVariableOverrideTableId) &&
                string.Equals(_tableVariableOverrideTableId, table.Id, StringComparison.Ordinal) &&
                _tableVariableOverrideExpressionsByName != null &&
                _tableVariableOverrideExpressionsByName.TryGetValue(variableName, out string? overrideExpression))
            {
                expression = overrideExpression ?? "";
                hasOverrideExpression = true;
            }

            if (hasOverrideExpression)
            {
                hasExpression = !string.IsNullOrWhiteSpace(expression);
            }
            else if (!_formulaContext.TryGetTableVariableExpression(
                         table.Id,
                         variableName,
                         out expression,
                         out hasExpression))
            {
                return FormulaValue.Null();
            }

            if (!hasExpression || string.IsNullOrWhiteSpace(expression))
            {
                return FormulaValue.Null();
            }

            string normalizedExpression = NormalizeTableFormulaExpression(expression);
            if (string.IsNullOrWhiteSpace(normalizedExpression))
            {
                return FormulaValue.Null();
            }

            if (!_tableVariableStateByTableId.TryGetValue(table.Id, out var tableVariableStateByName))
            {
                tableVariableStateByName = new Dictionary<string, TableVariableState>(StringComparer.OrdinalIgnoreCase);
                _tableVariableStateByTableId[table.Id] = tableVariableStateByName;
            }

            if (!tableVariableStateByName.TryGetValue(variableName, out var tableVariableState))
            {
                tableVariableState = new TableVariableState();
                tableVariableStateByName[variableName] = tableVariableState;
            }

            if (tableVariableState.HasValue)
            {
                return tableVariableState.Value;
            }

            if (tableVariableState.IsEvaluating)
            {
                throw new InvalidOperationException(
                    $"Cycle detected while evaluating table variable '{variableName}' in '{table.Name}'.");
            }

            var compiledFormula = CompileFormula(normalizedExpression);
            if (!compiledFormula.IsValid)
            {
                throw new InvalidOperationException(
                    $"Table variable '{variableName}' in '{table.Name}' has an invalid formula expression.");
            }

            tableVariableState.IsEvaluating = true;
            try
            {
                var frame = CreateTableEvaluationFrame(table);
                FormulaValue value = Evaluate(compiledFormula.Root, frame);
                tableVariableState.Value = value;
                tableVariableState.HasValue = true;
                return value;
            }
            finally
            {
                tableVariableState.IsEvaluating = false;
            }
        }

        private FormulaValue EvaluateDocumentVariable(DocDocument document, string variableName)
        {
            if (_precomputedDocumentVariableValuesByDocumentId != null &&
                _precomputedDocumentVariableValuesByDocumentId.TryGetValue(document.Id, out var precomputedValueByVariableName) &&
                precomputedValueByVariableName.TryGetValue(variableName, out var precomputedState))
            {
                if (precomputedState.HasError)
                {
                    throw new InvalidOperationException(
                        $"Document variable '{variableName}' in '{document.Title}' could not be evaluated.");
                }

                return precomputedState.Value;
            }

            if (!_formulaContext.TryGetDocumentVariableExpression(
                    document.Id,
                    variableName,
                    out string expression,
                    out bool hasExpression))
            {
                return FormulaValue.Null();
            }

            if (!hasExpression || string.IsNullOrWhiteSpace(expression))
            {
                return FormulaValue.Null();
            }

            string normalizedExpression = NormalizeDocumentFormulaExpression(expression);
            if (string.IsNullOrWhiteSpace(normalizedExpression))
            {
                return FormulaValue.Null();
            }

            if (!_documentVariableStateByDocumentId.TryGetValue(document.Id, out var variableStateByName))
            {
                variableStateByName = new Dictionary<string, DocumentVariableState>(StringComparer.OrdinalIgnoreCase);
                _documentVariableStateByDocumentId[document.Id] = variableStateByName;
            }

            if (!variableStateByName.TryGetValue(variableName, out var variableState))
            {
                variableState = new DocumentVariableState();
                variableStateByName[variableName] = variableState;
            }

            if (variableState.HasValue)
            {
                return variableState.Value;
            }

            if (variableState.IsEvaluating)
            {
                throw new InvalidOperationException(
                    $"Cycle detected while evaluating document variable '{variableName}' in '{document.Title}'.");
            }

            var compiledFormula = CompileFormula(normalizedExpression);
            if (!compiledFormula.IsValid)
            {
                throw new InvalidOperationException(
                    $"Document variable '{variableName}' in '{document.Title}' has an invalid formula expression.");
            }

            variableState.IsEvaluating = true;
            try
            {
                var frame = CreateDocumentEvaluationFrame(document);
                FormulaValue value = Evaluate(compiledFormula.Root, frame);
                variableState.Value = value;
                variableState.HasValue = true;
                return value;
            }
            finally
            {
                variableState.IsEvaluating = false;
            }
        }

        private FormulaValue EvaluateUnary(ExpressionNode node, EvaluationFrame frame)
        {
            if (node.Left == null)
            {
                return FormulaValue.Null();
            }

            var operandValue = Evaluate(node.Left, frame);
            if (node.Text == "!")
            {
                return FormulaValue.Bool(!ToBoolean(operandValue));
            }

            if (node.Text == "-" && TryToNumber(operandValue, out double numericValue))
            {
                return FormulaValue.Number(-numericValue);
            }

            if (node.Text == "-" &&
                TryGetVectorDimension(operandValue, out int vectorDimension))
            {
                return vectorDimension switch
                {
                    2 => FormulaValue.Vec2(-operandValue.XValue, -operandValue.YValue),
                    3 => FormulaValue.Vec3(-operandValue.XValue, -operandValue.YValue, -operandValue.ZValue),
                    _ => operandValue.Kind == FormulaValueKind.Color
                        ? FormulaValue.Color(-operandValue.XValue, -operandValue.YValue, -operandValue.ZValue, -operandValue.WValue)
                        : FormulaValue.Vec4(-operandValue.XValue, -operandValue.YValue, -operandValue.ZValue, -operandValue.WValue),
                };
            }

            return FormulaValue.Null();
        }

        private FormulaValue EvaluateBinary(ExpressionNode node, EvaluationFrame frame)
        {
            if (node.Left == null || node.Right == null)
            {
                return FormulaValue.Null();
            }

            var leftValue = Evaluate(node.Left, frame);
            var rightValue = Evaluate(node.Right, frame);

            switch (node.Text)
            {
                case "+":
                    if (TryAddVectors(leftValue, rightValue, out FormulaValue vectorAddResult))
                    {
                        return vectorAddResult;
                    }

                    if (TryToNumber(leftValue, out double leftNumber) && TryToNumber(rightValue, out double rightNumber))
                    {
                        return FormulaValue.Number(leftNumber + rightNumber);
                    }

                    return FormulaValue.String(ToStringValue(leftValue) + ToStringValue(rightValue));
                case "-":
                    if (TrySubtractVectors(leftValue, rightValue, out FormulaValue vectorSubtractResult))
                    {
                        return vectorSubtractResult;
                    }

                    if (leftValue.Kind == FormulaValueKind.DateTime &&
                        rightValue.Kind == FormulaValueKind.DateTime)
                    {
                        return FormulaValue.Number((leftValue.DateTimeValue - rightValue.DateTimeValue).TotalDays);
                    }

                    if (TryToNumber(leftValue, out leftNumber) && TryToNumber(rightValue, out rightNumber))
                    {
                        return FormulaValue.Number(leftNumber - rightNumber);
                    }

                    return FormulaValue.Null();
                case "*":
                    if (TryScaleVector(leftValue, rightValue, out FormulaValue vectorScaleResult))
                    {
                        return vectorScaleResult;
                    }

                    if (TryToNumber(leftValue, out leftNumber) && TryToNumber(rightValue, out rightNumber))
                    {
                        return FormulaValue.Number(leftNumber * rightNumber);
                    }

                    return FormulaValue.Null();
                case "/":
                    if (TryDivideVector(leftValue, rightValue, out FormulaValue vectorDivideResult))
                    {
                        return vectorDivideResult;
                    }

                    if (TryToNumber(leftValue, out leftNumber) && TryToNumber(rightValue, out rightNumber))
                    {
                        if (Math.Abs(rightNumber) < double.Epsilon)
                        {
                            return FormulaValue.Null();
                        }

                        return FormulaValue.Number(leftNumber / rightNumber);
                    }

                    return FormulaValue.Null();
                case "%":
                    if (TryToNumber(leftValue, out leftNumber) && TryToNumber(rightValue, out rightNumber))
                    {
                        if (Math.Abs(rightNumber) < double.Epsilon)
                        {
                            return FormulaValue.Null();
                        }

                        return FormulaValue.Number(leftNumber % rightNumber);
                    }

                    return FormulaValue.Null();
                case "==":
                    return FormulaValue.Bool(AreEqual(leftValue, rightValue));
                case "!=":
                    return FormulaValue.Bool(!AreEqual(leftValue, rightValue));
                case ">":
                    return FormulaValue.Bool(Compare(leftValue, rightValue) > 0);
                case ">=":
                    return FormulaValue.Bool(Compare(leftValue, rightValue) >= 0);
                case "<":
                    return FormulaValue.Bool(Compare(leftValue, rightValue) < 0);
                case "<=":
                    return FormulaValue.Bool(Compare(leftValue, rightValue) <= 0);
                case "&&":
                    return FormulaValue.Bool(ToBoolean(leftValue) && ToBoolean(rightValue));
                case "||":
                    return FormulaValue.Bool(ToBoolean(leftValue) || ToBoolean(rightValue));
                default:
                    return FormulaValue.Null();
            }
        }

        private FormulaValue EvaluateConditional(ExpressionNode node, EvaluationFrame frame)
        {
            if (node.Left == null || node.Right == null || node.Third == null)
            {
                return FormulaValue.Null();
            }

            var conditionValue = Evaluate(node.Left, frame);
            return ToBoolean(conditionValue)
                ? Evaluate(node.Right, frame)
                : Evaluate(node.Third, frame);
        }

        private FormulaValue EvaluateCall(ExpressionNode node, EvaluationFrame frame)
        {
            if (node.Callee == null)
            {
                return FormulaValue.Null();
            }

            if (node.Callee.Kind == ExpressionNodeKind.Identifier)
            {
                return EvaluateFunctionCall(node.Callee.Text, node.Arguments, frame);
            }

            if (node.Callee.Kind == ExpressionNodeKind.MemberAccess &&
                node.Callee.Left != null)
            {
                if (node.Callee.Left.Kind == ExpressionNodeKind.Identifier &&
                    string.Equals(node.Callee.Left.Text, "graph", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(node.Callee.Text, "in", StringComparison.OrdinalIgnoreCase))
                {
                    return EvaluateGraphInput(node.Arguments, frame);
                }

                var targetValue = Evaluate(node.Callee.Left, frame);
                return EvaluateMethodCall(targetValue, node.Callee.Text, node.Arguments, frame);
            }

            return FormulaValue.Null();
        }

        private FormulaValue EvaluateFunctionCall(string functionName, List<ExpressionNode>? arguments, EvaluationFrame frame)
        {
            var argumentList = arguments ?? new List<ExpressionNode>();

            if (string.Equals(functionName, "Lookup", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateLookup(argumentList, frame);
            }

            if (string.Equals(functionName, "CountIf", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateCountIf(argumentList, frame);
            }

            if (string.Equals(functionName, "SumIf", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateSumIf(argumentList, frame);
            }

            if (string.Equals(functionName, "Vec2", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 2)
                {
                    return FormulaValue.Null();
                }

                if (!TryToNumber(Evaluate(argumentList[0], frame), out double xValue) ||
                    !TryToNumber(Evaluate(argumentList[1], frame), out double yValue))
                {
                    return FormulaValue.Null();
                }

                return FormulaValue.Vec2(xValue, yValue);
            }

            if (string.Equals(functionName, "Vec3", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 3)
                {
                    return FormulaValue.Null();
                }

                if (!TryToNumber(Evaluate(argumentList[0], frame), out double xValue) ||
                    !TryToNumber(Evaluate(argumentList[1], frame), out double yValue) ||
                    !TryToNumber(Evaluate(argumentList[2], frame), out double zValue))
                {
                    return FormulaValue.Null();
                }

                return FormulaValue.Vec3(xValue, yValue, zValue);
            }

            if (string.Equals(functionName, "Vec4", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 4)
                {
                    return FormulaValue.Null();
                }

                if (!TryToNumber(Evaluate(argumentList[0], frame), out double xValue) ||
                    !TryToNumber(Evaluate(argumentList[1], frame), out double yValue) ||
                    !TryToNumber(Evaluate(argumentList[2], frame), out double zValue) ||
                    !TryToNumber(Evaluate(argumentList[3], frame), out double wValue))
                {
                    return FormulaValue.Null();
                }

                return FormulaValue.Vec4(xValue, yValue, zValue, wValue);
            }

            if (string.Equals(functionName, "Color", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 3)
                {
                    return FormulaValue.Null();
                }

                if (!TryToNumber(Evaluate(argumentList[0], frame), out double rValue) ||
                    !TryToNumber(Evaluate(argumentList[1], frame), out double gValue) ||
                    !TryToNumber(Evaluate(argumentList[2], frame), out double bValue))
                {
                    return FormulaValue.Null();
                }

                double aValue = 1;
                if (argumentList.Count >= 4 &&
                    !TryToNumber(Evaluate(argumentList[3], frame), out aValue))
                {
                    return FormulaValue.Null();
                }

                return FormulaValue.Color(rValue, gValue, bValue, aValue);
            }

            if (string.Equals(functionName, "If", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 3)
                {
                    return FormulaValue.Null();
                }

                var conditionValue = Evaluate(argumentList[0], frame);
                return ToBoolean(conditionValue)
                    ? Evaluate(argumentList[1], frame)
                    : Evaluate(argumentList[2], frame);
            }

            if (string.Equals(functionName, "Abs", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 1)
                {
                    return FormulaValue.Null();
                }

                var inputValue = Evaluate(argumentList[0], frame);
                if (!TryToNumber(inputValue, out double inputNumber))
                {
                    return FormulaValue.Null();
                }

                return FormulaValue.Number(Math.Abs(inputNumber));
            }

            if (string.Equals(functionName, "Pow", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 2)
                {
                    return FormulaValue.Null();
                }

                var baseValue = Evaluate(argumentList[0], frame);
                var exponentValue = Evaluate(argumentList[1], frame);
                if (!TryToNumber(baseValue, out double baseNumber) ||
                    !TryToNumber(exponentValue, out double exponentNumber))
                {
                    return FormulaValue.Null();
                }

                return FormulaValue.Number(Math.Pow(baseNumber, exponentNumber));
            }

            if (string.Equals(functionName, "Exp", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 1)
                {
                    return FormulaValue.Null();
                }

                var exponentValue = Evaluate(argumentList[0], frame);
                if (!TryToNumber(exponentValue, out double exponent))
                {
                    return FormulaValue.Null();
                }

                return FormulaValue.Number(Math.Exp(exponent));
            }

            if (string.Equals(functionName, "EvalSpline", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 2)
                {
                    return FormulaValue.Null();
                }

                var splineValue = Evaluate(argumentList[0], frame);
                string splineJson = ToStringValue(splineValue);
                if (string.IsNullOrWhiteSpace(splineJson))
                {
                    return FormulaValue.Null();
                }

                var tValue = Evaluate(argumentList[1], frame);
                if (!TryToNumber(tValue, out double tNumber))
                {
                    return FormulaValue.Null();
                }

                if (!TryResolveSplinePoints(splineJson, out var splinePoints))
                {
                    return FormulaValue.Null();
                }

                float t = Math.Clamp((float)tNumber, 0f, 1f);
                float result = SplineUtils.Evaluate(splinePoints, t);
                if (!float.IsFinite(result))
                {
                    return FormulaValue.Null();
                }

                return FormulaValue.Number(result);
            }

            if (string.Equals(functionName, "Upper", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 1)
                {
                    return FormulaValue.String("");
                }

                return FormulaValue.String(ToStringValue(Evaluate(argumentList[0], frame)).ToUpperInvariant());
            }

            if (string.Equals(functionName, "Lower", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 1)
                {
                    return FormulaValue.String("");
                }

                return FormulaValue.String(ToStringValue(Evaluate(argumentList[0], frame)).ToLowerInvariant());
            }

            if (string.Equals(functionName, "Contains", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 2)
                {
                    return FormulaValue.Bool(false);
                }

                string haystack = ToStringValue(Evaluate(argumentList[0], frame));
                string needle = ToStringValue(Evaluate(argumentList[1], frame));
                return FormulaValue.Bool(haystack.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(functionName, "Concat", StringComparison.OrdinalIgnoreCase))
            {
                var textBuilder = new System.Text.StringBuilder();
                for (int argumentIndex = 0; argumentIndex < argumentList.Count; argumentIndex++)
                {
                    textBuilder.Append(ToStringValue(Evaluate(argumentList[argumentIndex], frame)));
                }

                return FormulaValue.String(textBuilder.ToString());
            }

            if (string.Equals(functionName, "Date", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 1)
                {
                    return FormulaValue.Null();
                }

                string text = ToStringValue(Evaluate(argumentList[0], frame));
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime dateValue))
                {
                    return FormulaValue.DateTime(dateValue.Date);
                }

                return FormulaValue.Null();
            }

            if (string.Equals(functionName, "Today", StringComparison.OrdinalIgnoreCase))
            {
                return FormulaValue.DateTime(DateTime.Today);
            }

            if (string.Equals(functionName, "AddDays", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 2)
                {
                    return FormulaValue.Null();
                }

                var dateValue = Evaluate(argumentList[0], frame);
                var daysValue = Evaluate(argumentList[1], frame);
                if (dateValue.Kind != FormulaValueKind.DateTime || !TryToNumber(daysValue, out double days))
                {
                    return FormulaValue.Null();
                }

                return FormulaValue.DateTime(dateValue.DateTimeValue.AddDays(days));
            }

            if (string.Equals(functionName, "DaysBetween", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count < 2)
                {
                    return FormulaValue.Null();
                }

                var startDateValue = Evaluate(argumentList[0], frame);
                var endDateValue = Evaluate(argumentList[1], frame);
                if (startDateValue.Kind != FormulaValueKind.DateTime || endDateValue.Kind != FormulaValueKind.DateTime)
                {
                    return FormulaValue.Null();
                }

                return FormulaValue.Number((endDateValue.DateTimeValue - startDateValue.DateTimeValue).TotalDays);
            }

            if (argumentList.Count == 0)
            {
                if (FormulaFunctionRegistry.TryEvaluate(
                        functionName,
                        ReadOnlySpan<FormulaValue>.Empty,
                        CreateFormulaFunctionContext(frame),
                        out var noArgumentResult))
                {
                    return noArgumentResult;
                }

                return FormulaValue.Null();
            }

            int argumentCount = argumentList.Count;
            FormulaValue[] rentedArgumentValues = ArrayPool<FormulaValue>.Shared.Rent(argumentCount);
            try
            {
                for (int argumentIndex = 0; argumentIndex < argumentCount; argumentIndex++)
                {
                    rentedArgumentValues[argumentIndex] = Evaluate(argumentList[argumentIndex], frame);
                }

                if (FormulaFunctionRegistry.TryEvaluate(
                        functionName,
                        rentedArgumentValues.AsSpan(0, argumentCount),
                        CreateFormulaFunctionContext(frame),
                        out var customResult))
                {
                    return customResult;
                }
            }
            finally
            {
                Array.Clear(rentedArgumentValues, 0, argumentCount);
                ArrayPool<FormulaValue>.Shared.Return(rentedArgumentValues);
            }

            return FormulaValue.Null();
        }

        private FormulaFunctionContext CreateFormulaFunctionContext(EvaluationFrame frame)
        {
            return new FormulaFunctionContext(
                _formulaContext,
                frame.CurrentDocument,
                frame.CurrentTable,
                frame.CurrentRow,
                frame.ParentTable,
                frame.ParentRow);
        }

        private FormulaValue EvaluateGraphInput(List<ExpressionNode>? arguments, EvaluationFrame frame)
        {
            if (frame.CurrentTable == null || frame.CurrentRow == null)
            {
                return FormulaValue.Null();
            }

            var argumentList = arguments ?? new List<ExpressionNode>();
            if (argumentList.Count < 1)
            {
                return FormulaValue.Null();
            }

            string pinId = ToStringValue(Evaluate(argumentList[0], frame)).Trim();
            if (string.IsNullOrWhiteSpace(pinId))
            {
                return FormulaValue.Null();
            }

            if (!TryResolveNodeGraphEdgeSchema(
                    frame.CurrentTable,
                    out DocTable? edgeTable,
                    out DocColumn? fromNodeColumn,
                    out DocColumn? fromPinColumn,
                    out DocColumn? toNodeColumn,
                    out DocColumn? toPinColumn))
            {
                return FormulaValue.Null();
            }

            for (int edgeRowIndex = 0; edgeRowIndex < edgeTable!.Rows.Count; edgeRowIndex++)
            {
                DocRow edgeRow = edgeTable.Rows[edgeRowIndex];
                string toNodeId = edgeRow.GetCell(toNodeColumn!).StringValue ?? "";
                if (!string.Equals(toNodeId, frame.CurrentRow.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                string toPinId = edgeRow.GetCell(toPinColumn!).StringValue ?? "";
                if (!IsPinIdentifierMatch(frame.CurrentTable, pinId, toPinId))
                {
                    continue;
                }

                string fromNodeId = edgeRow.GetCell(fromNodeColumn!).StringValue ?? "";
                string fromPinId = edgeRow.GetCell(fromPinColumn!).StringValue ?? "";
                if (string.IsNullOrWhiteSpace(fromNodeId) || string.IsNullOrWhiteSpace(fromPinId))
                {
                    continue;
                }

                if (!_formulaContext.TryGetRowById(frame.CurrentTable, fromNodeId, out DocRow sourceRow))
                {
                    continue;
                }

                DocColumn? sourceColumn = ResolvePinColumn(frame.CurrentTable, fromPinId);
                if (sourceColumn == null)
                {
                    continue;
                }

                return GetCellAsFormulaValue(frame.CurrentTable, sourceRow, sourceColumn);
            }

            return FormulaValue.Null();
        }

        private bool TryResolveNodeGraphEdgeSchema(
            DocTable nodeTable,
            out DocTable? edgeTable,
            out DocColumn? fromNodeColumn,
            out DocColumn? fromPinColumn,
            out DocColumn? toNodeColumn,
            out DocColumn? toPinColumn)
        {
            edgeTable = null;
            fromNodeColumn = null;
            fromPinColumn = null;
            toNodeColumn = null;
            toPinColumn = null;

            DocColumn? edgeSubtableColumn = FindColumnByNameAndKind(nodeTable, NodeGraphEdgesColumnName, DocColumnKind.Subtable);
            if (edgeSubtableColumn == null || string.IsNullOrWhiteSpace(edgeSubtableColumn.SubtableId))
            {
                return false;
            }

            if (!_formulaContext.TryGetTableById(edgeSubtableColumn.SubtableId, out DocTable edgeTableById))
            {
                return false;
            }

            DocColumn? candidateFromNodeColumn = FindColumnByNameAndKind(edgeTableById, NodeGraphFromNodeColumnName, DocColumnKind.Relation);
            DocColumn? candidateFromPinColumn = FindColumnByNameAndKind(edgeTableById, NodeGraphFromPinColumnName, DocColumnKind.Text);
            DocColumn? candidateToNodeColumn = FindColumnByNameAndKind(edgeTableById, NodeGraphToNodeColumnName, DocColumnKind.Relation);
            DocColumn? candidateToPinColumn = FindColumnByNameAndKind(edgeTableById, NodeGraphToPinColumnName, DocColumnKind.Text);
            if (candidateFromNodeColumn == null ||
                candidateFromPinColumn == null ||
                candidateToNodeColumn == null ||
                candidateToPinColumn == null)
            {
                return false;
            }

            edgeTable = edgeTableById;
            fromNodeColumn = candidateFromNodeColumn;
            fromPinColumn = candidateFromPinColumn;
            toNodeColumn = candidateToNodeColumn;
            toPinColumn = candidateToPinColumn;
            return true;
        }

        private static DocColumn? FindColumnByNameAndKind(DocTable table, string name, DocColumnKind kind)
        {
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                DocColumn candidateColumn = table.Columns[columnIndex];
                if (candidateColumn.Kind == kind &&
                    string.Equals(candidateColumn.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return candidateColumn;
                }
            }

            return null;
        }

        private static DocColumn? ResolvePinColumn(DocTable table, string pinId)
        {
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                DocColumn candidateColumn = table.Columns[columnIndex];
                if (string.Equals(candidateColumn.Id, pinId, StringComparison.Ordinal) ||
                    string.Equals(candidateColumn.Name, pinId, StringComparison.OrdinalIgnoreCase))
                {
                    return candidateColumn;
                }
            }

            return null;
        }

        private static bool IsPinIdentifierMatch(DocTable table, string expectedPinId, string actualPinId)
        {
            if (string.Equals(expectedPinId, actualPinId, StringComparison.Ordinal))
            {
                return true;
            }

            DocColumn? actualPinColumn = ResolvePinColumn(table, actualPinId);
            if (actualPinColumn == null)
            {
                return false;
            }

            return string.Equals(actualPinColumn.Id, expectedPinId, StringComparison.Ordinal) ||
                   string.Equals(actualPinColumn.Name, expectedPinId, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryResolveSplinePoints(string splineJson, out SplineUtils.SplinePoint[] splinePoints)
        {
            if (_splinePointsCacheByJson.TryGetValue(splineJson, out splinePoints!))
            {
                return true;
            }

            splinePoints = SplineUtils.Deserialize(splineJson);
            _splinePointsCacheByJson[splineJson] = splinePoints;
            return true;
        }

        private FormulaValue EvaluateMethodCall(
            FormulaValue targetValue,
            string methodName,
            List<ExpressionNode>? arguments,
            EvaluationFrame frame)
        {
            var argumentList = arguments ?? new List<ExpressionNode>();
            if (targetValue.Kind == FormulaValueKind.TableReference &&
                targetValue.TableValue != null &&
                (string.Equals(methodName, "Variant", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(methodName, "V", StringComparison.OrdinalIgnoreCase)))
            {
                return EvaluateTableVariantMethod(targetValue.TableValue, argumentList, frame);
            }

            bool isCollectionLike = targetValue.Kind == FormulaValueKind.TableReference ||
                                    targetValue.Kind == FormulaValueKind.RowCollection;
            if (!isCollectionLike)
            {
                return FormulaValue.Null();
            }

            if (!TryGetRowsFromTarget(targetValue, out var sourceTable, out var sourceRows))
            {
                return FormulaValue.Null();
            }

            if (string.Equals(methodName, "Filter", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count == 0)
                {
                    return FormulaValue.Rows(sourceTable, sourceRows);
                }

                var filteredRows = new List<DocRow>();
                for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
                {
                    var candidateRow = sourceRows[rowIndex];
                    var candidateFrame = CreateCandidateEvaluationFrame(
                        frame,
                        sourceTable,
                        candidateRow,
                        ResolveRowIndexOneBased(sourceTable, candidateRow, frame));
                    var predicateValue = Evaluate(argumentList[0], candidateFrame);
                    if (ToBoolean(predicateValue))
                    {
                        filteredRows.Add(candidateRow);
                    }
                }

                return FormulaValue.Rows(sourceTable, filteredRows);
            }

            if (string.Equals(methodName, "Count", StringComparison.OrdinalIgnoreCase))
            {
                return FormulaValue.Number(sourceRows.Count);
            }

            if (string.Equals(methodName, "First", StringComparison.OrdinalIgnoreCase))
            {
                if (sourceRows.Count == 0)
                {
                    return FormulaValue.Null();
                }

                return FormulaValue.Row(sourceTable, sourceRows[0]);
            }

            if (string.Equals(methodName, "Sum", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count == 0)
                {
                    return FormulaValue.Number(0);
                }

                double sum = 0;
                for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
                {
                    var candidateRow = sourceRows[rowIndex];
                    var candidateFrame = CreateCandidateEvaluationFrame(
                        frame,
                        sourceTable,
                        candidateRow,
                        ResolveRowIndexOneBased(sourceTable, candidateRow, frame));
                    var value = Evaluate(argumentList[0], candidateFrame);
                    if (TryToNumber(value, out double numberValue))
                    {
                        sum += numberValue;
                    }
                }

                return FormulaValue.Number(sum);
            }

            if (string.Equals(methodName, "Average", StringComparison.OrdinalIgnoreCase))
            {
                if (argumentList.Count == 0 || sourceRows.Count == 0)
                {
                    return FormulaValue.Number(0);
                }

                double sum = 0;
                int count = 0;
                for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
                {
                    var candidateRow = sourceRows[rowIndex];
                    var candidateFrame = CreateCandidateEvaluationFrame(
                        frame,
                        sourceTable,
                        candidateRow,
                        ResolveRowIndexOneBased(sourceTable, candidateRow, frame));
                    var value = Evaluate(argumentList[0], candidateFrame);
                    if (TryToNumber(value, out double numberValue))
                    {
                        sum += numberValue;
                        count++;
                    }
                }

                return count == 0
                    ? FormulaValue.Number(0)
                    : FormulaValue.Number(sum / count);
            }

            if (string.Equals(methodName, "Sort", StringComparison.OrdinalIgnoreCase))
            {
                var sortedRows = new List<DocRow>(sourceRows);
                if (argumentList.Count == 0)
                {
                    sortedRows.Sort((leftRow, rightRow) => string.CompareOrdinal(leftRow.Id, rightRow.Id));
                    return FormulaValue.Rows(sourceTable, sortedRows);
                }

                var sortExpression = argumentList[0];
                sortedRows.Sort((leftRow, rightRow) =>
                {
                    var leftFrame = CreateCandidateEvaluationFrame(
                        frame,
                        sourceTable,
                        leftRow,
                        ResolveRowIndexOneBased(sourceTable, leftRow, frame));
                    var rightFrame = CreateCandidateEvaluationFrame(
                        frame,
                        sourceTable,
                        rightRow,
                        ResolveRowIndexOneBased(sourceTable, rightRow, frame));
                    var leftValue = Evaluate(sortExpression, leftFrame);
                    var rightValue = Evaluate(sortExpression, rightFrame);
                    return Compare(leftValue, rightValue);
                });

                return FormulaValue.Rows(sourceTable, sortedRows);
            }

            return FormulaValue.Null();
        }

        private FormulaValue EvaluateTableVariantMethod(DocTable baseTable, List<ExpressionNode> arguments, EvaluationFrame frame)
        {
            int variantId = 0;
            if (arguments.Count > 0)
            {
                FormulaValue argumentValue = Evaluate(arguments[0], frame);
                if (TryToNumber(argumentValue, out double numericVariant))
                {
                    variantId = (int)numericVariant;
                }
                else if (argumentValue.Kind == FormulaValueKind.String && argumentValue.StringValue != null)
                {
                    string variantName = argumentValue.StringValue;
                    if (string.Equals(variantName, DocTableVariant.BaseVariantName, StringComparison.OrdinalIgnoreCase))
                    {
                        variantId = 0;
                    }
                    else
                    {
                        for (int variantIndex = 0; variantIndex < baseTable.Variants.Count; variantIndex++)
                        {
                            if (string.Equals(baseTable.Variants[variantIndex].Name, variantName, StringComparison.OrdinalIgnoreCase))
                            {
                                variantId = baseTable.Variants[variantIndex].Id;
                                break;
                            }
                        }
                    }
                }
            }

            if (variantId <= 0)
            {
                return FormulaValue.Table(baseTable);
            }

            var cacheKey = new TableVariantCacheKey(baseTable.Id, variantId);
            if (_tableVariantCacheByKey.TryGetValue(cacheKey, out DocTable? cachedVariantTable))
            {
                return FormulaValue.Table(cachedVariantTable);
            }

            if (!TryMaterializeTableVariant(baseTable, variantId, out DocTable? variantTable))
            {
                return FormulaValue.Table(baseTable);
            }

            _tableVariantCacheByKey[cacheKey] = variantTable;
            return FormulaValue.Table(variantTable);
        }

        private static bool TryMaterializeTableVariant(DocTable baseTable, int variantId, out DocTable variantTable)
        {
            variantTable = null!;
            if (variantId == DocTableVariant.BaseVariantId)
            {
                return false;
            }

            DocTableVariantDelta? delta = null;
            for (int deltaIndex = 0; deltaIndex < baseTable.VariantDeltas.Count; deltaIndex++)
            {
                DocTableVariantDelta current = baseTable.VariantDeltas[deltaIndex];
                if (current.VariantId == variantId)
                {
                    delta = current;
                    break;
                }
            }

            if (delta == null)
            {
                return false;
            }

            var validColumnIds = new HashSet<string>(baseTable.Columns.Count, StringComparer.Ordinal);
            for (int columnIndex = 0; columnIndex < baseTable.Columns.Count; columnIndex++)
            {
                validColumnIds.Add(baseTable.Columns[columnIndex].Id);
            }

            var deletedBaseRowIds = new HashSet<string>(delta.DeletedBaseRowIds, StringComparer.Ordinal);
            var materializedRows = new List<DocRow>(baseTable.Rows.Count + delta.AddedRows.Count);
            var rowById = new Dictionary<string, DocRow>(StringComparer.Ordinal);

            for (int rowIndex = 0; rowIndex < baseTable.Rows.Count; rowIndex++)
            {
                DocRow sourceRow = baseTable.Rows[rowIndex];
                if (deletedBaseRowIds.Contains(sourceRow.Id))
                {
                    continue;
                }

                DocRow clonedRow = CloneRowTrimmedToSchema(sourceRow, validColumnIds);
                materializedRows.Add(clonedRow);
                rowById[clonedRow.Id] = clonedRow;
            }

            for (int rowIndex = 0; rowIndex < delta.AddedRows.Count; rowIndex++)
            {
                DocRow clonedRow = CloneRowTrimmedToSchema(delta.AddedRows[rowIndex], validColumnIds);
                materializedRows.Add(clonedRow);
                rowById[clonedRow.Id] = clonedRow;
            }

            for (int overrideIndex = 0; overrideIndex < delta.CellOverrides.Count; overrideIndex++)
            {
                DocTableCellOverride cellOverride = delta.CellOverrides[overrideIndex];
                if (!validColumnIds.Contains(cellOverride.ColumnId) ||
                    !rowById.TryGetValue(cellOverride.RowId, out DocRow? row))
                {
                    continue;
                }

                row.Cells[cellOverride.ColumnId] = cellOverride.Value.Clone();
            }

            variantTable = new DocTable
            {
                Id = baseTable.Id,
                Name = baseTable.Name,
                FolderId = baseTable.FolderId,
                FileName = baseTable.FileName,
                SchemaSourceTableId = baseTable.SchemaSourceTableId,
                InheritanceSourceTableId = baseTable.InheritanceSourceTableId,
                Columns = baseTable.Columns,
                Variants = baseTable.Variants,
                Rows = materializedRows,
                VariantDeltas = baseTable.VariantDeltas,
                DerivedConfig = baseTable.DerivedConfig,
                ExportConfig = baseTable.ExportConfig,
                Keys = baseTable.Keys,
                Views = baseTable.Views,
                Variables = baseTable.Variables,
                ParentTableId = baseTable.ParentTableId,
                ParentRowColumnId = baseTable.ParentRowColumnId,
            };

            return true;
        }

        private static DocRow CloneRowTrimmedToSchema(DocRow sourceRow, HashSet<string> validColumnIds)
        {
            var clone = new DocRow
            {
                Id = sourceRow.Id,
                Cells = new Dictionary<string, DocCellValue>(sourceRow.Cells.Count, StringComparer.Ordinal),
            };

            foreach (var cellEntry in sourceRow.Cells)
            {
                if (!validColumnIds.Contains(cellEntry.Key))
                {
                    continue;
                }

                clone.Cells[cellEntry.Key] = cellEntry.Value.Clone();
            }

            return clone;
        }

        private FormulaValue EvaluateLookup(List<ExpressionNode> arguments, EvaluationFrame frame)
        {
            if (arguments.Count < 2)
            {
                return FormulaValue.Null();
            }

            if (!TryResolveRowsArgument(arguments[0], frame, out var sourceTable, out var sourceRows))
            {
                return FormulaValue.Null();
            }

            for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
            {
                var candidateRow = sourceRows[rowIndex];
                var candidateFrame = CreateCandidateEvaluationFrame(
                    frame,
                    sourceTable,
                    candidateRow,
                    ResolveRowIndexOneBased(sourceTable, candidateRow, frame));
                var predicateValue = Evaluate(arguments[1], candidateFrame);
                if (!ToBoolean(predicateValue))
                {
                    continue;
                }

                if (arguments.Count < 3)
                {
                    return FormulaValue.Row(sourceTable, candidateRow);
                }

                var selectArgument = arguments[2];
                if (selectArgument.Kind == ExpressionNodeKind.StringLiteral &&
                    _formulaContext.TryGetColumnByName(sourceTable, selectArgument.Text, out var column))
                {
                    return GetCellAsFormulaValue(sourceTable, candidateRow, column);
                }

                return Evaluate(selectArgument, candidateFrame);
            }

            return FormulaValue.Null();
        }

        private FormulaValue EvaluateCountIf(List<ExpressionNode> arguments, EvaluationFrame frame)
        {
            if (arguments.Count < 2)
            {
                return FormulaValue.Number(0);
            }

            if (!TryResolveRowsArgument(arguments[0], frame, out var sourceTable, out var sourceRows))
            {
                return FormulaValue.Number(0);
            }

            int count = 0;
            for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
            {
                var candidateFrame = CreateCandidateEvaluationFrame(
                    frame,
                    sourceTable,
                    sourceRows[rowIndex],
                    ResolveRowIndexOneBased(sourceTable, sourceRows[rowIndex], frame));
                var predicateValue = Evaluate(arguments[1], candidateFrame);
                if (ToBoolean(predicateValue))
                {
                    count++;
                }
            }

            return FormulaValue.Number(count);
        }

        private FormulaValue EvaluateSumIf(List<ExpressionNode> arguments, EvaluationFrame frame)
        {
            if (arguments.Count < 3)
            {
                return FormulaValue.Number(0);
            }

            if (!TryResolveRowsArgument(arguments[0], frame, out var sourceTable, out var sourceRows))
            {
                return FormulaValue.Number(0);
            }

            double sum = 0;
            for (int rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
            {
                var candidateFrame = CreateCandidateEvaluationFrame(
                    frame,
                    sourceTable,
                    sourceRows[rowIndex],
                    ResolveRowIndexOneBased(sourceTable, sourceRows[rowIndex], frame));
                var predicateValue = Evaluate(arguments[1], candidateFrame);
                if (!ToBoolean(predicateValue))
                {
                    continue;
                }

                var value = Evaluate(arguments[2], candidateFrame);
                if (TryToNumber(value, out double numberValue))
                {
                    sum += numberValue;
                }
            }

            return FormulaValue.Number(sum);
        }

        private bool TryResolveRowsArgument(
            ExpressionNode tableArgument,
            EvaluationFrame frame,
            out DocTable sourceTable,
            out IReadOnlyList<DocRow> sourceRows)
        {
            var tableValue = Evaluate(tableArgument, frame);
            if (TryGetRowsFromTarget(tableValue, out sourceTable, out sourceRows))
            {
                return true;
            }

            if (tableArgument.Kind == ExpressionNodeKind.StringLiteral &&
                _formulaContext.TryGetTableByName(tableArgument.Text, out var tableFromString))
            {
                sourceTable = tableFromString;
                sourceRows = tableFromString.Rows;
                return true;
            }

            sourceTable = null!;
            sourceRows = null!;
            return false;
        }

        private static bool TryGetRowsFromTarget(
            FormulaValue targetValue,
            out DocTable sourceTable,
            out IReadOnlyList<DocRow> sourceRows)
        {
            if (targetValue.Kind == FormulaValueKind.TableReference && targetValue.TableValue != null)
            {
                sourceTable = targetValue.TableValue;
                sourceRows = targetValue.TableValue.Rows;
                return true;
            }

            if (targetValue.Kind == FormulaValueKind.RowCollection && targetValue.TableValue != null && targetValue.RowsValue != null)
            {
                sourceTable = targetValue.TableValue;
                sourceRows = targetValue.RowsValue;
                return true;
            }

            sourceTable = null!;
            sourceRows = null!;
            return false;
        }

        private int ResolveRowIndexOneBased(DocTable table, DocRow row, EvaluationFrame frame)
        {
            if (ReferenceEquals(table, frame.CurrentTable) &&
                ReferenceEquals(row, frame.CurrentRow) &&
                frame.CurrentRowIndexOneBased > 0)
            {
                return frame.CurrentRowIndexOneBased;
            }

            if (frame.CandidateTable != null &&
                frame.CandidateRow != null &&
                ReferenceEquals(table, frame.CandidateTable) &&
                ReferenceEquals(row, frame.CandidateRow) &&
                frame.CandidateRowIndexOneBased > 0)
            {
                return frame.CandidateRowIndexOneBased;
            }

            if (frame.ParentTable != null &&
                frame.ParentRow != null &&
                ReferenceEquals(table, frame.ParentTable) &&
                ReferenceEquals(row, frame.ParentRow) &&
                frame.ParentRowIndexOneBased > 0)
            {
                return frame.ParentRowIndexOneBased;
            }

            int oneBasedIndex = _formulaContext.GetRowIndexOneBased(table, row);
            if (oneBasedIndex > 0)
            {
                return oneBasedIndex;
            }

            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                DocRow candidateRow = table.Rows[rowIndex];
                if (ReferenceEquals(candidateRow, row) ||
                    string.Equals(candidateRow.Id, row.Id, StringComparison.Ordinal))
                {
                    return rowIndex + 1;
                }
            }

            return 0;
        }

        private FormulaValue GetCellAsFormulaValue(DocTable table, DocRow row, DocColumn column)
        {
            var cell = row.GetCell(column);
            if (string.Equals(cell.FormulaError, FormulaErrorText, StringComparison.Ordinal) ||
                string.Equals(cell.StringValue, FormulaErrorText, StringComparison.Ordinal))
            {
                return FormulaValue.Null();
            }

            switch (column.Kind)
            {
                case DocColumnKind.Number:
                    return FormulaValue.Number(cell.NumberValue);
                case DocColumnKind.Checkbox:
                    return FormulaValue.Bool(cell.BoolValue);
                case DocColumnKind.Relation:
                    {
                        string relationRowId = cell.StringValue ?? "";
                        string? relationTableId = DocRelationTargetResolver.ResolveTargetTableId(table, column);
                        if (string.IsNullOrEmpty(relationRowId) || string.IsNullOrEmpty(relationTableId))
                        {
                            return FormulaValue.String(relationRowId);
                        }

                        if (!_formulaContext.TryGetTableById(relationTableId, out var relationTable))
                        {
                            return FormulaValue.String(relationRowId);
                        }

                        if (!_formulaContext.TryGetRowById(relationTable, relationRowId, out var relationRow))
                        {
                            return FormulaValue.String(relationRowId);
                        }

                        return FormulaValue.Row(relationTable, relationRow);
                    }
                case DocColumnKind.TableRef:
                    {
                        string tableId = cell.StringValue ?? "";
                        if (string.IsNullOrWhiteSpace(tableId))
                        {
                            return FormulaValue.Null();
                        }

                        if (_formulaContext.TryGetTableById(tableId, out var referencedTable))
                        {
                            return FormulaValue.Table(referencedTable);
                        }

                        return FormulaValue.Null();
                    }
                case DocColumnKind.Subtable:
                    {
                        if (string.IsNullOrWhiteSpace(column.SubtableId) ||
                            !_formulaContext.TryGetTableById(column.SubtableId, out DocTable subtableTable))
                        {
                            return FormulaValue.Null();
                        }

                        if (string.IsNullOrWhiteSpace(subtableTable.ParentRowColumnId))
                        {
                            return FormulaValue.Table(subtableTable);
                        }

                        if (!TryGetSubtableParentRowColumn(subtableTable, subtableTable.ParentRowColumnId, out DocColumn parentRowColumn))
                        {
                            return FormulaValue.Table(subtableTable);
                        }

                        return FormulaValue.Rows(
                            subtableTable,
                            BuildSubtableRowsForParentRow(subtableTable, parentRowColumn, row.Id));
                    }
                case DocColumnKind.Formula:
                    {
                        if (!string.IsNullOrEmpty(cell.StringValue))
                        {
                            if (double.TryParse(cell.StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedNumber))
                            {
                                return FormulaValue.Number(parsedNumber);
                            }

                            if (bool.TryParse(cell.StringValue, out bool parsedBool))
                            {
                                return FormulaValue.Bool(parsedBool);
                            }

                            if (DateTime.TryParse(cell.StringValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsedDate))
                            {
                                return FormulaValue.DateTime(parsedDate);
                            }

                            return FormulaValue.String(cell.StringValue);
                        }

                        return FormulaValue.Number(cell.NumberValue);
                    }
                case DocColumnKind.Spline:
                    return FormulaValue.String(cell.StringValue ?? "");
                case DocColumnKind.Vec2:
                    return FormulaValue.Vec2(cell.XValue, cell.YValue);
                case DocColumnKind.Vec3:
                    return FormulaValue.Vec3(cell.XValue, cell.YValue, cell.ZValue);
                case DocColumnKind.Vec4:
                    return FormulaValue.Vec4(cell.XValue, cell.YValue, cell.ZValue, cell.WValue);
                case DocColumnKind.Color:
                    return FormulaValue.Color(cell.XValue, cell.YValue, cell.ZValue, cell.WValue);
                case DocColumnKind.Id:
                default:
                    if (column.Kind == DocColumnKind.Id)
                    {
                        return FormulaValue.String(string.IsNullOrWhiteSpace(cell.StringValue) ? row.Id : cell.StringValue);
                    }

                    return FormulaValue.String(cell.StringValue ?? "");
            }
        }

        private static bool TryGetSubtableParentRowColumn(
            DocTable subtableTable,
            string parentRowColumnId,
            out DocColumn parentRowColumn)
        {
            parentRowColumn = null!;
            for (int columnIndex = 0; columnIndex < subtableTable.Columns.Count; columnIndex++)
            {
                DocColumn candidateColumn = subtableTable.Columns[columnIndex];
                if (string.Equals(candidateColumn.Id, parentRowColumnId, StringComparison.Ordinal))
                {
                    parentRowColumn = candidateColumn;
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<DocRow> BuildSubtableRowsForParentRow(
            DocTable subtableTable,
            DocColumn parentRowColumn,
            string parentRowId)
        {
            if (string.IsNullOrWhiteSpace(parentRowId))
            {
                return Array.Empty<DocRow>();
            }

            List<DocRow>? matchingRows = null;
            for (int rowIndex = 0; rowIndex < subtableTable.Rows.Count; rowIndex++)
            {
                DocRow candidateRow = subtableTable.Rows[rowIndex];
                string candidateParentRowId = candidateRow.GetCell(parentRowColumn).StringValue ?? "";
                if (!string.Equals(candidateParentRowId, parentRowId, StringComparison.Ordinal))
                {
                    continue;
                }

                matchingRows ??= new List<DocRow>();
                matchingRows.Add(candidateRow);
            }

            if (matchingRows == null)
            {
                return Array.Empty<DocRow>();
            }

            return matchingRows;
        }

        private static bool TryToNumber(FormulaValue value, out double numberValue)
        {
            switch (value.Kind)
            {
                case FormulaValueKind.Number:
                    numberValue = value.NumberValue;
                    return true;
                case FormulaValueKind.Bool:
                    numberValue = value.BoolValue ? 1 : 0;
                    return true;
                case FormulaValueKind.String:
                    return double.TryParse(value.StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out numberValue);
                default:
                    numberValue = 0;
                    return false;
            }
        }

        private static bool ToBoolean(FormulaValue value)
        {
            return value.Kind switch
            {
                FormulaValueKind.Bool => value.BoolValue,
                FormulaValueKind.Number => Math.Abs(value.NumberValue) > double.Epsilon,
                FormulaValueKind.String => !string.IsNullOrEmpty(value.StringValue),
                FormulaValueKind.Vec2 => Math.Abs(value.XValue) > double.Epsilon ||
                                         Math.Abs(value.YValue) > double.Epsilon,
                FormulaValueKind.Vec3 => Math.Abs(value.XValue) > double.Epsilon ||
                                         Math.Abs(value.YValue) > double.Epsilon ||
                                         Math.Abs(value.ZValue) > double.Epsilon,
                FormulaValueKind.Vec4 => Math.Abs(value.XValue) > double.Epsilon ||
                                         Math.Abs(value.YValue) > double.Epsilon ||
                                         Math.Abs(value.ZValue) > double.Epsilon ||
                                         Math.Abs(value.WValue) > double.Epsilon,
                FormulaValueKind.Color => Math.Abs(value.XValue) > double.Epsilon ||
                                          Math.Abs(value.YValue) > double.Epsilon ||
                                          Math.Abs(value.ZValue) > double.Epsilon ||
                                          Math.Abs(value.WValue) > double.Epsilon,
                FormulaValueKind.DocumentReference => value.DocumentValue != null,
                FormulaValueKind.RowReference => value.RowValue != null,
                FormulaValueKind.RowCollection => value.RowsValue != null && value.RowsValue.Count > 0,
                _ => false
            };
        }

        private static string ToStringValue(FormulaValue value)
        {
            return value.Kind switch
            {
                FormulaValueKind.String => value.StringValue ?? "",
                FormulaValueKind.Number => value.NumberValue.ToString(CultureInfo.InvariantCulture),
                FormulaValueKind.Bool => value.BoolValue ? "true" : "false",
                FormulaValueKind.Vec2 => "(" + value.XValue.ToString(CultureInfo.InvariantCulture) + ", " +
                                         value.YValue.ToString(CultureInfo.InvariantCulture) + ")",
                FormulaValueKind.Vec3 => "(" + value.XValue.ToString(CultureInfo.InvariantCulture) + ", " +
                                         value.YValue.ToString(CultureInfo.InvariantCulture) + ", " +
                                         value.ZValue.ToString(CultureInfo.InvariantCulture) + ")",
                FormulaValueKind.Vec4 => "(" + value.XValue.ToString(CultureInfo.InvariantCulture) + ", " +
                                         value.YValue.ToString(CultureInfo.InvariantCulture) + ", " +
                                         value.ZValue.ToString(CultureInfo.InvariantCulture) + ", " +
                                         value.WValue.ToString(CultureInfo.InvariantCulture) + ")",
                FormulaValueKind.Color => "rgba(" + value.XValue.ToString(CultureInfo.InvariantCulture) + ", " +
                                          value.YValue.ToString(CultureInfo.InvariantCulture) + ", " +
                                          value.ZValue.ToString(CultureInfo.InvariantCulture) + ", " +
                                          value.WValue.ToString(CultureInfo.InvariantCulture) + ")",
                FormulaValueKind.DateTime => value.DateTimeValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FormulaValueKind.DocumentReference => value.DocumentValue?.Title ?? "",
                FormulaValueKind.Null => "",
                _ => ""
            };
        }

        private static bool AreEqual(FormulaValue leftValue, FormulaValue rightValue)
        {
            if (TryGetVectorDimension(leftValue, out int leftDimension) &&
                TryGetVectorDimension(rightValue, out int rightDimension) &&
                leftDimension == rightDimension)
            {
                return Math.Abs(leftValue.XValue - rightValue.XValue) < 0.000001 &&
                       Math.Abs(leftValue.YValue - rightValue.YValue) < 0.000001 &&
                       (leftDimension < 3 || Math.Abs(leftValue.ZValue - rightValue.ZValue) < 0.000001) &&
                       (leftDimension < 4 || Math.Abs(leftValue.WValue - rightValue.WValue) < 0.000001);
            }

            if (leftValue.Kind == FormulaValueKind.DateTime && rightValue.Kind == FormulaValueKind.DateTime)
            {
                return leftValue.DateTimeValue == rightValue.DateTimeValue;
            }

            if (TryToNumber(leftValue, out double leftNumber) && TryToNumber(rightValue, out double rightNumber))
            {
                return Math.Abs(leftNumber - rightNumber) < 0.000001;
            }

            return string.Equals(ToStringValue(leftValue), ToStringValue(rightValue), StringComparison.OrdinalIgnoreCase);
        }

        private static int Compare(FormulaValue leftValue, FormulaValue rightValue)
        {
            if (TryGetVectorDimension(leftValue, out int leftDimension) &&
                TryGetVectorDimension(rightValue, out int rightDimension) &&
                leftDimension == rightDimension)
            {
                int compareX = leftValue.XValue.CompareTo(rightValue.XValue);
                if (compareX != 0)
                {
                    return compareX;
                }

                int compareY = leftValue.YValue.CompareTo(rightValue.YValue);
                if (compareY != 0)
                {
                    return compareY;
                }

                if (leftDimension >= 3)
                {
                    int compareZ = leftValue.ZValue.CompareTo(rightValue.ZValue);
                    if (compareZ != 0)
                    {
                        return compareZ;
                    }
                }

                if (leftDimension >= 4)
                {
                    return leftValue.WValue.CompareTo(rightValue.WValue);
                }

                return 0;
            }

            if (leftValue.Kind == FormulaValueKind.DateTime && rightValue.Kind == FormulaValueKind.DateTime)
            {
                return leftValue.DateTimeValue.CompareTo(rightValue.DateTimeValue);
            }

            if (TryToNumber(leftValue, out double leftNumber) && TryToNumber(rightValue, out double rightNumber))
            {
                return leftNumber.CompareTo(rightNumber);
            }

            return string.Compare(ToStringValue(leftValue), ToStringValue(rightValue), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryAddVectors(FormulaValue leftValue, FormulaValue rightValue, out FormulaValue result)
        {
            result = FormulaValue.Null();
            if (!TryGetVectorDimension(leftValue, out int leftDimension) ||
                !TryGetVectorDimension(rightValue, out int rightDimension) ||
                leftDimension != rightDimension)
            {
                return false;
            }

            if ((leftValue.Kind == FormulaValueKind.Color) != (rightValue.Kind == FormulaValueKind.Color))
            {
                return false;
            }

            return TryCreateVectorResult(
                leftValue.Kind == FormulaValueKind.Color ? FormulaValueKind.Color : leftValue.Kind,
                leftValue.XValue + rightValue.XValue,
                leftValue.YValue + rightValue.YValue,
                leftValue.ZValue + rightValue.ZValue,
                leftValue.WValue + rightValue.WValue,
                out result);
        }

        private static bool TrySubtractVectors(FormulaValue leftValue, FormulaValue rightValue, out FormulaValue result)
        {
            result = FormulaValue.Null();
            if (!TryGetVectorDimension(leftValue, out int leftDimension) ||
                !TryGetVectorDimension(rightValue, out int rightDimension) ||
                leftDimension != rightDimension)
            {
                return false;
            }

            if ((leftValue.Kind == FormulaValueKind.Color) != (rightValue.Kind == FormulaValueKind.Color))
            {
                return false;
            }

            return TryCreateVectorResult(
                leftValue.Kind == FormulaValueKind.Color ? FormulaValueKind.Color : leftValue.Kind,
                leftValue.XValue - rightValue.XValue,
                leftValue.YValue - rightValue.YValue,
                leftValue.ZValue - rightValue.ZValue,
                leftValue.WValue - rightValue.WValue,
                out result);
        }

        private static bool TryScaleVector(FormulaValue leftValue, FormulaValue rightValue, out FormulaValue result)
        {
            result = FormulaValue.Null();

            if (TryGetVectorDimension(leftValue, out _) &&
                TryToNumber(rightValue, out double scalarRight))
            {
                return TryCreateVectorResult(
                    leftValue.Kind,
                    leftValue.XValue * scalarRight,
                    leftValue.YValue * scalarRight,
                    leftValue.ZValue * scalarRight,
                    leftValue.WValue * scalarRight,
                    out result);
            }

            if (TryToNumber(leftValue, out double scalarLeft) &&
                TryGetVectorDimension(rightValue, out _))
            {
                return TryCreateVectorResult(
                    rightValue.Kind,
                    rightValue.XValue * scalarLeft,
                    rightValue.YValue * scalarLeft,
                    rightValue.ZValue * scalarLeft,
                    rightValue.WValue * scalarLeft,
                    out result);
            }

            return false;
        }

        private static bool TryDivideVector(FormulaValue leftValue, FormulaValue rightValue, out FormulaValue result)
        {
            result = FormulaValue.Null();
            if (!TryGetVectorDimension(leftValue, out _) ||
                !TryToNumber(rightValue, out double divisor) ||
                Math.Abs(divisor) < double.Epsilon)
            {
                return false;
            }

            return TryCreateVectorResult(
                leftValue.Kind,
                leftValue.XValue / divisor,
                leftValue.YValue / divisor,
                leftValue.ZValue / divisor,
                leftValue.WValue / divisor,
                out result);
        }

        private static bool TryCreateVectorResult(
            FormulaValueKind kind,
            double xValue,
            double yValue,
            double zValue,
            double wValue,
            out FormulaValue result)
        {
            switch (kind)
            {
                case FormulaValueKind.Vec2:
                    result = FormulaValue.Vec2(xValue, yValue);
                    return true;
                case FormulaValueKind.Vec3:
                    result = FormulaValue.Vec3(xValue, yValue, zValue);
                    return true;
                case FormulaValueKind.Vec4:
                    result = FormulaValue.Vec4(xValue, yValue, zValue, wValue);
                    return true;
                case FormulaValueKind.Color:
                    result = FormulaValue.Color(xValue, yValue, zValue, wValue);
                    return true;
                default:
                    result = FormulaValue.Null();
                    return false;
            }
        }

        private static bool TryGetVectorDimension(FormulaValue value, out int dimension)
        {
            switch (value.Kind)
            {
                case FormulaValueKind.Vec2:
                    dimension = 2;
                    return true;
                case FormulaValueKind.Vec3:
                    dimension = 3;
                    return true;
                case FormulaValueKind.Vec4:
                case FormulaValueKind.Color:
                    dimension = 4;
                    return true;
                default:
                    dimension = 0;
                    return false;
            }
        }
    }

    private enum ExpressionNodeKind
    {
        NullLiteral,
        NumberLiteral,
        StringLiteral,
        BoolLiteral,
        Identifier,
        AtIdentifier,
        Unary,
        Binary,
        Conditional,
        MemberAccess,
        Call
    }

    private sealed class ExpressionNode
    {
        public ExpressionNodeKind Kind { get; }
        public string Text { get; }
        public double NumberValue { get; }
        public bool BoolValue { get; }
        public ExpressionNode? Left { get; }
        public ExpressionNode? Right { get; }
        public ExpressionNode? Third { get; }
        public ExpressionNode? Callee { get; }
        public List<ExpressionNode>? Arguments { get; }

        private ExpressionNode(
            ExpressionNodeKind kind,
            string text = "",
            double numberValue = 0,
            bool boolValue = false,
            ExpressionNode? left = null,
            ExpressionNode? right = null,
            ExpressionNode? third = null,
            ExpressionNode? callee = null,
            List<ExpressionNode>? arguments = null)
        {
            Kind = kind;
            Text = text;
            NumberValue = numberValue;
            BoolValue = boolValue;
            Left = left;
            Right = right;
            Third = third;
            Callee = callee;
            Arguments = arguments;
        }

        public static ExpressionNode NullLiteral() => new(ExpressionNodeKind.NullLiteral);
        public static ExpressionNode NumberLiteral(double value) => new(ExpressionNodeKind.NumberLiteral, numberValue: value);
        public static ExpressionNode StringLiteral(string value) => new(ExpressionNodeKind.StringLiteral, text: value);
        public static ExpressionNode BoolLiteral(bool value) => new(ExpressionNodeKind.BoolLiteral, boolValue: value);
        public static ExpressionNode Identifier(string identifier) => new(ExpressionNodeKind.Identifier, text: identifier);
        public static ExpressionNode AtIdentifier(string identifier) => new(ExpressionNodeKind.AtIdentifier, text: identifier);
        public static ExpressionNode Unary(string op, ExpressionNode operand) => new(ExpressionNodeKind.Unary, text: op, left: operand);
        public static ExpressionNode Binary(string op, ExpressionNode left, ExpressionNode right) => new(ExpressionNodeKind.Binary, text: op, left: left, right: right);
        public static ExpressionNode Conditional(ExpressionNode condition, ExpressionNode whenTrue, ExpressionNode whenFalse) => new(ExpressionNodeKind.Conditional, left: condition, right: whenTrue, third: whenFalse);
        public static ExpressionNode MemberAccess(ExpressionNode target, string memberName) => new(ExpressionNodeKind.MemberAccess, text: memberName, left: target);
        public static ExpressionNode Call(ExpressionNode callee, List<ExpressionNode> arguments) => new(ExpressionNodeKind.Call, callee: callee, arguments: arguments);
    }
}
