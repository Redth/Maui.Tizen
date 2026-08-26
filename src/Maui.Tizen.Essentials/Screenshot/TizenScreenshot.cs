using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Maui.Media;
using Tizen.NUI;
using TizenColorSpace = Tizen.Multimedia.ColorSpace;
using TizenJpegEncoder = Tizen.Multimedia.Util.JpegEncoder;
using TizenMultimediaSize = Tizen.Multimedia.Size;
using TizenNUISize = Tizen.NUI.Size;
using TizenPngEncoder = Tizen.Multimedia.Util.PngEncoder;
using TizenView = Tizen.NUI.BaseComponents.View;
using TizenWindow = Tizen.NUI.Window;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IScreenshot"/> and <see cref="IViewScreenshot"/>,
	/// backed by <c>Tizen.NUI.Capture</c>.
	/// </summary>
	/// <remarks>
	/// The in-box dotnet/maui Tizen backend left screenshot unimplemented (every member threw).
	/// This implementation performs a real offscreen capture of the NUI window or view, then encodes
	/// the resulting pixel buffer with <c>Tizen.Multimedia.Util</c>.
	/// <para>
	/// <c>IPlatformScreenshot</c> is intentionally not implemented: in the neutral (non platform
	/// specific) <c>Microsoft.Maui.Essentials</c> assembly that interface declares no members, so it
	/// carries no Tizen-typed contract. The Tizen-typed overloads live on
	/// <see cref="TizenScreenshotExtensions"/> instead, and element-level capture flows through the
	/// neutral <see cref="IViewScreenshot"/> contract.
	/// </para>
	/// </remarks>
	public sealed class TizenScreenshot : IScreenshot, IViewScreenshot
	{
		/// <inheritdoc/>
		public bool IsCaptureSupported => true;

		/// <inheritdoc/>
		public Task<IScreenshotResult> CaptureAsync() =>
			CaptureAsync(TizenWindow.Instance);

		/// <summary>
		/// Captures a screenshot of the supplied NUI window.
		/// </summary>
		/// <param name="window">The window to capture.</param>
		/// <returns>The captured screenshot.</returns>
		public async Task<IScreenshotResult> CaptureAsync(TizenWindow window)
		{
			ArgumentNullException.ThrowIfNull(window);

			var size = window.WindowSize;
			return await CaptureCoreAsync(window.GetDefaultLayer(), new TizenNUISize(size.Width, size.Height, 0)).ConfigureAwait(false)
				?? throw new InvalidOperationException("Tizen reported a failed window capture.");
		}

		/// <summary>
		/// Captures a screenshot of the supplied NUI view.
		/// </summary>
		/// <param name="view">The view to capture.</param>
		/// <returns>The captured screenshot, or <see langword="null"/> when Tizen reported a failed capture.</returns>
		public Task<IScreenshotResult?> CaptureAsync(TizenView view)
		{
			ArgumentNullException.ThrowIfNull(view);

			var size = view.Size2D;
			return CaptureCoreAsync(view, new TizenNUISize(size.Width, size.Height, 0));
		}

		/// <inheritdoc/>
		public Task<IScreenshotResult?> CaptureViewAsync(object platformView) =>
			platformView switch
			{
				TizenView view => CaptureAsync(view),
				TizenWindow window => CaptureWindowAsync(window),
				_ => Task.FromResult<IScreenshotResult?>(null),
			};

		async Task<IScreenshotResult?> CaptureWindowAsync(TizenWindow window) =>
			await CaptureAsync(window).ConfigureAwait(false);

		static async Task<IScreenshotResult?> CaptureCoreAsync(Container source, TizenNUISize size)
		{
			if (size.Width <= 0 || size.Height <= 0)
				throw new InvalidOperationException("Cannot capture a screenshot of a zero-sized NUI container.");

			var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			// Capture.Start requires a writable destination path even when the pixel buffer is what
			// we actually consume, so a cache-scoped scratch file is used and removed afterwards.
			var scratchPath = System.IO.Path.Combine(
				global::Tizen.Applications.Application.Current.DirectoryInfo.Cache,
				$"maui-screenshot-{Guid.NewGuid():N}.png");

			var capture = new Capture();

			void OnFinished(object? sender, CaptureFinishedEventArgs e) => tcs.TrySetResult(e.Success);

			capture.Finished += OnFinished;

			try
			{
				capture.Start(source, size, scratchPath, Color.Transparent);

				if (!await tcs.Task.ConfigureAwait(false))
					return null;

				var buffer = capture.GetCapturedBuffer();
				if (buffer is null)
					return null;

				return TizenScreenshotResult.FromPixelBuffer(buffer);
			}
			finally
			{
				capture.Finished -= OnFinished;
				capture.Dispose();

				try
				{
					if (File.Exists(scratchPath))
						File.Delete(scratchPath);
				}
				catch
				{
					// A leftover scratch file in the cache directory is not worth failing a capture.
				}
			}
		}
	}

	/// <summary>
	/// A captured Tizen screenshot, encoded on demand.
	/// </summary>
	public sealed class TizenScreenshotResult : IScreenshotResult
	{
		readonly byte[] _pixels;
		readonly TizenColorSpace _colorSpace;

		TizenScreenshotResult(byte[] pixels, int width, int height, TizenColorSpace colorSpace)
		{
			_pixels = pixels;
			_colorSpace = colorSpace;
			Width = width;
			Height = height;
		}

		/// <inheritdoc/>
		public int Width { get; }

		/// <inheritdoc/>
		public int Height { get; }

		internal static TizenScreenshotResult FromPixelBuffer(PixelBuffer buffer)
		{
			var width = (int)buffer.GetWidth();
			var height = (int)buffer.GetHeight();
			var format = buffer.GetPixelFormat();
			var colorSpace = MapColorSpace(format);
			var stride = BytesPerPixel(format);

			var pixels = new byte[width * height * stride];
			var native = buffer.GetBuffer();

			if (native == IntPtr.Zero)
				throw new InvalidOperationException("Tizen returned an empty capture buffer.");

			Marshal.Copy(native, pixels, 0, pixels.Length);

			return new TizenScreenshotResult(pixels, width, height, colorSpace);
		}

		/// <inheritdoc/>
		public async Task<Stream> OpenReadAsync(ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
		{
			var stream = new MemoryStream();
			await EncodeAsync(stream, format, quality).ConfigureAwait(false);
			stream.Position = 0;
			return stream;
		}

		/// <inheritdoc/>
		public Task CopyToAsync(Stream destination, ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
		{
			ArgumentNullException.ThrowIfNull(destination);

			return EncodeAsync(destination, format, quality);
		}

		async Task EncodeAsync(Stream destination, ScreenshotFormat format, int quality)
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
			ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);

			var resolution = new TizenMultimediaSize(Width, Height);

			switch (format)
			{
				case ScreenshotFormat.Jpeg:
					{
						using var encoder = new TizenJpegEncoder(quality);
						encoder.SetResolution(resolution);
						encoder.SetColorSpace(_colorSpace);
						await encoder.EncodeAsync(_pixels, destination).ConfigureAwait(false);
						break;
					}

				case ScreenshotFormat.Png:
					{
						using var encoder = new TizenPngEncoder();
						encoder.SetResolution(resolution);
						encoder.SetColorSpace(_colorSpace);
						await encoder.EncodeAsync(_pixels, destination).ConfigureAwait(false);
						break;
					}

				default:
					throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown screenshot format.");
			}
		}

		internal static TizenColorSpace MapColorSpace(PixelFormat format) =>
			format switch
			{
				PixelFormat.RGBA8888 or PixelFormat.RGB8888 => TizenColorSpace.Rgba8888,
				PixelFormat.BGRA8888 or PixelFormat.BGR8888 => TizenColorSpace.Bgra8888,
				PixelFormat.RGB888 => TizenColorSpace.Rgb888,
				PixelFormat.RGB565 => TizenColorSpace.Rgb565,
				_ => throw TizenEssentialsSupport.NotSupported(
					$"{nameof(IScreenshot)} capture in pixel format '{format}'",
					"Tizen.Multimedia.Util has no matching color space for this NUI pixel format."),
			};

		internal static int BytesPerPixel(PixelFormat format) =>
			format switch
			{
				PixelFormat.RGBA8888 or PixelFormat.RGB8888 or PixelFormat.BGRA8888 or PixelFormat.BGR8888 => 4,
				PixelFormat.RGB888 => 3,
				PixelFormat.RGB565 => 2,
				_ => throw TizenEssentialsSupport.NotSupported(
					$"{nameof(IScreenshot)} capture in pixel format '{format}'",
					"This NUI pixel format has no fixed byte stride understood by this backend."),
			};
	}

	/// <summary>
	/// Tizen-typed capture helpers for <see cref="IScreenshot"/>.
	/// </summary>
	/// <remarks>
	/// Mirrors the <c>ScreenshotExtensions.CaptureAsync(this IScreenshot, Tizen.NUI.Window)</c>
	/// overloads that dotnet/maui only compiled into its Tizen-specific Essentials assembly.
	/// </remarks>
	public static class TizenScreenshotExtensions
	{
		/// <summary>
		/// Captures a screenshot of the supplied NUI window.
		/// </summary>
		/// <param name="screenshot">The screenshot service.</param>
		/// <param name="window">The window to capture.</param>
		/// <returns>The captured screenshot.</returns>
		/// <exception cref="PlatformNotSupportedException">The service is not the Tizen implementation.</exception>
		public static Task<IScreenshotResult> CaptureAsync(this IScreenshot screenshot, TizenWindow window) =>
			AsTizen(screenshot).CaptureAsync(window);

		/// <summary>
		/// Captures a screenshot of the supplied NUI view.
		/// </summary>
		/// <param name="screenshot">The screenshot service.</param>
		/// <param name="view">The view to capture.</param>
		/// <returns>The captured screenshot, or <see langword="null"/> when Tizen reported a failed capture.</returns>
		/// <exception cref="PlatformNotSupportedException">The service is not the Tizen implementation.</exception>
		public static Task<IScreenshotResult?> CaptureAsync(this IScreenshot screenshot, TizenView view) =>
			AsTizen(screenshot).CaptureAsync(view);

		static TizenScreenshot AsTizen(IScreenshot screenshot)
		{
			ArgumentNullException.ThrowIfNull(screenshot);

			if (screenshot is not TizenScreenshot tizen)
			{
				throw new PlatformNotSupportedException(
					$"This implementation of {nameof(IScreenshot)} is not {nameof(TizenScreenshot)}. " +
					$"Use {nameof(IViewScreenshot)}.{nameof(IViewScreenshot.CaptureViewAsync)} for platform-neutral view capture.");
			}

			return tizen;
		}
	}
}
