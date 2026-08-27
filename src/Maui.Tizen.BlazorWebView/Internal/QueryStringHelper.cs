// Ported from dotnet/maui (net11.0) src/BlazorWebView/src/SharedSource/QueryStringHelper.cs.
// The upstream helper is internal to Microsoft.AspNetCore.Components.WebView.Maui, so the
// standalone Tizen handler carries its own copy rather than reaching into private MAUI APIs.

using System;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	internal static class QueryStringHelper
	{
		/// <summary>
		/// Splits <paramref name="url"/> into its path portion and classifies whether that path should
		/// fall back to the host page when no static file matches it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Classification MUST run on the path, never on the raw URL. Blazor navigates with query
		/// strings, so <c>http://0.0.0.0/?returnUrl=x</c> is a root document request whose raw form does
		/// not end in <c>/</c>; testing the raw URL classifies it as an asset, the lookup misses, and the
		/// application fails to start.
		/// </para>
		/// <para>
		/// A path with no file extension in its last segment is also a document route. Blazor routing is
		/// client side, so a deep link such as <c>/CustomStart/SomeData</c> - reachable via
		/// <see cref="Microsoft.AspNetCore.Components.WebView.Maui.IBlazorWebView.StartPath"/> or an
		/// in-app navigation - has no file behind it and must be answered with the host page so the
		/// router can resolve it. Treating it as a missing asset returns 404 and Blazor never
		/// initializes.
		/// </para>
		/// </remarks>
		public static bool IsDocumentRequest(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return true;
			}

			if (path.EndsWith('/'))
			{
				return true;
			}

			var lastSegment = path.Substring(path.LastIndexOf('/') + 1);

			// No dot means no extension, so nothing on disk can satisfy it: it is a routed document.
			// A leading dot (".well-known") is a name, not an extension.
			var dot = lastSegment.LastIndexOf('.');
			return dot <= 0;
		}

		public static string RemovePossibleQueryString(string? url)
		{
			if (string.IsNullOrEmpty(url))
			{
				return string.Empty;
			}

			var indexOfQueryString = url.IndexOf('?', StringComparison.Ordinal);
			return indexOfQueryString == -1
				? url
				: url.Substring(0, indexOfQueryString);
		}
	}
}
