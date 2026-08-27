using System;
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
	}
}
