// Ported from dotnet/maui (net11.0) src/BlazorWebView/src/SharedSource/QueryStringHelper.cs.
// The upstream helper is internal to Microsoft.AspNetCore.Components.WebView.Maui, so the
// standalone Tizen handler carries its own copy rather than reaching into private MAUI APIs.

using System;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	internal static class QueryStringHelper
	{
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
