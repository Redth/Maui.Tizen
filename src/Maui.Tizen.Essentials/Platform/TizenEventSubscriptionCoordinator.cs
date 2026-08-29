using System;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	internal sealed class TizenEventSubscriptionCoordinator<TEventArgs> : IDisposable
		where TEventArgs : EventArgs
	{
		readonly object _stateLock = new();
		readonly object _transitionLock = new();
		readonly object _sender;
		readonly Func<Action<TEventArgs>, Action> _start;
		readonly TizenNativeCallbackCoordinator _callbacks;
		EventHandler<TEventArgs>? _handlers;
		Action? _unsubscribe;
		long _generation;
		bool _disposed;

		public TizenEventSubscriptionCoordinator(
			object sender,
			Func<Action<TEventArgs>, Action> start,
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
					unsubscribe = _start(args => Publish(generation, args));
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

		public void Publish(long generation, TEventArgs args)
		{
			lock (_stateLock)
			{
				if (_disposed || _generation != generation || _handlers is null)
					return;
			}

			_callbacks.Post(
				() =>
				{
					lock (_stateLock)
					{
						return !_disposed &&
							_generation == generation &&
							_handlers is not null;
					}
				},
				() =>
				{
					EventHandler<TEventArgs>? handlers;
					lock (_stateLock)
						handlers = _generation == generation ? _handlers : null;
					handlers?.Invoke(_sender, args);
				});
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
}
