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
			// Registers the complete standalone Tizen Controls backend: Core handlers,
			// Essentials, Controls mappings, dispatcher, animation ticker and lifecycle.
			//
			// NOTE: this is UseMauiAppTizenControls, not the Core-only UseMauiAppTizen.
			// Never wrap this call in a C# preprocessor conditional - the .NET Template Engine
			// reads those as TEMPLATE conditionals in template content and strips the branch
			// before you ever see it. See src/Maui.Tizen.Templates/Maui.Tizen.Templates.csproj.
			.UseMauiAppTizenControls<App>()
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
