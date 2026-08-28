// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Owns one active popup and rejects completions from an earlier view lifetime.
	/// </summary>
	internal sealed class TizenPopupLifecycle<TPopup>
		where TPopup : class, IDisposable
	{
		readonly object _gate = new();

		TPopup? _activePopup;
		CancellationTokenSource? _activeCancellation;
		long _generation;

		public bool IsOpen
		{
			get
			{
				lock (_gate)
					return _activePopup is not null;
			}
		}

		/// <summary>
		/// Opens and owns a popup for the supplied virtual/platform view pair.
		/// </summary>
		public async Task RunAsync<TVirtualView, TPlatformView, TResult>(
			TVirtualView virtualView,
			TPlatformView platformView,
			Func<TVirtualView?> getCurrentVirtualView,
			Func<TPlatformView?> getCurrentPlatformView,
			Func<TPopup> createPopup,
			Func<TPopup, CancellationToken, Task<TResult>> openPopup,
			Action<TPopup> closePopup,
			Func<Action, Task> dispatchOnUiThread,
			Action<TVirtualView, TResult> apply)
			where TVirtualView : class
			where TPlatformView : class
		{
			ArgumentNullException.ThrowIfNull(virtualView);
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(getCurrentVirtualView);
			ArgumentNullException.ThrowIfNull(getCurrentPlatformView);
			ArgumentNullException.ThrowIfNull(createPopup);
			ArgumentNullException.ThrowIfNull(openPopup);
			ArgumentNullException.ThrowIfNull(closePopup);
			ArgumentNullException.ThrowIfNull(dispatchOnUiThread);
			ArgumentNullException.ThrowIfNull(apply);

			TPopup popup;
			CancellationTokenSource cancellation;
			long generation;

			lock (_gate)
			{
				if (_activePopup is not null ||
					!ReferenceEquals(getCurrentVirtualView(), virtualView) ||
					!ReferenceEquals(getCurrentPlatformView(), platformView))
				{
					return;
				}

				popup = createPopup();
				cancellation = new CancellationTokenSource();
				generation = ++_generation;
				_activePopup = popup;
				_activeCancellation = cancellation;
			}

			try
			{
				TResult result;

				try
				{
					result = await openPopup(popup, cancellation.Token);
				}
				catch (OperationCanceledException)
				{
					return;
				}

				await dispatchOnUiThread(() =>
				{
					lock (_gate)
					{
						if (!IsCurrent(popup, cancellation, generation) ||
							!ReferenceEquals(getCurrentVirtualView(), virtualView) ||
							!ReferenceEquals(getCurrentPlatformView(), platformView))
						{
							return;
						}

						// The view pair cannot be replaced or cancelled between this final check
						// and the property write.
						apply(virtualView, result);
					}
				});
			}
			finally
			{
				await dispatchOnUiThread(() =>
					CompleteOnUiThread(popup, cancellation, generation, closePopup));
			}
		}

		/// <summary>
		/// Invalidates, closes and disposes the active popup from a handler's UI-thread teardown.
		/// </summary>
		public void CancelOnUiThread(Action<TPopup> closePopup)
		{
			ArgumentNullException.ThrowIfNull(closePopup);

			TPopup? popup;
			CancellationTokenSource? cancellation;

			lock (_gate)
			{
				popup = _activePopup;
				cancellation = _activeCancellation;

				if (popup is null)
					return;

				_generation++;
				_activePopup = null;
				_activeCancellation = null;
			}

			try
			{
				cancellation?.Cancel();
			}
			finally
			{
				CloseAndDispose(popup, cancellation, closePopup);
			}
		}

		bool IsCurrent(TPopup popup, CancellationTokenSource cancellation, long generation) =>
			_generation == generation &&
			ReferenceEquals(_activePopup, popup) &&
			ReferenceEquals(_activeCancellation, cancellation) &&
			!cancellation.IsCancellationRequested;

		void CompleteOnUiThread(
			TPopup popup,
			CancellationTokenSource cancellation,
			long generation,
			Action<TPopup> closePopup)
		{
			lock (_gate)
			{
				if (!IsCurrent(popup, cancellation, generation))
					return;

				_generation++;
				_activePopup = null;
				_activeCancellation = null;
			}

			try
			{
				cancellation.Cancel();
			}
			finally
			{
				CloseAndDispose(popup, cancellation, closePopup);
			}
		}

		static void CloseAndDispose(
			TPopup popup,
			CancellationTokenSource? cancellation,
			Action<TPopup> closePopup)
		{
			try
			{
				closePopup(popup);
			}
			finally
			{
				try
				{
					popup.Dispose();
				}
				finally
				{
					cancellation?.Dispose();
				}
			}
		}
	}
}
