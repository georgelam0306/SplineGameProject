// SPDX-License-Identifier: MIT
#nullable enable
namespace Property.Generator
{
    internal sealed class PropertyArrayFieldModel
    {
        public string FieldName { get; }
        public PropertyArrayValues ArrayValues { get; }

        public PropertyArrayFieldModel(string fieldName, PropertyArrayValues arrayValues)
        {
            FieldName = fieldName;
            ArrayValues = arrayValues;
        }
    }
}

