using System.Text.Json;
using System.Globalization;
using Derp.Doc.Model;
using Derp.Doc.Plugins;
using Derp.Doc.Tables;

namespace Derp.Doc.Storage;

/// <summary>
/// Saves a DocProject to the .derpdoc directory format:
///   project.json
///   tables/
///     {fileName}.schema.json
///     {fileName}.rows.jsonl
/// </summary>
internal static class ProjectSerializer
{
    public static void Save(DocProject project, string directoryPath)
    {
        SchemaLinkedTableSynchronizer.Synchronize(project);

        Directory.CreateDirectory(directoryPath);
        var tablesDir = Path.Combine(directoryPath, "tables");
        Directory.CreateDirectory(tablesDir);
        List<DocTable> uniqueTables = BuildDistinctTablesForPersistence(project);

        // Write project.json
        var projectDto = new ProjectDto
        {
            Name = project.Name,
            Folders = project.Folders.Count > 0
                ? project.Folders.Select(folder => new FolderDto
                {
                    Id = folder.Id,
                    Name = folder.Name,
                    Scope = folder.Scope.ToString(),
                    ParentFolderId = folder.ParentFolderId,
                }).ToList()
                : null,
            Tables = uniqueTables.Select(t => new TableRefDto
            {
                Id = t.Id,
                Name = t.Name,
                FileName = t.FileName,
                FolderId = t.FolderId,
            }).ToList(),
            Documents = project.Documents.Count > 0
                ? project.Documents.Select(d => new DocumentRefDto
                {
                    Id = d.Id,
                    Title = d.Title,
                    FileName = d.FileName,
                    FolderId = d.FolderId,
                }).ToList()
                : null,
            Ui = CreateProjectUiDto(project),
            PluginSettings = project.PluginSettingsByKey.Count > 0
                ? new Dictionary<string, string>(project.PluginSettingsByKey, StringComparer.Ordinal)
                : null,
        };

        var projectJson = JsonSerializer.Serialize(projectDto, DocJsonContext.Default.ProjectDto);
        File.WriteAllText(Path.Combine(directoryPath, "project.json"), projectJson);

        // Write each table
        for (int tableIndex = 0; tableIndex < uniqueTables.Count; tableIndex++)
        {
            SaveTable(uniqueTables[tableIndex], tablesDir);
        }

        // Write each document
        if (project.Documents.Count > 0)
        {
            var docsDir = Path.Combine(directoryPath, "docs");
            foreach (var document in project.Documents)
            {
                DocumentSerializer.Save(document, docsDir);
            }
        }
    }

    private static List<DocTable> BuildDistinctTablesForPersistence(DocProject project)
    {
        var distinctTables = new List<DocTable>(project.Tables.Count);
        var seenTableIds = new HashSet<string>(StringComparer.Ordinal);
        var seenFileNames = new HashSet<string>(StringComparer.Ordinal);

        for (int tableIndex = 0; tableIndex < project.Tables.Count; tableIndex++)
        {
            DocTable table = project.Tables[tableIndex];
            if (string.IsNullOrWhiteSpace(table.Id) || string.IsNullOrWhiteSpace(table.FileName))
            {
                continue;
            }

            if (!seenTableIds.Add(table.Id))
            {
                continue;
            }

            if (!seenFileNames.Add(table.FileName))
            {
                continue;
            }

            distinctTables.Add(table);
        }

        return distinctTables;
    }

