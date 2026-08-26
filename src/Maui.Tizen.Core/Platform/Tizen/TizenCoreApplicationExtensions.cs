using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platforms.Tizen.LifecycleEvents;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.NUI;
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

			applicationContext.Services.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnMauiContextCreated>(
				del => del(mauiContext));

			var activationState = new ActivationState(mauiContext);
			var window = application.CreateWindow(activationState);

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

		/// <summary>Sets the main content view of the platform window.</summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="content">The content view.</param>
		public static void SetMainContent(this TizenNativeWindow platformWindow, TizenNativeView content)
		{
			ArgumentNullException.ThrowIfNull(platformWindow);
			ArgumentNullException.ThrowIfNull(content);

			content.WidthResizePolicy = ResizePolicyType.FillToParent;
			content.HeightResizePolicy = ResizePolicyType.FillToParent;

			platformWindow.Add(content);
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
