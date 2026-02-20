using System;
using System.Collections.Generic;
using Derp.Doc.Model;

namespace Derp.Doc.Tables;

/// <summary>
/// Result of materializing a derived table.
/// </summary>
internal sealed class DerivedMaterializeResult
{
    public List<DocRow> Rows { get; } = new();
    public List<OutRowKey> RowKeys { get; } = new();
    public List<DerivedMatchState> RowDiagnostics { get; } = new();

    // Counts of final per-row diagnostic states.
    public int NoMatchCount;
    public int MultiMatchCount;
    public int TypeMismatchCount;
}

/// <summary>
/// Materializes derived table rows from source tables via a mixed Append/Join pipeline.
/// </summary>
internal static class DerivedResolver
{
    private sealed class WorkingRow
    {
        public OutRowKey Key;
        public DocRow Row;
        public DerivedMatchState State;
        public bool Filtered;

        public WorkingRow(OutRowKey key, DocRow row)
        {
            Key = key;
            Row = row;
            State = DerivedMatchState.Matched;
            Filtered = false;
        }
    }

    public static DerivedMaterializeResult Materialize(DocTable derivedTable, IFormulaContext context)
    {
        var result = new DerivedMaterializeResult();
        var config = derivedTable.DerivedConfig;
        if (config == null)
        {
            return result;
        }

        // Build derived column lookup once for join key extraction.
        var derivedColumnById = new Dictionary<string, DocColumn>(derivedTable.Columns.Count, StringComparer.Ordinal);
        for (int i = 0; i < derivedTable.Columns.Count; i++)
        {
            derivedColumnById[derivedTable.Columns[i].Id] = derivedTable.Columns[i];
        }

        var working = new List<WorkingRow>(64);

        // Seed from base table if present (join pipelines) - in mixed pipelines this simply provides initial rows.
        if (!string.IsNullOrEmpty(config.BaseTableId) && context.TryGetTableById(config.BaseTableId, out var baseTable))
        {
            SeedFromBaseTable(baseTable, config, context, working);
        }

        for (int stepIndex = 0; stepIndex < config.Steps.Count; stepIndex++)
        {
            var step = config.Steps[stepIndex];
            if (step.Kind == DerivedStepKind.Append)
            {
                ApplyAppendStep(step, config, context, working);
            }
            else if (step.Kind == DerivedStepKind.Join)
            {
                ApplyJoinStep(derivedColumnById, step, config, context, working);
            }
        }

        ApplyFilterExpressionIfNeeded(derivedTable, config, working);

        // Build final result lists and final-state counts.
        for (int i = 0; i < working.Count; i++)
        {
            var wr = working[i];
            if (wr.Filtered)
            {
                continue;
            }

            result.Rows.Add(wr.Row);
            result.RowKeys.Add(wr.Key);
            result.RowDiagnostics.Add(wr.State);

            if (wr.State == DerivedMatchState.TypeMismatch) result.TypeMismatchCount++;
            else if (wr.State == DerivedMatchState.MultiMatch) result.MultiMatchCount++;
            else if (wr.State == DerivedMatchState.NoMatch) result.NoMatchCount++;
        }

        return result;
    }

    private static void SeedFromBaseTable(
        DocTable baseTable,
        DocDerivedConfig config,
        IFormulaContext context,
        List<WorkingRow> working)
    {
        for (int i = 0; i < baseTable.Rows.Count; i++)
        {
            var baseRow = baseTable.Rows[i];
            var outKey = new OutRowKey(baseTable.Id, baseRow.Id);
            var outRow = new DocRow { Id = EncodeRowId(outKey) };

            CopyProjectedCells(outRow, baseRow, baseTable, baseTable.Id, config);
            working.Add(new WorkingRow(outKey, outRow));
        }
    }

    private static void ApplyAppendStep(
        DerivedStep step,
        DocDerivedConfig config,
        IFormulaContext context,
        List<WorkingRow> working)
    {
        if (string.IsNullOrEmpty(step.SourceTableId))
        {
            return;
        }

        if (!context.TryGetTableById(step.SourceTableId, out var sourceTable))
        {
            return;
        }

        string appendOriginId = !string.IsNullOrEmpty(step.Id) ? step.Id : step.SourceTableId;

        for (int rowIndex = 0; rowIndex < sourceTable.Rows.Count; rowIndex++)
        {
            var sourceRow = sourceTable.Rows[rowIndex];
            var outKey = new OutRowKey(appendOriginId, sourceRow.Id);
            var outRow = new DocRow { Id = EncodeRowId(outKey) };

            CopyProjectedCells(outRow, sourceRow, sourceTable, step.SourceTableId, config);
            working.Add(new WorkingRow(outKey, outRow));
        }
    }

