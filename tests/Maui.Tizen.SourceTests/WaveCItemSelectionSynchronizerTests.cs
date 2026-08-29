using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Executable tests for selection synchronisation between MAUI and the native collection view.
/// </summary>
/// <remarks>
/// The synchronisation rules are pure logic behind <see cref="ITizenNativeSelection"/>, so they run
/// here against a fake rather than only on a device. The NUI half
/// (<c>TizenNativeCollectionSelection</c>) is a thin forwarder with no decisions in it and stays
/// device-only.
/// </remarks>
public class WaveCItemSelectionSynchronizerTests
{
	/// <summary>
	/// A stand-in for the native collection view that records what was asked of it.
	/// </summary>
	sealed class FakeNativeSelection : ITizenNativeSelection
	{
		readonly List<int> _selected = new();

		public FakeNativeSelection(int count, params int[] initiallySelected)
		{
			Count = count;
			_selected.AddRange(initiallySelected);
		}

		public int Count { get; }

		public IReadOnlyList<int> SelectedIndexes => _selected;

		public List<string> Operations { get; } = new();

		public void RequestItemSelect(int index)
		{
			Operations.Add($"select:{index}");

			if (!_selected.Contains(index))
			{
				_selected.Add(index);
			}
		}

		public void RequestItemUnselect(int index)
		{
			Operations.Add($"unselect:{index}");
			_selected.Remove(index);
		}
	}

	sealed class HeaderFooterFilter : ITizenSelectableItemFilter
	{
		readonly HashSet<int> _unselectable;

		public HeaderFooterFilter(params int[] unselectable) => _unselectable = unselectable.ToHashSet();

		public bool IsItemSelectableAt(int index) => !_unselectable.Contains(index);
	}

	// -----------------------------------------------------------------
	// Diff synchronisation
	// -----------------------------------------------------------------

	[Fact]
	public void SelectingAnItemSelectsItNatively()
	{
		var native = new FakeNativeSelection(count: 5);

		new ItemSelectionSynchronizer().PushToNative(native, new[] { 2 });

		Assert.Equal(new[] { 2 }, native.SelectedIndexes);
	}

