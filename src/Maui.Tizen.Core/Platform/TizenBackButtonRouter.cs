using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal sealed class TizenBackButtonRouter
	{
		readonly object _gate = new();
		readonly Func<bool> _closeLastPopup;
		readonly List<Func<bool>> _handlers = new();
		Func<bool>? _fallback;

		public TizenBackButtonRouter(Func<bool> closeLastPopup) =>
			_closeLastPopup = closeLastPopup ?? throw new ArgumentNullException(nameof(closeLastPopup));

		public void SetFallback(Func<bool> fallback)
		{
			ArgumentNullException.ThrowIfNull(fallback);

			lock (_gate)
			{
				_fallback = fallback;
			}
		}

		public IDisposable Register(Func<bool> handler)
		{
			ArgumentNullException.ThrowIfNull(handler);

			lock (_gate)
			{
				_handlers.Add(handler);
			}

			return new Registration(this, handler);
		}

		public bool Invoke()
		{
			if (_closeLastPopup())
			{
				return true;
			}

			Func<bool>[] handlers;
			Func<bool>? fallback;

			lock (_gate)
			{
				handlers = _handlers.ToArray();
				fallback = _fallback;
			}

			for (var index = handlers.Length - 1; index >= 0; index--)
			{
				if (handlers[index]())
				return true;
			}

			return fallback?.Invoke() == true;
		}

		void Remove(Func<bool> handler)
		{
			lock (_gate)
			{
				_handlers.Remove(handler);
			}
		}

		sealed class Registration : IDisposable
		{
			TizenBackButtonRouter? _owner;
			readonly Func<bool> _handler;

			public Registration(TizenBackButtonRouter owner, Func<bool> handler)
			{
				_owner = owner;
				_handler = handler;
			}

			public void Dispose() =>
				Interlocked.Exchange(ref _owner, null)?.Remove(_handler);
		}
	}
}
