// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// A loaded Tizen image, addressed by the NUI resource URL that view properties consume.
	/// </summary>
	/// <remarks>
	/// Named <c>TizenImageSource</c> rather than reusing the imported <c>MauiImageSource</c> so
	/// Wave A's copy is unambiguous while the raw imported partials still exist.
	/// </remarks>
	public class TizenImageSource : IDisposable
	{
		bool _disposed;
		global::Tizen.NUI.ImageUrl? _imageUrl;

		/// <summary>The NUI resource URL, or <see langword="null"/> if nothing is loaded.</summary>
		public string? ResourceUrl { get; set; }

		/// <summary>
		/// Decodes <paramref name="stream"/> into a NUI image buffer and publishes its URL.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Decoding is pushed onto the thread pool because <c>EncodedImageBuffer</c> decodes
		/// synchronously and would otherwise block the NUI main loop.
		/// </para>
		/// <para>
		/// Only the decode is off-thread. <c>GenerateUrl</c> registers the buffer with NUI and so
		/// must run on the main loop; it is deliberately outside the <see cref="Task.Run"/>, and
		/// the caller is responsible for having invoked this from the UI thread. The previous
		/// arrangement awaited with <c>ConfigureAwait(false)</c> and then called
		/// <c>GenerateUrl</c> on the continuation - i.e. on a thread-pool thread.
		/// </para>
		/// </remarks>
		public async Task LoadSource(Stream stream)
		{
			ArgumentNullException.ThrowIfNull(stream);

			// Decode off the main loop, then register on it.
			var imageBuffer = await Task.Run(() => new global::Tizen.NUI.EncodedImageBuffer(stream));

			try
			{
				_imageUrl = imageBuffer.GenerateUrl();
				ResourceUrl = _imageUrl?.ToString();
			}
			finally
			{
				imageBuffer.Dispose();
			}
		}

		/// <summary>
		/// Resolves a URI through NUI and publishes it only after the platform reports a successful
		/// resource load.
		/// </summary>
		internal async Task<bool> LoadUrlAsync(string url, CancellationToken cancellationToken)
		{
			ArgumentException.ThrowIfNullOrEmpty(url);
			cancellationToken.ThrowIfCancellationRequested();

			using var imageView = new global::Tizen.NUI.BaseComponents.ImageView();
			var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			void OnResourceReady(
				object? sender,
				global::Tizen.NUI.BaseComponents.ImageView.ResourceReadyEventArgs args) =>
				completion.TrySetResult(
					imageView.LoadingStatus ==
					global::Tizen.NUI.BaseComponents.ImageView.LoadingStatusType.Ready);

			imageView.ResourceReady += OnResourceReady;
			using var registration = cancellationToken.Register(
				static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
				completion);

			try
			{
				imageView.ResourceUrl = url;
				var ready = await completion.Task;
				cancellationToken.ThrowIfCancellationRequested();

				if (ready)
					ResourceUrl = url;

				return ready;
			}
			finally
			{
				imageView.ResourceReady -= OnResourceReady;
			}
		}

		protected virtual void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			if (disposing)
				_imageUrl?.Dispose();

			_disposed = true;
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}
	}
}
