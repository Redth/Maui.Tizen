using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Hosting;

namespace MauiTizenApp;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder
			// Registers the standalone Maui.Tizen backend: handlers, dispatcher, animation
			// ticker and the Tizen application lifecycle.
			//
			// NOTE: this is UseMauiAppTizen, not UseMauiApp. They are different entry points.
			// Never wrap this call in a C# preprocessor conditional - the .NET Template Engine
			// reads those as TEMPLATE conditionals in template content and strips the branch
			// before you ever see it. See src/Maui.Tizen.Templates/Maui.Tizen.Templates.csproj.
			.UseMauiAppTizen<App>()
			.ConfigureFonts(fonts =>
			{
				// Drop a .ttf into Resources/Fonts and register it here, for example:
				//     fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				// Every registered file must actually exist in Resources/Fonts. A name that
				// does not resolve fails at runtime, not at build time, so this template
				// deliberately ships no dangling registration.
			});

		return builder.Build();
	}
}
