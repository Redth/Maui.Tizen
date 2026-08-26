using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView;

namespace Maui.Tizen.BlazorWebView.Sample
{
	/// <summary>
	/// Minimal host wiring for a Blazor application running on the standalone Tizen backend.
	/// </summary>
	public static class MauiProgram
	{
		/// <summary>
		/// Builds the <see cref="MauiApp"/> for the sample.
		/// </summary>
		public static MauiApp CreateMauiApp()
		{
			var builder = MauiApp.CreateBuilder();
			builder.UseMauiApp<App>();

			// AddTizenBlazorWebView() is the one-call form of:
			//
			//     builder.Services.AddMauiBlazorWebView()
			//                     .UsePlatformHandler<TizenBlazorWebViewHandler>();
			//
			// Handler registration is last-registration-wins, so it must run after every other
			// AddMauiBlazorWebView() call in the application, otherwise the default handler wins.
			builder.Services.AddTizenBlazorWebView();

			return builder.Build();
		}
	}

	/// <summary>
	/// The sample application.
	/// </summary>
	public class App : Application
	{
		/// <inheritdoc />
		protected override Window CreateWindow(IActivationState? activationState)
			=> new Window(new MainPage());
	}

	/// <summary>
	/// Hosts a <see cref="Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView"/> rendering
	/// <c>Maui.Tizen.BlazorWebView.Sample.Components.Main</c>.
	/// </summary>
	public class MainPage : ContentPage
	{
		/// <summary>
		/// Creates the page and its BlazorWebView.
		/// </summary>
		public MainPage()
		{
			Title = "Maui.Tizen BlazorWebView Sample";

			var blazorWebView = new Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView
			{
				HostPage = "wwwroot/index.html",
			};

			blazorWebView.RootComponents.Add(new RootComponent
			{
				Selector = "#app",
				ComponentType = typeof(global::Maui.Tizen.BlazorWebView.Sample.Components.Main),
			});

			Content = blazorWebView;
		}
	}
}
