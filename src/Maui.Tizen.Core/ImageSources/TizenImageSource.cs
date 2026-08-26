// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using System;
using System.IO;
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
		/// Decoding is pushed onto the thread pool because <c>EncodedImageBuffer</c> decodes
		/// synchronously and would otherwise block the NUI main loop.
		/// </remarks>
		public async Task LoadSource(Stream stream)
		{
			ArgumentNullException.ThrowIfNull(stream);

			global::Tizen.NUI.EncodedImageBuffer? imageBuffer = null;
			await Task.Run(() => imageBuffer = new global::Tizen.NUI.EncodedImageBuffer(stream)).ConfigureAwait(false);

			_imageUrl = imageBuffer?.GenerateUrl();
			ResourceUrl = _imageUrl?.ToString();
			imageBuffer?.Dispose();
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
