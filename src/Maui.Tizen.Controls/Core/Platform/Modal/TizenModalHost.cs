using System;
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

			_stack.ShownBehindPage = true;

			try
			{
				// Awaited, not fire-and-forget. A discarded task swallows the fault and lets the
				// dialog open over a stack that never actually took the placeholder, which then
				// unbalances the pop.
				await _stack.PushAsync(placeholder, false).ConfigureAwait(true);
			}
			finally
			{
				_stack.ShownBehindPage = false;
			}

			try
			{
				await dialogOperation().ConfigureAwait(true);
			}
			finally
			{
				// Always unwind, otherwise the stack is left permanently unbalanced. The
				// placeholder may no longer be on top if something else was pushed while the dialog
				// was open, which is why the non-top case removes it by identity.
				if (ReferenceEquals(_stack.Top, placeholder))
				{
					await _stack.PopAsync(false).ConfigureAwait(true);
				}
				else
				{
					_logger?.LogDebug(
						"The dialog placeholder was no longer on top of the navigation stack; removing it by identity instead of popping.");
					_stack.Remove(placeholder);
				}
			}
		}
	}
}
