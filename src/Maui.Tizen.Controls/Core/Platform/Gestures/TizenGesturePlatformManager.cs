using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementation of <see cref="IGesturePlatformManager"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the port of the NUI <c>GesturePlatformManager</c> from dotnet/maui. It watches the
	/// element's <see cref="View.GestureRecognizers"/> collection plus its
	/// <see cref="VisualElement.IsEnabled"/> and <see cref="VisualElement.InputTransparent"/>
	/// state, and keeps the native detectors in sync.
	/// </para>
	/// <para>
	/// .NET MAUI creates one instance per handler connection and disposes it when the handler
	/// disconnects or changes, so an instance is never reused across connections.
	/// </para>
	/// </remarks>
	public sealed class TizenGesturePlatformManager : IGesturePlatformManager
	{
		readonly Lazy<TizenGestureDetector> _gestureDetector;

		IViewHandler? _handler;
		bool _disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenGesturePlatformManager"/> class.
		/// </summary>
		/// <param name="handler">The handler connection this manager serves.</param>
		/// <param name="handlerFactory">Creates the per-recognizer gesture handlers.</param>
		public TizenGesturePlatformManager(IViewHandler handler, ITizenGestureHandlerFactory handlerFactory)
		{
			_handler = handler ?? throw new ArgumentNullException(nameof(handler));
			ArgumentNullException.ThrowIfNull(handlerFactory);

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

			SetupElement(Element, null);

			if (_gestureDetector.IsValueCreated)
			{
				_gestureDetector.Value.Dispose();
			}

			_handler = null;
		}

		void SetupElement(VisualElement? oldElement, VisualElement? newElement)
		{
			if (oldElement is not null)
			{
				if (oldElement is View oldView && oldView.GestureRecognizers is INotifyCollectionChanged oldObservable)
				{
					oldObservable.CollectionChanged -= OnGestureRecognizerCollectionChanged;
				}

				oldElement.PropertyChanged -= OnElementPropertyChanged;
			}

			if (newElement is null)
			{
				return;
			}

			if (newElement is View newView && newView.GestureRecognizers is INotifyCollectionChanged newObservable)
			{
				newObservable.CollectionChanged += OnGestureRecognizerCollectionChanged;

				if (newView.GestureRecognizers.Count > 0)
				{
					_gestureDetector.Value.AddGestures(newView.GestureRecognizers);
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

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenGesturePlatformManagerFactory"/> class.
		/// </summary>
		/// <param name="handlerFactory">Creates the per-recognizer gesture handlers.</param>
		public TizenGesturePlatformManagerFactory(ITizenGestureHandlerFactory handlerFactory)
		{
			_handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
		}

		/// <inheritdoc/>
		/// <remarks>
		/// A new instance is returned for every call because .NET MAUI disposes and recreates the
		/// manager on every connect or handler change.
		/// </remarks>
		public IGesturePlatformManager CreateGesturePlatformManager(IViewHandler handler)
		{
			ArgumentNullException.ThrowIfNull(handler);

			return new TizenGesturePlatformManager(handler, _handlerFactory);
		}
	}
}
