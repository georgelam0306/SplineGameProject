// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Property.Generator
{
    internal sealed class PropertyFieldModel
    {
        public IFieldSymbol FieldSymbol { get; }
        public INamedTypeSymbol ComponentSymbol { get; }
        public PropertyAttributeValues Values { get; }
        public PropertyArrayValues ArrayValues { get; }
        public PooledSettings PooledSettings { get; }
        public bool ColumnReadOnly { get; }
        public ImmutableArray<Diagnostic> Diagnostics { get; }
        public bool IsValid { get; }

        private PropertyFieldModel(
            IFieldSymbol fieldSymbol,
            INamedTypeSymbol componentSymbol,
            PropertyAttributeValues values,
            PropertyArrayValues arrayValues,
            PooledSettings pooledSettings,
            bool columnReadOnly,
            ImmutableArray<Diagnostic> diagnostics,
            bool isValid)
        {
            FieldSymbol = fieldSymbol;
            ComponentSymbol = componentSymbol;
            Values = values;
            ArrayValues = arrayValues;
            PooledSettings = pooledSettings;
            ColumnReadOnly = columnReadOnly;
            Diagnostics = diagnostics;
            IsValid = isValid;
        }

        public static PropertyFieldModel Create(GeneratorAttributeSyntaxContext context)
        {
            var fieldSymbol = (IFieldSymbol)context.TargetSymbol;
            var componentSymbol = fieldSymbol.ContainingType;
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
            bool isValid = true;

            if (fieldSymbol.IsStatic || fieldSymbol.IsConst)
            {
                diagnostics.Add(Diagnostic.Create(
                    Diag.PROP007,
                    fieldSymbol.Locations[0],
                    fieldSymbol.Name,
                    componentSymbol.ToDisplayString()));
                isValid = false;
            }

            if (!TypeHelpers.TryGetPooledSettings(componentSymbol, out var pooledSettings) ||
                !TypeHelpers.IsPartialStruct(componentSymbol) ||
                !TypeHelpers.IsPublicOrInternal(componentSymbol))
            {
                diagnostics.Add(Diagnostic.Create(
                    Diag.PROP001,
                    fieldSymbol.Locations[0],
                    componentSymbol.ToDisplayString()));
                isValid = false;
                pooledSettings = new PooledSettings(soA: true, poolId: 0);
            }

            if (!TypeHelpers.TryGetColumnReadOnly(fieldSymbol, out var columnReadOnly))
            {
                diagnostics.Add(Diagnostic.Create(
                    Diag.PROP002,
                    fieldSymbol.Locations[0],
                    fieldSymbol.Name,
                    componentSymbol.ToDisplayString()));
                isValid = false;
                columnReadOnly = false;
            }

            var attribute = context.Attributes.Length > 0 ? context.Attributes[0] : null;
            var values = attribute != null
                ? ReadValues(attribute)
                : new PropertyAttributeValues(string.Empty, string.Empty, 0, PropertyKind.Auto, PropertyFlags.None, float.NaN, float.NaN, float.NaN, false);

            var arrayValues = ReadArrayValues(fieldSymbol);

            return new PropertyFieldModel(
                fieldSymbol,
                componentSymbol,
                values,
                arrayValues,
                pooledSettings,
                columnReadOnly,
                diagnostics.ToImmutable(),
                isValid);
        }

        private static PropertyAttributeValues ReadValues(AttributeData attribute)
        {
            string name = string.Empty;
            string group = string.Empty;
            int order = 0;
            PropertyKind kind = PropertyKind.Auto;
            PropertyFlags flags = PropertyFlags.None;
            float min = float.NaN;
            float max = float.NaN;
            float step = float.NaN;
            bool expandSubfields = false;

            foreach (var namedArg in attribute.NamedArguments)
            {
                string argName = namedArg.Key;
                var value = namedArg.Value.Value;
                switch (argName)
                {
                    case "Name":
                        name = value as string ?? string.Empty;
                        break;
                    case "Group":
                        group = value as string ?? string.Empty;
                        break;
                    case "Order":
                        if (value is int orderValue)
                        {
                            order = orderValue;
                        }
                        break;
                    case "Flags":
                        if (value is int flagsValue)
                        {
                            flags = (PropertyFlags)flagsValue;
                        }
                        else if (value is ushort flagsUShort)
                        {
                            flags = (PropertyFlags)flagsUShort;
                        }
                        else if (value is short flagsShort)
                        {
                            flags = (PropertyFlags)flagsShort;
                        }
                        else if (value is byte flagsByte)
                        {
                            flags = (PropertyFlags)flagsByte;
                        }
                        break;
                    case "Min":
                        if (value is float minValue)
                        {
                            min = minValue;
                        }
                        break;
                    case "Max":
                        if (value is float maxValue)
                        {
                            max = maxValue;
                        }
                        break;
                    case "Step":
                        if (value is float stepValue)
                        {
                            step = stepValue;
                        }
                        break;
                    case "ExpandSubfields":
                        if (value is bool expandValue)
                        {
                            expandSubfields = expandValue;
                        }
                        break;
                    case "Kind":
                        if (value is int kindValue)
                        {
                            kind = (PropertyKind)kindValue;
                        }
                        break;
                }
            }

            return new PropertyAttributeValues(
                name,
                group,
                order,
                kind,
                flags,
                min,
                max,
                step,
                expandSubfields);
        }

        private static PropertyArrayValues ReadArrayValues(IFieldSymbol fieldSymbol)
        {
            int length = 0;
            int rows = 0;
            int cols = 0;

            foreach (var attribute in fieldSymbol.GetAttributes())
            {
                string? fullName = attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (fullName == "global::Property.ArrayAttribute")
                {
                    if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int len)
                    {
                        length = len;
                    }
                }
                else if (fullName == "global::Property.Array2DAttribute")
                {
                    if (attribute.ConstructorArguments.Length == 2 &&
                        attribute.ConstructorArguments[0].Value is int r &&
                        attribute.ConstructorArguments[1].Value is int c)
                    {
                        rows = r;
                        cols = c;
                    }
                }
            }

            return new PropertyArrayValues(length, rows, cols);
        }
    }
}
