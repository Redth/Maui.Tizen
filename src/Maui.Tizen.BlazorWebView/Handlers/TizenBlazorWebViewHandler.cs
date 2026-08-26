// Ported from dotnet/maui (net11.0) src/BlazorWebView/src/Maui/Tizen/BlazorWebViewHandler.Tizen.cs.
//
// The upstream type is `Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler`, a partial
// class compiled into Microsoft.AspNetCore.Components.WebView.Maui. This package deliberately does NOT
// redefine that type: it supplies an independent handler that implements the public
// `IBlazorWebViewHandler` capability interface introduced by dotnet/maui#36658 and is registered through
// `IMauiBlazorWebViewBuilder.UsePlatformHandler`.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.StaticContent;
using Tizen.NUI;
using NWebView = Tizen.NUI.BaseComponents.WebView;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView
{
	/// <summary>
	/// The Tizen handler for <see cref="IBlazorWebView"/>, backed by a Tizen NUI WebView.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Register it with
	/// <c>services.AddMauiBlazorWebView().UsePlatformHandler&lt;TizenBlazorWebViewHandler&gt;()</c>, or use the
	/// <see cref="TizenBlazorWebViewServiceCollectionExtensions.AddTizenBlazorWebView(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>
	/// convenience method which performs both calls in the required order.
	/// </para>
	/// <para>
	/// It derives from this backend's <see cref="TizenViewHandler{TVirtualView, TPlatformView}"/> rather
	/// than directly from MAUI's <see cref="ViewHandler{TVirtualView, TPlatformView}"/>. That is a
	/// correctness requirement, not a convention: <c>TizenLayoutHandler</c> and
	/// <c>TizenContentViewHandler</c> reach a child through <c>ITizenPlatformViewHandler</c> when adding
	/// it to the native tree, so a BlazorWebView whose handler did not implement that interface would
	/// simply never be parented. Deriving here also inherits Tizen measurement, arrangement, focus
	/// propagation and disposal.
	/// </para>
	/// </remarks>
	public class TizenBlazorWebViewHandler : TizenViewHandler<IBlazorWebView, NWebView>, IBlazorWebViewHandler
	{
		private const string UseBlockingDisposalSwitch = "BlazorWebView.UseBlockingDisposal";
		private const string JavaScriptMessageHandlerName = "BlazorHandler";

		/// <summary>Value stored in <see cref="s_javaScriptBridgeInstalled"/>; only its presence matters.</summary>
		private static readonly object BridgeInstalledMarker = new();

		internal const string AppOrigin = TizenWebViewManager.AppOrigin;

		private const string BlazorInitScript = @"
			window.__receiveMessageCallbacks = [];
			window.__dispatchMessageCallback = function(message) {
				window.__receiveMessageCallbacks.forEach(function(callback) { callback(message); });
			};
			window.external = {
				sendMessage: function(message) {
					window.BlazorHandler.postMessage(message);
				},
				receiveMessage: function(callback) {
					window.__receiveMessageCallbacks.push(callback);
				}
			};

			Blazor.start();

			(function () {
				window.onpageshow = function(event) {
					if (event.persisted) {
						window.location.reload();
					}
				};
			})();
		";

		/// <summary>
		/// Tizen registers the HTTP request interception callback on the shared <c>WebContext</c>, so a single
		/// static callback must route each request back to the handler that owns the requesting WebView.
		/// </summary>
		private static readonly Dictionary<string, WeakReference<TizenBlazorWebViewHandler>> s_handlers = new(StringComparer.Ordinal);

		/// <summary>
		/// Source of <see cref="HandlerKey"/> values.
		/// </summary>
		/// <remarks>
		/// A monotonic counter rather than <see cref="object.GetHashCode"/>. Hash codes are not unique -
		/// two live handlers can share one - and the key is what routes an intercepted request back to
		/// its owning handler, so a collision would silently serve one BlazorWebView's content into
		/// another. It would also make the routing table entry ambiguous on removal.
		/// </remarks>
		private static long s_nextHandlerKey;

		/// <summary>
		/// Tracks which platform views have already had the JavaScript message handler installed.
		/// </summary>
		/// <remarks>
		/// Tizen's <c>AddJavaScriptMessageHandler</c> has no remove counterpart, so a handler that is
		/// disconnected and reconnected against the same NUI WebView must not install a second one.
		/// A <see cref="ConditionalWeakTable{TKey, TValue}"/> keeps this from rooting platform views.
		/// </remarks>
		private static readonly ConditionalWeakTable<NWebView, object> s_javaScriptBridgeInstalled = new();

		private readonly StaticContentResponseCache _staticContentResponseCache = new();
		private readonly string _handlerKey = Interlocked.Increment(ref s_nextHandlerKey).ToString(CultureInfo.InvariantCulture);

		private TizenWebViewManager? _webviewManager;
		private StaticContentRequestProcessor? _requestProcessor;
		private ILogger? _logger;
		private string? _hostPage;
		private RootComponentsCollection? _rootComponents;
		private string? _userAgentBeforeConnect;

		/// <summary>
		/// This field is part of MAUI infrastructure and is not intended for use by application code.
		/// </summary>
		public static readonly PropertyMapper<IBlazorWebView, TizenBlazorWebViewHandler> TizenBlazorWebViewMapper = new(ViewMapper)
		{
			[nameof(IBlazorWebView.HostPage)] = MapHostPage,
			[nameof(IBlazorWebView.RootComponents)] = MapRootComponents,
		};

		/// <summary>
		/// Command mapper for <see cref="IBlazorWebView"/> on Tizen.
		/// </summary>
		/// <remarks>
		/// Chains <see cref="ViewHandler.ViewCommandMapper"/> so the standard view commands - focus and
		/// unfocus, invalidate measure, frame updates and z-index ordering - reach the platform view.
		/// Without a command mapper the handler is constructed with a null one and every
		/// <c>IView.Invoke</c> is silently dropped, so a BlazorWebView could not be focused
		/// programmatically and would not re-order correctly inside a layout.
		/// </remarks>
		public static readonly CommandMapper<IBlazorWebView, TizenBlazorWebViewHandler> TizenBlazorWebViewCommandMapper =
			new(ViewHandler.ViewCommandMapper);

		/// <summary>
		/// Initializes a new instance of <see cref="TizenBlazorWebViewHandler"/> with the default mappings.
		/// </summary>
		public TizenBlazorWebViewHandler()
			: base(TizenBlazorWebViewMapper, TizenBlazorWebViewCommandMapper)
		{
		}

		/// <summary>
		/// Initializes a new instance of <see cref="TizenBlazorWebViewHandler"/> using the specified mappings.
		/// </summary>
		/// <param name="mapper">The property mappings, or <see langword="null"/> to use the defaults.</param>
		/// <param name="commandMapper">The command mappings, if any.</param>
		public TizenBlazorWebViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? TizenBlazorWebViewMapper, commandMapper ?? TizenBlazorWebViewCommandMapper)
		{
		}

		internal ILogger Logger =>
			_logger ??= MauiContext?.Services?.GetService<ILogger<TizenBlazorWebViewHandler>>()
				?? (ILogger)NullLogger<TizenBlazorWebViewHandler>.Instance;

		internal string HandlerKey => _handlerKey;

		internal StaticContentResponseCache StaticContentResponseCache => _staticContentResponseCache;

		internal TizenWebViewManager? WebViewManager => _webviewManager;

		private bool RequiredStartupPropertiesSet => _hostPage != null && MauiContext?.Services != null;

		/// <summary>
		/// Maps the <see cref="IBlazorWebView.HostPage"/> property to the specified handler.
		/// </summary>
		/// <param name="handler">The handler.</param>
		/// <param name="webView">The virtual view.</param>
		public static void MapHostPage(TizenBlazorWebViewHandler handler, IBlazorWebView webView)
		{
			ArgumentNullException.ThrowIfNull(handler);
			ArgumentNullException.ThrowIfNull(webView);

			handler._hostPage = webView.HostPage;
			handler.StartWebViewCoreIfPossible();
		}

		/// <summary>
		/// Maps the <see cref="IBlazorWebView.RootComponents"/> property to the specified handler.
		/// </summary>
		/// <param name="handler">The handler.</param>
		/// <param name="webView">The virtual view.</param>
		public static void MapRootComponents(TizenBlazorWebViewHandler handler, IBlazorWebView webView)
		{
			ArgumentNullException.ThrowIfNull(handler);
			ArgumentNullException.ThrowIfNull(webView);

			handler.SetRootComponents(webView.RootComponents);
			handler.StartWebViewCoreIfPossible();
		}

		/// <inheritdoc />
		protected override NWebView CreatePlatformView()
		{
			return new NWebView
			{
				MouseEventsEnabled = true,
				KeyEventsEnabled = true,
			};
		}

		/// <inheritdoc />
		protected override void ConnectHandler(NWebView platformView)
		{
			ArgumentNullException.ThrowIfNull(platformView);

			base.ConnectHandler(platformView);

			// Register the routing entry before anything can produce a request for it.
			lock (s_handlers)
			{
				s_handlers[HandlerKey] = new WeakReference<TizenBlazorWebViewHandler>(this);
			}

			platformView.PageLoadFinished += OnLoadFinished;

			// Interception is registered on the shared WebContext and has no unregister counterpart, so
			// it is deliberately process-wide and permanent. OnRequestInterceptStaticCallback ignores
			// anything it cannot route, which is what keeps that safe.
			platformView.Context.RegisterHttpRequestInterceptedCallback(OnRequestInterceptStaticCallback);

			// AddJavaScriptMessageHandler also has no remove counterpart. Installing it twice on the
			// same NUI WebView - which happens on disconnect/reconnect - would leave a duplicate bridge
			// delivering every message twice.
			if (!s_javaScriptBridgeInstalled.TryGetValue(platformView, out _))
			{
				platformView.AddJavaScriptMessageHandler(JavaScriptMessageHandlerName, PostMessageFromJS);
				s_javaScriptBridgeInstalled.Add(platformView, BridgeInstalledMarker);
			}

			// Remember the agent so DisconnectHandler can restore it exactly. Appending on every
			// connect without restoring would accumulate suffixes across reconnects and eventually
			// make the routing key unparseable.
			_userAgentBeforeConnect = platformView.UserAgent;
			platformView.UserAgent = _userAgentBeforeConnect + BlazorWebViewUserAgent.BuildUserAgentSuffix(HandlerKey);

			Logger.TizenHandlerConnected(HandlerKey);
		}

		/// <inheritdoc />
		protected override void DisconnectHandler(NWebView platformView)
		{
			ArgumentNullException.ThrowIfNull(platformView);

			platformView.PageLoadFinished -= OnLoadFinished;

			// Symmetric with ConnectHandler. The JavaScript bridge and the interception callback cannot
			// be removed through the NUI API, so the user agent is the one piece of connect-time state
			// that must be undone - otherwise a reconnect appends a second suffix.
			if (_userAgentBeforeConnect is not null)
			{
				platformView.UserAgent = _userAgentBeforeConnect;
				_userAgentBeforeConnect = null;
			}

			lock (s_handlers)
			{
				s_handlers.Remove(HandlerKey);
			}

			SetRootComponents(null, clearPrevious: false);

			if (_webviewManager is not null)
			{
				// Dispose this component's contents so user-written disposal logic and Blazor disposal logic run.
				var disposalTask = _webviewManager.DisposeAsync().AsTask();

				if (AppContext.TryGetSwitch(UseBlockingDisposalSwitch, out var blockingDisposal) && blockingDisposal)
				{
					// Opt-in only: synchronously waiting here can deadlock when disposal needs the UI thread.
					disposalTask.GetAwaiter().GetResult();
				}
				else
				{
					_ = disposalTask.ContinueWith(
						static (task, state) => ((ILogger)state!).LogWarning(task.Exception, "Disposing the Tizen BlazorWebView failed."),
						Logger,
						System.Threading.CancellationToken.None,
						TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
						TaskScheduler.Default);
				}

				_webviewManager = null;
			}

			_requestProcessor = null;
			_staticContentResponseCache.Clear();

			Logger.TizenHandlerDisconnected(HandlerKey);

			base.DisconnectHandler(platformView);
		}

		/// <summary>
		/// Creates the <see cref="IFileProvider"/> used to serve static web assets from the Tizen
		/// application's resource directory.
		/// </summary>
		/// <param name="contentRootDir">The base directory for static web assets, such as <c>wwwroot</c>.</param>
		/// <returns>An <see cref="IFileProvider"/> rooted at the Tizen resource directory.</returns>
		public virtual IFileProvider CreateFileProvider(string contentRootDir)
			=> new TizenAssetFileProvider(GetResourceDirectory(), contentRootDir);

		/// <summary>
		/// Returns the Tizen application's resource directory that static web assets are served from.
		/// </summary>
		/// <remarks>Overridable so the file provider can be redirected in tests and in host-side tooling.</remarks>
		protected virtual string GetResourceDirectory()
			=> global::Tizen.Applications.Application.Current.DirectoryInfo.Resource;

		/// <summary>
		/// Calls the specified <paramref name="workItem"/> asynchronously and passes in the scoped services
		/// available to Razor components.
		/// </summary>
		/// <param name="workItem">The action to call.</param>
		/// <returns>
		/// A task representing <see langword="true"/> if <paramref name="workItem"/> was called, or
		/// <see langword="false"/> if it was not called because Blazor is not currently running.
		/// </returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="workItem"/> is <see langword="null"/>.</exception>
		public virtual async Task<bool> TryDispatchAsync(Action<IServiceProvider> workItem)
		{
			ArgumentNullException.ThrowIfNull(workItem);

			if (_webviewManager is null)
			{
				return false;
			}

			return await _webviewManager.TryDispatchAsync(workItem).ConfigureAwait(false);
		}

		private void PostMessageFromJS(string message)
		{
			_webviewManager?.MessageReceivedInternal(new Uri(PlatformView.Url), message);
		}

		private void OnLoadFinished(object? sender, WebViewPageLoadEventArgs e)
		{
			if (PlatformView.Url == AppOrigin)
			{
				PlatformView.EvaluateJavaScript(BlazorInitScript);
			}
		}

		private static void OnRequestInterceptStaticCallback(WebHttpRequestInterceptor interceptor)
		{
			if (BlazorWebViewUserAgent.TryGetHandlerKey(interceptor?.Headers, out var handlerKey))
			{
				TizenBlazorWebViewHandler? handler = null;
				lock (s_handlers)
				{
					if (s_handlers.TryGetValue(handlerKey, out var weakHandler) && !weakHandler.TryGetTarget(out handler))
					{
						s_handlers.Remove(handlerKey);
					}
				}

				if (handler is not null)
				{
					handler.HandleInterceptedRequest(interceptor!);
					return;
				}
			}

			interceptor?.Ignore();
		}

		private void HandleInterceptedRequest(WebHttpRequestInterceptor interceptor)
		{
			var processor = _requestProcessor;
			if (processor is null)
			{
				interceptor.Ignore();
				return;
			}

			processor.Process(new TizenInterceptedRequest(interceptor));
		}

		/// <summary>
		/// Swaps the tracked <see cref="RootComponentsCollection"/>.
		/// </summary>
		/// <param name="rootComponents">The new collection, or <see langword="null"/> to detach.</param>
		/// <param name="clearPrevious">
		/// Whether the previously tracked collection should be emptied. This is what the built-in handler does
		/// when the virtual view swaps collections, but it must not happen on disconnect: the collection belongs
		/// to the <c>BlazorWebView</c> control, so clearing it would discard the application's root components
		/// and leave nothing to render if the handler is later reconnected.
		/// </param>
		internal void SetRootComponents(RootComponentsCollection? rootComponents, bool clearPrevious = true)
		{
			if (_rootComponents is not null)
			{
				_rootComponents.CollectionChanged -= OnRootComponentsCollectionChanged;

				if (clearPrevious && !ReferenceEquals(_rootComponents, rootComponents))
				{
					_rootComponents.Clear();
				}
			}

			_rootComponents = rootComponents;

			if (_rootComponents is null)
			{
				return;
			}

			if (_rootComponents.Count > 0 && _webviewManager is not null)
			{
				_webviewManager.Dispatcher.AssertAccess();
				foreach (var component in _rootComponents)
				{
					_ = AddRootComponentAsync(component, _webviewManager);
				}
			}

			_rootComponents.CollectionChanged += OnRootComponentsCollectionChanged;
		}

		private void OnRootComponentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
		{
			var webviewManager = _webviewManager;
			if (webviewManager is null)
			{
				return;
			}

			var newItems = eventArgs.NewItems?.Cast<RootComponent>().ToList() ?? new List<RootComponent>();
			var oldItems = eventArgs.OldItems?.Cast<RootComponent>().ToList() ?? new List<RootComponent>();

			_ = webviewManager.Dispatcher.InvokeAsync(async () =>
			{
				foreach (var item in newItems.Except(oldItems))
				{
					await AddRootComponentAsync(item, webviewManager).ConfigureAwait(false);
				}

				foreach (var item in oldItems.Except(newItems))
				{
					await RemoveRootComponentAsync(item, webviewManager).ConfigureAwait(false);
				}
			});
		}

		/// <summary>
		/// Adds a <see cref="RootComponent"/> to <paramref name="webViewManager"/>.
		/// </summary>
		/// <remarks>
		/// <c>RootComponent.AddToWebViewManagerAsync</c> is internal to
		/// Microsoft.AspNetCore.Components.WebView.Maui, so this reproduces its validation and behavior using
		/// only public API. Validation is performed here (rather than deferring to the renderer) because
		/// XAML cannot rely on non-default constructors, so required properties may be unset.
		/// </remarks>
		internal static Task AddRootComponentAsync(RootComponent rootComponent, TizenWebViewManager webViewManager)
		{
			ArgumentNullException.ThrowIfNull(rootComponent);
			ArgumentNullException.ThrowIfNull(webViewManager);

			if (string.IsNullOrWhiteSpace(rootComponent.Selector))
			{
				throw new InvalidOperationException($"{nameof(RootComponent)} requires a value for its {nameof(RootComponent.Selector)} property, but no value was set.");
			}

			if (rootComponent.ComponentType is null)
			{
				throw new InvalidOperationException($"{nameof(RootComponent)} requires a value for its {nameof(RootComponent.ComponentType)} property, but no value was set.");
			}

			var parameterView = rootComponent.Parameters is null
				? ParameterView.Empty
				: ParameterView.FromDictionary(rootComponent.Parameters);

			return webViewManager.AddRootComponentAsync(rootComponent.ComponentType, rootComponent.Selector, parameterView);
		}

		internal static Task RemoveRootComponentAsync(RootComponent rootComponent, TizenWebViewManager webViewManager)
		{
			ArgumentNullException.ThrowIfNull(rootComponent);
			ArgumentNullException.ThrowIfNull(webViewManager);

			if (string.IsNullOrWhiteSpace(rootComponent.Selector))
			{
				throw new InvalidOperationException($"{nameof(RootComponent)} requires a value for its {nameof(RootComponent.Selector)} property, but no value was set.");
			}

			return webViewManager.RemoveRootComponentAsync(rootComponent.Selector);
		}

		private void StartWebViewCoreIfPossible()
		{
			if (!RequiredStartupPropertiesSet || _webviewManager is not null)
			{
				return;
			}

			if (PlatformView is null)
			{
				throw new InvalidOperationException($"Can't start {nameof(IBlazorWebView)} without a platform web view instance.");
			}

			var services = MauiContext!.Services;

			// The host page is assumed to be in the root of the content directory.
			var contentRootDir = System.IO.Path.GetDirectoryName(_hostPage!) ?? string.Empty;
			var hostPageRelativePath = System.IO.Path.GetRelativePath(contentRootDir, _hostPage!);

			var fileProvider = VirtualView.CreateFileProvider(contentRootDir);

			_webviewManager = new TizenWebViewManager(
				this,
				PlatformView,
				services,
				new TizenBlazorDispatcher(services.GetRequiredService<IDispatcher>()),
				fileProvider,
				VirtualView.JSComponents,
				contentRootDir,
				hostPageRelativePath);

			_requestProcessor = CreateRequestProcessor(_webviewManager);

			VirtualView.BlazorWebViewInitializing(new BlazorWebViewInitializingEventArgs());

			// BlazorWebViewInitializedEventArgs.WebView is declared only in the platform-specific builds of
			// Microsoft.AspNetCore.Components.WebView.Maui (ANDROID/IOS/MACCATALYST/WINDOWS/TIZEN). The package
			// this handler compiles against no longer produces a Tizen TFM, so the neutral net11.0 build has no
			// WebView property to populate. Applications that need the native control can read
			// ((TizenBlazorWebViewHandler)blazorWebView.Handler).PlatformView instead. See docs/blazorwebview.md.
			VirtualView.BlazorWebViewInitialized(new BlazorWebViewInitializedEventArgs());

			if (_rootComponents is not null)
			{
				foreach (var rootComponent in _rootComponents)
				{
					// Since the page isn't loaded yet, this always completes synchronously.
					_ = AddRootComponentAsync(rootComponent, _webviewManager);
				}
			}

			Logger.TizenWebViewStarted(contentRootDir, hostPageRelativePath);

			_webviewManager.Navigate(VirtualView.StartPath);
		}

		private StaticContentRequestProcessor CreateRequestProcessor(TizenWebViewManager webViewManager)
			=> new(
				AppOrigin,
				_staticContentResponseCache,
				webViewManager.TryGetResponseContentInternal,
				(requestUri, contentType) => StaticContentCacheControl.ResolveOverride(VirtualView, requestUri, contentType, Logger),
				Logger);

		private sealed class TizenInterceptedRequest : IInterceptedRequest
		{
			private readonly WebHttpRequestInterceptor _interceptor;

			public TizenInterceptedRequest(WebHttpRequestInterceptor interceptor)
			{
				_interceptor = interceptor;
			}

			public string Url => _interceptor.Url;

			public string Method => _interceptor.Method;

			public IDictionary<string, string> Headers => _interceptor.Headers;

			public void Ignore() => _interceptor.Ignore();

			public void SetResponse(string headerBlock, byte[] body) => _interceptor.SetResponse(headerBlock, body);
		}
	}
}
