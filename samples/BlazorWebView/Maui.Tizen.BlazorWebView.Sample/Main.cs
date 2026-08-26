using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace Maui.Tizen.BlazorWebView.Sample
{
	/// <summary>
	/// Tizen entry point for the sample.
	/// </summary>
	internal class Program : MauiApplication
	{
		protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

		private static void Main(string[] args)
		{
			var app = new Program();
			app.Run(args);
		}
	}
}
