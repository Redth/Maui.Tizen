using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen;

namespace MauiTizenApp;

/// <summary>
/// The Tizen entry point. The Samsung workload launches this executable from the
/// <c>exec</c> attribute in <c>Platforms/Tizen/tizen-manifest.xml</c>.
/// </summary>
/// <remarks>
/// Derives from <see cref="TizenMauiApplication"/>, this backend's equivalent of MAUI's
/// <c>Microsoft.Maui.MauiApplication</c>. The name differs deliberately: the
/// <c>net11.0-tizen*</c> build of <c>Microsoft.Maui.dll</c> still ships its own
/// <c>MauiApplication</c>, so re-using that name would be a CS0433 hazard for any app that
/// references both. Do not "simplify" this back to <c>MauiApplication</c>.
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
