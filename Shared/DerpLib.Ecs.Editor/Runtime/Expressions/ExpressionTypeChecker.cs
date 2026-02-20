using System;
using System.Collections.Generic;
using Property;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Single forward pass over <see cref="ExpressionNode"/>[] (bottom-up since children have lower indices).
/// Annotates <see cref="ExpressionNode.ResultType"/> on each node, validates operand type compatibility,
/// and handles implicit Int→Float promotion.
/// </summary>
public static class ExpressionTypeChecker
{
    /// <summary>
    /// Type-checks the AST nodes in-place. Returns diagnostics (empty if valid).
    /// </summary>
    /// <param name="nodes">AST nodes array (mutated: ResultType is filled in).</param>
    /// <param name="sourceKinds">PropertyKind for each source variable.</param>
    public static ExpressionDiagnostic[] Check(Span<ExpressionNode> nodes, ReadOnlySpan<PropertyKind> sourceKinds)
    {
        List<ExpressionDiagnostic>? errors = null;

        for (int i = 0; i < nodes.Length; i++)
        {
            ref var node = ref nodes[i];

            switch (node.Kind)
            {
                case ExpressionKind.FloatLiteral:
                    node.ResultType = PropertyKind.Float;
                    break;

                case ExpressionKind.IntLiteral:
                    node.ResultType = PropertyKind.Int;
                    break;

                case ExpressionKind.BoolLiteral:
                    node.ResultType = PropertyKind.Bool;
                    break;

                case ExpressionKind.StringLiteral:
                    node.ResultType = PropertyKind.StringHandle;
                    break;

                case ExpressionKind.ColorLiteral:
                    node.ResultType = PropertyKind.Color32;
                    break;

                case ExpressionKind.Variable:
                {
                    if (node.SourceIndex >= 0 && node.SourceIndex < sourceKinds.Length)
                        node.ResultType = sourceKinds[node.SourceIndex];
                    else
                    {
                        node.ResultType = PropertyKind.Float; // default fallback
                        AddError(ref errors, node, "Variable has invalid source index");
                    }
                    break;
                }

                case ExpressionKind.Add:
                case ExpressionKind.Subtract:
                {
                    var (leftType, rightType) = GetBinaryTypes(nodes, node);
                    var promoted = PromoteArithmetic(leftType, rightType);
                    if (promoted == PropertyKind.Auto)
                    {
                        AddError(ref errors, node, $"Cannot apply '{node.Kind}' to {leftType} and {rightType}");
                        node.ResultType = PropertyKind.Float;
                    }
                    else
                        node.ResultType = promoted;
                    break;
                }

                case ExpressionKind.Multiply:
                case ExpressionKind.Divide:
                {
                    var (leftType, rightType) = GetBinaryTypes(nodes, node);

                    // Vec * Scalar or Scalar * Vec → Vec (scalar broadcast)
                    // Handles both Float Vec2/3/4 and Fixed64 Fixed64Vec2/3
                    if (IsVector(leftType) && ScalarOf(leftType) == rightType)
                    {
                        node.ResultType = leftType;
                        break;
                    }
                    if (IsVector(rightType) && ScalarOf(rightType) == leftType)
                    {
                        node.ResultType = rightType;
                        break;
                    }

                    var promoted = PromoteArithmetic(leftType, rightType);
                    if (promoted == PropertyKind.Auto)
                    {
                        AddError(ref errors, node, $"Cannot apply '{node.Kind}' to {leftType} and {rightType}");
                        node.ResultType = PropertyKind.Float;
                    }
                    else
                        node.ResultType = promoted;
                    break;
                }

                case ExpressionKind.Equal:
                case ExpressionKind.NotEqual:
                {
                    // These accept any pair where types are compatible
                    node.ResultType = PropertyKind.Bool;
                    break;
                }

                case ExpressionKind.Less:
                case ExpressionKind.Greater:
                case ExpressionKind.LessEqual:
                case ExpressionKind.GreaterEqual:
                {
                    var (leftType, rightType) = GetBinaryTypes(nodes, node);
                    if (!IsNumeric(leftType) || !IsNumeric(rightType))
                        AddError(ref errors, node, $"Comparison requires numeric types, got {leftType} and {rightType}");
                    node.ResultType = PropertyKind.Bool;
                    break;
                }

                case ExpressionKind.And:
                case ExpressionKind.Or:
                {
                    var (leftType, rightType) = GetBinaryTypes(nodes, node);
                    if (leftType != PropertyKind.Bool || rightType != PropertyKind.Bool)
                        AddError(ref errors, node, $"Logical operator requires Bool operands, got {leftType} and {rightType}");
                    node.ResultType = PropertyKind.Bool;
                    break;
                }

                case ExpressionKind.Not:
                {
                    var operandType = nodes[node.LeftIndex].ResultType;
                    if (operandType != PropertyKind.Bool)
                        AddError(ref errors, node, $"Logical NOT requires Bool, got {operandType}");
                    node.ResultType = PropertyKind.Bool;
                    break;
                }

                case ExpressionKind.Negate:
                {
                    var operandType = nodes[node.LeftIndex].ResultType;
                    if (!IsNumeric(operandType) && !IsVector(operandType))
                        AddError(ref errors, node, $"Negate requires numeric or vector type, got {operandType}");
                    node.ResultType = operandType;
                    break;
                }

                case ExpressionKind.Ternary:
                {
                    var condType = nodes[node.ConditionIndex].ResultType;
                    if (condType != PropertyKind.Bool)
                        AddError(ref errors, node, $"Ternary condition must be Bool, got {condType}");

                    var trueType = nodes[node.TrueIndex].ResultType;
                    var falseType = nodes[node.FalseIndex].ResultType;

                    if (trueType == falseType)
                    {
                        node.ResultType = trueType;
                    }
                    else if (IsNumeric(trueType) && IsNumeric(falseType))
                    {
                        // Int/Float promotion
                        node.ResultType = PropertyKind.Float;
                    }
                    else
                    {
                        AddError(ref errors, node, $"Ternary branches have incompatible types: {trueType} and {falseType}");
                        node.ResultType = trueType;
                    }
                    break;
                }

                case ExpressionKind.FunctionCall:
                    CheckFunctionCall(ref node, nodes, ref errors);
                    break;
            }
        }

        return errors?.ToArray() ?? Array.Empty<ExpressionDiagnostic>();
    }

