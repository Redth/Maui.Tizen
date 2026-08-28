using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Translates native tap gestures into <see cref="TapGestureRecognizer"/> events.
	/// </summary>
	/// <remarks>
	/// Ported from the NUI <c>TapGestureHandler</c>. The tap is only reported when the number of
	/// detected taps matches <see cref="TapGestureRecognizer.NumberOfTapsRequired"/>, matching the
	/// original behaviour.
	/// </remarks>
	public sealed class TizenTapGestureHandler : TizenGestureHandler
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="TizenTapGestureHandler"/> class.
		/// </summary>
		/// <param name="recognizer">The tap recognizer to feed.</param>
		/// <param name="detector">The native tap detector.</param>
		/// <param name="dispatcher">Raises the translated gesture events.</param>
		/// <param name="scaler">Converts native device pixels into device-independent units.</param>
		public TizenTapGestureHandler(
			TapGestureRecognizer recognizer,
			ITizenNativeGestureDetector detector,
			ITizenGestureDispatcher dispatcher,
			ITizenPixelScaler scaler)
			: base(recognizer, detector, dispatcher, scaler)
		{
		}

		new TapGestureRecognizer Recognizer => (TapGestureRecognizer)base.Recognizer;

		/// <inheritdoc/>
		protected override void OnGestureDetected(View view, TizenGestureEventArgs args)
		{
			if (args.Kind != TizenGestureKind.Tap)
			{
				return;
			}

			if (args.TapCount != Recognizer.NumberOfTapsRequired)
			{
				return;
			}

			Dispatcher.SendTapped(Recognizer, view, ToScaledDp(args), args.Button);
		}
	}

	/// <summary>
	/// Translates native pan gestures into <see cref="PanGestureRecognizer"/> events.
	/// </summary>
	/// <remarks>
	/// Ported from the NUI <c>PanGestureHandler</c>. Native detectors report per-frame
	/// displacement, so the handler accumulates the running total and allocates a new gesture id
	/// for each pan, exactly as the original did.
	/// </remarks>
	public sealed class TizenPanGestureHandler : TizenGestureHandler
	{
		readonly int _requiredTouchPoints;

		int _gestureId;
		bool _gestureActive;
		double _totalX;
		double _totalY;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenPanGestureHandler"/> class.
		/// </summary>
		/// <param name="recognizer">The pan recognizer to feed.</param>
		/// <param name="detector">The native pan detector.</param>
		/// <param name="dispatcher">Raises the translated gesture events.</param>
		/// <param name="scaler">Converts native device pixels into device-independent units.</param>
		public TizenPanGestureHandler(
			PanGestureRecognizer recognizer,
			ITizenNativeGestureDetector detector,
			ITizenGestureDispatcher dispatcher,
			ITizenPixelScaler scaler)
			: base(recognizer, detector, dispatcher, scaler)
		{
			_requiredTouchPoints = GetRequiredTouchPoints(recognizer);
		}

		new PanGestureRecognizer Recognizer => (PanGestureRecognizer)base.Recognizer;

		internal static int GetRequiredTouchPoints(PanGestureRecognizer recognizer) =>
			Math.Max(1, recognizer.TouchPoints);

		/// <inheritdoc/>
		protected override void OnGestureDetected(View view, TizenGestureEventArgs args)
		{
			if (args.Kind != TizenGestureKind.Pan)
			{
				return;
			}

			// Leave the native gesture unconsumed so that overlapping gestures keep working.
			args.Handled = false;

			switch (args.State)
			{
				case TizenGestureState.Started:
					_gestureActive = false;

					if (args.TouchCount != _requiredTouchPoints)
					{
						return;
					}

					_gestureActive = true;
					_gestureId++;
					_totalX = args.Displacement.X;
					_totalY = args.Displacement.Y;
					Dispatcher.SendPan(Recognizer, view, TizenGestureState.Started, 0, 0, _gestureId);
					break;

				case TizenGestureState.Continuing:
					if (!_gestureActive || args.TouchCount != _requiredTouchPoints)
					{
						return;
					}

					_totalX += args.Displacement.X;
					_totalY += args.Displacement.Y;
					Dispatcher.SendPan(
						Recognizer,
						view,
						TizenGestureState.Continuing,
						Scaler.ToScaledDp(_totalX),
						Scaler.ToScaledDp(_totalY),
						_gestureId);
					break;

				case TizenGestureState.Canceled:
					if (!_gestureActive)
					{
						return;
					}

					_gestureActive = false;
					Dispatcher.SendPan(Recognizer, view, TizenGestureState.Canceled, 0, 0, _gestureId);
					break;

				case TizenGestureState.Finished:
					if (!_gestureActive)
					{
						return;
					}

					_gestureActive = false;
					Dispatcher.SendPan(Recognizer, view, TizenGestureState.Finished, 0, 0, _gestureId);
					break;
			}
		}
	}

	/// <summary>
	/// Translates native pan gestures into <see cref="SwipeGestureRecognizer"/> events.
	/// </summary>
	/// <remarks>
	/// Ported from the NUI <c>SwipeGestureHandler</c>. Tizen has no dedicated swipe detector, so a
	/// pan detector is used and the accumulated movement is handed to the swipe recognizer, which
	/// decides whether the movement qualifies as a swipe in the configured direction.
	/// </remarks>
	public sealed class TizenSwipeGestureHandler : TizenGestureHandler
	{
		double _totalX;
		double _totalY;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenSwipeGestureHandler"/> class.
		/// </summary>
		/// <param name="recognizer">The swipe recognizer to feed.</param>
		/// <param name="detector">The native pan detector backing swipe detection.</param>
		/// <param name="dispatcher">Raises the translated gesture events.</param>
		/// <param name="scaler">Converts native device pixels into device-independent units.</param>
		public TizenSwipeGestureHandler(
			SwipeGestureRecognizer recognizer,
			ITizenNativeGestureDetector detector,
			ITizenGestureDispatcher dispatcher,
			ITizenPixelScaler scaler)
			: base(recognizer, detector, dispatcher, scaler)
		{
		}

		new SwipeGestureRecognizer Recognizer => (SwipeGestureRecognizer)base.Recognizer;

		/// <inheritdoc/>
		protected override void OnGestureDetected(View view, TizenGestureEventArgs args)
		{
			if (args.Kind != TizenGestureKind.Swipe && args.Kind != TizenGestureKind.Pan)
			{
				return;
			}

			args.Handled = false;

			switch (args.State)
			{
				case TizenGestureState.Started:
					_totalX = args.Displacement.X;
					_totalY = args.Displacement.Y;
					break;

				case TizenGestureState.Continuing:
					_totalX += args.Displacement.X;
					_totalY += args.Displacement.Y;
					Dispatcher.SendSwipe(
						Recognizer,
						view,
						TizenGestureState.Continuing,
						Scaler.ToScaledDp(_totalX),
						Scaler.ToScaledDp(_totalY));
					break;

				case TizenGestureState.Finished:
					Dispatcher.SendSwipe(Recognizer, view, TizenGestureState.Finished, 0, 0);
					break;
			}
		}
	}

	/// <summary>
	/// Translates native pinch gestures into <see cref="PinchGestureRecognizer"/> events.
	/// </summary>
	/// <remarks>
	/// Ported from the NUI <c>PinchGestureHandler</c>. The native scale is relative to the start
	/// of the gesture, so it is combined with the view's scale at the moment the pinch started,
	/// and the pinch centre is expressed as a fraction of the view as .NET MAUI expects.
	/// </remarks>
	public sealed class TizenPinchGestureHandler : TizenGestureHandler
	{
		double _pinchStartingScale = 1d;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenPinchGestureHandler"/> class.
		/// </summary>
		/// <param name="recognizer">The pinch recognizer to feed.</param>
		/// <param name="detector">The native pinch detector.</param>
		/// <param name="dispatcher">Raises the translated gesture events.</param>
		/// <param name="scaler">Converts native device pixels into device-independent units.</param>
		public TizenPinchGestureHandler(
			PinchGestureRecognizer recognizer,
			ITizenNativeGestureDetector detector,
			ITizenGestureDispatcher dispatcher,
			ITizenPixelScaler scaler)
			: base(recognizer, detector, dispatcher, scaler)
		{
		}

		new PinchGestureRecognizer Recognizer => (PinchGestureRecognizer)base.Recognizer;

		/// <inheritdoc/>
		protected override void OnGestureDetected(View view, TizenGestureEventArgs args)
		{
			if (args.Kind != TizenGestureKind.Pinch)
			{
				return;
			}

			switch (args.State)
			{
				case TizenGestureState.Started:
					_pinchStartingScale = view.Scale;
					Dispatcher.SendPinch(Recognizer, view, TizenGestureState.Started, args.Scale, ScalePoint(args));
					break;

				case TizenGestureState.Continuing:
					var scale = 1 + ((args.Scale - 1) * _pinchStartingScale);
					Dispatcher.SendPinch(Recognizer, view, TizenGestureState.Continuing, scale, ScalePoint(args));
					break;

				case TizenGestureState.Finished:
					Dispatcher.SendPinch(Recognizer, view, TizenGestureState.Finished, args.Scale, ScalePoint(args));
					break;

				case TizenGestureState.Canceled:
					Dispatcher.SendPinch(Recognizer, view, TizenGestureState.Canceled, args.Scale, ScalePoint(args));
					break;
			}
		}

		static Point ScalePoint(TizenGestureEventArgs args)
		{
			// The pinch centre is expressed as a fraction of the view, so a zero-sized view - which
			// happens when the platform view has not been measured yet - degrades to the origin
			// instead of producing NaN or infinity.
			var width = args.ViewSize.Width;
			var height = args.ViewSize.Height;

			return new Point(
				width > 0 ? args.LocalPosition.X / width : 0,
				height > 0 ? args.LocalPosition.Y / height : 0);
		}
	}

	/// <summary>
	/// Translates native long-press gestures into <see cref="LongPressGestureRecognizer"/> events.
	/// </summary>
	/// <remarks>
	/// Ported from the NUI <c>LongPressGestureHandler</c>. A completed long press reports both the
	/// long-pressed event and the terminal long-pressing update, matching the original ordering.
	/// </remarks>
	public sealed class TizenLongPressGestureHandler : TizenGestureHandler
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="TizenLongPressGestureHandler"/> class.
		/// </summary>
		/// <param name="recognizer">The long-press recognizer to feed.</param>
		/// <param name="detector">The native long-press detector.</param>
		/// <param name="dispatcher">Raises the translated gesture events.</param>
		/// <param name="scaler">Converts native device pixels into device-independent units.</param>
		public TizenLongPressGestureHandler(
			LongPressGestureRecognizer recognizer,
			ITizenNativeGestureDetector detector,
			ITizenGestureDispatcher dispatcher,
			ITizenPixelScaler scaler)
			: base(recognizer, detector, dispatcher, scaler)
		{
		}

		new LongPressGestureRecognizer Recognizer => (LongPressGestureRecognizer)base.Recognizer;

		/// <inheritdoc/>
		protected override void OnGestureDetected(View view, TizenGestureEventArgs args)
		{
			if (args.Kind != TizenGestureKind.LongPress)
			{
				return;
			}

			args.Handled = false;

			var position = ToScaledDp(args);

			switch (args.State)
			{
				case TizenGestureState.Started:
				// Continuing is deliberately included. dotnet/maui's in-box Tizen handler drops it,
				// so a Tizen long press never reports GestureStatus.Running and an app that tracks
				// the gesture's progress sees Started jump straight to Completed. iOS maps its
				// equivalent (UIGestureRecognizerState.Changed) to Running, and that is the correct
				// behaviour; the in-box gap is not copied here.
				case TizenGestureState.Continuing:
				case TizenGestureState.Finished:
				case TizenGestureState.Canceled:
					Dispatcher.SendLongPress(Recognizer, view, args.State, position);
					break;
			}
		}
	}

	/// <summary>
	/// Translates native pointer activity into <see cref="PointerGestureRecognizer"/> events.
	/// </summary>
	/// <remarks>
	/// Tizen has no pointer gesture detector; the native adapter derives pointer transitions from
	/// NUI touch and hover events and reports them through
	/// <see cref="TizenGestureEventArgs.PointerAction"/>.
	/// </remarks>
	public sealed class TizenPointerGestureHandler : TizenGestureHandler
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="TizenPointerGestureHandler"/> class.
		/// </summary>
		/// <param name="recognizer">The pointer recognizer to feed.</param>
		/// <param name="detector">The native pointer detector.</param>
		/// <param name="dispatcher">Raises the translated gesture events.</param>
		/// <param name="scaler">Converts native device pixels into device-independent units.</param>
		public TizenPointerGestureHandler(
			PointerGestureRecognizer recognizer,
			ITizenNativeGestureDetector detector,
			ITizenGestureDispatcher dispatcher,
			ITizenPixelScaler scaler)
			: base(recognizer, detector, dispatcher, scaler)
		{
		}

		new PointerGestureRecognizer Recognizer => (PointerGestureRecognizer)base.Recognizer;

		/// <inheritdoc/>
		protected override void OnGestureDetected(View view, TizenGestureEventArgs args)
		{
			if (args.Kind != TizenGestureKind.Pointer)
			{
				return;
			}

			Dispatcher.SendPointer(Recognizer, view, args.PointerAction, ToScaledDp(args), args.Button);
		}
	}
}