    private static void ApplyJoinStep(
        Dictionary<string, DocColumn> derivedColumnById,
        DerivedStep step,
        DocDerivedConfig config,
        IFormulaContext context,
        List<WorkingRow> working)
    {
        if (string.IsNullOrEmpty(step.SourceTableId))
        {
            return;
        }

        if (!context.TryGetTableById(step.SourceTableId, out var sourceTable))
        {
            return;
        }

        if (step.KeyMappings.Count <= 0)
        {
            // No join key configured: treat as config/type mismatch for all existing rows.
            for (int i = 0; i < working.Count; i++)
            {
                working[i].State = CombineState(working[i].State, DerivedMatchState.TypeMismatch);
            }
            return;
        }

        // Build source column lookup and validate configuration.
        var sourceColumnById = new Dictionary<string, DocColumn>(sourceTable.Columns.Count, StringComparer.Ordinal);
        for (int i = 0; i < sourceTable.Columns.Count; i++)
        {
            sourceColumnById[sourceTable.Columns[i].Id] = sourceTable.Columns[i];
        }

        for (int k = 0; k < step.KeyMappings.Count; k++)
        {
            var mapping = step.KeyMappings[k];
            if (!derivedColumnById.ContainsKey(mapping.BaseColumnId) || !sourceColumnById.ContainsKey(mapping.SourceColumnId))
            {
                for (int i = 0; i < working.Count; i++)
                {
                    working[i].State = CombineState(working[i].State, DerivedMatchState.TypeMismatch);
                }
                return;
            }
        }

        int keyCount = step.KeyMappings.Count;
        if (keyCount > 3)
        {
            // Path forward: support more keys, but keep Phase 4 strict and predictable.
            for (int i = 0; i < working.Count; i++)
            {
                working[i].State = CombineState(working[i].State, DerivedMatchState.TypeMismatch);
            }
            return;
        }

        // Build index once for this join step.
        var index1 = keyCount == 1 ? BuildIndex1(sourceTable, step, sourceColumnById) : null;
        var index2 = keyCount == 2 ? BuildIndex2(sourceTable, step, sourceColumnById) : null;
        var index3 = keyCount == 3 ? BuildIndex3(sourceTable, step, sourceColumnById) : null;

        for (int rowIndex = 0; rowIndex < working.Count; rowIndex++)
        {
            var wr = working[rowIndex];
            if (wr.Filtered)
            {
                continue;
            }

            var joinResult = FindJoinMatch(
                derivedColumnById,
                sourceColumnById,
                step,
                wr.Row,
                index1,
                index2,
                index3,
                out int matchedSourceRowIndex);

            if (joinResult == DerivedMatchState.Matched && matchedSourceRowIndex >= 0 && matchedSourceRowIndex < sourceTable.Rows.Count)
            {
                var matchedRow = sourceTable.Rows[matchedSourceRowIndex];
                CopyProjectedCells(wr.Row, matchedRow, sourceTable, step.SourceTableId, config);
            }
            else
            {
                wr.State = CombineState(wr.State, joinResult);
            }

            if (step.JoinKind == DerivedJoinKind.Inner && joinResult == DerivedMatchState.NoMatch)
            {
                wr.Filtered = true;
            }
        }
    }

    private static void ApplyFilterExpressionIfNeeded(
        DocTable derivedTable,
        DocDerivedConfig config,
        List<WorkingRow> working)
    {
        if (string.IsNullOrWhiteSpace(config.FilterExpression))
        {
            return;
        }

        FilterExpressionNode? root = TryCompileFilterExpression(derivedTable, config.FilterExpression);
        if (root == null)
        {
            for (int rowIndex = 0; rowIndex < working.Count; rowIndex++)
            {
                if (!working[rowIndex].Filtered)
                {
                    working[rowIndex].Filtered = true;
                }
            }

            return;
        }

        for (int rowIndex = 0; rowIndex < working.Count; rowIndex++)
        {
            WorkingRow row = working[rowIndex];
            if (row.Filtered)
            {
                continue;
            }

            FilterValue value;
            try
            {
                value = root.Evaluate(row.Row);
            }
            catch
            {
                row.Filtered = true;
                continue;
            }

            if (!FilterValueToBoolean(value))
            {
                row.Filtered = true;
            }
        }
    }

    private static FilterExpressionNode? TryCompileFilterExpression(DocTable derivedTable, string expression)
    {
        var lexer = new FilterLexer(expression);
        if (!lexer.TryTokenize(out List<FilterToken>? tokens))
        {
            return null;
        }

        var parser = new FilterParser(tokens!, derivedTable);
        if (!parser.TryParse(out FilterExpressionNode? node))
        {
            return null;
        }

        return node;
    }

