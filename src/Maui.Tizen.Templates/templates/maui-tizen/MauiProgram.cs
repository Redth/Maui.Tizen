using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;

#if TIZEN
using Maui.Tizen;
#endif

namespace MauiTizenApp;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder
#if TIZEN
			// Registers the standalone Maui.Tizen backend: handlers, fonts, image sources
			// and the Tizen application lifecycle.
			.UseMauiAppTizen<App>()
#else
			.UseMauiApp<App>()
#endif
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		return builder.Build();
	}
}
