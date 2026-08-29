using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>
	/// The native selection surface a <see cref="ItemSelectionSynchronizer"/> drives.
	/// </summary>
	/// <remarks>
	/// Tizen's <c>CollectionView</c> exposes exactly these operations. Naming them as an interface
	/// keeps the synchronisation rules - which are pure logic and where the defects were - separable
	/// from NUI, so they can be executed in a host test instead of only on a device.
	/// </remarks>
	internal interface ITizenNativeSelection
	{
		/// <summary>Indexes the native view currently considers selected.</summary>
		IReadOnlyList<int> SelectedIndexes { get; }

		/// <summary>Number of items the native view is showing.</summary>
		int Count { get; }

		/// <summary>Asks the native view to select <paramref name="index"/>.</summary>
		void RequestItemSelect(int index);

		/// <summary>Asks the native view to deselect <paramref name="index"/>.</summary>
		void RequestItemUnselect(int index);
	}

	/// <summary>
	/// Declares which item positions may be selected.
	/// </summary>
	/// <remarks>
	/// Grouped sources interleave group headers and footers with real items in one flat index space,
	/// and those rows must never become selected. Filtering them out of the selection path is the
	/// only point that works: rejecting them after the native selection has already changed leaves
	/// the header visibly highlighted and the native view holding a selection the virtual view does
	/// not agree with.
	/// </remarks>
	internal interface ITizenSelectableItemFilter
	{
		/// <summary>Gets whether the item at <paramref name="index"/> may be selected.</summary>
		bool IsItemSelectableAt(int index);
	}

	/// <summary>
	/// Keeps a <c>SelectableItemsView</c>'s selection and its native selection in step.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two defects motivated this type, and both are structural rather than incidental.
	/// </para>
	/// <para>
	/// <b>Unguarded feedback.</b> A native selection change wrote
	/// <c>VirtualView.SelectedItem</c>, which raised a property change, which ran the mapper, which
	/// called back into the native view to select the same item, which raised the native event
	/// again. Nothing stopped that cycle, so it either re-entered until the stack overflowed or - in
	/// the multiple-selection case - churned the collection while enumerating it. The direction of
	/// travel has to be recorded so the echo can be dropped, which is what upstream's
	/// <c>_updateSelection</c> / <c>_updateFromUI</c> pair does.
	/// </para>
	/// <para>
	/// <b>Add-only synchronisation.</b> The previous code walked <c>SelectedItems</c> and requested a
	/// select for each one. Nothing was ever deselected, so removing an item from the MAUI selection
	/// left it selected natively, clearing the selection entirely was invisible, and assigning
	/// <c>SelectedItem = null</c> did nothing at all. Selection is a set, so it needs a set
	/// difference in both directions.
	/// </para>
	/// </remarks>
	internal sealed class ItemSelectionSynchronizer
	{
		bool _pushingToNative;
		bool _applyingFromNative;

		/// <summary>
		/// Gets whether a native-originated update is currently being applied.
		/// </summary>
		public bool IsApplyingFromNative => _applyingFromNative;

		/// <summary>
		/// Gets whether a MAUI-originated update is currently being pushed to the native view.
		/// </summary>
		public bool IsPushingToNative => _pushingToNative;

		/// <summary>
		/// Pushes the MAUI selection onto <paramref name="native"/> as a set difference.
		/// </summary>
		/// <param name="native">The native selection surface.</param>
		/// <param name="selectedIndexes">
		/// Indexes that should end up selected. Empty clears the selection; an index of -1 (an item
		/// the adaptor could not resolve) is ignored rather than passed through.
		/// </param>
		/// <returns>
		/// <see langword="false"/> if the push was suppressed because a native-originated update is
		/// already in flight - that echo is exactly what would otherwise recurse.
		/// </returns>
		public bool PushToNative(
			ITizenNativeSelection native,
			IEnumerable<int> selectedIndexes,
			ITizenSelectableItemFilter? filter = null)
		{
			if (native is null || _applyingFromNative)
			{
				return false;
			}

			_pushingToNative = true;

			try
			{
				HashSet<int> wanted = selectedIndexes is null
					? new HashSet<int>()
					: selectedIndexes
						.Where(i => i >= 0 && i < native.Count)
						.Where(i => filter?.IsItemSelectableAt(i) != false)
						.ToHashSet();

				// Snapshot first: the native list mutates as requests are applied, and enumerating it
				// while doing so is undefined.
				List<int> current = native.SelectedIndexes.ToList();

				foreach (int index in current)
				{
					// A stale index can outlive the item it referred to when the source shrinks.
					if (index < 0 || index >= native.Count)
					{
						continue;
					}

					if (!wanted.Contains(index))
					{
						native.RequestItemUnselect(index);
					}
				}

				HashSet<int> alreadySelected = current.ToHashSet();

				foreach (int index in wanted)
				{
					if (!alreadySelected.Contains(index))
					{
						native.RequestItemSelect(index);
					}
				}

				return true;
			}
			finally
			{
				_pushingToNative = false;
			}
		}

		/// <summary>
		/// Removes any position the filter rejects from a native-originated selection, deselecting it
		/// natively so the two sides do not disagree. When <paramref name="previousValidIndex"/> is
		/// provided and all selected indexes are rejected, restores that previous valid selection so
		/// a <c>SingleAlways</c> collection is never left with nothing selected.
		/// </summary>
		/// <returns>The indexes that survived the filter.</returns>
		/// <remarks>
		/// <para>
		/// A user can tap a group header, so the rejection has to happen on this side too - not only
		/// when pushing a selection down. Deselecting immediately keeps the native view from holding
		/// a highlighted row the virtual view never accepted.
		/// </para>
		/// <para>
		/// <b>SingleAlways preservation.</b> The native API (<c>Tizen.UIExtensions.NUI.CollectionView</c>)
		/// does not expose a pre-selection hook that allows rejecting items before the selection mutates.
		/// Once the user taps a group header, the native selection has already changed. Without the
		/// previous valid index, rejecting the header leaves the collection with nothing selected,
		/// violating the <c>SingleAlways</c> contract. Passing the previous valid index allows this
		/// method to restore it when all tapped items are rejected.
		/// </para>
		/// </remarks>
		public IReadOnlyList<int> RejectUnselectableIndexes(
			ITizenNativeSelection native,
			IEnumerable<int> selectedIndexes,
			ITizenSelectableItemFilter? filter,
			int? previousValidIndex = null)
		{
			if (native is null || selectedIndexes is null)
			{
				return System.Array.Empty<int>();
			}

			List<int> kept = new();

			foreach (int index in selectedIndexes.ToList())
			{
				if (filter?.IsItemSelectableAt(index) == false)
				{
					_pushingToNative = true;

					try
					{
						native.RequestItemUnselect(index);
					}
					finally
					{
						_pushingToNative = false;
					}

					continue;
				}

				kept.Add(index);
			}

			// If all items were rejected and we have a previous valid index, restore it.
			// This ensures SingleAlways mode is never left with no selection.
			if (kept.Count == 0 && previousValidIndex.HasValue && previousValidIndex.Value >= 0)
			{
				int prevIdx = previousValidIndex.Value;
				// Validate the previous index is still in range and selectable
				if (prevIdx < native.Count && (filter?.IsItemSelectableAt(prevIdx) != false))
				{
					_pushingToNative = true;

					try
					{
						native.RequestItemSelect(prevIdx);
					}
					finally
					{
						_pushingToNative = false;
					}

					kept.Add(prevIdx);
				}
			}

			return kept;
		}

		/// <summary>
		/// Runs <paramref name="apply"/> as a native-originated update, unless one is already being
		/// pushed the other way.
		/// </summary>
		/// <returns>
		/// <see langword="false"/> if the update was suppressed because it is the echo of a push this
		/// synchronizer just made.
		/// </returns>
		public bool ApplyFromNative(System.Action apply)
		{
			if (apply is null || _pushingToNative)
			{
				return false;
			}

			_applyingFromNative = true;

			try
			{
				apply();
				return true;
			}
			finally
			{
				_applyingFromNative = false;
			}
		}
	}
}
