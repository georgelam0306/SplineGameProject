using System;
using System.Collections.Generic;
using Core;
using DerpLib.Ecs.Editor;
using FixedMath;
using Property;

namespace DerpLib.Ecs.Editor.Smoke;

/// <summary>
/// Smoke tests for the Expression DSL pipeline:
/// Lexer → Parser → AST → Type Check → SSA Flatten → Evaluate.
/// Plus binding system toposort + propagation.
/// </summary>
internal static class SmokeExpressionDsl
{
    public static void Touch()
    {
        TestLexer();
        TestParseAndEvaluateSimple();
        TestParseAndEvaluateComplex();
        TestTernary();
        TestFunctions();
        TestIntFloatPromotion();
        TestBooleanLogic();
        TestBindingToposort();
        TestBindingCycleDetection();
        TestBindingChainPropagation();
        TestStringLiterals();
        TestFixed64Arithmetic();
    }

    private static void TestLexer()
    {
        var lexer = new ExpressionLexer("@health / @maxHealth * 100 + clamp(@shield, 0, 50)".AsMemory());
        var tokens = new List<TokenKind>();
        while (true)
        {
            var t = lexer.NextToken();
            tokens.Add(t.Kind);
            if (t.Kind == TokenKind.End) break;
        }

        Assert(tokens.Count == 16, "Lexer: expected 16 tokens (15 + End)");
        Assert(tokens[0] == TokenKind.Variable, "Lexer: first token should be Variable");
        Assert(tokens[1] == TokenKind.Slash, "Lexer: second token should be Slash");
        Assert(tokens[4] == TokenKind.IntLiteral, "Lexer: '100' should be IntLiteral");
    }

    private static void TestParseAndEvaluateSimple()
    {
        // Parse: @health / @maxHealth
        var vars = new Dictionary<string, int> { ["health"] = 0, ["maxHealth"] = 1 };
        var parser = new ExpressionParser("@health / @maxHealth".AsMemory(), vars);
        var result = parser.Parse();
        Assert(!result.HasErrors, "Simple: parse should succeed without errors");

        // Type check
        var sourceKinds = new[] { PropertyKind.Float, PropertyKind.Float };
        var typeErrors = ExpressionTypeChecker.Check(result.Nodes.AsSpan(), sourceKinds);
        Assert(typeErrors.Length == 0, "Simple: type check should pass");

        // Flatten
        var flat = ExpressionFlattener.Flatten(result.Nodes.AsSpan(), result.RootIndex);
        Assert(flat.IsValid, "Simple: flat expression should be valid");

        // Evaluate with (75, 100) → 0.75
        ReadOnlySpan<PropertyValue> sources = stackalloc PropertyValue[]
        {
            PropertyValue.FromFloat(75f),
            PropertyValue.FromFloat(100f),
        };
        var value = ExpressionEvaluator.Evaluate(flat, sources);
        AssertFloat(value.Float, 0.75f, "Simple: 75/100 should equal 0.75");
    }

    private static void TestParseAndEvaluateComplex()
    {
        // Parse: @health / @maxHealth * 100 + clamp(@shield, 0, 50)
        var vars = new Dictionary<string, int> { ["health"] = 0, ["maxHealth"] = 1, ["shield"] = 2 };
        var parser = new ExpressionParser("@health / @maxHealth * 100 + clamp(@shield, 0, 50)".AsMemory(), vars);
        var result = parser.Parse();
        Assert(!result.HasErrors, "Complex: parse should succeed");

        var sourceKinds = new[] { PropertyKind.Float, PropertyKind.Float, PropertyKind.Float };
        var typeErrors = ExpressionTypeChecker.Check(result.Nodes.AsSpan(), sourceKinds);
        Assert(typeErrors.Length == 0, "Complex: type check should pass");

        var flat = ExpressionFlattener.Flatten(result.Nodes.AsSpan(), result.RootIndex);

        ReadOnlySpan<PropertyValue> sources = stackalloc PropertyValue[]
        {
            PropertyValue.FromFloat(75f),
            PropertyValue.FromFloat(100f),
            PropertyValue.FromFloat(80f),
        };
        var value = ExpressionEvaluator.Evaluate(flat, sources);
        // 75/100 = 0.75, 0.75*100 = 75, clamp(80, 0, 50) = 50, total = 125
        AssertFloat(value.Float, 125f, "Complex: expected 125");
    }

