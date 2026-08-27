using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen;
using Microsoft.Maui.Platforms.Tizen.Hosting;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Guards Wave B's service registrations against MAUI's default-service shadowing.
/// </summary>
/// <remarks>
/// <para>
/// <c>MauiApp.CreateBuilder</c> registers a default implementation for a number of contracts before
/// any backend configuration runs. For those, <c>TryAdd</c> is a guaranteed no-op: the Tizen
/// implementation is never used, nothing throws, nothing is logged, and the app quietly runs on
/// MAUI's neutral implementation — which on Tizen generally does nothing at all.
/// </para>
/// <para>
/// The rule this pins is therefore: <b>a contract MAUI registers by default must be
/// <c>Replace</c>d</b> (or overridden through the collection's own last-wins mechanism); a
/// Tizen-only contract may use <c>TryAdd</c>, so a host can still substitute its own.
/// </para>
/// <para>
/// These resolve through a real <see cref="MauiApp"/> container and assert the concrete
/// implementation type, because that is the only thing that distinguishes a winning registration
/// from a shadowed one. Asserting that a service resolves proves nothing: the neutral default
/// resolves too.
/// </para>
/// </remarks>
public class ServiceShadowingTests
{
	sealed class HostApplication : Microsoft.Maui.Controls.Application
	{
		protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState) =>
			new(new Microsoft.Maui.Controls.ContentPage());
	}

	sealed class StubDirectories : ITizenFontDirectoryProvider
	{
		public string ResourceDirectory { get; } = Path.Combine(Path.GetTempPath(), "maui-tizen-shadow-res");

		public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "maui-tizen-shadow-data");

		public void AddCustomFontDirectory(string path)
		{
		}
	}

	/// <summary>Contracts MAUI itself registers before any backend configuration runs.</summary>
	/// <remarks>
	/// Measured rather than assumed: this is the set observed on the builder returned by
	/// <c>MauiApp.CreateBuilder</c>, and it is what makes <c>TryAdd</c> unsafe for these.
	/// </remarks>
	public static TheoryData<string> MauiOwnedContracts =>
		new() { "IFontManager", "IEmbeddedFontLoader", "IFontRegistrar", "IDispatcherProvider", "IDispatcher", "ITicker", "IAnimationManager" };

	/// <summary>
	/// The premise of the whole audit: MAUI really does pre-register these, so a later
	/// <c>TryAdd</c> for one of them cannot win.
	/// </summary>
	/// <remarks>
	/// Without this the tests below could pass for the wrong reason — if MAUI stopped registering a
	/// default, a <c>TryAdd</c> would start winning and the shadowing assertions would look
	/// satisfied while no longer testing anything.
	/// </remarks>
	[Theory]
	[MemberData(nameof(MauiOwnedContracts))]
	public void MauiRegistersADefaultForThisContract(string contractName)
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<HostApplication>();

		Assert.Contains(builder.Services, descriptor => descriptor.ServiceType.Name == contractName);
	}

	static IServiceProvider BuildWithTizenFonts()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<HostApplication>();

		builder.Services.AddSingleton<ITizenFontDirectoryProvider>(new StubDirectories());
		builder.Services.AddTizenFontServices();

		return builder.Build().Services;
	}

	/// <summary>
	/// The embedded font loader must beat MAUI's default.
	/// </summary>
	/// <remarks>
	/// MAUI's <c>EmbeddedFontLoader</c> has no Tizen implementation, so losing this race means every
	/// <c>ConfigureFonts</c> alias silently falls back to the system typeface.
	/// </remarks>
	[Fact]
	public void TheEmbeddedFontLoaderIsTheTizenImplementation() =>
		Assert.IsType<TizenEmbeddedFontLoader>(BuildWithTizenFonts().GetRequiredService<IEmbeddedFontLoader>());

	/// <summary>
	/// Image source services are overridden through the collection's own last-wins mechanism.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>IImageSourceServiceCollection.AddService</c> is deliberately <em>not</em> a
	/// <c>TryAdd</c>-style API: it calls plain <c>AddSingleton</c>, so the last registration wins.
	/// That is the intended override mechanism for image sources and needs no <c>Replace</c>.
	/// </para>
	/// <para>
	/// Only the URI and font services can be asserted here. Wave A's file and stream registrations
	/// sit behind <c>#if TIZEN</c>, so on a host TFM they are not registered at all and those two
	/// types resolve to MAUI's neutral services — which is exactly why
	/// <see cref="ImageSourceSeamTests"/> proves the full set from the ref-pack lane's emitted IL
	/// instead.
	/// </para>
	/// </remarks>
	[Theory]
	[InlineData(typeof(IUriImageSource), typeof(TizenUriImageSourceService))]
	[InlineData(typeof(IFontImageSource), typeof(TizenFontImageSourceService))]
	public void TizenImageSourceServicesOverrideTheNeutralOnes(Type imageSourceType, Type expected)
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<HostApplication>();
		builder.ConfigureImageSources(sources => sources.AddTizenUriAndFontImageSources());

		var provider = builder.Build().Services.GetRequiredService<IImageSourceServiceProvider>();

		Assert.IsType(expected, provider.GetImageSourceService(imageSourceType));
	}

	/// <summary>
	/// Registering the Tizen services after MAUI's defaults must still win.
	/// </summary>
	/// <remarks>
	/// Ordering is the whole hazard. This registers the image sources last — the realistic order,
	/// since backend configuration runs after <c>CreateBuilder</c> — and confirms the Tizen service
	/// is still the one that resolves.
	/// </remarks>
	[Fact]
	public void TizenImageSourcesWinEvenWhenRegisteredAfterTheNeutralDefaults()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<HostApplication>();

		// Force MAUI's neutral font service to be registered first.
		builder.ConfigureImageSources(sources => sources.AddService<IFontImageSource, FontImageSourceService>());
		builder.ConfigureImageSources(sources => sources.AddTizenUriAndFontImageSources());

		var provider = builder.Build().Services.GetRequiredService<IImageSourceServiceProvider>();

		Assert.IsType<TizenFontImageSourceService>(provider.GetImageSourceService(typeof(IFontImageSource)));
	}

	/// <summary>
	/// Tizen-only contracts may use <c>TryAdd</c>, and a host must be able to substitute its own.
	/// </summary>
	/// <remarks>
	/// This is the other half of the rule. <c>ITizenFontDirectoryProvider</c> is not a MAUI contract,
	/// so nothing shadows it — and registering it with <c>TryAdd</c> is what lets a host replace it,
	/// which is exactly how these tests supply a temp-directory stand-in for one that would
	/// otherwise call <c>Tizen.Applications.Application.Current</c>.
	/// </remarks>
	[Fact]
	public void AHostCanSubstituteATizenOnlyContract()
	{
		var directories = new StubDirectories();

		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<HostApplication>();
		builder.Services.AddSingleton<ITizenFontDirectoryProvider>(directories);
		// Stands in for TizenPlatformFontDirectoryProvider, which needs TizenFX and so is not
		// compiled here. The TryAdd semantics under test are identical.
		builder.Services.TryAddSingleton<ITizenFontDirectoryProvider, StubDirectories>();

		Assert.Same(directories, builder.Build().Services.GetRequiredService<ITizenFontDirectoryProvider>());
	}

	/// <summary>
	/// No Wave B source may register a MAUI-owned contract with <c>TryAdd</c>.
	/// </summary>
	/// <remarks>
	/// The resolution tests above cover the registrations that exist today; this covers the ones
	/// someone adds tomorrow. It is a source check rather than a container check because a
	/// <c>TryAdd</c> for a shadowed contract produces no observable symptom to assert on — the
	/// service still resolves, to the wrong implementation.
	/// </remarks>
	[Fact]
	public void NoWaveBRegistrationTryAddsAMauiOwnedContract()
	{
		string[] mauiOwned =
		{
			"IFontManager",
			"IEmbeddedFontLoader",
			"IFontRegistrar",
			"IDispatcherProvider",
			"IDispatcher",
			"ITicker",
			"IAnimationManager",
			"IImageSourceServiceProvider",
		};

		var failures = new List<string>();

		foreach (var relative in WaveBOwnedRegistrationFiles())
		{
			var path = RepoPaths.Combine(relative.Split('/'));

			if (!File.Exists(path))
				continue;

			var lines = File.ReadAllLines(path);

			for (var i = 0; i < lines.Length; i++)
			{
				var line = lines[i];

				if (!line.Contains("TryAdd", StringComparison.Ordinal) || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
					continue;

				foreach (var contract in mauiOwned)
				{
					if (line.Contains($"<{contract}>", StringComparison.Ordinal)
						|| line.Contains($"<{contract},", StringComparison.Ordinal))
					{
						failures.Add($"{relative}:{i + 1} TryAdds {contract}, which MAUI registers by default, so the Tizen implementation would never be used: {line.Trim()}");
					}
				}
			}
		}

		Assert.Empty(failures);
	}

	/// <summary>Registration files Wave B owns. Wave A owns the rest of the composition root.</summary>
	static IEnumerable<string> WaveBOwnedRegistrationFiles()
	{
		yield return "src/Maui.Tizen.Core/Hosting/TizenMauiAppBuilderExtensions.Content.cs";
		yield return "src/Maui.Tizen.Core/Hosting/TizenFontServiceCollectionExtensions.cs";
		yield return "src/Maui.Tizen.Core/Hosting/TizenContentHandlerCollectionExtensions.cs";
		yield return "src/Maui.Tizen.Core/ImageSources/TizenWaveBImageSourceServices.cs";
		yield return "src/Maui.Tizen.Controls/Hosting/TizenShapeHandlerCollectionExtensions.cs";
	}
}
