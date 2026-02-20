using System;
using System.Runtime.CompilerServices;

namespace Core;

/// <summary>
/// Zero-allocation array and span utilities for hot path operations.
/// </summary>
public static class ArrayUtils
{
    /// <summary>
    /// Swaps two elements in a span without allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Swap<T>(Span<T> span, int indexA, int indexB)
    {
        (span[indexA], span[indexB]) = (span[indexB], span[indexA]);
    }

    /// <summary>
    /// Swaps two elements in an array without allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Swap<T>(T[] array, int indexA, int indexB)
    {
        (array[indexA], array[indexB]) = (array[indexB], array[indexA]);
    }

    /// <summary>
    /// Clears a span by setting all elements to default.
    /// More explicit than Span.Clear() for readability.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Clear<T>(Span<T> span)
    {
        span.Clear();
    }

    /// <summary>
    /// Fills a span with a specified value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Fill<T>(Span<T> span, T value)
    {
        span.Fill(value);
    }

    /// <summary>
    /// Copies elements from source to destination with bounds checking.
    /// Returns the number of elements actually copied.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SafeCopy<T>(ReadOnlySpan<T> source, Span<T> destination)
    {
        int count = Math.Min(source.Length, destination.Length);
        source.Slice(0, count).CopyTo(destination);
        return count;
    }

    /// <summary>
    /// Finds the index of the first element matching the predicate.
    /// Returns -1 if not found. Uses delegate to avoid closure allocations.
    /// </summary>
    public static int FindIndex<T>(ReadOnlySpan<T> span, T target) where T : IEquatable<T>
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i].Equals(target))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Finds the index of the first element matching the predicate using a ref struct enumerator.
    /// </summary>
    public static int FindIndex<T, TState>(ReadOnlySpan<T> span, TState state, Func<T, TState, bool> predicate)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (predicate(span[i], state))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Counts elements matching the target value.
    /// </summary>
    public static int Count<T>(ReadOnlySpan<T> span, T target) where T : IEquatable<T>
    {
        int count = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i].Equals(target))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Checks if any element matches the target value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Contains<T>(ReadOnlySpan<T> span, T target) where T : IEquatable<T>
    {
        return FindIndex(span, target) >= 0;
    }

    /// <summary>
    /// Reverses elements in place.
    /// </summary>
    public static void Reverse<T>(Span<T> span)
    {
        int left = 0;
        int right = span.Length - 1;
        while (left < right)
        {
            Swap(span, left, right);
            left++;
            right--;
        }
    }

    /// <summary>
    /// Removes element at index by swapping with last element (O(1) removal).
    /// Returns the new length. Does not preserve order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SwapRemoveAt<T>(Span<T> span, int index, int currentLength)
    {
        if (index < 0 || index >= currentLength)
            throw new ArgumentOutOfRangeException(nameof(index));

        int lastIndex = currentLength - 1;
        if (index != lastIndex)
        {
            span[index] = span[lastIndex];
        }
        span[lastIndex] = default!;
        return currentLength - 1;
    }

    /// <summary>
    /// Binary search on a sorted span. Returns index if found, otherwise ~insertionPoint.
    /// </summary>
    public static int BinarySearch<T>(ReadOnlySpan<T> span, T value) where T : IComparable<T>
    {
        int low = 0;
        int high = span.Length - 1;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            int cmp = span[mid].CompareTo(value);

            if (cmp == 0)
                return mid;
            if (cmp < 0)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return ~low;
    }

    /// <summary>
    /// Ensures array capacity, resizing if needed.
    /// Returns true if resized, false if capacity was sufficient.
    /// </summary>
    public static bool EnsureCapacity<T>(ref T[] array, int requiredCapacity, int growthFactor = 2)
    {
        if (array.Length >= requiredCapacity)
            return false;

        int newCapacity = Math.Max(array.Length * growthFactor, requiredCapacity);
        var newArray = new T[newCapacity];
        Array.Copy(array, newArray, array.Length);
        array = newArray;
        return true;
    }

    /// <summary>
    /// Copies a portion of the array to the beginning, shifting elements.
    /// Useful for compacting sparse arrays.
    /// </summary>
    public static void ShiftLeft<T>(Span<T> span, int startIndex, int count)
    {
        if (startIndex <= 0 || count <= 0)
            return;

        int endIndex = Math.Min(startIndex + count, span.Length);
        int actualCount = endIndex - startIndex;

        for (int i = 0; i < actualCount; i++)
        {
            span[i] = span[startIndex + i];
        }
    }
}