    private static bool FilterValueToBoolean(FilterValue value)
    {
        return value.Kind switch
        {
            FilterValueKind.Bool => value.BoolValue,
            FilterValueKind.Number => Math.Abs(value.NumberValue) > double.Epsilon,
            FilterValueKind.String => !string.IsNullOrWhiteSpace(value.StringValue),
            _ => false,
        };
    }

    private static bool FilterValuesEqual(FilterValue left, FilterValue right)
    {
        if (left.Kind == FilterValueKind.Number && right.Kind == FilterValueKind.Number)
        {
            return left.NumberValue.Equals(right.NumberValue);
        }

        if (left.Kind == FilterValueKind.Bool && right.Kind == FilterValueKind.Bool)
        {
            return left.BoolValue == right.BoolValue;
        }

        return string.Equals(
            FilterValueToString(left),
            FilterValueToString(right),
            StringComparison.Ordinal);
    }

    private static string FilterValueToString(FilterValue value)
    {
        return value.Kind switch
        {
            FilterValueKind.String => value.StringValue ?? "",
            FilterValueKind.Bool => value.BoolValue ? "true" : "false",
            FilterValueKind.Number => value.NumberValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => "",
        };
    }

    private enum FilterValueKind
    {
        None,
        String,
        Number,
        Bool,
    }

    private readonly struct FilterValue
    {
        public FilterValue(
            FilterValueKind kind,
            string stringValue,
            double numberValue,
            bool boolValue)
        {
            Kind = kind;
            StringValue = stringValue;
            NumberValue = numberValue;
            BoolValue = boolValue;
        }

        public FilterValueKind Kind { get; }
        public string StringValue { get; }
        public double NumberValue { get; }
        public bool BoolValue { get; }

        public static FilterValue String(string value)
        {
            return new FilterValue(FilterValueKind.String, value, 0, false);
        }

        public static FilterValue Number(double value)
        {
            return new FilterValue(FilterValueKind.Number, "", value, false);
        }

        public static FilterValue Bool(bool value)
        {
            return new FilterValue(FilterValueKind.Bool, "", 0, value);
        }
    }

    private enum FilterTokenKind
    {
        End,
        Identifier,
        String,
        Number,
        True,
        False,
        Dot,
        LeftParen,
        RightParen,
        Bang,
        AndAnd,
        OrOr,
        EqualEqual,
        BangEqual,
    }

    private readonly struct FilterToken
    {
        public FilterToken(FilterTokenKind kind, string text, double numberValue)
        {
            Kind = kind;
            Text = text;
            NumberValue = numberValue;
        }

        public FilterTokenKind Kind { get; }
        public string Text { get; }
        public double NumberValue { get; }
    }

    private sealed class FilterLexer
    {
        private readonly string _expression;
        private readonly List<FilterToken> _tokens = new();
        private int _index;

        public FilterLexer(string expression)
        {
            _expression = expression;
        }

        public bool TryTokenize(out List<FilterToken>? tokens)
        {
            tokens = null;
            while (_index < _expression.Length)
            {
                char value = _expression[_index];
                if (char.IsWhiteSpace(value))
                {
                    _index++;
                    continue;
                }

                if (value == '.')
                {
                    _tokens.Add(new FilterToken(FilterTokenKind.Dot, ".", 0));
                    _index++;
                    continue;
                }

                if (value == '(')
                {
                    _tokens.Add(new FilterToken(FilterTokenKind.LeftParen, "(", 0));
                    _index++;
                    continue;
                }

                if (value == ')')
                {
                    _tokens.Add(new FilterToken(FilterTokenKind.RightParen, ")", 0));
                    _index++;
                    continue;
                }

                if (value == '!' && PeekChar(1) == '=')
                {
                    _tokens.Add(new FilterToken(FilterTokenKind.BangEqual, "!=", 0));
                    _index += 2;
                    continue;
                }

                if (value == '!' && PeekChar(1) != '=')
                {
                    _tokens.Add(new FilterToken(FilterTokenKind.Bang, "!", 0));
                    _index++;
                    continue;
                }

                if (value == '=' && PeekChar(1) == '=')
                {
                    _tokens.Add(new FilterToken(FilterTokenKind.EqualEqual, "==", 0));
                    _index += 2;
                    continue;
                }

                if (value == '&' && PeekChar(1) == '&')
                {
                    _tokens.Add(new FilterToken(FilterTokenKind.AndAnd, "&&", 0));
                    _index += 2;
                    continue;
                }

                if (value == '|' && PeekChar(1) == '|')
                {
                    _tokens.Add(new FilterToken(FilterTokenKind.OrOr, "||", 0));
                    _index += 2;
                    continue;
                }

                if (value == '"' && !TokenizeString())
                {
                    return false;
                }

                if (value == '"' )
                {
                    continue;
                }

                if (char.IsDigit(value) && !TokenizeNumber())
                {
                    return false;
                }

                if (char.IsDigit(value))
                {
                    continue;
                }

                if ((char.IsLetter(value) || value == '_') && !TokenizeIdentifier())
                {
                    return false;
                }

                if (char.IsLetter(value) || value == '_')
                {
                    continue;
                }

                return false;
            }

            _tokens.Add(new FilterToken(FilterTokenKind.End, "", 0));
            tokens = _tokens;
            return true;
        }

