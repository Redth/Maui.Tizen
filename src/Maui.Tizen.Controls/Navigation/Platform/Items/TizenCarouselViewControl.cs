using System;
using System.Collections.Generic;
using System.ComponentModel;
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
		int _lastPosition = -1;
		readonly DeferredCarouselPosition _deferredPosition = new();
		IItemsLayout? _observedItemsLayout;
		bool _disposed;

		public TizenCarouselViewControl(CarouselView element) : base(element)
		{
		}

		public override void Rebind(CarouselView element)
		{
			base.Rebind(element);
			_lastPosition = -1;
			UpdateLayoutManager();
		}

		/// <summary>
		/// Raised when the scroll position changes.
		/// </summary>
		public event EventHandler<int>? Scrolled;

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
			CollectionView.Scrolled += OnCollectionViewScrolled;
			Relayout += OnRelayout;
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
			if (itemsLayout is ItemsLayout layout)
			{
				CollectionView.SnapPointsType = (TSnapPointsType)layout.SnapPointsType;
				CollectionView.SnapPointsAlignment = (TSnapPointsAlignment)layout.SnapPointsAlignment;
			}
			CollectionView.ScrollView.HideScrollbar = CollectionView.LayoutManager.IsHorizontal
				? Element.HorizontalScrollBarVisibility == ScrollBarVisibility.Never
				: Element.VerticalScrollBarVisibility == ScrollBarVisibility.Never;
			if (Element.CurrentItem is not null)
				_deferredPosition.SetCurrentItem(Element.CurrentItem);
			else
				_deferredPosition.SetPosition(Element.Position);
			TryApplyPendingPosition();
		}

		public void UpdatePosition(int position)
		{
			_deferredPosition.SetPosition(position);
			TryApplyPendingPosition();
		}

		public void UpdateCurrentItem(object? currentItem)
		{
			_deferredPosition.SetCurrentItem(currentItem);
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

			if (currentIndex != _lastPosition && currentIndex >= 0)
			{
				UpdateVisualStates(currentIndex);
				_lastPosition = currentIndex;
				Scrolled?.Invoke(this, currentIndex);
			}
		}

		void OnRelayout(object? sender, EventArgs e) => TryApplyPendingPosition();

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
				index => CollectionView.ScrollTo(index, animate: false));
		}

		void UpdateVisualStates(int currentIndex)
		{
			var adaptor = CollectionView.Adaptor as TizenItemTemplateAdaptor;
			if (adaptor == null)
				return;

			// Update visual states for visible items
			int count = adaptor.Count;
			for (int i = 0; i < count; i++)
			{
				var view = adaptor.GetTemplatedView(i);
				if (view == null)
					continue;

				var state = i == currentIndex
					? VisualStateManager.CommonStates.Selected
					: VisualStateManager.CommonStates.Normal;

				// Use VisualStateManager to transition the state
				VisualStateManager.GoToState(view, state);
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			if (disposing)
			{
				CollectionView.Scrolled -= OnCollectionViewScrolled;
				Relayout -= OnRelayout;
				if (_observedItemsLayout is not null)
				{
					_observedItemsLayout.PropertyChanged -= OnItemsLayoutPropertyChanged;
					_observedItemsLayout = null;
				}
			}
			_disposed = true;
			base.Dispose(disposing);
		}
	}
}
