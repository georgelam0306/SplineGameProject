// SPDX-License-Identifier: MIT
#nullable enable
namespace Property.Runtime
{
    public readonly struct AnyComponentHandle
    {
        public readonly ushort Kind;
        public readonly uint Index;
        public readonly uint Generation;

        public AnyComponentHandle(ushort kind, uint index, uint generation)
        {
            Kind = kind;
            Index = index;
            Generation = generation;
        }

        public bool IsNull
        {
            get { return Generation == 0; }
        }

        public bool IsValid
        {
            get { return Generation != 0; }
        }
    }
}
