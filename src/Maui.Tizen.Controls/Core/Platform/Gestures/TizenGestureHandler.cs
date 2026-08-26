using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Translates the events of one native Tizen detector into .NET MAUI gesture events for one
	/// <see cref="IGestureRecognizer"/>.
	/// </summary>
	/// <remarks>
	/// This is the port of the NUI <c>GestureHandler</c> from dotnet/maui. The two behavioural
	/// changes are that the native view is resolved through the standard
	/// <see cref="IViewHandler.ContainerView"/> / <see cref="IViewHandler.PlatformView"/> pair
	/// instead of the Tizen-only <c>IPlatformViewHandler</c> shape, and that the native detector
	/// is supplied through <see cref="ITizenNativeGestureDetector"/> rather than constructed
	/// in-place, which is what makes the translation logic testable off device.
	/// </remarks>
	public abstract class TizenGestureHandler : IDisposable
	{
		readonly ITizenNativeGestureDetector _detector;

		IViewHandler? _handler;
		object? _attachedView;
		bool _disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenGestureHandler"/> class.
		/// </summary>
		/// <param name="recognizer">The recognizer this handler feeds.</param>
		/// <param name="detector">The native detector to observe.</param>
		/// <param name="dispatcher">Raises the translated gesture events.</param>
		/// <param name="scaler">Converts native device pixels into device-independent units.</param>
		protected TizenGestureHandler(
			IGestureRecognizer recognizer,
			ITizenNativeGestureDetector detector,
			ITizenGestureDispatcher dispatcher,
			ITizenPixelScaler scaler)
		{
			Recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
			_detector = detector ?? throw new ArgumentNullException(nameof(detector));
			Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
			Scaler = scaler ?? throw new ArgumentNullException(nameof(scaler));

			_detector.Detected += OnDetected;
		}

		/// <summary>Gets the recognizer this handler feeds.</summary>
		public IGestureRecognizer Recognizer { get; }

		/// <summary>Gets the native detector being observed.</summary>
		public ITizenNativeGestureDetector Detector => _detector;

		/// <summary>Gets the dispatcher used to raise .NET MAUI gesture events.</summary>
		protected ITizenGestureDispatcher Dispatcher { get; }

		/// <summary>Gets the pixel-to-DP converter.</summary>
		protected ITizenPixelScaler Scaler { get; }

		/// <summary>Gets the virtual view the gestures are reported against.</summary>
		protected View? View => _handler?.VirtualView as View;

		/// <summary>
		/// Attaches the native detector to the platform view owned by <paramref name="handler"/>.
		/// </summary>
		/// <param name="handler">The handler whose platform view should be observed.</param>
		/// <remarks>
		/// The container view is preferred when the handler has one, because that is the view that
		/// covers the whole element including any decoration the container adds. Attaching when the
		/// handler has no platform view yet is a safe no-op; the detector is attached on the next
		/// call once the platform view exists.
		/// </remarks>
		public void Attach(IViewHandler handler)
		{
			ArgumentNullException.ThrowIfNull(handler);
			ObjectDisposedException.ThrowIf(_disposed, this);

			_handler = handler;

			var platformView = handler.ContainerView ?? handler.PlatformView;

			if (platformView is null)
			{
				return;
			}

			if (_detector.IsAttached && ReferenceEquals(_attachedView, platformView))
			{
				return;
			}

			if (_detector.IsAttached)
			{
				_detector.Detach();
			}

			_detector.Attach(platformView);
			_attachedView = platformView;
		}

		/// <summary>
		/// Detaches the native detector from the platform view.
		/// </summary>
		/// <remarks>Detaching when not attached is a safe no-op.</remarks>
		public void Detach()
		{
			if (!_detector.IsAttached)
			{
				return;
			}

			_detector.Detach();
			_attachedView = null;
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Translates a native gesture event into .NET MAUI gesture events.
		/// </summary>
		/// <param name="view">The view the gesture occurred on.</param>
		/// <param name="args">The native gesture event.</param>
		protected abstract void OnGestureDetected(View view, TizenGestureEventArgs args);

		/// <summary>
		/// Releases the handler and unsubscribes from the native detector.
		/// </summary>
		/// <param name="disposing">Whether managed state should be released.</param>
		protected virtual void Dispose(bool disposing)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;

			if (!disposing)
			{
				return;
			}

			_detector.Detected -= OnDetected;
			Detach();
			_detector.Dispose();
			_handler = null;
		}

		/// <summary>
		/// Converts a device-pixel point into device-independent units.
		/// </summary>
		/// <param name="point">The point in device pixels.</param>
		protected Point ToScaledDp(Point point) =>
			new(Scaler.ToScaledDp(point.X), Scaler.ToScaledDp(point.Y));

		/// <summary>
		/// Converts the native event's local and screen positions into device-independent units.
		/// </summary>
		/// <param name="args">The native gesture event.</param>
		/// <remarks>
		/// The screen position stays <see langword="null"/> when the native event did not report
		/// one, so a missing screen coordinate is surfaced as "unknown" rather than being faked
		/// from the view-local value.
		/// </remarks>
		protected TizenGesturePosition ToScaledDp(TizenGestureEventArgs args) =>
			new(
				ToScaledDp(args.LocalPosition),
				args.ScreenPosition is { } screen ? ToScaledDp(screen) : null);

		void OnDetected(object? sender, TizenGestureEventArgs args)
		{
			if (_disposed)
			{
				return;
			}

			var view = View;

			if (view is null)
			{
				return;
			}

			OnGestureDetected(view, args);
		}
	}
}
