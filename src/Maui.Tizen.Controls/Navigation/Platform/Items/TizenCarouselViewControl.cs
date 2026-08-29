using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.NUI;
using Tizen.UIExtensions.NUI;

using NCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NLayoutParamPolicies = Tizen.NUI.BaseComponents.LayoutParamPolicies;
using NView = Tizen.NUI.BaseComponents.View;
using TSnapPointsAlignment = Tizen.UIExtensions.NUI.SnapPointsAlignment;
using TSnapPointsType = Tizen.UIExtensions.NUI.SnapPointsType;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Platform view for <see cref="CarouselView"/> in the Tizen backend.
	/// </summary>
	/// <remarks>
	/// Manages the CurrentItem/Position visual state changes and carousel-specific layout.
	/// </remarks>
	public class TizenCarouselViewControl : TizenItemsViewControl<CarouselView>
	{
		List<View> _visibleViews = new();
		int _lastPosition = -1;
		readonly DeferredCarouselPosition _deferredPosition = new();
		readonly CarouselInteractionState _interaction = new();
		readonly CarouselViewportTracker _viewport = new();
		IItemsLayout? _observedItemsLayout;
		bool _disposed;
		bool _eventsConnected;

		public TizenCarouselViewControl(CarouselView element) : base(element)
		{
		}

		public override void Rebind(CarouselView element)
		{
			var previous = Element;
			base.Rebind(element);
			ClearVisibleViews(previous);
			_lastPosition = -1;
			_viewport.Reset();
		}

		/// <summary>
		/// Raised when the scroll position changes.
		/// </summary>
		public event EventHandler<int>? Scrolled;

		internal event EventHandler? ItemsLayoutChanged;

		protected override NCollectionView CreateCollectionView()
		{
			return new NCollectionView
			{
				WidthSpecification = NLayoutParamPolicies.MatchParent,
				HeightSpecification = NLayoutParamPolicies.MatchParent,
				SelectionMode = CollectionViewSelectionMode.None,
				SnapPointsType = TSnapPointsType.MandatorySingle,
			};
		}

		protected override void Initialize()
		{
			base.Initialize();
			UpdateLayoutManager();
			ConnectEvents();
		}

		public void UpdateLayoutManager()
		{
			var itemsLayout = Element.ItemsLayout ?? new LinearItemsLayout(ItemsLayoutOrientation.Horizontal);
			if (!ReferenceEquals(_observedItemsLayout, itemsLayout))
			{
				if (_observedItemsLayout is not null)
					_observedItemsLayout.PropertyChanged -= OnItemsLayoutPropertyChanged;

				_observedItemsLayout = itemsLayout;
				_observedItemsLayout.PropertyChanged += OnItemsLayoutPropertyChanged;
			}

			CollectionView.LayoutManager = itemsLayout.ToLayoutManager(Microsoft.Maui.Controls.ItemSizingStrategy.MeasureAllItems);
			var state = ItemsLayoutSnapshot.Capture(itemsLayout);
			CollectionView.SnapPointsType = (TSnapPointsType)state.SnapPointsType;
			CollectionView.SnapPointsAlignment = (TSnapPointsAlignment)state.SnapPointsAlignment;
			CollectionView.ScrollView.HideScrollbar = CollectionView.LayoutManager.IsHorizontal
				? Element.HorizontalScrollBarVisibility == ScrollBarVisibility.Never
				: Element.VerticalScrollBarVisibility == ScrollBarVisibility.Never;
			TryApplyPendingPosition();
			ItemsLayoutChanged?.Invoke(this, EventArgs.Empty);
		}

		public void UpdatePosition(int position)
			=> UpdatePosition(position, animate: false);

		internal void UpdatePosition(int position, bool animate)
		{
			_deferredPosition.SetPosition(position, animate);
			TryApplyPendingPosition();
		}

		public void UpdateCurrentItem(object? currentItem)
			=> UpdateCurrentItem(currentItem, animate: false);

		internal void UpdateCurrentItem(object? currentItem, bool animate)
		{
			_deferredPosition.SetCurrentItem(currentItem, animate);
			TryApplyPendingPosition();
		}

		public void UpdateLoop()
		{
			// Loop mode requires special adaptor handling
			// Currently Tizen.UIExtensions.NUI does not fully support loop mode
			// so this is a no-op placeholder for future implementation
		}

		void OnCollectionViewScrolled(object? sender, CollectionViewScrolledEventArgs e)
		{
			int currentIndex = e.CenterItemIndex;

			if (currentIndex >= 0)
			{
				UpdateVisualStates(currentIndex, e.FirstVisibleItemIndex, e.LastVisibleItemIndex);
				if (currentIndex != _lastPosition)
				{
					_lastPosition = currentIndex;
					Scrolled?.Invoke(this, currentIndex);
				}
			}
		}

		void OnRelayout(object? sender, EventArgs e)
		{
			if (!_viewport.Update(Size.Width, Size.Height))
				return;
			if (Element.CurrentItem is not null)
				_deferredPosition.SetCurrentItem(Element.CurrentItem);
			else
				_deferredPosition.SetPosition(Element.Position);
			TryApplyPendingPosition();
		}

		void OnItemsLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (ReferenceEquals(sender, _observedItemsLayout))
				UpdateLayoutManager();
		}

		void TryApplyPendingPosition()
		{
			var adaptor = CollectionView.Adaptor;
			_deferredPosition.TryApply(
				adaptor is not null
					&& CollectionView.LayoutManager is not null
					&& Size.Width > 0
					&& Size.Height > 0,
				adaptor?.Count ?? 0,
				item => item is null ? -1 : adaptor?.GetItemIndex(item) ?? -1,
				(index, animate) =>
				{
					if (animate)
					{
						_interaction.BeginAnimation();
						ApplyInteractionState();
					}
					CollectionView.ScrollTo(index, animate: animate);
				});
		}

		internal void PrepareForAdaptorReplacement() => ClearVisibleViews(Element);

		internal void ConnectEvents()
		{
			if (_eventsConnected)
				return;

			_eventsConnected = true;
			CollectionView.Scrolled += OnCollectionViewScrolled;
			CollectionView.ScrollView.ScrollDragStarted += OnDragStarted;
			CollectionView.ScrollView.ScrollDragEnded += OnDragEnded;
			CollectionView.ScrollView.ScrollAnimationStarted += OnScrollAnimationStarted;
			CollectionView.ScrollView.ScrollAnimationEnded += OnScrollAnimationEnded;
			Relayout += OnRelayout;
		}

		internal void DisconnectEvents()
		{
			if (!_eventsConnected)
				return;

			_eventsConnected = false;
			CollectionView.Scrolled -= OnCollectionViewScrolled;
			CollectionView.ScrollView.ScrollDragStarted -= OnDragStarted;
			CollectionView.ScrollView.ScrollDragEnded -= OnDragEnded;
			CollectionView.ScrollView.ScrollAnimationStarted -= OnScrollAnimationStarted;
			CollectionView.ScrollView.ScrollAnimationEnded -= OnScrollAnimationEnded;
			Relayout -= OnRelayout;
			if (_observedItemsLayout is not null)
			{
				_observedItemsLayout.PropertyChanged -= OnItemsLayoutPropertyChanged;
				_observedItemsLayout = null;
			}
			ClearVisibleViews(Element);
			_interaction.Reset();
			ApplyInteractionState();
		}

		void OnDragStarted(object? sender, EventArgs e)
		{
			_interaction.BeginDrag();
			ApplyInteractionState();
		}

		void OnDragEnded(object? sender, EventArgs e)
		{
			_interaction.EndDrag();
			ApplyInteractionState();
		}

		void OnScrollAnimationStarted(object? sender, EventArgs e)
		{
			_interaction.BeginAnimation();
			ApplyInteractionState();
		}

		void OnScrollAnimationEnded(object? sender, EventArgs e)
		{
			_interaction.EndAnimation();
			ApplyInteractionState();
		}

		void ApplyInteractionState()
		{
			Element.SetIsDragging(_interaction.IsDragging);
			Element.IsScrolling = _interaction.IsScrolling;
		}

		void ClearVisibleViews(CarouselView owner)
		{
			foreach (var view in _visibleViews)
			{
				VisualStateManager.GoToState(view, CarouselView.DefaultItemVisualState);
				owner.VisibleViews.Remove(view);
			}
			_visibleViews.Clear();
		}

		void UpdateVisualStates(int currentIndex, int firstVisibleIndex, int lastVisibleIndex)
		{
			var adaptor = CollectionView.Adaptor as TizenItemTemplateAdaptor;
			if (adaptor == null)
				return;

			var newViews = new List<View>();
			var first = Math.Max(0, firstVisibleIndex);
			var last = Math.Min(adaptor.Count - 1, lastVisibleIndex);
			for (int i = first; i <= last; i++)
			{
				var view = adaptor.GetTemplatedView(i);
				if (view == null)
					continue;

				VisualStateManager.GoToState(view, CarouselVisualState.ForIndex(i, currentIndex));
				newViews.Add(view);
				if (!Element.VisibleViews.Contains(view))
					Element.VisibleViews.Add(view);
			}

			foreach (var view in _visibleViews.Where(view => !newViews.Contains(view)))
			{
				VisualStateManager.GoToState(view, CarouselView.DefaultItemVisualState);
				Element.VisibleViews.Remove(view);
			}
			_visibleViews = newViews;
		}

		protected override void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			if (disposing)
			{
				DisconnectEvents();
			}
			_disposed = true;
			base.Dispose(disposing);
		}
	}
}
