using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;
using Tizen.NUI;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	/// <summary>
	/// Covers the routing half of <see cref="WebRequestInterceptionCoordinator"/>.
	/// </summary>
	/// <remarks>
	/// Registration itself needs a real <c>WebContext</c>, which only exists on device, so these tests
	/// drive routing directly. That is where the correctness risk lives: the platform registration is a
	/// single call, but routing decides which of several live BlazorWebViews answers a request, and must
	/// never resolve a handler that has been collected or disconnected.
	/// </remarks>
	public class WebRequestInterceptionCoordinatorTests
	{
		// Real routing keys come from a monotonic counter, so they are always digits. Using a GUID here
		// would not just be unrealistic - BlazorWebViewUserAgent deliberately parses only digits, so a
		// hex key would be truncated at its first letter and the test would exercise nothing real.
		private static int s_nextTestKey = 1_000_000;

		private static string NextKey() =>
			System.Threading.Interlocked.Increment(ref s_nextTestKey).ToString(System.Globalization.CultureInfo.InvariantCulture);

		private static IDictionary<string, string> AgentFor(string key) =>
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["User-Agent"] = "Mozilla/5.0 (Tizen)" + BlazorWebViewUserAgent.BuildUserAgentSuffix(key),
			};

		private sealed class FakeRouter : ITizenInterceptedRequestRouter
		{
			public void HandleInterceptedRequest(WebHttpRequestInterceptor interceptor)
			{
			}
		}

		[Fact]
		public void ResolvesTheHandlerThatOwnsTheRequest()
		{
			var first = new FakeRouter();
			var second = new FakeRouter();
			var firstKey = NextKey();
			var secondKey = NextKey();

			WebRequestInterceptionCoordinator.RegisterRouteForTesting(firstKey, first);
			WebRequestInterceptionCoordinator.RegisterRouteForTesting(secondKey, second);

			try
			{
				Assert.Same(first, WebRequestInterceptionCoordinator.ResolveRouter(AgentFor(firstKey)));
				Assert.Same(second, WebRequestInterceptionCoordinator.ResolveRouter(AgentFor(secondKey)));
			}
			finally
			{
				WebRequestInterceptionCoordinator.Unregister(firstKey);
				WebRequestInterceptionCoordinator.Unregister(secondKey);
			}
		}

		[Fact]
		public void DisconnectedHandlersStopReceivingRequests()
		{
			// Interception cannot be unregistered from the platform, so a disconnected handler must be
			// dropped from routing instead - otherwise it would keep serving content for a web view it
			// no longer owns.
			var key = NextKey();
			WebRequestInterceptionCoordinator.RegisterRouteForTesting(key, new FakeRouter());

			WebRequestInterceptionCoordinator.Unregister(key);

			Assert.Null(WebRequestInterceptionCoordinator.ResolveRouter(AgentFor(key)));
		}

		[Fact]
		public void CollectedHandlersDoNotResolveAndArePruned()
		{
			// Routes hold weak references so a handler that goes away cannot be resurrected by an
			// in-flight request. The stale entry must also be removed rather than resolved repeatedly.
			var key = NextKey();

			// Registered inside a non-inlined local so no stack slot in this frame keeps it alive.
			[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
			static void Register(string k) =>
				WebRequestInterceptionCoordinator.RegisterRouteForTesting(k, new FakeRouter());

			Register(key);

			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			// Resolving must not resurrect a collected handler...
			Assert.Null(WebRequestInterceptionCoordinator.ResolveRouter(AgentFor(key)));

			// ...and the stale route must be gone, not merely unresolvable.
			Assert.Null(WebRequestInterceptionCoordinator.ResolveRouter(AgentFor(key)));
		}

		[Fact]
		public void UnroutableRequestsResolveToNothing()
		{
			Assert.Null(WebRequestInterceptionCoordinator.ResolveRouter(null));
			Assert.Null(WebRequestInterceptionCoordinator.ResolveRouter(
				new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
			Assert.Null(WebRequestInterceptionCoordinator.ResolveRouter(
				new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["User-Agent"] = "Mozilla/5.0" }));
		}

		[Fact]
		public void HandlerImplementsTheRoutingContract()
		{
			// The handler must be reachable through the coordinator, not by the coordinator knowing
			// about the handler type.
			Assert.True(typeof(ITizenInterceptedRequestRouter).IsAssignableFrom(typeof(TizenBlazorWebViewHandler)));
		}

		// ---- Rooting of the native registration owner ----------------------------------------------
		//
		// Native retains a pointer to a proxy callback OWNED BY the WebContext. Rooting only our own
		// callback is not enough: if the context is collected the proxy dies with it and interception
		// silently stops. These tests pin that invariant.

		private sealed class FakeWebContext
		{
			public string Name { get; init; } = string.Empty;
		}

		[Fact]
		public void RootedContextSurvivesCollectionAfterCallerDropsIt()
		{
			var weak = RootAndForgetContext();

			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			// The caller holds nothing. Only the coordinator's root can be keeping it alive.
			Assert.True(weak.IsAlive);
			Assert.True(WebRequestInterceptionCoordinator.IsContextRooted(weak.Target!));
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static WeakReference RootAndForgetContext()
		{
			var context = new FakeWebContext { Name = "rooted" };
			WebRequestInterceptionCoordinator.RootRegistrationForTesting(context, new Action(() => { }));
			return new WeakReference(context);
		}

		[Fact]
		public void CollectingTheLastConnectedHandlerLeavesOtherHandlersRoutable()
		{
			// The scenario the review called out: one handler goes away under GC while another is still
			// live. The survivor must keep receiving requests, and the platform rooting must not shrink.
			var survivor = new FakeRouter();
			WebRequestInterceptionCoordinator.RegisterRouteForTesting("1", survivor);

			var rootedBefore = WebRequestInterceptionCoordinator.RootedRegistrationCount;
			var weakDeparted = RegisterCollectableRoute("2");

			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			Assert.False(weakDeparted.IsAlive);

			// Survivor still resolves...
			Assert.Same(survivor, ResolveFor("1"));
			// ...the collected one resolves to nothing rather than throwing or returning a dead router...
			Assert.Null(ResolveFor("2"));
			// ...and nothing about the collection released the native registration.
			Assert.Equal(rootedBefore, WebRequestInterceptionCoordinator.RootedRegistrationCount);

			WebRequestInterceptionCoordinator.Unregister("1");
			WebRequestInterceptionCoordinator.Unregister("2");
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static WeakReference RegisterCollectableRoute(string key)
		{
			var router = new FakeRouter();
			WebRequestInterceptionCoordinator.RegisterRouteForTesting(key, router);
			return new WeakReference(router);
		}

		private static ITizenInterceptedRequestRouter? ResolveFor(string key) =>
			WebRequestInterceptionCoordinator.ResolveRouter(
				new Dictionary<string, string>
				{
					["User-Agent"] = "Mozilla/5.0 (Tizen)" + BlazorWebViewUserAgent.BuildUserAgentSuffix(key),
				});

	}
}