    private static void TestTernary()
    {
        var vars = new Dictionary<string, int> { ["x"] = 0 };
        var parser = new ExpressionParser("@x > 10 ? 1.0 : 0.0".AsMemory(), vars);
        var result = parser.Parse();
        Assert(!result.HasErrors, "Ternary: parse should succeed");

        var sourceKinds = new[] { PropertyKind.Float };
        ExpressionTypeChecker.Check(result.Nodes.AsSpan(), sourceKinds);
        var flat = ExpressionFlattener.Flatten(result.Nodes.AsSpan(), result.RootIndex);

        // x = 15 > 10 → 1.0
        ReadOnlySpan<PropertyValue> sources1 = stackalloc PropertyValue[] { PropertyValue.FromFloat(15f) };
        AssertFloat(ExpressionEvaluator.Evaluate(flat, sources1).Float, 1.0f, "Ternary: 15>10 should be 1.0");

        // x = 5 ≤ 10 → 0.0
        ReadOnlySpan<PropertyValue> sources2 = stackalloc PropertyValue[] { PropertyValue.FromFloat(5f) };
        AssertFloat(ExpressionEvaluator.Evaluate(flat, sources2).Float, 0.0f, "Ternary: 5>10 should be 0.0");
    }

    private static void TestFunctions()
    {
        // lerp(0, 100, 0.5) → 50
        var parser = new ExpressionParser("lerp(0.0, 100.0, 0.5)".AsMemory());
        var result = parser.Parse();
        Assert(!result.HasErrors, "Functions: lerp parse should succeed");

        ExpressionTypeChecker.Check(result.Nodes.AsSpan(), ReadOnlySpan<PropertyKind>.Empty);
        var flat = ExpressionFlattener.Flatten(result.Nodes.AsSpan(), result.RootIndex);
        var value = ExpressionEvaluator.Evaluate(flat, ReadOnlySpan<PropertyValue>.Empty);
        AssertFloat(value.Float, 50f, "Functions: lerp(0,100,0.5) should equal 50");

        // min(3.0, 7.0) → 3
        parser = new ExpressionParser("min(3.0, 7.0)".AsMemory());
        result = parser.Parse();
        Assert(!result.HasErrors, "Functions: min parse should succeed");

        ExpressionTypeChecker.Check(result.Nodes.AsSpan(), ReadOnlySpan<PropertyKind>.Empty);
        flat = ExpressionFlattener.Flatten(result.Nodes.AsSpan(), result.RootIndex);
        value = ExpressionEvaluator.Evaluate(flat, ReadOnlySpan<PropertyValue>.Empty);
        AssertFloat(value.Float, 3f, "Functions: min(3,7) should equal 3");

        // abs(-5.0) → 5
        parser = new ExpressionParser("abs(-5.0)".AsMemory());
        result = parser.Parse();
        Assert(!result.HasErrors, "Functions: abs parse should succeed");

        ExpressionTypeChecker.Check(result.Nodes.AsSpan(), ReadOnlySpan<PropertyKind>.Empty);
        flat = ExpressionFlattener.Flatten(result.Nodes.AsSpan(), result.RootIndex);
        value = ExpressionEvaluator.Evaluate(flat, ReadOnlySpan<PropertyValue>.Empty);
        AssertFloat(value.Float, 5f, "Functions: abs(-5) should equal 5");
    }

    private static void TestIntFloatPromotion()
    {
        // 3 + 2.5 → 5.5 (Int promoted to Float)
        var parser = new ExpressionParser("3 + 2.5".AsMemory());
        var result = parser.Parse();
        Assert(!result.HasErrors, "Promotion: parse should succeed");

        ExpressionTypeChecker.Check(result.Nodes.AsSpan(), ReadOnlySpan<PropertyKind>.Empty);
        Assert(result.Nodes[result.RootIndex].ResultType == PropertyKind.Float,
            "Promotion: result should be Float");

        var flat = ExpressionFlattener.Flatten(result.Nodes.AsSpan(), result.RootIndex);
        var value = ExpressionEvaluator.Evaluate(flat, ReadOnlySpan<PropertyValue>.Empty);
        AssertFloat(value.Float, 5.5f, "Promotion: 3 + 2.5 should equal 5.5");
    }

