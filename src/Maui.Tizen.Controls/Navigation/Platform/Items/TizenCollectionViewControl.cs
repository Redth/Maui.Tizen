using System;
using System.ComponentModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.NUI;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;
using IMeasurable = Tizen.UIExtensions.Common.IMeasurable;
using NCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NLayoutParamPolicies = Tizen.NUI.BaseComponents.LayoutParamPolicies;
using NView = Tizen.NUI.BaseComponents.View;
using TCollectionViewSelectionMode = Tizen.UIExtensions.NUI.CollectionViewSelectionMode;
using TSnapPointsAlignment = Tizen.UIExtensions.NUI.SnapPointsAlignment;
using TSnapPointsType = Tizen.UIExtensions.NUI.SnapPointsType;
using TSize = Tizen.UIExtensions.Common.Size;
using MauiCollectionView = Microsoft.Maui.Controls.CollectionView;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Platform view for <see cref="ItemsView"/> in the Tizen backend.
	/// </summary>
	public class TizenItemsViewControl<TItemsView> : NView, IMeasurable where TItemsView : ItemsView
	{
		bool _disposed;

		public TizenItemsViewControl(TItemsView element)
		{
			Element = element;
			CollectionView = CreateCollectionView();
			Initialize();
		}

		public TItemsView Element { get; protected set; }

		public NCollectionView CollectionView { get; protected set; }

		protected virtual NCollectionView CreateCollectionView()
		{
			return new NCollectionView
			{
				WidthSpecification = NLayoutParamPolicies.MatchParent,
				HeightSpecification = NLayoutParamPolicies.MatchParent,
			};
		}

		protected virtual void Initialize()
		{
			Layout = new LinearLayout();
			WidthSpecification = NLayoutParamPolicies.MatchParent;
			HeightSpecification = NLayoutParamPolicies.MatchParent;
			Add(CollectionView);
		}

		public virtual void Rebind(TItemsView element)
		{
			ArgumentNullException.ThrowIfNull(element);
			Element = element;
		}

		/// <summary>Measures the items surface using finite viewport and scroll-canvas bounds.</summary>
		public TSize Measure(double availableWidth, double availableHeight)
		{
			var allocated = ((NView)CollectionView).Size.ToCommon();
			var hasNativeLayout = CollectionView.Adaptor is not null
				&& CollectionView.LayoutManager is not null
				&& allocated.Width > 0
				&& allocated.Height > 0;
			var canvas = hasNativeLayout
				? CollectionView.LayoutManager!.GetScrollCanvasSize()
				: TSize.Zero;
			var display = Devices.DeviceDisplay.MainDisplayInfo;
			var measured = ItemsViewMeasure.Resolve(
				availableWidth,
				availableHeight,
				allocated.Width,
				allocated.Height,
				canvas.Width,
				canvas.Height,
				display.Width,
				display.Height,
				hasNativeLayout,
				CollectionView.LayoutManager?.IsHorizontal == true);

			return new TSize((float)measured.Width, (float)measured.Height);
		}

		protected override void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			if (disposing)
			{
				var adaptor = CollectionView.Adaptor;
				CollectionView.Adaptor = null;
				adaptor?.Dispose();
				CollectionView.Dispose();
			}
			_disposed = true;
			base.Dispose(disposing);
		}
	}

	/// <summary>
	/// Platform view for <see cref="StructuredItemsView"/> in the Tizen backend.
	/// </summary>
	public class TizenStructuredItemsViewControl<TItemsView> : TizenItemsViewControl<TItemsView>
		where TItemsView : StructuredItemsView
	{
		IItemsLayout? _observedItemsLayout;

		public TizenStructuredItemsViewControl(TItemsView element) : base(element)
		{
		}

		public void UpdateLayoutManager(IItemsLayout itemsLayout)
		{
			if (!ReferenceEquals(_observedItemsLayout, itemsLayout))
			{
				if (_observedItemsLayout is not null)
					_observedItemsLayout.PropertyChanged -= OnItemsLayoutPropertyChanged;

				_observedItemsLayout = itemsLayout;
				_observedItemsLayout.PropertyChanged += OnItemsLayoutPropertyChanged;
			}

			CollectionView.LayoutManager = itemsLayout.ToLayoutManager(
				Element.ItemSizingStrategy,
				forceSingleSpan: CollectionView.Adaptor is ITizenLogicalItemAdaptor { LogicalCount: 0 });
			var state = ItemsLayoutSnapshot.Capture(itemsLayout);
			CollectionView.SnapPointsType = (TSnapPointsType)state.SnapPointsType;
			CollectionView.SnapPointsAlignment = (TSnapPointsAlignment)state.SnapPointsAlignment;
			CollectionView.ScrollView.HideScrollbar = CollectionView.LayoutManager.IsHorizontal
				? Element.HorizontalScrollBarVisibility == ScrollBarVisibility.Never
				: Element.VerticalScrollBarVisibility == ScrollBarVisibility.Never;
		}

		public override void Rebind(TItemsView element)
		{
			base.Rebind(element);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _observedItemsLayout is not null)
			{
				_observedItemsLayout.PropertyChanged -= OnItemsLayoutPropertyChanged;
				_observedItemsLayout = null;
			}

			base.Dispose(disposing);
		}

		void OnItemsLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (ReferenceEquals(sender, _observedItemsLayout) && _observedItemsLayout is not null)
				UpdateLayoutManager(_observedItemsLayout);
		}
	}

	/// <summary>
	/// Platform view for <see cref="SelectableItemsView"/> in the Tizen backend.
	/// </summary>
	public class TizenSelectableItemsViewControl<TItemsView> : TizenStructuredItemsViewControl<TItemsView>
		where TItemsView : SelectableItemsView
	{
		public TizenSelectableItemsViewControl(TItemsView element) : base(element)
		{
		}

		protected override void Initialize()
		{
			base.Initialize();
			UpdateSelectionMode();
		}

		public void UpdateSelectionMode()
		{
			CollectionView.SelectionMode = CollectionView.Adaptor is TizenEmptyItemAdaptor
				? TCollectionViewSelectionMode.None
				: Element.SelectionMode.ToNative();
		}

		public override void Rebind(TItemsView element)
		{
			base.Rebind(element);
		}
	}

	/// <summary>
	/// Platform view for <see cref="GroupableItemsView"/> in the Tizen backend.
	/// </summary>
	public class TizenGroupableItemsViewControl<TItemsView> : TizenSelectableItemsViewControl<TItemsView>
		where TItemsView : GroupableItemsView
	{
		public TizenGroupableItemsViewControl(TItemsView element) : base(element)
		{
		}
	}

	/// <summary>
	/// Platform view for <see cref="MauiCollectionView"/> in the Tizen backend.
	/// </summary>
	public class TizenCollectionViewControl : TizenGroupableItemsViewControl<MauiCollectionView>
	{
		public TizenCollectionViewControl(MauiCollectionView element) : base(element)
		{
		}
	}
}
