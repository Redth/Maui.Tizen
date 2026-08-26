using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

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

			return AsTizenContext(mauiContext).AddSpecific(platformApplication);
		}

		/// <summary>
		/// Creates the window-scoped context, publishing the platform window instance.
		/// </summary>
		/// <param name="mauiContext">The application-scoped context.</param>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="scope">The created DI scope, which the caller owns.</param>
		/// <returns>The window-scoped context.</returns>
		public static IMauiContext MakeWindowScope(
			this IMauiContext mauiContext,
			TizenNativeWindow platformWindow,
			out IServiceScope scope)
		{
			ArgumentNullException.ThrowIfNull(mauiContext);
			ArgumentNullException.ThrowIfNull(platformWindow);

			scope = mauiContext.Services.CreateScope();

			return AsTizenContext(mauiContext)
				.WithServices(scope.ServiceProvider)
				.AddSpecific(platformWindow);
		}

		static TizenMauiContext AsTizenContext(IMauiContext mauiContext) =>
			mauiContext as TizenMauiContext ?? new TizenMauiContext(mauiContext.Services);
	}
}
