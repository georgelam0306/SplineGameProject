// SPDX-License-Identifier: MIT
#nullable enable
namespace Property.Generator
{
    internal readonly struct PropertyAttributeValues
    {
        public string Name { get; }
        public string Group { get; }
        public int Order { get; }
        public PropertyKind Kind { get; }
        public PropertyFlags Flags { get; }
        public float Min { get; }
        public float Max { get; }
        public float Step { get; }
        public bool ExpandSubfields { get; }

        public PropertyAttributeValues(
            string name,
            string group,
            int order,
            PropertyKind kind,
            PropertyFlags flags,
            float min,
            float max,
            float step,
            bool expandSubfields)
        {
            Name = name;
            Group = group;
            Order = order;
            Kind = kind;
            Flags = flags;
            Min = min;
            Max = max;
            Step = step;
            ExpandSubfields = expandSubfields;
        }

        public bool HasKindOverride
        {
            get { return Kind != PropertyKind.Auto; }
        }
    }
}