    private static void TestBooleanLogic()
    {
        var vars = new Dictionary<string, int> { ["a"] = 0, ["b"] = 1 };
        var parser = new ExpressionParser("@a && !@b".AsMemory(), vars);
        var result = parser.Parse();
        Assert(!result.HasErrors, "Boolean: parse should succeed");

        var sourceKinds = new[] { PropertyKind.Bool, PropertyKind.Bool };
        ExpressionTypeChecker.Check(result.Nodes.AsSpan(), sourceKinds);
        var flat = ExpressionFlattener.Flatten(result.Nodes.AsSpan(), result.RootIndex);

        // true && !false → true
        ReadOnlySpan<PropertyValue> sources = stackalloc PropertyValue[]
        {
            PropertyValue.FromBool(true),
            PropertyValue.FromBool(false),
        };
        Assert(ExpressionEvaluator.Evaluate(flat, sources).Bool, "Boolean: true && !false should be true");
    }

    private static void TestBindingToposort()
    {
        // Binding A: sources=[] → target=P1
        // Binding B: sources=[P1] → target=P2
        // Expected order: A before B
        var pathP1 = new ExpressionBindingPath(1, 100, 1000, 0, PropertyKind.Float);
        var pathP2 = new ExpressionBindingPath(1, 100, 2000, 1, PropertyKind.Float);

        var bindingA = new ExpressionBindingDefinition
        {
            Sources = Array.Empty<ExpressionBindingSource>(),
            Target = pathP1,
            ExpressionText = "42.0",
        };

        var bindingB = new ExpressionBindingDefinition
        {
            Sources = new[] { new ExpressionBindingSource(pathP1, "x") },
            Target = pathP2,
            ExpressionText = "@x * 2.0",
        };

        var sorted = ExpressionBindingGraph.Toposort(new[] { bindingB, bindingA });
        Assert(sorted != null, "Toposort: should succeed (no cycle)");
        // bindingA is index 1 in input, bindingB is index 0
        // After toposort, A (index 1) should come before B (index 0)
        Assert(sorted![0] == 1 && sorted[1] == 0, "Toposort: A should come before B");
    }

    private static void TestBindingCycleDetection()
    {
        var pathP1 = new ExpressionBindingPath(1, 100, 1000, 0, PropertyKind.Float);
        var pathP2 = new ExpressionBindingPath(1, 100, 2000, 1, PropertyKind.Float);

        // A reads P2, writes P1; B reads P1, writes P2 → cycle
        var bindingA = new ExpressionBindingDefinition
        {
            Sources = new[] { new ExpressionBindingSource(pathP2, "y") },
            Target = pathP1,
            ExpressionText = "@y + 1.0",
        };

        var bindingB = new ExpressionBindingDefinition
        {
            Sources = new[] { new ExpressionBindingSource(pathP1, "x") },
            Target = pathP2,
            ExpressionText = "@x + 1.0",
        };

        var sorted = ExpressionBindingGraph.Toposort(new[] { bindingA, bindingB });
        Assert(sorted == null, "Cycle: toposort should return null for cyclic bindings");
    }

