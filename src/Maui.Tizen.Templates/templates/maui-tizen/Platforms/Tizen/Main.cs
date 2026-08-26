using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace MauiTizenApp;

/// <summary>
/// The Tizen entry point. The Samsung workload launches this executable from the
/// <c>exec</c> attribute in <c>Platforms/Tizen/tizen-manifest.xml</c>.
/// </summary>
internal sealed class Program : MauiApplication
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	private static void Main(string[] args)
	{
		var app = new Program();
		app.Run(args);
	}
}
