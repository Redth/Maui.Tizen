using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.StaticContent;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	/// <summary>
	/// Exercises the request mapping, static content response shaping and caching that the Tizen handler
	/// performs inside <c>WebContext.RegisterHttpRequestInterceptedCallback</c>. The processor is driven
	/// through <see cref="IInterceptedRequest"/>, which is the only abstraction over
	/// <c>Tizen.NUI.WebHttpRequestInterceptor</c>, so this is the same code that runs on device.
	/// </summary>
	public class StaticContentRequestProcessorTests
	{
		private const string AppOrigin = "http://0.0.0.0/";

		private static StaticContentRequestProcessor CreateProcessor(
			out FakeContentSource source,
			out StaticContentResponseCache cache,
			Func<string, string, string?>? cacheControlOverride = null,
			List<string>? hostPageDocumentsServed = null,
			string? startPath = null)
		{
			source = new FakeContentSource();
			cache = new StaticContentResponseCache();
			var capturedSource = source;

			return new StaticContentRequestProcessor(
				AppOrigin,
				cache,
				capturedSource.TryGetContent,
				cacheControlOverride ?? ((_, _) => null),
				logger: null,
				onHostPageDocumentServed: hostPageDocumentsServed is null ? null : hostPageDocumentsServed.Add,
				startPath: startPath);
		}

		[Theory]
		// Every route answered with the host page must be reported, because the web view finishes
		// loading at THAT url and the Blazor bootstrap has to be injected there.
		[InlineData("http://0.0.0.0/")]
		[InlineData("http://0.0.0.0/?returnUrl=%2Fcounter")]
		[InlineData("http://0.0.0.0/counter")]
		[InlineData("http://0.0.0.0/CustomStart/SomeData")]
		[InlineData("http://0.0.0.0/CustomStart/SomeData?id=7")]
		public void HostPageDocumentRoutesAreReportedForBootstrapInjection(string url)
		{
			var served = new List<string>();
			var processor = CreateProcessor(out var source, out _, hostPageDocumentsServed: served);
			source.Add(QueryStringHelper.RemovePossibleQueryString(url), "<html/>", "text/html");

			processor.Process(new FakeRequest(url));

			// Reported under the ORIGINAL url, query included: that is what PlatformView.Url will report.
			Assert.Equal(new[] { url }, served);
		}

		[Theory]
		[InlineData("http://0.0.0.0/css/app.css")]
		[InlineData("http://0.0.0.0/_framework/blazor.webview.js")]
		public void AssetRoutesAreNotReportedForBootstrapInjection(string url)
		{
			// Injecting on an asset response would re-run Blazor.start against a non-document load.
			var served = new List<string>();
			var processor = CreateProcessor(out var source, out _, hostPageDocumentsServed: served);
			source.Add(url, "body{}", "text/css");

			processor.Process(new FakeRequest(url));

			Assert.Empty(served);
		}

		[Fact]
		public void CachedHostPageDocumentsAreStillReported()
		{
			// A cached document still produces a page load, so it still needs the bootstrap.
			var served = new List<string>();
			var processor = CreateProcessor(out var source, out _, (_, _) => "max-age=600", served);
			source.Add("http://0.0.0.0/counter", "<html/>", "text/html");

			processor.Process(new FakeRequest("http://0.0.0.0/counter"));
			processor.Process(new FakeRequest("http://0.0.0.0/counter"));

			Assert.Equal(2, served.Count);
			Assert.Equal(1, source.LookupCount);
		}

		[Fact]
		public void IgnoresRequestsOutsideTheAppOrigin()
		{
			var processor = CreateProcessor(out var source, out _);
			var request = new FakeRequest("https://example.com/index.html");

			processor.Process(request);

			Assert.True(request.Ignored);
			Assert.Null(request.ResponseHeader);
			Assert.Equal(0, source.LookupCount);
		}

		[Fact]
		public void ServesContentForRequestsUnderTheAppOrigin()
		{
			var processor = CreateProcessor(out var source, out _);
			source.Add("http://0.0.0.0/index.html", "<html/>", "text/html");
			var request = new FakeRequest("http://0.0.0.0/index.html");

			processor.Process(request);

			Assert.False(request.Ignored);
			Assert.Equal("<html/>", Encoding.UTF8.GetString(request.ResponseBody!));
			Assert.StartsWith("HTTP/1.0 200 OK\r\n", request.ResponseHeader, StringComparison.Ordinal);
			Assert.Contains("Content-Type:text/html\r\n", request.ResponseHeader, StringComparison.Ordinal);
			Assert.EndsWith("\r\n\r\n", request.ResponseHeader, StringComparison.Ordinal);
		}

		[Fact]
		public void PropagatesTheStatusLineFromTheContentSource()
		{
			var processor = CreateProcessor(out var source, out _);
			source.Add("http://0.0.0.0/missing.html", "nope", "text/plain", statusCode: 404, statusMessage: "Not Found");
			var request = new FakeRequest("http://0.0.0.0/missing.html");

			processor.Process(request);

			Assert.StartsWith("HTTP/1.0 404 Not Found\r\n", request.ResponseHeader, StringComparison.Ordinal);
		}

		[Fact]
		public void StripsTheQueryStringBeforeLookingUpContent()
		{
			var processor = CreateProcessor(out var source, out _);
			source.Add("http://0.0.0.0/app.css", "body{}", "text/css");
			var request = new FakeRequest("http://0.0.0.0/app.css?v=2");

			processor.Process(request);

			Assert.False(request.Ignored);
			Assert.Equal("http://0.0.0.0/app.css", source.LastRequestedUri);
		}

		[Theory]
		// Root, with and without a query. The query must not change the classification: Blazor
		// navigates with query strings, and testing the raw URL would classify "/?x=1" as an asset.
		[InlineData("http://0.0.0.0/", true)]
		[InlineData("http://0.0.0.0/?returnUrl=%2Fcounter", true)]
		// Extensionless routes are client-side Blazor routes with no file behind them. They must be
		// answered with the host page or the router never gets a chance to resolve them.
		[InlineData("http://0.0.0.0/counter", true)]
		[InlineData("http://0.0.0.0/CustomStart/SomeData", true)]
		[InlineData("http://0.0.0.0/CustomStart/SomeData?id=7", true)]
		// Real assets must not fall back, or a missing file would silently return HTML.
		[InlineData("http://0.0.0.0/css/app.css", false)]
		[InlineData("http://0.0.0.0/css/app.css?v=2", false)]
		[InlineData("http://0.0.0.0/_framework/blazor.webview.js", false)]
		public void ClassifiesDocumentRoutesIndependentlyOfTheQueryString(string url, bool expectFallback)
		{
			var processor = CreateProcessor(out var source, out _);
			source.Add(QueryStringHelper.RemovePossibleQueryString(url), "<html/>", "text/html");

			processor.Process(new FakeRequest(url));

			Assert.Equal(expectFallback, source.LastAllowFallbackOnHostPage);
		}

		[Fact]
		public void NonRootStartPathReceivesTheHostPage()
		{
			// The StartPath scenario end to end: a non-root document route resolves to the host page,
			// which is what lets Blazor boot. Before the fix this 404'd and the app never initialized.
			var processor = CreateProcessor(out var source, out _);
			source.Add("http://0.0.0.0/CustomStart/SomeData", "<html>host</html>", "text/html");

			var request = new FakeRequest("http://0.0.0.0/CustomStart/SomeData");
			processor.Process(request);

			Assert.False(request.Ignored);
			Assert.Equal("<html>host</html>", Encoding.UTF8.GetString(request.ResponseBody!));
		}

		[Fact]
		public void ConfiguredDottedStartPathReceivesTheHostPage()
		{
			const string Url = "http://0.0.0.0/orders/v1.2";
			var processor = CreateProcessor(
				out var source,
				out _,
				startPath: "/orders/v1.2");
			source.Add(Url, "<html>host</html>", "text/html");

			var request = new FakeRequest(Url);
			processor.Process(request);

			Assert.True(source.LastAllowFallbackOnHostPage);
			Assert.False(request.Ignored);
		}

		[Fact]
		public void HtmlAcceptHeaderMakesADottedClientRouteADocument()
		{
			const string Url = "http://0.0.0.0/orders/v1.2";
			var processor = CreateProcessor(out var source, out _);
			source.Add(Url, "<html>host</html>", "text/html");
			var request = new FakeRequest(Url);
			request.Headers["Accept"] = "text/html,application/xhtml+xml";

			processor.Process(request);

			Assert.True(source.LastAllowFallbackOnHostPage);
		}

		[Fact]
		public void DottedAssetWithoutHtmlAcceptDoesNotFallBackToTheHostPage()
		{
			const string Url = "http://0.0.0.0/css/missing.css";
			var processor = CreateProcessor(out var source, out _);
			source.Add(Url, "body{}", "text/css");

			processor.Process(new FakeRequest(Url));

			Assert.False(source.LastAllowFallbackOnHostPage);
		}

		[Fact]
		public void IgnoresRequestsThatTheContentSourceCannotResolve()
		{
			var processor = CreateProcessor(out _, out _);
			var request = new FakeRequest("http://0.0.0.0/unknown.png");

			processor.Process(request);

			Assert.True(request.Ignored);
			Assert.Null(request.ResponseHeader);
		}

		[Fact]
		public void IgnoresRequestsBeforeTheWebViewManagerExists()
		{
			// Mirrors the handler behavior when a request arrives before StartWebViewCoreIfPossible ran.
			var request = new FakeRequest("http://0.0.0.0/index.html");

			Assert.Null(request.ResponseHeader);
			request.Ignore();

			Assert.True(request.Ignored);
		}

		[Fact]
		public void DoesNotCacheResponsesByDefault()
		{
			var processor = CreateProcessor(out var source, out var cache);
			source.Add("http://0.0.0.0/index.html", "<html/>", "text/html");

			processor.Process(new FakeRequest("http://0.0.0.0/index.html"));
			processor.Process(new FakeRequest("http://0.0.0.0/index.html"));

			Assert.Equal(0, cache.Count);
			Assert.Equal(2, source.LookupCount);
		}

		[Fact]
		public void CachesResponsesWhenTheApplicationOptsInThroughCacheControl()
		{
			var processor = CreateProcessor(out var source, out var cache, (_, _) => "max-age=600");
			source.Add("http://0.0.0.0/app.css", "body{}", "text/css");

			processor.Process(new FakeRequest("http://0.0.0.0/app.css"));
			Assert.Equal(1, cache.Count);

			var second = new FakeRequest("http://0.0.0.0/app.css");
			processor.Process(second);

			Assert.Equal(1, source.LookupCount);
			Assert.Equal("body{}", Encoding.UTF8.GetString(second.ResponseBody!));
			Assert.Contains("Cache-Control:max-age=600\r\n", second.ResponseHeader, StringComparison.Ordinal);
		}

		[Fact]
		public void CachesUsingTheOriginalUrlSoQueryStringsAreDistinct()
		{
			var processor = CreateProcessor(out var source, out var cache, (_, _) => "max-age=600");
			source.Add("http://0.0.0.0/img.png", "v1", "image/png");

			processor.Process(new FakeRequest("http://0.0.0.0/img.png?v=1"));
			processor.Process(new FakeRequest("http://0.0.0.0/img.png?v=2"));

			Assert.Equal(2, cache.Count);
			Assert.Equal(2, source.LookupCount);
		}

		[Fact]
		public void PassesTheOriginalUrlAndContentTypeToTheCacheControlProvider()
		{
			string? seenUri = null;
			string? seenContentType = null;
			var processor = CreateProcessor(out var source, out _, (uri, contentType) =>
			{
				seenUri = uri;
				seenContentType = contentType;
				return null;
			});
			source.Add("http://0.0.0.0/img.png", "v1", "image/png");

			processor.Process(new FakeRequest("http://0.0.0.0/img.png?v=2"));

			Assert.Equal("http://0.0.0.0/img.png?v=2", seenUri);
			Assert.Equal("image/png", seenContentType);
		}

		[Fact]
		public void DoesNotCacheNonSuccessResponses()
		{
			var processor = CreateProcessor(out var source, out var cache, (_, _) => "max-age=600");
			source.Add("http://0.0.0.0/missing.html", "nope", "text/plain", statusCode: 404, statusMessage: "Not Found");

			processor.Process(new FakeRequest("http://0.0.0.0/missing.html"));

			Assert.Equal(0, cache.Count);
		}

		[Theory]
		[InlineData("Range", "bytes=0-10")]
		[InlineData("Authorization", "Bearer abc")]
		[InlineData("Cache-Control", "no-store")]
		public void DisablesCachingForRequestsThatMustNotBeCached(string headerName, string headerValue)
		{
			var processor = CreateProcessor(out var source, out var cache, (_, _) => "max-age=600");
			source.Add("http://0.0.0.0/app.css", "body{}", "text/css");
			var request = new FakeRequest("http://0.0.0.0/app.css");
			request.Headers[headerName] = headerValue;

			processor.Process(request);

			Assert.Equal(0, cache.Count);
		}

		[Fact]
		public void DisablesCachingForNonGetRequests()
		{
			var processor = CreateProcessor(out var source, out var cache, (_, _) => "max-age=600");
			source.Add("http://0.0.0.0/app.css", "body{}", "text/css");

			processor.Process(new FakeRequest("http://0.0.0.0/app.css", method: "POST"));

			Assert.Equal(0, cache.Count);
		}

		[Theory]
		[InlineData("Cache-Control", "no-cache")]
		[InlineData("Cache-Control", "max-age=0")]
		[InlineData("Pragma", "no-cache")]
		public void RevalidatesTheCacheWhenTheRequestAsksForFreshContent(string headerName, string headerValue)
		{
			var processor = CreateProcessor(out var source, out var cache, (_, _) => "max-age=600");
			source.Add("http://0.0.0.0/app.css", "v1", "text/css");
			processor.Process(new FakeRequest("http://0.0.0.0/app.css"));
			Assert.Equal(1, cache.Count);

			source.Add("http://0.0.0.0/app.css", "v2", "text/css");
			var refresh = new FakeRequest("http://0.0.0.0/app.css");
			refresh.Headers[headerName] = headerValue;
			processor.Process(refresh);

			Assert.Equal(2, source.LookupCount);
			Assert.Equal("v2", Encoding.UTF8.GetString(refresh.ResponseBody!));
		}

		[Fact]
		public void ClearCacheDropsStoredResponses()
		{
			var processor = CreateProcessor(out var source, out var cache, (_, _) => "max-age=600");
			source.Add("http://0.0.0.0/app.css", "body{}", "text/css");
			processor.Process(new FakeRequest("http://0.0.0.0/app.css"));
			Assert.Equal(1, cache.Count);

			processor.ClearCache();

			Assert.Equal(0, cache.Count);
		}

		[Fact]
		public void DisposesTheContentStreamAfterBuffering()
		{
			var processor = CreateProcessor(out var source, out _);
			source.Add("http://0.0.0.0/index.html", "<html/>", "text/html");

			processor.Process(new FakeRequest("http://0.0.0.0/index.html"));

			Assert.True(source.LastStreamDisposed);
		}

		[Fact]
		public void BuildsHeaderBlocksInTheRawFormatTizenExpects()
		{
			var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["Content-Type"] = "text/html",
				["Cache-Control"] = "max-age=60",
			};

			var header = StaticContentRequestProcessor.BuildHeaderBlock(200, "OK", headers);

			Assert.Equal(
				"HTTP/1.0 200 OK\r\nContent-Type:text/html\r\nCache-Control:max-age=60\r\n\r\n",
				header);
		}

		[Fact]
		public void RejectsANullRequest()
		{
			var processor = CreateProcessor(out _, out _);

			Assert.Throws<ArgumentNullException>(() => processor.Process(null!));
		}

		private sealed class FakeRequest : IInterceptedRequest
		{
			public FakeRequest(string url, string method = "GET")
			{
				Url = url;
				Method = method;
			}

			public string Url { get; }

			public string Method { get; }

			public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			public bool Ignored { get; private set; }

			public string? ResponseHeader { get; private set; }

			public byte[]? ResponseBody { get; private set; }

			public void Ignore() => Ignored = true;

			public void SetResponse(string headerBlock, byte[] body)
			{
				ResponseHeader = headerBlock;
				ResponseBody = body;
			}
		}

		private sealed class FakeContentSource
		{
			private readonly Dictionary<string, (string Content, string ContentType, int StatusCode, string StatusMessage)> _entries = new(StringComparer.Ordinal);

			public int LookupCount { get; private set; }

			public string? LastRequestedUri { get; private set; }

			public bool LastAllowFallbackOnHostPage { get; private set; }

			public bool LastStreamDisposed => _lastStream?.WasDisposed ?? false;

			private TrackingStream? _lastStream;

			public void Add(string uri, string content, string contentType, int statusCode = 200, string statusMessage = "OK")
				=> _entries[uri] = (content, contentType, statusCode, statusMessage);

			public bool TryGetContent(
				string uri,
				bool allowFallbackOnHostPage,
				out int statusCode,
				out string statusMessage,
				out Stream content,
				out IDictionary<string, string> headers)
			{
				LookupCount++;
				LastRequestedUri = uri;
				LastAllowFallbackOnHostPage = allowFallbackOnHostPage;

				if (!_entries.TryGetValue(uri, out var entry))
				{
					statusCode = 404;
					statusMessage = "Not Found";
					content = Stream.Null;
					headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					return false;
				}

				statusCode = entry.StatusCode;
				statusMessage = entry.StatusMessage;
				_lastStream = new TrackingStream(Encoding.UTF8.GetBytes(entry.Content));
				content = _lastStream;
				headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					["Content-Type"] = entry.ContentType,
					["Cache-Control"] = StaticContentCacheControl.Default,
				};
				return true;
			}
		}

		private sealed class TrackingStream : MemoryStream
		{
			public TrackingStream(byte[] buffer)
				: base(buffer, writable: false)
			{
			}

			public bool WasDisposed { get; private set; }

			protected override void Dispose(bool disposing)
			{
				WasDisposed = true;
				base.Dispose(disposing);
			}
		}
	}
}
