using System;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementation of <see cref="IAlertManager"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// .NET MAUI resolves <see cref="IAlertManager"/> from the window's scoped service provider
	/// and, when one is registered, uses it in place of the built-in manager. Register this type
	/// with a scoped lifetime so that every window gets its own manager and therefore its own
	/// window-affine subscription.
	/// </para>
	/// <para>
	/// The Tizen backend supplies a full manager rather than only an
	/// <see cref="IAlertManagerSubscription"/> because native NUI popups must be dismissed
	/// explicitly. The built-in manager treats <see cref="Unsubscribe"/> as "drop the reference",
	/// which on Tizen would leave an orphaned modal popup on screen holding native resources and
	/// would leave the awaiting <c>DisplayAlertAsync</c> caller pending forever. This manager
	/// disposes its subscription on <see cref="Unsubscribe"/>, which dismisses in-flight dialogs
	/// and completes their callers with the documented cancellation result.
	/// </para>
	/// </remarks>
	public sealed class TizenAlertManager : IAlertManager, IDisposable
	{
		readonly ITizenWindowContext _windowContext;
		readonly ITizenAlertDialogFactory _dialogs;
		readonly ITizenModalHost _modalHost;
		readonly ITizenPlatformWindowProvider _windowProvider;
		readonly ILogger<TizenAlertManager>? _logger;

		TizenAlertManagerSubscription? _subscription;
		bool _disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenAlertManager"/> class.
		/// </summary>
		/// <param name="windowContext">Identifies the window this manager serves.</param>
		/// <param name="dialogs">Creates the dialogs used to service requests.</param>
		/// <param name="modalHost">Coordinates dialogs with the Tizen modal navigation stack.</param>
		/// <param name="windowProvider">Resolves the native window that a requesting page belongs to.</param>
		/// <param name="logger">Optional logger used to report unexpected subscription states.</param>
		public TizenAlertManager(
			ITizenWindowContext windowContext,
			ITizenAlertDialogFactory dialogs,
			ITizenModalHost modalHost,
			ITizenPlatformWindowProvider windowProvider,
			ILogger<TizenAlertManager>? logger = null)
		{
			_windowContext = windowContext ?? throw new ArgumentNullException(nameof(windowContext));
			_dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
			_modalHost = modalHost ?? throw new ArgumentNullException(nameof(modalHost));
			_windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
			_logger = logger;
		}

		/// <summary>
		/// Gets the active subscription, or <see langword="null"/> when this manager is not subscribed.
		/// </summary>
		public IAlertManagerSubscription? Subscription => _subscription;

		/// <inheritdoc/>
		/// <remarks>
		/// Subscribing while already subscribed is a safe no-op, because .NET MAUI calls this
		/// whenever the window's page handler changes, including when a page that already has a
		/// handler is assigned to the window.
		/// </remarks>
		public void Subscribe()
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			if (_subscription is not null)
			{
				_logger?.LogWarning(
					"Window already had an alert manager subscription, but a new one was requested. Keeping the existing subscription.");
				return;
			}

			_subscription = new TizenAlertManagerSubscription(
				_windowContext.PlatformWindow,
				_dialogs,
				_modalHost,
				_windowProvider);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Unsubscribing dismisses any dialog that is still on screen. Unsubscribing while not
		/// subscribed is a safe no-op, and a later <see cref="Subscribe"/> creates a fresh
		/// subscription bound to the window's current native window.
		/// </remarks>
		public void Unsubscribe()
		{
			var subscription = _subscription;
			_subscription = null;
			subscription?.Dispose();
		}

		/// <inheritdoc/>
		public void RequestAlert(Page page, AlertArguments arguments) =>
			_subscription?.OnAlertRequested(page, arguments);

		/// <inheritdoc/>
		public void RequestActionSheet(Page page, ActionSheetArguments arguments) =>
			_subscription?.OnActionSheetRequested(page, arguments);

		/// <inheritdoc/>
		public void RequestPrompt(Page page, PromptArguments arguments) =>
			_subscription?.OnPromptRequested(page, arguments);

		/// <inheritdoc/>
		/// <remarks>
		/// Page busy notifications are obsolete and have no replacement, but
		/// <see cref="Page.IsBusy"/> still routes through this member, so the Tizen backend
		/// forwards it rather than dropping it.
		/// </remarks>
		[Obsolete("Page busy notifications are obsolete and have no replacement. Remove usage. This method will be removed in a future release.")]
		public void RequestPageBusy(Page page, bool isBusy) =>
#pragma warning disable CS0618 // Deliberately forwarding the obsolete notification.
			_subscription?.OnPageBusy(page, isBusy);
#pragma warning restore CS0618

		/// <summary>
		/// Unsubscribes and releases the manager. Called by the DI container when the window scope
		/// is disposed.
		/// </summary>
		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			Unsubscribe();
		}
	}
}
