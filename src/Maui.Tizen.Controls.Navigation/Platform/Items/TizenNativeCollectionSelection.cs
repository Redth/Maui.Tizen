using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using TCollectionView = Tizen.UIExtensions.NUI.CollectionView;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Adapts Tizen's native <c>CollectionView</c> to <see cref="ITizenNativeSelection"/>.
	/// </summary>
	/// <remarks>
	/// The synchronisation rules live in <see cref="ItemSelectionSynchronizer"/>, which is pure
	/// logic and therefore host-testable. This is the thin NUI-facing half that cannot be, and it is
	/// deliberately kept free of decisions so there is nothing here to test.
	/// </remarks>
	public sealed class TizenNativeCollectionSelection : ITizenNativeSelection
	{
		readonly TCollectionView _collectionView;

		public TizenNativeCollectionSelection(TCollectionView collectionView, int count)
		{
			_collectionView = collectionView;
			Count = count;
		}

		public int Count { get; }

		public IReadOnlyList<int> SelectedIndexes => _collectionView.SelectedItems?.ToList() ?? new List<int>();

		public void RequestItemSelect(int index) => _collectionView.RequestItemSelect(index);

		public void RequestItemUnselect(int index) => _collectionView.RequestItemUnselect(index);
	}
}
