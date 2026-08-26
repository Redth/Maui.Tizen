using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Handler for <see cref="ReorderableItemsView"/> in the Tizen backend.
	/// </summary>
	/// <typeparam name="TItemsView">The MAUI ReorderableItemsView type.</typeparam>
	/// <remarks>
	/// <para>
	/// Adds support for CanReorderItems property.
	/// </para>
	/// <para>
	/// Unsupported: Tizen.UIExtensions.NUI.CollectionView does not support drag-and-drop reordering.
	/// The mapper is declared for API completeness but performs no operation.
	/// </para>
	/// </remarks>
	public abstract class TizenReorderableItemsViewHandler<TItemsView> : TizenGroupableItemsViewHandler<TItemsView>
		where TItemsView : ReorderableItemsView
	{
		/// <summary>
		/// Property mapper for <see cref="ReorderableItemsView"/> properties.
		/// </summary>
		public static IPropertyMapper<TItemsView, TizenReorderableItemsViewHandler<TItemsView>> ReorderableItemsViewMapper =
			new PropertyMapper<TItemsView, TizenReorderableItemsViewHandler<TItemsView>>(GroupableItemsViewMapper)
			{
				[nameof(ReorderableItemsView.CanReorderItems)] = MapCanReorderItems,
			};

		/// <summary>
		/// Command mapper for <see cref="ReorderableItemsView"/> commands.
		/// </summary>
		public static CommandMapper<TItemsView, TizenReorderableItemsViewHandler<TItemsView>> ReorderableItemsViewCommandMapper =
			new CommandMapper<TItemsView, TizenReorderableItemsViewHandler<TItemsView>>(GroupableItemsViewCommandMapper);

		protected TizenReorderableItemsViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
			: base(mapper, commandMapper ?? ReorderableItemsViewCommandMapper)
		{
		}

		#region Mapper Methods

		/// <summary>
		/// Unsupported: CanReorderItems is not supported on Tizen.
		/// </summary>
		/// <remarks>
		/// Tizen.UIExtensions.NUI.CollectionView does not currently support drag-and-drop reordering
		/// of items. This mapper is declared for API completeness but performs no operation.
		/// </remarks>
		public static void MapCanReorderItems(TizenReorderableItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			// No-op: Tizen CollectionView does not support item reordering
		}

		#endregion
	}
}
