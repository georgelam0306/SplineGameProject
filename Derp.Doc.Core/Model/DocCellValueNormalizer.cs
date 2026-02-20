using Derp.Doc.Plugins;
using FixedMath;

namespace Derp.Doc.Model;

public static class DocCellValueNormalizer
{
    public static DocCellValue NormalizeForColumn(DocColumn column, DocCellValue value)
    {
        ArgumentNullException.ThrowIfNull(column);

        var normalizedValue = value.Clone();
        switch (column.Kind)
        {
            case DocColumnKind.Number:
            case DocColumnKind.Formula:
                normalizedValue.NumberValue = NormalizeNumber(column, normalizedValue.NumberValue);
                break;
            case DocColumnKind.Vec2:
            case DocColumnKind.Vec3:
            case DocColumnKind.Vec4:
                NormalizeVector(column, ref normalizedValue);
                break;
            case DocColumnKind.Color:
                NormalizeColor(column, ref normalizedValue);
                break;
        }

        return normalizedValue;
    }

    public static double NormalizeNumber(DocColumn column, double rawValue)
    {
        ArgumentNullException.ThrowIfNull(column);

        double normalizedValue = rawValue;
        string mapping = string.IsNullOrWhiteSpace(column.ExportType) ? "Fixed64" : column.ExportType!;
        if (string.Equals(mapping, "int", StringComparison.OrdinalIgnoreCase))
        {
            normalizedValue = Math.Round(normalizedValue, MidpointRounding.AwayFromZero);
        }
        else if (string.Equals(mapping, "float", StringComparison.OrdinalIgnoreCase))
        {
            normalizedValue = (double)(float)normalizedValue;
        }
        else if (string.Equals(mapping, "fixed32", StringComparison.OrdinalIgnoreCase))
        {
            normalizedValue = QuantizeFixed32(normalizedValue);
        }
        else
        {
            normalizedValue = QuantizeFixed64(normalizedValue);
        }

        if (column.NumberMin.HasValue && normalizedValue < column.NumberMin.Value)
        {
            normalizedValue = column.NumberMin.Value;
        }

        if (column.NumberMax.HasValue && normalizedValue > column.NumberMax.Value)
        {
            normalizedValue = column.NumberMax.Value;
        }

        return normalizedValue;
    }

    private static void NormalizeVector(DocColumn column, ref DocCellValue value)
    {
        string columnTypeId = DocColumnTypeIdMapper.Resolve(column.ColumnTypeId, column.Kind);
        int dimension = DocColumnTypeIdMapper.GetVectorDimension(columnTypeId, column.Kind);
        if (dimension <= 0)
        {
            dimension = 2;
        }

        value.XValue = QuantizeVectorComponent(columnTypeId, value.XValue);
        value.YValue = QuantizeVectorComponent(columnTypeId, value.YValue);
        value.ZValue = dimension >= 3 ? QuantizeVectorComponent(columnTypeId, value.ZValue) : 0;
        value.WValue = dimension >= 4 ? QuantizeVectorComponent(columnTypeId, value.WValue) : 0;
    }

    private static void NormalizeColor(DocColumn column, ref DocCellValue value)
    {
        string columnTypeId = DocColumnTypeIdMapper.Resolve(column.ColumnTypeId, column.Kind);
        if (DocColumnTypeIdMapper.IsHdrColorTypeId(columnTypeId))
        {
            return;
        }

        value.XValue = Math.Clamp(value.XValue, 0, 1);
        value.YValue = Math.Clamp(value.YValue, 0, 1);
        value.ZValue = Math.Clamp(value.ZValue, 0, 1);
        value.WValue = Math.Clamp(value.WValue, 0, 1);
    }

    private static double QuantizeVectorComponent(string columnTypeId, double value)
    {
        if (DocColumnTypeIdMapper.IsFixedVectorTypeId(columnTypeId))
        {
            bool isFixed32 = string.Equals(columnTypeId, DocColumnTypeIds.Vec2Fixed32, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(columnTypeId, DocColumnTypeIds.Vec3Fixed32, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(columnTypeId, DocColumnTypeIds.Vec4Fixed32, StringComparison.OrdinalIgnoreCase);
            return isFixed32
                ? QuantizeFixed32(value)
                : QuantizeFixed64(value);
        }

        if (DocColumnTypeIdMapper.IsFloat32VectorTypeId(columnTypeId))
        {
            return (double)(float)value;
        }

        return value;
    }

    private static double QuantizeFixed32(double value)
    {
        double minFixed32 = int.MinValue / (double)Fixed32.One;
        double maxFixed32 = int.MaxValue / (double)Fixed32.One;
        double clampedValue = value;
        if (clampedValue < minFixed32)
        {
            clampedValue = minFixed32;
        }
        else if (clampedValue > maxFixed32)
        {
            clampedValue = maxFixed32;
        }

        return Fixed32.FromDouble(clampedValue).ToDouble();
    }

    private static double QuantizeFixed64(double value)
    {
        return Fixed64.FromDouble(value).ToDouble();
    }
}
