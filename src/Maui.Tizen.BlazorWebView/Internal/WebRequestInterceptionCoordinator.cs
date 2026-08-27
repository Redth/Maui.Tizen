using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Tizen.NUI;
using NWebContext = Tizen.NUI.WebContext;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	/// <summary>
	/// Owns the process-wide HTTP request interception that every Tizen BlazorWebView shares, and routes
	/// each intercepted request to the handler that produced it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Tizen registers interception on the <c>WebContext</c>, not on the web view, and exposes no
	/// unregister counterpart. Two consequences make a naive registration unsafe, and both are silent:
	/// </para>
	/// <para>
	/// <b>Last writer wins.</b> Each call to <c>RegisterHttpRequestInterceptedCallback</c> replaces the
	/// previous callback. Registering on every <c>ConnectHandler</c> means the most recently connected
	/// BlazorWebView's delegate displaces the one already installed. That happens to keep working only
	/// because every delegate routes through the same shared table - but it is accidental, and it makes
	/// the number of live registrations depend on connect ordering.
	/// </para>
	/// <para>
	/// <b>The delegate is not rooted.</b> A method group conversion allocates a new delegate per call,
	/// and the native side holds it without any managed reference. Once the last managed reference is
	/// dropped it becomes eligible for collection, after which interception stops and every request 404s
	/// - intermittently, under GC pressure, long after startup. Registering repeatedly makes this worse,
	/// not better: each new delegate abandons the previous one.
	/// </para>
	/// <para>
	/// This type therefore registers <b>exactly once per <c>WebContext</c></b> and keeps the winning
	/// delegate strongly rooted in a static field for the lifetime of the process. Handlers register
	/// themselves for routing instead of registering with the platform.
	/// </para>
	/// </remarks>
	internal static class WebRequestInterceptionCoordinator
	{
		private static readonly object s_gate = new();

		/// <summary>
		/// Contexts already carrying a registration. Keyed weakly so a context that goes away does not
		/// keep this table growing, but see <see cref="s_rootedCallbacks"/> for the delegate lifetime.
		/// </summary>
		private static readonly ConditionalWeakTable<NWebContext, object> s_registeredContexts = new();

		/// <summary>
		/// The delegates handed to native code, rooted for the process lifetime.
		/// </summary>
		/// <remarks>
		/// This is the whole point of the type. Native holds an unmanaged pointer to the callback with no
		/// managed reference of its own, so if this list did not exist the delegate would be collected
		/// and interception would silently stop.
		/// </remarks>
		private static readonly List<NWebContext.HttpRequestInterceptedCallback> s_rootedCallbacks = new();

		/// <summary>Handlers eligible to receive routed requests, by routing key.</summary>
		private static readonly Dictionary<string, WeakReference<ITizenInterceptedRequestRouter>> s_routes =
			new(StringComparer.Ordinal);

		private static readonly object RegisteredMarker = new();

		/// <summary>
		/// Ensures <paramref name="context"/> has interception installed, and routes its requests to
		/// <paramref name="router"/> when they carry <paramref name="routingKey"/>.
		/// </summary>
		public static void Register(NWebContext context, string routingKey, ITizenInterceptedRequestRouter router)
		{
			ArgumentNullException.ThrowIfNull(context);
			ArgumentNullException.ThrowIfNull(router);

			lock (s_gate)
			{
				s_routes[routingKey] = new WeakReference<ITizenInterceptedRequestRouter>(router);

				if (s_registeredContexts.TryGetValue(context, out _))
				{
					return;
				}

				NWebContext.HttpRequestInterceptedCallback callback = OnRequestIntercepted;

				// Root before registering: native takes the pointer immediately.
				s_rootedCallbacks.Add(callback);
				s_registeredContexts.Add(context, RegisteredMarker);
				context.RegisterHttpRequestInterceptedCallback(callback);
			}
		}

		/// <summary>Stops routing requests for <paramref name="routingKey"/>.</summary>
		/// <remarks>
		/// The platform registration itself is never removed - it cannot be - so this only detaches the
		/// handler. Requests that no longer resolve to a live router are ignored, which is the correct
		/// behavior for a web view that has been disconnected.
		/// </remarks>
		public static void Unregister(string routingKey)
		{
			lock (s_gate)
			{
				s_routes.Remove(routingKey);
			}
		}

		/// <summary>
		/// Adds a route without touching the platform, for tests.
		/// </summary>
		/// <remarks>
		/// <see cref="Register"/> needs a real <c>WebContext</c>, which only exists on device. Routing is
		/// the part with the correctness risk, so it is made reachable on the host rather than left
		/// untested.
		/// </remarks>
		internal static void RegisterRouteForTesting(string routingKey, ITizenInterceptedRequestRouter router)
		{
			lock (s_gate)
			{
				s_routes[routingKey] = new WeakReference<ITizenInterceptedRequestRouter>(router);
			}
		}

		internal static int RoutedHandlerCount
		{
			get
			{
				lock (s_gate)
				{
					return s_routes.Count;
				}
			}
		}

		internal static int RootedCallbackCount
		{
			get
			{
				lock (s_gate)
				{
					return s_rootedCallbacks.Count;
				}
			}
		}

		/// <summary>
		/// Resolves the router for an intercepted request, pruning entries whose handler has been
		/// collected.
		/// </summary>
		internal static ITizenInterceptedRequestRouter? ResolveRouter(IDictionary<string, string>? headers)
		{
			if (!BlazorWebViewUserAgent.TryGetHandlerKey(headers, out var key))
			{
				return null;
			}

			lock (s_gate)
			{
				if (!s_routes.TryGetValue(key, out var weak))
				{
					return null;
				}

				if (weak.TryGetTarget(out var router))
				{
					return router;
				}

				// The handler is gone; drop the stale route rather than resolving it again.
				s_routes.Remove(key);
				return null;
			}
		}

		private static void OnRequestIntercepted(WebHttpRequestInterceptor interceptor)
		{
			if (interceptor is null)
			{
				return;
			}

			var router = ResolveRouter(interceptor.Headers);
			if (router is null)
			{
				// Not ours, or the owning handler is gone. Ignoring lets the platform handle it normally
				// instead of failing the request.
				interceptor.Ignore();
				return;
			}

			router.HandleInterceptedRequest(interceptor);
		}
	}

	/// <summary>
	/// Receives requests routed by <see cref="WebRequestInterceptionCoordinator"/>.
	/// </summary>
	internal interface ITizenInterceptedRequestRouter
	{
		void HandleInterceptedRequest(WebHttpRequestInterceptor interceptor);
	}
}
