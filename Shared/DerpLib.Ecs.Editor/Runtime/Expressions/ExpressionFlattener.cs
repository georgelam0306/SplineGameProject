using System;
using System.Collections.Generic;
using Property;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Converts a type-checked AST (<see cref="ExpressionNode"/>[]) into an SSA <see cref="FlatExpression"/>.
/// Applies Common Subexpression Elimination (CSE) so each unique subexpression is evaluated once.
/// </summary>
public static class ExpressionFlattener
{
    public static FlatExpression Flatten(ReadOnlySpan<ExpressionNode> nodes, int rootIndex)
    {
        if (nodes.Length == 0)
            return new FlatExpression(Array.Empty<FlatOp>(), -1);

        var ops = new List<FlatOp>(nodes.Length);
        var cse = new Dictionary<long, int>(); // CSE key → op index
        Span<int> astToOp = stackalloc int[nodes.Length];
        astToOp.Fill(-1);

        // Forward pass: nodes are already in bottom-up order
        for (int i = 0; i < nodes.Length; i++)
        {
            ref readonly var node = ref nodes[i];
            int opIndex = FlattenNode(node, astToOp, ops, cse);
            astToOp[i] = opIndex;
        }

        int resultOpIndex = rootIndex >= 0 ? astToOp[rootIndex] : -1;
        return new FlatExpression(ops.ToArray(), resultOpIndex);
    }

