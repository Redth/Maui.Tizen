using System;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen;

namespace Maui.Tizen.BlazorWebView.Sample
{
	/// <summary>
	/// Tizen entry point.
	/// </summary>
	/// <remarks>
	/// Derives from <see cref="TizenMauiApplication"/>, this backend's non-colliding equivalent of
	/// MAUI's <c>Microsoft.Maui.MauiApplication</c>. Deriving from MAUI's own type instead would bind
	/// the app to the platform lifecycle of a backend that no longer ships for Tizen.
	/// </remarks>
	internal sealed class Program : TizenMauiApplication
	{
		protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

		private static void Main(string[] args)
		{
			var app = new Program();
			app.Run(args);
		}
	}
}
