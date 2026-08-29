using System;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	internal sealed class TizenEventSubscriptionCoordinator<TEventArgs> : IDisposable
		where TEventArgs : EventArgs
	{
		readonly object _stateLock = new();
		readonly object _transitionLock = new();
		readonly object _sender;
		readonly Action _start;
		readonly Action _stop;
		readonly TizenNativeCallbackCoordinator _callbacks;
		EventHandler<TEventArgs>? _handlers;
		long _generation;
		bool _listening;
		bool _disposed;

		public TizenEventSubscriptionCoordinator(
			object sender,
			Action start,
			Action stop,
			TizenNativeCallbackCoordinator? callbacks = null)
		{
			_sender = sender;
			_start = start;
			_stop = stop;
			_callbacks = callbacks ?? new TizenNativeCallbackCoordinator();
		}

		public void Add(EventHandler<TEventArgs>? handler)
		{
			if (handler is null)
				return;

			lock (_transitionLock)
			{
				var start = false;
				lock (_stateLock)
				{
					ObjectDisposedException.ThrowIf(_disposed, this);
					start = _handlers is null;
					_handlers += handler;
					if (start)
						_generation++;
				}

				if (!start)
					return;

				try
				{
					_start();
					lock (_stateLock)
						_listening = true;
				}
				catch
				{
					lock (_stateLock)
					{
						_handlers -= handler;
						_generation++;
					}

					try
					{
						_stop();
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
				var stop = false;
				lock (_stateLock)
				{
					_handlers -= handler;
					if (_handlers is null && _listening)
					{
						_generation++;
						_listening = false;
						stop = true;
					}
				}

				if (stop)
					_stop();
			}
		}

		public void Publish(TEventArgs args)
		{
			long generation;
			lock (_stateLock)
			{
				if (_disposed || !_listening || _handlers is null)
					return;
				generation = _generation;
			}

			_callbacks.Post(
				() =>
				{
					lock (_stateLock)
					{
						return !_disposed &&
							_listening &&
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
				var stop = false;
				lock (_stateLock)
				{
					if (_disposed)
						return;

					_disposed = true;
					_generation++;
					_handlers = null;
					stop = _listening;
					_listening = false;
				}

				if (stop)
					_stop();
			}
		}
	}
}
