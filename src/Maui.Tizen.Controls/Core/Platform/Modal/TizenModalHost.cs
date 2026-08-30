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
		sealed class PlaceholderState
		{
			public required object Placeholder { get; init; }
			public long Generation { get; set; }
			public bool OperationOwned { get; set; }
			public bool DialogActive { get; set; }
			public bool Released { get; set; }
		}

		readonly record struct RemovalResult(
			bool IsAbsent,
			bool PlaceholderDisposed,
			Exception? Failure);

		static readonly SharedBooleanPropertyLease<ITizenNavigationStack> s_shownBehindPage = new(
			static stack => stack.ShownBehindPage,
			static (stack, value) => stack.ShownBehindPage = value);

		readonly ITizenNavigationStack _stack;
		readonly ILogger<TizenModalHost>? _logger;
		readonly Dictionary<object, PlaceholderState> _placeholders = new(ReferenceEqualityComparer.Instance);

		long _nextGeneration;
		bool _disposed;

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
			ObjectDisposedException.ThrowIf(_disposed, this);

			await RetryPendingPlaceholdersAsync().ConfigureAwait(true);

			var placeholder = _stack.CreatePlaceholder();
			var generation = ++_nextGeneration;
			var state = new PlaceholderState
			{
				Placeholder = placeholder,
				Generation = generation,
				OperationOwned = true,
			};
			_placeholders.Add(placeholder, state);

			Exception? pushFailure = null;
			IDisposable? shownBehindLease = null;

			try
			{
				// Multiple/nested dialogs can push concurrently. A shared lease keeps the property
				// true until the final in-flight push finishes, then restores the first owner's
				// original value exactly once.
				shownBehindLease = s_shownBehindPage.Acquire(_stack);

				// Awaited, not fire-and-forget. A discarded task swallows the fault and lets the
				// dialog open over a stack that never actually took the placeholder, which then
				// unbalances the pop.
				await _stack.PushAsync(placeholder, false).ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				pushFailure = ex;
			}
			finally
			{
				try
				{
					shownBehindLease?.Dispose();
				}
				catch (Exception ex)
				{
					pushFailure = CombineFailures(
						"The placeholder push and ShownBehindPage restoration both failed.",
						pushFailure,
						ex);
				}
			}

			Exception? invalidationFailure = null;
			if (_disposed || state.Generation != generation)
			{
				invalidationFailure = new ObjectDisposedException(nameof(TizenModalHost));
			}

			if (pushFailure is not null || invalidationFailure is not null)
			{
				// PushAsync may have failed before insertion, after insertion, or completed after
				// Dispose invalidated the operation. Only the operation owns cleanup until this
				// post-await point.
				var cleanupFailure = await CleanupPlaceholderAsync(state).ConfigureAwait(true);
				var failure = CombineFailures(
					"The dialog placeholder push, invalidation, and rollback failed.",
					pushFailure,
					invalidationFailure,
					cleanupFailure);
				ExceptionDispatchInfo.Capture(failure!).Throw();
			}

			state.OperationOwned = false;
			state.DialogActive = true;

			Exception? dialogFailure = null;

			try
			{
				await dialogOperation().ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				dialogFailure = ex;
			}
			finally
			{
				state.DialogActive = false;
			}

			if (state.Released)
			{
				if (dialogFailure is not null)
				{
					ExceptionDispatchInfo.Capture(dialogFailure).Throw();
				}

				return;
			}

			generation = ++_nextGeneration;
			state.Generation = generation;
			state.OperationOwned = true;

			var removal = await RemovePlaceholderAsync(placeholder).ConfigureAwait(true);
			invalidationFailure = null;
			if (_disposed || state.Generation != generation)
			{
				invalidationFailure = new ObjectDisposedException(nameof(TizenModalHost));
			}

			Exception? releaseFailure = null;
			if (removal.IsAbsent)
			{
				releaseFailure = ReleasePlaceholder(state, removal.PlaceholderDisposed);
			}
			else
			{
				state.OperationOwned = false;
			}

			var finalFailure = CombineFailures(
				"The dialog operation and placeholder cleanup failed.",
				dialogFailure,
				removal.Failure,
				removal.IsAbsent
					? null
					: new InvalidOperationException(
						"The dialog placeholder remains in the native stack and was kept alive for retry."),
				invalidationFailure,
				releaseFailure);
			if (finalFailure is not null)
			{
				ExceptionDispatchInfo.Capture(finalFailure).Throw();
			}
		}

		static void DisposePlaceholder(object placeholder) =>
			(placeholder as IDisposable)?.Dispose();

		/// <inheritdoc/>
		public void Dispose()
		{
			_disposed = true;

			foreach (var state in _placeholders.Values.ToArray())
			{
				state.Generation = ++_nextGeneration;

				// The pending PushAsync/removal operation still owns the placeholder and its
				// ShownBehindPage lease. It must perform post-await cleanup.
				if (state.OperationOwned || state.Released)
				{
					continue;
				}

				try
				{
					if (_stack.Contains(state.Placeholder))
					{
						_stack.Remove(state.Placeholder);
					}

					if (_stack.Contains(state.Placeholder))
					{
						ReportTeardownFailure(
							new InvalidOperationException(
								"The dialog placeholder remained in the native stack during modal-host teardown."));
						continue;
					}

					var disposed = _stack.IsDisposed(state.Placeholder);
					var releaseFailure = ReleasePlaceholder(state, disposed);
					if (releaseFailure is not null)
					{
						ReportTeardownFailure(releaseFailure);
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
			foreach (var state in _placeholders.Values
				.Where(static state => !state.OperationOwned && !state.DialogActive && !state.Released)
				.ToArray())
			{
				var generation = ++_nextGeneration;
				state.Generation = generation;
				state.OperationOwned = true;
				var removal = await RemovePlaceholderAsync(state.Placeholder).ConfigureAwait(true);
				if (!removal.IsAbsent)
				{
					state.OperationOwned = false;
					ExceptionDispatchInfo.Capture(
						removal.Failure ?? new InvalidOperationException(
							"A previous dialog placeholder remains in the native stack.")).Throw();
				}

				var releaseFailure = ReleasePlaceholder(state, removal.PlaceholderDisposed);
				var failure = CombineFailures(
					"Retrying a previous dialog placeholder cleanup failed.",
					removal.Failure,
					releaseFailure,
					state.Generation != generation
						? new ObjectDisposedException(nameof(TizenModalHost))
						: null);
				if (failure is not null)
				{
					ExceptionDispatchInfo.Capture(failure).Throw();
				}
			}
		}

		async Task<Exception?> CleanupPlaceholderAsync(PlaceholderState state)
		{
			var removal = await RemovePlaceholderAsync(state.Placeholder).ConfigureAwait(true);

			if (!removal.IsAbsent)
			{
				state.OperationOwned = false;
				return CombineFailures(
					"The placeholder could not be confirmed absent.",
					removal.Failure,
					new InvalidOperationException(
						"The placeholder remains live in the native stack for teardown retry."));
			}

			return CombineFailures(
				"The placeholder removal and release failed.",
				removal.Failure,
				ReleasePlaceholder(state, removal.PlaceholderDisposed));
		}

		Exception? ReleasePlaceholder(PlaceholderState state, bool placeholderDisposed)
		{
			if (state.Released)
			{
				return null;
			}

			state.Released = true;
			state.OperationOwned = false;
			state.DialogActive = false;
			_placeholders.Remove(state.Placeholder);

			if (placeholderDisposed)
			{
				return null;
			}

			try
			{
				DisposePlaceholder(state.Placeholder);
				return null;
			}
			catch (Exception ex)
			{
				return ex;
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