	/// <summary>
	/// The regression: synchronisation used to be add-only, so nothing was ever deselected.
	/// </summary>
	[Fact]
	public void RemovingAnItemFromTheSelectionDeselectsItNatively()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: new[] { 1, 3 });

		new ItemSelectionSynchronizer().PushToNative(native, new[] { 1 });

		Assert.Equal(new[] { 1 }, native.SelectedIndexes);
		Assert.Contains("unselect:3", native.Operations);
	}

	/// <summary>
	/// Clearing the selection entirely has to reach the native view.
	/// </summary>
	[Fact]
	public void AnEmptySelectionDeselectsEverything()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: new[] { 0, 2, 4 });

		new ItemSelectionSynchronizer().PushToNative(native, System.Array.Empty<int>());

		Assert.Empty(native.SelectedIndexes);
	}

	/// <summary>
	/// <c>SelectedItem = null</c> reaches the synchronizer as an unresolved index, and must clear
	/// rather than be ignored.
	/// </summary>
	[Fact]
	public void AnUnresolvedIndexClearsTheSelection()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: new[] { 2 });

		// -1 is what GetItemIndex returns for an item that is not in the source.
		new ItemSelectionSynchronizer().PushToNative(native, new[] { -1 });

		Assert.Empty(native.SelectedIndexes);
	}

	/// <summary>
	/// Already-correct selections must not be re-requested; a redundant request re-raises the
	/// native event for no reason.
	/// </summary>
	[Fact]
	public void AnUnchangedSelectionIssuesNoRequests()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: new[] { 1, 2 });

		new ItemSelectionSynchronizer().PushToNative(native, new[] { 1, 2 });

		Assert.Empty(native.Operations);
	}

	/// <summary>
	/// A multi-selection change is a set difference, not a clear-and-re-add.
	/// </summary>
	[Fact]
	public void MultipleSelectionSyncsAsASetDifference()
	{
		var native = new FakeNativeSelection(count: 6, initiallySelected: new[] { 0, 1, 2 });

		new ItemSelectionSynchronizer().PushToNative(native, new[] { 1, 2, 5 });

		Assert.Equal(new[] { 1, 2, 5 }, native.SelectedIndexes.OrderBy(i => i));

		// Only the difference is touched.
		Assert.Contains("unselect:0", native.Operations);
		Assert.Contains("select:5", native.Operations);
		Assert.DoesNotContain("select:1", native.Operations);
	}

	/// <summary>
	/// A stale index left over from a shrunken source must not be forwarded.
	/// </summary>
	[Fact]
	public void OutOfRangeIndexesAreIgnored()
	{
		var native = new FakeNativeSelection(count: 3);

		new ItemSelectionSynchronizer().PushToNative(native, new[] { 7 });

		Assert.Empty(native.SelectedIndexes);
	}

	[Fact]
	public void StaleNativeIndexesAreExplicitlyClearedAfterTheSourceShrinks()
	{
		var native = new FakeNativeSelection(count: 3, initiallySelected: new[] { 4 });

		new ItemSelectionSynchronizer().PushToNative(native, Array.Empty<int>());

		Assert.Empty(native.SelectedIndexes);
		Assert.Contains("unselect:4", native.Operations);
	}

	// -----------------------------------------------------------------
	// Re-entrancy guards
	// -----------------------------------------------------------------

	/// <summary>
	/// The regression: a native change wrote the virtual view, whose property change pushed straight
	/// back into the native view, which raised the native change again - unbounded recursion.
	/// </summary>
	[Fact]
	public void APushTriggeredWhileApplyingFromNativeIsSuppressed()
	{
		var sync = new ItemSelectionSynchronizer();
		var native = new FakeNativeSelection(count: 5);
		bool pushed = true;

		sync.ApplyFromNative(() =>
		{
			// This is the echo: the mapper firing as a result of writing the virtual view.
			pushed = sync.PushToNative(native, new[] { 1 });
		});

		Assert.False(pushed);
		Assert.Empty(native.Operations);
	}

	/// <summary>
	/// The mirror direction: the native event raised by our own push must not be applied back.
	/// </summary>
	[Fact]
	public void ApplyingFromNativeWhilePushingIsSuppressed()
	{
		var sync = new ItemSelectionSynchronizer();
		var applied = true;

		var native = new ReentrantNative(() => applied = sync.ApplyFromNative(() => { }));

		sync.PushToNative(native, new[] { 1 });

		Assert.False(applied);
	}

	[Fact]
	public void NativeFeedbackIsSuppressedWhileSelectionModeAndAdaptorAreConfigured()
	{
		var sync = new ItemSelectionSynchronizer();
		var applied = true;

		sync.SuppressNativeFeedback(() =>
			applied = sync.ApplyFromNative(() => { }));

		Assert.False(applied);
		Assert.False(sync.IsPushingToNative);
	}

	/// <summary>
	/// A native view that re-raises its selection event synchronously from a request.
	/// </summary>
	sealed class ReentrantNative : ITizenNativeSelection
	{
		readonly System.Action _onRequest;

		public ReentrantNative(System.Action onRequest) => _onRequest = onRequest;

		public int Count => 5;

		public IReadOnlyList<int> SelectedIndexes => System.Array.Empty<int>();

		public void RequestItemSelect(int index) => _onRequest();

		public void RequestItemUnselect(int index) => _onRequest();
	}

	/// <summary>
	/// Guards must not latch: after a cycle completes, both directions work again.
	/// </summary>
	[Fact]
	public void GuardsAreReleasedAfterEachCycle()
	{
		var sync = new ItemSelectionSynchronizer();
		var native = new FakeNativeSelection(count: 5);

		sync.ApplyFromNative(() => { });

		Assert.False(sync.IsApplyingFromNative);
		Assert.False(sync.IsPushingToNative);
		Assert.True(sync.PushToNative(native, new[] { 1 }));
	}

	// -----------------------------------------------------------------
	// Group header / footer filtering
	// -----------------------------------------------------------------

	/// <summary>
	/// A group header must never be selected, even if something asks for it.
	/// </summary>
	[Fact]
	public void AnUnselectableIndexIsNeverPushed()
	{
		var native = new FakeNativeSelection(count: 5);

		// 0 is a group header.
		new ItemSelectionSynchronizer().PushToNative(native, new[] { 0, 2 }, new HeaderFooterFilter(0));

		Assert.Equal(new[] { 2 }, native.SelectedIndexes);
	}

	/// <summary>
	/// A user tapping a group header must be rejected AND deselected natively.
	/// </summary>
	/// <remarks>
	/// The filter existed before this change but was never called, so a header tap highlighted the
	/// header and propagated to the virtual view. Rejecting after the fact is too late: the native
	/// view is already holding a selection the virtual view does not agree with.
	/// </remarks>
	[Fact]
	public void AnUnselectableIndexSelectedNativelyIsRejectedAndDeselected()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: new[] { 0 });

		var kept = new ItemSelectionSynchronizer()
			.RejectUnselectableIndexes(native, new[] { 0 }, new HeaderFooterFilter(0));

		Assert.Empty(kept);
		Assert.Contains("unselect:0", native.Operations);
		Assert.Empty(native.SelectedIndexes);
	}

	[Fact]
	public void SelectableIndexesSurviveTheFilter()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: new[] { 3 });

		var kept = new ItemSelectionSynchronizer()
			.RejectUnselectableIndexes(native, new[] { 3 }, new HeaderFooterFilter(0));

		Assert.Equal(new[] { 3 }, kept);
		Assert.Empty(native.Operations);
	}

	/// <summary>
	/// With no filter supplied every index is selectable, so ungrouped sources are unaffected.
	/// </summary>
	[Fact]
	public void WithoutAFilterEveryIndexIsSelectable()
	{
		var native = new FakeNativeSelection(count: 5);

		new ItemSelectionSynchronizer().PushToNative(native, new[] { 0, 1 }, filter: null);

		Assert.Equal(new[] { 0, 1 }, native.SelectedIndexes.OrderBy(i => i));
	}

	// -----------------------------------------------------------------
	// SingleAlways preservation (Blocker B fix)
	// -----------------------------------------------------------------

	/// <summary>
	/// When all selected items are rejected and a previous valid index is provided, restore it.
	/// This is required for SingleAlways mode where the collection must never be left unselected.
	/// </summary>
	/// <remarks>
	/// The native Tizen.UIExtensions.NUI.CollectionView does not support pre-emption of selection,
	/// so when a user taps a group header the native selection has already changed. Without restoring
	/// the previous valid selection, the collection is left with nothing selected, violating the
	/// SingleAlways contract.
	/// </remarks>
	[Fact]
	public void WhenAllItemsRejectedWithPreviousValidIndex_RestoresIt()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: new[] { 0 }); // user tapped header at 0

		var kept = new ItemSelectionSynchronizer()
			.RejectUnselectableIndexes(native, new[] { 0 }, new HeaderFooterFilter(0), previousValidIndex: 2);

		// Header should be deselected
		Assert.Contains("unselect:0", native.Operations);
		// Previous valid index should be restored
		Assert.Contains("select:2", native.Operations);
		Assert.Equal(new[] { 2 }, kept);
		Assert.Equal(new[] { 2 }, native.SelectedIndexes);
	}

	/// <summary>
	/// When some items survive rejection, the previous valid index is not used.
	/// </summary>
	[Fact]
	public void WhenSomeItemsSurvive_DoesNotRestorePreviousIndex()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: new[] { 0, 2 });

		var kept = new ItemSelectionSynchronizer()
			.RejectUnselectableIndexes(native, new[] { 0, 2 }, new HeaderFooterFilter(0), previousValidIndex: 4);

		Assert.Contains("unselect:0", native.Operations);
		Assert.DoesNotContain("select:4", native.Operations);
		Assert.Equal(new[] { 2 }, kept);
	}

	/// <summary>
	/// A null previous valid index does not restore anything.
	/// </summary>
	[Fact]
	public void WhenAllItemsRejectedWithNullPreviousIndex_NoRestore()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: new[] { 0 });

		var kept = new ItemSelectionSynchronizer()
			.RejectUnselectableIndexes(native, new[] { 0 }, new HeaderFooterFilter(0), previousValidIndex: null);

		Assert.Empty(kept);
		Assert.Contains("unselect:0", native.Operations);
	}

	[Fact]
	public void EmptyNativeSelectionDoesNotRestoreWithoutAnInvalidRejection()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: Array.Empty<int>());

		var kept = new ItemSelectionSynchronizer()
			.RejectUnselectableIndexes(native, Array.Empty<int>(), new HeaderFooterFilter(0), previousValidIndex: 2);

		Assert.Empty(kept);
		Assert.DoesNotContain("select:2", native.Operations);
		Assert.DoesNotContain("select:", string.Join(",", native.Operations.Where(o => o.StartsWith("select:"))));
	}

	/// <summary>
	/// A negative previous valid index is ignored (represents "no previous selection").
	/// </summary>
	[Fact]
	public void WhenPreviousValidIndexIsNegative_DoesNotRestore()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: new[] { 0 });

		var kept = new ItemSelectionSynchronizer()
			.RejectUnselectableIndexes(native, new[] { 0 }, new HeaderFooterFilter(0), previousValidIndex: -1);

		Assert.Empty(kept);
	}

	/// <summary>
	/// If the previous valid index is itself unselectable (e.g., it was a group header that was replaced),
	/// it should not be restored.
	/// </summary>
	[Fact]
	public void WhenPreviousIndexIsAlsoUnselectable_DoesNotRestore()
	{
		var native = new FakeNativeSelection(count: 5, initiallySelected: new[] { 0 });

		// Both 0 and 2 are unselectable (both are group headers)
		var kept = new ItemSelectionSynchronizer()
			.RejectUnselectableIndexes(native, new[] { 0 }, new HeaderFooterFilter(0, 2), previousValidIndex: 2);

		Assert.Empty(kept);
		Assert.DoesNotContain("select:2", native.Operations);
	}

	/// <summary>
	/// If the previous valid index is out of range (collection shrunk), it should not be restored.
	/// </summary>
	[Fact]
	public void WhenPreviousIndexIsOutOfRange_DoesNotRestore()
	{
		var native = new FakeNativeSelection(count: 3, initiallySelected: new[] { 0 });

		var kept = new ItemSelectionSynchronizer()
			.RejectUnselectableIndexes(native, new[] { 0 }, new HeaderFooterFilter(0), previousValidIndex: 5);

		Assert.Empty(kept);
		Assert.DoesNotContain("select:5", native.Operations);
	}
}
