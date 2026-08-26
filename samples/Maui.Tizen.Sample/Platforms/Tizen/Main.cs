using System;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen;

namespace Maui.Tizen.Sample
{
	/// <summary>
	/// Tizen entry point.
	/// </summary>
	/// <remarks>
	/// Derives from <see cref="TizenMauiApplication"/>, this backend's non-colliding equivalent of
	/// MAUI's <c>Microsoft.Maui.MauiApplication</c>.
	/// </remarks>
	internal sealed class Program : TizenMauiApplication
	{
		protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

		static void Main(string[] args)
		{
			var app = new Program();
			app.Run(args);
		}
	}
}
