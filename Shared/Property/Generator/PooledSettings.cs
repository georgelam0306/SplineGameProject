// SPDX-License-Identifier: MIT
#nullable enable
namespace Property.Generator
{
    internal readonly struct PooledSettings
    {
        public bool SoA { get; }
        public ushort PoolId { get; }

        public PooledSettings(bool soA, ushort poolId)
        {
            SoA = soA;
            PoolId = poolId;
        }
    }
}
