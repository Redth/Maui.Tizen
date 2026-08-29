using System;
using Microsoft.Maui.Controls;
using Tizen.NUI;
using Tizen.UIExtensions.NUI;
using NCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using NLayoutParamPolicies = Tizen.NUI.BaseComponents.LayoutParamPolicies;
using NView = Tizen.NUI.BaseComponents.View;
using MauiCollectionView = Microsoft.Maui.Controls.CollectionView;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Platform view for <see cref="ItemsView"/> in the Tizen backend.
	/// </summary>
	public class TizenItemsViewControl<TItemsView> : NView where TItemsView : ItemsView
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
		public TizenStructuredItemsViewControl(TItemsView element) : base(element)
		{
		}

		public void UpdateLayoutManager(IItemsLayout itemsLayout)
		{
			CollectionView.LayoutManager = itemsLayout.ToLayoutManager(Element.ItemSizingStrategy);
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
			CollectionView.SelectionMode = Element.SelectionMode.ToNative();
		}

		public override void Rebind(TItemsView element)
		{
			base.Rebind(element);
			UpdateSelectionMode();
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
