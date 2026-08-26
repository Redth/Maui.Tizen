using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.StaticContent;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	/// <summary>
	/// The Tizen request-interception surface, abstracted away from <c>Tizen.NUI.WebHttpRequestInterceptor</c>
	/// so the static content response behavior can be exercised without a native NUI WebView.
	/// </summary>
	internal interface IInterceptedRequest
	{
		string Url { get; }

		string Method { get; }

		IDictionary<string, string> Headers { get; }

		void Ignore();

		void SetResponse(string headerBlock, byte[] body);
	}

	/// <summary>
	/// Resolves static content for a request. Mirrors <c>WebViewManager.TryGetResponseContent</c>.
	/// </summary>
	internal delegate bool TryGetStaticContent(
		string uri,
		bool allowFallbackOnHostPage,
		out int statusCode,
		out string statusMessage,
		out Stream content,
		out IDictionary<string, string> headers);

	/// <summary>
	/// Implements the request mapping, caching and raw-response construction the Tizen NUI WebView requires.
	/// </summary>
	/// <remarks>
	/// Behavior is ported from <c>BlazorWebViewHandler.Tizen.cs</c> in dotnet/maui (net11.0):
	/// only requests under the app origin are served, query strings are stripped before the content lookup,
	/// a trailing slash allows falling back to the host page, and successful cacheable responses are buffered
	/// in a bounded LRU cache keyed by the original (unstripped) URL.
	/// </remarks>
	internal sealed class StaticContentRequestProcessor
	{
		private readonly string _appOrigin;
		private readonly StaticContentResponseCache _cache;
		private readonly TryGetStaticContent _contentLookup;
		private readonly Func<string, string, string?> _resolveCacheControlOverride;
		private readonly ILogger _logger;

		public StaticContentRequestProcessor(
			string appOrigin,
			StaticContentResponseCache cache,
			TryGetStaticContent contentLookup,
			Func<string, string, string?> resolveCacheControlOverride,
			ILogger? logger = null)
		{
			_appOrigin = appOrigin ?? throw new ArgumentNullException(nameof(appOrigin));
			_cache = cache ?? throw new ArgumentNullException(nameof(cache));
			_contentLookup = contentLookup ?? throw new ArgumentNullException(nameof(contentLookup));
			_resolveCacheControlOverride = resolveCacheControlOverride ?? throw new ArgumentNullException(nameof(resolveCacheControlOverride));
			_logger = logger ?? NullLogger.Instance;
		}

		public void Process(IInterceptedRequest request)
		{
			ArgumentNullException.ThrowIfNull(request);

			var url = request.Url;
			if (url is null || !url.StartsWith(_appOrigin, StringComparison.Ordinal))
			{
				request.Ignore();
				return;
			}

			var cacheRequestBehavior = StaticContentResponseCachePolicy.GetRequestBehavior(request.Method, request.Headers);
			if (_cache.TryGet(url, out var cachedResponse))
			{
				if (cacheRequestBehavior == StaticContentCacheRequestBehavior.Default)
				{
					var cachedRequestUri = QueryStringHelper.RemovePossibleQueryString(url);
					_logger.HandlingWebRequest(cachedRequestUri);
					_logger.ResponseContentBeingSent(cachedRequestUri, cachedResponse.StatusCode);
					request.SetResponse(BuildHeaderBlock(cachedResponse), cachedResponse.Content);
					return;
				}

				if (cacheRequestBehavior == StaticContentCacheRequestBehavior.Refresh)
				{
					_cache.Remove(url);
				}
			}

			var allowFallbackOnHostPage = url.EndsWith('/');
			var originalUrl = url;
			var requestUri = QueryStringHelper.RemovePossibleQueryString(url);
			_logger.HandlingWebRequest(requestUri);

			if (!_contentLookup(requestUri, allowFallbackOnHostPage, out var statusCode, out var statusMessage, out var content, out var headers))
			{
				_logger.ResponseContentNotFound(requestUri);
				request.Ignore();
				return;
			}

			// By default local caching is disabled so that user scripts are always re-executed. Applications can
			// opt specific resources into caching via BlazorWebView.StaticContentCacheControlProvider. The
			// original (unstripped) URI is passed so the provider can act on query strings (e.g. img.png?v=2).
			var contentType = headers.TryGetValue("Content-Type", out var resolvedContentType) ? resolvedContentType : string.Empty;
			var cacheControlOverride = _resolveCacheControlOverride(originalUrl, contentType);
			if (cacheControlOverride is not null)
			{
				headers["Cache-Control"] = cacheControlOverride;
			}

			byte[] contentBytes;
			using (var buffer = new MemoryStream())
			{
				using (content)
				{
					content.CopyTo(buffer);
				}

				contentBytes = buffer.ToArray();
			}

			if (statusCode == 200 &&
				cacheRequestBehavior != StaticContentCacheRequestBehavior.Disabled &&
				contentBytes.Length <= StaticContentResponseCache.MaxEntrySize &&
				headers.TryGetValue("Cache-Control", out var cacheControl) &&
				StaticContentResponseCachePolicy.TryGetCacheLifetime(cacheControl, out var cacheLifetime))
			{
				_cache.Set(new StaticContentResponse(
					originalUrl,
					contentType,
					statusCode,
					statusMessage,
					headers,
					contentBytes,
					StaticContentResponseCachePolicy.GetExpiration(cacheLifetime)));
			}

			_logger.ResponseContentBeingSent(requestUri, statusCode);
			request.SetResponse(BuildHeaderBlock(statusCode, statusMessage, headers), contentBytes);
		}

		public void ClearCache() => _cache.Clear();

		internal static string BuildHeaderBlock(StaticContentResponse response)
			=> BuildHeaderBlock(response.StatusCode, response.StatusMessage, response.Headers);

		internal static string BuildHeaderBlock(int statusCode, string statusMessage, IDictionary<string, string> headers)
		{
			var header = $"HTTP/1.0 {statusCode} {statusMessage}\r\n";
			foreach (var item in headers)
			{
				header += $"{item.Key}:{item.Value}\r\n";
			}

			return header + "\r\n";
		}
	}
}
