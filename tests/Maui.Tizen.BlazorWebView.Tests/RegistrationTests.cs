using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Maui.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	/// <summary>
	/// Verifies that the Tizen handler replaces the default BlazorWebView handler through the public
	/// <c>UsePlatformHandler</c> extensibility point, and that the documented last-registration-wins
	/// ordering rule actually behaves as documented.
	/// </summary>
	public class RegistrationTests
	{
		[Fact]
		public void AddTizenBlazorWebViewRegistersTizenHandlerForBlazorWebView()
		{
			var builder = MauiApp.CreateBuilder(useDefaults: false);
			builder.Services.AddTizenBlazorWebView();

			using var app = builder.Build();
			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

			Assert.Equal(typeof(TizenBlazorWebViewHandler), handlers.GetHandlerType(typeof(IBlazorWebView)));
			Assert.Equal(typeof(TizenBlazorWebViewHandler), handlers.GetHandlerType(typeof(AspNetCore.Components.WebView.Maui.BlazorWebView)));
		}

		[Fact]
		public void AddTizenBlazorWebViewRegistersSharedBlazorServices()
		{
			var builder = MauiApp.CreateBuilder(useDefaults: false);
			builder.Services.AddTizenBlazorWebView();

			// AddMauiBlazorWebView() must still run: these scoped Blazor services come from the shared
			// registration, not from this package. Registering the platform handler must not replace them.
			Assert.Contains(builder.Services, d => d.ServiceType == typeof(AspNetCore.Components.NavigationManager));
			Assert.Contains(builder.Services, d => d.ServiceType == typeof(Microsoft.JSInterop.IJSRuntime));

			using var app = builder.Build();
			Assert.NotNull(app.Services.GetService<Extensions.Logging.ILoggerFactory>());
		}

		[Fact]
		public void ManualRegistrationOrderMatchesTheConvenienceMethod()
		{
			var builder = MauiApp.CreateBuilder(useDefaults: false);
			builder.Services
				.AddMauiBlazorWebView()
				.UsePlatformHandler<TizenBlazorWebViewHandler>();

			using var app = builder.Build();
			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

			Assert.Equal(typeof(TizenBlazorWebViewHandler), handlers.GetHandlerType(typeof(IBlazorWebView)));
		}

		[Fact]
		public void UseTizenBlazorWebViewReplacesHandlerOnAnExistingBuilder()
		{
			var builder = MauiApp.CreateBuilder(useDefaults: false);
			builder.Services
				.AddMauiBlazorWebView()
				.UseTizenBlazorWebView();

			using var app = builder.Build();
			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

			Assert.Equal(typeof(TizenBlazorWebViewHandler), handlers.GetHandlerType(typeof(IBlazorWebView)));
		}

		[Fact]
		public void RegisteringBeforeAddMauiBlazorWebViewLosesToTheDefaultHandler()
		{
			// This is the failure mode called out in the XML docs: handler registration is
			// last-registration-wins, so a later AddMauiBlazorWebView() silently restores the default.
			var builder = MauiApp.CreateBuilder(useDefaults: false);
			builder.Services.AddTizenBlazorWebView();
			builder.Services.AddMauiBlazorWebView();

			using var app = builder.Build();
			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

			Assert.NotEqual(typeof(TizenBlazorWebViewHandler), handlers.GetHandlerType(typeof(IBlazorWebView)));
		}

		[Fact]
		public void RegisteringLastWinsOverADownstreamAddMauiBlazorWebView()
		{
			var builder = MauiApp.CreateBuilder(useDefaults: false);
			builder.Services.AddMauiBlazorWebView();
			builder.Services.AddMauiBlazorWebView();
			builder.Services.AddTizenBlazorWebView();

			using var app = builder.Build();
			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

			Assert.Equal(typeof(TizenBlazorWebViewHandler), handlers.GetHandlerType(typeof(IBlazorWebView)));
		}

		[Fact]
		public void AddTizenBlazorWebViewRejectsNullServices()
		{
			IServiceCollection services = null!;
			Assert.Throws<ArgumentNullException>(() => services.AddTizenBlazorWebView());
		}

		[Fact]
		public void UseTizenBlazorWebViewRejectsNullBuilder()
		{
			IMauiBlazorWebViewBuilder builder = null!;
			Assert.Throws<ArgumentNullException>(() => builder.UseTizenBlazorWebView());
		}

		[Fact]
		public void TizenHandlerSatisfiesThePublicHandlerContract()
		{
			// UsePlatformHandler<THandler>() constrains THandler to IBlazorWebViewHandler + new().
			Assert.True(typeof(IBlazorWebViewHandler).IsAssignableFrom(typeof(TizenBlazorWebViewHandler)));
			Assert.NotNull(typeof(TizenBlazorWebViewHandler).GetConstructor(Type.EmptyTypes));
		}
	}
}