        private char PeekChar(int offset)
        {
            int nextIndex = _index + offset;
            if (nextIndex < 0 || nextIndex >= _expression.Length)
            {
                return '\0';
            }

            return _expression[nextIndex];
        }

        private bool TokenizeString()
        {
            _index++;
            var builder = new System.Text.StringBuilder();
            while (_index < _expression.Length)
            {
                char value = _expression[_index];
                if (value == '"')
                {
                    _tokens.Add(new FilterToken(FilterTokenKind.String, builder.ToString(), 0));
                    _index++;
                    return true;
                }

                if (value == '\\')
                {
                    _index++;
                    if (_index >= _expression.Length)
                    {
                        return false;
                    }

                    char escaped = _expression[_index];
                    builder.Append(escaped switch
                    {
                        '\\' => '\\',
                        '"' => '"',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => escaped,
                    });
                    _index++;
                    continue;
                }

                builder.Append(value);
                _index++;
            }

            return false;
        }

        private bool TokenizeNumber()
        {
            int start = _index;
            bool hasDot = false;
            while (_index < _expression.Length)
            {
                char value = _expression[_index];
                if (value == '.')
                {
                    if (hasDot)
                    {
                        return false;
                    }

                    hasDot = true;
                    _index++;
                    continue;
                }

                if (!char.IsDigit(value))
                {
                    break;
                }

                _index++;
            }

            string numberText = _expression.Substring(start, _index - start);
            if (!double.TryParse(numberText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                return false;
            }

            _tokens.Add(new FilterToken(FilterTokenKind.Number, numberText, parsed));
            return true;
        }

        private bool TokenizeIdentifier()
        {
            int start = _index;
            while (_index < _expression.Length)
            {
                char value = _expression[_index];
                if (char.IsLetterOrDigit(value) || value == '_')
                {
                    _index++;
                    continue;
                }

                break;
            }

            string identifier = _expression.Substring(start, _index - start);
            if (string.Equals(identifier, "true", StringComparison.OrdinalIgnoreCase))
            {
                _tokens.Add(new FilterToken(FilterTokenKind.True, identifier, 0));
                return true;
            }

            if (string.Equals(identifier, "false", StringComparison.OrdinalIgnoreCase))
            {
                _tokens.Add(new FilterToken(FilterTokenKind.False, identifier, 0));
                return true;
            }

            _tokens.Add(new FilterToken(FilterTokenKind.Identifier, identifier, 0));
            return true;
        }
    }

    private abstract class FilterExpressionNode
    {
        public abstract FilterValue Evaluate(DocRow row);
    }

    private sealed class FilterLiteralNode : FilterExpressionNode
    {
        private readonly FilterValue _value;

        public FilterLiteralNode(FilterValue value)
        {
            _value = value;
        }

        public override FilterValue Evaluate(DocRow row)
        {
            return _value;
        }
    }

    private sealed class FilterColumnNode : FilterExpressionNode
    {
        private readonly DocColumn _column;

        public FilterColumnNode(DocColumn column)
        {
            _column = column;
        }

        public override FilterValue Evaluate(DocRow row)
        {
            DocCellValue cell = row.GetCell(_column);
            return _column.Kind switch
            {
                DocColumnKind.Number => FilterValue.Number(cell.NumberValue),
                DocColumnKind.Formula => FilterValue.Number(cell.NumberValue),
                DocColumnKind.Checkbox => FilterValue.Bool(cell.BoolValue),
                _ => FilterValue.String(cell.StringValue ?? ""),
            };
        }
    }

    private sealed class FilterNotNode : FilterExpressionNode
    {
        private readonly FilterExpressionNode _operand;

        public FilterNotNode(FilterExpressionNode operand)
        {
            _operand = operand;
        }

