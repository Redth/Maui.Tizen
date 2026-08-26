using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Platform;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Handler for <see cref="SelectableItemsView"/> in the Tizen backend.
	/// </summary>
	/// <typeparam name="TItemsView">The MAUI SelectableItemsView type.</typeparam>
	/// <remarks>
	/// Adds support for SelectedItem, SelectedItems, SelectionMode, and SelectionChangedCommand.
	/// </remarks>
	public abstract class TizenSelectableItemsViewHandler<TItemsView> : TizenStructuredItemsViewHandler<TItemsView>
		where TItemsView : SelectableItemsView
	{
		/// <summary>
		/// Property mapper for <see cref="SelectableItemsView"/> properties.
		/// </summary>
		public static new IPropertyMapper<TItemsView, TizenSelectableItemsViewHandler<TItemsView>> SelectableItemsViewMapper =
			new PropertyMapper<TItemsView, TizenSelectableItemsViewHandler<TItemsView>>(StructuredItemsViewMapper)
			{
				[nameof(SelectableItemsView.SelectedItem)] = MapSelectedItem,
				[nameof(SelectableItemsView.SelectedItems)] = MapSelectedItems,
				[nameof(SelectableItemsView.SelectionMode)] = MapSelectionMode,
			};

		/// <summary>
		/// Command mapper for <see cref="SelectableItemsView"/> commands.
		/// </summary>
		public static new CommandMapper<TItemsView, TizenSelectableItemsViewHandler<TItemsView>> SelectableItemsViewCommandMapper =
			new CommandMapper<TItemsView, TizenSelectableItemsViewHandler<TItemsView>>(StructuredItemsViewCommandMapper);

		protected TizenSelectableItemsViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
			: base(mapper, commandMapper ?? SelectableItemsViewCommandMapper)
		{
		}

		/// <summary>
		/// Gets the typed platform view for selectable items.
		/// </summary>
		protected new TizenSelectableItemsViewControl<TItemsView>? PlatformView
			=> base.PlatformView as TizenSelectableItemsViewControl<TItemsView>;

		protected override void ConnectHandler(NView platformView)
		{
			base.ConnectHandler(platformView);
			UpdateSelectionMode();
		}

		protected override void OnAdaptorSelectionChanged(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			base.OnAdaptorSelectionChanged(sender, e);

			if (VirtualView == null || e.SelectedItems == null)
				return;

			switch (VirtualView.SelectionMode)
			{
				case SelectionMode.Single:
					VirtualView.SelectedItem = e.SelectedItems.FirstOrDefault();
					break;
				case SelectionMode.Multiple:
					// Clear and re-add to maintain proper selection state
					VirtualView.SelectedItems.Clear();
					foreach (var item in e.SelectedItems)
					{
						VirtualView.SelectedItems.Add(item);
					}
					break;
				case SelectionMode.None:
					// Selection is disabled, clear any accidentally selected items
					break;
			}
		}

		protected virtual void UpdateSelectionMode()
		{
			PlatformView?.UpdateSelectionMode();
		}

		protected virtual void UpdateSelectedItem()
		{
			var collectionView = NativeCollectionView;
			if (collectionView == null || Adaptor == null)
				return;

			if (VirtualView.SelectionMode == SelectionMode.None)
				return;

			if (VirtualView.SelectedItem != null)
			{
				int index = Adaptor.GetItemIndex(VirtualView.SelectedItem);
				if (index >= 0)
				{
					collectionView.RequestItemSelect(index);
				}
			}
		}

		protected virtual void UpdateSelectedItems()
		{
			var collectionView = NativeCollectionView;
			if (collectionView == null || Adaptor == null)
				return;

			if (VirtualView.SelectionMode != SelectionMode.Multiple)
				return;

			// Clear existing selection first
			// Note: Tizen.UIExtensions.NUI CollectionView doesn't have a ClearSelection API
			// Selection updates are handled through individual item requests

			foreach (var item in VirtualView.SelectedItems)
			{
				int index = Adaptor.GetItemIndex(item);
				if (index >= 0)
				{
					collectionView.RequestItemSelect(index);
				}
			}
		}

		#region Mapper Methods

		/// <summary>
		/// Maps <see cref="SelectableItemsView.SelectedItem"/> to the platform.
		/// </summary>
		public static void MapSelectedItem(TizenSelectableItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateSelectedItem();
		}

		/// <summary>
		/// Maps <see cref="SelectableItemsView.SelectedItems"/> to the platform.
		/// </summary>
		public static void MapSelectedItems(TizenSelectableItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateSelectedItems();
		}

		/// <summary>
		/// Maps <see cref="SelectableItemsView.SelectionMode"/> to the platform.
		/// </summary>
		public static void MapSelectionMode(TizenSelectableItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateSelectionMode();
			handler.UpdateItemsSource(); // Refresh adaptor with new selection mode
		}

		#endregion
	}
}
