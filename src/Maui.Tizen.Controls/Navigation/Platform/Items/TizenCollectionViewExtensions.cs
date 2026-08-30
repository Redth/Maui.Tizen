using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;

using TCollectionViewSelectionMode = Tizen.UIExtensions.NUI.CollectionViewSelectionMode;
using TItemSizingStrategy = Tizen.UIExtensions.NUI.ItemSizingStrategy;
using MauiItemSizingStrategy = Microsoft.Maui.Controls.ItemSizingStrategy;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Extension methods for CollectionView selection mode and layout manager conversions.
	/// </summary>
	internal static class TizenCollectionViewExtensions
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
		public static ICollectionViewLayoutManager ToLayoutManager(
			this IItemsLayout layout,
			MauiItemSizingStrategy sizing = MauiItemSizingStrategy.MeasureFirstItem,
			bool forceSingleSpan = false)
		{
			var state = ItemsLayoutSnapshot.Capture(layout);
			return layout switch
			{
				LinearItemsLayout => new LinearLayoutManager(
					state.IsHorizontal,
					(TItemSizingStrategy)sizing,
					(int)state.ItemSpacing.ToScaledPixel()),

				GridItemsLayout => new GridLayoutManager(
					state.IsHorizontal,
					state.EffectiveSpan(forceSingleSpan),
					(TItemSizingStrategy)sizing,
					(int)state.VerticalItemSpacing.ToScaledPixel(),
					(int)state.HorizontalItemSpacing.ToScaledPixel()),

				_ => new LinearLayoutManager(false),
			};
		}
	}
}
