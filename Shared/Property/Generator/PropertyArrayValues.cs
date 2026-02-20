// SPDX-License-Identifier: MIT
#nullable enable
namespace Property.Generator
{
    internal readonly struct PropertyArrayValues
    {
        public int Length { get; }
        public int Rows { get; }
        public int Cols { get; }

        public PropertyArrayValues(int length, int rows, int cols)
        {
            Length = length;
            Rows = rows;
            Cols = cols;
        }

        public bool IsArray => Length > 0;
        public bool IsArray2D => Rows > 0 && Cols > 0;
        public int TotalLength => Rows * Cols;
    }
}

