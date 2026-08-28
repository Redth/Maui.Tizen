using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal sealed class SharedBooleanPropertyLease<TTarget>
		where TTarget : class
	{
		sealed class State
		{
			public int OwnerCount { get; set; }

			public bool OriginalValue { get; set; }
		}

		sealed class Lease : IDisposable
		{
			sealed class Registration
			{
				public Registration(SharedBooleanPropertyLease<TTarget> owner, TTarget target)
				{
					Owner = owner;
					Target = target;
				}

				public SharedBooleanPropertyLease<TTarget> Owner { get; }

				public TTarget Target { get; }
			}

			Registration? _registration;

			public Lease(SharedBooleanPropertyLease<TTarget> owner, TTarget target)
			{
				_registration = new Registration(owner, target);
			}

			public void Dispose()
			{
				var registration = Interlocked.Exchange(ref _registration, null);

				if (registration is not null)
				{
					registration.Owner.Release(registration.Target);
				}
			}
		}

		readonly Func<TTarget, bool> _getValue;
		readonly Action<TTarget, bool> _setValue;
		readonly ConditionalWeakTable<TTarget, State> _states = new();
		readonly object _sync = new();

		public SharedBooleanPropertyLease(
			Func<TTarget, bool> getValue,
			Action<TTarget, bool> setValue)
		{
			_getValue = getValue ?? throw new ArgumentNullException(nameof(getValue));
			_setValue = setValue ?? throw new ArgumentNullException(nameof(setValue));
		}

		public IDisposable Acquire(TTarget target)
		{
			ArgumentNullException.ThrowIfNull(target);

			lock (_sync)
			{
				var state = _states.GetOrCreateValue(target);

				if (state.OwnerCount == 0)
				{
					state.OriginalValue = _getValue(target);
					_setValue(target, true);
				}

				state.OwnerCount++;
			}

			return new Lease(this, target);
		}

		void Release(TTarget target)
		{
			lock (_sync)
			{
				if (!_states.TryGetValue(target, out var state) || state.OwnerCount == 0)
				{
					return;
				}

				state.OwnerCount--;

				if (state.OwnerCount != 0)
				{
					return;
				}

				try
				{
					_setValue(target, state.OriginalValue);
				}
				finally
				{
					_states.Remove(target);
				}
			}
		}
	}
}
