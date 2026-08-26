using System;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The kinds of gesture the Tizen backend can detect.
	/// </summary>
	public enum TizenGestureKind
	{
		/// <summary>A discrete tap.</summary>
		Tap,

		/// <summary>A continuous pan.</summary>
		Pan,

		/// <summary>A directional swipe.</summary>
		Swipe,

		/// <summary>A two-finger pinch.</summary>
		Pinch,

		/// <summary>A press held beyond the long-press threshold.</summary>
		LongPress,

		/// <summary>A pointer entering, moving over, pressing or leaving the view.</summary>
		Pointer,
	}

	/// <summary>
	/// The phase a continuous gesture is in.
	/// </summary>
	/// <remarks>
	/// These values map one-to-one onto <c>Tizen.NUI.Gesture.StateType</c>, which is why the
	/// NUI adapter can translate native gesture state without a lookup table.
	/// </remarks>
	public enum TizenGestureState
	{
		/// <summary>The gesture may be starting but has not been recognised yet.</summary>
		Possible,

		/// <summary>The gesture has been recognised and has started.</summary>
		Started,

		/// <summary>The gesture is in progress.</summary>
		Continuing,

		/// <summary>The gesture completed normally.</summary>
		Finished,

		/// <summary>The gesture was canceled before completing.</summary>
		Canceled,
	}

	/// <summary>
	/// The pointer transitions reported by <see cref="TizenGestureKind.Pointer"/>.
	/// </summary>
	public enum TizenPointerAction
	{
		/// <summary>The pointer entered the view bounds.</summary>
		Entered,

		/// <summary>The pointer moved within the view bounds.</summary>
		Moved,

		/// <summary>The pointer was pressed.</summary>
		Pressed,

		/// <summary>The pointer was released.</summary>
		Released,

		/// <summary>The pointer left the view bounds.</summary>
		Exited,
	}

	/// <summary>
	/// A toolkit-neutral description of a gesture reported by a native Tizen detector.
	/// </summary>
	/// <remarks>
	/// Native NUI detectors translate their own event arguments into this shape, which keeps the
	/// gesture translation logic - state machines, running totals, pixel-to-DP conversion and
	/// recognizer dispatch - free of any dependency on NUI and therefore unit testable off device.
	/// </remarks>
	public sealed class TizenGestureEventArgs : EventArgs
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="TizenGestureEventArgs"/> class.
		/// </summary>
		/// <param name="kind">The kind of gesture being reported.</param>
		/// <param name="state">The phase the gesture is in.</param>
		public TizenGestureEventArgs(TizenGestureKind kind, TizenGestureState state)
		{
			Kind = kind;
			State = state;
		}

		/// <summary>Gets the kind of gesture being reported.</summary>
		public TizenGestureKind Kind { get; }

		/// <summary>Gets the phase the gesture is in.</summary>
		public TizenGestureState State { get; }

		/// <summary>
		/// Gets or sets the gesture position local to the view, in device pixels.
		/// </summary>
		public Point LocalPosition { get; init; }

		/// <summary>
		/// Gets or sets the movement since the previous event, in device pixels.
		/// </summary>
		public Point Displacement { get; init; }

		/// <summary>
		/// Gets or sets the size of the view the gesture occurred in, in device pixels.
		/// </summary>
		/// <remarks>Used to express the pinch centre as a fraction of the view.</remarks>
		public Size ViewSize { get; init; }

		/// <summary>Gets or sets the pinch scale relative to the start of the gesture.</summary>
		public double Scale { get; init; } = 1d;

		/// <summary>Gets or sets the number of taps that have been detected.</summary>
		public int TapCount { get; init; }

		/// <summary>Gets or sets the number of touch points involved in the gesture.</summary>
		public int TouchCount { get; init; } = 1;

		/// <summary>Gets or sets the pointer transition being reported.</summary>
		public TizenPointerAction PointerAction { get; init; }

		/// <summary>
		/// Gets or sets a value indicating whether the native detector should treat the gesture
		/// as consumed.
		/// </summary>
		/// <remarks>
		/// The ported handlers leave this <see langword="false"/> so that overlapping gestures
		/// keep working, matching the original NUI backend.
		/// </remarks>
		public bool Handled { get; set; }
	}
}
