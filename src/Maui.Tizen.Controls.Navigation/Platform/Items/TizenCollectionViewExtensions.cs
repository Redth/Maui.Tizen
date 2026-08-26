using Microsoft.Maui.Controls;
using Tizen.UIExtensions.NUI;

using TCollectionViewSelectionMode = Tizen.UIExtensions.NUI.CollectionViewSelectionMode;
using TItemSizingStrategy = Tizen.UIExtensions.NUI.ItemSizingStrategy;
using MauiItemSizingStrategy = Microsoft.Maui.Controls.ItemSizingStrategy;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Extension methods for CollectionView selection mode and layout manager conversions.
	/// </summary>
	public static class TizenCollectionViewExtensions
	{
		/// <summary>
		/// Converts a MAUI <see cref="SelectionMode"/> to a Tizen <see cref="TCollectionViewSelectionMode"/>.
		/// </summary>
		public static TCollectionViewSelectionMode ToNative(this SelectionMode selectionMode)
		{
			return selectionMode switch
			{
				SelectionMode.Multiple => TCollectionViewSelectionMode.Multiple,
				SelectionMode.Single => TCollectionViewSelectionMode.SingleAlways,
				_ => TCollectionViewSelectionMode.None,
			};
		}

		/// <summary>
		/// Converts a MAUI <see cref="IItemsLayout"/> to a Tizen <see cref="ICollectionViewLayoutManager"/>.
		/// </summary>
		public static ICollectionViewLayoutManager ToLayoutManager(this IItemsLayout layout, MauiItemSizingStrategy sizing = MauiItemSizingStrategy.MeasureFirstItem)
		{
			return layout switch
			{
				LinearItemsLayout listItemsLayout => new LinearLayoutManager(
					listItemsLayout.Orientation == ItemsLayoutOrientation.Horizontal,
					(TItemSizingStrategy)sizing,
					(int)listItemsLayout.ItemSpacing.ToScaledPixel()),

				GridItemsLayout gridItemsLayout => new GridLayoutManager(
					gridItemsLayout.Orientation == ItemsLayoutOrientation.Horizontal,
					gridItemsLayout.Span,
					(TItemSizingStrategy)sizing,
					(int)gridItemsLayout.VerticalItemSpacing.ToScaledPixel(),
					(int)gridItemsLayout.HorizontalItemSpacing.ToScaledPixel()),

				_ => new LinearLayoutManager(false),
			};
		}
	}
}
