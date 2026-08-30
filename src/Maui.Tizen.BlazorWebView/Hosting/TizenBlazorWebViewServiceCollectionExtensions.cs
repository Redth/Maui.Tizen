using System;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView
{
	/// <summary>
	/// Registration helpers that wire the Tizen BlazorWebView handler into a MAUI application.
	/// </summary>
	public static class TizenBlazorWebViewServiceCollectionExtensions
	{
		/// <summary>
		/// Adds the shared MAUI BlazorWebView services and replaces the default handler with
		/// <see cref="TizenBlazorWebViewHandler"/>.
		/// </summary>
		/// <remarks>
		/// This is equivalent to calling
		/// <c>services.AddMauiBlazorWebView().UsePlatformHandler&lt;TizenBlazorWebViewHandler&gt;()</c>.
		/// Handler registration is last-registration-wins, so <c>UsePlatformHandler</c> must run after
		/// <c>AddMauiBlazorWebView</c>. If another library calls <c>AddMauiBlazorWebView()</c> later in the
		/// pipeline, that call re-registers the default handler and silently overrides this one — call this
		/// method after every other MAUI Blazor configuration when composing multiple sources.
		/// </remarks>
		/// <param name="services">The service collection.</param>
		/// <returns>An <see cref="IMauiBlazorWebViewBuilder"/> for further configuration.</returns>
		public static IMauiBlazorWebViewBuilder AddTizenBlazorWebView(this IServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			return services
				.AddMauiBlazorWebView()
				.UsePlatformHandler<TizenBlazorWebViewHandler>();
		}

		/// <summary>
		/// Replaces the BlazorWebView handler with <see cref="TizenBlazorWebViewHandler"/> on an existing
		/// <see cref="IMauiBlazorWebViewBuilder"/>.
		/// </summary>
		/// <param name="builder">The builder returned by <c>AddMauiBlazorWebView()</c>.</param>
		/// <returns>The same builder, for chaining.</returns>
		public static IMauiBlazorWebViewBuilder UseTizenBlazorWebView(this IMauiBlazorWebViewBuilder builder)
		{
			ArgumentNullException.ThrowIfNull(builder);

			return builder.UsePlatformHandler<TizenBlazorWebViewHandler>();
		}
	}
}