        public override FilterValue Evaluate(DocRow row)
        {
            return FilterValue.Bool(!FilterValueToBoolean(_operand.Evaluate(row)));
        }
    }

    private enum FilterBinaryOperator
    {
        And,
        Or,
        Equals,
        NotEquals,
    }

    private sealed class FilterBinaryNode : FilterExpressionNode
    {
        private readonly FilterBinaryOperator _operator;
        private readonly FilterExpressionNode _left;
        private readonly FilterExpressionNode _right;

        public FilterBinaryNode(FilterBinaryOperator @operator, FilterExpressionNode left, FilterExpressionNode right)
        {
            _operator = @operator;
            _left = left;
            _right = right;
        }

        public override FilterValue Evaluate(DocRow row)
        {
            if (_operator == FilterBinaryOperator.And)
            {
                if (!FilterValueToBoolean(_left.Evaluate(row)))
                {
                    return FilterValue.Bool(false);
                }

                return FilterValue.Bool(FilterValueToBoolean(_right.Evaluate(row)));
            }

            if (_operator == FilterBinaryOperator.Or)
            {
                if (FilterValueToBoolean(_left.Evaluate(row)))
                {
                    return FilterValue.Bool(true);
                }

                return FilterValue.Bool(FilterValueToBoolean(_right.Evaluate(row)));
            }

            FilterValue leftValue = _left.Evaluate(row);
            FilterValue rightValue = _right.Evaluate(row);
            bool equals = FilterValuesEqual(leftValue, rightValue);
            return FilterValue.Bool(_operator == FilterBinaryOperator.Equals ? equals : !equals);
        }
    }

    private sealed class FilterParser
    {
        private readonly List<FilterToken> _tokens;
        private readonly Dictionary<string, DocColumn> _columnByName;
        private readonly Dictionary<string, DocColumn> _columnById;
        private int _index;

        public FilterParser(List<FilterToken> tokens, DocTable table)
        {
            _tokens = tokens;
            _columnByName = new Dictionary<string, DocColumn>(table.Columns.Count, StringComparer.OrdinalIgnoreCase);
            _columnById = new Dictionary<string, DocColumn>(table.Columns.Count, StringComparer.Ordinal);
            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                DocColumn column = table.Columns[columnIndex];
                if (!_columnByName.ContainsKey(column.Name))
                {
                    _columnByName[column.Name] = column;
                }

                _columnById[column.Id] = column;
            }
        }

        public bool TryParse(out FilterExpressionNode? expression)
        {
            expression = ParseOr();
            if (expression == null)
            {
                return false;
            }

            return Peek().Kind == FilterTokenKind.End;
        }

        private FilterExpressionNode? ParseOr()
        {
            FilterExpressionNode? left = ParseAnd();
            if (left == null)
            {
                return null;
            }

            while (Match(FilterTokenKind.OrOr))
            {
                FilterExpressionNode? right = ParseAnd();
                if (right == null)
                {
                    return null;
                }

                left = new FilterBinaryNode(FilterBinaryOperator.Or, left, right);
            }

            return left;
        }

        private FilterExpressionNode? ParseAnd()
        {
            FilterExpressionNode? left = ParseEquality();
            if (left == null)
            {
                return null;
            }

            while (Match(FilterTokenKind.AndAnd))
            {
                FilterExpressionNode? right = ParseEquality();
                if (right == null)
                {
                    return null;
                }

                left = new FilterBinaryNode(FilterBinaryOperator.And, left, right);
            }

            return left;
        }

        private FilterExpressionNode? ParseEquality()
        {
            FilterExpressionNode? left = ParseUnary();
            if (left == null)
            {
                return null;
            }

            while (true)
            {
                if (Match(FilterTokenKind.EqualEqual))
                {
                    FilterExpressionNode? right = ParseUnary();
                    if (right == null)
                    {
                        return null;
                    }

                    left = new FilterBinaryNode(FilterBinaryOperator.Equals, left, right);
                    continue;
                }

                if (Match(FilterTokenKind.BangEqual))
                {
                    FilterExpressionNode? right = ParseUnary();
                    if (right == null)
                    {
                        return null;
                    }

                    left = new FilterBinaryNode(FilterBinaryOperator.NotEquals, left, right);
                    continue;
                }

                return left;
            }
        }

        private FilterExpressionNode? ParseUnary()
        {
            if (Match(FilterTokenKind.Bang))
            {
                FilterExpressionNode? operand = ParseUnary();
                if (operand == null)
                {
                    return null;
                }

                return new FilterNotNode(operand);
            }

            return ParsePrimary();
        }

