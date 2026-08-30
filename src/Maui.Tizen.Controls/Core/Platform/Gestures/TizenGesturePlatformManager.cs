using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementation of <see cref="IGesturePlatformManager"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the port of the NUI <c>GesturePlatformManager</c> from dotnet/maui. It watches the
	/// element's <see cref="IGestureController.CompositeGestureRecognizers"/> collection plus its
	/// <see cref="VisualElement.IsEnabled"/> and <see cref="VisualElement.InputTransparent"/>
	/// state, and keeps the native detectors in sync. The composite collection includes both the
	/// public <see cref="View.GestureRecognizers"/> and framework-added recognizers such as the
	/// pointer recognizer that drives the <c>PointerOver</c> visual state.
	/// </para>
	/// <para>
	/// .NET MAUI creates one instance per handler connection and disposes it when the handler
	/// disconnects or changes, so an instance is never reused across connections.
	/// </para>
	/// </remarks>
	public sealed class TizenGesturePlatformManager : IGesturePlatformManager
	{
		readonly Lazy<TizenGestureDetector> _gestureDetector;
		readonly ILogger<TizenGesturePlatformManager>? _logger;

		IViewHandler? _handler;
		IList<IGestureRecognizer>? _gestureRecognizers;
		bool _disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenGesturePlatformManager"/> class.
		/// </summary>
		/// <param name="handler">The handler connection this manager serves.</param>
		/// <param name="handlerFactory">Creates the per-recognizer gesture handlers.</param>
		public TizenGesturePlatformManager(
			IViewHandler handler,
			ITizenGestureHandlerFactory handlerFactory)
			: this(handler, handlerFactory, logger: null)
		{
		}

		internal TizenGesturePlatformManager(
			IViewHandler handler,
			ITizenGestureHandlerFactory handlerFactory,
			ILogger<TizenGesturePlatformManager>? logger)
		{
			_handler = handler ?? throw new ArgumentNullException(nameof(handler));
			ArgumentNullException.ThrowIfNull(handlerFactory);
			_logger = logger;

			// Created lazily so that a view with no gesture recognizers never allocates native
			// detectors, matching the original backend.
			_gestureDetector = new Lazy<TizenGestureDetector>(() => new TizenGestureDetector(handler, handlerFactory));

			SetupElement(null, Element);
		}

		/// <summary>
		/// Gets the element whose gestures are being observed.
		/// </summary>
		public VisualElement? Element => _handler?.VirtualView as VisualElement;

		/// <summary>
		/// Gets the gesture detector, or <see langword="null"/> when no recognizer has required one yet.
		/// </summary>
		public TizenGestureDetector? GestureDetector => _gestureDetector.IsValueCreated ? _gestureDetector.Value : null;

		/// <inheritdoc/>
		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;

			var element = Element;
			var gestureRecognizers = _gestureRecognizers;
			_handler = null;
			_gestureRecognizers = null;

			var cleanup = new List<Action>();
			if (gestureRecognizers is INotifyCollectionChanged observable)
			{
				cleanup.Add(() => observable.CollectionChanged -= OnGestureRecognizerCollectionChanged);
			}

			if (element is not null)
			{
				cleanup.Add(() => element.PropertyChanged -= OnElementPropertyChanged);
			}

			if (_gestureDetector.IsValueCreated)
			{
				cleanup.Add(_gestureDetector.Value.Dispose);
			}

			try
			{
				TizenGestureCleanup.Run(
					"One or more gesture platform manager cleanup operations failed.",
					cleanup.ToArray());
			}
			catch (Exception ex)
			{
				try
				{
					if (_logger is not null)
					{
						_logger.LogError(ex, "Gesture platform manager cleanup failed.");
					}
					else
					{
						Trace.TraceError("Gesture platform manager cleanup failed: {0}", ex);
					}
				}
				catch
				{
					// Framework teardown must not fail because a logger or trace listener failed.
				}
			}
		}

		void SetupElement(VisualElement? oldElement, VisualElement? newElement)
		{
			if (oldElement is not null)
			{
				if (_gestureRecognizers is INotifyCollectionChanged oldObservable)
				{
					oldObservable.CollectionChanged -= OnGestureRecognizerCollectionChanged;
				}

				_gestureRecognizers = null;
				oldElement.PropertyChanged -= OnElementPropertyChanged;
			}

			if (newElement is null)
			{
				return;
			}

			if (newElement is View newView)
			{
				_gestureRecognizers = ((IGestureController)newView).CompositeGestureRecognizers;

				if (_gestureRecognizers is INotifyCollectionChanged newObservable)
				{
					newObservable.CollectionChanged += OnGestureRecognizerCollectionChanged;
				}

				if (_gestureRecognizers.Count > 0)
				{
					_gestureDetector.Value.AddGestures(_gestureRecognizers);
				}
			}

			newElement.PropertyChanged += OnElementPropertyChanged;

			UpdateInputTransparent();
			UpdateIsEnabled();
		}

		void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == VisualElement.InputTransparentProperty.PropertyName)
			{
				UpdateInputTransparent();
			}
			else if (e.PropertyName == VisualElement.IsEnabledProperty.PropertyName)
			{
				UpdateIsEnabled();
			}
		}

		void UpdateInputTransparent()
		{
			if (Element is { } element && _gestureDetector.IsValueCreated)
			{
				_gestureDetector.Value.InputTransparent = element.InputTransparent;
			}
		}

		void UpdateIsEnabled()
		{
			if (Element is { } element && _gestureDetector.IsValueCreated)
			{
				_gestureDetector.Value.IsEnabled = element.IsEnabled;
			}
		}

		void OnGestureRecognizerCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (_disposed)
			{
				return;
			}

			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
					_gestureDetector.Value.AddGestures(e.NewItems?.OfType<IGestureRecognizer>());
					break;

				case NotifyCollectionChangedAction.Replace:
					_gestureDetector.Value.RemoveGestures(e.OldItems?.OfType<IGestureRecognizer>());
					_gestureDetector.Value.AddGestures(e.NewItems?.OfType<IGestureRecognizer>());
					break;

				case NotifyCollectionChangedAction.Remove:
					_gestureDetector.Value.RemoveGestures(e.OldItems?.OfType<IGestureRecognizer>());
					break;

				case NotifyCollectionChangedAction.Reset:
					_gestureDetector.Value.Clear();
					break;
			}
		}
	}

	/// <summary>
	/// Creates <see cref="TizenGesturePlatformManager"/> instances for .NET MAUI.
	/// </summary>
	/// <remarks>
	/// <para>
	/// .NET MAUI resolves <see cref="IGesturePlatformManagerFactory"/> from the application's
	/// services and, when one is registered, uses it instead of constructing its built-in gesture
	/// manager. This is the extensibility point that lets the Tizen backend supply gesture support
	/// from outside <c>Microsoft.Maui.Controls</c>.
	/// </para>
	/// <para>
	/// The built-in managers for Apple and Windows require handlers to implement
	/// <c>IPlatformViewHandler</c>. The Tizen backend deliberately does not, and works with any
	/// <see cref="IViewHandler"/> through its standard
	/// <see cref="IViewHandler.PlatformView"/> and <see cref="IViewHandler.ContainerView"/>
	/// members.
	/// </para>
	/// </remarks>
	public sealed class TizenGesturePlatformManagerFactory : IGesturePlatformManagerFactory
	{
		readonly ITizenGestureHandlerFactory _handlerFactory;
		readonly ILoggerFactory? _loggerFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenGesturePlatformManagerFactory"/> class.
		/// </summary>
		/// <param name="handlerFactory">Creates the per-recognizer gesture handlers.</param>
		public TizenGesturePlatformManagerFactory(
			ITizenGestureHandlerFactory handlerFactory)
			: this(handlerFactory, loggerFactory: null)
		{
		}

		/// <summary>
		/// Initializes a new instance with logging for framework-owned teardown failures.
		/// </summary>
		/// <param name="handlerFactory">Creates the per-recognizer gesture handlers.</param>
		/// <param name="loggerFactory">Creates the per-manager logger.</param>
		public TizenGesturePlatformManagerFactory(
			ITizenGestureHandlerFactory handlerFactory,
			ILoggerFactory? loggerFactory)
		{
			_handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
			_loggerFactory = loggerFactory;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// A new instance is returned for every call because .NET MAUI disposes and recreates the
		/// manager on every connect or handler change.
		/// </remarks>
		public IGesturePlatformManager CreateGesturePlatformManager(IViewHandler handler)
		{
			ArgumentNullException.ThrowIfNull(handler);

			return new TizenGesturePlatformManager(
				handler,
				_handlerFactory,
				_loggerFactory?.CreateLogger<TizenGesturePlatformManager>());
		}
	}
}