    private static void CheckFunctionCall(ref ExpressionNode node, Span<ExpressionNode> nodes, ref List<ExpressionDiagnostic>? errors)
    {
        switch (node.Function)
        {
            case BuiltinFunction.Abs:
            case BuiltinFunction.Floor:
            case BuiltinFunction.Ceil:
            case BuiltinFunction.Round:
            {
                if (node.Arg0 >= 0)
                    node.ResultType = nodes[node.Arg0].ResultType;
                else
                    node.ResultType = PropertyKind.Float;
                break;
            }

            case BuiltinFunction.Min:
            case BuiltinFunction.Max:
            {
                if (node.Arg0 >= 0 && node.Arg1 >= 0)
                {
                    var a = nodes[node.Arg0].ResultType;
                    var b = nodes[node.Arg1].ResultType;
                    var promoted = PromoteArithmetic(a, b);
                    if (promoted == PropertyKind.Auto)
                    {
                        AddError(ref errors, node, $"{node.Function} requires numeric types, got {a} and {b}");
                        node.ResultType = PropertyKind.Float;
                    }
                    else
                        node.ResultType = promoted;
                }
                else
                    node.ResultType = PropertyKind.Float;
                break;
            }

            case BuiltinFunction.Clamp:
            {
                if (node.Arg0 >= 0)
                    node.ResultType = nodes[node.Arg0].ResultType;
                else
                    node.ResultType = PropertyKind.Float;
                break;
            }

            case BuiltinFunction.Lerp:
            {
                // lerp(a, b, t) — result is same type as a/b; t must be numeric
                if (node.Arg0 >= 0 && node.Arg1 >= 0)
                {
                    var a = nodes[node.Arg0].ResultType;
                    var b = nodes[node.Arg1].ResultType;
                    if (a == b)
                        node.ResultType = a;
                    else if (IsNumeric(a) && IsNumeric(b))
                        node.ResultType = PromoteArithmetic(a, b);
                    else
                    {
                        AddError(ref errors, node, $"lerp first two arguments must be same type, got {a} and {b}");
                        node.ResultType = a;
                    }
                }
                else
                    node.ResultType = PropertyKind.Float;

                if (node.Arg2 >= 0 && !IsNumeric(nodes[node.Arg2].ResultType))
                    AddError(ref errors, node, $"lerp 't' parameter must be numeric, got {nodes[node.Arg2].ResultType}");
                break;
            }

            case BuiltinFunction.Remap:
            {
                // remap(x, inLo, inHi, outLo, outHi) — all Float → Float
                node.ResultType = PropertyKind.Float;
                break;
            }

            default:
                node.ResultType = PropertyKind.Float;
                break;
        }
    }