        private FilterExpressionNode? ParsePrimary()
        {
            if (Match(FilterTokenKind.LeftParen))
            {
                FilterExpressionNode? nested = ParseOr();
                if (nested == null || !Match(FilterTokenKind.RightParen))
                {
                    return null;
                }

                return nested;
            }

            if (Match(FilterTokenKind.True))
            {
                return new FilterLiteralNode(FilterValue.Bool(true));
            }

            if (Match(FilterTokenKind.False))
            {
                return new FilterLiteralNode(FilterValue.Bool(false));
            }

            if (Match(FilterTokenKind.Number))
            {
                FilterToken token = Previous();
                return new FilterLiteralNode(FilterValue.Number(token.NumberValue));
            }

            if (Match(FilterTokenKind.String))
            {
                FilterToken token = Previous();
                return new FilterLiteralNode(FilterValue.String(token.Text));
            }

            if (Match(FilterTokenKind.Identifier))
            {
                FilterToken token = Previous();
                if (!string.Equals(token.Text, "thisRow", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (!Match(FilterTokenKind.Dot) || !Match(FilterTokenKind.Identifier))
                {
                    return null;
                }

                string memberName = Previous().Text;
                if (!_columnByName.TryGetValue(memberName, out DocColumn? columnByName) &&
                    !_columnById.TryGetValue(memberName, out columnByName))
                {
                    return null;
                }

                return new FilterColumnNode(columnByName);
            }

            return null;
        }

        private bool Match(FilterTokenKind kind)
        {
            if (Peek().Kind != kind)
            {
                return false;
            }

            _index++;
            return true;
        }

        private FilterToken Peek()
        {
            if (_index >= _tokens.Count)
            {
                return _tokens[_tokens.Count - 1];
            }

            return _tokens[_index];
        }

        private FilterToken Previous()
        {
            return _tokens[Math.Max(0, _index - 1)];
        }
    }

    private static void CopyProjectedCells(
        DocRow outRow,
        DocRow sourceRow,
        DocTable sourceTable,
        string sourceTableId,
        DocDerivedConfig config)
    {
        for (int projIndex = 0; projIndex < config.Projections.Count; projIndex++)
        {
            var proj = config.Projections[projIndex];
            if (!string.Equals(proj.SourceTableId, sourceTableId, StringComparison.Ordinal))
            {
                continue;
            }

            DocColumn? sourceCol = null;
            for (int colIndex = 0; colIndex < sourceTable.Columns.Count; colIndex++)
            {
                if (string.Equals(sourceTable.Columns[colIndex].Id, proj.SourceColumnId, StringComparison.Ordinal))
                {
                    sourceCol = sourceTable.Columns[colIndex];
                    break;
                }
            }

            if (sourceCol == null)
            {
                continue;
            }

            outRow.SetCell(proj.OutputColumnId, sourceRow.GetCell(sourceCol));
        }
    }

    private static DerivedMatchState CombineState(DerivedMatchState current, DerivedMatchState next)
    {
        if (current == DerivedMatchState.TypeMismatch || next == DerivedMatchState.TypeMismatch) return DerivedMatchState.TypeMismatch;
        if (current == DerivedMatchState.MultiMatch || next == DerivedMatchState.MultiMatch) return DerivedMatchState.MultiMatch;
        if (current == DerivedMatchState.NoMatch || next == DerivedMatchState.NoMatch) return DerivedMatchState.NoMatch;
        return DerivedMatchState.Matched;
    }

    private readonly struct KeyAtom : IEquatable<KeyAtom>
    {
        private readonly byte _kind; // 0=string,1=number,2=bool
        private readonly string? _s;
        private readonly long _i64;
        private readonly double _d;

        private KeyAtom(byte kind, string? s, long i64, double d)
        {
            _kind = kind;
            _s = s;
            _i64 = i64;
            _d = d;
        }

        public static KeyAtom FromCell(DocCellValue cell, DocColumnKind kind)
        {
            switch (kind)
            {
                case DocColumnKind.Number:
                case DocColumnKind.Formula:
                    return new KeyAtom(1, null, 0, cell.NumberValue);
                case DocColumnKind.Checkbox:
                    return new KeyAtom(2, null, cell.BoolValue ? 1 : 0, 0);
                default:
                    return new KeyAtom(0, cell.StringValue ?? "", 0, 0);
            }
        }

        public bool Equals(KeyAtom other)
        {
            if (_kind != other._kind) return false;
            return _kind switch
            {
                0 => string.Equals(_s, other._s, StringComparison.Ordinal),
                1 => _d.Equals(other._d),
                2 => _i64 == other._i64,
                _ => false
            };
        }

        public override bool Equals(object? obj) => obj is KeyAtom other && Equals(other);

        public override int GetHashCode()
        {
            return _kind switch
            {
                0 => HashCode.Combine(_kind, _s != null ? _s.GetHashCode(StringComparison.Ordinal) : 0),
                1 => HashCode.Combine(_kind, BitConverter.DoubleToInt64Bits(_d)),
                _ => HashCode.Combine(_kind, _i64)
            };
        }
    }

    private readonly struct JoinKey1 : IEquatable<JoinKey1>
    {
        public readonly KeyAtom A;
        public JoinKey1(KeyAtom a) { A = a; }
        public bool Equals(JoinKey1 other) => A.Equals(other.A);
        public override bool Equals(object? obj) => obj is JoinKey1 other && Equals(other);
        public override int GetHashCode() => A.GetHashCode();
    }

    private readonly struct JoinKey2 : IEquatable<JoinKey2>
    {
        public readonly KeyAtom A;
        public readonly KeyAtom B;
        public JoinKey2(KeyAtom a, KeyAtom b) { A = a; B = b; }
        public bool Equals(JoinKey2 other) => A.Equals(other.A) && B.Equals(other.B);
        public override bool Equals(object? obj) => obj is JoinKey2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(A.GetHashCode(), B.GetHashCode());
    }

    private readonly struct JoinKey3 : IEquatable<JoinKey3>
    {
        public readonly KeyAtom A;
        public readonly KeyAtom B;
        public readonly KeyAtom C;
        public JoinKey3(KeyAtom a, KeyAtom b, KeyAtom c) { A = a; B = b; C = c; }
        public bool Equals(JoinKey3 other) => A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C);
        public override bool Equals(object? obj) => obj is JoinKey3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(A.GetHashCode(), B.GetHashCode(), C.GetHashCode());
    }

