using System;
using System.Collections.Generic;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	/// <summary>
	/// Tizen's HTTP request interception callback is registered per <c>WebContext</c> and is therefore
	/// effectively process-wide. The Tizen handler tags its own NUI WebView's user agent with a unique
	/// key so an intercepted request can be routed back to the handler that owns it.
	/// </summary>
	internal static class BlazorWebViewUserAgent
	{
		internal const string HandlerKeyPrefix = "BlazorWebView:";
		internal const string UserAgentHeaderKey = "User-Agent";

		/// <summary>
		/// Builds the user agent suffix appended to the NUI WebView's user agent for <paramref name="handlerKey"/>.
		/// </summary>
		public static string BuildUserAgentSuffix(string handlerKey) => $" {HandlerKeyPrefix}{handlerKey}";

		/// <summary>
		/// Extracts the owning handler key from the intercepted request headers.
		/// </summary>
		/// <returns><see langword="true"/> when the request originated from a tagged BlazorWebView.</returns>
		public static bool TryGetHandlerKey(IDictionary<string, string>? headers, out string handlerKey)
		{
			handlerKey = string.Empty;

			if (headers is null || !headers.TryGetValue(UserAgentHeaderKey, out var agent) || string.IsNullOrEmpty(agent))
			{
				return false;
			}

			var index = agent.IndexOf(HandlerKeyPrefix, StringComparison.Ordinal);
			if (index < 0)
			{
				return false;
			}

			var key = agent.Substring(index + HandlerKeyPrefix.Length);
			if (key.Length == 0)
			{
				return false;
			}

			handlerKey = key;
			return true;
		}
	}
}
