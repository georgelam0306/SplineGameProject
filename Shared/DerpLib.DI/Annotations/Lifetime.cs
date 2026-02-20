// SPDX-License-Identifier: MIT
#nullable enable

namespace DerpLib.DI
{
    /// <summary>
    /// Specifies the lifetime of a binding.
    /// </summary>
    public enum Lifetime
    {
        /// <summary>
        /// A single instance is created and reused for all resolutions within the scope.
        /// </summary>
        Singleton = 0,

        /// <summary>
        /// A new instance is created for each resolution.
        /// </summary>
        Transient = 1
    }
}