    private static Dictionary<JoinKey1, int> BuildIndex1(DocTable sourceTable, DerivedStep step, Dictionary<string, DocColumn> sourceColumnById)
    {
        var mapping = step.KeyMappings[0];
        var col = sourceColumnById[mapping.SourceColumnId];
        var index = new Dictionary<JoinKey1, int>(sourceTable.Rows.Count);

        for (int i = 0; i < sourceTable.Rows.Count; i++)
        {
            var row = sourceTable.Rows[i];
            var key = new JoinKey1(KeyAtom.FromCell(row.GetCell(col), col.Kind));
            if (index.TryGetValue(key, out int existing))
            {
                if (existing != -1) index[key] = -1;
            }
            else
            {
                index.Add(key, i);
            }
        }

        return index;
    }

    private static Dictionary<JoinKey2, int> BuildIndex2(DocTable sourceTable, DerivedStep step, Dictionary<string, DocColumn> sourceColumnById)
    {
        var m0 = step.KeyMappings[0];
        var m1 = step.KeyMappings[1];
        var c0 = sourceColumnById[m0.SourceColumnId];
        var c1 = sourceColumnById[m1.SourceColumnId];
        var index = new Dictionary<JoinKey2, int>(sourceTable.Rows.Count);

        for (int i = 0; i < sourceTable.Rows.Count; i++)
        {
            var row = sourceTable.Rows[i];
            var key = new JoinKey2(
                KeyAtom.FromCell(row.GetCell(c0), c0.Kind),
                KeyAtom.FromCell(row.GetCell(c1), c1.Kind));
            if (index.TryGetValue(key, out int existing))
            {
                if (existing != -1) index[key] = -1;
            }
            else
            {
                index.Add(key, i);
            }
        }

        return index;
    }

    private static Dictionary<JoinKey3, int> BuildIndex3(DocTable sourceTable, DerivedStep step, Dictionary<string, DocColumn> sourceColumnById)
    {
        var m0 = step.KeyMappings[0];
        var m1 = step.KeyMappings[1];
        var m2 = step.KeyMappings[2];
        var c0 = sourceColumnById[m0.SourceColumnId];
        var c1 = sourceColumnById[m1.SourceColumnId];
        var c2 = sourceColumnById[m2.SourceColumnId];
        var index = new Dictionary<JoinKey3, int>(sourceTable.Rows.Count);

        for (int i = 0; i < sourceTable.Rows.Count; i++)
        {
            var row = sourceTable.Rows[i];
            var key = new JoinKey3(
                KeyAtom.FromCell(row.GetCell(c0), c0.Kind),
                KeyAtom.FromCell(row.GetCell(c1), c1.Kind),
                KeyAtom.FromCell(row.GetCell(c2), c2.Kind));
            if (index.TryGetValue(key, out int existing))
            {
                if (existing != -1) index[key] = -1;
            }
            else
            {
                index.Add(key, i);
            }
        }

        return index;
    }

