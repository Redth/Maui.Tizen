using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Services alert, action sheet, prompt and page-busy requests for a single Tizen window.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the Tizen port of the NUI <c>AlertRequestHelper</c> that used to live in
	/// dotnet/maui. It keeps the original routing rules - window affinity, modal-stack
	/// coordination and cancellation mapping - but presents dialogs through the Tizen-owned
	/// <see cref="ITizenAlertDialogFactory"/> contract instead of constructing NUI popups
	/// directly, so the routing logic is independent of the native toolkit.
	/// </para>
	/// <para>
	/// An instance belongs to exactly one window. Requests raised by pages that belong to a
	/// different window are ignored, because that window's own subscription services them.
	/// </para>
	/// </remarks>
	public sealed class TizenAlertManagerSubscription : IAlertManagerSubscription, IDisposable
	{
		sealed class DialogTeardown
		{
			readonly TaskCompletionSource<Exception?> _completion = new(
				TaskCreationOptions.RunContinuationsAsynchronously);

			public Task<Exception?> Completion => _completion.Task;

			public void Complete(Exception? failure) => _completion.TrySetResult(failure);
		}

		readonly ITizenAlertDialogFactory _dialogs;
		readonly ITizenModalHost _modalHost;
		readonly ITizenPlatformWindowProvider _windowProvider;
		readonly Func<object?> _resolveWindow;
		readonly Dictionary<ITizenAlertDialog, DialogTeardown> _openDialogs = new();
		readonly object _sync = new();

		int _busyCount;
		ITizenBusyIndicator? _busyIndicator;
		bool _disposed;

		/// <summary>
		/// Initializes a new subscription whose window is resolved lazily.
		/// </summary>
		/// <param name="resolveWindow">
		/// Returns the native window this subscription serves, or <see langword="null"/> when the
		/// window has not been attached yet. Evaluated on every request rather than captured.
		/// </param>
		/// <param name="dialogs">Creates the dialogs used to service requests.</param>
		/// <param name="modalHost">Coordinates dialogs with the Tizen modal navigation stack.</param>
		/// <param name="windowProvider">Resolves the native window that a requesting page belongs to.</param>
		/// <remarks>
		/// The window is resolved per request, not captured at construction. .NET MAUI can create
		/// the page handler before the window handler has attached the native window, and a
		/// snapshot taken in that order would be <see langword="null"/> forever, silently dropping
		/// every alert for the window's lifetime.
		/// </remarks>
		public TizenAlertManagerSubscription(
			Func<object?> resolveWindow,
			ITizenAlertDialogFactory dialogs,
			ITizenModalHost modalHost,
			ITizenPlatformWindowProvider windowProvider)
		{
			_resolveWindow = resolveWindow ?? throw new ArgumentNullException(nameof(resolveWindow));
			_dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
			_modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
			_windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
		}

		/// <summary>
		/// Initializes a new subscription bound to a known native window.
		/// </summary>
		/// <param name="platformWindow">The native window this subscription serves.</param>
		/// <param name="dialogs">Creates the dialogs used to service requests.</param>
		/// <param name="modalHost">Coordinates dialogs with the Tizen modal navigation stack.</param>
		/// <param name="windowProvider">Resolves the native window that a requesting page belongs to.</param>
		public TizenAlertManagerSubscription(
			object? platformWindow,
			ITizenAlertDialogFactory dialogs,
			ITizenModalHost modalHost,
			ITizenPlatformWindowProvider windowProvider)
			: this(() => platformWindow, dialogs, modalHost, windowProvider)
		{
		}

		/// <summary>
		/// Gets the native window this subscription serves.
		/// </summary>
		public object? PlatformWindow => _resolveWindow();

		/// <inheritdoc/>
		public void OnAlertRequested(Page sender, AlertArguments arguments)
		{
			ArgumentNullException.ThrowIfNull(arguments);

			if (!ShouldHandle(sender))
			{
				return;
			}

			// The original NUI implementation reports "cancel" as the result when the alert is
			// dismissed without a selection.
			_ = ShowAsync(
				() => _dialogs.CreateAlertDialog(arguments),
				arguments.SetResult,
				static () => false,
				arguments.Result);
		}

		/// <inheritdoc/>
		public void OnActionSheetRequested(Page sender, ActionSheetArguments arguments)
		{
			ArgumentNullException.ThrowIfNull(arguments);

			if (!ShouldHandle(sender))
			{
				return;
			}

			var cancel = arguments.Cancel;
			_ = ShowAsync(
				() => _dialogs.CreateActionSheetDialog(arguments),
				arguments.SetResult,
				() => cancel,
				arguments.Result);
		}

		/// <inheritdoc/>
		public void OnPromptRequested(Page sender, PromptArguments arguments)
		{
			ArgumentNullException.ThrowIfNull(arguments);

			if (!ShouldHandle(sender))
			{
				return;
			}

			_ = ShowAsync(
				() => _dialogs.CreatePromptDialog(arguments),
				arguments.SetResult,
				static () => (string?)null,
				arguments.Result);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para>
		/// Page busy notifications are obsolete in .NET MAUI and have no replacement, but
		/// <see cref="Page.IsBusy"/> still routes through this member, so the Tizen backend keeps
		/// honouring it rather than silently dropping the request.
		/// </para>
		/// <para>
		/// This implementation deliberately deviates from the original NUI code in one respect.
		/// The original closed and disposed the busy popup on every nested "busy" notification
		/// after the first, so overlapping busy scopes dismissed the indicator early. Here the
		/// indicator stays open for as long as the reference count is positive and is only torn
		/// down once it reaches zero, which is what nested <see cref="Page.IsBusy"/> scopes imply.
		/// </para>
		/// </remarks>
		[Obsolete("Page busy notifications are obsolete and have no replacement. Remove usage. This method will be removed in a future release.")]
		public void OnPageBusy(Page sender, bool enabled)
		{
			if (!ShouldHandle(sender))
			{
				return;
			}

			_busyCount = Math.Max(0, enabled ? _busyCount + 1 : _busyCount - 1);

			if (_busyCount > 0)
			{
				_busyIndicator ??= _dialogs.CreateBusyIndicator();

				if (!_busyIndicator.IsOpen)
				{
					_busyIndicator.Open();
				}

				return;
			}

			CloseBusyIndicator();
		}

		/// <summary>
		/// Ends page-busy state when the owning page handler detaches while preserving dialogs.
		/// </summary>
		internal void Detach()
		{
			_busyCount = 0;

			try
			{
				CloseBusyIndicator();
			}
			catch (Exception ex)
			{
				ReportTeardownFailures([ex]);
			}
		}

		/// <summary>
		/// Dismisses every dialog that is still open and tears down the busy indicator.
		/// </summary>
		/// <remarks>
		/// Native NUI popups hold platform resources and stay on screen until they are explicitly
		/// closed, so a window teardown that merely dropped the subscription reference would leave
		/// an orphaned modal overlay. Disposing cancels the pending dialogs, which completes the
		/// awaiting <c>DisplayAlertAsync</c> callers with their documented cancellation result
		/// instead of hanging forever.
		/// </remarks>
		public void Dispose()
		{
			KeyValuePair<ITizenAlertDialog, DialogTeardown>[] open;
			List<Exception>? failures = null;

			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;
				open = _openDialogs.ToArray();

				// Clearing while holding the tracking lock transfers close and disposal ownership
				// before either operation can synchronously resume ShowAsync.
				_openDialogs.Clear();
			}

			foreach (var (dialog, teardown) in open)
			{
				List<Exception>? dialogFailures = null;

				try
				{
					dialog.Close();
				}
				catch (ObjectDisposedException)
				{
					// The dialog raced us to teardown; nothing left to dismiss.
				}
				catch (Exception ex)
				{
					(failures ??= new()).Add(ex);
					(dialogFailures ??= new()).Add(ex);
				}
				finally
				{
					try
					{
						dialog.Dispose();
					}
					catch (Exception ex)
					{
						(failures ??= new()).Add(ex);
						(dialogFailures ??= new()).Add(ex);
					}
				}

				teardown.Complete(
					dialogFailures is null
						? null
						: dialogFailures.Count == 1
							? dialogFailures[0]
							: new AggregateException(
								"The Tizen dialog failed during window teardown.",
								dialogFailures));
			}

			_busyCount = 0;

			try
			{
				CloseBusyIndicator();
			}
			catch (Exception ex)
			{
				(failures ??= new()).Add(ex);
			}

			ReportTeardownFailures(failures);
		}

		bool ShouldHandle(Page sender)
		{
			ArgumentNullException.ThrowIfNull(sender);

			lock (_sync)
			{
				if (_disposed)
				{
					return false;
				}
			}

			return PageIsInThisWindow(sender);
		}

		bool PageIsInThisWindow(IView sender)
		{
			var window = _resolveWindow();
			var senderWindow = _windowProvider.GetPlatformWindow(sender.Handler?.MauiContext);

			if (window is null)
			{
				// The window handler has not attached the native window yet. Treat the request as
				// ours rather than dropping it: this subscription belongs to exactly one window
				// scope, and silently swallowing an alert because of handler ordering is the worst
				// possible outcome. If the requesting page already knows its window, that is
				// authoritative and is compared normally once we know ours.
				return true;
			}

			return Equals(senderWindow, window);
		}

		async Task ShowAsync<TResult>(
			Func<ITizenAlertDialog<TResult>> createDialog,
			Action<TResult> setResult,
			Func<TResult> canceledResult,
			TaskCompletionSource<TResult> completion)
		{
			ITizenAlertDialog<TResult> dialog;

			try
			{
				// Factory execution belongs to the same completion boundary as OpenAsync. A
				// constructor can throw (or a custom factory can return null) before any native
				// callback exists, and the request's Result TCS must still complete exactly once.
				dialog = createDialog()
					?? throw new InvalidOperationException(
						$"{nameof(ITizenAlertDialogFactory)} returned a null dialog.");
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
				return;
			}

			var teardown = new DialogTeardown();

			if (!TrackDialog(dialog, teardown))
			{
				// Disposed between the affinity check and here.
				try
				{
					dialog.Dispose();
				}
				catch (Exception ex)
				{
					completion.TrySetException(ex);
					return;
				}

				setResult(canceledResult());
				return;
			}

			TResult outcome;
			Exception? failure = null;
			bool disposeDialog;

			try
			{
				outcome = canceledResult();

				// The result is captured here rather than published from inside the modal scope so
				// that the placeholder entry is popped and the native popup disposed before the
				// awaiting caller resumes. The original NUI backend published from inside the
				// scope, which let a continuation that immediately shows another dialog push onto
				// the modal stack while the previous placeholder was still on it. Result and
				// cancellation values are unchanged.
				var captured = outcome;

				await _modalHost.RunModalAsync(async () =>
				{
					try
					{
						var openTask = dialog.OpenAsync();
						var completed = await Task.WhenAny(openTask, teardown.Completion).ConfigureAwait(true);

						if (ReferenceEquals(completed, teardown.Completion))
						{
							captured = canceledResult();
							return;
						}

						captured = await openTask.ConfigureAwait(true);
					}
					catch (OperationCanceledException)
					{
						captured = canceledResult();
					}
				}).ConfigureAwait(true);

				outcome = captured;
			}
			catch (OperationCanceledException)
			{
				// The modal stack itself was torn down while the dialog was on screen.
				outcome = canceledResult();
			}
			catch (Exception ex)
			{
				failure = ex;
				outcome = canceledResult();
			}
			finally
			{
				disposeDialog = UntrackDialog(dialog);

				try
				{
					if (disposeDialog)
					{
						dialog.Dispose();
					}
				}
				catch (Exception ex)
				{
					failure = failure is null
						? ex
						: new AggregateException(
							"The dialog operation and disposal both failed.",
							failure,
							ex);
				}
			}

			if (!disposeDialog)
			{
				// Teardown owns the dialog. Even when Close completes OpenAsync inline, defer
				// publication until both Close and Dispose have finished and their failures are known.
				outcome = canceledResult();

				var teardownFailure = await teardown.Completion.ConfigureAwait(true);

				if (teardownFailure is not null)
				{
					failure = failure is null
						? teardownFailure
						: new AggregateException(
							"The dialog operation and window teardown both failed.",
							failure,
							teardownFailure);
				}
			}

			if (failure is not null)
			{
				// Never leave the awaiting DisplayXyzAsync caller hanging on an unexpected failure.
				completion.TrySetException(failure);
				return;
			}

			setResult(outcome);
		}

		bool TrackDialog(ITizenAlertDialog dialog, DialogTeardown teardown)
		{
			lock (_sync)
			{
				if (_disposed)
				{
					return false;
				}

				_openDialogs.Add(dialog, teardown);
				return true;
			}
		}

		bool UntrackDialog(ITizenAlertDialog dialog)
		{
			lock (_sync)
			{
				return _openDialogs.Remove(dialog);
			}
		}

		void CloseBusyIndicator()
		{
			var indicator = _busyIndicator;
			_busyIndicator = null;

			if (indicator is null)
			{
				return;
			}

			try
			{
				indicator.Close();
			}
			finally
			{
				indicator.Dispose();
			}
		}

		static void ReportTeardownFailures(IReadOnlyList<Exception>? failures)
		{
			if (failures is null || failures.Count == 0)
			{
				return;
			}

			var failure = failures.Count == 1
				? failures[0]
				: new AggregateException(
					"One or more Tizen alert cleanup operations failed during framework teardown.",
					failures);

			try
			{
				Trace.TraceError("Tizen alert framework teardown failed: {0}", failure);
			}
			catch
			{
				// Framework teardown must not throw through a failing trace listener.
			}
		}
	}
}