    private static int FlattenNode(
        in ExpressionNode node,
        Span<int> astToOp,
        List<FlatOp> ops,
        Dictionary<long, int> cse)
    {
        switch (node.Kind)
        {
            case ExpressionKind.FloatLiteral:
            case ExpressionKind.IntLiteral:
            case ExpressionKind.BoolLiteral:
            case ExpressionKind.ColorLiteral:
            case ExpressionKind.StringLiteral:
            {
                var op = new FlatOp
                {
                    Kind = FlatOpKind.LoadLiteral,
                    ResultType = node.ResultType,
                    LiteralValue = node.LiteralValue,
                    Left = -1, Right = -1, Arg2 = -1, Arg3 = -1, Arg4 = -1,
                };
                return EmitOp(ops, op);
            }

            case ExpressionKind.Variable:
            {
                // CSE for same source variable
                long key = CseKey(FlatOpKind.LoadSource, node.SourceIndex, 0);
                if (cse.TryGetValue(key, out int existing))
                    return existing;

                var op = new FlatOp
                {
                    Kind = FlatOpKind.LoadSource,
                    ResultType = node.ResultType,
                    SourceIndex = node.SourceIndex,
                    Left = -1, Right = -1, Arg2 = -1, Arg3 = -1, Arg4 = -1,
                };
                int idx = EmitOp(ops, op);
                cse[key] = idx;
                return idx;
            }

            case ExpressionKind.Add:
            case ExpressionKind.Subtract:
            case ExpressionKind.Multiply:
            case ExpressionKind.Divide:
            {
                int left = ResolveChild(node.LeftIndex, astToOp);
                int right = ResolveChild(node.RightIndex, astToOp);

                // Handle scalar broadcast for Mul/Div
                var flatKind = node.Kind switch
                {
                    ExpressionKind.Add => FlatOpKind.Add,
                    ExpressionKind.Subtract => FlatOpKind.Subtract,
                    ExpressionKind.Multiply => FlatOpKind.Multiply,
                    ExpressionKind.Divide => FlatOpKind.Divide,
                    _ => FlatOpKind.Add,
                };

                // Check for scalar broadcast: if result is vector but one operand is scalar
                if (node.Kind is ExpressionKind.Multiply or ExpressionKind.Divide)
                {
                    bool resultIsVec = IsVector(node.ResultType);
                    if (resultIsVec && node.LeftIndex >= 0 && node.RightIndex >= 0)
                    {
                        if (IsFixed64Vector(node.ResultType))
                        {
                            flatKind = node.Kind == ExpressionKind.Multiply
                                ? FlatOpKind.MulFixed64Scalar
                                : FlatOpKind.DivFixed64Scalar;
                        }
                        else
                        {
                            flatKind = node.Kind == ExpressionKind.Multiply
                                ? FlatOpKind.MulScalar
                                : FlatOpKind.DivScalar;
                        }
                    }
                }

                // Check for Int→Float or Int→Fixed64 promotion needed
                left = MaybePromote(left, ops, node.ResultType);
                right = MaybePromote(right, ops, node.ResultType);

                long key = CseKey(flatKind, left, right);
                if (cse.TryGetValue(key, out int existing))
                    return existing;

                var op = new FlatOp
                {
                    Kind = flatKind,
                    ResultType = node.ResultType,
                    Left = left,
                    Right = right,
                    Arg2 = -1, Arg3 = -1, Arg4 = -1,
                };
                int idx = EmitOp(ops, op);
                cse[key] = idx;
                return idx;
            }

            case ExpressionKind.Equal:
            case ExpressionKind.NotEqual:
            case ExpressionKind.Less:
            case ExpressionKind.Greater:
            case ExpressionKind.LessEqual:
            case ExpressionKind.GreaterEqual:
            {
                int left = ResolveChild(node.LeftIndex, astToOp);
                int right = ResolveChild(node.RightIndex, astToOp);

                // Determine operand type for comparison dispatch
                var leftType = left >= 0 ? ops[left].ResultType : PropertyKind.Float;
                var rightType = right >= 0 ? ops[right].ResultType : PropertyKind.Float;
                var operandType = leftType;
                // Promote Int×Float → Float for comparisons
                if ((leftType == PropertyKind.Int && rightType == PropertyKind.Float) ||
                    (leftType == PropertyKind.Float && rightType == PropertyKind.Int))
                {
                    operandType = PropertyKind.Float;
                    left = MaybePromote(left, ops, PropertyKind.Float);
                    right = MaybePromote(right, ops, PropertyKind.Float);
                }
                // Promote Int×Fixed64 → Fixed64 for comparisons
                else if ((leftType == PropertyKind.Int && rightType == PropertyKind.Fixed64) ||
                         (leftType == PropertyKind.Fixed64 && rightType == PropertyKind.Int))
                {
                    operandType = PropertyKind.Fixed64;
                    left = MaybePromote(left, ops, PropertyKind.Fixed64);
                    right = MaybePromote(right, ops, PropertyKind.Fixed64);
                }

                var flatKind = node.Kind switch
                {
                    ExpressionKind.Equal => FlatOpKind.Equal,
                    ExpressionKind.NotEqual => FlatOpKind.NotEqual,
                    ExpressionKind.Less => FlatOpKind.Less,
                    ExpressionKind.Greater => FlatOpKind.Greater,
                    ExpressionKind.LessEqual => FlatOpKind.LessEqual,
                    ExpressionKind.GreaterEqual => FlatOpKind.GreaterEqual,
                    _ => FlatOpKind.Equal,
                };

                var op = new FlatOp
                {
                    Kind = flatKind,
                    ResultType = PropertyKind.Bool,
                    OperandType = operandType,
                    Left = left,
                    Right = right,
                    Arg2 = -1, Arg3 = -1, Arg4 = -1,
                };
                return EmitOp(ops, op);
            }

            case ExpressionKind.And:
            case ExpressionKind.Or:
            {
                int left = ResolveChild(node.LeftIndex, astToOp);
                int right = ResolveChild(node.RightIndex, astToOp);
                var op = new FlatOp
                {
                    Kind = node.Kind == ExpressionKind.And ? FlatOpKind.And : FlatOpKind.Or,
                    ResultType = PropertyKind.Bool,
                    Left = left,
                    Right = right,
                    Arg2 = -1, Arg3 = -1, Arg4 = -1,
                };
                return EmitOp(ops, op);
            }

            case ExpressionKind.Not:
            {
                int operand = ResolveChild(node.LeftIndex, astToOp);
                var op = new FlatOp
                {
                    Kind = FlatOpKind.Not,
                    ResultType = PropertyKind.Bool,
                    Left = operand,
                    Right = -1, Arg2 = -1, Arg3 = -1, Arg4 = -1,
                };
                return EmitOp(ops, op);
            }

            case ExpressionKind.Negate:
            {
                int operand = ResolveChild(node.LeftIndex, astToOp);
                var op = new FlatOp
                {
                    Kind = FlatOpKind.Negate,
                    ResultType = node.ResultType,
                    Left = operand,
                    Right = -1, Arg2 = -1, Arg3 = -1, Arg4 = -1,
                };
                return EmitOp(ops, op);
            }

            case ExpressionKind.Ternary:
            {
                int cond = ResolveChild(node.ConditionIndex, astToOp);
                int trueVal = ResolveChild(node.TrueIndex, astToOp);
                int falseVal = ResolveChild(node.FalseIndex, astToOp);

                var op = new FlatOp
                {
                    Kind = FlatOpKind.Select,
                    ResultType = node.ResultType,
                    Left = cond,
                    Right = trueVal,
                    Arg2 = falseVal,
                    Arg3 = -1, Arg4 = -1,
                };
                return EmitOp(ops, op);
            }

            case ExpressionKind.FunctionCall:
                return FlattenFunctionCall(node, astToOp, ops, cse);

            default:
                return EmitOp(ops, new FlatOp
                {
                    Kind = FlatOpKind.LoadLiteral,
                    ResultType = PropertyKind.Float,
                    Left = -1, Right = -1, Arg2 = -1, Arg3 = -1, Arg4 = -1,
                });
        }
    }

