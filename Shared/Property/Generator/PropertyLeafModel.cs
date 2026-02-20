// SPDX-License-Identifier: MIT
#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Property.Generator
{
    internal sealed class PropertyLeafModel
    {
        public string Identifier { get; set; }
        public string DisplayName { get; }
        public string GroupName { get; }
        public int Order { get; }
        public PropertyKind Kind { get; }
        public PropertyFlags Flags { get; }
        public float Min { get; }
        public float Max { get; }
        public float Step { get; }
        public ulong PropertyId { get; }
        public bool HasChannels { get; set; }
        public bool IsChannel { get; set; }
        public ulong ChannelGroupId { get; set; }
        public ushort ChannelIndex { get; set; }
        public ushort ChannelCount { get; set; }
        public ImmutableArray<FieldPathSegment> Segments { get; }
        public ITypeSymbol LeafType { get; }
        public int Index { get; set; }

        public PropertyLeafModel(
            string identifier,
            string displayName,
            string groupName,
            int order,
            PropertyKind kind,
            PropertyFlags flags,
            float min,
            float max,
            float step,
            ulong propertyId,
            bool hasChannels,
            bool isChannel,
            ulong channelGroupId,
            ushort channelIndex,
            ushort channelCount,
            ImmutableArray<FieldPathSegment> segments,
            ITypeSymbol leafType)
        {
            Identifier = identifier;
            DisplayName = displayName;
            GroupName = groupName;
            Order = order;
            Kind = kind;
            Flags = flags;
            Min = min;
            Max = max;
            Step = step;
            PropertyId = propertyId;
            HasChannels = hasChannels;
            IsChannel = isChannel;
            ChannelGroupId = channelGroupId;
            ChannelIndex = channelIndex;
            ChannelCount = channelCount;
            Segments = segments;
            LeafType = leafType;
        }
    }
}