    private static void TestBindingChainPropagation()
    {
        // Source property P0 = 10.0 (external, not a binding target)
        // Binding A: reads P0, writes P1 = @p0 * 2 → 20.0
        // Binding B: reads P1, writes P2 = @p1 + 5 → 25.0
        var pathP0 = new ExpressionBindingPath(1, 100, 1, 0, PropertyKind.Float);
        var pathP1 = new ExpressionBindingPath(1, 100, 2, 1, PropertyKind.Float);
        var pathP2 = new ExpressionBindingPath(1, 100, 3, 2, PropertyKind.Float);

        var bindingA = new ExpressionBindingDefinition
        {
            Sources = new[] { new ExpressionBindingSource(pathP0, "p0") },
            Target = pathP1,
            ExpressionText = "@p0 * 2.0",
        };

        var bindingB = new ExpressionBindingDefinition
        {
            Sources = new[] { new ExpressionBindingSource(pathP1, "p1") },
            Target = pathP2,
            ExpressionText = "@p1 + 5.0",
        };

        var evaluator = new ExpressionBindingEvaluator();
        bool ok = evaluator.SetBindings(new[] { bindingB, bindingA }); // deliberately out of order
        Assert(ok, "Chain: SetBindings should succeed");
        Assert(evaluator.BindingCount == 2, "Chain: should have 2 bindings");

        var store = new DictionaryPropertyStore();
        store.Set(pathP0, PropertyValue.FromFloat(10f));
        store.Set(pathP1, PropertyValue.FromFloat(0f));
        store.Set(pathP2, PropertyValue.FromFloat(0f));

        evaluator.EvaluateAll(store, store);

        AssertFloat(store.Get(pathP1).Float, 20f, "Chain: P1 should be 20");
        AssertFloat(store.Get(pathP2).Float, 25f, "Chain: P2 should be 25");
    }

    private static void TestStringLiterals()
    {
        // String literal should be interned as StringHandle and comparable
        StringHandle expected = "hello";
        var result = EvalExpr("\"hello\"", Array.Empty<string>(), Array.Empty<PropertyKind>(), Array.Empty<PropertyValue>());
        Assert(result.StringHandle == expected, $"String literal: expected StringHandle 'hello', got '{(string)result.StringHandle}'");

        // Ternary with string branches
        var vars = new Dictionary<string, int> { { "flag", 0 } };
        var sourceKinds = new PropertyKind[] { PropertyKind.Bool };

        var trueResult = EvalExpr("@flag ? \"yes\" : \"no\"", vars, sourceKinds, new[] { PropertyValue.FromBool(true) });
        StringHandle yes = "yes";
        Assert(trueResult.StringHandle == yes, "String ternary true branch");

        var falseResult = EvalExpr("@flag ? \"yes\" : \"no\"", vars, sourceKinds, new[] { PropertyValue.FromBool(false) });
        StringHandle no = "no";
        Assert(falseResult.StringHandle == no, "String ternary false branch");

        // String equality
        var eqResult = EvalExpr("@tag == \"hero\"", new Dictionary<string, int> { { "tag", 0 } },
            new PropertyKind[] { PropertyKind.StringHandle }, new[] { PropertyValue.FromStringHandle((StringHandle)"hero") });
        Assert(eqResult.Bool, "String equality: should be true");
    }

