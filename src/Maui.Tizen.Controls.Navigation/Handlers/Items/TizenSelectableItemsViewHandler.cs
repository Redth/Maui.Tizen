using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Adapters;
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
		public static IPropertyMapper<TItemsView, TizenSelectableItemsViewHandler<TItemsView>> SelectableItemsViewMapper =
			new PropertyMapper<TItemsView, TizenSelectableItemsViewHandler<TItemsView>>(StructuredItemsViewMapper)
			{
				[nameof(SelectableItemsView.SelectedItem)] = MapSelectedItem,
				[nameof(SelectableItemsView.SelectedItems)] = MapSelectedItems,
				[nameof(SelectableItemsView.SelectionMode)] = MapSelectionMode,
			};

		/// <summary>
		/// Command mapper for <see cref="SelectableItemsView"/> commands.
		/// </summary>
		public static CommandMapper<TItemsView, TizenSelectableItemsViewHandler<TItemsView>> SelectableItemsViewCommandMapper =
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

		readonly ItemSelectionSynchronizer _selection = new();

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

			// Group headers and footers share one flat index space with real items and must never
			// become selected. Rejecting them here - before anything reaches the virtual view -
			// deselects them natively too, so the two sides cannot disagree.
			if (Adaptor is ITizenSelectableItemFilter filter && NativeCollectionView is { } native)
			{
				var kept = _selection.RejectUnselectableIndexes(
					new TizenNativeCollectionSelection(native, Adaptor.Count),
					e.SelectedItems.Select(item => Adaptor.GetItemIndex(item)),
					filter);

				if (kept.Count != e.SelectedItems.Count)
				{
					return;
				}
			}

			// Guarded: writing the virtual view raises property changes that run the mappers, which
			// push straight back into the native view. Without recording the direction of travel
			// that echo re-enters here and recurses until the stack overflows.
			_selection.ApplyFromNative(() =>
			{
				switch (VirtualView.SelectionMode)
				{
					case SelectionMode.Single:
						VirtualView.SelectedItem = e.SelectedItems.FirstOrDefault();
						break;
					case SelectionMode.Multiple:
						// Assign rather than Clear()+Add(): clearing an observable collection that
						// the virtual view is watching raises a reset its own handlers react to.
						VirtualView.SelectedItems = e.SelectedItems.ToList();
						break;
					case SelectionMode.None:
						// Selection is disabled; nothing is propagated.
						break;
				}
			});
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

			// A null SelectedItem is a real instruction to clear the selection, so it must reach the
			// synchronizer as an empty set rather than being skipped.
			int index = VirtualView.SelectedItem is null ? -1 : Adaptor.GetItemIndex(VirtualView.SelectedItem);

			_selection.PushToNative(
				new TizenNativeCollectionSelection(collectionView, Adaptor.Count),
				index >= 0 ? new[] { index } : System.Array.Empty<int>(),
				Adaptor as ITizenSelectableItemFilter);
		}

		protected virtual void UpdateSelectedItems()
		{
			var collectionView = NativeCollectionView;
			if (collectionView == null || Adaptor == null)
				return;

			if (VirtualView.SelectionMode != SelectionMode.Multiple)
				return;

			// Full diff, not add-only. RequestItemUnselect exists on the native view, so items
			// dropped from the MAUI selection can actually be deselected instead of being left
			// selected natively forever.
			_selection.PushToNative(
				new TizenNativeCollectionSelection(collectionView, Adaptor.Count),
				(VirtualView.SelectedItems ?? (System.Collections.Generic.IList<object>)System.Array.Empty<object>())
					.Select(item => Adaptor.GetItemIndex(item)),
				Adaptor as ITizenSelectableItemFilter);
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
