// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace Property.Generator
{
    internal sealed class PropertyComponentModel
    {
        public INamedTypeSymbol ComponentSymbol { get; }
        public bool IsSoA { get; }
        public ushort PoolId { get; }
        public List<PropertyLeafModel> Properties { get; }
        public List<PropertyArrayFieldModel> ArrayFields { get; }

        public PropertyComponentModel(
            INamedTypeSymbol componentSymbol,
            bool isSoA,
            ushort poolId)
        {
            ComponentSymbol = componentSymbol;
            IsSoA = isSoA;
            PoolId = poolId;
            Properties = new List<PropertyLeafModel>();
            ArrayFields = new List<PropertyArrayFieldModel>();
        }
    }
}
