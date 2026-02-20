// SPDX-License-Identifier: MIT
#nullable enable
using DerpLib.DI.Generator.Model;
using System.Text;

namespace DerpLib.DI.Generator.Emit
{
    /// <summary>
    /// Emits disposal tracking and IDisposable implementation.
    /// </summary>
    internal static class DisposalEmitter
    {
        /// <summary>
        /// Emits the disposal tracking fields.
        /// </summary>
        public static void EmitFields(StringBuilder sb, string indent)
        {
            sb.AppendLine($"{indent}private global::System.IDisposable?[] _disposables = new global::System.IDisposable?[8];");
            sb.AppendLine($"{indent}private int _disposableCount;");
        }

        /// <summary>
        /// Emits the TrackDisposable helper method.
        /// </summary>
        public static void EmitTrackDisposable(StringBuilder sb, string indent)
        {
            sb.AppendLine($"{indent}private void TrackDisposable(object instance)");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    if (instance is global::System.IDisposable disposable)");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        if (_disposableCount >= _disposables.Length)");
            sb.AppendLine($"{indent}        {{");
            sb.AppendLine($"{indent}            global::System.Array.Resize(ref _disposables, _disposables.Length * 2);");
            sb.AppendLine($"{indent}        }}");
            sb.AppendLine($"{indent}        _disposables[_disposableCount++] = disposable;");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");
        }

        /// <summary>
        /// Emits the Dispose method.
        /// </summary>
        public static void EmitDisposeMethod(StringBuilder sb, CompositionModel composition, string indent)
        {
            var hasLock = HasLock(composition);

            sb.AppendLine($"{indent}public void Dispose()");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    global::System.IDisposable?[] toDispose;");
            sb.AppendLine($"{indent}    int count;");
            if (hasLock)
            {
                sb.AppendLine($"{indent}    lock (_lock)");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        toDispose = _disposables;");
                sb.AppendLine($"{indent}        count = _disposableCount;");
                sb.AppendLine($"{indent}        _disposables = global::System.Array.Empty<global::System.IDisposable?>();");
                sb.AppendLine($"{indent}        _disposableCount = 0;");
                sb.AppendLine();

                // Null out singleton fields
                foreach (var binding in composition.Bindings)
                {
                    if (binding.Lifetime == BindingLifetime.Singleton)
                    {
                        sb.AppendLine($"{indent}        {binding.GetSingletonFieldName()} = null;");
                    }
                }

                // Null out scope factory fields
                foreach (var scope in composition.Scopes)
                {
                    sb.AppendLine($"{indent}        {scope.GetFactorySingletonFieldName()} = null;");
                }

                sb.AppendLine($"{indent}    }}");
            }
            else
            {
                sb.AppendLine($"{indent}    toDispose = _disposables;");
                sb.AppendLine($"{indent}    count = _disposableCount;");
                sb.AppendLine($"{indent}    _disposables = global::System.Array.Empty<global::System.IDisposable?>();");
                sb.AppendLine($"{indent}    _disposableCount = 0;");
            }
            sb.AppendLine();
            sb.AppendLine($"{indent}    for (int i = count - 1; i >= 0; i--)");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        try {{ toDispose[i]?.Dispose(); }}");
            sb.AppendLine($"{indent}        catch (global::System.Exception ex) {{ OnDisposeException(toDispose[i]!, ex); }}");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");
        }

        private static bool HasLock(CompositionModel composition)
        {
            foreach (var binding in composition.Bindings)
            {
                if (binding.Lifetime == BindingLifetime.Singleton)
                {
                    return true;
                }
            }

            return composition.Scopes.Count > 0;
        }

        /// <summary>
        /// Emits the partial OnDisposeException method.
        /// </summary>
        public static void EmitOnDisposeException(StringBuilder sb, string indent)
        {
            sb.AppendLine($"{indent}partial void OnDisposeException(global::System.IDisposable instance, global::System.Exception exception);");
        }
    }
}
