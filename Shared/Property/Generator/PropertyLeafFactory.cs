// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Property.Generator
{
    internal static class PropertyLeafFactory
    {
        public static ImmutableArray<PropertyLeafModel> CreateLeaves(
            PropertyFieldModel fieldModel,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            var diagnosticList = new List<Diagnostic>();
            var leaves = new List<PropertyLeafModel>();

            IFieldSymbol fieldSymbol = fieldModel.FieldSymbol;
            INamedTypeSymbol componentSymbol = fieldModel.ComponentSymbol;
            PropertyAttributeValues values = fieldModel.Values;
            PropertyArrayValues arrayValues = fieldModel.ArrayValues;

            PropertyFlags flags = values.Flags;
            if (fieldModel.ColumnReadOnly || fieldSymbol.IsReadOnly)
            {
                flags |= PropertyFlags.ReadOnly;
            }

            string baseDisplayName = !string.IsNullOrWhiteSpace(values.Name)
                ? values.Name
                : NameFormatter.ToDisplayName(fieldSymbol.Name);
            string groupName = values.Group ?? string.Empty;
            int baseOrder = values.Order;

            if (arrayValues.IsArray || arrayValues.IsArray2D)
            {
                if (!TryGetInlineArrayElementType(fieldSymbol.Type, out var elementType, out int inlineLength))
                {
                    diagnosticList.Add(Diagnostic.Create(
                        Diag.PROP008,
                        fieldSymbol.Locations[0],
                        fieldSymbol.Name,
                        componentSymbol.ToDisplayString(),
                        fieldSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    diagnostics = diagnosticList.ToImmutableArray();
                    return ImmutableArray<PropertyLeafModel>.Empty;
                }

                int expectedLength = arrayValues.IsArray2D ? arrayValues.TotalLength : arrayValues.Length;
                if (inlineLength != expectedLength)
                {
                    diagnosticList.Add(Diagnostic.Create(
                        Diag.PROP009,
                        fieldSymbol.Locations[0],
                        fieldSymbol.Name,
                        componentSymbol.ToDisplayString(),
                        expectedLength,
                        inlineLength));
                    diagnostics = diagnosticList.ToImmutableArray();
                    return ImmutableArray<PropertyLeafModel>.Empty;
                }

                int elementCount = expectedLength;
                for (int elementIndex = 0; elementIndex < elementCount; elementIndex++)
                {
                    string elementDisplayName = BuildArrayElementDisplayName(baseDisplayName, arrayValues, elementIndex);
                    int elementOrder = (baseOrder * 1000) + (elementIndex * 10);

                    var elementSegments = new List<FieldPathSegment>(capacity: 2)
                    {
                        new FieldPathSegment(fieldSymbol.Name, fieldSymbol.Type, fieldSymbol.IsReadOnly),
                        new FieldPathSegment(elementIndex, elementType),
                    };

                    ulong channelGroupId = PropertyIdHelper.Compute(BuildPropertyPath(componentSymbol, elementSegments));

                    bool elementForceExpandUnmanaged = values.ExpandSubfields;
                    bool elementAutoExpand = PropertyTypeMap.IsAutoExpandable(elementType);
                    bool elementShouldExpand = elementAutoExpand || elementForceExpandUnmanaged;

                    if (elementShouldExpand)
                    {
                        if (elementForceExpandUnmanaged && !elementType.IsUnmanagedType)
                        {
                            diagnosticList.Add(Diagnostic.Create(
                                Diag.PROP004,
                                fieldSymbol.Locations[0],
                                fieldSymbol.Name,
                                componentSymbol.ToDisplayString(),
                                elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                            diagnostics = diagnosticList.ToImmutableArray();
                            return ImmutableArray<PropertyLeafModel>.Empty;
                        }

                        if (values.HasKindOverride)
                        {
                            diagnosticList.Add(Diagnostic.Create(
                                Diag.PROP006,
                                fieldSymbol.Locations[0],
                                fieldSymbol.Name,
                                componentSymbol.ToDisplayString(),
                                values.Kind.ToString(),
                                elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                            diagnostics = diagnosticList.ToImmutableArray();
                            return ImmutableArray<PropertyLeafModel>.Empty;
                        }

                        int rootChannelLeafIndex = -1;
                        if (elementAutoExpand && PropertyTypeMap.TryGetKind(elementType, out var vectorKind) &&
                            (vectorKind == PropertyKind.Vec2 || vectorKind == PropertyKind.Vec3 || vectorKind == PropertyKind.Vec4))
                        {
                            string identifier = BuildIdentifier(elementSegments);
                            string propertyPath = BuildPropertyPath(componentSymbol, elementSegments);
                            ulong propertyId = PropertyIdHelper.Compute(propertyPath);

                            int rootOrder = (elementOrder * 10) - 1;
                            rootChannelLeafIndex = leaves.Count;
                            leaves.Add(new PropertyLeafModel(
                                identifier,
                                elementDisplayName,
                                groupName,
                                rootOrder,
                                vectorKind,
                                flags,
                                values.Min,
                                values.Max,
                                values.Step,
                                propertyId,
                                hasChannels: true,
                                isChannel: false,
                                channelGroupId: propertyId,
                                channelIndex: 0,
                                channelCount: 0,
                                elementSegments.ToImmutableArray(),
                                elementType));
                        }

                        int groupLeafStartIndex = leaves.Count;
                        int leafIndex = 0;
                        PropertyFlags expandedFlags = flags;
                        if (elementAutoExpand && !elementForceExpandUnmanaged && !values.ExpandSubfields)
                        {
                            expandedFlags |= PropertyFlags.Hidden;
                        }

                        bool expanded = ExpandType(
                            componentSymbol,
                            fieldSymbol,
                            elementType,
                            elementSegments,
                            elementDisplayName,
                            groupName,
                            elementOrder,
                            expandedFlags,
                            values.Min,
                            values.Max,
                            values.Step,
                            elementForceExpandUnmanaged,
                            channelGroupId,
                            leaves,
                            diagnosticList,
                            ref leafIndex);

                        if (!expanded)
                        {
                            diagnostics = diagnosticList.ToImmutableArray();
                            return ImmutableArray<PropertyLeafModel>.Empty;
                        }

                        ushort channelCount = (ushort)leafIndex;
                        for (int i = groupLeafStartIndex; i < leaves.Count; i++)
                        {
                            leaves[i].ChannelCount = channelCount;
                        }
                        if (rootChannelLeafIndex >= 0)
                        {
                            leaves[rootChannelLeafIndex].ChannelCount = channelCount;
                        }
                    }
                    else
                    {
                        if (!TryResolveKind(elementType, values, fieldSymbol, componentSymbol, diagnosticList, out var kind))
                        {
                            diagnostics = diagnosticList.ToImmutableArray();
                            return ImmutableArray<PropertyLeafModel>.Empty;
                        }

                        string identifier = BuildIdentifier(elementSegments);
                        string propertyPath = BuildPropertyPath(componentSymbol, elementSegments);
                        ulong propertyId = PropertyIdHelper.Compute(propertyPath);

                        leaves.Add(new PropertyLeafModel(
                            identifier,
                            elementDisplayName,
                            groupName,
                            elementOrder,
                            kind,
                            flags,
                            values.Min,
                            values.Max,
                            values.Step,
                            propertyId,
                            hasChannels: false,
                            isChannel: false,
                            channelGroupId: 0,
                            channelIndex: 0,
                            channelCount: 0,
                            elementSegments.ToImmutableArray(),
                            elementType));
                    }
                }

                diagnostics = diagnosticList.ToImmutableArray();
                return leaves.ToImmutableArray();
            }

            bool forceExpandUnmanaged = values.ExpandSubfields;
            bool autoExpand = PropertyTypeMap.IsAutoExpandable(fieldSymbol.Type);
            bool shouldExpand = autoExpand || forceExpandUnmanaged;

            if (shouldExpand)
            {
                if (forceExpandUnmanaged && !fieldSymbol.Type.IsUnmanagedType)
                {
                    diagnosticList.Add(Diagnostic.Create(
                        Diag.PROP004,
                        fieldSymbol.Locations[0],
                        fieldSymbol.Name,
                        componentSymbol.ToDisplayString(),
                        fieldSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    diagnostics = diagnosticList.ToImmutableArray();
                    return ImmutableArray<PropertyLeafModel>.Empty;
                }

                if (values.HasKindOverride)
                {
                    diagnosticList.Add(Diagnostic.Create(
                        Diag.PROP006,
                        fieldSymbol.Locations[0],
                        fieldSymbol.Name,
                        componentSymbol.ToDisplayString(),
                        values.Kind.ToString(),
                        fieldSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    diagnostics = diagnosticList.ToImmutableArray();
                    return ImmutableArray<PropertyLeafModel>.Empty;
                }

                // For auto-expanded vector types, also emit a "root" property (Vec2/Vec3/Vec4)
                // that references the same underlying field as the expanded channels.
                // Channel leaves can be hidden by default unless ExpandSubfields is explicitly set.
                int rootChannelLeafIndex = -1;
                if (autoExpand && PropertyTypeMap.TryGetKind(fieldSymbol.Type, out var vectorKind) &&
                    (vectorKind == PropertyKind.Vec2 || vectorKind == PropertyKind.Vec3 || vectorKind == PropertyKind.Vec4))
                {
                    var segments = ImmutableArray.Create(new FieldPathSegment(fieldSymbol.Name, fieldSymbol.Type, fieldSymbol.IsReadOnly));
                    string identifier = NameFormatter.ToIdentifierSegment(fieldSymbol.Name);
                    string propertyPath = BuildPropertyPath(componentSymbol, segments);
                    ulong propertyId = PropertyIdHelper.Compute(propertyPath);

                    // Ensure the root property sorts just before its expanded channels.
                    int rootOrder = (baseOrder * 10) - 1;
                    rootChannelLeafIndex = leaves.Count;
                    leaves.Add(new PropertyLeafModel(
                        identifier,
                        baseDisplayName,
                        groupName,
                        rootOrder,
                        vectorKind,
                        flags,
                        values.Min,
                        values.Max,
                        values.Step,
                        propertyId,
                        hasChannels: true,
                        isChannel: false,
                        channelGroupId: propertyId,
                        channelIndex: 0,
                        channelCount: 0,
                        segments,
                        fieldSymbol.Type));
                }

                var rootSegments = new List<FieldPathSegment>
                {
                    new FieldPathSegment(fieldSymbol.Name, fieldSymbol.Type, fieldSymbol.IsReadOnly)
                };
                ulong channelGroupId = PropertyIdHelper.Compute(BuildPropertyPath(componentSymbol, rootSegments));

                int groupLeafStartIndex = leaves.Count;
                int leafIndex = 0;
                PropertyFlags expandedFlags = flags;
                if (autoExpand && !forceExpandUnmanaged && !values.ExpandSubfields)
                {
                    expandedFlags |= PropertyFlags.Hidden;
                }
                bool expanded = ExpandType(
                    componentSymbol,
                    fieldSymbol,
                    fieldSymbol.Type,
                    rootSegments,
                    baseDisplayName,
                    groupName,
                    baseOrder,
                    expandedFlags,
                    values.Min,
                    values.Max,
                    values.Step,
                    forceExpandUnmanaged,
                    channelGroupId,
                    leaves,
                    diagnosticList,
                    ref leafIndex);

                if (!expanded)
                {
                    diagnostics = diagnosticList.ToImmutableArray();
                    return ImmutableArray<PropertyLeafModel>.Empty;
                }

                ushort channelCount = (ushort)leafIndex;
                for (int i = groupLeafStartIndex; i < leaves.Count; i++)
                {
                    leaves[i].ChannelCount = channelCount;
                }

                if (rootChannelLeafIndex >= 0)
                {
                    leaves[rootChannelLeafIndex].ChannelCount = channelCount;
                }
            }
            else
            {
                if (!TryResolveKind(fieldSymbol.Type, values, fieldSymbol, componentSymbol, diagnosticList, out var kind))
                {
                    diagnostics = diagnosticList.ToImmutableArray();
                    return ImmutableArray<PropertyLeafModel>.Empty;
                }

                var segments = ImmutableArray.Create(new FieldPathSegment(fieldSymbol.Name, fieldSymbol.Type, fieldSymbol.IsReadOnly));
                string identifier = NameFormatter.ToIdentifierSegment(fieldSymbol.Name);
                string propertyPath = BuildPropertyPath(componentSymbol, segments);
                ulong propertyId = PropertyIdHelper.Compute(propertyPath);

                leaves.Add(new PropertyLeafModel(
                    identifier,
                    baseDisplayName,
                    groupName,
                    baseOrder,
                    kind,
                    flags,
                    values.Min,
                    values.Max,
                    values.Step,
                    propertyId,
                    hasChannels: false,
                    isChannel: false,
                    channelGroupId: 0,
                    channelIndex: 0,
                    channelCount: 0,
                    segments,
                    fieldSymbol.Type));
            }

            diagnostics = diagnosticList.ToImmutableArray();
            return leaves.ToImmutableArray();
        }

        private static bool TryGetInlineArrayElementType(ITypeSymbol fieldType, out ITypeSymbol elementType, out int length)
        {
            elementType = fieldType;
            length = 0;

            if (fieldType is not INamedTypeSymbol named)
            {
                return false;
            }

            int inlineLength = 0;
            foreach (var attr in named.GetAttributes())
            {
                string? fullName = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (fullName == "global::System.Runtime.CompilerServices.InlineArrayAttribute")
                {
                    if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is int len)
                    {
                        inlineLength = len;
                    }
                    break;
                }
            }

            if (inlineLength <= 0)
            {
                return false;
            }

            foreach (var member in named.GetMembers())
            {
                if (member is IFieldSymbol f && !f.IsStatic)
                {
                    elementType = f.Type;
                    length = inlineLength;
                    return true;
                }
            }

            return false;
        }

        private static string BuildArrayElementDisplayName(string baseDisplayName, in PropertyArrayValues arrayValues, int elementIndex)
        {
            if (!arrayValues.IsArray2D)
            {
                return baseDisplayName + " " + elementIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            int cols = arrayValues.Cols;
            if (cols <= 0)
            {
                return baseDisplayName + " " + elementIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            int row = elementIndex / cols;
            int col = elementIndex - row * cols;
            return baseDisplayName + " " +
                row.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                col.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool ExpandType(
            INamedTypeSymbol componentSymbol,
            IFieldSymbol rootField,
            ITypeSymbol currentType,
            List<FieldPathSegment> currentSegments,
            string displayNamePrefix,
            string groupName,
            int baseOrder,
            PropertyFlags flags,
            float min,
            float max,
            float step,
            bool forceExpandUnmanaged,
            ulong channelGroupId,
            List<PropertyLeafModel> leaves,
            List<Diagnostic> diagnostics,
            ref int leafIndex)
        {
            var instanceFields = new List<IFieldSymbol>();
            foreach (var member in currentType.GetMembers())
            {
                if (member is IFieldSymbol fieldSymbol && !fieldSymbol.IsStatic)
                {
                    instanceFields.Add(fieldSymbol);
                }
            }

            if (instanceFields.Count == 0)
            {
                diagnostics.Add(Diagnostic.Create(
                    Diag.PROP005,
                    rootField.Locations[0],
                    rootField.Name,
                    componentSymbol.ToDisplayString(),
                    currentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                return false;
            }

            foreach (var fieldSymbol in instanceFields)
            {
                if (fieldSymbol.IsConst || fieldSymbol.IsReadOnly)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diag.PROP005,
                        rootField.Locations[0],
                        rootField.Name,
                        componentSymbol.ToDisplayString(),
                        fieldSymbol.Name));
                    return false;
                }

                string segmentDisplayName = displayNamePrefix + " " + NameFormatter.ToDisplayName(fieldSymbol.Name);
                var segment = new FieldPathSegment(fieldSymbol.Name, fieldSymbol.Type, fieldSymbol.IsReadOnly);
                currentSegments.Add(segment);

                bool autoExpand = PropertyTypeMap.IsAutoExpandable(fieldSymbol.Type);
                bool isLeafType = PropertyTypeMap.TryGetKind(fieldSymbol.Type, out var leafKind) && !autoExpand;

                if (autoExpand || (forceExpandUnmanaged && fieldSymbol.Type.IsUnmanagedType && !isLeafType))
                {
                    if (!ExpandType(
                        componentSymbol,
                        rootField,
                        fieldSymbol.Type,
                        currentSegments,
                        segmentDisplayName,
                        groupName,
                        baseOrder,
                        flags,
                        min,
                        max,
                        step,
                        forceExpandUnmanaged,
                        channelGroupId,
                        leaves,
                        diagnostics,
                        ref leafIndex))
                    {
                        currentSegments.RemoveAt(currentSegments.Count - 1);
                        return false;
                    }
                }
                else
                {
                    if (!PropertyTypeMap.TryGetKind(fieldSymbol.Type, out leafKind))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            Diag.PROP003,
                            rootField.Locations[0],
                            rootField.Name,
                            componentSymbol.ToDisplayString(),
                            fieldSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                        currentSegments.RemoveAt(currentSegments.Count - 1);
                        return false;
                    }

                    int order = (baseOrder * 10) + leafIndex;
                    ushort channelIndex = (ushort)leafIndex;
                    leafIndex++;

                    string identifier = BuildIdentifier(currentSegments);
                    string propertyPath = BuildPropertyPath(componentSymbol, currentSegments);
                    ulong propertyId = PropertyIdHelper.Compute(propertyPath);

                    leaves.Add(new PropertyLeafModel(
                        identifier,
                        segmentDisplayName,
                        groupName,
                        order,
                        leafKind,
                        flags,
                        min,
                        max,
                        step,
                        propertyId,
                        hasChannels: false,
                        isChannel: true,
                        channelGroupId: channelGroupId,
                        channelIndex: channelIndex,
                        channelCount: 0,
                        currentSegments.ToImmutableArray(),
                        fieldSymbol.Type));
                }

                currentSegments.RemoveAt(currentSegments.Count - 1);
            }

            return true;
        }

        private static bool TryResolveKind(
            ITypeSymbol fieldType,
            PropertyAttributeValues values,
            IFieldSymbol fieldSymbol,
            INamedTypeSymbol componentSymbol,
            List<Diagnostic> diagnostics,
            out PropertyKind kind)
        {
            if (values.HasKindOverride)
            {
                if (PropertyTypeMap.TryGetKind(fieldType, out var inferred) && inferred == values.Kind)
                {
                    kind = values.Kind;
                    return true;
                }

                diagnostics.Add(Diagnostic.Create(
                    Diag.PROP006,
                    fieldSymbol.Locations[0],
                    fieldSymbol.Name,
                    componentSymbol.ToDisplayString(),
                    values.Kind.ToString(),
                    fieldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                kind = PropertyKind.Auto;
                return false;
            }

            if (!PropertyTypeMap.TryGetKind(fieldType, out kind))
            {
                diagnostics.Add(Diagnostic.Create(
                    Diag.PROP003,
                    fieldSymbol.Locations[0],
                    fieldSymbol.Name,
                    componentSymbol.ToDisplayString(),
                    fieldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                return false;
            }

            return true;
        }

        private static string BuildIdentifier(IReadOnlyList<FieldPathSegment> segments)
        {
            string result = string.Empty;
            for (int index = 0; index < segments.Count; index++)
            {
                var seg = segments[index];
                if (seg.IsArrayElement)
                {
                    result += seg.FixedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    result += NameFormatter.ToIdentifierSegment(seg.Name);
                }
            }
            return result;
        }

        private static string BuildPropertyPath(INamedTypeSymbol componentSymbol, IReadOnlyList<FieldPathSegment> segments)
        {
            string componentName = componentSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string path = componentName;
            for (int index = 0; index < segments.Count; index++)
            {
                var seg = segments[index];
                if (seg.IsArrayElement)
                {
                    path += "[";
                    path += seg.FixedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    path += "]";
                }
                else
                {
                    path += ".";
                    path += seg.Name;
                }
            }
            return path;
        }
    }
}
