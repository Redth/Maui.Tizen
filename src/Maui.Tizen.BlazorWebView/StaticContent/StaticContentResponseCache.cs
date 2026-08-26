// Ported from dotnet/maui (net11.0) src/BlazorWebView/src/Maui/StaticContentResponseCache.cs.
// The upstream types are internal to Microsoft.AspNetCore.Components.WebView.Maui, so the
// standalone Tizen handler carries its own copy to preserve identical response behavior.

using System;
using System.Collections.Generic;
using System.Net.Http.Headers;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.StaticContent
{
	/// <summary>
	/// A bounded, least-recently-used cache of static content responses served to the Tizen NUI WebView.
	/// </summary>
	internal sealed class StaticContentResponseCache
	{
		internal const int MaxEntrySize = 8 * 1024 * 1024;
		private const int MaxEntryCount = 256;
		private const long MaxTotalSize = 32L * 1024 * 1024;

		private readonly object _lock = new();
		private readonly Dictionary<string, LinkedListNode<StaticContentResponse>> _entries = new(StringComparer.Ordinal);
		private readonly LinkedList<StaticContentResponse> _leastRecentlyUsed = new();
		private long _totalSize;

		public int Count
		{
			get
			{
				lock (_lock)
				{
					return _entries.Count;
				}
			}
		}

		public bool TryGet(string requestUri, out StaticContentResponse cachedResponse)
		{
			lock (_lock)
			{
				if (!_entries.TryGetValue(requestUri, out var node))
				{
					cachedResponse = null!;
					return false;
				}

				if (node.Value.ExpiresAt <= DateTimeOffset.UtcNow)
				{
					Remove(node);
					cachedResponse = null!;
					return false;
				}

				_leastRecentlyUsed.Remove(node);
				_leastRecentlyUsed.AddLast(node);
				cachedResponse = node.Value;
				return true;
			}
		}

		public void Set(StaticContentResponse cachedResponse)
		{
			if (cachedResponse.Content.Length > MaxEntrySize)
			{
				return;
			}

			lock (_lock)
			{
				if (_entries.TryGetValue(cachedResponse.RequestUri, out var existingNode))
				{
					Remove(existingNode);
				}

				while (_leastRecentlyUsed.Count >= MaxEntryCount ||
					(_leastRecentlyUsed.Count > 0 && _totalSize + cachedResponse.Content.Length > MaxTotalSize))
				{
					Remove(_leastRecentlyUsed.First!);
				}

				var node = _leastRecentlyUsed.AddLast(cachedResponse);
				_entries.Add(cachedResponse.RequestUri, node);
				_totalSize += cachedResponse.Content.Length;
			}
		}

		public void Remove(string requestUri)
		{
			lock (_lock)
			{
				if (_entries.TryGetValue(requestUri, out var node))
				{
					Remove(node);
				}
			}
		}

		public void Clear()
		{
			lock (_lock)
			{
				_entries.Clear();
				_leastRecentlyUsed.Clear();
				_totalSize = 0;
			}
		}

		private void Remove(LinkedListNode<StaticContentResponse> node)
		{
			_entries.Remove(node.Value.RequestUri);
			_leastRecentlyUsed.Remove(node);
			_totalSize -= node.Value.Content.Length;
		}
	}

	/// <summary>
	/// A buffered static content response that can be replayed to the Tizen request interceptor.
	/// </summary>
	internal sealed class StaticContentResponse
	{
		public StaticContentResponse(
			string requestUri,
			string contentType,
			int statusCode,
			string statusMessage,
			IDictionary<string, string> headers,
			byte[] content,
			DateTimeOffset expiresAt)
		{
			RequestUri = requestUri;
			ContentType = contentType;
			StatusCode = statusCode;
			StatusMessage = statusMessage;
			Headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
			Content = content;
			ExpiresAt = expiresAt;
		}

		public string RequestUri { get; }
		public string ContentType { get; }
		public int StatusCode { get; }
		public string StatusMessage { get; }
		public Dictionary<string, string> Headers { get; }
		public byte[] Content { get; }
		public DateTimeOffset ExpiresAt { get; }
	}

	/// <summary>
	/// Decides whether a request may be served from, or stored in, the static content cache.
	/// </summary>
	internal static class StaticContentResponseCachePolicy
	{
		public static StaticContentCacheRequestBehavior GetRequestBehavior(
			string? method,
			IEnumerable<KeyValuePair<string, string>>? headers)
		{
			if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
			{
				return StaticContentCacheRequestBehavior.Disabled;
			}

			var behavior = StaticContentCacheRequestBehavior.Default;
			if (headers is not null)
			{
				foreach (var header in headers)
				{
					if (string.Equals(header.Key, "Range", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
					{
						return StaticContentCacheRequestBehavior.Disabled;
					}

					if (string.Equals(header.Key, "Cache-Control", StringComparison.OrdinalIgnoreCase) &&
						CacheControlHeaderValue.TryParse(header.Value, out var cacheControl))
					{
						if (cacheControl.NoStore)
						{
							return StaticContentCacheRequestBehavior.Disabled;
						}

						if (cacheControl.NoCache ||
							(cacheControl.MaxAge is TimeSpan maxAge && maxAge <= TimeSpan.Zero))
						{
							behavior = StaticContentCacheRequestBehavior.Refresh;
						}
					}

					if (string.Equals(header.Key, "Pragma", StringComparison.OrdinalIgnoreCase) &&
						ContainsDirective(header.Value, "no-cache"))
					{
						behavior = StaticContentCacheRequestBehavior.Refresh;
					}
				}
			}

			return behavior;
		}

		public static bool TryGetCacheLifetime(string cacheControl, out TimeSpan cacheLifetime)
		{
			cacheLifetime = default;

			if (!CacheControlHeaderValue.TryParse(cacheControl, out var parsedCacheControl) ||
				parsedCacheControl.NoStore ||
				parsedCacheControl.NoCache ||
				parsedCacheControl.MaxAge is not TimeSpan maxAge ||
				maxAge <= TimeSpan.Zero)
			{
				return false;
			}

			cacheLifetime = maxAge;
			return true;
		}

		public static DateTimeOffset GetExpiration(TimeSpan cacheLifetime)
		{
			var now = DateTimeOffset.UtcNow;
			var maximumLifetime = DateTimeOffset.MaxValue - now;
			return cacheLifetime >= maximumLifetime
				? DateTimeOffset.MaxValue
				: now + cacheLifetime;
		}

		private static bool ContainsDirective(string value, string directive)
		{
			foreach (var item in value.Split(','))
			{
				if (string.Equals(item.Trim(), directive, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}
	}

	internal enum StaticContentCacheRequestBehavior
	{
		Disabled,
		Default,
		Refresh,
	}
}
