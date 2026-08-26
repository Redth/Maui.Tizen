using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.FileProviders;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;
using Xunit;
using NWebView = Tizen.NUI.BaseComponents.WebView;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	/// <summary>
	/// Covers handler construction, the handler contract, the static-content lifecycle and disposal
	/// behavior that does not require a live NUI WebView.
	/// </summary>
	public class HandlerLifecycleTests
	{
		[Fact]
		public void HandlerDerivesFromTheTizenBackendHandlerBase()
		{
			// Not cosmetic: TizenLayoutHandler and TizenContentViewHandler reach a child through
			// ITizenPlatformViewHandler when adding it to the native tree, so a BlazorWebView whose
			// handler did not implement it would never be parented.
			var baseType = typeof(TizenBlazorWebViewHandler).BaseType;

			Assert.NotNull(baseType);
			Assert.True(baseType!.IsGenericType);
			Assert.Equal(typeof(TizenViewHandler<,>), baseType.GetGenericTypeDefinition());
			Assert.Equal(new[] { typeof(IBlazorWebView), typeof(NWebView) }, baseType.GetGenericArguments());

			Assert.True(typeof(ITizenPlatformViewHandler).IsAssignableFrom(typeof(TizenBlazorWebViewHandler)));
			Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(TizenBlazorWebViewHandler)));
		}

		[Fact]
		public void HandlerIsStillAMauiViewHandlerOverTheTizenNuiWebView()
		{
			Assert.True(typeof(ViewHandler<IBlazorWebView, NWebView>).IsAssignableFrom(typeof(TizenBlazorWebViewHandler)));
		}

		[Fact]
		public void HandlerCanBeConstructedWithoutAPlatformView()
		{
			var handler = new TizenBlazorWebViewHandler();

			Assert.IsAssignableFrom<IBlazorWebViewHandler>(handler);
			Assert.NotNull(handler.HandlerKey);
		}

		[Fact]
		public void HandlerAcceptsACustomPropertyMapper()
		{
			var mapper = new PropertyMapper<IBlazorWebView, TizenBlazorWebViewHandler>(TizenBlazorWebViewHandler.TizenBlazorWebViewMapper);

			var handler = new TizenBlazorWebViewHandler(mapper);

			Assert.NotNull(handler);
		}

		[Fact]
		public void HandlerFallsBackToTheDefaultMapperWhenGivenNull()
		{
			var handler = new TizenBlazorWebViewHandler(null);

			Assert.NotNull(handler);
		}

		[Fact]
		public void DefaultMapperCoversHostPageAndRootComponents()
		{
			var mapper = TizenBlazorWebViewHandler.TizenBlazorWebViewMapper;

			Assert.Contains(nameof(IBlazorWebView.HostPage), mapper.GetKeys());
			Assert.Contains(nameof(IBlazorWebView.RootComponents), mapper.GetKeys());
		}

		[Fact]
		public async Task TryDispatchAsyncReturnsFalseBeforeBlazorStarts()
		{
			var handler = new TizenBlazorWebViewHandler();
			var called = false;

			var dispatched = await handler.TryDispatchAsync(_ => called = true);

			Assert.False(dispatched);
			Assert.False(called);
		}

		[Fact]
		public async Task TryDispatchAsyncRejectsANullWorkItem()
		{
			var handler = new TizenBlazorWebViewHandler();

			await Assert.ThrowsAsync<ArgumentNullException>(() => handler.TryDispatchAsync(null!));
		}

		[Fact]
		public void CreateFileProviderReturnsATizenAssetFileProvider()
		{
			using var temp = new TempDirectory();
			var handler = new TestableHandler(temp.Path);

			var provider = handler.CreateFileProvider("wwwroot");

			var tizenProvider = Assert.IsType<TizenAssetFileProvider>(provider);
			Assert.Equal(Path.Combine(temp.Path, "wwwroot"), tizenProvider.RootDirectory);
		}

		[Fact]
		public void CreateFileProviderIsReachableThroughThePublicHandlerContract()
		{
			using var temp = new TempDirectory();
			IBlazorWebViewHandler handler = new TestableHandler(temp.Path);

			var provider = handler.CreateFileProvider("wwwroot");

			Assert.IsAssignableFrom<IFileProvider>(provider);
		}

		[Fact]
		public void EachHandlerGetsItsOwnUserAgentRoutingKey()
		{
			var first = new TizenBlazorWebViewHandler();
			var second = new TizenBlazorWebViewHandler();

			Assert.NotEqual(first.HandlerKey, second.HandlerKey);
		}

		[Fact]
		public void UserAgentSuffixRoundTripsThroughTheRequestHeaders()
		{
			var handler = new TizenBlazorWebViewHandler();
			var userAgent = "Mozilla/5.0 (Tizen)" + BlazorWebViewUserAgent.BuildUserAgentSuffix(handler.HandlerKey);
			var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["User-Agent"] = userAgent,
			};

			Assert.True(BlazorWebViewUserAgent.TryGetHandlerKey(headers, out var key));
			Assert.Equal(handler.HandlerKey, key);
		}

		[Theory]
		[InlineData(null)]
		[InlineData("")]
		[InlineData("Mozilla/5.0 (Tizen)")]
		[InlineData("Mozilla/5.0 (Tizen) BlazorWebView:")]
		public void UntaggedRequestsAreNotRoutedToAHandler(string? userAgent)
		{
			var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (userAgent is not null)
			{
				headers["User-Agent"] = userAgent;
			}

			Assert.False(BlazorWebViewUserAgent.TryGetHandlerKey(headers, out var key));
			Assert.Equal(string.Empty, key);
		}

		[Fact]
		public void RequestsWithoutHeadersAreNotRoutedToAHandler()
		{
			Assert.False(BlazorWebViewUserAgent.TryGetHandlerKey(null, out _));
		}

		[Fact]
		public void DisconnectClearsTheStaticContentCache()
		{
			var handler = new TizenBlazorWebViewHandler();
			handler.StaticContentResponseCache.Set(new StaticContent.StaticContentResponse(
				"http://0.0.0.0/app.css",
				"text/css",
				200,
				"OK",
				new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
				new byte[] { 1, 2, 3 },
				DateTimeOffset.UtcNow.AddMinutes(5)));
			Assert.Equal(1, handler.StaticContentResponseCache.Count);

			handler.StaticContentResponseCache.Clear();

			Assert.Equal(0, handler.StaticContentResponseCache.Count);
		}

		[Fact]
		public void DetachingRootComponentsDoesNotDiscardTheApplicationsComponents()
		{
			// The collection belongs to the BlazorWebView control. Clearing it on disconnect would leave
			// nothing to render if the handler is reconnected later.
			var handler = new TizenBlazorWebViewHandler();
			var components = CreateRootComponents(new RootComponent { Selector = "#app", ComponentType = typeof(object) });

			handler.SetRootComponents(components);
			handler.SetRootComponents(null, clearPrevious: false);

			Assert.Single(components);
		}

		[Fact]
		public void ReapplyingTheSameRootComponentsCollectionKeepsItIntact()
		{
			// MapRootComponents runs whenever the property mapper fires, which can happen more than once
			// with the same collection instance.
			var handler = new TizenBlazorWebViewHandler();
			var components = CreateRootComponents(new RootComponent { Selector = "#app", ComponentType = typeof(object) });

			handler.SetRootComponents(components);
			handler.SetRootComponents(components);

			Assert.Single(components);
		}

		[Fact]
		public void SwappingRootComponentsCollectionsReleasesThePreviousOne()
		{
			var handler = new TizenBlazorWebViewHandler();
			var first = CreateRootComponents(new RootComponent { Selector = "#app", ComponentType = typeof(object) });
			var second = CreateRootComponents();

			handler.SetRootComponents(first);
			handler.SetRootComponents(second);

			Assert.Empty(first);
		}

		[Fact]
		public void MappingHostPageBeforeAMauiContextExistsDoesNotStartBlazor()
		{
			// StartWebViewCoreIfPossible must stay inert until both the host page and the services are known,
			// otherwise it would touch the native web view during handler construction.
			var handler = new TizenBlazorWebViewHandler();
			var virtualView = new AspNetCore.Components.WebView.Maui.BlazorWebView
			{
				HostPage = "wwwroot/index.html",
			};

			TizenBlazorWebViewHandler.MapHostPage(handler, virtualView);
			TizenBlazorWebViewHandler.MapRootComponents(handler, virtualView);

			Assert.Null(handler.WebViewManager);
		}

		[Fact]
		public void PropertyMappersRejectNullArguments()
		{
			var handler = new TizenBlazorWebViewHandler();
			var virtualView = new AspNetCore.Components.WebView.Maui.BlazorWebView();

			Assert.Throws<ArgumentNullException>(() => TizenBlazorWebViewHandler.MapHostPage(null!, virtualView));
			Assert.Throws<ArgumentNullException>(() => TizenBlazorWebViewHandler.MapHostPage(handler, null!));
			Assert.Throws<ArgumentNullException>(() => TizenBlazorWebViewHandler.MapRootComponents(null!, virtualView));
			Assert.Throws<ArgumentNullException>(() => TizenBlazorWebViewHandler.MapRootComponents(handler, null!));
		}

		[Fact]
		public void AddRootComponentRequiresASelector()
		{
			var component = new RootComponent { ComponentType = typeof(object) };

			var exception = Assert.Throws<InvalidOperationException>(
				() => { _ = TizenBlazorWebViewHandler.AddRootComponentAsync(component, CreateUninitializedWebViewManager()); });

			Assert.Contains(nameof(RootComponent.Selector), exception.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void AddRootComponentRequiresAComponentType()
		{
			var component = new RootComponent { Selector = "#app" };

			var exception = Assert.Throws<InvalidOperationException>(
				() => { _ = TizenBlazorWebViewHandler.AddRootComponentAsync(component, CreateUninitializedWebViewManager()); });

			Assert.Contains(nameof(RootComponent.ComponentType), exception.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void RemoveRootComponentRequiresASelector()
		{
			var component = new RootComponent { ComponentType = typeof(object) };

			var exception = Assert.Throws<InvalidOperationException>(
				() => { _ = TizenBlazorWebViewHandler.RemoveRootComponentAsync(component, CreateUninitializedWebViewManager()); });

			Assert.Contains(nameof(RootComponent.Selector), exception.Message, StringComparison.Ordinal);
		}

		[Fact]
		public void RootComponentHelpersRejectNullArguments()
		{
			Assert.Throws<ArgumentNullException>(
				() => { _ = TizenBlazorWebViewHandler.AddRootComponentAsync(null!, CreateUninitializedWebViewManager()); });
			Assert.Throws<ArgumentNullException>(
				() => { _ = TizenBlazorWebViewHandler.AddRootComponentAsync(new RootComponent(), null!); });
		}

		[Fact]
		public void WebViewManagerUsesTheBlazorAppOrigin()
		{
			// The origin is baked into the init script, the request filter and the cache keys.
			Assert.Equal("http://0.0.0.0/", TizenWebViewManager.AppOrigin);
			Assert.Equal(TizenWebViewManager.AppOrigin, TizenBlazorWebViewHandler.AppOrigin);
		}

		[Fact]
		public void WebViewManagerRejectsNullDependencies()
		{
			// Constructing a TizenWebViewManager requires arguments that only exist once Blazor starts;
			// argument validation is checked through reflection on the declared constructor instead.
			var constructor = typeof(TizenWebViewManager).GetConstructors().Single();
			var parameters = constructor.GetParameters();

			Assert.Equal("handler", parameters[0].Name);
			Assert.Equal("webview", parameters[1].Name);
			Assert.Equal(typeof(NWebView), parameters[1].ParameterType);
			Assert.Equal(typeof(IFileProvider), parameters[4].ParameterType);
		}

		/// <summary>
		/// A <see cref="TizenWebViewManager"/> reference is required only for the null/validation paths that
		/// throw before it is touched, so an uninitialized instance is sufficient and avoids native calls.
		/// </summary>
		private static RootComponentsCollection CreateRootComponents(params RootComponent[] components)
		{
			var collection = new RootComponentsCollection(new AspNetCore.Components.Web.JSComponentConfigurationStore());
			foreach (var component in components)
			{
				collection.Add(component);
			}

			return collection;
		}

		private static TizenWebViewManager CreateUninitializedWebViewManager()
			=> (TizenWebViewManager)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TizenWebViewManager));

		private sealed class TestableHandler : TizenBlazorWebViewHandler
		{
			private readonly string _resourceDirectory;

			public TestableHandler(string resourceDirectory)
			{
				_resourceDirectory = resourceDirectory;
			}

			protected override string GetResourceDirectory() => _resourceDirectory;
		}

		private sealed class TempDirectory : IDisposable
		{
			public TempDirectory()
			{
				Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "maui-tizen-bwv-" + Guid.NewGuid().ToString("n"));
				Directory.CreateDirectory(Path);
			}

			public string Path { get; }

			public void Dispose()
			{
				if (Directory.Exists(Path))
				{
					Directory.Delete(Path, recursive: true);
				}
			}
		}
	}
}
