using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	/// <summary>
	/// Pins why <see cref="IWebRequestInterceptingWebView.WebResourceRequested"/> is not wired up by the
	/// Tizen handler.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="IBlazorWebView"/> derives from <see cref="IWebRequestInterceptingWebView"/>, so a
	/// BlazorWebView exposes <c>WebResourceRequested</c> and an application may reasonably expect the
	/// Tizen backend to raise it from its request interception path. It does not, and cannot.
	/// </para>
	/// <para>
	/// <c>WebResourceRequestedEventArgs</c> declares only <see langword="internal"/> constructors, and
	/// its per-platform surface is strongly typed to WebView2, <c>WKWebView</c> or Android's
	/// <c>WebView</c>. There is no Tizen shape, because Tizen was removed from MAUI before this API
	/// was added. An out-of-repo backend therefore has no way to construct the argument it would need
	/// to pass, so the callback can never be raised.
	/// </para>
	/// <para>
	/// These tests exist so the situation is checked rather than assumed. If MAUI later exposes a
	/// constructible or extensible form, they fail and point at the work to do — which is the intended
	/// signal, not a nuisance. Tracked on the same upstream lane as the other BlazorWebView API gaps;
	/// see docs/blazorwebview.md.
	/// </para>
	/// </remarks>
	public class WebResourceRequestedTests
	{
		private static Type WebResourceRequestedEventArgsType =>
			typeof(IWebRequestInterceptingWebView).Assembly.GetType("Microsoft.Maui.WebResourceRequestedEventArgs")!;

		[Fact]
		public void BlazorWebViewStillExposesTheRequestInterceptionContract()
		{
			// If this ever stops being true the documentation below is stale.
			Assert.True(typeof(IWebRequestInterceptingWebView).IsAssignableFrom(typeof(IBlazorWebView)));
		}

		[Fact]
		public void EventArgsCannotBeConstructedByAThirdPartyBackend()
		{
			// The reason the Tizen handler cannot raise WebResourceRequested.
			var type = WebResourceRequestedEventArgsType;
			Assert.NotNull(type);

			var constructible = type
				.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
				.Any();

			Assert.False(
				constructible,
				"WebResourceRequestedEventArgs is now publicly constructible. The Tizen handler can and "
				+ "should raise IBlazorWebView.WebResourceRequested from its interception path; see "
				+ "docs/blazorwebview.md.");
		}

		[Fact]
		public void EventArgsExposesNoTizenPlatformShape()
		{
			// Even given an instance, there is nothing for a Tizen backend to populate: the properties
			// are typed to the other platforms' native request/response objects.
			var members = WebResourceRequestedEventArgsType
				.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Select(m => m.Name)
				.ToArray();

			Assert.DoesNotContain(members, name => name.Contains("Tizen", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public void StaticContentIsStillServedThroughTheTizenInterceptionPath()
		{
			// The capability is not lost, only the public notification: the handler intercepts every
			// request under the app origin through WebContext.RegisterHttpRequestInterceptedCallback and
			// serves it from the file provider. StaticContentRequestProcessorTests covers that path.
			Assert.Equal("http://0.0.0.0/", TizenBlazorWebViewHandler.AppOrigin);
		}
	}
}
