using System;

namespace Profiling
{
    /// <summary>
    /// Marks a class or method for profiling. The source generator will create
    /// a scope ID constant for this scope in the ProfileScopes class.
    /// </summary>
    /// <remarks>
    /// Usage:
    /// <code>
    /// [ProfileScope("MySystem")]
    /// public class MySystem : SimTableSystem
    /// {
    ///     public override void Tick(in SimulationContext ctx)
    ///     {
    ///         using var _ = ProfileScope.Begin(ProfileScopes.MySystem);
    ///         // implementation
    ///     }
    /// }
    /// </code>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProfileScopeAttribute : Attribute
    {
        /// <summary>
        /// The name of the profiling scope. This will become a constant in ProfileScopes.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Creates a new ProfileScope attribute with the specified name.
        /// </summary>
        /// <param name="name">The scope name. Should be a valid C# identifier (no spaces or special characters).</param>
        public ProfileScopeAttribute(string name)
        {
            Name = name;
        }
    }
}
