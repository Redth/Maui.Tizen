using System;
using Microsoft.Maui.Controls;
using GPoint = Microsoft.Maui.Graphics.Point;
using GSize = Microsoft.Maui.Graphics.Size;
using global::Tizen.NUI;
using NGestureDetector = global::Tizen.NUI.GestureDetector;
using NGestureState = global::Tizen.NUI.Gesture.StateType;
using NView = global::Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Nui
{
	/// <summary>
	/// Base adapter that turns a <see cref="NGestureDetector"/> into an
	/// <see cref="ITizenNativeGestureDetector"/>.
	/// </summary>
	internal abstract class NuiGestureDetectorAdapter : ITizenNativeGestureDetector
	{
		NView? _attachedView;
		bool _disposed;

		protected NuiGestureDetectorAdapter(NGestureDetector detector) =>
			NativeDetector = detector ?? throw new ArgumentNullException(nameof(detector));

		public event EventHandler<TizenGestureEventArgs>? Detected;

		public bool IsAttached => _attachedView is not null;

		protected NGestureDetector NativeDetector { get; }

		protected NView? AttachedView => _attachedView;

		public void Attach(object platformView)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ObjectDisposedException.ThrowIf(_disposed, this);

			if (platformView is not NView view)
			{
				throw new ArgumentException(
					$"The Tizen gesture infrastructure needs a Tizen.NUI.BaseComponents.View but the handler supplied a '{platformView.GetType()}'.",
					nameof(platformView));
			}

			if (_attachedView is not null)
			{
				return;
			}

			NativeDetector.Attach(view);
			_attachedView = view;
			OnAttached(view);
		}

		public void Detach()
		{
			var view = _attachedView;
			_attachedView = null;

			if (view is null)
			{
				return;
			}

			TizenGestureCleanup.Run(
				"One or more native detector detach operations failed.",
				() => OnDetaching(view),
				() => NativeDetector.Detach(view));
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			Detected = null;

			TizenGestureCleanup.Run(
				"One or more NUI gesture detector cleanup operations failed.",
				UnsubscribeNativeEvents,
				Detach,
				NativeDetector.Dispose);
		}

		/// <summary>Called after the detector has been attached to <paramref name="view"/>.</summary>
		/// <param name="view">The native view.</param>
		protected virtual void OnAttached(NView view)
		{
		}

		/// <summary>Called before the detector is detached from <paramref name="view"/>.</summary>
		/// <param name="view">The native view.</param>
		protected virtual void OnDetaching(NView view)
		{
		}

		/// <summary>Unsubscribes the concrete NUI detector event raised by the adapter.</summary>
		protected virtual void UnsubscribeNativeEvents()
		{
		}

		protected void SubscribeNativeEvents(Action subscribe)
		{
			try
			{
				subscribe();
			}
			catch
			{
				NativeDetector.Dispose();
				throw;
			}
		}

		protected TizenGestureEventArgs Raise(TizenGestureEventArgs args)
		{
			if (!_disposed)
			{
				Detected?.Invoke(this, args);
			}

			return args;
		}

		protected static TizenGestureState ToState(NGestureState state) => state switch
		{
			NGestureState.Started => TizenGestureState.Started,
			NGestureState.Continuing => TizenGestureState.Continuing,
			NGestureState.Finished => TizenGestureState.Finished,
			NGestureState.Cancelled => TizenGestureState.Canceled,
			_ => TizenGestureState.Possible,
		};

		protected static GSize ViewSizeOf(NView? view) =>
			view is null ? GSize.Zero : new GSize(view.Size.Width, view.Size.Height);
	}

	/// <summary>Ported from the NUI <c>TapGestureHandler</c> detector setup.</summary>
	internal sealed class NuiTapGestureDetector : NuiGestureDetectorAdapter
	{
		public NuiTapGestureDetector(uint tapsRequired)
			: base(new TapGestureDetector(tapsRequired == 0 ? 1u : tapsRequired))
		{
			SubscribeNativeEvents(
				() => ((TapGestureDetector)NativeDetector).Detected += OnDetected);
		}

		void OnDetected(object? source, TapGestureDetector.DetectedEventArgs e)
		{
			var tap = e.TapGesture;

			Raise(new TizenGestureEventArgs(TizenGestureKind.Tap, TizenGestureState.Finished)
			{
				TapCount = (int)tap.NumberOfTaps,
				TouchCount = (int)tap.NumberOfTouches,
				LocalPosition = new GPoint(tap.LocalPoint.X, tap.LocalPoint.Y),
				ScreenPosition = new GPoint(tap.ScreenPoint.X, tap.ScreenPoint.Y),
				Button = ToButton(tap.SourceData),
				ViewSize = ViewSizeOf(AttachedView),
			});
		}

		protected override void UnsubscribeNativeEvents() =>
			((TapGestureDetector)NativeDetector).Detected -= OnDetected;

		static TizenPointerButton ToButton(Gesture.SourceDataType sourceData) => sourceData switch
		{
			Gesture.SourceDataType.MousePrimary => TizenPointerButton.Primary,
			Gesture.SourceDataType.MouseSecondary => TizenPointerButton.Secondary,
			Gesture.SourceDataType.MouseTertiary => TizenPointerButton.Tertiary,
			_ => TizenPointerButton.Unknown,
		};
	}

	/// <summary>
	/// Ported from the NUI <c>PanGestureHandler</c> detector setup. Tizen has no dedicated swipe
	/// detector, so this also backs swipe recognition.
	/// </summary>
	internal sealed class NuiPanGestureDetector : NuiGestureDetectorAdapter
	{
		readonly TizenGestureKind _kind;

		public NuiPanGestureDetector(TizenGestureKind kind, uint? touchesRequired = null)
			: base(new PanGestureDetector())
		{
			_kind = kind;
			var detector = (PanGestureDetector)NativeDetector;

			if (touchesRequired is { } touchPoints)
			{
				detector.SetMinimumTouchesRequired(touchPoints);
				detector.SetMaximumTouchesRequired(touchPoints);
			}

			SubscribeNativeEvents(() => detector.Detected += OnDetected);
		}

		void OnDetected(object? source, PanGestureDetector.DetectedEventArgs e)
		{
			var pan = e.PanGesture;

			var args = Raise(new TizenGestureEventArgs(_kind, ToState(pan.State))
			{
				Displacement = new GPoint(pan.Displacement.X, pan.Displacement.Y),
				LocalPosition = new GPoint(pan.Position.X, pan.Position.Y),
				ScreenPosition = new GPoint(pan.ScreenPosition.X, pan.ScreenPosition.Y),
				TouchCount = (int)pan.NumberOfTouches,
				ViewSize = ViewSizeOf(AttachedView),
			});

			// The ported handlers leave the gesture unconsumed so overlapping gestures keep working.
			e.Handled = args.Handled;
		}

		protected override void UnsubscribeNativeEvents() =>
			((PanGestureDetector)NativeDetector).Detected -= OnDetected;
	}

	/// <summary>Ported from the NUI <c>PinchGestureHandler</c> detector setup.</summary>
	internal sealed class NuiPinchGestureDetector : NuiGestureDetectorAdapter
	{
		public NuiPinchGestureDetector()
			: base(new PinchGestureDetector())
		{
			SubscribeNativeEvents(
				() => ((PinchGestureDetector)NativeDetector).Detected += OnDetected);
		}

		void OnDetected(object? source, PinchGestureDetector.DetectedEventArgs e)
		{
			var pinch = e.PinchGesture;

			Raise(new TizenGestureEventArgs(TizenGestureKind.Pinch, ToState(pinch.State))
			{
				Scale = pinch.Scale,
				LocalPosition = new GPoint(pinch.LocalCenterPoint.X, pinch.LocalCenterPoint.Y),
				ScreenPosition = new GPoint(pinch.ScreenCenterPoint.X, pinch.ScreenCenterPoint.Y),
				ViewSize = ViewSizeOf(AttachedView),
			});
		}

		protected override void UnsubscribeNativeEvents() =>
			((PinchGestureDetector)NativeDetector).Detected -= OnDetected;
	}

	/// <summary>Ported from the NUI <c>LongPressGestureHandler</c> detector setup.</summary>
	internal sealed class NuiLongPressGestureDetector : NuiGestureDetectorAdapter
	{
		public NuiLongPressGestureDetector(uint touchesRequired)
			: base(new LongPressGestureDetector(touchesRequired == 0 ? 1u : touchesRequired))
		{
			// NOTE: LongPressGestureRecognizer.MinimumPressDuration cannot be honoured here.
			// Tizen.NUI.LongPressGestureDetector exposes no minimum-holding-time API - it only
			// configures the touch count - so the platform's system-wide long-press duration
			// applies. The dotnet/maui net11.0 source calls SetMinimumHoldingTime, which does not
			// exist in TizenFX (verified against Samsung.Tizen.Ref API13 and API15); that code was
			// never compiled because Tizen was dropped from the MAUI build. See
			// docs/tizen-gesture-support-matrix.md.
			SubscribeNativeEvents(
				() => ((LongPressGestureDetector)NativeDetector).Detected += OnDetected);
		}

		void OnDetected(object? source, LongPressGestureDetector.DetectedEventArgs e)
		{
			var longPress = e.LongPressGesture;

			var args = Raise(new TizenGestureEventArgs(TizenGestureKind.LongPress, ToState(longPress.State))
			{
				LocalPosition = new GPoint(longPress.LocalPoint.X, longPress.LocalPoint.Y),
				ScreenPosition = new GPoint(longPress.ScreenPoint.X, longPress.ScreenPoint.Y),
				TouchCount = (int)longPress.NumberOfTouches,
				ViewSize = ViewSizeOf(AttachedView),
			});

			e.Handled = args.Handled;
		}

		protected override void UnsubscribeNativeEvents() =>
			((LongPressGestureDetector)NativeDetector).Detected -= OnDetected;
	}

	/// <summary>
	/// Derives pointer transitions from NUI touch and hover events.
	/// </summary>
	/// <remarks>
	/// NUI has no pointer gesture detector, so this adapter subscribes to the view's touch and
	/// hover events directly. It is the one detector that has no upstream equivalent: the original
	/// NUI backend had no <see cref="PointerGestureRecognizer"/> support at all. While attached it
	/// shares a lease on <see cref="NView.LeaveRequired"/> so NUI reports boundary exits; the final
	/// pointer detector to detach restores the value that was present before the first attached.
	/// </remarks>
	internal sealed class NuiPointerGestureDetector : ITizenNativeGestureDetector
	{
		static readonly SharedBooleanPropertyLease<NView> s_leaveRequired = new(
			static view => view.LeaveRequired,
			static (view, value) => view.LeaveRequired = value);

		NView? _view;
		IDisposable? _leaveRequiredLease;
		bool _disposed;

		public event EventHandler<TizenGestureEventArgs>? Detected;

		public bool IsAttached => _view is not null;

		public void Attach(object platformView)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ObjectDisposedException.ThrowIf(_disposed, this);

			if (platformView is not NView view)
			{
				throw new ArgumentException(
					$"The Tizen gesture infrastructure needs a Tizen.NUI.BaseComponents.View but the handler supplied a '{platformView.GetType()}'.",
					nameof(platformView));
			}

			if (_view is not null)
			{
				return;
			}

			var leaveRequiredLease = s_leaveRequired.Acquire(view);
			_view = view;
			_leaveRequiredLease = leaveRequiredLease;

			try
			{
				view.TouchEvent += OnTouch;
				view.HoverEvent += OnHover;
			}
			catch
			{
				Detach();
				throw;
			}
		}

		public void Detach()
		{
			var view = _view;
			var leaveRequiredLease = _leaveRequiredLease;
			_view = null;
			_leaveRequiredLease = null;

			if (view is null)
			{
				return;
			}

			TizenGestureCleanup.Run(
				"One or more NUI pointer detector detach operations failed.",
				() => view.TouchEvent -= OnTouch,
				() => view.HoverEvent -= OnHover,
				() => leaveRequiredLease?.Dispose());
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			Detected = null;
			Detach();
		}

		bool OnTouch(object source, NView.TouchEventArgs e)
		{
			var touch = e.Touch;

			if (touch is null || touch.GetPointCount() == 0)
			{
				return false;
			}

			var action = touch.GetState(0) switch
			{
				PointStateType.Down => TizenPointerAction.Pressed,
				PointStateType.Up => TizenPointerAction.Released,
				PointStateType.Motion => TizenPointerAction.Moved,
				PointStateType.Leave => TizenPointerAction.Exited,
				_ => (TizenPointerAction?)null,
			};

			if (action is null)
			{
				return false;
			}

			var local = touch.GetLocalPosition(0);
			var screen = touch.GetScreenPosition(0);

			Raise(
				action.Value,
				new GPoint(local.X, local.Y),
				new GPoint(screen.X, screen.Y),
				ToButton(touch.GetMouseButton(0)));

			// Never consume the touch: doing so would stop the view's own handlers from running.
			return false;
		}

		bool OnHover(object source, NView.HoverEventArgs e)
		{
			var hover = e.Hover;

			if (hover is null || hover.GetPointCount() == 0)
			{
				return false;
			}

			var action = hover.GetState(0) switch
			{
				PointStateType.Started => TizenPointerAction.Entered,
				PointStateType.Motion => TizenPointerAction.Moved,
				PointStateType.Finished or PointStateType.Leave => TizenPointerAction.Exited,
				_ => (TizenPointerAction?)null,
			};

			if (action is null)
			{
				return false;
			}

			var local = hover.GetLocalPosition(0);
			var screen = hover.GetScreenPosition(0);

			// Tizen.NUI.Hover exposes no GetMouseButton: a hover is pointer movement with nothing
			// pressed, so there is no button to report. Unknown maps to Primary downstream.
			Raise(
				action.Value,
				new GPoint(local.X, local.Y),
				new GPoint(screen.X, screen.Y),
				TizenPointerButton.Unknown);

			return false;
		}

		/// <summary>
		/// Maps a native NUI mouse button onto the toolkit-neutral enum.
		/// </summary>
		/// <remarks>
		/// Touch input has no button and NUI reports <c>MouseButton.Invalid</c> for it, which maps
		/// to <see cref="TizenPointerButton.Unknown"/>. The dispatcher then treats it as the
		/// primary button rather than inventing a secondary click.
		/// </remarks>
		static TizenPointerButton ToButton(MouseButton button) => button switch
		{
			MouseButton.Primary => TizenPointerButton.Primary,
			MouseButton.Secondary => TizenPointerButton.Secondary,
			MouseButton.Tertiary => TizenPointerButton.Tertiary,
			_ => TizenPointerButton.Unknown,
		};

		void Raise(TizenPointerAction action, GPoint local, GPoint screen, TizenPointerButton button)
		{
			if (_disposed)
			{
				return;
			}

			Detected?.Invoke(this, new TizenGestureEventArgs(TizenGestureKind.Pointer, TizenGestureState.Finished)
			{
				PointerAction = action,
				LocalPosition = local,
				ScreenPosition = screen,
				Button = button,
				ViewSize = _view is null ? GSize.Zero : new GSize(_view.Size.Width, _view.Size.Height),
			});
		}
	}

	/// <summary>
	/// Creates the NUI detectors that back the Tizen gesture pipeline.
	/// </summary>
	/// <remarks>
	/// Returning <see langword="null"/> means "this gesture is not available", which the gesture
	/// detector treats as "skip this recognizer" rather than an error. See
	/// <c>docs/tizen-gesture-support-matrix.md</c> for what each Tizen profile supports.
	/// </remarks>
	public sealed class NuiGestureDetectorFactory : ITizenNativeGestureDetectorFactory
	{
		/// <inheritdoc/>
		public ITizenNativeGestureDetector? CreateDetector(
			TizenGestureKind kind,
			IGestureRecognizer recognizer,
			TizenNativeGestureConfiguration configuration)
		{
			ArgumentNullException.ThrowIfNull(recognizer);

			return kind switch
			{
				TizenGestureKind.Tap when recognizer is TapGestureRecognizer =>
					new NuiTapGestureDetector((uint)configuration.RequiredTapCount),

				TizenGestureKind.Pan when recognizer is PanGestureRecognizer =>
					new NuiPanGestureDetector(
						kind,
						(uint)configuration.RequiredTouchCount),

				// Swipe rides on the pan detector because Tizen has no swipe gesture of its own.
				TizenGestureKind.Swipe when recognizer is SwipeGestureRecognizer =>
					new NuiPanGestureDetector(kind),

				TizenGestureKind.Pinch =>
					new NuiPinchGestureDetector(),

				TizenGestureKind.LongPress when recognizer is LongPressGestureRecognizer =>
					new NuiLongPressGestureDetector((uint)configuration.RequiredTouchCount),

				TizenGestureKind.Pointer =>
					new NuiPointerGestureDetector(),

				_ => null,
			};
		}
	}
}
