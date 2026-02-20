using Derp.Doc.Model;

namespace Derp.Doc.Tables;

public readonly struct FormulaValue
{
    public FormulaValueKind Kind { get; }
    public double NumberValue { get; }
    public double XValue { get; }
    public double YValue { get; }
    public double ZValue { get; }
    public double WValue { get; }
    public string? StringValue { get; }
    public bool BoolValue { get; }
    public DateTime DateTimeValue { get; }
    public DocDocument? DocumentValue { get; }
    public DocTable? TableValue { get; }
    public DocRow? RowValue { get; }
    public IReadOnlyList<DocRow>? RowsValue { get; }

    private FormulaValue(
        FormulaValueKind kind,
        double numberValue = 0,
        double xValue = 0,
        double yValue = 0,
        double zValue = 0,
        double wValue = 0,
        string? stringValue = null,
        bool boolValue = false,
        DateTime dateTimeValue = default,
        DocDocument? documentValue = null,
        DocTable? tableValue = null,
        DocRow? rowValue = null,
        IReadOnlyList<DocRow>? rowsValue = null)
    {
        Kind = kind;
        NumberValue = numberValue;
        XValue = xValue;
        YValue = yValue;
        ZValue = zValue;
        WValue = wValue;
        StringValue = stringValue;
        BoolValue = boolValue;
        DateTimeValue = dateTimeValue;
        DocumentValue = documentValue;
        TableValue = tableValue;
        RowValue = rowValue;
        RowsValue = rowsValue;
    }

    public static FormulaValue Null() => new(FormulaValueKind.Null);
    public static FormulaValue Number(double value) => new(FormulaValueKind.Number, numberValue: value);
    public static FormulaValue String(string value) => new(FormulaValueKind.String, stringValue: value);
    public static FormulaValue Bool(bool value) => new(FormulaValueKind.Bool, boolValue: value);
    public static FormulaValue Vec2(double x, double y) => new(FormulaValueKind.Vec2, xValue: x, yValue: y);
    public static FormulaValue Vec3(double x, double y, double z) => new(FormulaValueKind.Vec3, xValue: x, yValue: y, zValue: z);
    public static FormulaValue Vec4(double x, double y, double z, double w) => new(FormulaValueKind.Vec4, xValue: x, yValue: y, zValue: z, wValue: w);
    public static FormulaValue Color(double r, double g, double b, double a) => new(FormulaValueKind.Color, xValue: r, yValue: g, zValue: b, wValue: a);
    public static FormulaValue DateTime(DateTime value) => new(FormulaValueKind.DateTime, dateTimeValue: value);
    public static FormulaValue Document(DocDocument document) => new(FormulaValueKind.DocumentReference, documentValue: document);
    public static FormulaValue Table(DocTable table) => new(FormulaValueKind.TableReference, tableValue: table);
    public static FormulaValue Row(DocTable table, DocRow row) => new(FormulaValueKind.RowReference, tableValue: table, rowValue: row);
    public static FormulaValue Rows(DocTable table, IReadOnlyList<DocRow> rows) => new(FormulaValueKind.RowCollection, tableValue: table, rowsValue: rows);
}
