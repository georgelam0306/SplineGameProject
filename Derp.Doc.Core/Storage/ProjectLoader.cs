using System.Globalization;
using System.Text.Json;
using Derp.Doc.Model;
using Derp.Doc.Plugins;
using Derp.Doc.Tables;

namespace Derp.Doc.Storage;

/// <summary>
/// Loads a DocProject from a .derpdoc directory.
/// </summary>
internal static class ProjectLoader
{
    public static DocProject Load(string directoryPath)
    {
        var projectJsonPath = Path.Combine(directoryPath, "project.json");
        if (!File.Exists(projectJsonPath))
            throw new FileNotFoundException("project.json not found", projectJsonPath);

        var projectJson = File.ReadAllText(projectJsonPath);
        var projectDto = JsonSerializer.Deserialize(projectJson, DocJsonContext.Default.ProjectDto)
            ?? throw new InvalidOperationException("Failed to parse project.json");

        var project = new DocProject
        {
            Name = projectDto.Name,
            UiState = DeserializeProjectUiState(projectDto.Ui),
            PluginSettingsByKey = projectDto.PluginSettings != null
                ? new Dictionary<string, string>(projectDto.PluginSettings, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal),
        };
        // Legacy migration: variants used to live at the project level.
        // If present, we copy them into each table that does not explicitly define its own variants.
        List<DocTableVariant>? legacyTableVariants = null;
        if (projectDto.Variants != null && projectDto.Variants.Count > 0)
        {
            legacyTableVariants = new List<DocTableVariant>(projectDto.Variants.Count);
            for (int variantIndex = 0; variantIndex < projectDto.Variants.Count; variantIndex++)
            {
                var variantDto = projectDto.Variants[variantIndex];
                if (variantDto.Id == DocTableVariant.BaseVariantId)
                {
                    continue;
                }

                legacyTableVariants.Add(new DocTableVariant
                {
                    Id = variantDto.Id,
                    Name = variantDto.Name,
                });
            }
        }

        if (projectDto.Folders != null)
        {
            for (int folderIndex = 0; folderIndex < projectDto.Folders.Count; folderIndex++)
            {
                var folderDto = projectDto.Folders[folderIndex];
                project.Folders.Add(new DocFolder
                {
                    Id = folderDto.Id,
                    Name = folderDto.Name,
                    Scope = Enum.TryParse<DocFolderScope>(folderDto.Scope, out var scope)
                        ? scope
                        : DocFolderScope.Tables,
                    ParentFolderId = folderDto.ParentFolderId,
                });
            }
        }

        var tablesDir = Path.Combine(directoryPath, "tables");
        var seenTableRefIds = new HashSet<string>(StringComparer.Ordinal);
        var seenTableFileNames = new HashSet<string>(StringComparer.Ordinal);
        var loadedTableIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tableRef in projectDto.Tables)
        {
            if (string.IsNullOrWhiteSpace(tableRef.Id) || string.IsNullOrWhiteSpace(tableRef.FileName))
            {
                continue;
            }

            if (!seenTableRefIds.Add(tableRef.Id))
            {
                continue;
            }

            if (!seenTableFileNames.Add(tableRef.FileName))
            {
                continue;
            }

            var table = LoadTable(tableRef, tablesDir, legacyTableVariants);
            if (!loadedTableIds.Add(table.Id))
            {
                continue;
            }

            project.Tables.Add(table);
        }

        // Load documents
        if (projectDto.Documents != null)
        {
            var docsDir = Path.Combine(directoryPath, "docs");
            foreach (var docRef in projectDto.Documents)
            {
                var document = DocumentLoader.Load(docRef, docsDir);
                project.Documents.Add(document);
            }
        }

        DocSystemTableSynchronizer.Synchronize(project, directoryPath);
        SchemaLinkedTableSynchronizer.Synchronize(project);
        ValidateFolderReferences(project);