    private static DerivedMatchState FindJoinMatch(
        Dictionary<string, DocColumn> derivedColumnById,
        Dictionary<string, DocColumn> sourceColumnById,
        DerivedStep step,
        DocRow leftRow,
        Dictionary<JoinKey1, int>? index1,
        Dictionary<JoinKey2, int>? index2,
        Dictionary<JoinKey3, int>? index3,
        out int matchedSourceRowIndex)
    {
        matchedSourceRowIndex = -1;

        int keyCount = step.KeyMappings.Count;
        if (keyCount <= 0 || keyCount > 3)
        {
            return DerivedMatchState.TypeMismatch;
        }

        // Build key atoms from the left (current output) row and validate type compatibility.
        KeyAtom a;
        KeyAtom b;
        KeyAtom c;

        var m0 = step.KeyMappings[0];
        if (!derivedColumnById.TryGetValue(m0.BaseColumnId, out var leftCol0))
        {
            return DerivedMatchState.TypeMismatch;
        }
        if (!sourceColumnById.TryGetValue(m0.SourceColumnId, out var srcCol0))
        {
            return DerivedMatchState.TypeMismatch;
        }
        if (!AreKeyKindsCompatible(leftCol0.Kind, srcCol0.Kind)) return DerivedMatchState.TypeMismatch;
        a = KeyAtom.FromCell(leftRow.GetCell(leftCol0), leftCol0.Kind);

        if (keyCount == 1)
        {
            var key = new JoinKey1(a);
            if (index1 == null) return DerivedMatchState.TypeMismatch;
            if (!index1.TryGetValue(key, out int idx)) return DerivedMatchState.NoMatch;
            if (idx == -1) return DerivedMatchState.MultiMatch;
            matchedSourceRowIndex = idx;
            return DerivedMatchState.Matched;
        }

        var m1 = step.KeyMappings[1];
        if (!derivedColumnById.TryGetValue(m1.BaseColumnId, out var leftCol1))
        {
            return DerivedMatchState.TypeMismatch;
        }
        if (!sourceColumnById.TryGetValue(m1.SourceColumnId, out var srcCol1))
        {
            return DerivedMatchState.TypeMismatch;
        }
        if (!AreKeyKindsCompatible(leftCol1.Kind, srcCol1.Kind)) return DerivedMatchState.TypeMismatch;
        b = KeyAtom.FromCell(leftRow.GetCell(leftCol1), leftCol1.Kind);

        if (keyCount == 2)
        {
            var key = new JoinKey2(a, b);
            if (index2 == null) return DerivedMatchState.TypeMismatch;
            if (!index2.TryGetValue(key, out int idx)) return DerivedMatchState.NoMatch;
            if (idx == -1) return DerivedMatchState.MultiMatch;
            matchedSourceRowIndex = idx;
            return DerivedMatchState.Matched;
        }

        var m2 = step.KeyMappings[2];
        if (!derivedColumnById.TryGetValue(m2.BaseColumnId, out var leftCol2))
        {
            return DerivedMatchState.TypeMismatch;
        }
        if (!sourceColumnById.TryGetValue(m2.SourceColumnId, out var srcCol2))
        {
            return DerivedMatchState.TypeMismatch;
        }
        if (!AreKeyKindsCompatible(leftCol2.Kind, srcCol2.Kind)) return DerivedMatchState.TypeMismatch;
        c = KeyAtom.FromCell(leftRow.GetCell(leftCol2), leftCol2.Kind);

        var key3 = new JoinKey3(a, b, c);
        if (index3 == null) return DerivedMatchState.TypeMismatch;
        if (!index3.TryGetValue(key3, out int idx3)) return DerivedMatchState.NoMatch;
        if (idx3 == -1) return DerivedMatchState.MultiMatch;
        matchedSourceRowIndex = idx3;
        return DerivedMatchState.Matched;
    }

    private static bool AreKeyKindsCompatible(DocColumnKind left, DocColumnKind right)
    {
        // Join keys are strict and type-aware. Allow Formula to behave as Number.
        DocColumnKind nl = left == DocColumnKind.Formula ? DocColumnKind.Number : left;
        DocColumnKind nr = right == DocColumnKind.Formula ? DocColumnKind.Number : right;
        return nl == nr;
    }

    private static string EncodeRowId(OutRowKey key)
    {
        // Compact, stable, human-readable. TableId/RowId are GUID-like strings in practice.
        // (Do not use '_' because existing ids used it and it is ambiguous without escaping.)
        return key.TableId + ":" + (key.RowId ?? "");
    }
}