    private static void SaveTable(DocTable table, string tablesDir)
    {
        // Write schema.json
        var schemaDto = new TableSchemaDto
        {
            Id = table.Id,
            Name = table.Name,
            Columns = table.Columns.Select(c => new ColumnDto
            {
                Id = c.Id,
                Name = c.Name,
                Kind = c.Kind.ToString(),
                TypeId = DocColumnTypeIdMapper.Resolve(c.ColumnTypeId, c.Kind),
                PluginSettings = string.IsNullOrWhiteSpace(c.PluginSettingsJson) ? null : c.PluginSettingsJson,
                Width = c.Width,
                Options = c.Kind == DocColumnKind.Select ? c.Options : null,
                Formula = string.IsNullOrWhiteSpace(c.FormulaExpression) ? null : c.FormulaExpression,
                RelationTableId = c.Kind == DocColumnKind.Relation ? c.RelationTableId : null,
                TableRefBaseTableId = c.Kind == DocColumnKind.TableRef ? c.TableRefBaseTableId : null,
                RowRefTableRefColumnId = string.IsNullOrWhiteSpace(c.RowRefTableRefColumnId) ? null : c.RowRefTableRefColumnId,
                RelationTargetMode = c.Kind == DocColumnKind.Relation &&
                                     c.RelationTargetMode != DocRelationTargetMode.ExternalTable
                    ? c.RelationTargetMode.ToString()
                    : null,
                RelationTableVariantId = c.Kind == DocColumnKind.Relation ? c.RelationTableVariantId : 0,
                RelationDisplayColumnId = c.Kind == DocColumnKind.Relation ? c.RelationDisplayColumnId : null,
                Hidden = c.IsHidden,
                Projected = c.IsProjected,
                Inherited = c.IsInherited,
                ExportType = string.IsNullOrWhiteSpace(c.ExportType) ? null : c.ExportType,
                NumberMin = c.Kind == DocColumnKind.Number ? c.NumberMin : null,
                NumberMax = c.Kind == DocColumnKind.Number ? c.NumberMax : null,
                ExportEnumName = string.IsNullOrWhiteSpace(c.ExportEnumName) ? null : c.ExportEnumName,
                ExportIgnore = c.ExportIgnore,
                SubtableId = c.Kind == DocColumnKind.Subtable ? c.SubtableId : null,
                SubtableDisplayRendererId = c.Kind == DocColumnKind.Subtable && !string.IsNullOrWhiteSpace(c.SubtableDisplayRendererId)
                    ? c.SubtableDisplayRendererId
                    : null,
                SubtableDisplayCellWidth = c.Kind == DocColumnKind.Subtable ? c.SubtableDisplayCellWidth : null,
                SubtableDisplayCellHeight = c.Kind == DocColumnKind.Subtable ? c.SubtableDisplayCellHeight : null,
                SubtableDisplayPreviewQuality = c.Kind == DocColumnKind.Subtable && c.SubtableDisplayPreviewQuality.HasValue
                    ? c.SubtableDisplayPreviewQuality.Value.ToString()
                    : null,
                FormulaEvalScopes = c.FormulaEvalScopes == DocFormulaEvalScope.None
                    ? null
                    : c.FormulaEvalScopes.ToString(),
                ModelPreview = SerializeModelPreview(c),
            }).ToList(),
            Variants = table.Variants.Count > 0
                ? table.Variants.Select(variant => new TableVariantDto
                {
                    Id = variant.Id,
                    Name = variant.Name,
                }).ToList()
                : null,
            Derived = table.IsDerived ? SerializeDerivedConfig(table.DerivedConfig!) : null,
            Export = table.ExportConfig != null ? new ExportConfigDto
            {
                Enabled = table.ExportConfig.Enabled,
                Namespace = string.IsNullOrWhiteSpace(table.ExportConfig.Namespace) ? null : table.ExportConfig.Namespace,
                StructName = string.IsNullOrWhiteSpace(table.ExportConfig.StructName) ? null : table.ExportConfig.StructName,
            } : null,
            ParentTableId = table.ParentTableId,
            ParentRowColumnId = table.ParentRowColumnId,
            PluginTableTypeId = string.IsNullOrWhiteSpace(table.PluginTableTypeId) ? null : table.PluginTableTypeId,
            PluginOwnerColumnTypeId = string.IsNullOrWhiteSpace(table.PluginOwnerColumnTypeId) ? null : table.PluginOwnerColumnTypeId,
            PluginSchemaLocked = table.IsPluginSchemaLocked,
            Variables = table.Variables.Count > 0
                ? table.Variables.Select(variable => new TableVariableDto
                {
                    Id = variable.Id,
                    Name = variable.Name,
                    Kind = variable.Kind.ToString(),
                    TypeId = DocColumnTypeIdMapper.IsBuiltIn(variable.ColumnTypeId)
                        ? null
                        : variable.ColumnTypeId,
                    Expression = string.IsNullOrWhiteSpace(variable.Expression) ? null : variable.Expression,
                }).ToList()
                : null,
            SchemaSourceTableId = string.IsNullOrWhiteSpace(table.SchemaSourceTableId) ? null : table.SchemaSourceTableId,
            InheritanceSourceTableId = string.IsNullOrWhiteSpace(table.InheritanceSourceTableId) ? null : table.InheritanceSourceTableId,
            SystemKey = string.IsNullOrWhiteSpace(table.SystemKey) ? null : table.SystemKey,
            SystemSchemaLocked = table.IsSystemSchemaLocked,
            SystemDataLocked = table.IsSystemDataLocked,
            Keys = HasKeys(table.Keys) ? new TableKeysDto
            {
                PrimaryKeyColumnId = string.IsNullOrWhiteSpace(table.Keys.PrimaryKeyColumnId) ? null : table.Keys.PrimaryKeyColumnId,
                SecondaryKeys = table.Keys.SecondaryKeys.Count > 0 ? table.Keys.SecondaryKeys.Select(sk => new SecondaryKeyDto
                {
                    ColumnId = sk.ColumnId,
                    Unique = sk.Unique,
                }).ToList() : null,
            } : null,
        };

        var schemaJson = JsonSerializer.Serialize(schemaDto, DocJsonContext.Default.TableSchemaDto);
        File.WriteAllText(Path.Combine(tablesDir, $"{table.FileName}.schema.json"), schemaJson);

        if (table.IsDerived)
        {
            // Derived tables: don't write rows.jsonl (rows are computed).
            // Write own-data.jsonl for local (non-projected) cell data only.
            SaveOwnData(table, tablesDir);
        }
        else
        {
            // Write rows.jsonl (one JSON object per line)
            SaveRows(table, tablesDir);
        }

        // Write views.json (Phase 6)
        SaveViews(table, tablesDir);
        SaveVariantData(table, tablesDir);
    }

