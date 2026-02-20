// SPDX-License-Identifier: MIT
#nullable enable
using Core;
using Property;

namespace Property.Runtime
{
    public readonly struct PropertyInfo
    {
        public readonly ulong PropertyId;
        public readonly StringHandle Name;
        public readonly PropertyKind Kind;
        public readonly PropertyFlags Flags;
        public readonly StringHandle Group;
        public readonly float Min;
        public readonly float Max;
        public readonly float Step;
        public readonly int Order;
        public readonly bool HasChannels;
        public readonly bool IsChannel;
        public readonly ulong ChannelGroupId;
        public readonly ushort ChannelIndex;
        public readonly ushort ChannelCount;

        public PropertyInfo(
            ulong propertyId,
            StringHandle name,
            PropertyKind kind,
            StringHandle group,
            int order,
            float min,
            float max,
            float step,
            PropertyFlags flags,
            bool hasChannels,
            bool isChannel,
            ulong channelGroupId,
            ushort channelIndex,
            ushort channelCount)
        {
            PropertyId = propertyId;
            Name = name;
            Kind = kind;
            Group = group;
            Order = order;
            Min = min;
            Max = max;
            Step = step;
            Flags = flags;
            HasChannels = hasChannels;
            IsChannel = isChannel;
            ChannelGroupId = channelGroupId;
            ChannelIndex = channelIndex;
            ChannelCount = channelCount;
        }
    }
}
