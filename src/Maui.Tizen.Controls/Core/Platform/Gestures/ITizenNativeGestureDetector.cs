using System;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// A native Tizen gesture detector that can be attached to and detached from a platform view.
	/// </summary>
	/// <remarks>
	/// The default implementation wraps a <c>Tizen.NUI.GestureDetector</c>. Declaring the contract
	/// here keeps the gesture translation logic independent of NUI, which is what allows it to be
	/// exercised by host-side tests.
	/// </remarks>
	public interface ITizenNativeGestureDetector : IDisposable
	{
		/// <summary>
		/// Raised whenever the native detector reports gesture activity.
		/// </summary>
		event EventHandler<TizenGestureEventArgs>? Detected;

		/// <summary>
		/// Gets a value indicating whether the detector is currently attached to a platform view.
		/// </summary>
		bool IsAttached { get; }

		/// <summary>
		/// Attaches the detector to <paramref name="platformView"/>.
		/// </summary>
		/// <param name="platformView">
		/// The native view to observe. This is the handler's container view when it has one,
		/// otherwise its platform view.
		/// </param>
		/// <remarks>Attaching an already-attached detector must be a safe no-op.</remarks>
		void Attach(object platformView);

		/// <summary>
		/// Detaches the detector from the view it was attached to.
		/// </summary>
		/// <remarks>Detaching a detector that is not attached must be a safe no-op.</remarks>
		void Detach();
	}

	/// <summary>
	/// Creates the native detectors used to observe gestures on a platform view.
	/// </summary>
	/// <remarks>
	/// This is the single seam through which the gesture infrastructure reaches NUI. Registering a
	/// different implementation replaces the entire native detection layer without touching the
	/// recognizer translation logic.
	/// </remarks>
	public interface ITizenNativeGestureDetectorFactory
	{
		/// <summary>
		/// Creates a native detector for <paramref name="recognizer"/>, or <see langword="null"/>
		/// when the gesture is not supported on the current Tizen profile.
		/// </summary>
		/// <param name="kind">The kind of gesture the detector should report.</param>
		/// <param name="recognizer">The recognizer the detector will feed.</param>
		ITizenNativeGestureDetector? CreateDetector(TizenGestureKind kind, IGestureRecognizer recognizer);
	}

	internal sealed class UnsupportedTizenNativeGestureDetectorFactory : ITizenNativeGestureDetectorFactory
	{
		public ITizenNativeGestureDetector? CreateDetector(TizenGestureKind kind, IGestureRecognizer recognizer)
		{
			ArgumentNullException.ThrowIfNull(recognizer);
			return null;
		}
	}

	/// <summary>
	/// Converts Tizen device pixels into device-independent units.
	/// </summary>
	/// <remarks>
	/// Native gesture coordinates are reported in device pixels, but .NET MAUI gesture events are
	/// expressed in device-independent units. The original NUI backend used the
	/// <c>ToScaledDP</c> helper for this; declaring it as a contract keeps the conversion testable
	/// and lets the scaling factor come from the real display on device.
	/// </remarks>
	public interface ITizenPixelScaler
	{
		/// <summary>Converts a device-pixel value into device-independent units.</summary>
		/// <param name="pixels">The value in device pixels.</param>
		double ToScaledDp(double pixels);
	}

	/// <summary>
	/// An <see cref="ITizenPixelScaler"/> backed by a fixed scaling factor.
	/// </summary>
	public sealed class TizenPixelScaler : ITizenPixelScaler
	{
		readonly double _scalingFactor;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenPixelScaler"/> class.
		/// </summary>
		/// <param name="scalingFactor">
		/// The number of device pixels per device-independent unit. Must be greater than zero.
		/// </param>
		public TizenPixelScaler(double scalingFactor = 1d)
		{
			if (scalingFactor <= 0d || double.IsNaN(scalingFactor) || double.IsInfinity(scalingFactor))
			{
				throw new ArgumentOutOfRangeException(nameof(scalingFactor), scalingFactor, "The scaling factor must be a positive, finite number.");
			}

			_scalingFactor = scalingFactor;
		}

		/// <inheritdoc/>
		public double ToScaledDp(double pixels) => pixels / _scalingFactor;
	}
}
