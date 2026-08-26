using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Realizes modal pages using only public .NET MAUI handler APIs.
	/// </summary>
	/// <remarks>
	/// This mirrors what <c>Microsoft.Maui.Platform.ElementExtensions.ToPlatform</c> does, but that
	/// extension is compiled per platform and has no Tizen build now that Tizen left the MAUI
	/// repository. The steps here - resolve a handler from the context's handler factory, give it
	/// the context and the virtual view, then take its container or platform view - use only
	/// public, platform-neutral API, which is also what makes modal navigation testable on the host.
	/// </remarks>
	public sealed class TizenModalPageRealizer : ITizenModalPageRealizer
	{
		/// <inheritdoc/>
		public object Realize(Page page, IMauiContext mauiContext)
		{
			ArgumentNullException.ThrowIfNull(page);
			ArgumentNullException.ThrowIfNull(mauiContext);

			// Element.Handler is IElementHandler; VisualElement narrows it to IViewHandler, so the
			// element-level member is used here to stay compatible with any handler shape.
			var element = (Element)page;
			var handler = element.Handler;

			if (handler is null)
			{
				handler = mauiContext.Handlers.GetHandler(page.GetType())
					?? throw new InvalidOperationException(
						$"No handler is registered for '{page.GetType()}', so it cannot be presented modally.");

				handler.SetMauiContext(mauiContext);
				element.Handler = handler;
			}

			if (!ReferenceEquals(handler.VirtualView, page))
			{
				handler.SetVirtualView(page);
			}

			var platformView = (handler as IViewHandler)?.ContainerView
				?? handler.PlatformView;

			return platformView
				?? throw new InvalidOperationException(
					$"The handler for '{page.GetType()}' produced no platform view, so it cannot be presented modally.");
		}

		/// <inheritdoc/>
		public void Release(Page page)
		{
			ArgumentNullException.ThrowIfNull(page);

			// Disconnecting is what releases the native views; the framework detaches the page from
			// its parent afterwards.
			page.Handler?.DisconnectHandler();
		}
	}

	/// <summary>
	/// Presents and dismisses modal pages on the Tizen navigation stack.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the port of <c>ModalNavigationManager.Tizen.cs</c> from dotnet/maui, reshaped onto
	/// the extensibility seam proposed by dotnet/maui#37853. The upstream file was an internal
	/// partial-class completion compiled into <c>Microsoft.Maui.Controls</c> and could not be
	/// supplied from outside the framework at all.
	/// </para>
	/// <para>
	/// The division of labour follows the seam: the framework owns the cross-platform modal stack,
	/// the <c>Appearing</c>/<c>Disappearing</c> notifications, the window modal events and the
	/// reconciliation loop. This type only performs the visual push and pop, which is why the
	/// upstream code's <c>SendDisappearing</c>/<c>SendAppearing</c> calls and its manual
	/// <c>_platformModalPages</c> bookkeeping are deliberately absent - keeping them would raise
	/// those events twice.
	/// </para>
	/// </remarks>
	public sealed class TizenModalNavigationPlatform : IModalNavigationPlatform
	{
		readonly IModalNavigationHost _host;
		readonly ITizenNavigationStack _stack;
		readonly ITizenModalPageRealizer _realizer;
		readonly ITizenWindowBackButton? _backButton;
		readonly ILogger<TizenModalNavigationPlatform>? _logger;

		bool _disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenModalNavigationPlatform"/> class.
		/// </summary>
		/// <param name="host">The per-window host exposing the framework's modal navigation state.</param>
		/// <param name="stack">The window's navigation stack.</param>
		/// <param name="realizer">Turns modal pages into native views.</param>
		/// <param name="backButton">Installs the back-button handler. May be <see langword="null"/>.</param>
		/// <param name="logger">Optional logger.</param>
		public TizenModalNavigationPlatform(
			IModalNavigationHost host,
			ITizenNavigationStack stack,
			ITizenModalPageRealizer realizer,
			ITizenWindowBackButton? backButton = null,
			ILogger<TizenModalNavigationPlatform>? logger = null)
		{
			_host = host ?? throw new ArgumentNullException(nameof(host));
			_stack = stack ?? throw new ArgumentNullException(nameof(stack));
			_realizer = realizer ?? throw new ArgumentNullException(nameof(realizer));
			_backButton = backButton;
			_logger = logger;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Tizen has no deferred-readiness requirement: the NUI navigation stack accepts a push as
		/// soon as the window exists. This matches <c>IsModalPlatformReady =&gt; true</c> in the
		/// upstream Tizen partial, and means <see cref="IModalNavigationHost.RequestSync"/> is
		/// never needed here.
		/// </remarks>
		public bool IsReady => !_disposed;

		/// <inheritdoc/>
		public async Task PushModalAsync(Page modal, bool animated)
		{
			ArgumentNullException.ThrowIfNull(modal);
			ObjectDisposedException.ThrowIf(_disposed, this);

			var platformView = _realizer.Realize(modal, _host.MauiContext);

			await _stack.PushAsync(platformView, animated).ConfigureAwait(true);
		}

		/// <inheritdoc/>
		public async Task PopModalAsync(Page modal, bool animated)
		{
			ArgumentNullException.ThrowIfNull(modal);

			if (_disposed)
			{
				// The window went away underneath us. The native stack is gone with it, so there is
				// nothing to dismiss and faulting here would surface as a PopModalAsync failure.
				return;
			}

			// A batch pop dismisses several modals at once, for example a Shell pop-to-root.
			// Animating the intermediate ones makes them flash on screen.
			await _stack.PopAsync(animated && !_host.IsBatchPopping).ConfigureAwait(true);

			_realizer.Release(modal);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Installs the Tizen back-button handler. The handler is resolved through the host on every
		/// press rather than captured, because the current page changes as modals come and go.
		/// </remarks>
		public void PageAttached()
		{
			if (_disposed)
			{
				return;
			}

			if (_backButton is null)
			{
				_logger?.LogDebug(
					"No ITizenWindowBackButton is registered for this window, so the hardware back button will not be routed to the current page.");
				return;
			}

			_backButton.SetBackButtonPressedHandler(OnBackButtonPressed);
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_backButton?.SetBackButtonPressedHandler(null);
		}

		bool OnBackButtonPressed() => _host.CurrentPage?.SendBackButtonPressed() ?? false;
	}

	/// <summary>
	/// Creates <see cref="TizenModalNavigationPlatform"/> instances for .NET MAUI.
	/// </summary>
	/// <remarks>
	/// Resolves the window's navigation stack from the window's service scope, so each window gets
	/// its own platform bound to its own stack. Returns <see langword="null"/> when no stack is
	/// registered, which the seam defines as "keep the built-in platform" - a partially configured
	/// host degrades instead of throwing.
	/// </remarks>
	public sealed class TizenModalNavigationPlatformFactory : IModalNavigationPlatformFactory
	{
		readonly ITizenModalPageRealizer _realizer;
		readonly ILoggerFactory? _loggerFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenModalNavigationPlatformFactory"/> class.
		/// </summary>
		/// <param name="realizer">Turns modal pages into native views.</param>
		/// <param name="loggerFactory">Optional logger factory.</param>
		public TizenModalNavigationPlatformFactory(
			ITizenModalPageRealizer realizer,
			ILoggerFactory? loggerFactory = null)
		{
			_realizer = realizer ?? throw new ArgumentNullException(nameof(realizer));
			_loggerFactory = loggerFactory;
		}

		/// <inheritdoc/>
		public IModalNavigationPlatform? CreateModalNavigationPlatform(IModalNavigationHost host)
		{
			ArgumentNullException.ThrowIfNull(host);

			var services = host.MauiContext.Services;
			var stack = services?.GetService(typeof(ITizenNavigationStack)) as ITizenNavigationStack;

			if (stack is null)
			{
				return null;
			}

			return new TizenModalNavigationPlatform(
				host,
				stack,
				_realizer,
				services?.GetService(typeof(ITizenWindowBackButton)) as ITizenWindowBackButton,
				_loggerFactory?.CreateLogger<TizenModalNavigationPlatform>());
		}
	}
}