    private static int FlattenFunctionCall(
        in ExpressionNode node,
        Span<int> astToOp,
        List<FlatOp> ops,
        Dictionary<long, int> cse)
    {
        int a0 = ResolveChild(node.Arg0, astToOp);
        int a1 = ResolveChild(node.Arg1, astToOp);
        int a2 = ResolveChild(node.Arg2, astToOp);
        int a3 = ResolveChild(node.Arg3, astToOp);
        int a4 = ResolveChild(node.Arg4, astToOp);

        // Promote Int→Float for function arguments when result type is Float
        var rt = node.ResultType;
        a0 = MaybePromote(a0, ops, rt);
        a1 = MaybePromote(a1, ops, rt);
        a2 = MaybePromote(a2, ops, rt);
        a3 = MaybePromote(a3, ops, rt);
        a4 = MaybePromote(a4, ops, rt);

        var flatKind = node.Function switch
        {
            BuiltinFunction.Min => FlatOpKind.Min,
            BuiltinFunction.Max => FlatOpKind.Max,
            BuiltinFunction.Clamp => FlatOpKind.Clamp,
            BuiltinFunction.Lerp => FlatOpKind.Lerp,
            BuiltinFunction.Remap => FlatOpKind.Remap,
            BuiltinFunction.Abs => FlatOpKind.Abs,
            BuiltinFunction.Floor => FlatOpKind.Floor,
            BuiltinFunction.Ceil => FlatOpKind.Ceil,
            BuiltinFunction.Round => FlatOpKind.Round,
            _ => FlatOpKind.LoadLiteral,
        };

        var op = new FlatOp
        {
            Kind = flatKind,
            ResultType = node.ResultType,
            Left = a0,
            Right = a1,
            Arg2 = a2,
            Arg3 = a3,
            Arg4 = a4,
        };
        return EmitOp(ops, op);
    }

    private static int ResolveChild(int astIndex, Span<int> astToOp)
    {
        return astIndex >= 0 ? astToOp[astIndex] : -1;
    }

    /// <summary>Insert an Int→Float or Int→Fixed64 promotion op if needed.</summary>
    private static int MaybePromote(int opIndex, List<FlatOp> ops, PropertyKind targetType)
    {
        if (opIndex < 0) return opIndex;
        if (ops[opIndex].ResultType != PropertyKind.Int) return opIndex;

        if (targetType == PropertyKind.Float)
        {
            var promote = new FlatOp
            {
                Kind = FlatOpKind.PromoteIntToFloat,
                ResultType = PropertyKind.Float,
                Left = opIndex,
                Right = -1, Arg2 = -1, Arg3 = -1, Arg4 = -1,
            };
            return EmitOp(ops, promote);
        }

        if (targetType == PropertyKind.Fixed64)
        {
            var promote = new FlatOp
            {
                Kind = FlatOpKind.PromoteIntToFixed64,
                ResultType = PropertyKind.Fixed64,
                Left = opIndex,
                Right = -1, Arg2 = -1, Arg3 = -1, Arg4 = -1,
            };
            return EmitOp(ops, promote);
        }

        return opIndex;
    }

    private static int EmitOp(List<FlatOp> ops, in FlatOp op)
    {
        int index = ops.Count;
        ops.Add(op);
        return index;
    }

    private static long CseKey(FlatOpKind kind, int left, int right)
    {
        return ((long)kind << 48) | ((long)(left & 0xFFFFFF) << 24) | (long)(right & 0xFFFFFF);
    }

    private static bool IsVector(PropertyKind kind) =>
        kind is PropertyKind.Vec2 or PropertyKind.Vec3 or PropertyKind.Vec4
            or PropertyKind.Fixed64Vec2 or PropertyKind.Fixed64Vec3;

    private static bool IsFixed64Vector(PropertyKind kind) =>
        kind is PropertyKind.Fixed64Vec2 or PropertyKind.Fixed64Vec3;
}
