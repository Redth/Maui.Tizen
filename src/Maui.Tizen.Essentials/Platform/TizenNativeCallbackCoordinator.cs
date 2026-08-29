using System;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	internal sealed class TizenNativeCallbackCoordinator
	{
		readonly ITizenNativeCallbackDispatcher _dispatcher;

		public TizenNativeCallbackCoordinator()
			: this(TizenNativeCallbackDispatcher.Instance)
		{
		}

		internal TizenNativeCallbackCoordinator(ITizenNativeCallbackDispatcher dispatcher)
		{
			_dispatcher = dispatcher;
		}

		public void Post(Func<bool> isCurrent, Action callback)
		{
			ArgumentNullException.ThrowIfNull(isCurrent);
			ArgumentNullException.ThrowIfNull(callback);

			_dispatcher.Post(() =>
			{
				if (isCurrent())
					callback();
			});
		}
	}

	internal interface ITizenNativeCallbackDispatcher
	{
		void Post(Action action);
	}

	sealed class TizenNativeCallbackDispatcher : ITizenNativeCallbackDispatcher
	{
		public static TizenNativeCallbackDispatcher Instance { get; } = new();

		public void Post(Action action) => MainThread.BeginInvokeOnMainThread(action);
	}
}
