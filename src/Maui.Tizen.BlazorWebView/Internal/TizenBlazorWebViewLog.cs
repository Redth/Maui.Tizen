using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	/// <summary>
	/// Structured logging for the Tizen BlazorWebView handler.
	/// </summary>
	/// <remarks>
	/// The equivalent upstream helper (<c>src/BlazorWebView/src/SharedSource/Log.cs</c>) is internal to
	/// <c>Microsoft.AspNetCore.Components.WebView.Maui</c>. Event ids intentionally match the upstream
	/// values for the messages that were ported so existing log queries keep working after migration.
	/// </remarks>
	internal static partial class TizenBlazorWebViewLog
	{
		[LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Handling web request to URI '{requestUri}'.")]
		public static partial void HandlingWebRequest(this ILogger logger, string requestUri);

		[LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "Response content being sent for web request to URI '{requestUri}' with HTTP status code {statusCode}.")]
		public static partial void ResponseContentBeingSent(this ILogger logger, string requestUri, int statusCode);

		[LoggerMessage(EventId = 6, Level = LogLevel.Debug, Message = "Response content was not found for web request to URI '{requestUri}'.")]
		public static partial void ResponseContentNotFound(this ILogger logger, string requestUri);

		[LoggerMessage(EventId = 19, Level = LogLevel.Error, Message = "The StaticContentCacheControlProvider threw an exception for request '{requestUri}'. Falling back to the default Cache-Control header.")]
		public static partial void StaticContentCacheControlProviderFailed(this ILogger logger, string requestUri, Exception exception);

		[LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Tizen BlazorWebView handler '{handlerKey}' connected to the NUI WebView.")]
		public static partial void TizenHandlerConnected(this ILogger logger, string handlerKey);

		[LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Tizen BlazorWebView handler '{handlerKey}' disconnected from the NUI WebView.")]
		public static partial void TizenHandlerDisconnected(this ILogger logger, string handlerKey);

		[LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Tizen BlazorWebView started with content root '{contentRootDir}' and host page '{hostPageRelativePath}'.")]
		public static partial void TizenWebViewStarted(this ILogger logger, string contentRootDir, string hostPageRelativePath);
	}
}
