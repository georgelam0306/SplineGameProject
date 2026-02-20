// SPDX-License-Identifier: MIT
#nullable enable
using System;

namespace DerpLib.DI
{
    /// <summary>
    /// Static entry point for the fluent DI configuration API.
    /// This API is compile-time only - the source generator parses these calls.
    /// </summary>
    public static class DI
    {
        /// <summary>Singleton lifetime constant for use with As().</summary>
        public static Lifetime Singleton => Lifetime.Singleton;

        /// <summary>Transient lifetime constant for use with As().</summary>
        public static Lifetime Transient => Lifetime.Transient;

        /// <summary>Begins configuration of a composition.</summary>
        public static ISetup Setup() => default!;
    }

    /// <summary>Fluent API for composition setup.</summary>
    public interface ISetup
    {
        /// <summary>Declares a runtime argument that will be passed to the constructor.</summary>
        ISetup Arg<T>(string name);

        /// <summary>Begins binding configuration for a type.</summary>
        IBinding<T> Bind<T>();

        /// <summary>Begins binding configuration for a tagged type.</summary>
        IBinding<T> Bind<T>(string tag);

        /// <summary>Begins a collection binding — multiple implementations of the same interface, resolved as T[].</summary>
        ICollectionBinding<T> BindAll<T>();

        /// <summary>Declares a child scope that can be created from this composition.</summary>
        ISetup Scope<TChild>() where TChild : class;

        /// <summary>Declares a composition root.</summary>
        ISetup Root<T>(string name);
    }

    /// <summary>Fluent API for binding configuration.</summary>
    public interface IBinding<T>
    {
        /// <summary>Sets the lifetime of the binding.</summary>
        IBindingWithLifetime<T> As(Lifetime lifetime);
    }

    /// <summary>Fluent API for binding with lifetime set. Extends ISetup for self-binding shorthand.</summary>
    public interface IBindingWithLifetime<T> : ISetup
    {
        /// <summary>Binds to a concrete type using constructor injection.</summary>
        ISetup To<TImpl>() where TImpl : T;

        /// <summary>Binds to a factory function.</summary>
        ISetup To(Func<IContext, T> factory);
    }

    /// <summary>Fluent API for collection binding configuration.</summary>
    public interface ICollectionBinding<T>
    {
        /// <summary>Sets the lifetime for all items in the collection.</summary>
        ICollectionBindingWithLifetime<T> As(Lifetime lifetime);
    }

    /// <summary>Fluent API for adding implementations to a collection binding.</summary>
    public interface ICollectionBindingWithLifetime<T> : ISetup
    {
        /// <summary>Adds an implementation to the collection.</summary>
        ICollectionBindingWithLifetime<T> Add<TImpl>() where TImpl : T;
    }

    /// <summary>Context provided to factory functions.</summary>
    public interface IContext
    {
        /// <summary>Injects a dependency.</summary>
        void Inject<TDep>(out TDep dependency);

        /// <summary>Injects a tagged dependency.</summary>
        void Inject<TDep>(string tag, out TDep dependency);

        /// <summary>Injects a dependency from the parent scope (for decorator pattern).</summary>
        void InjectFromParent<TDep>(out TDep dependency);
    }
}
