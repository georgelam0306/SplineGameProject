// SPDX-License-Identifier: MIT
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Pooled.Runtime
{
	public interface IPoolRegistry
	{
		T GetOrCreate<T>(Func<T> create) where T : class;
	}

		public sealed class World : IPoolRegistry
		{
			private readonly Dictionary<Type, object> _pools = new Dictionary<Type, object>();

			public void Clear()
			{
				_pools.Clear();
			}

			public T GetOrCreate<T>(Func<T> create) where T : class
			{
				object value;
			if (_pools.TryGetValue(typeof(T), out value))
			{
				return (T)value;
			}
			var inst = create();
			_pools[typeof(T)] = inst;
			return inst;
		}
	}

	public static class IdGenerator
	{
		static long _next = DateTime.UtcNow.Ticks;
		public static ulong Next()
		{
			return (ulong)Interlocked.Increment(ref _next);
		}
	}
}
