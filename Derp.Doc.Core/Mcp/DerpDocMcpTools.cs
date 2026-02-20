namespace Derp.Doc.Mcp;

public static class DerpDocMcpTools
{
    public static IReadOnlyList<DerpDocMcpToolDefinition> All { get; } =
    [
        new DerpDocMcpToolDefinition(
            "derpdoc.project.create",
            "Create Derp.Doc Project",
            "Create a new Derp.Doc project directory (project.json + tables/ + docs/).",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "path": { "type":"string", "description":"DB root directory to create (or any path inside it)." },
              "name": { "type":"string", "description":"Project display name." }
            }, "required": ["path"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "dbRoot": { "type":"string" }
            }, "required": ["dbRoot"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.project.open",
            "Open Derp.Doc Project",
            "Open a Derp.Doc DB root (contains project.json) or a game root (contains derpgame). Sets it as active.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "path": { "type":"string", "description":"Game root (derpgame) or DB root (project.json)." },
              "createIfMissing": { "type":"boolean", "description":"If true, create a DB root scaffold when opening a game root with no Database/project.json yet." }
            }, "required": ["path"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "dbRoot": { "type":"string" },
              "projectName": { "type":"string" }
            }, "required": ["dbRoot","projectName"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.project.get",
            "Get Active Project",
            "Get the active project and basic metadata.",
            """{ "type":"object", "additionalProperties": false }""",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "dbRoot": { "type":"string" },
              "projectName": { "type":"string" },
              "tableCount": { "type":"integer" }
            }, "required": ["dbRoot","projectName","tableCount"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.project.legacy.variants.cleanup",
            "Cleanup Legacy Variants",
            "Migrate legacy project-level variants into per-table schema variants and strip the project.json variants field.",
            """{ "type":"object", "additionalProperties": false }""",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "dbRoot": { "type":"string" },
              "hadLegacyProjectVariants": { "type":"boolean" },
              "legacyProjectVariantCount": { "type":"integer" },
              "remainingLegacyProjectVariantCount": { "type":"integer" },
              "cleanedProjectJson": { "type":"boolean" },
              "saved": { "type":"boolean" }
            }, "required": ["dbRoot","hadLegacyProjectVariants","legacyProjectVariantCount","remainingLegacyProjectVariantCount","cleanedProjectJson","saved"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.variant.list",
            "List Variants",
            "List table variants (id=0 is Base).",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" }
            }, "required": ["tableId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variants": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "id": { "type":"integer" },
                "name": { "type":"string" }
              }, "required": ["id","name"] } }
            }, "required": ["tableId","variants"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.variant.set",
            "Create Or Rename Variant",
            "Create a table variant (id>0) or rename an existing table variant.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variantId": { "type":"integer", "description":"Variant id. Must be > 0." },
              "name": { "type":"string" }
            }, "required": ["tableId","variantId","name"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variantId": { "type":"integer" },
              "name": { "type":"string" },
              "created": { "type":"boolean" },
              "updated": { "type":"boolean" }
            }, "required": ["tableId","variantId","name","created","updated"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.variant.delete",
            "Delete Variant",
            "Delete a table variant (id>0) and remove its table deltas. Base (id=0) cannot be deleted.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variantId": { "type":"integer" }
            }, "required": ["tableId","variantId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variantId": { "type":"integer" },
              "deleted": { "type":"boolean" },
              "tablesUpdated": { "type":"integer" },
              "blocksResetToBase": { "type":"integer" }
            }, "required": ["tableId","variantId","deleted","tablesUpdated","blocksResetToBase"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.folder.list",
            "List Folders",
            "List folders in the active project. Optionally filter by scope.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "scope": { "type":"string", "description":"Optional scope filter: Tables|Documents" }
            } }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "folders": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "id": { "type":"string" },
                "name": { "type":"string" },
                "scope": { "type":"string" },
                "parentFolderId": { "type":"string" }
              }, "required": ["id","name","scope"] } }
            }, "required": ["folders"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.folder.create",
            "Create Folder",
            "Create a folder in Tables or Documents scope. Optionally nest under a parent folder in the same scope.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "name": { "type":"string" },
              "scope": { "type":"string", "description":"Tables|Documents" },
              "parentFolderId": { "type":"string", "description":"Optional parent folder id in same scope." }
            }, "required": ["name","scope"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "folderId": { "type":"string" }
            }, "required": ["folderId"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.table.list",
            "List Tables",
            "List all tables in the active project.",
            """{ "type":"object", "additionalProperties": false }""",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tables": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "id": { "type":"string" },
                "name": { "type":"string" },
                "fileName": { "type":"string" },
                "folderId": { "type":"string" },
                "isDerived": { "type":"boolean" },
                "isSchemaLinked": { "type":"boolean" },
                "schemaSourceTableId": { "type":"string" },
                "isInherited": { "type":"boolean" },
                "inheritanceSourceTableId": { "type":"string" }
              }, "required": ["id","name","fileName","isDerived","isSchemaLinked"] } }
            }, "required": ["tables"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.table.create",
            "Create Table",
            "Create a new table in the active project.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "name": { "type":"string" },
              "fileName": { "type":"string", "description":"Optional file name stem (e.g. 'units')." },
              "folderId": { "type":"string", "description":"Optional parent folder id (Tables scope)." },
              "schemaSourceTableId": { "type":"string", "description":"Optional source table id for schema-linked tables. Columns become source-controlled." },
              "inheritanceSourceTableId": { "type":"string", "description":"Optional base table id for inherited tables. Base columns are locked and local columns can be added." }
            }, "required": ["name"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" }
            }, "required": ["tableId"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.table.delete",
            "Delete Table",
            "Delete a table from the active project.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" }
            }, "required": ["tableId"] }
            """,
            """{ "type":"object", "additionalProperties": false }"""),
        new DerpDocMcpToolDefinition(
            "derpdoc.table.folder.set",
            "Set Table Folder",
            "Move a table into a Tables folder. Pass empty folderId to move to root.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "folderId": { "type":"string", "description":"Tables-scope folder id. Empty to clear." }
            }, "required": ["tableId","folderId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "folderId": { "type":"string" },
              "updated": { "type":"boolean" }
            }, "required": ["tableId","folderId","updated"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.table.schema.get",
            "Get Table Schema",
            "Get table schema, export config, key metadata, and schema/inheritance link info.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" }
            }, "required": ["tableId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "table": { "type":"object" }
            }, "required": ["table"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.table.schema.link.set",
            "Set Schema Link Source",
            "Set or clear a table's schema source table. When set, the table becomes schema-linked and its columns are source-controlled.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "schemaSourceTableId": { "type":["string","null"], "description":"Source table id. Pass null/empty to clear schema link." }
            }, "required": ["tableId","schemaSourceTableId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "schemaSourceTableId": { "type":"string" },
              "isSchemaLinked": { "type":"boolean" },
              "updated": { "type":"boolean" }
            }, "required": ["tableId","schemaSourceTableId","isSchemaLinked","updated"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.table.inheritance.set",
            "Set Inheritance Source",
            "Set or clear a table's inheritance source table. Inherited columns are source-controlled while local columns remain editable.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "inheritanceSourceTableId": { "type":["string","null"], "description":"Source table id. Pass null/empty to clear inheritance." }
            }, "required": ["tableId","inheritanceSourceTableId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "inheritanceSourceTableId": { "type":"string" },
              "isInherited": { "type":"boolean" },
              "updated": { "type":"boolean" }
            }, "required": ["tableId","inheritanceSourceTableId","isInherited","updated"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.column.add",
            "Add Column",
            "Add a column to a table and initialize default cells for existing rows.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "name": { "type":"string" },
              "kind": { "type":"string", "description":"Id|Text|Number|Checkbox|Select|Formula|Relation|TableRef|Subtable|Spline|TextureAsset|MeshAsset|AudioAsset|UiAsset|Vec2|Vec3|Vec4|Color" },
              "typeId": { "type":["string","null"], "description":"Optional stable column type id. Built-ins use core.* ids. Custom plugin columns should set this." },
              "width": { "type":"number" },
              "options": { "type":"array", "items": { "type":"string" } },
              "formulaExpression": { "type":"string" },
              "formulaEvalScopes": { "type":"string", "description":"Optional flags enum (for example: 'Interactive' or 'Interactive, Export')." },
              "relationTableId": { "type":"string" },
              "relationTargetMode": { "type":"string", "description":"ExternalTable|SelfTable|ParentTable. Defaults to ExternalTable." },
              "relationTableVariantId": { "type":"integer", "description":"Variant id on the relation target table. Defaults to 0 (Base)." },
              "relationDisplayColumnId": { "type":["string","null"] }
            }, "required": ["tableId","name","kind"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "columnId": { "type":"string" },
              "childTableId": { "type":"string" }
            }, "required": ["columnId"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.column.update",
            "Update Column",
            "Update a column's properties (name/options/formula/relation/export settings).",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "columnId": { "type":"string" },
              "name": { "type":"string" },
              "typeId": { "type":["string","null"], "description":"Optional stable column type id. Null/empty resets to built-in mapping for current kind." },
              "width": { "type":"number" },
              "options": { "type":"array", "items": { "type":"string" } },
              "formulaExpression": { "type":"string" },
              "formulaEvalScopes": { "type":"string", "description":"Optional flags enum (for example: 'Interactive' or 'Interactive, Export')." },
              "relationTableId": { "type":["string","null"] },
              "relationTargetMode": { "type":["string","null"], "description":"ExternalTable|SelfTable|ParentTable. Null resets to ExternalTable." },
              "relationTableVariantId": { "type":"integer", "description":"Variant id on the relation target table. Defaults to 0 (Base)." },
              "relationDisplayColumnId": { "type":["string","null"] },
              "exportIgnore": { "type":"boolean" },
              "exportType": { "type":["string","null"] },
              "exportEnumName": { "type":["string","null"] }
            }, "required": ["tableId","columnId"] }
            """,
            """{ "type":"object", "additionalProperties": false }"""),
        new DerpDocMcpToolDefinition(
            "derpdoc.column.delete",
            "Delete Column",
            "Delete a column from a table and remove the cell values from all rows.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "columnId": { "type":"string" }
            }, "required": ["tableId","columnId"] }
            """,
            """{ "type":"object", "additionalProperties": false }"""),
        new DerpDocMcpToolDefinition(
            "derpdoc.table.export.set",
            "Set Table Export Config",
            "Set export config for a table (enabled). Namespace is fixed and struct defaults to table name.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "enabled": { "type":"boolean" }
            }, "required": ["tableId","enabled"] }
            """,
            """{ "type":"object", "additionalProperties": false }"""),
        new DerpDocMcpToolDefinition(
            "derpdoc.table.keys.set",
            "Set Table Keys",
            "Set primary key and secondary keys for a table.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "primaryKeyColumnId": { "type":"string" },
              "secondaryKeys": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "columnId": { "type":"string" },
                "unique": { "type":"boolean" }
              }, "required": ["columnId","unique"] } }
            }, "required": ["tableId"] }
            """,
            """{ "type":"object", "additionalProperties": false }"""),
        new DerpDocMcpToolDefinition(
            "derpdoc.row.add",
            "Add Row",
            "Add one row to a table. For adding multiple rows, use derpdoc.row.add.batch.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variantId": { "type":"integer", "description":"Optional variant id. Defaults to 0 (Base)." },
              "rowId": { "type":"string", "description":"Optional stable row id." },
              "cells": { "type":"object", "description":"Map of columnId -> value (string|number|boolean)." }
            }, "required": ["tableId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "variantId": { "type":"integer" },
              "rowId": { "type":"string" }
            }, "required": ["variantId","rowId"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.row.add.batch",
            "Batch Add Rows",
            "Add multiple rows to the same table in one call. Preferred for 2+ row inserts.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variantId": { "type":"integer", "description":"Optional variant id. Defaults to 0 (Base)." },
              "rows": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "rowId": { "type":"string", "description":"Optional stable row id." },
                "cells": { "type":"object", "description":"Map of columnId -> value (string|number|boolean)." }
              } } }
            }, "required": ["tableId","rows"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "variantId": { "type":"integer" },
              "addedCount": { "type":"integer" },
              "addedRowIds": { "type":"array", "items": { "type":"string" } },
              "errors": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "rowId": { "type":"string" },
                "error": { "type":"string" }
              }, "required": ["rowId","error"] } }
            }, "required": ["variantId","addedCount","addedRowIds","errors"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.row.update",
            "Update Row",
            "Update cells in one existing row. For updating multiple rows, use derpdoc.row.update.batch.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variantId": { "type":"integer", "description":"Optional variant id. Defaults to 0 (Base)." },
              "rowId": { "type":"string" },
              "cells": { "type":"object", "description":"Map of columnId -> value (string|number|boolean)." }
            }, "required": ["tableId","rowId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "variantId": { "type":"integer" },
              "updated": { "type":"boolean" }
            }, "required": ["variantId","updated"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.row.update.batch",
            "Batch Update Rows",
            "Update cells in multiple rows of the same table in one call. Preferred for 2+ row updates.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variantId": { "type":"integer", "description":"Optional variant id. Defaults to 0 (Base)." },
              "updates": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "rowId": { "type":"string" },
                "cells": { "type":"object", "description":"Map of columnId -> value (string|number|boolean)." }
              }, "required": ["rowId"] } }
            }, "required": ["tableId","updates"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "variantId": { "type":"integer" },
              "updatedCount": { "type":"integer" },
              "updatedRowIds": { "type":"array", "items": { "type":"string" } },
              "errors": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "rowId": { "type":"string" },
                "error": { "type":"string" }
              }, "required": ["rowId","error"] } }
            }, "required": ["variantId","updatedCount","updatedRowIds","errors"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.row.delete",
            "Delete Row",
            "Delete one row from a table. For deleting multiple rows, use derpdoc.row.delete.batch.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variantId": { "type":"integer", "description":"Optional variant id. Defaults to 0 (Base)." },
              "rowId": { "type":"string" }
            }, "required": ["tableId","rowId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "variantId": { "type":"integer" },
              "deleted": { "type":"boolean" }
            }, "required": ["variantId","deleted"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.row.delete.batch",
            "Batch Delete Rows",
            "Delete multiple rows from the same table in one call. Preferred for 2+ row deletes.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variantId": { "type":"integer", "description":"Optional variant id. Defaults to 0 (Base)." },
              "rowIds": { "type":"array", "items": { "type":"string" } }
            }, "required": ["tableId","rowIds"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "variantId": { "type":"integer" },
              "deletedCount": { "type":"integer" },
              "deletedRowIds": { "type":"array", "items": { "type":"string" } },
              "errors": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "rowId": { "type":"string" },
                "error": { "type":"string" }
              }, "required": ["rowId","error"] } }
            }, "required": ["variantId","deletedCount","deletedRowIds","errors"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.table.query",
            "Query Table",
            "Return rows for one table (no filtering language in V1). For multi-table reads, use derpdoc.table.query.batch.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "variantId": { "type":"integer", "description":"Optional variant id. Defaults to 0 (Base)." },
              "limit": { "type":"integer" },
              "offset": { "type":"integer" }
            }, "required": ["tableId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "variantId": { "type":"integer" },
              "rows": { "type":"array", "items": { "type":"object" } }
            }, "required": ["variantId","rows"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.table.query.batch",
            "Batch Query Tables",
            "Query multiple tables (or slices) in one call. Preferred for multi-table reads.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "queries": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "tableId": { "type":"string" },
                "variantId": { "type":"integer", "description":"Optional variant id. Defaults to 0 (Base)." },
                "limit": { "type":"integer" },
                "offset": { "type":"integer" }
              }, "required": ["tableId"] } }
            }, "required": ["queries"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "results": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "tableId": { "type":"string" },
                "variantId": { "type":"integer" },
                "rows": { "type":"array", "items": { "type":"object" } },
                "error": { "type":"string" }
              }, "required": ["tableId","variantId","rows"] } }
            }, "required": ["results"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.export",
            "Export .derpdoc",
            "Run the export pipeline for the active project directory (or provided path), writing .derpdoc + generated code.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "path": { "type":"string", "description":"Optional game root or DB root; defaults to active project." },
              "generatedDir": { "type":"string" },
              "binPath": { "type":"string" },
              "livePath": { "type":"string" },
              "noManifest": { "type":"boolean" },
              "noLive": { "type":"boolean" }
            } }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "binPath": { "type":"string" },
              "livePath": { "type":"string" },
              "diagnostics": { "type":"array", "items": { "type":"object" } }
            }, "required": ["binPath","diagnostics"] }
            """),

        // ── View tools ──

        new DerpDocMcpToolDefinition(
            "derpdoc.view.list",
            "List Table Views",
            "List all views for a table.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" }
            }, "required": ["tableId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "views": { "type":"array", "items": { "type":"object" } }
            }, "required": ["views"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.view.create",
            "Create Table View",
            "Create a new view for a table with type/name/config.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "name": { "type":"string" },
              "type": { "type":"string", "description":"Grid|Board|Calendar|Chart|Custom" },
              "customRendererId": { "type":"string", "description":"Renderer ID for Custom views." },
              "groupByColumnId": { "type":"string" },
              "calendarDateColumnId": { "type":"string" },
              "chartKind": { "type":"string", "description":"Bar|Line|Pie|Area (for Chart views)" },
              "chartCategoryColumnId": { "type":"string", "description":"Column ID for X-axis/pie labels" },
              "chartValueColumnId": { "type":"string", "description":"Number column ID for Y-axis/pie values" },
              "visibleColumnIds": { "type":"array", "items": { "type":"string" }, "description":"Ordered column IDs to show. Omit to show all." },
              "filters": { "type":"array", "items": { "type":"object", "properties": {
                "columnId": { "type":"string" }, "op": { "type":"string" }, "value": { "type":"string" }
              } } },
              "sorts": { "type":"array", "items": { "type":"object", "properties": {
                "columnId": { "type":"string" }, "descending": { "type":"boolean" }
              } } }
            }, "required": ["tableId", "name", "type"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "viewId": { "type":"string" },
              "name": { "type":"string" },
              "type": { "type":"string" }
            }, "required": ["viewId"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.view.update",
            "Update Table View",
            "Update a view's config (name/type/filters/sorts/columns/groupBy/calendarDate/chart).",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "viewId": { "type":"string" },
              "name": { "type":"string" },
              "type": { "type":"string", "description":"Grid|Board|Calendar|Chart|Custom" },
              "customRendererId": { "type":"string", "description":"Renderer ID for Custom views." },
              "groupByColumnId": { "type":"string" },
              "calendarDateColumnId": { "type":"string" },
              "chartKind": { "type":"string", "description":"Bar|Line|Pie|Area (for Chart views)" },
              "chartCategoryColumnId": { "type":"string", "description":"Column ID for X-axis/pie labels" },
              "chartValueColumnId": { "type":"string", "description":"Number column ID for Y-axis/pie values" },
              "visibleColumnIds": { "type":"array", "items": { "type":"string" }, "description":"Ordered column IDs to show. Pass empty array to clear (show all). Omit to leave unchanged." },
              "filters": { "type":"array", "items": { "type":"object", "properties": {
                "columnId": { "type":"string" }, "op": { "type":"string" }, "value": { "type":"string" }
              } } },
              "sorts": { "type":"array", "items": { "type":"object", "properties": {
                "columnId": { "type":"string" }, "descending": { "type":"boolean" }
              } } }
            }, "required": ["tableId", "viewId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "viewId": { "type":"string" },
              "updated": { "type":"boolean" }
            }, "required": ["viewId","updated"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.view.delete",
            "Delete Table View",
            "Delete a view from a table.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "viewId": { "type":"string" }
            }, "required": ["tableId", "viewId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "deleted": { "type":"boolean" }
            }, "required": ["deleted"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.nodegraph.ensure",
            "Ensure Node Graph View",
            "Create or update a node graph view and scaffold required node-graph schema columns/tables.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "viewId": { "type":"string", "description":"Optional existing view id. If omitted, a node graph view is created (or reused)." },
              "viewName": { "type":"string", "description":"Optional view name when creating/updating the node graph view." }
            }, "required": ["tableId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "viewId": { "type":"string" },
              "createdView": { "type":"boolean" },
              "updatedView": { "type":"boolean" },
              "scaffolded": { "type":"boolean" },
              "schema": { "type":"object", "additionalProperties": false, "properties": {
                "typeColumnId": { "type":"string" },
                "positionColumnId": { "type":"string" },
                "titleColumnId": { "type":"string" },
                "executionOutputColumnId": { "type":"string" },
                "edgeSubtableColumnId": { "type":"string" },
                "edgeTableId": { "type":"string" }
              }, "required": ["typeColumnId","positionColumnId","titleColumnId","executionOutputColumnId","edgeSubtableColumnId","edgeTableId"] }
            }, "required": ["tableId","viewId","createdView","updatedView","scaffolded","schema"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.nodegraph.get",
            "Get Node Graph Config",
            "Get node-graph schema wiring and per-type layout settings for a table/view.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "viewId": { "type":"string", "description":"Optional node graph view id. If omitted, uses the first node graph view on the table." }
            }, "required": ["tableId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "viewId": { "type":"string" },
              "viewName": { "type":"string" },
              "schema": { "type":"object" },
              "settings": { "type":"object" }
            }, "required": ["tableId","viewId","viewName","schema","settings"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.nodegraph.layout.set",
            "Set Node Graph Layout",
            "Update node graph per-type layout (node width and column display modes).",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "viewId": { "type":"string" },
              "typeName": { "type":"string" },
              "nodeWidth": { "type":"number", "description":"Optional node width override for this type." },
              "replaceFields": { "type":"boolean", "description":"If true, replaces the type layout field list with the provided fields." },
              "fields": { "type":"array", "items": { "type":"object", "additionalProperties": false, "properties": {
                "columnId": { "type":"string" },
                "mode": { "type":"string", "description":"Hidden|Setting|InputPin|OutputPin" }
              }, "required": ["columnId","mode"] } }
            }, "required": ["tableId","viewId","typeName"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "viewId": { "type":"string" },
              "typeName": { "type":"string" },
              "nodeWidth": { "type":"number" },
              "fieldCount": { "type":"integer" },
              "updated": { "type":"boolean" }
            }, "required": ["tableId","viewId","typeName","nodeWidth","fieldCount","updated"] }
            """),

        // ── Document tools ──

        new DerpDocMcpToolDefinition(
            "derpdoc.document.list",
            "List Documents",
            "List all documents in the active project.",
            """{ "type":"object", "additionalProperties": false }""",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documents": { "type":"array", "items": { "type":"object" } }
            }, "required": ["documents"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.document.create",
            "Create Document",
            "Create a document with an initial paragraph block.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "title": { "type":"string" },
              "fileName": { "type":"string", "description":"Optional file name stem." },
              "folderId": { "type":"string", "description":"Optional Documents-scope folder id." },
              "initialText": { "type":"string", "description":"Optional initial paragraph text." }
            } }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "fileName": { "type":"string" }
            }, "required": ["documentId","fileName"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.document.update",
            "Update Document",
            "Update a document title and/or move it between document folders.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "title": { "type":"string" },
              "folderId": { "type":"string", "description":"Documents-scope folder id. Empty to clear." }
            }, "required": ["documentId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "updated": { "type":"boolean" }
            }, "required": ["documentId","updated"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.document.delete",
            "Delete Document",
            "Delete a document.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" }
            }, "required": ["documentId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "deleted": { "type":"boolean" }
            }, "required": ["deleted"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.document.folder.set",
            "Set Document Folder",
            "Move a document into a Documents folder. Pass empty folderId to move to root.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "folderId": { "type":"string", "description":"Documents-scope folder id. Empty to clear." }
            }, "required": ["documentId","folderId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "folderId": { "type":"string" },
              "updated": { "type":"boolean" }
            }, "required": ["documentId","folderId","updated"] }
            """),

        // ── Document block tools ──

        new DerpDocMcpToolDefinition(
            "derpdoc.block.list",
            "List Document Blocks",
            "List blocks in a document, including full text when includeText=true.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "includeText": { "type":"boolean", "description":"If true (default), include full plain text for non-table blocks." }
            }, "required": ["documentId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "blocks": { "type":"array", "items": { "type":"object" } }
            }, "required": ["blocks"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.block.add",
            "Add Document Block",
            "Insert a block at an index in a document. Defaults to append.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "index": { "type":"integer", "description":"0-based insert index. Omit to append." },
              "type": { "type":"string", "description":"Paragraph|Heading1|Heading2|Heading3|Heading4|Heading5|Heading6|BulletList|NumberedList|CheckboxList|CodeBlock|Quote|Formula|Variable|Divider|Table" },
              "text": { "type":"string", "description":"Plain text for text blocks." },
              "indentLevel": { "type":"integer" },
              "checked": { "type":"boolean" },
              "language": { "type":"string", "description":"Code block language hint." },
              "tableId": { "type":"string", "description":"Required for Table blocks." },
              "tableVariantId": { "type":"integer", "description":"Optional variant id for Table blocks. Defaults to 0 (Base)." },
              "viewId": { "type":"string", "description":"Optional per-block view id for Table blocks." }
            }, "required": ["documentId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "blockId": { "type":"string" },
              "index": { "type":"integer" },
              "order": { "type":"string" },
              "tableVariantId": { "type":"integer" }
            }, "required": ["blockId","index","order","tableVariantId"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.block.update",
            "Update Document Block",
            "Update a block's type/text/indent/check/language/table/view fields.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "blockId": { "type":"string" },
              "type": { "type":"string", "description":"Paragraph|Heading1|Heading2|Heading3|Heading4|Heading5|Heading6|BulletList|NumberedList|CheckboxList|CodeBlock|Quote|Formula|Variable|Divider|Table" },
              "text": { "type":"string" },
              "indentLevel": { "type":"integer" },
              "checked": { "type":"boolean" },
              "language": { "type":"string" },
              "tableId": { "type":"string" },
              "tableVariantId": { "type":"integer" },
              "viewId": { "type":"string" }
            }, "required": ["documentId","blockId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "blockId": { "type":"string" },
              "tableVariantId": { "type":"integer" },
              "updated": { "type":"boolean" }
            }, "required": ["blockId","tableVariantId","updated"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.block.delete",
            "Delete Document Block",
            "Delete a block from a document.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "blockId": { "type":"string" }
            }, "required": ["documentId","blockId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "deleted": { "type":"boolean" }
            }, "required": ["deleted"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.block.move",
            "Move Document Block",
            "Move a block to a target index in the document.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "blockId": { "type":"string" },
              "index": { "type":"integer", "description":"0-based target index in final order." }
            }, "required": ["documentId","blockId","index"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "blockId": { "type":"string" },
              "index": { "type":"integer" },
              "order": { "type":"string" },
              "updated": { "type":"boolean" }
            }, "required": ["blockId","index","order","updated"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.block.view.set",
            "Set Block View",
            "Set the viewId on an embedded table block to use a specific per-instance view. Pass empty viewId to clear (use table default).",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "blockId": { "type":"string" },
              "viewId": { "type":"string", "description":"View ID from the table's views list. Empty string to clear." }
            }, "required": ["documentId", "blockId", "viewId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "blockId": { "type":"string" },
              "viewId": { "type":"string" },
              "updated": { "type":"boolean" }
            }, "required": ["blockId","viewId","updated"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.block.view.create",
            "Create Per-Block View",
            "Create a new view on the table and assign it to an embedded table block in one step. Returns the new view ID.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "documentId": { "type":"string" },
              "blockId": { "type":"string" },
              "name": { "type":"string" },
              "type": { "type":"string", "description":"Grid|Board|Calendar|Chart|Custom" },
              "customRendererId": { "type":"string", "description":"Renderer ID for Custom views." },
              "groupByColumnId": { "type":"string" },
              "calendarDateColumnId": { "type":"string" },
              "visibleColumnIds": { "type":"array", "items": { "type":"string" } },
              "filters": { "type":"array", "items": { "type":"object", "properties": {
                "columnId": { "type":"string" }, "op": { "type":"string" }, "value": { "type":"string" }
              } } },
              "sorts": { "type":"array", "items": { "type":"object", "properties": {
                "columnId": { "type":"string" }, "descending": { "type":"boolean" }
              } } }
            }, "required": ["documentId", "blockId", "name", "type"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "viewId": { "type":"string" },
              "blockId": { "type":"string" },
              "name": { "type":"string" },
              "type": { "type":"string" }
            }, "required": ["viewId","blockId"] }
            """),

        // ── Derived table tools ──

        new DerpDocMcpToolDefinition(
            "derpdoc.derived.get",
            "Get Derived Config",
            "Get the derived (Append/Join) configuration for a table. Returns null config if the table is not derived.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" }
            }, "required": ["tableId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "isDerived": { "type":"boolean" },
              "config": { "type":"object" }
            }, "required": ["tableId","isDerived"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.derived.set",
            "Set Derived Config",
            "Set or update the full derived configuration for a table (baseTableId, filterExpression, steps, projections). Pass null config to clear derived status.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "config": { "type":"object", "description":"Full derived config, or null to clear. Shape: { baseTableId?, filterExpression?, steps: [{ kind:'Append'|'Join', sourceTableId, joinKind?:'Left'|'Inner'|'FullOuter', keyMappings?: [{ baseColumnId, sourceColumnId }] }], projections?: [{ sourceTableId, sourceColumnId, outputColumnId?, renameAlias? }] }", "properties": {
                "baseTableId": { "type":"string" },
                "filterExpression": { "type":"string" },
                "steps": { "type":"array", "items": { "type":"object", "properties": {
                  "kind": { "type":"string" }, "sourceTableId": { "type":"string" },
                  "joinKind": { "type":"string" }, "keyMappings": { "type":"array", "items": { "type":"object", "properties": {
                    "baseColumnId": { "type":"string" }, "sourceColumnId": { "type":"string" }
                  } } }
                } } },
                "projections": { "type":"array", "items": { "type":"object", "properties": {
                  "sourceTableId": { "type":"string" }, "sourceColumnId": { "type":"string" },
                  "outputColumnId": { "type":"string" }, "renameAlias": { "type":"string" }
                } } }
              } }
            }, "required": ["tableId"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "isDerived": { "type":"boolean" },
              "updated": { "type":"boolean" }
            }, "required": ["tableId","isDerived","updated"] }
            """),

        // ── Nanobanana image tools ──

        new DerpDocMcpToolDefinition(
            "derpdoc.nanobanana.generate",
            "Generate Image",
            "Generate an image with Gemini generateContent, save it under Assets/, and optionally assign it to a TextureAsset cell.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "request": { "type":"object", "description":"Gemini request payload. Provide full generateContent body (with contents), or shorthand { prompt, imageBase64|imageDataUrl, mimeType?, generationConfig?, safetySettings?, systemInstruction?, tools?, toolConfig?, cachedContent? }." },
              "outputFolder": { "type":"string", "description":"Relative folder under Assets/. Default: Generated/Nanobanana" },
              "outputName": { "type":"string", "description":"Optional file name (or relative path). If provided, existing file is overwritten." },
              "tableId": { "type":"string", "description":"Optional table id for direct cell assignment." },
              "rowId": { "type":"string", "description":"Optional row id for direct cell assignment." },
              "columnId": { "type":"string", "description":"Optional TextureAsset column id for direct cell assignment." },
              "variantId": { "type":"integer", "description":"Optional variant id for cell assignment. Defaults to 0 (Base)." }
            }, "required": ["request"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "operation": { "type":"string" },
              "assetPath": { "type":"string" },
              "overwroteExisting": { "type":"boolean" },
              "rowUpdated": { "type":"boolean" },
              "variantId": { "type":"integer" },
              "responseJson": { "type":"string" }
            }, "required": ["operation","assetPath","overwroteExisting","rowUpdated","variantId","responseJson"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.nanobanana.edit",
            "Edit Image",
            "Edit an image with Gemini generateContent, save it under Assets/, and optionally assign it to a TextureAsset cell.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "request": { "type":"object", "description":"Gemini request payload. Provide full generateContent body (with contents), or shorthand { prompt, imageBase64|imageDataUrl, mimeType?, generationConfig?, safetySettings?, systemInstruction?, tools?, toolConfig?, cachedContent? }. Shorthand edit requires imageBase64 or imageDataUrl." },
              "outputFolder": { "type":"string", "description":"Relative folder under Assets/. Default: Generated/Nanobanana" },
              "outputName": { "type":"string", "description":"Optional file name (or relative path). If provided, existing file is overwritten." },
              "tableId": { "type":"string", "description":"Optional table id for direct cell assignment." },
              "rowId": { "type":"string", "description":"Optional row id for direct cell assignment." },
              "columnId": { "type":"string", "description":"Optional TextureAsset column id for direct cell assignment." },
              "variantId": { "type":"integer", "description":"Optional variant id for cell assignment. Defaults to 0 (Base)." }
            }, "required": ["request"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "operation": { "type":"string" },
              "assetPath": { "type":"string" },
              "overwroteExisting": { "type":"boolean" },
              "rowUpdated": { "type":"boolean" },
              "variantId": { "type":"integer" },
              "responseJson": { "type":"string" }
            }, "required": ["operation","assetPath","overwroteExisting","rowUpdated","variantId","responseJson"] }
            """),

        // ── ElevenLabs audio tools ──

        new DerpDocMcpToolDefinition(
            "derpdoc.elevenlabs.generate",
            "Generate Audio",
            "Generate speech audio with ElevenLabs text-to-speech, save it under Assets/, and optionally assign it to an AudioAsset cell.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "request": { "type":"object", "description":"ElevenLabs TTS payload. Must include voiceId (or voice_id) and text. Additional fields are forwarded into the request body." },
              "outputFolder": { "type":"string", "description":"Relative folder under Assets/. Default: Generated/ElevenLabs" },
              "outputName": { "type":"string", "description":"Optional file name (or relative path). If provided, existing file is overwritten." },
              "tableId": { "type":"string", "description":"Optional table id for direct cell assignment." },
              "rowId": { "type":"string", "description":"Optional row id for direct cell assignment." },
              "columnId": { "type":"string", "description":"Optional AudioAsset column id for direct cell assignment." },
              "variantId": { "type":"integer", "description":"Optional variant id for cell assignment. Defaults to 0 (Base)." }
            }, "required": ["request"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "operation": { "type":"string" },
              "assetPath": { "type":"string" },
              "overwroteExisting": { "type":"boolean" },
              "rowUpdated": { "type":"boolean" },
              "variantId": { "type":"integer" },
              "responseText": { "type":"string" }
            }, "required": ["operation","assetPath","overwroteExisting","rowUpdated","variantId","responseText"] }
            """),
        new DerpDocMcpToolDefinition(
            "derpdoc.elevenlabs.edit",
            "Edit Audio",
            "Transform audio with ElevenLabs speech-to-speech, save it under Assets/, and optionally assign it to an AudioAsset cell.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "request": { "type":"object", "description":"ElevenLabs speech-to-speech payload. Must include voiceId (or voice_id) and one of audioBase64/audioDataUrl. Additional fields are forwarded into the multipart form body." },
              "outputFolder": { "type":"string", "description":"Relative folder under Assets/. Default: Generated/ElevenLabs" },
              "outputName": { "type":"string", "description":"Optional file name (or relative path). If provided, existing file is overwritten." },
              "tableId": { "type":"string", "description":"Optional table id for direct cell assignment." },
              "rowId": { "type":"string", "description":"Optional row id for direct cell assignment." },
              "columnId": { "type":"string", "description":"Optional AudioAsset column id for direct cell assignment." },
              "variantId": { "type":"integer", "description":"Optional variant id for cell assignment. Defaults to 0 (Base)." }
            }, "required": ["request"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "operation": { "type":"string" },
              "assetPath": { "type":"string" },
              "overwroteExisting": { "type":"boolean" },
              "rowUpdated": { "type":"boolean" },
              "variantId": { "type":"integer" },
              "responseText": { "type":"string" }
            }, "required": ["operation","assetPath","overwroteExisting","rowUpdated","variantId","responseText"] }
            """),

        // ── Formula tools ──

        new DerpDocMcpToolDefinition(
            "derpdoc.formula.validate",
            "Validate Formula",
            "Validate a formula expression against a table's columns. Returns whether the formula compiles and any error messages.",
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "tableId": { "type":"string" },
              "expression": { "type":"string", "description":"Formula expression to validate, e.g. 'col(Price) * col(Quantity)'" }
            }, "required": ["tableId", "expression"] }
            """,
            """
            { "type":"object", "additionalProperties": false, "properties": {
              "valid": { "type":"boolean" },
              "error": { "type":"string" }
            }, "required": ["valid"] }
            """),
    ];
}
