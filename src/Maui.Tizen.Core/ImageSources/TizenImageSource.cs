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

			using var target = new NuiImageReadinessTarget();
			var ready = await TizenImageReadinessCoordinator
				.WaitAsync(target, url, immediate: true, cancellationToken);

			if (ready)
				ResourceUrl = url;

			return ready;
		}

		sealed class NuiImageReadinessTarget : ITizenImageReadinessTarget, IDisposable
		{
			readonly global::Tizen.NUI.BaseComponents.ImageView _imageView = new();

			public event EventHandler? ResourceReady;

			public bool IsReady =>
				_imageView.LoadingStatus ==
				global::Tizen.NUI.BaseComponents.ImageView.LoadingStatusType.Ready;

			public NuiImageReadinessTarget() =>
				_imageView.ResourceReady += OnResourceReady;

			public void Start(string url, bool immediate)
			{
				if (immediate)
					_imageView.LoadPolicy = global::Tizen.NUI.LoadPolicyType.Immediate;
				_imageView.ResourceUrl = url;
			}

			void OnResourceReady(
				object? sender,
				global::Tizen.NUI.BaseComponents.ImageView.ResourceReadyEventArgs args) =>
				ResourceReady?.Invoke(this, EventArgs.Empty);

			public void Dispose()
			{
				_imageView.ResourceReady -= OnResourceReady;
				_imageView.Dispose();
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
