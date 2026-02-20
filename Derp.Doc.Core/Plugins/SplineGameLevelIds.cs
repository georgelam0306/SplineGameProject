namespace Derp.Doc.Plugins;

public static class SplineGameLevelIds
{
    public const string ColumnTypeId = "splinegame.level";
    public const string TableTypeId = "splinegame.level.table";
    public const string PointsTableTypeId = "splinegame.level.points.table";
    public const string EntitiesTableTypeId = "splinegame.level.entities.table";
    public const string EntityToolTableTypeId = "splinegame.entity.tools.table";
    public const string LevelEditorRendererId = "splinegame.level-editor";
    public const string LevelEditorViewName = "Spline Level Editor";

    public const string ParentRowIdColumnId = "_parentRowId";
    public const string PointsSubtableColumnId = "points";
    public const string EntitiesSubtableColumnId = "entities";
    public const string EntityToolsTableColumnId = "entity_tools_table";

    public const string PointsParentRowIdColumnId = "_parentRowId";
    public const string PointsOrderColumnId = "point_order";
    public const string PointsPositionColumnId = "point_position";
    public const string PointsTangentInColumnId = "point_tangent_in";
    public const string PointsTangentOutColumnId = "point_tangent_out";

    public const string EntitiesParentRowIdColumnId = "_parentRowId";
    public const string EntitiesOrderColumnId = "entity_order";
    public const string EntitiesParamTColumnId = "entity_param_t";
    public const string EntitiesPositionColumnId = "entity_position";
    public const string EntitiesTableRefColumnId = "entity_table";
    public const string EntitiesRowIdColumnId = "entity_row_id";
    public const string EntitiesDataJsonColumnId = "entity_data_json";

    public const string EntityToolIdColumnId = "id";
    public const string EntityToolNameColumnId = "name";
    public const string EntityToolTableRefColumnId = "entities_table";

    public const string EntityDefinitionIdColumnId = "id";
    public const string EntityDefinitionNameColumnId = "name";
    public const string EntityDefinitionUiAssetColumnId = "ui_asset";
    public const string EntityDefinitionScaleColumnId = "scale";

    public const string SystemEntityToolsTableKey = "system.splinegame.entity_tools";
    public const string SystemEntityToolsTableName = "splinegame_entity_tools";
    public const string SystemEntityToolsFileName = "system_splinegame_entity_tools";
    public const string SystemEntityBaseTableKey = "system.splinegame.entity_base";
    public const string SystemEntityBaseTableName = "splinegame_entity_base";
    public const string SystemEntityBaseFileName = "system_splinegame_entity_base";

    // Legacy flat schema ids kept for backward-compat migration.
    public const string EntryTypeColumnId = "entry_type";
    public const string OrderColumnId = "order";
    public const string ParamTColumnId = "param_t";
    public const string PositionColumnId = "position";
    public const string TangentInColumnId = "tangent_in";
    public const string TangentOutColumnId = "tangent_out";
    public const string EnemyTypeColumnId = "enemy_type";
    public const string ObstacleTypeColumnId = "obstacle_type";
    public const string TriggerTypeColumnId = "trigger_type";
    public const string DataJsonColumnId = "data_json";

    public const string EntryTypeSplinePoint = "SplinePoint";
    public const string EntryTypeEnemy = "Enemy";
    public const string EntryTypeObstacle = "Obstacle";
    public const string EntryTypeTrigger = "Trigger";

    public static readonly string[] EntryTypeOptions =
    [
        EntryTypeSplinePoint,
        EntryTypeEnemy,
        EntryTypeObstacle,
        EntryTypeTrigger,
    ];
}