    private static (PropertyKind left, PropertyKind right) GetBinaryTypes(Span<ExpressionNode> nodes, in ExpressionNode node)
    {
        var leftType = node.LeftIndex >= 0 ? nodes[node.LeftIndex].ResultType : PropertyKind.Auto;
        var rightType = node.RightIndex >= 0 ? nodes[node.RightIndex].ResultType : PropertyKind.Auto;
        return (leftType, rightType);
    }

    /// <summary>
    /// Returns the promoted result type for arithmetic: same-type returns that type,
    /// Int×Float → Float, incompatible → Auto (error).
    /// </summary>
    private static PropertyKind PromoteArithmetic(PropertyKind a, PropertyKind b)
    {
        if (a == b) return a;

        // Int + Float → Float
        if ((a == PropertyKind.Int && b == PropertyKind.Float) ||
            (a == PropertyKind.Float && b == PropertyKind.Int))
            return PropertyKind.Float;

        // Int + Fixed64 → Fixed64
        if ((a == PropertyKind.Int && b == PropertyKind.Fixed64) ||
            (a == PropertyKind.Fixed64 && b == PropertyKind.Int))
            return PropertyKind.Fixed64;

        // Same-dimension vector ops
        if (a == b && IsVector(a)) return a;

        // No Float↔Fixed64 promotion (lossy in both directions)
        return PropertyKind.Auto;
    }

    private static bool IsNumeric(PropertyKind kind) => kind is PropertyKind.Float or PropertyKind.Int or PropertyKind.Fixed64;

    private static bool IsVector(PropertyKind kind) =>
        kind is PropertyKind.Vec2 or PropertyKind.Vec3 or PropertyKind.Vec4
            or PropertyKind.Fixed64Vec2 or PropertyKind.Fixed64Vec3;

    /// <summary>Returns the scalar type for a given vector kind (e.g. Vec2→Float, Fixed64Vec2→Fixed64).</summary>
    private static PropertyKind ScalarOf(PropertyKind kind) => kind switch
    {
        PropertyKind.Vec2 or PropertyKind.Vec3 or PropertyKind.Vec4 => PropertyKind.Float,
        PropertyKind.Fixed64Vec2 or PropertyKind.Fixed64Vec3 => PropertyKind.Fixed64,
        _ => kind,
    };

    private static void AddError(ref List<ExpressionDiagnostic>? list, in ExpressionNode node, string message)
    {
        list ??= new List<ExpressionDiagnostic>();
        list.Add(new ExpressionDiagnostic(node.SourceOffset, node.SourceLength, message));
    }
}
