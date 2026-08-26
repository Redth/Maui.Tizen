// Ported from dotnet/maui (net11.0) src/BlazorWebView/src/Maui/Tizen/TizenWebViewManager.cs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.FileProviders;
using NWebView = Tizen.NUI.BaseComponents.WebView;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView
{
	/// <summary>
	/// An implementation of <see cref="WebViewManager"/> that uses the Tizen NUI WebView control
	/// to render Blazor content.
	/// </summary>
	public class TizenWebViewManager : WebViewManager
	{
		internal const string AppOrigin = "http://0.0.0.0/";

		private readonly TizenBlazorWebViewHandler _handler;
		private readonly NWebView _webview;
		private readonly string _contentRootRelativeToAppRoot;

		/// <summary>
		/// Initializes a new instance of <see cref="TizenWebViewManager"/>.
		/// </summary>
		/// <param name="handler">The owning <see cref="TizenBlazorWebViewHandler"/>.</param>
		/// <param name="webview">The Tizen NUI WebView the content is rendered into.</param>
		/// <param name="provider">The <see cref="IServiceProvider"/> for the application.</param>
		/// <param name="dispatcher">A dispatcher that marshals calls to the Tizen UI thread.</param>
		/// <param name="fileProvider">Provides static content to the WebView.</param>
		/// <param name="jsComponents">Configuration for adding, removing and updating root components from JavaScript.</param>
		/// <param name="contentRootRelativeToAppRoot">Path to the directory containing application content files.</param>
		/// <param name="hostPageRelativePath">Path to the host page within <paramref name="fileProvider"/>.</param>
		public TizenWebViewManager(
			TizenBlazorWebViewHandler handler,
			NWebView webview,
			IServiceProvider provider,
			Dispatcher dispatcher,
			IFileProvider fileProvider,
			JSComponentConfigurationStore jsComponents,
			string contentRootRelativeToAppRoot,
			string hostPageRelativePath)
			: base(provider, dispatcher, new Uri(AppOrigin), fileProvider, jsComponents, hostPageRelativePath)
		{
			_handler = handler ?? throw new ArgumentNullException(nameof(handler));
			_webview = webview ?? throw new ArgumentNullException(nameof(webview));
			_contentRootRelativeToAppRoot = contentRootRelativeToAppRoot;
		}

		/// <summary>
		/// Gets the content root of this manager, relative to the application root.
		/// </summary>
		public string ContentRootRelativeToAppRoot => _contentRootRelativeToAppRoot;

		internal bool TryGetResponseContentInternal(
			string uri,
			bool allowFallbackOnHostPage,
			out int statusCode,
			out string statusMessage,
			out Stream content,
			out IDictionary<string, string> headers)
			=> TryGetResponseContent(uri, allowFallbackOnHostPage, out statusCode, out statusMessage, out content, out headers);

		/// <inheritdoc />
		protected override void NavigateCore(Uri absoluteUri)
		{
			_webview.LoadUrl(absoluteUri.ToString());
		}

		/// <inheritdoc />
		protected override void SendMessage(string message)
		{
			var messageJSStringLiteral = JavaScriptEncoder.Default.Encode(message);
			_webview.EvaluateJavaScript($"__dispatchMessageCallback(\"{messageJSStringLiteral}\")");
		}

		internal void MessageReceivedInternal(Uri uri, string message) => MessageReceived(uri, message);
	}
}
