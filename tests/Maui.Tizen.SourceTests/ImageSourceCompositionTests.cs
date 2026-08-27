using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Verifies that Wave B's image source services actually compose into a host and resolve.
/// </summary>
/// <remarks>
/// Registration code is easy to write and easy to leave uncalled. These tests resolve the services
/// through a real <see cref="MauiApp"/> container rather than asserting that a method exists.
/// <para>
/// Only the URI and font services are covered here. Wave A's file and stream services call
/// <c>Tizen.Applications.ResourceManager</c>, so they cannot be constructed on a host; their half of
/// the composition is verified by the ref-pack compile lane and, ultimately, at integration.
/// </para>
/// </remarks>
public class ImageSourceCompositionTests
{
	static IImageSourceServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
	{
		// A real app builder, so IImageSourceServiceProvider comes from MAUI's own composition
		// rather than from a hand-built container that might not resolve the way the product does.
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<HostApplication>();
		builder.ConfigureImageSources(sources => sources.AddTizenUriAndFontImageSources());

		configure?.Invoke(builder.Services);

		return builder.Build().Services.GetRequiredService<IImageSourceServiceProvider>();
	}

	[Fact]
	public void UriImageSourceResolvesToTheTizenService()
	{
		var provider = BuildProvider();

		var service = provider.GetImageSourceService(typeof(IUriImageSource));

		Assert.IsType<TizenUriImageSourceService>(service);
	}

	[Fact]
	public void FontImageSourceResolvesToTheTizenService()
	{
		var provider = BuildProvider();

		var service = provider.GetImageSourceService(typeof(IFontImageSource));

		Assert.IsType<TizenFontImageSourceService>(service);
	}

	/// <summary>
	/// Both services must implement the Tizen-typed contract, or the loader's
	/// <c>GetTizenImageAsync</c> silently returns null and every image renders blank.
	/// </summary>
	[Fact]
	public void RegisteredServicesImplementTheTizenContract()
	{
		var provider = BuildProvider();

		Assert.IsAssignableFrom<ITizenImageSourceService>(provider.GetImageSourceService(typeof(IUriImageSource)));
		Assert.IsAssignableFrom<ITizenImageSourceService>(provider.GetImageSourceService(typeof(IFontImageSource)));
	}

	/// <summary>
	/// The registration must pass a logger through from DI.
	/// </summary>
	/// <remarks>
	/// The services have always accepted an <c>ILogger</c>, but the registration used to construct
	/// them with none, so the parameter was dead and every diagnostic went nowhere. Resolving a font
	/// image is the one path that logs unconditionally, which makes it a usable probe.
	/// </remarks>
	[Fact]
	public async Task RegistrationSuppliesALoggerFromDependencyInjection()
	{
		var sink = new RecordingLoggerProvider();

		var provider = BuildProvider(services => services.AddLogging(logging => logging.AddProvider(sink)));

		var service = (ITizenImageSourceService)provider.GetImageSourceService(typeof(IFontImageSource))!;
		await service.GetImageAsync(new StubFontImageSource());

		Assert.Contains(sink.Messages, m => m.Contains("not rasterised", StringComparison.Ordinal));
	}

	sealed class HostApplication : Microsoft.Maui.Controls.Application
	{
		protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState) =>
			new Microsoft.Maui.Controls.Window(new Microsoft.Maui.Controls.ContentPage());
	}

	sealed class StubFontImageSource : IFontImageSource
	{
		public bool IsEmpty => false;
		public Microsoft.Maui.Font Font => Microsoft.Maui.Font.Default;
		public string Glyph => "\uf000";
		public Microsoft.Maui.Graphics.Color Color => Microsoft.Maui.Graphics.Colors.Black;
	}

	sealed class RecordingLoggerProvider : ILoggerProvider
	{
		public List<string> Messages { get; } = new();

		public ILogger CreateLogger(string categoryName) => new Recorder(Messages);

		public void Dispose()
		{
		}

		sealed class Recorder : ILogger
		{
			readonly List<string> _messages;

			public Recorder(List<string> messages) => _messages = messages;

			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => true;

			public void Log<TState>(
				LogLevel logLevel,
				EventId eventId,
				TState state,
				Exception? exception,
				Func<TState, Exception?, string> formatter)
			{
				lock (_messages)
				{
					_messages.Add(formatter(state, exception));
				}
			}
		}
	}
}
