using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Drives <see cref="TizenEmbeddedFontLoader"/> through MAUI's real <c>ConfigureFonts</c> and
/// <see cref="IFontRegistrar"/> path.
/// </summary>
/// <remarks>
/// The failure this guards against is silent. MAUI's neutral <c>EmbeddedFontLoader</c> has no Tizen
/// implementation, so without a Tizen loader an aliased font never reaches the font client: text
/// still renders, just in the system typeface, with nothing thrown and nothing logged. Asserting
/// that a registration method exists would not catch that; resolving a real alias does.
/// </remarks>
public class EmbeddedFontLoaderTests : IDisposable
{
	readonly string _root = Path.Combine(Path.GetTempPath(), "maui-tizen-fonts-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(_root))
			Directory.Delete(_root, recursive: true);
	}

	sealed class StubDirectories : ITizenFontDirectoryProvider
	{
		public StubDirectories(string root)
		{
			ResourceDirectory = Path.Combine(root, "res");
			DataDirectory = Path.Combine(root, "data");
			Directory.CreateDirectory(ResourceDirectory);
			Directory.CreateDirectory(DataDirectory);
		}

		public string ResourceDirectory { get; }

		public string DataDirectory { get; }

		public List<string> Registered { get; } = new();

		public void AddCustomFontDirectory(string path) => Registered.Add(path);
	}

	IFontRegistrar BuildRegistrar(StubDirectories directories)
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<HostApplication>();

		// The alias a real app would write in ConfigureFonts.
		builder.ConfigureFonts(fonts =>
			fonts.AddEmbeddedResourceFont(typeof(EmbeddedFontLoaderTests).Assembly, "TizenTestFont.ttf", "TizenTestAlias"));

		builder.Services.AddSingleton<ITizenFontDirectoryProvider>(directories);

		// The product registration, not a hand-rolled stand-in, so this exercises the real
		// Replace-over-MAUI's-default behaviour.
		builder.Services.AddTizenFontServices();

		return builder.Build().Services.GetRequiredService<IFontRegistrar>();
	}

	/// <summary>
	/// The registration must REPLACE MAUI's neutral loader, not defer to it.
	/// </summary>
	/// <remarks>
	/// MauiApp.CreateBuilder registers IEmbeddedFontLoader before any backend configuration runs, so
	/// a TryAdd here would silently lose and every aliased font would fall back to the system
	/// typeface. Switching AddTizenFontServices to TryAdd makes this test fail.
	/// </remarks>
	[Fact]
	public void TheTizenLoaderReplacesMauisDefault()
	{
		var directories = new StubDirectories(_root);

		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<HostApplication>();
		builder.Services.AddSingleton<ITizenFontDirectoryProvider>(directories);
		builder.Services.AddTizenFontServices();

		var loader = builder.Build().Services.GetRequiredService<IEmbeddedFontLoader>();

		Assert.IsType<TizenEmbeddedFontLoader>(loader);
	}

	[Fact]
	public void AConfigureFontsAliasResolvesThroughTheTizenLoader()
	{
		var directories = new StubDirectories(_root);

		var family = BuildRegistrar(directories).GetFont("TizenTestAlias");

		// The family name NUI is given is the file name without its extension.
		Assert.Equal("TizenTestFont", family);
	}

	[Fact]
	public void TheFontIsCachedAndItsDirectoryHandedToTheFontClient()
	{
		var directories = new StubDirectories(_root);

		BuildRegistrar(directories).GetFont("TizenTestAlias");

		var cached = Path.Combine(directories.DataDirectory, "fonts", "TizenTestFont.ttf");

		Assert.True(File.Exists(cached), $"The embedded font was never written to {cached}.");

		// Without this the file is on disk but the platform is never told to look there, so the
		// alias still resolves to nothing at render time.
		Assert.Contains(Path.Combine(directories.DataDirectory, "fonts"), directories.Registered);
	}

	/// <summary>
	/// A font shipped as an app resource is used where it is, rather than copied into the cache.
	/// </summary>
	[Fact]
	public void AFontAlreadyPresentInResourcesIsUsedInPlace()
	{
		var directories = new StubDirectories(_root);

		var resourceFonts = Directory.CreateDirectory(Path.Combine(directories.ResourceDirectory, "fonts"));
		File.WriteAllText(Path.Combine(resourceFonts.FullName, "TizenTestFont.ttf"), "already-shipped");

		var loader = new TizenEmbeddedFontLoader(directories);

		var family = loader.LoadFont(new EmbeddedFont { FontName = "TizenTestFont.ttf" });

		Assert.Equal("TizenTestFont", family);
		Assert.False(Directory.Exists(Path.Combine(directories.DataDirectory, "fonts")));
	}

	/// <summary>
	/// A failed copy must not leave a zero-length file behind: File.Exists would then treat it as a
	/// successful cache hit forever after.
	/// </summary>
	[Fact]
	public void AFailedLoadLeavesNoPartialFileBehind()
	{
		var directories = new StubDirectories(_root);
		var loader = new TizenEmbeddedFontLoader(directories);

		var family = loader.LoadFont(new EmbeddedFont { FontName = "Broken.ttf", ResourceStream = null });

		Assert.Null(family);
		Assert.False(File.Exists(Path.Combine(directories.DataDirectory, "fonts", "Broken.ttf")));
	}

	sealed class HostApplication : Microsoft.Maui.Controls.Application
	{
		protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState) =>
			new Microsoft.Maui.Controls.Window(new Microsoft.Maui.Controls.ContentPage());
	}
}
