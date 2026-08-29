using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Platform;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Concrete handler for <see cref="CollectionView"/> in the Tizen backend.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This handler provides full support for CollectionView functionality including:
	/// items source binding, item templates, selection (single/multiple/none),
	/// grouping with headers/footers, linear and grid layouts, header/footer views,
	/// empty views, scroll bar visibility, remaining items threshold, and vertical/horizontal orientations.
	/// </para>
	/// <para>
	/// Unsupported features: CanReorderItems (drag-and-drop reordering is not available on Tizen).
	/// </para>
	/// </remarks>
	public class TizenCollectionViewHandler : TizenReorderableItemsViewHandler<CollectionView>
	{
		/// <summary>
		/// Property mapper for <see cref="CollectionView"/>.
		/// </summary>
		public static IPropertyMapper<CollectionView, TizenCollectionViewHandler> CollectionViewMapper =
			new PropertyMapper<CollectionView, TizenCollectionViewHandler>(ReorderableItemsViewMapper);

		/// <summary>
		/// Command mapper for <see cref="CollectionView"/> commands.
		/// </summary>
		public static CommandMapper<CollectionView, TizenCollectionViewHandler> CollectionViewCommandMapper =
			new CommandMapper<CollectionView, TizenCollectionViewHandler>(ReorderableItemsViewCommandMapper);

		/// <summary>
		/// Initializes a new instance of <see cref="TizenCollectionViewHandler"/> using default mappers.
		/// </summary>
		public TizenCollectionViewHandler()
			: base(CollectionViewMapper, CollectionViewCommandMapper)
		{
		}

		/// <summary>
		/// Initializes a new instance of <see cref="TizenCollectionViewHandler"/> with custom mappers.
		/// </summary>
		/// <param name="mapper">The property mapper.</param>
		/// <param name="commandMapper">Optional command mapper.</param>
		public TizenCollectionViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
			: base(mapper, commandMapper)
		{
		}

		/// <summary>
		/// Creates the platform view for the CollectionView.
		/// </summary>
		/// <returns>A <see cref="TizenCollectionViewControl"/> instance.</returns>
		protected override NView CreatePlatformView()
		{
			return new TizenCollectionViewControl(VirtualView);
		}
	}
}
