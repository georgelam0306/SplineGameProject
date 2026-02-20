using System;
using FixedMath;
using Property;

namespace DerpLib.Ecs.Editor;

/// <summary>
/// Evaluates a <see cref="FlatExpression"/> with source variable values.
/// Uses stackalloc temps for zero-allocation evaluation.
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>Max number of ops supported for stackalloc. Beyond this, heap-allocate.</summary>
    private const int StackAllocLimit = 128;

    public static PropertyValue Evaluate(in FlatExpression expr, ReadOnlySpan<PropertyValue> sources)
    {
        if (!expr.IsValid) return default;

        int count = expr.Ops.Length;
        if (count <= StackAllocLimit)
        {
            Span<PropertyValue> temps = stackalloc PropertyValue[count];
            EvaluateCore(expr.Ops, sources, temps);
            return expr.ResultIndex >= 0 ? temps[expr.ResultIndex] : default;
        }
        else
        {
            var temps = new PropertyValue[count];
            EvaluateCore(expr.Ops, sources, temps);
            return expr.ResultIndex >= 0 ? temps[expr.ResultIndex] : default;
        }
    }

    private static void EvaluateCore(ReadOnlySpan<FlatOp> ops, ReadOnlySpan<PropertyValue> sources, Span<PropertyValue> temps)
    {
        for (int i = 0; i < ops.Length; i++)
        {
            ref readonly var op = ref ops[i];

            switch (op.Kind)
            {
                case FlatOpKind.LoadLiteral:
                    temps[i] = op.LiteralValue;
                    break;

                case FlatOpKind.LoadSource:
                    temps[i] = op.SourceIndex >= 0 && op.SourceIndex < sources.Length
                        ? sources[op.SourceIndex]
                        : default;
                    break;

                case FlatOpKind.Add:
                    temps[i] = PropertyValueOps.Add(op.ResultType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.Subtract:
                    temps[i] = PropertyValueOps.Sub(op.ResultType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.Multiply:
                    temps[i] = PropertyValueOps.Mul(op.ResultType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.Divide:
                    temps[i] = PropertyValueOps.Div(op.ResultType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.MulScalar:
                    temps[i] = PropertyValueOps.MulScalar(op.ResultType, temps[op.Left], temps[op.Right].Float);
                    break;

                case FlatOpKind.DivScalar:
                    temps[i] = PropertyValueOps.DivScalar(op.ResultType, temps[op.Left], temps[op.Right].Float);
                    break;

                case FlatOpKind.MulFixed64Scalar:
                    temps[i] = PropertyValueOps.MulFixed64Scalar(op.ResultType, temps[op.Left], temps[op.Right].Fixed64);
                    break;

                case FlatOpKind.DivFixed64Scalar:
                    temps[i] = PropertyValueOps.DivFixed64Scalar(op.ResultType, temps[op.Left], temps[op.Right].Fixed64);
                    break;

                case FlatOpKind.Negate:
                    temps[i] = PropertyValueOps.Negate(op.ResultType, temps[op.Left]);
                    break;

                case FlatOpKind.PromoteIntToFloat:
                    temps[i] = PropertyValueOps.PromoteToFloat(temps[op.Left]);
                    break;

                case FlatOpKind.PromoteIntToFixed64:
                    temps[i] = PropertyValueOps.PromoteToFixed64(temps[op.Left]);
                    break;

                case FlatOpKind.Equal:
                    temps[i] = PropertyValueOps.Equal(op.OperandType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.NotEqual:
                    temps[i] = PropertyValueOps.NotEqual(op.OperandType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.Less:
                    temps[i] = PropertyValueOps.Less(op.OperandType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.LessEqual:
                    temps[i] = PropertyValueOps.LessEqual(op.OperandType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.Greater:
                    temps[i] = PropertyValueOps.Greater(op.OperandType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.GreaterEqual:
                    temps[i] = PropertyValueOps.GreaterEqual(op.OperandType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.And:
                    temps[i] = PropertyValue.FromBool(temps[op.Left].Bool && temps[op.Right].Bool);
                    break;

                case FlatOpKind.Or:
                    temps[i] = PropertyValue.FromBool(temps[op.Left].Bool || temps[op.Right].Bool);
                    break;

                case FlatOpKind.Not:
                    temps[i] = PropertyValueOps.Not(temps[op.Left]);
                    break;

                case FlatOpKind.Select:
                    temps[i] = temps[op.Left].Bool ? temps[op.Right] : temps[op.Arg2];
                    break;

                case FlatOpKind.Min:
                    temps[i] = PropertyValueOps.Min(op.ResultType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.Max:
                    temps[i] = PropertyValueOps.Max(op.ResultType, temps[op.Left], temps[op.Right]);
                    break;

                case FlatOpKind.Clamp:
                    temps[i] = PropertyValueOps.Clamp(op.ResultType, temps[op.Left], temps[op.Right], temps[op.Arg2]);
                    break;

                case FlatOpKind.Abs:
                    temps[i] = PropertyValueOps.Abs(op.ResultType, temps[op.Left]);
                    break;

                case FlatOpKind.Floor:
                    temps[i] = PropertyValueOps.Floor(op.ResultType, temps[op.Left]);
                    break;

                case FlatOpKind.Ceil:
                    temps[i] = PropertyValueOps.Ceil(op.ResultType, temps[op.Left]);
                    break;

                case FlatOpKind.Round:
                    temps[i] = PropertyValueOps.Round(op.ResultType, temps[op.Left]);
                    break;

                case FlatOpKind.Lerp:
                    temps[i] = PropertyValueOps.Lerp(op.ResultType, temps[op.Left], temps[op.Right], temps[op.Arg2].Float);
                    break;

                case FlatOpKind.Remap:
                    temps[i] = PropertyValueOps.Remap(
                        temps[op.Left].Float,
                        temps[op.Right].Float,
                        temps[op.Arg2].Float,
                        temps[op.Arg3].Float,
                        temps[op.Arg4].Float);
                    break;
            }
        }
    }
}
