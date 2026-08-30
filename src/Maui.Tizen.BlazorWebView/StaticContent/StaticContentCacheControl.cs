// Ported from dotnet/maui (net11.0) src/BlazorWebView/src/Maui/StaticContentCacheControl.cs.
// The upstream type is internal to Microsoft.AspNetCore.Components.WebView.Maui.

using System;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.StaticContent
{
	internal static class StaticContentCacheControl
	{
		/// <summary>
		/// Historical default that disables all WebView caching of served content so that user scripts are
		/// always re-executed. Applications opt specific resources into caching through
		/// <see cref="IBlazorWebView.StaticContentCacheControlProvider"/>. See dotnet/maui#8279.
		/// </summary>
		internal const string Default = "no-cache, max-age=0, must-revalidate, no-store";

		/// <summary>
		/// Returns the application-provided <c>Cache-Control</c> override for the request, or
		/// <see langword="null"/> to keep the default.
		/// </summary>
		internal static string? ResolveOverride(IBlazorWebView? blazorWebView, string requestUri, string contentType, ILogger? logger)
		{
			var provider = blazorWebView?.StaticContentCacheControlProvider;
			if (provider is null)
			{
				return null;
			}

			// The Tizen request interceptor callback runs on a background thread, so guard against a malformed
			// URI rather than letting an unexpected UriFormatException surface as a crash.
			if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri))
			{
				return null;
			}

			string? cacheControl;
			try
			{
				cacheControl = provider(new BlazorWebViewStaticContentRequest(uri, contentType));
			}
			catch (Exception ex)
			{
				// The provider is arbitrary application code invoked from the native request-handling path.
				// A faulty provider must not take down static asset serving, so keep the default.
				logger?.StaticContentCacheControlProviderFailed(requestUri, ex);
				return null;
			}

			// An empty or whitespace-only value is deliberately treated like null (keep the default).
			if (string.IsNullOrWhiteSpace(cacheControl))
			{
				return null;
			}

			// Values containing CR/LF are rejected: Tizen concatenates the value into a raw response header
			// block, so a stray newline would produce a malformed response or allow header injection.
			if (cacheControl.Contains('\r', StringComparison.Ordinal) || cacheControl.Contains('\n', StringComparison.Ordinal))
			{
				return null;
			}

			return cacheControl;
		}
	}
}
