using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen-specific helpers over <see cref="IMauiContext"/>.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Platform.MauiContextExtensions</c> (Tizen) and
	/// <c>Microsoft.Maui.MauiContextExtensions</c> in dotnet/maui, both of which are
	/// <c>internal</c> there.
	/// </remarks>
	public static class TizenMauiContextExtensions
	{
		/// <summary>Gets the platform window registered in the context.</summary>
		/// <param name="mauiContext">The context.</param>
		/// <returns>The platform window.</returns>
		public static TizenNativeWindow GetPlatformWindow(this IMauiContext mauiContext)
		{
			ArgumentNullException.ThrowIfNull(mauiContext);

			return mauiContext.Services.GetService(typeof(TizenNativeWindow)) as TizenNativeWindow
				?? throw new InvalidOperationException(
					$"No {typeof(TizenNativeWindow).FullName} was registered in the {nameof(IMauiContext)}. "
					+ "The window scope is created by TizenMauiApplication during OnCreate.");
		}

		/// <summary>Tries to get the platform window registered in the context.</summary>
		/// <param name="mauiContext">The context.</param>
		/// <returns>The platform window, or <see langword="null"/> when none is registered.</returns>
		public static TizenNativeWindow? GetPlatformWindowOrDefault(this IMauiContext mauiContext)
		{
			ArgumentNullException.ThrowIfNull(mauiContext);

			return mauiContext.Services.GetService(typeof(TizenNativeWindow)) as TizenNativeWindow;
		}

		/// <summary>Gets the platform application registered in the context.</summary>
		/// <param name="mauiContext">The context.</param>
		/// <returns>The platform application, or <see langword="null"/> when none is registered.</returns>
		public static TizenNativeApplication? GetPlatformApplicationOrDefault(this IMauiContext mauiContext)
		{
			ArgumentNullException.ThrowIfNull(mauiContext);

			return mauiContext.Services.GetService(typeof(TizenNativeApplication)) as TizenNativeApplication;
		}

		/// <summary>
		/// Creates the application-scoped context, publishing the platform application instance.
		/// </summary>
		/// <param name="mauiContext">The root context.</param>
		/// <param name="platformApplication">The platform application.</param>
		/// <returns>The application-scoped context.</returns>
		public static IMauiContext MakeApplicationScope(
			this IMauiContext mauiContext,
			TizenNativeApplication platformApplication)
		{
			ArgumentNullException.ThrowIfNull(mauiContext);
			ArgumentNullException.ThrowIfNull(platformApplication);

			// A NEW context, never the caller's. AsTizenContext returns its argument unchanged when
			// it is already a TizenMauiContext, so routing through it here would publish the
			// platform application into the ROOT context and silently pollute any context a caller
			// passed in. MakeWindowScope already gets this right via WithServices.
			return new TizenMauiContext(mauiContext.Services).AddSpecific(platformApplication);
		}

		/// <summary>
		/// Creates the window-scoped context, publishing the platform window instance.
		/// </summary>
		/// <param name="mauiContext">The application-scoped context.</param>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="scope">The created DI scope, which the caller owns.</param>
		/// <returns>The window-scoped context.</returns>
		/// <remarks>
		/// Registered <see cref="IMauiInitializeScopedService"/> implementations are run against
		/// the new context before it is returned, matching MAUI's own
		/// <c>MauiContextExtensions.MakeWindowScope</c>. Skipping this is not benign: MAUI's
		/// dispatcher registers a scoped initializer that primes the window's dispatcher, and a
		/// host's own scoped initializers would silently never run.
		/// </remarks>
		public static IMauiContext MakeWindowScope(
			this IMauiContext mauiContext,
			TizenNativeWindow platformWindow,
			out IServiceScope scope)
			=> MakeWindowScope(mauiContext, platformWindow, platformSpecific: null, out scope);

		internal static IMauiContext MakeWindowScope(
			this IMauiContext mauiContext,
			TizenNativeWindow platformWindow,
			object? platformSpecific,
			out IServiceScope scope)
		{
			ArgumentNullException.ThrowIfNull(mauiContext);
			ArgumentNullException.ThrowIfNull(platformWindow);

			scope = mauiContext.Services.CreateScope();

			var scopedContext = AsTizenContext(mauiContext)
				.WithServices(scope.ServiceProvider)
				.AddSpecific(platformWindow);

			scopedContext.AddSpecific<IMauiContext>(scopedContext);

			if (platformSpecific is not null)
				scopedContext.AddSpecific(platformSpecific.GetType(), platformSpecific);

			scopedContext.InitializeTizenScopedServices();

			return scopedContext;
		}

		/// <summary>
		/// Runs every registered <see cref="IMauiInitializeScopedService"/> against the context.
		/// </summary>
		/// <remarks>
		/// MAUI's own <c>MauiContextExtensions.InitializeScopedServices</c> is a public method on
		/// an <c>internal</c> class, so it is unreachable from outside the assembly. See
		/// docs/net11-status.md ("Required public MAUI API gaps").
		/// </remarks>
		/// <param name="scopedContext">The window-scoped context.</param>
		public static void InitializeTizenScopedServices(this IMauiContext scopedContext)
		{
			ArgumentNullException.ThrowIfNull(scopedContext);

			var scopedServices = scopedContext.Services.GetServices<IMauiInitializeScopedService>();
			if (scopedServices is null)
				return;

			foreach (var service in scopedServices)
				service.Initialize(scopedContext.Services);
		}

		static TizenMauiContext AsTizenContext(IMauiContext mauiContext) =>
			mauiContext as TizenMauiContext ?? new TizenMauiContext(mauiContext.Services);
	}
}
