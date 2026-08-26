using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Hosting;

namespace Maui.Tizen.Sample
{
	/// <summary>Builds the <see cref="MauiApp"/> for the sample.</summary>
	public static class MauiProgram
	{
		/// <summary>Creates the configured <see cref="MauiApp"/>.</summary>
		/// <returns>The app.</returns>
		public static MauiApp CreateMauiApp()
		{
			var builder = MauiApp.CreateBuilder();

			// Registers the Tizen application/window/content view/layout/label handlers, the
			// dispatcher provider (which is what makes MainThread work through the .NET 11
			// dispatcher bridge) and the animation ticker.
			builder.UseMauiAppTizen<SampleApplication>();

			builder.ConfigureMauiHandlers(handlers => handlers.AddTizenPageHandler<SamplePage>());

			return builder.Build();
		}
	}
}
