using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen;
using Microsoft.Maui.Platforms.Tizen.Controls;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;

namespace Maui.Tizen.Controls.ConsumerCompile;

public static class ControlsConsumer
{
	public static MauiApp Build() =>
		MauiApp.CreateBuilder()
			.UseMauiAppTizenControls<ConsumerApplication>()
			.Build();

	public static MauiAppBuilder ConfigureExisting(MauiAppBuilder builder) =>
		builder.ConfigureTizenControls();

	sealed class ConsumerApplication : Application
	{
	}
}