        return project;
    }

    private static DocTable LoadTable(TableRefDto tableRef, string tablesDir, List<DocTableVariant>? legacyTableVariants)
    {
        // Load schema
        var schemaPath = Path.Combine(tablesDir, $"{tableRef.FileName}.schema.json");
        var schemaJson = File.ReadAllText(schemaPath);
        var schemaDto = JsonSerializer.Deserialize(schemaJson, DocJsonContext.Default.TableSchemaDto)
            ?? throw new InvalidOperationException($"Failed to parse {schemaPath}");

        var table = new DocTable
        {
            Id = schemaDto.Id,
            Name = schemaDto.Name,
            FileName = tableRef.FileName,
            FolderId = tableRef.FolderId,
            SchemaSourceTableId = schemaDto.SchemaSourceTableId,
            InheritanceSourceTableId = schemaDto.InheritanceSourceTableId,
            SystemKey = schemaDto.SystemKey,
            IsSystemSchemaLocked = schemaDto.SystemSchemaLocked,
            IsSystemDataLocked = schemaDto.SystemDataLocked,
            ParentTableId = schemaDto.ParentTableId,
            ParentRowColumnId = schemaDto.ParentRowColumnId,
            PluginTableTypeId = schemaDto.PluginTableTypeId,
            PluginOwnerColumnTypeId = schemaDto.PluginOwnerColumnTypeId,
            IsPluginSchemaLocked = schemaDto.PluginSchemaLocked,
        };

        if (schemaDto.Variants != null && schemaDto.Variants.Count > 0)
        {
            for (int variantIndex = 0; variantIndex < schemaDto.Variants.Count; variantIndex++)
            {
                var variantDto = schemaDto.Variants[variantIndex];
                if (variantDto.Id == DocTableVariant.BaseVariantId)
                {
                    continue;
                }

                table.Variants.Add(new DocTableVariant
                {
                    Id = variantDto.Id,
                    Name = variantDto.Name,
                });
            }
        }
        else if (legacyTableVariants != null && legacyTableVariants.Count > 0)
        {
            // Copy legacy project variants into this table (table-level variants going forward).
            for (int variantIndex = 0; variantIndex < legacyTableVariants.Count; variantIndex++)
            {
                table.Variants.Add(legacyTableVariants[variantIndex].Clone());
            }
        }

        // Build column lookup for cell deserialization
        var columnLookup = new Dictionary<string, DocColumn>();
        foreach (var colDto in schemaDto.Columns)
        {
            DocColumnKind parsedKind = Enum.TryParse<DocColumnKind>(colDto.Kind, out var kind)
                ? kind
                : DocColumnKind.Text;
            string? columnTypeId = colDto.TypeId;
            ApplyLegacyUiColumnTypeMigration(ref parsedKind, ref columnTypeId);
            string resolvedColumnTypeId = DocColumnTypeIdMapper.Resolve(columnTypeId, parsedKind);
            DocRelationTargetMode relationTargetMode = parsedKind == DocColumnKind.Relation
                ? DeserializeRelationTargetMode(colDto.RelationTargetMode)
                : DocRelationTargetMode.ExternalTable;
            string? relationTableId = parsedKind == DocColumnKind.Relation
                ? DocRelationTargetResolver.ResolveTargetTableId(
                    table,
                    relationTargetMode,
                    colDto.RelationTableId)
                : null;
            var column = new DocColumn
            {
                Id = colDto.Id,
                Name = colDto.Name,
                Kind = parsedKind,
                ColumnTypeId = resolvedColumnTypeId,
                PluginSettingsJson = colDto.PluginSettings,
                Width = colDto.Width,
                Options = colDto.Options,
                FormulaExpression = colDto.Formula ?? "",
                RelationTableId = relationTableId,
                TableRefBaseTableId = colDto.TableRefBaseTableId,
                RowRefTableRefColumnId = colDto.RowRefTableRefColumnId,
                RelationTargetMode = relationTargetMode,
                RelationTableVariantId = colDto.RelationTableVariantId,
                RelationDisplayColumnId = colDto.RelationDisplayColumnId,
                IsHidden = colDto.Hidden,
                IsProjected = colDto.Projected,
                IsInherited = colDto.Inherited,
                ExportType = colDto.ExportType,
                NumberMin = colDto.NumberMin,
                NumberMax = colDto.NumberMax,
                ExportEnumName = colDto.ExportEnumName,
                ExportIgnore = colDto.ExportIgnore,
                SubtableId = colDto.SubtableId,
                SubtableDisplayRendererId = colDto.SubtableDisplayRendererId,
                SubtableDisplayCellWidth = colDto.SubtableDisplayCellWidth,
                SubtableDisplayCellHeight = colDto.SubtableDisplayCellHeight,
                SubtableDisplayPreviewQuality = DeserializeSubtablePreviewQuality(colDto.SubtableDisplayPreviewQuality),
                FormulaEvalScopes = DeserializeFormulaEvalScopes(colDto),
                ModelPreviewSettings = DeserializeModelPreview(colDto),
            };
            table.Columns.Add(column);
            columnLookup[column.Id] = column;
        }

        // Load derived config if present
        if (schemaDto.Derived != null)
        {
            table.DerivedConfig = DeserializeDerivedConfig(schemaDto.Derived);
        }

        // Load export config if present
        if (schemaDto.Export != null)
        {
            table.ExportConfig = new DocTableExportConfig
            {
                Enabled = schemaDto.Export.Enabled,
                Namespace = schemaDto.Export.Namespace ?? "",
                StructName = schemaDto.Export.StructName ?? "",
            };
        }

        // Load key metadata if present
        if (schemaDto.Keys != null)
        {
            table.Keys.PrimaryKeyColumnId = schemaDto.Keys.PrimaryKeyColumnId ?? "";
            if (schemaDto.Keys.SecondaryKeys != null)
            {
                table.Keys.SecondaryKeys.Clear();
                for (int i = 0; i < schemaDto.Keys.SecondaryKeys.Count; i++)
                {
                    table.Keys.SecondaryKeys.Add(new DocSecondaryKey
                    {
                        ColumnId = schemaDto.Keys.SecondaryKeys[i].ColumnId,
                        Unique = schemaDto.Keys.SecondaryKeys[i].Unique,
                    });
                }
            }
        }

        if (schemaDto.Variables != null)
        {
            for (int variableIndex = 0; variableIndex < schemaDto.Variables.Count; variableIndex++)
            {
                var variableDto = schemaDto.Variables[variableIndex];
                DocColumnKind variableKind = Enum.TryParse<DocColumnKind>(variableDto.Kind, ignoreCase: true, out var parsedVariableKind)
                    ? parsedVariableKind
                    : DocColumnKind.Text;
                string? variableTypeId = variableDto.TypeId;
                ApplyLegacyUiColumnTypeMigration(ref variableKind, ref variableTypeId);
                table.Variables.Add(new DocTableVariable
                {
                    Id = string.IsNullOrWhiteSpace(variableDto.Id) ? Guid.NewGuid().ToString() : variableDto.Id,
                    Name = variableDto.Name,
                    Kind = variableKind,
                    ColumnTypeId = DocColumnTypeIdMapper.Resolve(variableTypeId, variableKind),
                    Expression = variableDto.Expression ?? "",
                });
            }
        }

        // Load views (Phase 6)
        LoadViews(table, tableRef.FileName, tablesDir);

        if (table.IsDerived)
        {
            // Derived tables: load own-data.jsonl (local cell data only).
            // Rows are materialized later via the formula engine.
            LoadOwnData(table, tableRef.FileName, tablesDir, columnLookup);
            LoadDerivedVariantOwnData(table, tableRef.FileName, tablesDir, columnLookup);
        }
        else
        {
            // Load rows from JSONL
            LoadRows(table, tableRef.FileName, tablesDir, columnLookup);
            LoadVariantOperations(table, tableRef.FileName, tablesDir, columnLookup);
        }

        return table;
    }

    private static DocFormulaEvalScope DeserializeFormulaEvalScopes(ColumnDto columnDto)
    {
        if (Enum.TryParse<DocFormulaEvalScope>(
            columnDto.FormulaEvalScopes,
            ignoreCase: true,
            out var parsedScopes))
        {
            return parsedScopes;
        }

        // Backward-compat for old schema values.
        if (string.Equals(columnDto.LegacyLivePreviewPriority, "ChartImmediate", StringComparison.OrdinalIgnoreCase))
        {
            return DocFormulaEvalScope.Interactive;
        }

        return DocFormulaEvalScope.None;
    }

    private static void ApplyLegacyUiColumnTypeMigration(ref DocColumnKind kind, ref string? columnTypeId)
    {
        if (string.Equals(columnTypeId, "derp.ui", StringComparison.OrdinalIgnoreCase))
        {
            kind = DocColumnKind.UiAsset;
            columnTypeId = DocColumnTypeIds.UiAsset;
        }
    }

    private static DocSubtablePreviewQuality? DeserializeSubtablePreviewQuality(string? serializedQuality)
    {
        if (!Enum.TryParse<DocSubtablePreviewQuality>(
                serializedQuality,
                ignoreCase: true,
                out var parsedQuality))
        {
            return null;
        }

        return parsedQuality;
    }

    private static DocRelationTargetMode DeserializeRelationTargetMode(string? serializedMode)
    {
        if (!Enum.TryParse<DocRelationTargetMode>(
                serializedMode,
                ignoreCase: true,
                out var parsedMode))
        {
            return DocRelationTargetMode.ExternalTable;
        }

        return parsedMode;
    }

    private static DocModelPreviewSettings? DeserializeModelPreview(ColumnDto columnDto)
    {
        return DeserializeModelPreview(columnDto.ModelPreview);
    }

    private static DocModelPreviewSettings? DeserializeModelPreview(ModelPreviewDto? modelPreviewDto)
    {
        if (modelPreviewDto == null)
        {
            return null;
        }

        var settings = new DocModelPreviewSettings
        {
            OrbitYawDegrees = modelPreviewDto.OrbitYawDegrees ?? DocModelPreviewSettings.DefaultOrbitYawDegrees,
            OrbitPitchDegrees = modelPreviewDto.OrbitPitchDegrees ?? DocModelPreviewSettings.DefaultOrbitPitchDegrees,
            PanX = modelPreviewDto.PanX ?? DocModelPreviewSettings.DefaultPanX,
            PanY = modelPreviewDto.PanY ?? DocModelPreviewSettings.DefaultPanY,
            Zoom = modelPreviewDto.Zoom ?? DocModelPreviewSettings.DefaultZoom,
            TextureRelativePath = modelPreviewDto.TextureRelativePath,
        };
        settings.ClampInPlace();
        return DocModelPreviewSettings.IsDefault(settings) ? null : settings;
    }

    private static void LoadRows(DocTable table, string fileName, string tablesDir, Dictionary<string, DocColumn> columnLookup)
    {
        var rowsPath = Path.Combine(tablesDir, $"{fileName}.rows.jsonl");
        if (!File.Exists(rowsPath)) return;

        foreach (var line in File.ReadLines(rowsPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var rowDto = JsonSerializer.Deserialize(line, DocJsonCompactContext.Default.RowDto);
            if (rowDto == null) continue;

            var row = new DocRow { Id = rowDto.Id };

            foreach (var (columnId, element) in rowDto.Cells)
            {
                if (!columnLookup.TryGetValue(columnId, out var column)) continue;

                DocCellValue cellValue = DeserializeCellValueFromStorage(column, element);

                row.Cells[columnId] = cellValue;
            }

            EnsureRowHasAutoIdCells(table, row);
            table.Rows.Add(row);
        }
    }

    private static void LoadOwnData(DocTable table, string fileName, string tablesDir, Dictionary<string, DocColumn> columnLookup)
    {
        var ownDataPath = Path.Combine(tablesDir, $"{fileName}.own-data.jsonl");
        if (!File.Exists(ownDataPath)) return;

        // Store own-data keyed by row ID. Rows will be created during materialization;
        // the formula engine restores local cells from Rows that already have matching IDs.
        // We add placeholder rows here so the engine can find them.
        foreach (var line in File.ReadLines(ownDataPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var dto = JsonSerializer.Deserialize(line, DocJsonCompactContext.Default.OwnDataRowDto);
            if (dto == null) continue;

            var row = new DocRow { Id = dto.Id };
            foreach (var (columnId, element) in dto.Cells)
            {
                if (!columnLookup.TryGetValue(columnId, out var column)) continue;

                DocCellValue cellValue = DeserializeCellValueFromStorage(column, element);
                row.Cells[columnId] = cellValue;
            }

            EnsureRowHasAutoIdCells(table, row);
            table.Rows.Add(row);
        }
    }

    private static void LoadVariantOperations(
        DocTable table,
        string fileName,
        string tablesDir,
        Dictionary<string, DocColumn> columnLookup)
    {
        string variantsPath = Path.Combine(tablesDir, fileName + ".variants.jsonl");
        if (!File.Exists(variantsPath))
        {
            return;
        }

        var deltaByVariantId = new Dictionary<int, DocTableVariantDelta>();
        foreach (string line in File.ReadLines(variantsPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            TableVariantDeltaOperationDto? operation = JsonSerializer.Deserialize(
                line,
                DocJsonCompactContext.Default.TableVariantDeltaOperationDto);
            if (operation == null || operation.VariantId <= DocTableVariant.BaseVariantId)
            {
                continue;
            }

            if (!deltaByVariantId.TryGetValue(operation.VariantId, out DocTableVariantDelta? delta))
            {
                delta = new DocTableVariantDelta
                {
                    VariantId = operation.VariantId,
                };
                deltaByVariantId[operation.VariantId] = delta;
            }

            if (string.Equals(operation.Op, "row_delete", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(operation.RowId))
                {
                    delta.DeletedBaseRowIds.Add(operation.RowId);
                }

                continue;
            }

            if (string.Equals(operation.Op, "row_add", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(operation.RowId))
                {
                    continue;
                }

                var addedRow = new DocRow
                {
                    Id = operation.RowId,
                };

                if (operation.Cells != null)
                {
                    foreach (var cellEntry in operation.Cells)
                    {
                        if (!columnLookup.TryGetValue(cellEntry.Key, out DocColumn? column))
                        {
                            continue;
                        }

                        addedRow.Cells[cellEntry.Key] = DeserializeCellValueFromStorage(column, cellEntry.Value);
                    }
                }

                EnsureRowHasAutoIdCells(table, addedRow);
                delta.AddedRows.Add(addedRow);
                continue;
            }

            if (string.Equals(operation.Op, "cell_set", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(operation.RowId) ||
                    string.IsNullOrWhiteSpace(operation.ColumnId) ||
                    !operation.Value.HasValue ||
                    !columnLookup.TryGetValue(operation.ColumnId, out DocColumn? column))
                {
                    continue;
                }

                delta.CellOverrides.Add(new DocTableCellOverride
                {
                    RowId = operation.RowId,
                    ColumnId = operation.ColumnId,
                    Value = DeserializeCellValueFromStorage(column, operation.Value.Value),
                });
            }
        }

        if (deltaByVariantId.Count <= 0)
        {
            return;
        }

        var variants = deltaByVariantId.Keys.ToList();
        variants.Sort();
        for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
        {
            table.VariantDeltas.Add(deltaByVariantId[variants[variantIndex]]);
        }
    }

    private static void EnsureRowHasAutoIdCells(DocTable table, DocRow row)
    {
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (column.Kind != DocColumnKind.Id)
            {
                continue;
            }

            if (!row.Cells.TryGetValue(column.Id, out DocCellValue existingCell) ||
                string.IsNullOrWhiteSpace(existingCell.StringValue))
            {
                row.Cells[column.Id] = DocCellValue.Text(row.Id);
            }
        }
    }

    private static void LoadDerivedVariantOwnData(
        DocTable table,
        string fileName,
        string tablesDir,
        Dictionary<string, DocColumn> columnLookup)
    {
        string[] paths = Directory.GetFiles(tablesDir, fileName + ".own-data@v*.jsonl");
        if (paths.Length <= 0)
        {
            return;
        }

        Array.Sort(paths, StringComparer.Ordinal);
        var deltaByVariantId = new Dictionary<int, DocTableVariantDelta>();
        for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            string path = paths[pathIndex];
            if (!TryParseVariantIdFromOwnDataPath(path, fileName, out int variantId) ||
                variantId <= DocTableVariant.BaseVariantId)
            {
                continue;
            }

            if (!deltaByVariantId.TryGetValue(variantId, out DocTableVariantDelta? delta))
            {
                delta = new DocTableVariantDelta
                {
                    VariantId = variantId,
                };
                deltaByVariantId[variantId] = delta;
            }

            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                OwnDataRowDto? dto = JsonSerializer.Deserialize(line, DocJsonCompactContext.Default.OwnDataRowDto);
                if (dto == null || string.IsNullOrWhiteSpace(dto.Id))
                {
                    continue;
                }

                foreach (var cellEntry in dto.Cells)
                {
                    if (!columnLookup.TryGetValue(cellEntry.Key, out DocColumn? column))
                    {
                        continue;
                    }

                    delta.CellOverrides.Add(new DocTableCellOverride
                    {
                        RowId = dto.Id,
                        ColumnId = cellEntry.Key,
                        Value = DeserializeCellValueFromStorage(column, cellEntry.Value),
                    });
                }
            }
        }

        if (deltaByVariantId.Count <= 0)
        {
            return;
        }

        var variantIds = deltaByVariantId.Keys.ToList();
        variantIds.Sort();
        for (int variantIndex = 0; variantIndex < variantIds.Count; variantIndex++)
        {
            table.VariantDeltas.Add(deltaByVariantId[variantIds[variantIndex]]);
        }
    }

    private static bool TryParseVariantIdFromOwnDataPath(string path, string fileName, out int variantId)
    {
        variantId = DocTableVariant.BaseVariantId;
        string leafName = Path.GetFileName(path);
        string prefix = fileName + ".own-data@v";
        if (!leafName.StartsWith(prefix, StringComparison.Ordinal) ||
            !leafName.EndsWith(".jsonl", StringComparison.Ordinal))
        {
            return false;
        }

        int start = prefix.Length;
        int end = leafName.Length - ".jsonl".Length;
        if (end <= start)
        {
            return false;
        }

        string idText = leafName.Substring(start, end - start);
        return int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out variantId);
    }

    private static void LoadViews(DocTable table, string fileName, string tablesDir)
    {
        var viewsPath = Path.Combine(tablesDir, $"{fileName}.views.json");
        if (!File.Exists(viewsPath)) return;

        var json = File.ReadAllText(viewsPath);
        var dto = JsonSerializer.Deserialize(json, DocJsonContext.Default.ViewsFileDto);
        if (dto == null) return;

        for (int i = 0; i < dto.Views.Count; i++)
        {
            var viewDto = dto.Views[i];
            var view = new DocView
            {
                Id = viewDto.Id,
                Name = viewDto.Name,
                Type = Enum.TryParse<DocViewType>(viewDto.Type, out var vt) ? vt : DocViewType.Grid,
                VisibleColumnIds = viewDto.VisibleColumnIds != null ? new List<string>(viewDto.VisibleColumnIds) : null,
                GroupByColumnId = viewDto.GroupByColumnId,
                GroupByColumnBinding = DeserializeViewBinding(viewDto.GroupByColumnBinding),
                CalendarDateColumnId = viewDto.CalendarDateColumnId,
                CalendarDateColumnBinding = DeserializeViewBinding(viewDto.CalendarDateColumnBinding),
                ChartKind = !string.IsNullOrEmpty(viewDto.ChartKind) && Enum.TryParse<DocChartKind>(viewDto.ChartKind, out var ck) ? ck : null,
                ChartKindBinding = DeserializeViewBinding(viewDto.ChartKindBinding),
                ChartCategoryColumnId = viewDto.ChartCategoryColumnId,
                ChartCategoryColumnBinding = DeserializeViewBinding(viewDto.ChartCategoryColumnBinding),
                ChartValueColumnId = viewDto.ChartValueColumnId,
                ChartValueColumnBinding = DeserializeViewBinding(viewDto.ChartValueColumnBinding),
                CustomRendererId = viewDto.CustomRendererId,
            };

            if (viewDto.Sorts != null)
            {
                for (int s = 0; s < viewDto.Sorts.Count; s++)
                {
                    view.Sorts.Add(new DocViewSort
                    {
                        Id = string.IsNullOrWhiteSpace(viewDto.Sorts[s].Id) ? Guid.NewGuid().ToString() : viewDto.Sorts[s].Id!,
                        ColumnId = viewDto.Sorts[s].ColumnId,
                        Descending = viewDto.Sorts[s].Descending,
                        ColumnIdBinding = DeserializeViewBinding(viewDto.Sorts[s].ColumnIdBinding),
                        DescendingBinding = DeserializeViewBinding(viewDto.Sorts[s].DescendingBinding),
                    });
                }
            }

            if (viewDto.Filters != null)
            {
                for (int f = 0; f < viewDto.Filters.Count; f++)
                {
                    view.Filters.Add(new DocViewFilter
                    {
                        Id = string.IsNullOrWhiteSpace(viewDto.Filters[f].Id) ? Guid.NewGuid().ToString() : viewDto.Filters[f].Id!,
                        ColumnId = viewDto.Filters[f].ColumnId,
                        Op = Enum.TryParse<DocViewFilterOp>(viewDto.Filters[f].Op, out var op) ? op : DocViewFilterOp.Equals,
                        Value = viewDto.Filters[f].Value ?? "",
                        ColumnIdBinding = DeserializeViewBinding(viewDto.Filters[f].ColumnIdBinding),
                        OpBinding = DeserializeViewBinding(viewDto.Filters[f].OpBinding),
                        ValueBinding = DeserializeViewBinding(viewDto.Filters[f].ValueBinding),
                    });
                }
            }

            table.Views.Add(view);
        }
    }

    private static DocViewBinding? DeserializeViewBinding(ViewBindingDto? bindingDto)
    {
        if (bindingDto == null)
        {
            return null;
        }

        var binding = new DocViewBinding
        {
            VariableName = bindingDto.VariableName ?? "",
            FormulaExpression = bindingDto.Formula ?? "",
        };

        return binding.IsEmpty ? null : binding;
    }

    private static DocDerivedConfig DeserializeDerivedConfig(DerivedConfigDto dto)
    {
        var config = new DocDerivedConfig
        {
            BaseTableId = dto.BaseTableId,
            FilterExpression = dto.FilterExpression ?? "",
        };
        for (int i = 0; i < dto.Steps.Count; i++)
        {
            var stepDto = dto.Steps[i];
            var step = new DerivedStep
            {
                Id = stepDto.Id ?? "",
                Kind = Enum.TryParse<DerivedStepKind>(stepDto.Kind, out var kind) ? kind : DerivedStepKind.Append,
                SourceTableId = stepDto.SourceTableId,
                JoinKind = stepDto.JoinKind != null && Enum.TryParse<DerivedJoinKind>(stepDto.JoinKind, out var jk)
                    ? jk : DerivedJoinKind.Left,
            };

            if (string.IsNullOrEmpty(step.Id))
            {
                // Back-compat: older configs had no step id. Prefer sourceTableId when unique.
                step.Id = step.SourceTableId;
            }
            if (step.Kind == DerivedStepKind.Append &&
                !string.IsNullOrEmpty(config.BaseTableId) &&
                string.Equals(step.Id, config.BaseTableId, StringComparison.Ordinal))
            {
                // Avoid row id collisions with base-table seeded rows when an older append step used SourceTableId as Id.
                step.Id = step.Id + "#append";
            }
            if (stepDto.KeyMappings != null)
            {
                for (int k = 0; k < stepDto.KeyMappings.Count; k++)
                {
                    step.KeyMappings.Add(new DerivedKeyMapping
                    {
                        BaseColumnId = stepDto.KeyMappings[k].BaseColumnId,
                        SourceColumnId = stepDto.KeyMappings[k].SourceColumnId,
                    });
                }
            }

            // Ensure append step ids are unique even if the same source is appended twice (mixed pipelines).
            // If duplicates exist, suffix with a stable counter based on declaration order.
            if (step.Kind == DerivedStepKind.Append)
            {
                int dupCount = 0;
                for (int j = 0; j < config.Steps.Count; j++)
                {
                    if (config.Steps[j].Kind == DerivedStepKind.Append &&
                        string.Equals(config.Steps[j].Id, step.Id, StringComparison.Ordinal))
                    {
                        dupCount++;
                    }
                }

                if (dupCount > 0)
                {
                    step.Id = step.Id + "#" + dupCount.ToString();
                }
            }

            config.Steps.Add(step);
        }
        for (int i = 0; i < dto.Projections.Count; i++)
        {
            var projDto = dto.Projections[i];
            config.Projections.Add(new DerivedProjection
            {
                SourceTableId = projDto.SourceTableId,
                SourceColumnId = projDto.SourceColumnId,
                OutputColumnId = projDto.OutputColumnId,
                RenameAlias = projDto.RenameAlias ?? "",
            });
        }

        if (dto.SuppressedProjections != null)
        {
            for (int i = 0; i < dto.SuppressedProjections.Count; i++)
            {
                var supDto = dto.SuppressedProjections[i];
                config.SuppressedProjections.Add(new DerivedProjectionSuppression
                {
                    SourceTableId = supDto.SourceTableId,
                    SourceColumnId = supDto.SourceColumnId,
                    OutputColumnId = supDto.OutputColumnId ?? "",
                });
            }
        }

        return config;
    }

    private static DocCellValue DeserializeFormulaCell(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => DocCellValue.Number(element.TryGetDouble(out var numberValue) ? numberValue : 0),
            JsonValueKind.True => DocCellValue.Text("true"),
            JsonValueKind.False => DocCellValue.Text("false"),
            _ => DocCellValue.Text(element.GetString() ?? "")
        };
    }

    private static DocCellValue DeserializeNumberCell(DocColumn column, JsonElement element)
    {
        if (!string.IsNullOrWhiteSpace(column.FormulaExpression) && element.ValueKind == JsonValueKind.String)
        {
            return DocCellValue.Text(element.GetString() ?? "");
        }

        return DocCellValue.Number(element.TryGetDouble(out var value) ? value : 0);
    }

    private static DocCellValue DeserializeCheckboxCell(DocColumn column, JsonElement element)
    {
        if (!string.IsNullOrWhiteSpace(column.FormulaExpression) && element.ValueKind == JsonValueKind.String)
        {
            return DocCellValue.Text(element.GetString() ?? "");
        }

        return DocCellValue.Bool(element.ValueKind == JsonValueKind.True);
    }

    private static DocCellValue DeserializeMeshAssetCell(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            AssetCellDto? assetCellDto = element.Deserialize(DocJsonContext.Default.AssetCellDto);
            var cellValue = DocCellValue.Text(assetCellDto?.Value ?? "");
            cellValue.ModelPreviewSettings = DeserializeModelPreview(assetCellDto?.ModelPreview);
            return cellValue;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return DocCellValue.Text(element.GetString() ?? "");
        }

        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return DocCellValue.Text("");
        }

        return DocCellValue.Text(element.ToString());
    }

    private static bool TryGetNumberProperty(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryReadVectorComponents(JsonElement element, int dimension, out double x, out double y, out double z, out double w)
    {
        x = 0;
        y = 0;
        z = 0;
        w = 0;

        if (element.ValueKind == JsonValueKind.Array)
        {
            int componentIndex = 0;
            foreach (JsonElement componentElement in element.EnumerateArray())
            {
                if (componentElement.ValueKind != JsonValueKind.Number || !componentElement.TryGetDouble(out double componentValue))
                {
                    continue;
                }

                switch (componentIndex)
                {
                    case 0:
                        x = componentValue;
                        break;
                    case 1:
                        y = componentValue;
                        break;
                    case 2:
                        z = componentValue;
                        break;
                    case 3:
                        w = componentValue;
                        break;
                }

                componentIndex++;
                if (componentIndex >= dimension)
                {
                    break;
                }
            }

            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        _ = TryGetNumberProperty(element, "x", out x);
        _ = TryGetNumberProperty(element, "y", out y);
        if (dimension >= 3)
        {
            _ = TryGetNumberProperty(element, "z", out z);
        }

        if (dimension >= 4)
        {
            _ = TryGetNumberProperty(element, "w", out w);
        }

        return true;
    }

    private static DocCellValue DeserializeVecCell(JsonElement element, int dimension)
    {
        if (TryReadVectorComponents(element, dimension, out double x, out double y, out double z, out double w))
        {
            return dimension switch
            {
                2 => DocCellValue.Vec2(x, y),
                3 => DocCellValue.Vec3(x, y, z),
                _ => DocCellValue.Vec4(x, y, z, w),
            };
        }

        return dimension switch
        {
            2 => DocCellValue.Vec2(0, 0),
            3 => DocCellValue.Vec3(0, 0, 0),
            _ => DocCellValue.Vec4(0, 0, 0, 0),
        };
    }

    private static DocCellValue DeserializeColorCell(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            double r = 0;
            double g = 0;
            double b = 0;
            double a = 1;
            if (!TryGetNumberProperty(element, "r", out r))
            {
                _ = TryGetNumberProperty(element, "x", out r);
            }

            if (!TryGetNumberProperty(element, "g", out g))
            {
                _ = TryGetNumberProperty(element, "y", out g);
            }

            if (!TryGetNumberProperty(element, "b", out b))
            {
                _ = TryGetNumberProperty(element, "z", out b);
            }

            if (!TryGetNumberProperty(element, "a", out a))
            {
                _ = TryGetNumberProperty(element, "w", out a);
            }

            return DocCellValue.Color(r, g, b, a);
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            if (TryReadVectorComponents(element, dimension: 4, out double r, out double g, out double b, out double a))
            {
                return DocCellValue.Color(r, g, b, a);
            }
        }

        return DocCellValue.Color(1, 1, 1, 1);
    }

    private static bool TryUnwrapFormulaOverrideCell(JsonElement element, out JsonElement payloadElement, out string? formulaExpression)
    {
        payloadElement = element;
        formulaExpression = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("f", out JsonElement formulaElement) ||
            !element.TryGetProperty("v", out JsonElement valueElement))
        {
            return false;
        }

        if (formulaElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        formulaExpression = formulaElement.GetString();
        payloadElement = valueElement;
        return true;
    }

    private static DocCellValue DeserializeCellValueFromStorage(DocColumn column, JsonElement element)
    {
        JsonElement payloadElement = element;
        string? cellFormulaExpression = null;
        _ = TryUnwrapFormulaOverrideCell(element, out payloadElement, out cellFormulaExpression);

        DocCellValue deserializedCellValue;
        string columnTypeId = DocColumnTypeIdMapper.Resolve(column.ColumnTypeId, column.Kind);
        if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
        {
            if (ColumnCellCodecProviderRegistry.TryDeserializeCell(columnTypeId, column, payloadElement, out var pluginValue))
            {
                deserializedCellValue = pluginValue;
                if (!string.IsNullOrWhiteSpace(cellFormulaExpression))
                {
                    deserializedCellValue.CellFormulaExpression = cellFormulaExpression;
                }

                return deserializedCellValue;
            }

            deserializedCellValue = DeserializeNonBuiltInCellFallback(payloadElement);
            if (!string.IsNullOrWhiteSpace(cellFormulaExpression))
            {
                deserializedCellValue.CellFormulaExpression = cellFormulaExpression;
            }

            return deserializedCellValue;
        }

        deserializedCellValue = column.Kind switch
        {
            DocColumnKind.Id => DocCellValue.Text(payloadElement.GetString() ?? ""),
            DocColumnKind.Number => DeserializeNumberCell(column, payloadElement),
            DocColumnKind.Checkbox => DeserializeCheckboxCell(column, payloadElement),
            DocColumnKind.Formula => DeserializeFormulaCell(payloadElement),
            DocColumnKind.TableRef => DocCellValue.Text(payloadElement.GetString() ?? ""),
            DocColumnKind.AudioAsset => DocCellValue.Text(payloadElement.GetString() ?? ""),
            DocColumnKind.UiAsset => DocCellValue.Text(payloadElement.GetString() ?? ""),
            DocColumnKind.MeshAsset => DeserializeMeshAssetCell(payloadElement),
            DocColumnKind.Vec2 => DeserializeVecCell(payloadElement, 2),
            DocColumnKind.Vec3 => DeserializeVecCell(payloadElement, 3),
            DocColumnKind.Vec4 => DeserializeVecCell(payloadElement, 4),
            DocColumnKind.Color => DeserializeColorCell(payloadElement),
            _ => DocCellValue.Text(payloadElement.GetString() ?? "")
        };

        if (!string.IsNullOrWhiteSpace(cellFormulaExpression))
        {
            deserializedCellValue.CellFormulaExpression = cellFormulaExpression;
        }

        return DocCellValueNormalizer.NormalizeForColumn(column, deserializedCellValue);
    }

    private static DocCellValue DeserializeNonBuiltInCellFallback(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => DocCellValue.Text(element.GetString() ?? ""),
            JsonValueKind.Number => DocCellValue.Text(element.TryGetDouble(out var numberValue)
                ? numberValue.ToString(CultureInfo.InvariantCulture)
                : "0"),
            JsonValueKind.True => DocCellValue.Text("true"),
            JsonValueKind.False => DocCellValue.Text("false"),
            JsonValueKind.Object => DocCellValue.Text(element.GetRawText()),
            JsonValueKind.Array => DocCellValue.Text(element.GetRawText()),
            JsonValueKind.Null => DocCellValue.Text(""),
            JsonValueKind.Undefined => DocCellValue.Text(""),
            _ => DocCellValue.Text(element.ToString()),
        };
    }

    private static bool TryFindFolder(DocProject project, string folderId, out DocFolder folder)
    {
        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            var candidateFolder = project.Folders[folderIndex];
            if (string.Equals(candidateFolder.Id, folderId, StringComparison.Ordinal))
            {
                folder = candidateFolder;
                return true;
            }
        }

        folder = null!;
        return false;
    }

    private static void ValidateFolderReferences(DocProject project)
    {
        if (project.Folders.Count == 0)
        {
            for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
            {
                project.Tables[tableIndex].FolderId = null;
            }

            for (int documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
            {
                project.Documents[documentIndex].FolderId = null;
            }

            project.UiState.TableFolderExpandedById.Clear();
            project.UiState.DocumentFolderExpandedById.Clear();

            return;
        }

        var folderIdSet = new HashSet<string>(StringComparer.Ordinal);
        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            folderIdSet.Add(project.Folders[folderIndex].Id);
        }

        for (int folderIndex = 0; folderIndex < project.Folders.Count; folderIndex++)
        {
            var folder = project.Folders[folderIndex];
            if (!string.IsNullOrWhiteSpace(folder.ParentFolderId) &&
                !folderIdSet.Contains(folder.ParentFolderId))
            {
                folder.ParentFolderId = null;
            }
        }

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            var table = project.Tables[tableIndex];
            if (string.IsNullOrWhiteSpace(table.FolderId))
            {
                table.FolderId = null;
                continue;
            }

            if (!folderIdSet.Contains(table.FolderId))
            {
                table.FolderId = null;
                continue;
            }

            var folder = project.Folders.Find(candidate => string.Equals(candidate.Id, table.FolderId, StringComparison.Ordinal));
            if (folder == null || folder.Scope != DocFolderScope.Tables)
            {
                table.FolderId = null;
            }
        }

        for (int documentIndex = 0; documentIndex < project.Documents.Count; documentIndex++)
        {
            var document = project.Documents[documentIndex];
            if (string.IsNullOrWhiteSpace(document.FolderId))
            {
                document.FolderId = null;
                continue;
            }

            if (!folderIdSet.Contains(document.FolderId))
            {
                document.FolderId = null;
                continue;
            }

            var folder = project.Folders.Find(candidate => string.Equals(candidate.Id, document.FolderId, StringComparison.Ordinal));
            if (folder == null || folder.Scope != DocFolderScope.Documents)
            {
                document.FolderId = null;
            }
        }

        PruneFolderExpansionState(project);
    }

    private static DocProjectUiState DeserializeProjectUiState(ProjectUiDto? uiDto)
    {
        var uiState = new DocProjectUiState();
        if (uiDto == null)
        {
            return uiState;
        }

        if (uiDto.TableFolderExpandedById != null)
        {
            foreach (var pair in uiDto.TableFolderExpandedById)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                uiState.TableFolderExpandedById[pair.Key] = pair.Value;
            }
        }

        if (uiDto.DocumentFolderExpandedById != null)
        {
            foreach (var pair in uiDto.DocumentFolderExpandedById)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                uiState.DocumentFolderExpandedById[pair.Key] = pair.Value;
            }
        }

        return uiState;
    }

    private static void PruneFolderExpansionState(DocProject project)
    {
        var uiState = project.UiState;
        PruneFolderExpansionStateForScope(project, uiState.TableFolderExpandedById, DocFolderScope.Tables);
        PruneFolderExpansionStateForScope(project, uiState.DocumentFolderExpandedById, DocFolderScope.Documents);
    }

    private static void PruneFolderExpansionStateForScope(
        DocProject project,
        Dictionary<string, bool> expandedById,
        DocFolderScope scope)
    {
        if (expandedById.Count == 0)
        {
            return;
        }

        List<string>? invalidFolderIds = null;
        foreach (var pair in expandedById)
        {
            if (!TryFindFolder(project, pair.Key, out var folder) || folder.Scope != scope)
            {
                invalidFolderIds ??= new List<string>();
                invalidFolderIds.Add(pair.Key);
            }
        }

        if (invalidFolderIds == null)
        {
            return;
        }

        for (int folderIdIndex = 0; folderIdIndex < invalidFolderIds.Count; folderIdIndex++)
        {
            expandedById.Remove(invalidFolderIds[folderIdIndex]);
        }
    }
}
