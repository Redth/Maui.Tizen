using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.NUI;
using Tizen.UIExtensions.NUI;

using NCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NLayoutParamPolicies = Tizen.NUI.BaseComponents.LayoutParamPolicies;
using NView = Tizen.NUI.BaseComponents.View;
using TItemSizingStrategy = Tizen.UIExtensions.NUI.ItemSizingStrategy;
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
		}

		public void UpdateLayoutManager()
		{
			// CarouselView uses a single-item-at-a-time layout
			// LinearItemsLayout.CarouselDefault is internal, so we create a horizontal linear layout
			var itemsLayout = Element.ItemsLayout ?? new LinearItemsLayout(ItemsLayoutOrientation.Horizontal);
			bool isHorizontal = itemsLayout is LinearItemsLayout linear && linear.Orientation == ItemsLayoutOrientation.Horizontal;

			CollectionView.LayoutManager = new LinearLayoutManager(
				isHorizontal,
				TItemSizingStrategy.MeasureAllItems,
				0);
		}

		public void UpdatePosition(int position)
		{
			if (position < 0 || CollectionView.Adaptor is null || position >= CollectionView.Adaptor.Count)
				return;

			CollectionView.ScrollTo(position, animate: false);
		}

		public void UpdateCurrentItem(object? currentItem)
		{
			if (currentItem == null || CollectionView.Adaptor == null)
				return;

			var index = CollectionView.Adaptor.GetItemIndex(currentItem);
			if (index >= 0)
			{
				CollectionView.ScrollTo(index, animate: false);
			}
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
			}
			_disposed = true;
			base.Dispose(disposing);
		}
	}
}
