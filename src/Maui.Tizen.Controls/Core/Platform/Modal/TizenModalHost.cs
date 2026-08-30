using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Coordinates dialogs with the Tizen navigation stack.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ported from <c>Microsoft.Maui.Platform.NavigationStackExtensions.PushDummyPopupPage</c> in
	/// dotnet/maui. A placeholder entry is pushed behind the dialog so the navigation stack knows
	/// something modal is on screen, and popped once the dialog closes.
	/// </para>
	/// <para>
	/// One deliberate deviation from the original: exceptions are not swallowed. The original
	/// published the dialog result from inside this scope, so swallowing was preferable to
	/// crashing but left the awaiting caller pending forever. The placeholder is still always
	/// popped, but the failure now propagates so
	/// <see cref="TizenAlertManagerSubscription"/> can fault the caller instead of hanging it.
	/// </para>
	/// <para>
	/// This type deliberately has no dependency on NUI - it works through
	/// <see cref="ITizenNavigationStack"/> - so placeholder balance is verified by host-side tests
	/// rather than only on device.
	/// </para>
	/// </remarks>
	public sealed class TizenModalHost : ITizenModalHost, IDisposable
	{
		readonly record struct RemovalResult(
			bool IsAbsent,
			bool PlaceholderDisposed,
			Exception? Failure);

		readonly ITizenNavigationStack _stack;
		readonly ILogger<TizenModalHost>? _logger;
		readonly HashSet<object> _activePlaceholders = new();
		readonly HashSet<object> _pendingPlaceholders = new();

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenModalHost"/> class.
		/// </summary>
		/// <param name="stack">The window's navigation stack.</param>
		/// <param name="logger">Optional logger.</param>
		public TizenModalHost(ITizenNavigationStack stack, ILogger<TizenModalHost>? logger = null)
		{
			_stack = stack ?? throw new ArgumentNullException(nameof(stack));
			_logger = logger;
		}

		/// <inheritdoc/>
		public async Task RunModalAsync(Func<Task> dialogOperation)
		{
			ArgumentNullException.ThrowIfNull(dialogOperation);

			await RetryPendingPlaceholdersAsync().ConfigureAwait(true);

			var placeholder = _stack.CreatePlaceholder();
			_activePlaceholders.Add(placeholder);

			// ShownBehindPage is stack-wide state that belongs to whatever is already presented,
			// not to this dialog. It is set so the placeholder does not hide the page underneath -
			// a dialog floats above it - and then RESTORED rather than forced to false, because
			// forcing false silently reconfigures how every later push renders for the lifetime of
			// the window.
			var shownBehindPage = _stack.ShownBehindPage;
			_stack.ShownBehindPage = true;

			try
			{
				// Awaited, not fire-and-forget. A discarded task swallows the fault and lets the
				// dialog open over a stack that never actually took the placeholder, which then
				// unbalances the pop.
				await _stack.PushAsync(placeholder, false).ConfigureAwait(true);
			}
			catch (Exception pushFailure)
			{
				// Some native stacks mutate before completing their transition task. Remove by
				// identity even though PushAsync faulted so a half-completed push cannot wedge the
				// stack.
				var pushRemoval = await RemovePlaceholderAsync(placeholder).ConfigureAwait(true);
				Exception? releaseFailure = null;
				if (pushRemoval.IsAbsent)
				{
					_activePlaceholders.Remove(placeholder);
					_pendingPlaceholders.Remove(placeholder);

					try
					{
						if (!pushRemoval.PlaceholderDisposed)
						{
							DisposePlaceholder(placeholder);
						}
					}
					catch (Exception ex)
					{
						releaseFailure = ex;
					}
				}
				else
				{
					_activePlaceholders.Remove(placeholder);
					_pendingPlaceholders.Add(placeholder);
				}

				var pushCleanupFailure = CombineFailures(
					"The dialog placeholder push rollback failed.",
					pushRemoval.Failure,
					pushRemoval.IsAbsent
						? null
						: new InvalidOperationException(
							"The placeholder remained in the native stack after its push failed."),
					releaseFailure);
				if (pushCleanupFailure is not null)
				{
					throw new AggregateException(
						"The dialog placeholder push and its rollback both failed.",
						pushFailure,
						pushCleanupFailure);
				}

				ExceptionDispatchInfo.Capture(pushFailure).Throw();
				throw new InvalidOperationException("Unreachable.");
			}
			finally
			{
				_stack.ShownBehindPage = shownBehindPage;
			}

			Exception? dialogFailure = null;

			try
			{
				await dialogOperation().ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				dialogFailure = ex;
			}

			RemovalResult removal;
			if (ReferenceEquals(_stack.Top, placeholder))
			{
				removal = await RemovePlaceholderAsync(placeholder).ConfigureAwait(true);
			}
			else
			{
				_logger?.LogDebug(
					"The dialog placeholder was no longer on top of the navigation stack; removing it by identity instead of popping.");
				removal = await RemovePlaceholderAsync(placeholder).ConfigureAwait(true);
			}

			Exception? cleanupFailure = removal.Failure;
			if (removal.IsAbsent)
			{
				_activePlaceholders.Remove(placeholder);
				_pendingPlaceholders.Remove(placeholder);

				try
				{
					if (!removal.PlaceholderDisposed)
					{
						DisposePlaceholder(placeholder);
					}
				}
				catch (Exception ex)
				{
					cleanupFailure = CombineFailures(
						"The placeholder removal and disposal both failed.",
						cleanupFailure,
						ex);
				}
			}
			else
			{
				_activePlaceholders.Remove(placeholder);
				_pendingPlaceholders.Add(placeholder);
				cleanupFailure = CombineFailures(
					"The placeholder remained live after cleanup.",
					cleanupFailure,
					new InvalidOperationException(
						"The dialog placeholder remains in the native stack and was kept alive for retry."));
			}

			if (dialogFailure is not null && cleanupFailure is not null)
			{
				throw new AggregateException(
					"The dialog operation and placeholder cleanup both failed.",
					dialogFailure,
					cleanupFailure);
			}

			if (cleanupFailure is not null)
			{
				ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
			}

			if (dialogFailure is not null)
			{
				ExceptionDispatchInfo.Capture(dialogFailure).Throw();
			}
		}

		static void DisposePlaceholder(object placeholder) =>
			(placeholder as IDisposable)?.Dispose();

		/// <inheritdoc/>
		public void Dispose()
		{
			foreach (var placeholder in _activePlaceholders.Concat(_pendingPlaceholders).Distinct().ToArray())
			{
				try
				{
					if (_stack.Contains(placeholder))
					{
						_stack.Remove(placeholder);
					}

					if (_stack.Contains(placeholder))
					{
						ReportTeardownFailure(
							new InvalidOperationException(
								"The dialog placeholder remained in the native stack during modal-host teardown."));
						continue;
					}

					var disposed = _stack.IsDisposed(placeholder);
					_activePlaceholders.Remove(placeholder);
					_pendingPlaceholders.Remove(placeholder);
					if (!disposed)
					{
						DisposePlaceholder(placeholder);
					}
				}
				catch (Exception ex)
				{
					ReportTeardownFailure(ex);
				}
			}
		}

		void ReportTeardownFailure(Exception failure)
		{
			try
			{
				if (_logger is not null)
				{
					_logger.LogError(
						failure,
						"Failed to clean up a dialog placeholder during modal-host teardown.");
				}
				else
				{
					Trace.TraceError(
						"Failed to clean up a dialog placeholder during modal-host teardown: {0}",
						failure);
				}
			}
			catch
			{
				// Framework teardown must not fail because a logger or trace listener failed.
			}
		}

		async Task RetryPendingPlaceholdersAsync()
		{
			foreach (var placeholder in _pendingPlaceholders.ToArray())
			{
				var removal = await RemovePlaceholderAsync(placeholder).ConfigureAwait(true);
				if (!removal.IsAbsent)
				{
					ExceptionDispatchInfo.Capture(
						removal.Failure ?? new InvalidOperationException(
							"A previous dialog placeholder remains in the native stack.")).Throw();
				}

				_pendingPlaceholders.Remove(placeholder);
				if (!removal.PlaceholderDisposed)
				{
					DisposePlaceholder(placeholder);
				}

				if (removal.Failure is not null)
				{
					ExceptionDispatchInfo.Capture(removal.Failure).Throw();
				}
			}
		}

		async Task<RemovalResult> RemovePlaceholderAsync(object placeholder)
		{
			Exception? failure = null;

			try
			{
				if (!_stack.Contains(placeholder))
				{
					return ConfirmPlaceholderAbsent(placeholder);
				}

				if (ReferenceEquals(_stack.Top, placeholder))
				{
					try
					{
						await _stack.PopAsync(false).ConfigureAwait(true);
					}
					catch (Exception popFailure)
					{
						failure = popFailure;

						try
						{
							if (_stack.Contains(placeholder))
							{
								_stack.Remove(placeholder);
							}
						}
						catch (Exception removeFailure)
						{
							failure = CombineFailures(
								"The nonanimated placeholder rollback and identity removal both failed.",
								popFailure,
								removeFailure);
						}
					}
				}
				else
				{
					_stack.Remove(placeholder);
				}
			}
			catch (Exception ex)
			{
				failure = CombineFailures(
					"The placeholder identity removal failed.",
					failure,
					ex);
			}

			var confirmed = ConfirmPlaceholderAbsent(placeholder);
			return confirmed with
			{
				Failure = CombineFailures(
					"The placeholder removal and absence verification failed.",
					failure,
					confirmed.Failure),
			};
		}

		RemovalResult ConfirmPlaceholderAbsent(object placeholder)
		{
			try
			{
				if (_stack.Contains(placeholder))
				{
					return new RemovalResult(false, false, null);
				}

				return new RemovalResult(true, _stack.IsDisposed(placeholder), null);
			}
			catch (Exception ex)
			{
				return new RemovalResult(false, false, ex);
			}
		}

		static Exception? CombineFailures(string message, params Exception?[] failures)
		{
			var present = new List<Exception>();
			foreach (var failure in failures)
			{
				if (failure is not null
					&& !present.Any(existing => ReferenceEquals(existing, failure)))
				{
					present.Add(failure);
				}
			}

			return present.Count switch
			{
				0 => null,
				1 => present[0],
				_ => new AggregateException(message, present),
			};
		}
	}
}
