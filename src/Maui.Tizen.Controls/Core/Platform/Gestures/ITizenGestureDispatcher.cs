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
	/// recognizers. Tap, pan, pinch, swipe and pointer are all dispatched through public
	/// .NET MAUI API: <see cref="IPanGestureController"/>, <see cref="IPinchGestureController"/>
	/// and <see cref="ISwipeGestureController"/>, plus
	/// <see cref="TapGestureRecognizer.SendTapped"/> and the
	/// <see cref="PointerGestureRecognizer"/> send members that became public in
	/// dotnet/maui#37420 and #37671.
	/// </para>
	/// <para>
	/// Long press is the one exception: <c>SendLongPressed</c> and <c>SendLongPressing</c> remain
	/// internal to <c>Microsoft.Maui.Controls</c>, and this repository does not use private
	/// reflection. Isolating dispatch behind this interface means the detection pipeline is
	/// complete and tested today, and closing that last gap is a change to one implementation
	/// rather than to every handler. See <c>docs/tizen-gesture-support-matrix.md</c>.
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
		/// <param name="button">The button that produced the tap.</param>
		/// <remarks>
		/// Implementations must not raise the event when <paramref name="button"/> is not present
		/// in <see cref="TapGestureRecognizer.Buttons"/>.
		/// </remarks>
		void SendTapped(TapGestureRecognizer recognizer, View view, TizenGesturePosition position, TizenPointerButton button);

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
		void SendLongPress(LongPressGestureRecognizer recognizer, View view, TizenGestureState state, TizenGesturePosition position);

		/// <summary>Raises a pointer transition on <paramref name="recognizer"/>.</summary>
		/// <param name="recognizer">The recognizer to notify.</param>
		/// <param name="view">The view the gesture occurred on.</param>
		/// <param name="action">The pointer transition being reported.</param>
		/// <param name="position">The pointer position, in device-independent units.</param>
		/// <param name="button">The button that produced the transition.</param>
		/// <remarks>
		/// Implementations must not raise the event when <paramref name="button"/> is not present
		/// in <see cref="PointerGestureRecognizer.Buttons"/>.
		/// </remarks>
		void SendPointer(PointerGestureRecognizer recognizer, View view, TizenPointerAction action, TizenGesturePosition position, TizenPointerButton button);
	}

	/// <summary>
	/// Default <see cref="ITizenGestureDispatcher"/> implementation. It raises every gesture that
	/// .NET MAUI exposes publicly and reports the rest as unsupported.
	/// </summary>
	public sealed class TizenGestureDispatcher : ITizenGestureDispatcher
	{
		internal const string UnsupportedGestureMessage =
			"The Tizen backend detected a {0} gesture but .NET MAUI does not expose a public API to raise it. " +
			"LongPressGestureRecognizer.SendLongPressed and SendLongPressing are still internal in " +
			"Microsoft.Maui.Controls; every other gesture this backend detects is dispatched through public API. " +
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
			// Long press is the only gesture this backend can detect but cannot raise:
			// SendLongPressed / SendLongPressing remain internal to Microsoft.Maui.Controls.
			TizenGestureKind.LongPress => false,
			_ => true,
		};

		/// <inheritdoc/>
		public void SendTapped(TapGestureRecognizer recognizer, View view, TizenGesturePosition position, TizenPointerButton button)
		{
			ArgumentNullException.ThrowIfNull(recognizer);

			var mask = ToButtonsMask(button);

			if (!recognizer.Buttons.HasFlag(mask))
			{
				// The recognizer asked for a different button, so this tap is not for it.
				return;
			}

			// SendTapped takes no button argument - TapGestureRecognizer derives TappedEventArgs.Buttons
			// from its own Buttons property - so filtering above is what enforces the mask.
			recognizer.SendTapped(view, relativeTo => ResolvePosition(relativeTo, view, position));
		}

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
		/// <remarks>
		/// Long press is the one gesture the Tizen backend detects but cannot raise.
		/// <c>LongPressGestureRecognizer.SendLongPressed</c> and <c>SendLongPressing</c> are still
		/// internal to <c>Microsoft.Maui.Controls</c> as of 11.0.0-preview.7.26426.4, and this
		/// repository does not use private reflection. Detection is wired up so behaviour is
		/// identical the moment those members become public.
		/// </remarks>
		public void SendLongPress(LongPressGestureRecognizer recognizer, View view, TizenGestureState state, TizenGesturePosition position) =>
			ReportUnsupported(TizenGestureKind.LongPress);

		/// <summary>
		/// Maps a native Tizen gesture state onto the .NET MAUI long-press status.
		/// </summary>
		/// <param name="state">The native gesture state.</param>
		/// <returns>
		/// The status to report, or <see langword="null"/> when the state carries no long-press
		/// meaning.
		/// </returns>
		/// <remarks>
		/// <para>
		/// This mirrors what iOS does, which is the reference behaviour:
		/// </para>
		/// <list type="table">
		/// <item><term>Started</term><description><see cref="GestureStatus.Started"/> - raise <c>LongPressing</c>.</description></item>
		/// <item><term>Continuing</term><description><see cref="GestureStatus.Running"/> - raise <c>LongPressing</c>.</description></item>
		/// <item><term>Finished</term><description><see cref="GestureStatus.Completed"/> - raise <c>LongPressed</c> FIRST, then <c>LongPressing</c>.</description></item>
		/// <item><term>Canceled</term><description><see cref="GestureStatus.Canceled"/> - raise <c>LongPressing</c> only. Never <c>LongPressed</c>, and never the command: a canceled press is not a press.</description></item>
		/// </list>
		/// <para>
		/// The <c>Continuing</c> row is the one that matters. dotnet/maui's in-box Tizen handler
		/// omits it, so a Tizen long press never reports <see cref="GestureStatus.Running"/> and an
		/// app tracking the gesture sees <c>Started</c> jump straight to <c>Completed</c>. That gap
		/// is deliberately not reproduced here.
		/// </para>
		/// <para>
		/// The mapping is defined and tested ahead of the dispatch itself so that adopting
		/// dotnet/maui#37861 - which makes <c>SendLongPressed</c> and <c>SendLongPressing</c>
		/// public - is a small, already-specified change rather than a fresh translation.
		/// </para>
		/// </remarks>
		internal static GestureStatus? ToLongPressStatus(TizenGestureState state) => state switch
		{
			TizenGestureState.Started => GestureStatus.Started,
			TizenGestureState.Continuing => GestureStatus.Running,
			TizenGestureState.Finished => GestureStatus.Completed,
			TizenGestureState.Canceled => GestureStatus.Canceled,
			_ => null,
		};

		/// <summary>
		/// Gets a value indicating whether <paramref name="state"/> completes a long press, and so
		/// must raise <c>LongPressed</c> and run the recognizer's command.
		/// </summary>
		/// <param name="state">The native gesture state.</param>
		/// <remarks>
		/// Only <see cref="TizenGestureState.Finished"/> qualifies. A canceled press reports a
		/// status change but is not a press, so it must not fire the event or the command.
		/// </remarks>
		internal static bool CompletesLongPress(TizenGestureState state) =>
			state == TizenGestureState.Finished;

		/// <inheritdoc/>
		public void SendPointer(PointerGestureRecognizer recognizer, View view, TizenPointerAction action, TizenGesturePosition position, TizenPointerButton button)
		{
			ArgumentNullException.ThrowIfNull(recognizer);

			var mask = ToButtonsMask(button);

			if ((action is TizenPointerAction.Pressed or TizenPointerAction.Released)
				&& !recognizer.Buttons.HasFlag(mask))
			{
				return;
			}

			Func<IElement?, Point?> getPosition = relativeTo => ResolvePosition(relativeTo, view, position);

			switch (action)
			{
				case TizenPointerAction.Entered:
					recognizer.SendPointerEntered(view, getPosition, button: mask);
					break;
				case TizenPointerAction.Moved:
					recognizer.SendPointerMoved(view, getPosition, button: mask);
					break;
				case TizenPointerAction.Pressed:
					recognizer.SendPointerPressed(view, getPosition, button: mask);
					break;
				case TizenPointerAction.Released:
					recognizer.SendPointerReleased(view, getPosition, button: mask);
					break;
				case TizenPointerAction.Exited:
					recognizer.SendPointerExited(view, getPosition, button: mask);
					break;
			}
		}

		/// <summary>
		/// Maps a native Tizen button onto the .NET MAUI button mask.
		/// </summary>
		/// <remarks>
		/// Touch input carries no button, and NUI reports it as <c>MouseButton.Invalid</c>. It is
		/// mapped to <see cref="ButtonsMask.Primary"/>, matching how .NET MAUI's own touch-based
		/// backends report a finger press. Anything NUI cannot classify is treated the same way
		/// rather than being reported as a secondary click, so a stray unknown value can never
		/// fabricate a right-click.
		/// </remarks>
		internal static ButtonsMask ToButtonsMask(TizenPointerButton button) => button switch
		{
			TizenPointerButton.Secondary => ButtonsMask.Secondary,
			_ => ButtonsMask.Primary,
		};

		/// <summary>
		/// Resolves the gesture position relative to <paramref name="relativeTo"/>.
		/// </summary>
		/// <remarks>
		/// .NET MAUI documents the parameter as "the element to use as the coordinate reference,
		/// or <see langword="null"/> for screen coordinates", so the three cases are distinct:
		/// <list type="bullet">
		/// <item><description><see langword="null"/> - the screen position.</description></item>
		/// <item><description>The view the gesture occurred on - the view-local position.</description></item>
		/// <item><description>Any other element - <see langword="null"/>, meaning "cannot be
		/// determined". Translating into another element's space needs that element's on-screen
		/// origin, which requires a native call the Tizen platform layer does not expose to this
		/// assembly. Returning a wrong coordinate would be worse than reporting it as unknown,
		/// which .NET MAUI already models.</description></item>
		/// </list>
		/// The screen position is also <see langword="null"/> when the native event did not carry
		/// one; the view-local position is deliberately not substituted, because doing so would
		/// answer a screen-coordinate question with a view-local number.
		/// </remarks>
		static Point? ResolvePosition(IElement? relativeTo, View view, TizenGesturePosition position)
		{
			if (relativeTo is null)
			{
				return position.Screen;
			}

			return ReferenceEquals(relativeTo, view) ? position.Local : null;
		}

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
