// SPDX-License-Identifier: MIT
#nullable enable
using System;

namespace Property
{
    /// <summary>
    /// Marks a field as a fixed-size array for property code generation.
    /// The field type must be an inline array struct (C# [InlineArray]) so element access is allocation-free.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ArrayAttribute : Attribute
    {
        public int Length { get; }

        public ArrayAttribute(int length)
        {
            if (length <= 0 || length > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Array length must be between 1 and 64");
            }

            Length = length;
        }
    }

    /// <summary>
    /// Marks a field as a fixed-size 2D array (row-major) for property code generation.
    /// The field type must be an inline array struct (C# [InlineArray]) of TotalLength elements.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class Array2DAttribute : Attribute
    {
        public int Rows { get; }
        public int Cols { get; }
        public int TotalLength => Rows * Cols;

        public Array2DAttribute(int rows, int cols)
        {
            if (rows <= 0 || rows > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), "Rows must be between 1 and 256");
            }
            if (cols <= 0 || cols > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(cols), "Cols must be between 1 and 256");
            }
            if (rows * cols > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), "Total elements (rows * cols) must not exceed 65536");
            }

            Rows = rows;
            Cols = cols;
        }
    }
}

