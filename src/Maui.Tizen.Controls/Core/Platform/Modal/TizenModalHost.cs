using System;
using System.Collections.Generic;
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
	public sealed class TizenModalHost : ITizenModalHost
	{
		readonly ITizenNavigationStack _stack;
		readonly ILogger<TizenModalHost>? _logger;

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

			var placeholder = _stack.CreatePlaceholder();

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
				var cleanupFailures = new List<Exception>();
				var pushPlaceholderDisposed = false;

				try
				{
					pushPlaceholderDisposed = await RemovePlaceholderAsync(placeholder).ConfigureAwait(true);
				}
				catch (Exception ex)
				{
					cleanupFailures.Add(ex);
				}

				try
				{
					if (!pushPlaceholderDisposed)
					{
						DisposePlaceholder(placeholder);
					}
				}
				catch (Exception ex)
				{
					cleanupFailures.Add(ex);
				}

				if (cleanupFailures.Count > 0)
				{
					cleanupFailures.Insert(0, pushFailure);
					throw new AggregateException("The dialog placeholder push and its rollback both failed.", cleanupFailures);
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

			Exception? cleanupFailure = null;
			var placeholderDisposed = false;

			try
			{
				// Always unwind, otherwise the stack is left permanently unbalanced. The
				// placeholder may no longer be on top if something else was pushed while the dialog
				// was open, which is why the non-top case removes it by identity.
				try
				{
					if (ReferenceEquals(_stack.Top, placeholder))
					{
						await _stack.PopAsync(false).ConfigureAwait(true);
						placeholderDisposed = true;
					}
					else
					{
						_logger?.LogDebug(
							"The dialog placeholder was no longer on top of the navigation stack; removing it by identity instead of popping.");
						if (_stack.Contains(placeholder))
						{
							placeholderDisposed = _stack.Remove(placeholder);
						}
					}
				}
				catch
				{
					// PopAsync can remove the top entry and then fault its transition task. Removal
					// is idempotent and also covers the pre-mutation failure case.
					placeholderDisposed = await RemovePlaceholderAsync(placeholder).ConfigureAwait(true);
					throw;
				}
			}
			catch (Exception ex)
			{
				cleanupFailure = ex;
			}

			try
			{
				if (!placeholderDisposed)
				{
					DisposePlaceholder(placeholder);
				}
			}
			catch (Exception ex)
			{
				cleanupFailure = cleanupFailure is null
					? ex
					: new AggregateException(cleanupFailure, ex);
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

		async Task<bool> RemovePlaceholderAsync(object placeholder)
		{
			if (!_stack.Contains(placeholder))
			{
				return _stack.IsDisposed(placeholder);
			}

			if (ReferenceEquals(_stack.Top, placeholder))
			{
				try
				{
					await _stack.PopAsync(false).ConfigureAwait(true);
					return _stack.IsDisposed(placeholder);
				}
				catch (Exception retryFailure)
				{
					try
					{
						if (_stack.Contains(placeholder))
						{
							return _stack.Remove(placeholder);
						}

						return true;
					}
					catch (Exception removeFailure)
					{
						throw new AggregateException(
							"The nonanimated placeholder rollback and identity removal both failed.",
							retryFailure,
							removeFailure);
					}
				}
			}
			else
			{
				return _stack.Remove(placeholder);
			}
		}
	}
}
