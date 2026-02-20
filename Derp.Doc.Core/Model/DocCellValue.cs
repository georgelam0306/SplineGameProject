using Derp.Doc.Tables;
using Derp.Doc.Plugins;

namespace Derp.Doc.Model;

/// <summary>
/// Discriminated union for cell values. Only one field is valid at a time,
/// determined by the column's DocColumnKind.
/// </summary>
public struct DocCellValue
{
    public string StringValue;
    public double NumberValue;
    public bool BoolValue;
    public double XValue;
    public double YValue;
    public double ZValue;
    public double WValue;
    public string? CellFormulaExpression;
    public string? FormulaError;
    public DocModelPreviewSettings? ModelPreviewSettings;

    public static DocCellValue Text(string value) => new() { StringValue = value ?? "" };
    public static DocCellValue Text(string value, DocModelPreviewSettings? modelPreviewSettings) => new()
    {
        StringValue = value ?? "",
        ModelPreviewSettings = modelPreviewSettings?.Clone(),
    };
    public static DocCellValue Number(double value) => new() { NumberValue = value };
    public static DocCellValue Bool(bool value) => new() { BoolValue = value };
    public static DocCellValue Vec2(double x, double y) => new() { XValue = x, YValue = y };
    public static DocCellValue Vec3(double x, double y, double z) => new() { XValue = x, YValue = y, ZValue = z };
    public static DocCellValue Vec4(double x, double y, double z, double w) => new() { XValue = x, YValue = y, ZValue = z, WValue = w };
    public static DocCellValue Color(double r, double g, double b, double a = 1.0) => new() { XValue = r, YValue = g, ZValue = b, WValue = a };

    public readonly bool HasCellFormulaExpression => !string.IsNullOrWhiteSpace(CellFormulaExpression);

    public readonly DocCellValue Clone()
    {
        return new DocCellValue
        {
            StringValue = StringValue,
            NumberValue = NumberValue,
            BoolValue = BoolValue,
            XValue = XValue,
            YValue = YValue,
            ZValue = ZValue,
            WValue = WValue,
            CellFormulaExpression = CellFormulaExpression,
            FormulaError = FormulaError,
            ModelPreviewSettings = ModelPreviewSettings?.Clone(),
        };
    }

    public readonly DocCellValue WithCellFormulaExpression(string? expression)
    {
        var clone = Clone();
        clone.CellFormulaExpression = string.IsNullOrWhiteSpace(expression)
            ? null
            : expression.Trim();
        return clone;
    }

    public readonly DocCellValue ClearCellFormulaExpression()
    {
        var clone = Clone();
        clone.CellFormulaExpression = null;
        return clone;
    }

    /// <summary>
    /// Returns a default value for the given column.
    /// </summary>
    public static DocCellValue Default(DocColumn column)
    {
        string columnTypeId = DocColumnTypeIdMapper.Resolve(column.ColumnTypeId, column.Kind);
        if (!DocColumnTypeIdMapper.IsBuiltIn(columnTypeId) &&
            ColumnDefaultValueProviderRegistry.TryCreateDefaultValue(columnTypeId, column, out var pluginDefaultValue))
        {
            return pluginDefaultValue;
        }

        return Default(column.Kind);
    }

    /// <summary>
    /// Returns a default value for the given column kind.
    /// </summary>
    public static DocCellValue Default(DocColumnKind kind) => kind switch
    {
        DocColumnKind.Id => Text(""),
        DocColumnKind.Text => Text(""),
        DocColumnKind.Number => Number(0),
        DocColumnKind.Checkbox => Bool(false),
        DocColumnKind.Select => Text(""),
        DocColumnKind.Formula => Number(0),
        DocColumnKind.Relation => Text(""),
        DocColumnKind.TableRef => Text(""),
        DocColumnKind.Subtable => Text(""),
        DocColumnKind.Spline => Text(SplineUtils.DefaultSplineJson),
        DocColumnKind.TextureAsset => Text(""),
        DocColumnKind.MeshAsset => Text(""),
        DocColumnKind.AudioAsset => Text(""),
        DocColumnKind.UiAsset => Text(""),
        DocColumnKind.Vec2 => Vec2(0, 0),
        DocColumnKind.Vec3 => Vec3(0, 0, 0),
        DocColumnKind.Vec4 => Vec4(0, 0, 0, 0),
        DocColumnKind.Color => Color(1, 1, 1, 1),
        _ => Text("")
    };
}
