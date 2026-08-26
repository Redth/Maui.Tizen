using System;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Raises .NET MAUI gesture events on the recognizers attached to a view.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the single seam between the Tizen gesture pipeline and .NET MAUI's gesture
	/// recognizers. It exists because .NET MAUI only exposes a subset of its gesture dispatch
	/// surface publicly: <see cref="IPanGestureController"/>, <see cref="IPinchGestureController"/>
	/// and <see cref="ISwipeGestureController"/> are public, but the members used to raise tap,
	/// long-press, pointer, drag and drop events are internal to
	/// <c>Microsoft.Maui.Controls</c>.
	/// </para>
	/// <para>
	/// An out-of-tree backend therefore cannot raise those gestures without private reflection,
	/// which this repository does not do. Isolating dispatch behind this interface means the
	/// detection pipeline is complete and tested today, and closing the upstream gap is a change
	/// to one implementation rather than to every handler. See
	/// <c>docs/tizen-gesture-support-matrix.md</c>.
	/// </para>
	/// </remarks>
	public interface ITizenGestureDispatcher
	{
		/// <summary>
		/// Gets a value indicating whether events for <paramref name="kind"/> can actually be
		/// raised on .NET MAUI recognizers.
		/// </summary>
		/// <param name="kind">The gesture kind to query.</param>
		/// <remarks>
		/// The gesture infrastructure still attaches native detectors for unsupported kinds so
		/// that behaviour is identical the moment dispatch becomes possible, but callers can use
		/// this to surface capability information.
		/// </remarks>
		bool IsSupported(TizenGestureKind kind);

		/// <summary>Raises a tap on <paramref name="recognizer"/>.</summary>
		/// <param name="recognizer">The recognizer to notify.</param>
		/// <param name="view">The view the gesture occurred on.</param>
		/// <param name="position">The tap position, in device-independent units.</param>
		void SendTapped(TapGestureRecognizer recognizer, View view, Point position);

		/// <summary>Raises a pan update on <paramref name="recognizer"/>.</summary>
		/// <param name="recognizer">The recognizer to notify.</param>
		/// <param name="view">The view the gesture occurred on.</param>
		/// <param name="state">The phase the pan is in.</param>
		/// <param name="totalX">Total horizontal movement since the pan started, in device-independent units.</param>
		/// <param name="totalY">Total vertical movement since the pan started, in device-independent units.</param>
		/// <param name="gestureId">The identifier of the current pan.</param>
		void SendPan(PanGestureRecognizer recognizer, View view, TizenGestureState state, double totalX, double totalY, int gestureId);

		/// <summary>Raises a pinch update on <paramref name="recognizer"/>.</summary>
		/// <param name="recognizer">The recognizer to notify.</param>
		/// <param name="view">The view the gesture occurred on.</param>
		/// <param name="state">The phase the pinch is in.</param>
		/// <param name="scale">The scale relative to the start of the gesture.</param>
		/// <param name="origin">The pinch centre, expressed as a fraction of the view.</param>
		void SendPinch(PinchGestureRecognizer recognizer, View view, TizenGestureState state, double scale, Point origin);

		/// <summary>Raises a swipe update on <paramref name="recognizer"/>.</summary>
		/// <param name="recognizer">The recognizer to notify.</param>
		/// <param name="view">The view the gesture occurred on.</param>
		/// <param name="state">The phase the swipe is in.</param>
		/// <param name="totalX">Total horizontal movement, in device-independent units.</param>
		/// <param name="totalY">Total vertical movement, in device-independent units.</param>
		void SendSwipe(SwipeGestureRecognizer recognizer, View view, TizenGestureState state, double totalX, double totalY);

		/// <summary>Raises a long-press update on <paramref name="recognizer"/>.</summary>
		/// <param name="recognizer">The recognizer to notify.</param>
		/// <param name="view">The view the gesture occurred on.</param>
		/// <param name="state">The phase the long press is in.</param>
		/// <param name="position">The press position, in device-independent units.</param>
		void SendLongPress(LongPressGestureRecognizer recognizer, View view, TizenGestureState state, Point position);

		/// <summary>Raises a pointer transition on <paramref name="recognizer"/>.</summary>
		/// <param name="recognizer">The recognizer to notify.</param>
		/// <param name="view">The view the gesture occurred on.</param>
		/// <param name="action">The pointer transition being reported.</param>
		/// <param name="position">The pointer position, in device-independent units.</param>
		void SendPointer(PointerGestureRecognizer recognizer, View view, TizenPointerAction action, Point position);
	}

	/// <summary>
	/// Default <see cref="ITizenGestureDispatcher"/> implementation. It raises every gesture that
	/// .NET MAUI exposes publicly and reports the rest as unsupported.
	/// </summary>
	public sealed class TizenGestureDispatcher : ITizenGestureDispatcher
	{
		internal const string UnsupportedGestureMessage =
			"The Tizen backend detected a {0} gesture but .NET MAUI does not expose a public API to raise it. " +
			"Pan, pinch and swipe are dispatched through IPanGestureController, IPinchGestureController and " +
			"ISwipeGestureController; tap, long-press, pointer, drag and drop have no public equivalent. " +
			"See docs/tizen-gesture-support-matrix.md.";

		readonly ILogger<TizenGestureDispatcher>? _logger;
		readonly bool[] _warned = new bool[Enum.GetValues<TizenGestureKind>().Length];

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenGestureDispatcher"/> class.
		/// </summary>
		/// <param name="logger">Optional logger used to report unsupported gestures once per kind.</param>
		public TizenGestureDispatcher(ILogger<TizenGestureDispatcher>? logger = null)
		{
			_logger = logger;
		}

		/// <inheritdoc/>
		public bool IsSupported(TizenGestureKind kind) => kind switch
		{
			TizenGestureKind.Pan or TizenGestureKind.Pinch or TizenGestureKind.Swipe => true,
			_ => false,
		};

		/// <inheritdoc/>
		public void SendTapped(TapGestureRecognizer recognizer, View view, Point position) =>
			ReportUnsupported(TizenGestureKind.Tap);

		/// <inheritdoc/>
		public void SendPan(PanGestureRecognizer recognizer, View view, TizenGestureState state, double totalX, double totalY, int gestureId)
		{
			ArgumentNullException.ThrowIfNull(recognizer);

			var controller = (IPanGestureController)recognizer;

			switch (state)
			{
				case TizenGestureState.Started:
					controller.SendPanStarted(view, gestureId);
					break;
				case TizenGestureState.Continuing:
					controller.SendPan(view, totalX, totalY, gestureId);
					break;
				case TizenGestureState.Finished:
					controller.SendPanCompleted(view, gestureId);
					break;
				case TizenGestureState.Canceled:
					controller.SendPanCanceled(view, gestureId);
					break;
			}
		}

		/// <inheritdoc/>
		public void SendPinch(PinchGestureRecognizer recognizer, View view, TizenGestureState state, double scale, Point origin)
		{
			ArgumentNullException.ThrowIfNull(recognizer);

			var controller = (IPinchGestureController)recognizer;

			switch (state)
			{
				case TizenGestureState.Started:
					controller.SendPinchStarted(view, origin);
					break;
				case TizenGestureState.Continuing:
					controller.SendPinch(view, scale, origin);
					break;
				case TizenGestureState.Finished:
					controller.SendPinchEnded(view);
					break;
				case TizenGestureState.Canceled:
					controller.SendPinchCanceled(view);
					break;
			}
		}

		/// <inheritdoc/>
		public void SendSwipe(SwipeGestureRecognizer recognizer, View view, TizenGestureState state, double totalX, double totalY)
		{
			ArgumentNullException.ThrowIfNull(recognizer);

			var controller = (ISwipeGestureController)recognizer;

			switch (state)
			{
				case TizenGestureState.Continuing:
					controller.SendSwipe(view, totalX, totalY);
					break;
				case TizenGestureState.Finished:
					controller.DetectSwipe(view, recognizer.Direction);
					break;
			}
		}

		/// <inheritdoc/>
		public void SendLongPress(LongPressGestureRecognizer recognizer, View view, TizenGestureState state, Point position) =>
			ReportUnsupported(TizenGestureKind.LongPress);

		/// <inheritdoc/>
		public void SendPointer(PointerGestureRecognizer recognizer, View view, TizenPointerAction action, Point position) =>
			ReportUnsupported(TizenGestureKind.Pointer);

		void ReportUnsupported(TizenGestureKind kind)
		{
			if (_warned[(int)kind])
			{
				return;
			}

			_warned[(int)kind] = true;
			_logger?.LogWarning(UnsupportedGestureMessage, kind);
		}
	}
}