    private static ModelPreviewDto? SerializeModelPreview(DocColumn column)
    {
        if (column.Kind != DocColumnKind.MeshAsset || column.ModelPreviewSettings == null)
        {
            return null;
        }

        return SerializeModelPreviewSettings(column.ModelPreviewSettings);
    }

    private static void SaveRows(DocTable table, string tablesDir)
    {
        var rowsPath = Path.Combine(tablesDir, $"{table.FileName}.rows.jsonl");
        using var writer = new StreamWriter(rowsPath);

        foreach (var row in table.Rows)
        {
            var rowDto = new RowDto { Id = row.Id };

            foreach (var (columnId, cellValue) in row.Cells)
            {
                var column = table.Columns.Find(c => c.Id == columnId);
                if (column == null) continue;

                DocCellValue cellValueToPersist = cellValue;
                if (column.Kind == DocColumnKind.Id &&
                    string.IsNullOrWhiteSpace(cellValueToPersist.StringValue))
                {
                    cellValueToPersist = DocCellValue.Text(row.Id);
                }

                JsonElement element = SerializeCellValueForStorage(column, cellValueToPersist);

                rowDto.Cells[columnId] = element;
            }

            var line = JsonSerializer.Serialize(rowDto, DocJsonCompactContext.Default.RowDto);
            writer.WriteLine(line);
        }
    }

    private static void SaveOwnData(DocTable table, string tablesDir)
    {
        var ownDataPath = Path.Combine(tablesDir, $"{table.FileName}.own-data.jsonl");

        // Collect local column IDs (non-projected)
        var localColumnIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < table.Columns.Count; i++)
        {
            if (!table.Columns[i].IsProjected)
                localColumnIds.Add(table.Columns[i].Id);
        }

        if (localColumnIds.Count == 0)
        {
            // No local data to persist — delete stale file if any
            if (File.Exists(ownDataPath)) File.Delete(ownDataPath);
            return;
        }

