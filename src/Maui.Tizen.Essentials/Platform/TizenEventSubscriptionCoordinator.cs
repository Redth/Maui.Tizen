using System;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	internal sealed class TizenEventSubscriptionCoordinator<TEventArgs> : IDisposable
		where TEventArgs : EventArgs
	{
		readonly object _stateLock = new();
		readonly object _transitionLock = new();
		readonly object _sender;
		readonly Func<TizenEventGeneration<TEventArgs>, Action> _start;
		readonly TizenNativeCallbackCoordinator _callbacks;
		EventHandler<TEventArgs>? _handlers;
		Action? _unsubscribe;
		long _generation;
		bool _disposed;

		public TizenEventSubscriptionCoordinator(
			object sender,
			Func<TizenEventGeneration<TEventArgs>, Action> start,
			TizenNativeCallbackCoordinator? callbacks = null)
		{
			_sender = sender;
			_start = start;
			_callbacks = callbacks ?? new TizenNativeCallbackCoordinator();
		}

		public void Add(EventHandler<TEventArgs>? handler)
		{
			if (handler is null)
				return;

			lock (_transitionLock)
			{
				long generation;
				lock (_stateLock)
				{
					ObjectDisposedException.ThrowIf(_disposed, this);
					var start = _handlers is null;
					_handlers += handler;
					if (!start)
						return;

					generation = ++_generation;
				}

				Action? unsubscribe = null;
				try
				{
					// This delegate is unique to this subscription generation. A native source that
					// retains it after unsubscribe cannot be relabelled as a later generation.
					unsubscribe = _start(new(this, generation));
					lock (_stateLock)
					{
						if (_disposed || _generation != generation || _handlers is null)
							throw new ObjectDisposedException(nameof(TizenEventSubscriptionCoordinator<TEventArgs>));
						_unsubscribe = unsubscribe;
					}
				}
				catch
				{
					lock (_stateLock)
					{
						_handlers -= handler;
						_generation++;
						_unsubscribe = null;
					}

					try
					{
						unsubscribe?.Invoke();
					}
					catch (Exception)
					{
						// Preserve the startup failure while rolling back a partial subscription.
					}

					throw;
				}
			}
		}

		public void Remove(EventHandler<TEventArgs>? handler)
		{
			if (handler is null)
				return;

			lock (_transitionLock)
			{
				Action? unsubscribe = null;
				lock (_stateLock)
				{
					_handlers -= handler;
					if (_handlers is null && _unsubscribe is not null)
					{
						_generation++;
						unsubscribe = _unsubscribe;
						_unsubscribe = null;
					}
				}

				unsubscribe?.Invoke();
			}
		}

		internal void Publish(long generation, TEventArgs args) =>
			Commit(generation, () => args);

		internal void Commit(long generation, Func<TEventArgs> createArgs)
		{
			ArgumentNullException.ThrowIfNull(createArgs);
			if (!IsCurrent(generation))
				return;

			_callbacks.Post(
				() => IsCurrent(generation),
				() =>
				{
					EventHandler<TEventArgs>? handlers;
					TEventArgs args;
					lock (_stateLock)
					{
						if (_disposed || _generation != generation || _handlers is null)
							return;

						args = createArgs();
						handlers = _handlers;
					}
					handlers.Invoke(_sender, args);
				});
		}

		bool IsCurrent(long generation)
		{
			lock (_stateLock)
				return !_disposed && _generation == generation && _handlers is not null;
		}

		public void Dispose()
		{
			lock (_transitionLock)
			{
				Action? unsubscribe;
				lock (_stateLock)
				{
					if (_disposed)
						return;

					_disposed = true;
					_generation++;
					_handlers = null;
					unsubscribe = _unsubscribe;
					_unsubscribe = null;
				}

				unsubscribe?.Invoke();
			}
		}
	}

	internal readonly struct TizenEventGeneration<TEventArgs>
		where TEventArgs : EventArgs
	{
		readonly TizenEventSubscriptionCoordinator<TEventArgs> _owner;
		readonly long _generation;

		public TizenEventGeneration(
			TizenEventSubscriptionCoordinator<TEventArgs> owner,
			long generation)
		{
			_owner = owner;
			_generation = generation;
		}

		public void Publish(TEventArgs args) =>
			_owner.Publish(_generation, args);

		public void Commit(Func<TEventArgs> createArgs) =>
			_owner.Commit(_generation, createArgs);
	}
}
