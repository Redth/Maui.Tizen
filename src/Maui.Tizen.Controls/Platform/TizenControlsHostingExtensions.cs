using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Tizen.Controls
{
	/// <summary>
	/// Startup integration that installs the Controls-to-Tizen mappings.
	/// </summary>
	/// <remarks>
	/// Without this, <see cref="TizenControlsMappings.Register"/> had no production caller at all:
	/// the bridge compiled, shipped, and did nothing. Every Controls property it binds -
	/// LineBreakMode, the accessibility annotations - stayed unmapped in a real app while the unit
	/// tests, which call Register directly, passed.
	/// </remarks>
	public static class TizenControlsHostingExtensions
	{
		/// <summary>
		/// Registers the Tizen Controls mappings so they are installed during app startup.
		/// </summary>
		/// <param name="builder">The app builder.</param>
		/// <returns>The builder, for chaining.</returns>
		public static MauiAppBuilder ConfigureTizenControls(this MauiAppBuilder builder)
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.Services.TryAddEnumerable(
				ServiceDescriptor.Singleton<IMauiInitializeService, TizenControlsMappingsInitializer>());

			return builder;
		}
	}

	/// <summary>
	/// Installs the Controls-to-Tizen mappings from <c>MauiApp.Build()</c>.
	/// </summary>
	/// <remarks>
	/// An initialize service rather than a call inside ConfigureTizen, because ordering matters and
	/// this is the point where it can be guaranteed: initializers run during Build, after the app
	/// class and its handlers are registered but before any handler is connected to a view. That is
	/// the window in which the static mappers can still be extended and the extension is certain to
	/// be seen.
	/// </remarks>
	internal sealed class TizenControlsMappingsInitializer : IMauiInitializeService
	{
		/// <inheritdoc />
		public void Initialize(IServiceProvider services) => TizenControlsMappings.Register();
	}
}
