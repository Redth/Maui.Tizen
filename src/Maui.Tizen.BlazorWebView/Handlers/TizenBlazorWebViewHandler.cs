// Ported from dotnet/maui (net11.0) src/BlazorWebView/src/Maui/Tizen/BlazorWebViewHandler.Tizen.cs.
//
// The upstream type is `Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler`, a partial
// class compiled into Microsoft.AspNetCore.Components.WebView.Maui. This package deliberately does NOT
// redefine that type: it supplies an independent handler that implements the public
// `IBlazorWebViewHandler` capability interface introduced by dotnet/maui#36658 and is registered through
// `IMauiBlazorWebViewBuilder.UsePlatformHandler`.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
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
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.StaticContent;
using Microsoft.Maui.Platforms.Tizen.Handlers;
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
		private sealed class ConnectionRequestRouter : ITizenInterceptedRequestRouter
		{
			private readonly InterceptedRequestLifetime _requests;

			public ConnectionRequestRouter(InterceptedRequestLifetime requests) =>
				_requests = requests;

			public void HandleInterceptedRequest(WebHttpRequestInterceptor interceptor) =>
				_requests.Process(new TizenInterceptedRequest(interceptor));
		}

		private sealed class HandlerConnection
		{
			public required long Generation { get; init; }
			public required NWebView PlatformView { get; init; }
			public required IBlazorWebView VirtualView { get; init; }
			public required string HostPage { get; init; }
			public required TizenWebViewManager Manager { get; init; }
			public required string RoutingKey { get; init; }
			public required ConnectionRequestRouter RequestRouter { get; init; }
			public required StaticContentResponseCache Cache { get; init; }
			public required HostPageLoadTracker HostPageLoads { get; init; }
			public required RootComponentConnection RootComponents { get; init; }
			public required InterceptedRequestLifetime Requests { get; init; }
			public required AsyncOperationLifetime Dispatches { get; init; }
			public required EventHandler<WebViewPageLoadEventArgs> PageLoadFinishedHandler { get; init; }

			public Task RetireAsync() =>
				Task.WhenAll(
					RootComponents.RetireAsync(),
					Requests.RetireAsync(),
					Dispatches.RetireAsync());
		}

		private const string JavaScriptMessageHandlerName = "BlazorHandler";
		private const string JavaScriptMessageGenerationPrefix = "__maui_tizen_connection__=";

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
					window.BlazorHandler.postMessage(""__maui_tizen_connection__=__GENERATION__;"" + message);
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
		/// Source of process-unique connection routing keys.
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

		private readonly StaticContentResponseCache _detachedStaticContentResponseCache = new();
		private readonly ConcurrentDictionary<long, HandlerConnection> _connectionsByGeneration = new();
		private readonly string _handlerKey = Interlocked.Increment(ref s_nextHandlerKey).ToString(CultureInfo.InvariantCulture);

		private HandlerConnection? _connection;
		private ILogger? _logger;
		private string? _hostPage;
		private RootComponentsCollection? _rootComponents;
		private long _connectionGeneration;
		private long _nextMessageGeneration;
		private int _connectionTransitioning;
		private int _routingKeyIssued;

		/// <summary>Whether a pass is in flight. Only ever touched on the Blazor dispatcher.</summary>
		private string? _userAgentBeforeConnect;

		/// <summary>
		/// This field is part of MAUI infrastructure and is not intended for use by application code.
		/// </summary>
		/// <remarks>
		/// Chains <see cref="TizenViewMappers.ViewMapper"/>, not MAUI's neutral
		/// <c>ViewHandler.ViewMapper</c>. The neutral mapper's bodies are no-ops on a non-platform TFM,
		/// so chaining it would leave every inherited <see cref="IView"/> property - background, clip,
		/// shadow, visibility, opacity, input transparency - silently unapplied on a BlazorWebView while
		/// working on every other Tizen view. Core's mapper carries the real Tizen bodies and itself
		/// chains MAUI's, so Controls' runtime <c>RemapForControls</c> additions are still observed.
		/// </remarks>
		public static readonly PropertyMapper<IBlazorWebView, TizenBlazorWebViewHandler> TizenBlazorWebViewMapper = new(TizenViewMappers.ViewMapper)
		{
			[nameof(IBlazorWebView.HostPage)] = MapHostPage,
			[nameof(IBlazorWebView.RootComponents)] = MapRootComponents,
		};

		/// <summary>
		/// Command mapper for <see cref="IBlazorWebView"/> on Tizen.
		/// </summary>
		/// <remarks>
		/// Chains <see cref="TizenViewMappers.ViewCommandMapper"/> so the standard view commands - focus
		/// and unfocus, invalidate measure, frame updates and z-index ordering - reach the platform view
		/// through the Tizen implementations rather than MAUI's neutral no-ops. Without a command mapper
		/// at all the handler is constructed with a null one and every <c>IView.Invoke</c> is silently
		/// dropped; with the neutral one the call is dispatched but does nothing on Tizen.
		/// </remarks>
		public static readonly CommandMapper<IBlazorWebView, TizenBlazorWebViewHandler> TizenBlazorWebViewCommandMapper =
			new(TizenViewMappers.ViewCommandMapper);

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

		internal StaticContentResponseCache StaticContentResponseCache =>
			Volatile.Read(ref _connection)?.Cache ?? _detachedStaticContentResponseCache;

		internal TizenWebViewManager? WebViewManager => Volatile.Read(ref _connection)?.Manager;

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

			// AddJavaScriptMessageHandler also has no remove counterpart. Installing it twice on the
			// same NUI WebView - which happens on disconnect/reconnect - would leave a duplicate bridge
			// delivering every message twice.
			if (!s_javaScriptBridgeInstalled.TryGetValue(platformView, out _))
			{
				platformView.AddJavaScriptMessageHandler(
					JavaScriptMessageHandlerName,
					message => PostMessageFromJS(platformView, message));
				s_javaScriptBridgeInstalled.Add(platformView, BridgeInstalledMarker);
			}

			// Remember the agent so each connection generation can append its own routing key and
			// replacement/disconnect can restore it exactly.
			_userAgentBeforeConnect = platformView.UserAgent;

			Logger.TizenHandlerConnected(HandlerKey);
		}

		/// <inheritdoc />
		protected override void DisconnectHandler(NWebView platformView)
		{
			ArgumentNullException.ThrowIfNull(platformView);

			Interlocked.Increment(ref _connectionGeneration);
			Interlocked.Exchange(ref _connectionTransitioning, 0);
			var connection = Interlocked.Exchange(ref _connection, null);
			var retirement = connection?.RetireAsync() ?? Task.CompletedTask;

			if (connection is not null)
				platformView.PageLoadFinished -= connection.PageLoadFinishedHandler;

			// Symmetric with ConnectHandler. The JavaScript bridge and the interception callback cannot
			// be removed through the NUI API, so the user agent is the one piece of connect-time state
			// that must be undone - otherwise a reconnect appends a second suffix.
			if (_userAgentBeforeConnect is not null)
			{
				platformView.UserAgent = _userAgentBeforeConnect;
				_userAgentBeforeConnect = null;
			}

			if (connection is not null)
				WebRequestInterceptionCoordinator.Unregister(connection.RoutingKey);

			SetRootComponents(null, clearPrevious: false);

			if (connection is not null)
			{
				// Dispose this component's contents so user-written disposal logic and Blazor disposal logic run.
				ObserveConnectionDisposal(DisposeConnectionAsync(connection, retirement));
			}

			_detachedStaticContentResponseCache.Clear();

			Logger.TizenHandlerDisconnected(HandlerKey);

			base.DisconnectHandler(platformView);
		}

		private void ObserveConnectionDisposal(Task disposalTask)
		{
			_ = disposalTask.ContinueWith(
				static (task, state) => ((TizenBlazorWebViewHandler)state!).ReportLifecycleFailure(
					task.Exception!,
					"Disposing the Tizen BlazorWebView failed."),
				this,
				System.Threading.CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}

		private void BeginConnectionReplacement(HandlerConnection connection)
		{
			if (Interlocked.CompareExchange(ref _connectionTransitioning, 1, 0) != 0)
				return;

			if (!ReferenceEquals(
				Interlocked.CompareExchange(ref _connection, null, connection),
				connection))
			{
				Interlocked.Exchange(ref _connectionTransitioning, 0);
				return;
			}

			WebRequestInterceptionCoordinator.Unregister(connection.RoutingKey);
			connection.PlatformView.PageLoadFinished -= connection.PageLoadFinishedHandler;
			RestoreUserAgent(connection.PlatformView);

			var generation = Interlocked.Increment(ref _connectionGeneration);
			var dispatcher = MauiContext?.Services.GetService<IDispatcher>();
			var replacement = ReplaceConnectionAsync(
				connection,
				connection.RetireAsync(),
				dispatcher,
				generation);
			_ = replacement.ContinueWith(
				static (task, state) => ((TizenBlazorWebViewHandler)state!).ReportLifecycleFailure(
					task.Exception!,
					"Replacing the Tizen BlazorWebView connection failed."),
				this,
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}

		private void ReportLifecycleFailure(Exception exception, string message)
		{
			try
			{
				Logger.LogWarning(exception, message);
			}
			catch
			{
				// Framework teardown and replacement must not fail because logging failed.
			}
		}

		private async Task ReplaceConnectionAsync(
			HandlerConnection connection,
			Task retirement,
			IDispatcher? dispatcher,
			long generation)
		{
			// The first replacement mapper starts this transition. Yield so the remaining mapped
			// properties are applied before a fast disposal can restart the manager.
			await Task.Yield();
			try
			{
				await DisposeConnectionAsync(connection, retirement).ConfigureAwait(false);

				if (dispatcher is null || generation != Volatile.Read(ref _connectionGeneration))
					return;

				await dispatcher.DispatchAsync(() =>
				{
					if (generation != Volatile.Read(ref _connectionGeneration))
						return;

					Interlocked.Exchange(ref _connectionTransitioning, 0);
					StartWebViewCoreIfPossible();
				}).ConfigureAwait(false);
			}
			finally
			{
				if (generation == Volatile.Read(ref _connectionGeneration))
					Interlocked.Exchange(ref _connectionTransitioning, 0);
			}
		}

		private void RestoreUserAgent(NWebView platformView)
		{
			if (_userAgentBeforeConnect is not null)
				platformView.UserAgent = _userAgentBeforeConnect;
		}

		private async Task DisposeConnectionAsync(
			HandlerConnection connection,
			Task retirement)
		{
			try
			{
				await retirement.ConfigureAwait(false);
				connection.HostPageLoads.Clear();
				connection.Cache.Clear();
				await connection.Manager.DisposeAsync().ConfigureAwait(false);
			}
			finally
			{
				if (_connectionsByGeneration.TryGetValue(connection.Generation, out var current)
					&& ReferenceEquals(current, connection))
				{
					_connectionsByGeneration.TryRemove(connection.Generation, out _);
				}
			}
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

			var connection = Volatile.Read(ref _connection);
			if (connection is null)
			{
				return false;
			}

			return await connection.Dispatches
				.TryRunAsync(() => connection.Manager.TryDispatchAsync(workItem))
				.ConfigureAwait(false);
		}

		private void PostMessageFromJS(NWebView source, string message)
		{
			if (!TryReadJavaScriptMessage(message, out var generation, out var payload)
				|| !_connectionsByGeneration.TryGetValue(generation, out var connection)
				|| !ReferenceEquals(connection.PlatformView, source))
			{
				return;
			}

			var delivery = connection.Dispatches.TryRunAsync(async () =>
			{
				await connection.Manager
					.MessageReceivedAsync(new Uri(source.Url), payload)
					.ConfigureAwait(false);
				return true;
			});
			_ = delivery.ContinueWith(
				static (task, state) => ((TizenBlazorWebViewHandler)state!).ReportLifecycleFailure(
					task.Exception!,
					"Processing a Tizen BlazorWebView JavaScript message failed."),
				this,
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}

		private void OnLoadFinished(
			HandlerConnection connection,
			object? sender,
			WebViewPageLoadEventArgs e)
		{
			if (!ReferenceEquals(Volatile.Read(ref _connection), connection)
				|| (sender is not null && !ReferenceEquals(sender, connection.PlatformView)))
				return;

			if (ShouldInjectBlazorStart(connection, e.PageUrl))
			{
				connection.PlatformView.EvaluateJavaScript(BuildBlazorInitScript(connection.Generation));
			}
		}

		/// <summary>
		/// Decides whether the document that just finished loading at <paramref name="url"/> is one we
		/// answered with the host page and therefore needs the Blazor bootstrap.
		/// </summary>
		/// <remarks>
		/// Kept free of platform state so the classification can be tested at the exact URL the web view
		/// reports, which is the value that actually drives injection.
		/// </remarks>
		internal bool ShouldInjectBlazorStart(string? url)
		{
			var connection = Volatile.Read(ref _connection);
			return connection is not null
				? ShouldInjectBlazorStart(connection, url)
				: IsAppOriginDocumentRoute(url);
		}

		private bool ShouldInjectBlazorStart(HandlerConnection connection, string? url)
		{
			if (string.IsNullOrEmpty(url))
			{
				return false;
			}

			// The connection's request processor records every host-page response before the document
			// finishes loading. Requiring that record prevents a stale load event from an older
			// generation from injecting the new generation's bootstrap into the wrong document.
			return connection.HostPageLoads.TryConsume(url);
		}

		private bool IsAppOriginDocumentRoute(string? url)
		{
			if (string.IsNullOrEmpty(url)
				|| !url.StartsWith(AppOrigin, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			return QueryStringHelper.IsDocumentRequest(QueryStringHelper.GetPath(url));
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
			if (rootComponents is not null)
			{
				foreach (var rootComponent in rootComponents)
					ValidateRootComponent(rootComponent);
			}

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
				Volatile.Read(ref _connection)?.RootComponents.UpdateDesired(null);
				return;
			}

			_rootComponents.CollectionChanged += OnRootComponentsCollectionChanged;
			Volatile.Read(ref _connection)?.RootComponents.UpdateDesired(_rootComponents);
		}

		private void OnRootComponentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
		{
			// The event arguments are deliberately ignored. Reconciliation is against the collection's
			// CURRENT contents, not against a per-event delta.
			if (_rootComponents is not null)
			{
				foreach (var rootComponent in _rootComponents)
					ValidateRootComponent(rootComponent);
			}

			Volatile.Read(ref _connection)?.RootComponents.UpdateDesired(_rootComponents);
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

			ValidateRootComponent(rootComponent);

			var parameterView = rootComponent.Parameters is null
				? ParameterView.Empty
				: ParameterView.FromDictionary(rootComponent.Parameters);

			return webViewManager.AddRootComponentAsync(rootComponent.ComponentType!, rootComponent.Selector!, parameterView);
		}

		private static void ValidateRootComponent(RootComponent rootComponent)
		{
			if (string.IsNullOrWhiteSpace(rootComponent.Selector))
			{
				throw new InvalidOperationException($"{nameof(RootComponent)} requires a value for its {nameof(RootComponent.Selector)} property, but no value was set.");
			}

			if (rootComponent.ComponentType is null)
			{
				throw new InvalidOperationException($"{nameof(RootComponent)} requires a value for its {nameof(RootComponent.ComponentType)} property, but no value was set.");
			}
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
			if (Volatile.Read(ref _connectionTransitioning) != 0)
			{
				return;
			}

			var existing = Volatile.Read(ref _connection);
			if (existing is not null)
			{
				if (NeedsConnectionReplacement(
					existing.VirtualView,
					existing.HostPage,
					VirtualView,
					_hostPage))
				{
					BeginConnectionReplacement(existing);
				}

				return;
			}

			if (!RequiredStartupPropertiesSet)
				return;

			if (PlatformView is null)
			{
				throw new InvalidOperationException($"Can't start {nameof(IBlazorWebView)} without a platform web view instance.");
			}

			var services = MauiContext!.Services;

			// The host page is assumed to be in the root of the content directory.
			var contentRootDir = System.IO.Path.GetDirectoryName(_hostPage!) ?? string.Empty;
			var hostPageRelativePath = System.IO.Path.GetRelativePath(contentRootDir, _hostPage!);

			var virtualView = VirtualView;
			var fileProvider = virtualView.CreateFileProvider(contentRootDir);

			var webviewManager = new TizenWebViewManager(
				this,
				PlatformView,
				services,
				new TizenBlazorDispatcher(services.GetRequiredService<IDispatcher>()),
				fileProvider,
				virtualView.JSComponents,
				contentRootDir,
				hostPageRelativePath);

			var cache = new StaticContentResponseCache();
			var hostPageLoads = new HostPageLoadTracker();
			var requestProcessor = CreateRequestProcessor(
				webviewManager,
				virtualView,
				cache,
				hostPageLoads.Record);
			var requests = new InterceptedRequestLifetime(requestProcessor.Process, Logger);
			var requestRouter = new ConnectionRequestRouter(requests);
			var routingKey = CreateRoutingKey();
			var rootComponents = new RootComponentConnection(
				rootComponent => AddRootComponentAsync(rootComponent, webviewManager),
				rootComponent => RemoveRootComponentAsync(rootComponent, webviewManager),
				work => webviewManager.Dispatcher.InvokeAsync(work),
				ReportRootComponentFailure);
			HandlerConnection? connection = null;
			EventHandler<WebViewPageLoadEventArgs> pageLoadFinished =
				(sender, args) => OnLoadFinished(connection!, sender, args);
			connection = new HandlerConnection
			{
				Generation = Interlocked.Increment(ref _nextMessageGeneration),
				PlatformView = PlatformView,
				VirtualView = virtualView,
				HostPage = _hostPage!,
				Manager = webviewManager,
				RoutingKey = routingKey,
				RequestRouter = requestRouter,
				Cache = cache,
				HostPageLoads = hostPageLoads,
				RootComponents = rootComponents,
				Requests = requests,
				Dispatches = new AsyncOperationLifetime(),
				PageLoadFinishedHandler = pageLoadFinished,
			};

			if (Interlocked.CompareExchange(ref _connection, connection, null) is not null)
			{
				ObserveConnectionDisposal(DisposeConnectionAsync(connection, connection.RetireAsync()));
				return;
			}
			_connectionsByGeneration[connection.Generation] = connection;

			try
			{
				PlatformView.PageLoadFinished += connection.PageLoadFinishedHandler;
				PlatformView.UserAgent =
					(_userAgentBeforeConnect ?? PlatformView.UserAgent)
					+ BlazorWebViewUserAgent.BuildUserAgentSuffix(routingKey);
				WebRequestInterceptionCoordinator.Register(
					PlatformView.Context,
					routingKey,
					requestRouter);

				virtualView.BlazorWebViewInitializing(new BlazorWebViewInitializingEventArgs());

				// BlazorWebViewInitializedEventArgs.WebView is declared only in the platform-specific builds of
				// Microsoft.AspNetCore.Components.WebView.Maui (ANDROID/IOS/MACCATALYST/WINDOWS/TIZEN). The package
				// this handler compiles against no longer produces a Tizen TFM, so the neutral net11.0 build has no
				// WebView property to populate. Applications that need the native control can read
				// ((TizenBlazorWebViewHandler)blazorWebView.Handler).PlatformView instead. See docs/blazorwebview.md.
				virtualView.BlazorWebViewInitialized(new BlazorWebViewInitializedEventArgs());

				rootComponents.UpdateDesired(_rootComponents);

				Logger.TizenWebViewStarted(contentRootDir, hostPageRelativePath);

				webviewManager.Navigate(virtualView.StartPath);
			}
			catch
			{
				PlatformView.PageLoadFinished -= connection.PageLoadFinishedHandler;
				WebRequestInterceptionCoordinator.Unregister(routingKey);
				RestoreUserAgent(PlatformView);
				if (ReferenceEquals(Interlocked.CompareExchange(ref _connection, null, connection), connection))
					ObserveConnectionDisposal(DisposeConnectionAsync(connection, connection.RetireAsync()));

				throw;
			}
		}

		internal static bool NeedsConnectionReplacement(
			IBlazorWebView connectedView,
			string connectedHostPage,
			IBlazorWebView currentView,
			string? currentHostPage) =>
			!ReferenceEquals(connectedView, currentView)
			|| !string.Equals(connectedHostPage, currentHostPage, StringComparison.Ordinal);

		internal static string BuildBlazorInitScript(long generation) =>
			BlazorInitScript.Replace(
				"__GENERATION__",
				generation.ToString(CultureInfo.InvariantCulture),
				StringComparison.Ordinal);

		internal static bool TryReadJavaScriptMessage(
			string message,
			out long generation,
			out string payload)
		{
			generation = default;
			payload = string.Empty;
			if (!message.StartsWith(JavaScriptMessageGenerationPrefix, StringComparison.Ordinal))
				return false;

			var delimiter = message.IndexOf(';', JavaScriptMessageGenerationPrefix.Length);
			if (delimiter < 0
				|| !long.TryParse(
					message.AsSpan(
						JavaScriptMessageGenerationPrefix.Length,
						delimiter - JavaScriptMessageGenerationPrefix.Length),
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out generation))
			{
				return false;
			}

			payload = message.Substring(delimiter + 1);
			return true;
		}

		private string CreateRoutingKey()
		{
			if (Interlocked.Increment(ref _routingKeyIssued) == 1)
				return _handlerKey;

			return Interlocked.Increment(ref s_nextHandlerKey).ToString(CultureInfo.InvariantCulture);
		}

		private StaticContentRequestProcessor CreateRequestProcessor(
			TizenWebViewManager webViewManager,
			IBlazorWebView virtualView,
			StaticContentResponseCache cache,
			Action<string> onHostPageDocumentServed)
			=> new(
				AppOrigin,
				cache,
				webViewManager.TryGetResponseContentInternal,
				(requestUri, contentType) => StaticContentCacheControl.ResolveOverride(
					virtualView,
					requestUri,
					contentType,
					Logger),
				logger: Logger,
				onHostPageDocumentServed: onHostPageDocumentServed,
				startPath: virtualView.StartPath);

		private void ReportRootComponentFailure(Exception exception)
		{
			try
			{
				Logger.LogError(exception, "Reconciling Tizen BlazorWebView root components failed.");
			}
			catch
			{
				// A logger failure must not create an unobserved dispatcher exception.
			}
		}

		private sealed class TizenInterceptedRequest : IInterceptedRequest
		{
			private readonly WebHttpRequestInterceptor _interceptor;
			private int _completed;

			public TizenInterceptedRequest(WebHttpRequestInterceptor interceptor)
			{
				_interceptor = interceptor;
			}

			public string Url => _interceptor.Url;

			public string Method => _interceptor.Method;

			public IDictionary<string, string> Headers => _interceptor.Headers;

			public void Ignore()
			{
				if (Interlocked.Exchange(ref _completed, 1) == 0)
					_interceptor.Ignore();
			}

			public void SetResponse(string headerBlock, byte[] body)
			{
				if (Interlocked.Exchange(ref _completed, 1) == 0)
					_interceptor.SetResponse(headerBlock, body);
			}
		}
	}
}
