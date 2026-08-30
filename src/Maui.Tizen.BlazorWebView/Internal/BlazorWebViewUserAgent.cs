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
		/// Determines whether <paramref name="value"/> is a well-formed handler key.
		/// </summary>
		/// <remarks>
		/// Keys are generated from a monotonic counter, so they are always non-empty ASCII digits.
		/// Validating that here is what lets <see cref="TryGetHandlerKey"/> stop at the end of the key
		/// instead of swallowing whatever follows it in the user agent.
		/// </remarks>
		private static bool IsKeyCharacter(char c) => c >= '0' && c <= '9';

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

			// Read only the key itself rather than the rest of the string. The suffix is appended to a
			// user agent owned by the platform, and nothing prevents another component from appending
			// after it; taking the remainder verbatim would produce a key that matches no entry in the
			// routing table, and the request would be silently ignored instead of served.
			var start = index + HandlerKeyPrefix.Length;
			var end = start;
			while (end < agent.Length && IsKeyCharacter(agent[end]))
			{
				end++;
			}

			if (end == start)
			{
				return false;
			}

			handlerKey = agent.Substring(start, end - start);
			return true;
		}
	}
}
