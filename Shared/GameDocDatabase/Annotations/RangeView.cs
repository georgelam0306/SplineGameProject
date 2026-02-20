using System;
using System.Runtime.CompilerServices;

namespace GameDocDatabase;

/// <summary>
/// Zero-allocation view over a contiguous range of records.
/// Used for non-unique secondary key lookups that return multiple results.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public readonly ref struct RangeView<T>
{
    private readonly ReadOnlySpan<T> _span;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RangeView(ReadOnlySpan<T> span)
    {
        _span = span;
    }

    /// <summary>Number of records in this range.</summary>
    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _span.Length;
    }

    /// <summary>Whether this range contains any records.</summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _span.IsEmpty;
    }

    /// <summary>Access a record by index within this range.</summary>
    public ref readonly T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _span[index];
    }

    /// <summary>Get the underlying span for advanced usage.</summary>
    public ReadOnlySpan<T> AsSpan()
    {
        return _span;
    }

    /// <summary>Get an enumerator for foreach support.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new Enumerator(_span);

    /// <summary>
    /// Enumerator for RangeView that supports foreach.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<T> _span;
        private int _index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(ReadOnlySpan<T> span)
        {
            _span = span;
            _index = -1;
        }

        /// <summary>Move to the next element.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            int next = _index + 1;
            if (next < _span.Length)
            {
                _index = next;
                return true;
            }
            return false;
        }

        /// <summary>Get the current element.</summary>
        public ref readonly T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _span[_index];
        }
    }

    /// <summary>Create an empty RangeView.</summary>
    public static RangeView<T> Empty => new RangeView<T>(ReadOnlySpan<T>.Empty);

    /// <summary>Implicit conversion from ReadOnlySpan.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator RangeView<T>(ReadOnlySpan<T> span) => new RangeView<T>(span);
}
