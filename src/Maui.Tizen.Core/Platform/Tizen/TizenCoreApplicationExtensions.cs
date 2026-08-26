using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platforms.Tizen.LifecycleEvents;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.NUI;
using TizenLifecycleEvents = Microsoft.Maui.Platforms.Tizen.LifecycleEvents.TizenLifecycle;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Application/window wiring helpers.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Platform.CoreAppExtensions</c> and
	/// <c>Microsoft.Maui.Platform.ElementExtensions</c> (Tizen) in dotnet/maui, both
	/// <c>internal</c> there.
	/// </remarks>
	public static class TizenCoreApplicationExtensions
	{
		// Keyed weakly so a disposed window does not keep its content, or its handlers, alive.
		static readonly ConditionalWeakTable<TizenNativeWindow, TizenNativeView> s_windowContent = new();
		static readonly ConditionalWeakTable<TizenNativeWindow, Action> s_windowCloseRequestHandler = new();
		static readonly ConditionalWeakTable<TizenNativeWindow, Func<bool>> s_windowBackButtonPressedHandler = new();

		/// <summary>Gets the process-wide NUI window.</summary>
		/// <remarks>
		/// dotnet/maui uses <c>Window.Instance</c>, which TizenFX deprecated in API12 in favour of
		/// <c>Window.Default</c>. This backend targets API15, so the modern member is used.
		/// </remarks>
		/// <returns>The default window.</returns>
		public static TizenNativeWindow GetDefaultWindow() => Window.Default;

		/// <summary>Creates and attaches the <see cref="IApplication"/> handler.</summary>
		/// <param name="platformApplication">The platform application.</param>
		/// <param name="application">The cross-platform application.</param>
		/// <param name="context">The application-scoped context.</param>
		public static void SetApplicationHandler(
			this TizenNativeApplication platformApplication,
			IApplication application,
			IMauiContext context)
		{
			ArgumentNullException.ThrowIfNull(platformApplication);
			ArgumentNullException.ThrowIfNull(application);
			ArgumentNullException.ThrowIfNull(context);

			SetHandler(application, platformApplication, context);
		}

		/// <summary>
		/// Creates the cross-platform window, its scoped context and its handler.
		/// </summary>
		/// <param name="platformApplication">The platform application.</param>
		/// <param name="application">The cross-platform application.</param>
		/// <param name="applicationContext">The application-scoped context.</param>
		/// <returns>The window DI scope, which the caller owns.</returns>
		public static IServiceScope CreatePlatformWindow(
			this TizenNativeApplication platformApplication,
			IApplication application,
			IMauiContext applicationContext)
		{
			ArgumentNullException.ThrowIfNull(platformApplication);
			ArgumentNullException.ThrowIfNull(application);
			ArgumentNullException.ThrowIfNull(applicationContext);

			var platformWindow = GetDefaultWindow()
				?? throw new InvalidOperationException("The default NUI window was not found.");

			var mauiContext = applicationContext.MakeWindowScope(platformWindow, out var windowScope);

			platformWindow.SetWindowCloseRequestHandler(platformApplication.Exit);

			applicationContext.Services.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnMauiContextCreated>(
				del => del(mauiContext));

			var activationState = new ActivationState(mauiContext);
			var window = application.CreateWindow(activationState);

			// Hardware back goes to the cross-platform window first; if it does not handle the
			// press, the close request runs and the application exits.
			platformWindow.SetBackButtonPressedHandler(window.BackButtonClicked);

			SetHandler(window, platformWindow, mauiContext);

			return windowScope;
		}

		/// <summary>Resolves a cross-platform window from its platform window.</summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <returns>The cross-platform window, or <see langword="null"/> when not found.</returns>
		public static IWindow? GetWindow(this TizenNativeWindow? platformWindow)
		{
			if (platformWindow is null)
				return null;

			foreach (var window in IPlatformApplication.Current?.Application?.Windows ?? Array.Empty<IWindow>())
			{
				if (ReferenceEquals(window?.Handler?.PlatformView, platformWindow))
					return window;
			}

			return null;
		}

		/// <summary>Sets the main content view of the platform window, replacing any previous content.</summary>
		/// <remarks>
		/// <see cref="Handlers.TizenWindowHandler.MapContent"/> is a property mapper on
		/// <see cref="IWindow.Content"/>, so it re-runs whenever the content changes. Adding without
		/// removing would leave the previous view parented and visible underneath the new one, and
		/// leak its whole NUI subtree. dotnet/maui avoids this by clearing a per-window
		/// <c>NavigationStack</c> before pushing; this backend has no modal stack yet, so it tracks
		/// the current content directly.
		/// </remarks>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="content">The content view.</param>
		public static void SetMainContent(this TizenNativeWindow platformWindow, TizenNativeView content)
		{
			ArgumentNullException.ThrowIfNull(platformWindow);
			ArgumentNullException.ThrowIfNull(content);

			if (s_windowContent.TryGetValue(platformWindow, out var previous))
			{
				if (ReferenceEquals(previous, content))
					return;

				platformWindow.Remove(previous);
			}

			content.WidthSpecification = LayoutParamPolicies.MatchParent;
			content.HeightSpecification = LayoutParamPolicies.MatchParent;
			content.WidthResizePolicy = ResizePolicyType.FillToParent;
			content.HeightResizePolicy = ResizePolicyType.FillToParent;

			platformWindow.Add(content);

			s_windowContent.Remove(platformWindow);
			s_windowContent.Add(platformWindow, content);
		}

		/// <summary>
		/// Registers the available orientations and the hardware back-key handler on the platform
		/// window.
		/// </summary>
		/// <remarks>
		/// Ported from the workable subset of <c>WindowExtensions.Initialize</c> in dotnet/maui.
		/// The <c>NavigationStack</c> that the upstream version also creates is not included: modal
		/// navigation is outside this vertical slice. Without this call the device would never
		/// rotate and the hardware back key would be inert.
		/// </remarks>
		/// <param name="platformWindow">The platform window.</param>
		public static void InitializePlatformWindow(this TizenNativeWindow platformWindow)
		{
			ArgumentNullException.ThrowIfNull(platformWindow);

			platformWindow.AddAvailableOrientation(Window.WindowOrientation.Landscape);
			platformWindow.AddAvailableOrientation(Window.WindowOrientation.LandscapeInverse);
			platformWindow.AddAvailableOrientation(Window.WindowOrientation.Portrait);
			platformWindow.AddAvailableOrientation(Window.WindowOrientation.PortraitInverse);

			platformWindow.KeyEvent += (_, e) =>
			{
				if (e.Key.IsDeclineKeyEvent())
					OnBackButtonPressed(platformWindow);
			};
		}

		/// <summary>Registers the handler invoked when the window asks to close.</summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="handler">The handler.</param>
		public static void SetWindowCloseRequestHandler(this TizenNativeWindow platformWindow, Action handler)
		{
			ArgumentNullException.ThrowIfNull(platformWindow);
			ArgumentNullException.ThrowIfNull(handler);

			s_windowCloseRequestHandler.Remove(platformWindow);
			s_windowCloseRequestHandler.Add(platformWindow, handler);
		}

		/// <summary>
		/// Registers the handler invoked when the hardware back key is pressed. Returning
		/// <see langword="true"/> marks the press as handled and suppresses the close request.
		/// </summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="handler">The handler.</param>
		public static void SetBackButtonPressedHandler(this TizenNativeWindow platformWindow, Func<bool> handler)
		{
			ArgumentNullException.ThrowIfNull(platformWindow);
			ArgumentNullException.ThrowIfNull(handler);

			s_windowBackButtonPressedHandler.Remove(platformWindow);
			s_windowBackButtonPressedHandler.Add(platformWindow, handler);
		}

		static void OnBackButtonPressed(TizenNativeWindow platformWindow)
		{
			if (s_windowBackButtonPressedHandler.TryGetValue(platformWindow, out var backHandler) && backHandler())
				return;

			if (s_windowCloseRequestHandler.TryGetValue(platformWindow, out var closeHandler))
				closeHandler();
		}

		static void SetHandler(IElement element, object platformElement, IMauiContext context)
		{
			_ = platformElement;

			var handler = element.Handler;
			if (handler is null)
			{
				handler = context.Handlers.GetHandler(element.GetType())
					?? throw new InvalidOperationException(
						$"No handler is registered for {element.GetType().FullName}. "
						+ "Call IMauiHandlersCollection.AddTizenHandlers() from ConfigureMauiHandlers.");

				handler.SetMauiContext(context);
			}

			element.Handler = handler;
			handler.SetVirtualView(element);
		}
	}
}