    private static void TestFixed64Arithmetic()
    {
        // Fixed64 addition via source variables
        var vars = new Dictionary<string, int> { { "a", 0 }, { "b", 1 } };
        var sourceKinds = new PropertyKind[] { PropertyKind.Fixed64, PropertyKind.Fixed64 };
        var sourceValues = new[]
        {
            PropertyValue.FromFixed64(Fixed64.FromInt(10)),
            PropertyValue.FromFixed64(Fixed64.FromInt(3)),
        };

        var addResult = EvalExpr("@a + @b", vars, sourceKinds, sourceValues);
        Assert(addResult.Fixed64 == Fixed64.FromInt(13), $"Fixed64 add: expected 13, got {addResult.Fixed64}");

        var subResult = EvalExpr("@a - @b", vars, sourceKinds, sourceValues);
        Assert(subResult.Fixed64 == Fixed64.FromInt(7), $"Fixed64 sub: expected 7, got {subResult.Fixed64}");

        var mulResult = EvalExpr("@a * @b", vars, sourceKinds, sourceValues);
        Assert(mulResult.Fixed64 == Fixed64.FromInt(30), $"Fixed64 mul: expected 30, got {mulResult.Fixed64}");

        // Fixed64 comparison
        var ltResult = EvalExpr("@a < @b", vars, sourceKinds, sourceValues);
        Assert(!ltResult.Bool, "Fixed64 less: 10 < 3 should be false");

        var gtResult = EvalExpr("@a > @b", vars, sourceKinds, sourceValues);
        Assert(gtResult.Bool, "Fixed64 greater: 10 > 3 should be true");

        // Fixed64 negate
        var negResult = EvalExpr("-@a", vars, sourceKinds, sourceValues);
        Assert(negResult.Fixed64 == Fixed64.FromInt(-10), $"Fixed64 negate: expected -10, got {negResult.Fixed64}");

        // Fixed64 min/max/clamp
        var minResult = EvalExpr("min(@a, @b)", vars, sourceKinds, sourceValues);
        Assert(minResult.Fixed64 == Fixed64.FromInt(3), $"Fixed64 min: expected 3, got {minResult.Fixed64}");

        var maxResult = EvalExpr("max(@a, @b)", vars, sourceKinds, sourceValues);
        Assert(maxResult.Fixed64 == Fixed64.FromInt(10), $"Fixed64 max: expected 10, got {maxResult.Fixed64}");

        // Fixed64Vec2 addition
        var vec2Vars = new Dictionary<string, int> { { "p", 0 }, { "q", 1 } };
        var vec2Kinds = new PropertyKind[] { PropertyKind.Fixed64Vec2, PropertyKind.Fixed64Vec2 };
        var vec2Values = new[]
        {
            PropertyValue.FromFixed64Vec2(Fixed64Vec2.FromInt(1, 2)),
            PropertyValue.FromFixed64Vec2(Fixed64Vec2.FromInt(3, 4)),
        };

        var vec2Add = EvalExpr("@p + @q", vec2Vars, vec2Kinds, vec2Values);
        Assert(vec2Add.Fixed64Vec2 == Fixed64Vec2.FromInt(4, 6), $"Fixed64Vec2 add: expected (4,6), got {vec2Add.Fixed64Vec2}");
    }

    /// <summary>Helper to parse, type-check, flatten, and evaluate an expression.</summary>
    private static PropertyValue EvalExpr(string expr, Dictionary<string, int> vars,
        PropertyKind[] sourceKinds, PropertyValue[] sourceValues)
    {
        var varMap = vars as Dictionary<string, int> ?? new Dictionary<string, int>();
        var parser = new ExpressionParser(expr.AsMemory(), varMap);
        var parseResult = parser.Parse();
        Assert(parseResult.Diagnostics.Length == 0,
            $"Parse errors in '{expr}': {(parseResult.Diagnostics.Length > 0 ? parseResult.Diagnostics[0].Message : "")}");
        var errors = ExpressionTypeChecker.Check(parseResult.Nodes, sourceKinds);
        Assert(errors.Length == 0,
            $"Type errors in '{expr}': {(errors.Length > 0 ? errors[0].Message : "")}");
        var flat = ExpressionFlattener.Flatten(parseResult.Nodes, parseResult.RootIndex);
        return ExpressionEvaluator.Evaluate(in flat, sourceValues);
    }

    private static PropertyValue EvalExpr(string expr, string[] varNames, PropertyKind[] sourceKinds, PropertyValue[] sourceValues)
    {
        var vars = new Dictionary<string, int>();
        for (int i = 0; i < varNames.Length; i++)
            vars[varNames[i]] = i;
        return EvalExpr(expr, vars, sourceKinds, sourceValues);
    }

    // ─── Helpers ───

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"Expression DSL smoke test failed: {message}");
    }

    private static void AssertFloat(float actual, float expected, string message, float tolerance = 0.001f)
    {
        if (MathF.Abs(actual - expected) > tolerance)
            throw new Exception($"Expression DSL smoke test failed: {message} (expected {expected}, got {actual})");
    }

    /// <summary>Simple in-memory property store for testing bindings.</summary>
    private sealed class DictionaryPropertyStore : IBindingPropertyReader, IBindingPropertyWriter
    {
        private readonly Dictionary<ExpressionBindingPath, PropertyValue> _values = new();

        public void Set(ExpressionBindingPath path, PropertyValue value) => _values[path] = value;
        public PropertyValue Get(ExpressionBindingPath path) => _values.TryGetValue(path, out var v) ? v : default;

        public PropertyValue ReadProperty(in ExpressionBindingPath path) => Get(path);
        public void WriteProperty(in ExpressionBindingPath path, in PropertyValue value) => Set(path, value);
    }
}
