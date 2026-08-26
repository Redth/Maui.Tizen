using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Creates the <see cref="TizenGestureHandler"/> that services a recognizer.
	/// </summary>
	public interface ITizenGestureHandlerFactory
	{
		/// <summary>
		/// Creates a handler for <paramref name="recognizer"/>, or <see langword="null"/> when the
		/// recognizer type is not supported on Tizen.
		/// </summary>
		/// <param name="recognizer">The recognizer to service.</param>
		TizenGestureHandler? CreateHandler(IGestureRecognizer recognizer);
	}

	/// <summary>
	/// Default <see cref="ITizenGestureHandlerFactory"/> implementation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This replaces the <c>Registrar.Registered.GetHandlerForObject</c> fallback used by the
	/// original NUI backend. Custom gesture support is now supplied by registering a different
	/// <see cref="ITizenGestureHandlerFactory"/> or
	/// <see cref="ITizenNativeGestureDetectorFactory"/> in the application's services, which is
	/// explicit and does not rely on assembly scanning.
	/// </para>
	/// <para>
	/// Drag and drop recognizers are intentionally unsupported; see
	/// <c>docs/tizen-gesture-support-matrix.md</c>.
	/// </para>
	/// </remarks>
	public sealed class TizenGestureHandlerFactory : ITizenGestureHandlerFactory
	{
		readonly ITizenNativeGestureDetectorFactory _detectors;
		readonly ITizenGestureDispatcher _dispatcher;
		readonly ITizenPixelScaler _scaler;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenGestureHandlerFactory"/> class.
		/// </summary>
		/// <param name="detectors">Creates the native detectors.</param>
		/// <param name="dispatcher">Raises the translated gesture events.</param>
		/// <param name="scaler">Converts native device pixels into device-independent units.</param>
		public TizenGestureHandlerFactory(
			ITizenNativeGestureDetectorFactory detectors,
			ITizenGestureDispatcher dispatcher,
			ITizenPixelScaler scaler)
		{
			_detectors = detectors ?? throw new ArgumentNullException(nameof(detectors));
			_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
			_scaler = scaler ?? throw new ArgumentNullException(nameof(scaler));
		}

		/// <inheritdoc/>
		public TizenGestureHandler? CreateHandler(IGestureRecognizer recognizer)
		{
			ArgumentNullException.ThrowIfNull(recognizer);

			// Ordering matters: SwipeGestureRecognizer and PanGestureRecognizer are unrelated
			// types, but both are driven by the native pan detector.
			switch (recognizer)
			{
				case TapGestureRecognizer tap:
					return Create(TizenGestureKind.Tap, tap, (d) => new TizenTapGestureHandler(tap, d, _dispatcher, _scaler));

				case SwipeGestureRecognizer swipe:
					return Create(TizenGestureKind.Swipe, swipe, (d) => new TizenSwipeGestureHandler(swipe, d, _dispatcher, _scaler));

				case PanGestureRecognizer pan:
					return Create(TizenGestureKind.Pan, pan, (d) => new TizenPanGestureHandler(pan, d, _dispatcher, _scaler));

				case PinchGestureRecognizer pinch:
					return Create(TizenGestureKind.Pinch, pinch, (d) => new TizenPinchGestureHandler(pinch, d, _dispatcher, _scaler));

				case LongPressGestureRecognizer longPress:
					return Create(TizenGestureKind.LongPress, longPress, (d) => new TizenLongPressGestureHandler(longPress, d, _dispatcher, _scaler));

				case PointerGestureRecognizer pointer:
					return Create(TizenGestureKind.Pointer, pointer, (d) => new TizenPointerGestureHandler(pointer, d, _dispatcher, _scaler));

				default:
					return null;
			}
		}

		TizenGestureHandler? Create(
			TizenGestureKind kind,
			IGestureRecognizer recognizer,
			Func<ITizenNativeGestureDetector, TizenGestureHandler> create)
		{
			var detector = _detectors.CreateDetector(kind, recognizer);

			return detector is null ? null : create(detector);
		}
	}

	/// <summary>
	/// Tracks the gesture handlers attached to one view and keeps them in sync with the view's
	/// recognizer collection and enabled state.
	/// </summary>
	/// <remarks>
	/// This is the port of the NUI <c>GestureDetector</c> from dotnet/maui, with the
	/// <c>Registrar</c> fallback replaced by <see cref="ITizenGestureHandlerFactory"/> and the
	/// handler type widened from the Tizen-only <c>IPlatformViewHandler</c> to
	/// <see cref="IViewHandler"/>.
	/// </remarks>
	public sealed class TizenGestureDetector : IDisposable
	{
		readonly Dictionary<IGestureRecognizer, TizenGestureHandler> _handlers = new();
		readonly ITizenGestureHandlerFactory _handlerFactory;

		IViewHandler? _handler;
		bool _inputTransparent;
		bool _isEnabled;
		bool _disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenGestureDetector"/> class.
		/// </summary>
		/// <param name="handler">The handler whose platform view gestures are observed on.</param>
		/// <param name="handlerFactory">Creates the per-recognizer gesture handlers.</param>
		public TizenGestureDetector(IViewHandler handler, ITizenGestureHandlerFactory handlerFactory)
		{
			_handler = handler ?? throw new ArgumentNullException(nameof(handler));
			_handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));

			var view = handler.VirtualView as View;
			_isEnabled = view?.IsEnabled ?? false;
			_inputTransparent = view?.InputTransparent ?? false;
		}

		/// <summary>
		/// Gets or sets a value indicating whether the owning element is enabled.
		/// </summary>
		public bool IsEnabled
		{
			get => _isEnabled;
			set
			{
				_isEnabled = value;
				UpdateIsEnabled();
			}
		}

		/// <summary>
		/// Gets or sets a value indicating whether the owning element is transparent to input.
		/// </summary>
		public bool InputTransparent
		{
			get => _inputTransparent;
			set
			{
				_inputTransparent = value;
				UpdateIsEnabled();
			}
		}

		/// <summary>
		/// Gets the number of recognizers currently being tracked.
		/// </summary>
		public int Count => _handlers.Count;

		bool GestureEnabled => IsEnabled && !InputTransparent;

		/// <summary>
		/// Starts tracking each recognizer in <paramref name="gestures"/>.
		/// </summary>
		/// <param name="gestures">The recognizers to track. May be <see langword="null"/>.</param>
		public void AddGestures(IEnumerable<IGestureRecognizer>? gestures)
		{
			if (gestures is null)
			{
				return;
			}

			foreach (var gesture in gestures)
			{
				AddGesture(gesture);
			}
		}

		/// <summary>
		/// Stops tracking each recognizer in <paramref name="gestures"/>.
		/// </summary>
		/// <param name="gestures">The recognizers to stop tracking. May be <see langword="null"/>.</param>
		public void RemoveGestures(IEnumerable<IGestureRecognizer>? gestures)
		{
			if (gestures is null)
			{
				return;
			}

			foreach (var gesture in gestures)
			{
				RemoveGesture(gesture);
			}
		}

		/// <summary>
		/// Stops tracking every recognizer.
		/// </summary>
		public void Clear()
		{
			foreach (var handler in _handlers.Values)
			{
				handler.Dispose();
			}

			_handlers.Clear();
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			Clear();
			_handler = null;
		}

		void AddGesture(IGestureRecognizer gesture)
		{
			if (_disposed || _handlers.ContainsKey(gesture))
			{
				return;
			}

			var handler = _handlerFactory.CreateHandler(gesture);

			if (handler is null)
			{
				return;
			}

			_handlers.Add(gesture, handler);

			if (GestureEnabled && _handler is not null)
			{
				handler.Attach(_handler);
			}
		}

		void RemoveGesture(IGestureRecognizer gesture)
		{
			if (!_handlers.Remove(gesture, out var handler))
			{
				return;
			}

			handler.Dispose();
		}

		void UpdateIsEnabled()
		{
			if (_disposed || _handler is null)
			{
				return;
			}

			if (GestureEnabled)
			{
				foreach (var handler in _handlers.Values)
				{
					handler.Attach(_handler);
				}

				return;
			}

			foreach (var handler in _handlers.Values)
			{
				handler.Detach();
			}
		}
	}
}
