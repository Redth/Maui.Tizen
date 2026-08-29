using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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
	/// <para>
	/// The in-box dotnet/maui Tizen backend left screenshot unimplemented (every member threw).
	/// This performs a real offscreen capture of the NUI window or view, then encodes the resulting
	/// pixel buffer with <c>Tizen.Multimedia.Util</c>.
	/// </para>
	/// <para>
	/// <c>IPlatformScreenshot</c> is intentionally not implemented: in the neutral (non platform
	/// specific) <c>Microsoft.Maui.Essentials</c> assembly that interface declares no members, so it
	/// carries no Tizen-typed contract. The Tizen-typed overloads live on
	/// <see cref="TizenScreenshotExtensions"/> instead, and element-level capture flows through the
	/// neutral <see cref="IViewScreenshot"/> contract.
	/// </para>
	/// </remarks>
	public sealed class TizenScreenshot : IScreenshot, IViewScreenshot, IDisposable
	{
		readonly ITizenScreenshotDispatcher _dispatcher;
		readonly ITizenScreenshotCaptureFactory _factory;
		readonly CancellationTokenSource _disposeCancellation = new();
		int _disposed;

		/// <summary>
		/// How long to wait for <c>Capture.Finished</c> before giving up.
		/// </summary>
		/// <remarks>
		/// <c>Capture.Finished</c> is the only signal that a capture completed. If the native side
		/// never raises it - a destroyed window, a driver failure, a zero-sized surface accepted by
		/// Tizen but never rendered - an un-timed wait would hang the caller forever and leak the
		/// <see cref="Capture"/> handle and its scratch file. Failing loudly after a bounded wait is
		/// strictly better than an unkillable task.
		/// </remarks>
		public static TimeSpan CaptureTimeout { get; set; } = TimeSpan.FromSeconds(10);

		/// <summary>Creates a screenshot service backed by NUI Capture.</summary>
		public TizenScreenshot()
			: this(TizenScreenshotDispatcher.Instance, TizenScreenshotCaptureFactory.Instance)
		{
		}

		internal TizenScreenshot(
			ITizenScreenshotDispatcher dispatcher,
			ITizenScreenshotCaptureFactory factory)
		{
			_dispatcher = dispatcher;
			_factory = factory;
		}

		/// <inheritdoc/>
		public bool IsCaptureSupported => true;

		/// <inheritdoc/>
		public async Task<IScreenshotResult> CaptureAsync() =>
			await CaptureCoreAsync(
				_factory.CreateDefaultWindowCapture,
				CancellationToken.None).ConfigureAwait(false)
			?? throw new InvalidOperationException("Tizen reported a failed window capture.");

		/// <summary>
		/// Captures a screenshot of the supplied NUI window.
		/// </summary>
		/// <param name="window">The window to capture.</param>
		/// <returns>The captured screenshot.</returns>
		public async Task<IScreenshotResult> CaptureAsync(TizenWindow window)
		{
			ArgumentNullException.ThrowIfNull(window);

			return await CaptureCoreAsync(
				() => _factory.CreateWindowCapture(window),
				CancellationToken.None).ConfigureAwait(false)
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

			return CaptureCoreAsync(
				() => _factory.CreateViewCapture(view),
				CancellationToken.None);
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

		internal async Task<IScreenshotResult?> CaptureCoreAsync(
			Func<ITizenScreenshotCaptureSession> createSession,
			CancellationToken cancellationToken,
			TimeSpan? timeoutOverride = null)
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
			var terminal = new TizenScreenshotTerminalCoordinator();
			ITizenScreenshotCaptureSession? session = null;
			void OnFinished(bool success) =>
				terminal.TryComplete(
					success
						? TizenScreenshotTerminal.NativeSucceeded
						: TizenScreenshotTerminal.NativeFailed);

			var effectiveTimeout = timeoutOverride ?? CaptureTimeout;
			using var timeout = new CancellationTokenSource(effectiveTimeout);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_disposeCancellation.Token,
				timeout.Token);

			using var registration = linked.Token.Register(() =>
			{
				if (_disposeCancellation.IsCancellationRequested)
					terminal.TryComplete(TizenScreenshotTerminal.Disposed);
				else if (cancellationToken.IsCancellationRequested)
					terminal.TryComplete(TizenScreenshotTerminal.Canceled);
				else
					terminal.TryComplete(TizenScreenshotTerminal.TimedOut);
			});

			try
			{
				await _dispatcher.InvokeAsync(() =>
				{
					if (!terminal.IsPending)
						return;

					session = createSession();
					session.Finished += OnFinished;
					if (terminal.IsPending)
						session.Start();
				}).ConfigureAwait(false);

				var outcome = await terminal.Completion.ConfigureAwait(false);
				switch (outcome)
				{
					case TizenScreenshotTerminal.NativeFailed:
						return null;
					case TizenScreenshotTerminal.Canceled:
						throw new OperationCanceledException(cancellationToken);
					case TizenScreenshotTerminal.Disposed:
						throw new ObjectDisposedException(nameof(TizenScreenshot));
					case TizenScreenshotTerminal.TimedOut:
						throw new TimeoutException(
							$"Tizen did not raise Capture.Finished within {effectiveTimeout}.");
					case TizenScreenshotTerminal.NativeSucceeded:
						break;
					default:
						throw new InvalidOperationException($"Unexpected screenshot terminal state '{outcome}'.");
				}

				// Native success owns the one terminal slot. A timeout/cancellation/disposal callback
				// racing while the dispatcher copies the buffer cannot replace that outcome.
				timeout.CancelAfter(Timeout.InfiniteTimeSpan);
				var buffer = await _dispatcher.InvokeAsync(() =>
				{
					return session!.CopyBuffer();
				}).ConfigureAwait(false);

				// Once the managed bytes are copied, encoding and result construction no longer
				// touch NUI and can continue off the dispatcher.
				return buffer is null
					? null
					: await Task.Run(
						() => TizenScreenshotResult.FromCapturedBuffer(buffer),
						CancellationToken.None).ConfigureAwait(false);
			}

			finally
			{
				if (session is not null)
				{
					try
					{
						await _dispatcher.InvokeAsync(() =>
						{
							session.Finished -= OnFinished;
							session.Dispose();
						}).ConfigureAwait(false);
					}
					catch (Exception) when (linked.IsCancellationRequested)
					{
						// Cancellation/disposal has already settled the caller. Native cleanup is
						// best effort when the app's dispatcher itself is shutting down.
					}
				}
			}
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
				_disposeCancellation.Cancel();
		}
	}

	internal enum TizenScreenshotTerminal
	{
		Pending,
		NativeSucceeded,
		NativeFailed,
		TimedOut,
		Canceled,
		Disposed,
	}

	internal sealed class TizenScreenshotTerminalCoordinator
	{
		readonly TaskCompletionSource<TizenScreenshotTerminal> _completion =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		int _terminal;

		public bool IsPending => Volatile.Read(ref _terminal) == 0;

		public Task<TizenScreenshotTerminal> Completion => _completion.Task;

		public bool TryComplete(TizenScreenshotTerminal terminal)
		{
			if (terminal == TizenScreenshotTerminal.Pending)
				throw new ArgumentOutOfRangeException(nameof(terminal));
			if (Interlocked.CompareExchange(ref _terminal, (int)terminal, 0) != 0)
				return false;

			_completion.TrySetResult(terminal);
			return true;
		}
	}

	internal sealed record TizenCapturedPixels(
		byte[] Pixels,
		int Width,
		int Height,
		TizenColorSpace ColorSpace);

	internal sealed record TizenCapturedBuffer(
		byte[] Buffer,
		int Width,
		int Height,
		int Stride,
		PixelFormat Format);

	internal interface ITizenScreenshotDispatcher
	{
		Task InvokeAsync(Action action);

		Task<T> InvokeAsync<T>(Func<T> action);
	}

	internal interface ITizenScreenshotCaptureFactory
	{
		ITizenScreenshotCaptureSession CreateDefaultWindowCapture();

		ITizenScreenshotCaptureSession CreateWindowCapture(TizenWindow window);

		ITizenScreenshotCaptureSession CreateViewCapture(TizenView view);
	}

	internal interface ITizenScreenshotCaptureSession : IDisposable
	{
		event Action<bool>? Finished;

		void Start();

		TizenCapturedBuffer? CopyBuffer();
	}

	sealed class TizenScreenshotDispatcher : ITizenScreenshotDispatcher
	{
		public static TizenScreenshotDispatcher Instance { get; } = new();

		public Task InvokeAsync(Action action) =>
			Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(action);

		public Task<T> InvokeAsync<T>(Func<T> action) =>
			Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(action);
	}

	sealed class TizenScreenshotCaptureFactory : ITizenScreenshotCaptureFactory
	{
		public static TizenScreenshotCaptureFactory Instance { get; } = new();

		public ITizenScreenshotCaptureSession CreateDefaultWindowCapture() =>
			CreateWindowCapture(TizenWindow.Default);

		public ITizenScreenshotCaptureSession CreateWindowCapture(TizenWindow window)
		{
			using var size = window.WindowSize;
			return new TizenScreenshotCaptureSession(
				window.GetDefaultLayer(),
				size.Width,
				size.Height);
		}

		public ITizenScreenshotCaptureSession CreateViewCapture(TizenView view)
		{
			using var size = view.Size2D;
			return new TizenScreenshotCaptureSession(view, size.Width, size.Height);
		}
	}

	sealed class TizenScreenshotCaptureSession : ITizenScreenshotCaptureSession
	{
		readonly Container _source;
		readonly int _width;
		readonly int _height;
		readonly string _scratchPath;
		readonly Capture _capture = new();

		public TizenScreenshotCaptureSession(Container source, int width, int height)
		{
			if (width <= 0 || height <= 0)
				throw new InvalidOperationException("Cannot capture a screenshot of a zero-sized NUI container.");

			_source = source;
			_width = width;
			_height = height;
			_scratchPath = System.IO.Path.Combine(
				global::Tizen.Applications.Application.Current.DirectoryInfo.Cache,
				$"maui-screenshot-{Guid.NewGuid():N}.png");
			_capture.Finished += OnFinished;
		}

		public event Action<bool>? Finished;

		public void Start()
		{
			using var size = new TizenNUISize(_width, _height, 0);
			using var background = Color.Transparent;
			_capture.Start(_source, size, _scratchPath, background);
		}

		public TizenCapturedBuffer? CopyBuffer()
		{
			using var buffer = _capture.GetCapturedBuffer();
			if (buffer is null)
				return null;

			var width = checked((int)buffer.GetWidth());
			var height = checked((int)buffer.GetHeight());
			var format = buffer.GetPixelFormat();
			var bytesPerPixel = TizenScreenshotResult.BytesPerPixel(format);
			var rowBytes = checked(width * bytesPerPixel);
#if TIZEN_API15
			var stride = checked((int)buffer.GetStrideBytes());
#else
			// The host verification package predates API15. Product and API15 lanes compile the
			// branch above; strict coordinator tests provide padded rows without invoking NUI.
			var stride = rowBytes;
#endif
			if (stride == 0)
				stride = rowBytes;
			if (stride < rowBytes)
				throw new InvalidOperationException(
					$"Tizen returned stride {stride} for a {rowBytes}-byte row.");

			var native = buffer.GetBuffer();
			if (native == IntPtr.Zero)
				throw new InvalidOperationException("Tizen returned an empty capture buffer.");

			var padded = new byte[checked(stride * height)];
			Marshal.Copy(native, padded, 0, padded.Length);

			return new TizenCapturedBuffer(
				padded,
				width,
				height,
				stride,
				format);
		}

		public void Dispose()
		{
			_capture.Finished -= OnFinished;
			_capture.Dispose();

			try
			{
				if (File.Exists(_scratchPath))
					File.Delete(_scratchPath);
			}
			catch (Exception)
			{
				// Cache cleanup must not hide an otherwise successful capture.
			}
		}

		void OnFinished(object? sender, CaptureFinishedEventArgs e) =>
			Finished?.Invoke(e.Success);
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

		internal static TizenScreenshotResult FromCapturedPixels(TizenCapturedPixels pixels) =>
			new(
				pixels.Pixels,
				pixels.Width,
				pixels.Height,
				pixels.ColorSpace);

		internal static TizenScreenshotResult FromCapturedBuffer(TizenCapturedBuffer buffer) =>
			FromCapturedPixels(
				CopyRows(
					buffer.Buffer,
					buffer.Width,
					buffer.Height,
					buffer.Stride,
					buffer.Format));

		internal static TizenCapturedPixels CopyRows(
			byte[] padded,
			int width,
			int height,
			int stride,
			PixelFormat format)
		{
			var colorSpace = MapColorSpace(format);
			var rowBytes = checked(width * BytesPerPixel(format));
			if (stride == 0)
				stride = rowBytes;
			if (stride < rowBytes)
				throw new ArgumentOutOfRangeException(nameof(stride));
			if (padded.Length < checked(stride * height))
				throw new ArgumentException("The source buffer is shorter than its declared stride.", nameof(padded));

			var pixels = new byte[checked(rowBytes * height)];
			for (var row = 0; row < height; row++)
			{
				Buffer.BlockCopy(
					padded,
					row * stride,
					pixels,
					row * rowBytes,
					rowBytes);
			}

			// RGBX has no Tizen colour space, so the padding byte is promoted to an opaque alpha and
			// the buffer encoded as RGBA. Encoding it as RGBA without this step would treat whatever
			// the driver left in the padding byte as transparency.
			if (format == PixelFormat.RGB8888)
				MakeOpaque(pixels);

			return new TizenCapturedPixels(pixels, width, height, colorSpace);
		}

		internal static void MakeOpaque(byte[] pixels)
		{
			for (var i = 3; i < pixels.Length; i += 4)
				pixels[i] = 0xFF;
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

		/// <summary>
		/// Maps a NUI pixel format onto the Tizen colour space used to encode it.
		/// </summary>
		/// <remarks>
		/// The X in RGBX/BGRX is padding, not alpha. BGRX has an exact Tizen counterpart
		/// (<see cref="TizenColorSpace.Bgrx8888"/>); RGBX does not, so it is converted to opaque
		/// RGBA by <see cref="MakeOpaque"/> before encoding.
		/// </remarks>
		internal static TizenColorSpace MapColorSpace(PixelFormat format) =>
			format switch
			{
				PixelFormat.RGBA8888 => TizenColorSpace.Rgba8888,
				PixelFormat.BGRA8888 => TizenColorSpace.Bgra8888,
				PixelFormat.BGR8888 => TizenColorSpace.Bgrx8888,
				PixelFormat.RGB8888 => TizenColorSpace.Rgba8888,
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
