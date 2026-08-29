using System;
using System.Threading;
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

			_dispatcher.PostDeferred(() =>
			{
				if (isCurrent())
					callback();
			});
		}
	}

	internal interface ITizenNativeCallbackDispatcher
	{
		void PostDeferred(Action action);
	}

	sealed class TizenNativeCallbackDispatcher : ITizenNativeCallbackDispatcher
	{
		public static TizenNativeCallbackDispatcher Instance { get; } = new();

		public void PostDeferred(Action action) =>
			PostDeferred(
				MainThread.BeginInvokeOnMainThread,
				static () => SynchronizationContext.Current,
				action);

		internal static void PostDeferred(
			Action<Action> beginInvoke,
			Func<SynchronizationContext?> getContext,
			Action action)
		{
			beginInvoke(() =>
			{
				var context = getContext() ??
					throw new InvalidOperationException(
						"The Tizen main loop has no synchronization context.");
				context.Post(static state => ((Action)state!).Invoke(), action);
			});
		}
	}
}
