using System;
using System.Collections.Concurrent;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	internal sealed class HostPageLoadTracker
	{
		private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

		public void Record(string url) => _pending[Normalize(url)] = 0;

		public bool TryConsume(string url) => _pending.TryRemove(Normalize(url), out _);

		public bool IsPending(string url) => _pending.ContainsKey(Normalize(url));

		public void Clear() => _pending.Clear();

		private static string Normalize(string url)
		{
			ArgumentNullException.ThrowIfNull(url);

			var fragment = url.IndexOf('#', StringComparison.Ordinal);
			return fragment < 0 ? url : url.Substring(0, fragment);
		}
	}
}