        using var writer = new StreamWriter(ownDataPath);
        foreach (var row in table.Rows)
        {
            var dto = new OwnDataRowDto { Id = row.Id };
            foreach (var (columnId, cellValue) in row.Cells)
            {
                if (!localColumnIds.Contains(columnId)) continue;
                var column = table.Columns.Find(c => c.Id == columnId);
                if (column == null) continue;

                DocCellValue cellValueToPersist = cellValue;
                if (column.Kind == DocColumnKind.Id &&
                    string.IsNullOrWhiteSpace(cellValueToPersist.StringValue))
                {
                    cellValueToPersist = DocCellValue.Text(row.Id);
                }

                JsonElement element = SerializeCellValueForStorage(column, cellValueToPersist);
                dto.Cells[columnId] = element;
            }
            if (dto.Cells.Count > 0)
            {
                var line = JsonSerializer.Serialize(dto, DocJsonCompactContext.Default.OwnDataRowDto);
                writer.WriteLine(line);
            }
        }
    }

    private static void SaveVariantData(DocTable table, string tablesDir)
    {
        if (table.IsDerived)
        {
            SaveDerivedVariantOwnData(table, tablesDir);
            string variantOpsPath = Path.Combine(tablesDir, $"{table.FileName}.variants.jsonl");
            if (File.Exists(variantOpsPath))
            {
                File.Delete(variantOpsPath);
            }

            return;
        }

        SaveTableVariantOperations(table, tablesDir);
        DeleteDerivedVariantOwnDataFiles(table, tablesDir);
    }

    private static void SaveTableVariantOperations(DocTable table, string tablesDir)
    {
        string variantPath = Path.Combine(tablesDir, $"{table.FileName}.variants.jsonl");
        var columnById = new Dictionary<string, DocColumn>(table.Columns.Count, StringComparer.Ordinal);
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            columnById[table.Columns[columnIndex].Id] = table.Columns[columnIndex];
        }

        var deltas = new List<DocTableVariantDelta>(table.VariantDeltas.Count);
        for (int deltaIndex = 0; deltaIndex < table.VariantDeltas.Count; deltaIndex++)
        {
            DocTableVariantDelta delta = table.VariantDeltas[deltaIndex];
            if (delta.VariantId <= DocTableVariant.BaseVariantId)
            {
                continue;
            }

            bool hasOperations = delta.DeletedBaseRowIds.Count > 0 ||
                                 delta.AddedRows.Count > 0 ||
                                 delta.CellOverrides.Count > 0;
            if (!hasOperations)
            {
                continue;
            }

            deltas.Add(delta);
        }

        if (deltas.Count <= 0)
        {
            if (File.Exists(variantPath))
            {
                File.Delete(variantPath);
            }

            return;
        }

        deltas.Sort(static (leftDelta, rightDelta) => leftDelta.VariantId.CompareTo(rightDelta.VariantId));

        using var writer = new StreamWriter(variantPath);
        for (int deltaIndex = 0; deltaIndex < deltas.Count; deltaIndex++)
        {
            DocTableVariantDelta delta = deltas[deltaIndex];

            for (int rowIndex = 0; rowIndex < delta.DeletedBaseRowIds.Count; rowIndex++)
            {
                var op = new TableVariantDeltaOperationDto
                {
                    VariantId = delta.VariantId,
                    Op = "row_delete",
                    RowId = delta.DeletedBaseRowIds[rowIndex],
                };

                writer.WriteLine(JsonSerializer.Serialize(op, DocJsonCompactContext.Default.TableVariantDeltaOperationDto));
            }

            for (int rowIndex = 0; rowIndex < delta.AddedRows.Count; rowIndex++)
            {
                DocRow addedRow = delta.AddedRows[rowIndex];
                var cells = new Dictionary<string, JsonElement>(addedRow.Cells.Count, StringComparer.Ordinal);
                foreach (var cellEntry in addedRow.Cells)
                {
                    if (!columnById.TryGetValue(cellEntry.Key, out DocColumn? column))
                    {
                        continue;
                    }

                    cells[cellEntry.Key] = SerializeCellValueForStorage(column, cellEntry.Value);
                }

                var op = new TableVariantDeltaOperationDto
                {
                    VariantId = delta.VariantId,
                    Op = "row_add",
                    RowId = addedRow.Id,
                    Cells = cells,
                };

                writer.WriteLine(JsonSerializer.Serialize(op, DocJsonCompactContext.Default.TableVariantDeltaOperationDto));
            }

            for (int overrideIndex = 0; overrideIndex < delta.CellOverrides.Count; overrideIndex++)
            {
                DocTableCellOverride cellOverride = delta.CellOverrides[overrideIndex];
                if (!columnById.TryGetValue(cellOverride.ColumnId, out DocColumn? column))
                {
                    continue;
                }

                JsonElement value = SerializeCellValueForStorage(column, cellOverride.Value);
                var op = new TableVariantDeltaOperationDto
                {
                    VariantId = delta.VariantId,
                    Op = "cell_set",
                    RowId = cellOverride.RowId,
                    ColumnId = cellOverride.ColumnId,
                    Value = value,
                };

                writer.WriteLine(JsonSerializer.Serialize(op, DocJsonCompactContext.Default.TableVariantDeltaOperationDto));
            }
        }
    }

    private static void SaveDerivedVariantOwnData(DocTable table, string tablesDir)
    {
        DeleteDerivedVariantOwnDataFiles(table, tablesDir);

        var localColumnIds = new HashSet<string>(StringComparer.Ordinal);
        var localColumnById = new Dictionary<string, DocColumn>(StringComparer.Ordinal);
        for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            DocColumn column = table.Columns[columnIndex];
            if (column.IsProjected)
            {
                continue;
            }

            localColumnIds.Add(column.Id);
            localColumnById[column.Id] = column;
        }

        if (localColumnIds.Count <= 0)
        {
            return;
        }

        for (int deltaIndex = 0; deltaIndex < table.VariantDeltas.Count; deltaIndex++)
        {
            DocTableVariantDelta delta = table.VariantDeltas[deltaIndex];
            if (delta.VariantId <= DocTableVariant.BaseVariantId || delta.CellOverrides.Count <= 0)
            {
                continue;
            }

            var cellsByRowId = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
            for (int overrideIndex = 0; overrideIndex < delta.CellOverrides.Count; overrideIndex++)
            {
                DocTableCellOverride cellOverride = delta.CellOverrides[overrideIndex];
                if (!localColumnIds.Contains(cellOverride.ColumnId) ||
                    !localColumnById.TryGetValue(cellOverride.ColumnId, out DocColumn? column))
                {
                    continue;
                }

                if (!cellsByRowId.TryGetValue(cellOverride.RowId, out Dictionary<string, JsonElement>? rowCells))
                {
                    rowCells = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    cellsByRowId[cellOverride.RowId] = rowCells;
                }

                rowCells[cellOverride.ColumnId] = SerializeCellValueForStorage(column, cellOverride.Value);
            }

            if (cellsByRowId.Count <= 0)
            {
                continue;
            }

            string ownDataVariantPath = Path.Combine(
                tablesDir,
                table.FileName + ".own-data@v" + delta.VariantId.ToString(CultureInfo.InvariantCulture) + ".jsonl");
            using var writer = new StreamWriter(ownDataVariantPath);
            foreach (var rowEntry in cellsByRowId)
            {
                var rowDto = new OwnDataRowDto
                {
                    Id = rowEntry.Key,
                    Cells = rowEntry.Value,
                };

                writer.WriteLine(JsonSerializer.Serialize(rowDto, DocJsonCompactContext.Default.OwnDataRowDto));
            }
        }
    }

    private static void DeleteDerivedVariantOwnDataFiles(DocTable table, string tablesDir)
    {
        string searchPattern = table.FileName + ".own-data@v*.jsonl";
        string[] paths = Directory.GetFiles(tablesDir, searchPattern);
        for (int pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            File.Delete(paths[pathIndex]);
        }
    }

    private static void SaveViews(DocTable table, string tablesDir)
    {
        var viewsPath = Path.Combine(tablesDir, $"{table.FileName}.views.json");

        if (table.Views.Count == 0)
        {
            if (File.Exists(viewsPath)) File.Delete(viewsPath);
            return;
        }

        var dto = new ViewsFileDto();
        for (int i = 0; i < table.Views.Count; i++)
        {
            var view = table.Views[i];
            var viewDto = new ViewDto
            {
                Id = view.Id,
                Name = view.Name,
                Type = view.Type.ToString(),
                VisibleColumnIds = view.VisibleColumnIds != null ? new List<string>(view.VisibleColumnIds) : null,
                GroupByColumnId = view.GroupByColumnId,
                GroupByColumnBinding = SerializeViewBinding(view.GroupByColumnBinding),
                CalendarDateColumnId = view.CalendarDateColumnId,
                CalendarDateColumnBinding = SerializeViewBinding(view.CalendarDateColumnBinding),
                ChartKind = view.ChartKind?.ToString(),
                ChartKindBinding = SerializeViewBinding(view.ChartKindBinding),
                ChartCategoryColumnId = view.ChartCategoryColumnId,
                ChartCategoryColumnBinding = SerializeViewBinding(view.ChartCategoryColumnBinding),
                ChartValueColumnId = view.ChartValueColumnId,
                ChartValueColumnBinding = SerializeViewBinding(view.ChartValueColumnBinding),
                CustomRendererId = view.CustomRendererId,
            };

            if (view.Sorts.Count > 0)
            {
                viewDto.Sorts = new List<ViewSortDto>(view.Sorts.Count);
                for (int s = 0; s < view.Sorts.Count; s++)
                {
                    viewDto.Sorts.Add(new ViewSortDto
                    {
                        Id = view.Sorts[s].Id,
                        ColumnId = view.Sorts[s].ColumnId,
                        Descending = view.Sorts[s].Descending,
                        ColumnIdBinding = SerializeViewBinding(view.Sorts[s].ColumnIdBinding),
                        DescendingBinding = SerializeViewBinding(view.Sorts[s].DescendingBinding),
                    });
                }
            }

            if (view.Filters.Count > 0)
            {
                viewDto.Filters = new List<ViewFilterDto>(view.Filters.Count);
                for (int f = 0; f < view.Filters.Count; f++)
                {
                    viewDto.Filters.Add(new ViewFilterDto
                    {
                        Id = view.Filters[f].Id,
                        ColumnId = view.Filters[f].ColumnId,
                        Op = view.Filters[f].Op.ToString(),
                        Value = string.IsNullOrEmpty(view.Filters[f].Value) ? null : view.Filters[f].Value,
                        ColumnIdBinding = SerializeViewBinding(view.Filters[f].ColumnIdBinding),
                        OpBinding = SerializeViewBinding(view.Filters[f].OpBinding),
                        ValueBinding = SerializeViewBinding(view.Filters[f].ValueBinding),
                    });
                }
            }

            dto.Views.Add(viewDto);
        }

        var json = JsonSerializer.Serialize(dto, DocJsonContext.Default.ViewsFileDto);
        File.WriteAllText(viewsPath, json);
    }

    private static ViewBindingDto? SerializeViewBinding(DocViewBinding? binding)
    {
        if (binding == null || binding.IsEmpty)
        {
            return null;
        }

        return new ViewBindingDto
        {
            VariableName = string.IsNullOrWhiteSpace(binding.VariableName) ? null : binding.VariableName,
            Formula = string.IsNullOrWhiteSpace(binding.FormulaExpression) ? null : binding.FormulaExpression,
        };
    }

    private static DerivedConfigDto SerializeDerivedConfig(DocDerivedConfig config)
    {
        var dto = new DerivedConfigDto
        {
            BaseTableId = config.BaseTableId,
            FilterExpression = string.IsNullOrWhiteSpace(config.FilterExpression) ? null : config.FilterExpression,
        };
        for (int i = 0; i < config.Steps.Count; i++)
        {
            var step = config.Steps[i];
            var stepDto = new DerivedStepDto
            {
                Id = string.IsNullOrEmpty(step.Id) ? null : step.Id,
                Kind = step.Kind.ToString(),
                SourceTableId = step.SourceTableId,
                JoinKind = step.Kind == DerivedStepKind.Join ? step.JoinKind.ToString() : null,
            };
            if (step.KeyMappings.Count > 0)
            {
                stepDto.KeyMappings = new List<DerivedKeyMappingDto>(step.KeyMappings.Count);
                for (int k = 0; k < step.KeyMappings.Count; k++)
                {
                    stepDto.KeyMappings.Add(new DerivedKeyMappingDto
                    {
                        BaseColumnId = step.KeyMappings[k].BaseColumnId,
                        SourceColumnId = step.KeyMappings[k].SourceColumnId,
                    });
                }
            }
            dto.Steps.Add(stepDto);
        }
        for (int i = 0; i < config.Projections.Count; i++)
        {
            var proj = config.Projections[i];
            dto.Projections.Add(new DerivedProjectionDto
            {
                SourceTableId = proj.SourceTableId,
                SourceColumnId = proj.SourceColumnId,
                OutputColumnId = proj.OutputColumnId,
                RenameAlias = string.IsNullOrEmpty(proj.RenameAlias) ? null : proj.RenameAlias,
            });
        }

        if (config.SuppressedProjections.Count > 0)
        {
            dto.SuppressedProjections = new List<DerivedSuppressedProjectionDto>(config.SuppressedProjections.Count);
            for (int i = 0; i < config.SuppressedProjections.Count; i++)
            {
                var sup = config.SuppressedProjections[i];
                dto.SuppressedProjections.Add(new DerivedSuppressedProjectionDto
                {
                    SourceTableId = sup.SourceTableId,
                    SourceColumnId = sup.SourceColumnId,
                    OutputColumnId = string.IsNullOrEmpty(sup.OutputColumnId) ? null : sup.OutputColumnId,
                });
            }
        }

        return dto;
    }

    private static bool HasKeys(DocTableKeys keys)
    {
        if (!string.IsNullOrWhiteSpace(keys.PrimaryKeyColumnId))
        {
            return true;
        }

        if (keys.SecondaryKeys.Count > 0)
        {
            return true;
        }

        return false;
    }

    private static ModelPreviewDto? SerializeModelPreviewSettings(DocModelPreviewSettings? modelPreviewSettings)
    {
        if (modelPreviewSettings == null)
        {
            return null;
        }

        var settings = modelPreviewSettings.Clone();
        settings.ClampInPlace();
        if (DocModelPreviewSettings.IsDefault(settings))
        {
            return null;
        }

        return new ModelPreviewDto
        {
            OrbitYawDegrees = settings.OrbitYawDegrees,
            OrbitPitchDegrees = settings.OrbitPitchDegrees,
            PanX = settings.PanX,
            PanY = settings.PanY,
            Zoom = settings.Zoom,
            TextureRelativePath = settings.TextureRelativePath,
        };
    }

    private static JsonElement SerializeMeshAssetCell(DocCellValue cellValue)
    {
        ModelPreviewDto? modelPreview = SerializeModelPreviewSettings(cellValue.ModelPreviewSettings);
        if (modelPreview == null)
        {
            return JsonSerializer.SerializeToElement(cellValue.StringValue ?? "");
        }

        var assetCell = new AssetCellDto
        {
            Value = cellValue.StringValue ?? "",
            ModelPreview = modelPreview,
        };
        return JsonSerializer.SerializeToElement(assetCell, DocJsonContext.Default.AssetCellDto);
    }

    private static ProjectUiDto? CreateProjectUiDto(DocProject project)
    {
        Dictionary<string, bool>? tableFolderExpanded = BuildSerializedFolderExpansionMap(
            project,
            project.UiState.TableFolderExpandedById,
            DocFolderScope.Tables);
        Dictionary<string, bool>? documentFolderExpanded = BuildSerializedFolderExpansionMap(
            project,
            project.UiState.DocumentFolderExpandedById,
            DocFolderScope.Documents);
        if (tableFolderExpanded == null && documentFolderExpanded == null)
        {
            return null;
        }

        return new ProjectUiDto
        {
            TableFolderExpandedById = tableFolderExpanded,
            DocumentFolderExpandedById = documentFolderExpanded,
        };
    }

    private static Dictionary<string, bool>? BuildSerializedFolderExpansionMap(
        DocProject project,
        Dictionary<string, bool> expansionMap,
        DocFolderScope scope)
    {
        if (expansionMap.Count == 0)
        {
            return null;
        }

        Dictionary<string, bool>? result = null;
        foreach (var pair in expansionMap)
        {
            if (!TryFindFolder(project, pair.Key, out var folder) || folder.Scope != scope)
            {
                continue;
            }

            result ??= new Dictionary<string, bool>(StringComparer.Ordinal);
            result[pair.Key] = pair.Value;
        }

        return result;
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

    private static JsonElement SerializeFormulaCell(DocCellValue cellValue)
    {
        if (!string.IsNullOrEmpty(cellValue.StringValue))
        {
            return JsonSerializer.SerializeToElement(cellValue.StringValue);
        }

        return JsonSerializer.SerializeToElement(cellValue.NumberValue);
    }

    private static JsonElement SerializeNumberCell(DocColumn column, DocCellValue cellValue)
    {
        if (!string.IsNullOrWhiteSpace(column.FormulaExpression) && !string.IsNullOrEmpty(cellValue.StringValue))
        {
            return JsonSerializer.SerializeToElement(cellValue.StringValue);
        }

        return JsonSerializer.SerializeToElement(cellValue.NumberValue);
    }

    private static JsonElement SerializeCheckboxCell(DocColumn column, DocCellValue cellValue)
    {
        if (!string.IsNullOrWhiteSpace(column.FormulaExpression) && !string.IsNullOrEmpty(cellValue.StringValue))
        {
            return JsonSerializer.SerializeToElement(cellValue.StringValue);
        }

        return JsonSerializer.SerializeToElement(cellValue.BoolValue);
    }

    private static JsonElement SerializeCellValueForStorage(DocColumn column, DocCellValue cellValue)
    {
        JsonElement valuePayload = SerializeCellValuePayloadForStorage(column, cellValue);
        if (!string.IsNullOrWhiteSpace(cellValue.CellFormulaExpression))
        {
            var formulaOverrideCell = new FormulaOverrideCellDto
            {
                Formula = cellValue.CellFormulaExpression!,
                Value = valuePayload,
            };
            return JsonSerializer.SerializeToElement(formulaOverrideCell, DocJsonContext.Default.FormulaOverrideCellDto);
        }

        return valuePayload;
    }

    private static JsonElement SerializeCellValuePayloadForStorage(DocColumn column, DocCellValue cellValue)
    {
        string columnTypeId = DocColumnTypeIdMapper.Resolve(column.ColumnTypeId, column.Kind);
        if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId))
        {
            if (ColumnCellCodecProviderRegistry.TrySerializeCell(columnTypeId, column, cellValue, out var pluginValue))
            {
                return pluginValue;
            }

            return SerializeNonBuiltInCellFallback(cellValue);
        }

        return column.Kind switch
        {
            DocColumnKind.Id => JsonSerializer.SerializeToElement(cellValue.StringValue ?? ""),
            DocColumnKind.Number => SerializeNumberCell(column, cellValue),
            DocColumnKind.Checkbox => SerializeCheckboxCell(column, cellValue),
            DocColumnKind.Formula => SerializeFormulaCell(cellValue),
            DocColumnKind.TableRef => JsonSerializer.SerializeToElement(cellValue.StringValue ?? ""),
            DocColumnKind.AudioAsset => JsonSerializer.SerializeToElement(cellValue.StringValue ?? ""),
            DocColumnKind.UiAsset => JsonSerializer.SerializeToElement(cellValue.StringValue ?? ""),
            DocColumnKind.MeshAsset => SerializeMeshAssetCell(cellValue),
            DocColumnKind.Vec2 => JsonSerializer.SerializeToElement(
                new Vec2CellDto
                {
                    X = cellValue.XValue,
                    Y = cellValue.YValue,
                },
                DocJsonContext.Default.Vec2CellDto),
            DocColumnKind.Vec3 => JsonSerializer.SerializeToElement(
                new Vec3CellDto
                {
                    X = cellValue.XValue,
                    Y = cellValue.YValue,
                    Z = cellValue.ZValue,
                },
                DocJsonContext.Default.Vec3CellDto),
            DocColumnKind.Vec4 => JsonSerializer.SerializeToElement(
                new Vec4CellDto
                {
                    X = cellValue.XValue,
                    Y = cellValue.YValue,
                    Z = cellValue.ZValue,
                    W = cellValue.WValue,
                },
                DocJsonContext.Default.Vec4CellDto),
            DocColumnKind.Color => JsonSerializer.SerializeToElement(
                new ColorCellDto
                {
                    R = cellValue.XValue,
                    G = cellValue.YValue,
                    B = cellValue.ZValue,
                    A = cellValue.WValue,
                },
                DocJsonContext.Default.ColorCellDto),
            _ => JsonSerializer.SerializeToElement(cellValue.StringValue ?? "")
        };
    }

    private static JsonElement SerializeNonBuiltInCellFallback(DocCellValue cellValue)
    {
        if (!string.IsNullOrEmpty(cellValue.StringValue))
        {
            return JsonSerializer.SerializeToElement(cellValue.StringValue);
        }

        if (Math.Abs(cellValue.NumberValue) > double.Epsilon)
        {
            return JsonSerializer.SerializeToElement(cellValue.NumberValue);
        }

        if (cellValue.BoolValue)
        {
            return JsonSerializer.SerializeToElement(true);
        }

        return JsonSerializer.SerializeToElement("");
    }
}
