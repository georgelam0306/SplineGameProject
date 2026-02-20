// SPDX-License-Identifier: MIT
#nullable enable
using System;

namespace DerpLib.DI
{
    /// <summary>
    /// Marks a partial class as a DI composition root.
    /// The class must contain a static Setup() method that configures bindings via DI.Setup().
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CompositionAttribute : Attribute
    {
    }
}
