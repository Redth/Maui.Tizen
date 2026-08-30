using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// A Tizen dialog instance whose lifetime is owned by the alert infrastructure.
	/// </summary>
	public interface ITizenAlertDialog : IDisposable
	{
		/// <summary>
		/// Requests that an open dialog be dismissed without a user selection.
		/// </summary>
		/// <remarks>
		/// Calling this on a dialog that is not open must be a safe no-op. A dismissed dialog
		/// causes the pending <see cref="ITizenAlertDialog{TResult}.OpenAsync"/> task to be
		/// canceled, which the alert infrastructure translates into the operation's documented
		/// cancellation result.
		/// </remarks>
		void Close();
	}

	/// <summary>
	/// A single Tizen dialog instance that can be opened once and produces a result of
	/// type <typeparamref name="TResult"/>.
	/// </summary>
	/// <typeparam name="TResult">The type of the value produced when the dialog is dismissed.</typeparam>
	/// <remarks>
	/// Implementations wrap a native NUI popup. The alert infrastructure owns the lifetime of the
	/// dialog and always disposes it once <see cref="OpenAsync"/> has completed or faulted.
	/// </remarks>
	public interface ITizenAlertDialog<TResult> : ITizenAlertDialog
	{
		/// <summary>
		/// Opens the dialog and completes when the user dismisses it.
		/// </summary>
		/// <returns>The value selected by the user.</returns>
		/// <exception cref="TaskCanceledException">
		/// The dialog was dismissed without a user selection, for example because
		/// <see cref="ITizenAlertDialog.Close"/> was called or the owning window went away.
		/// </exception>
		Task<TResult> OpenAsync();
	}

	/// <summary>
	/// The modal busy indicator shown while a page reports itself as busy.
	/// </summary>
	/// <remarks>
	/// Page busy notifications are obsolete in .NET MAUI and have no replacement, but
	/// <see cref="Page.IsBusy"/> still routes through them, so the Tizen backend continues
	/// to honour them.
	/// </remarks>
	public interface ITizenBusyIndicator : IDisposable
	{
		/// <summary>
		/// Gets a value indicating whether the indicator is currently displayed.
		/// </summary>
		bool IsOpen { get; }

		/// <summary>
		/// Displays the indicator. Calling this while already open must be a safe no-op.
		/// </summary>
		void Open();

		/// <summary>
		/// Hides the indicator. Calling this while not open must be a safe no-op.
		/// </summary>
		void Close();
	}

	/// <summary>
	/// Creates the Tizen dialogs used to service alert, action sheet and prompt requests.
	/// </summary>
	/// <remarks>
	/// This is a Tizen-owned contract. The default implementation builds NUI popups, but
	/// applications and tests can register their own implementation to replace the presentation
	/// layer without reimplementing the alert routing, window affinity and modal coordination
	/// logic in <see cref="TizenAlertManagerSubscription"/>.
	/// </remarks>
	public interface ITizenAlertDialogFactory
	{
		/// <summary>Creates the dialog that services a <see cref="Page.DisplayAlert(string, string, string, string)"/> request.</summary>
		/// <param name="arguments">The requested alert.</param>
		ITizenAlertDialog<bool> CreateAlertDialog(AlertArguments arguments);

		/// <summary>Creates the dialog that services a <see cref="Page.DisplayActionSheet(string, string, string, string[])"/> request.</summary>
		/// <param name="arguments">The requested action sheet.</param>
		ITizenAlertDialog<string?> CreateActionSheetDialog(ActionSheetArguments arguments);

		/// <summary>Creates the dialog that services a <see cref="Page.DisplayPromptAsync(string, string, string, string, string, int, Keyboard, string)"/> request.</summary>
		/// <param name="arguments">The requested prompt.</param>
		ITizenAlertDialog<string?> CreatePromptDialog(PromptArguments arguments);

		/// <summary>Creates the modal busy indicator.</summary>
		ITizenBusyIndicator CreateBusyIndicator();
	}
}
