using System;
using System.Collections.Generic;
using System.Linq;
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
	/// The normal-mode Tizen web context is process-global even though each WebView can expose a
	/// different managed wrapper. This type therefore registers exactly once for that context mode and
	/// keeps the winning wrapper and delegate strongly rooted for the lifetime of the process. Handlers
	/// register themselves for routing instead of registering with the platform.
	/// </para>
	/// </remarks>
	internal static class WebRequestInterceptionCoordinator
	{
		private static readonly object s_gate = new();
		private static readonly object s_normalContextMode = new();

		/// <summary>
		/// Everything native holds a pointer into, rooted for the process lifetime.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Rooting the callback alone is not sufficient, and that mistake is invisible until it isn't.
		/// <c>WebContext.RegisterHttpRequestInterceptedCallback</c> does not hand our delegate to native
		/// directly: it stores it on the <c>WebContext</c> and registers an intermediate proxy
		/// (<c>WebContextHttpRequestInterceptedProxyCallback</c>) owned by that same <c>WebContext</c>
		/// instance. Native retains a function pointer to the <b>proxy</b>. So if the <c>WebContext</c>
		/// is collected, the proxy dies with it and interception stops even though our callback is still
		/// perfectly alive and rooted - every request then 404s, intermittently, under GC pressure.
		/// </para>
		/// <para>
		/// Both the context and the callback are therefore held strongly and permanently. There are at
		/// most a handful of web contexts in a process, and the platform offers no way to unregister, so
		/// there is nothing to release: keeping them alive costs a few references and is the only way the
		/// registration can be relied upon.
		/// </para>
		/// </remarks>
		/// <para>
		/// Typed as <see cref="object"/> so the rooting invariant - the part with the actual correctness
		/// risk - can be exercised on the host, where no real <c>WebContext</c> can be constructed. The
		/// public <see cref="Register"/> entry point remains strongly typed.
		/// </para>
		private static readonly List<(object Mode, object Context, object Callback)> s_rootedRegistrations = new();

		/// <summary>Handlers eligible to receive routed requests, by routing key.</summary>
		private static readonly Dictionary<string, WeakReference<ITizenInterceptedRequestRouter>> s_routes =
			new(StringComparer.Ordinal);

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

				NWebContext.HttpRequestInterceptedCallback callback = OnRequestIntercepted;

				if (!TryRootRegistration(s_normalContextMode, context, callback))
					return;

				try
				{
					context.RegisterHttpRequestInterceptedCallback(callback);
				}
				catch
				{
					s_rootedRegistrations.RemoveAll(
						registration => ReferenceEquals(registration.Mode, s_normalContextMode));
					throw;
				}
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

		internal static int RootedRegistrationCount
		{
			get
			{
				lock (s_gate)
				{
					return s_rootedRegistrations.Count;
				}
			}
		}

		/// <summary>
		/// Roots a stand-in context/callback pair without touching the platform, for tests.
		/// </summary>
		internal static void RootRegistrationForTesting(object context, object callback)
		{
			lock (s_gate)
			{
				s_rootedRegistrations.Add((new object(), context, callback));
			}
		}

		internal static bool RootRegistrationForModeForTesting(
			object mode,
			object context,
			object callback)
		{
			lock (s_gate)
			{
				return TryRootRegistration(mode, context, callback);
			}
		}

		/// <summary>Whether <paramref name="context"/> is strongly rooted by a live registration.</summary>
		internal static bool IsContextRooted(object context)
		{
			lock (s_gate)
			{
				return s_rootedRegistrations.Any(r => ReferenceEquals(r.Context, context));
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

		private static bool TryRootRegistration(
			object mode,
			object context,
			object callback)
		{
			foreach (var registration in s_rootedRegistrations)
			{
				if (ReferenceEquals(registration.Mode, mode))
					return false;
			}

			// Root the context AND the callback before registering: native takes the pointer to the
			// context-owned proxy immediately.
			s_rootedRegistrations.Add((mode, context, callback));
			return true;
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
